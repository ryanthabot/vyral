using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VyralBot.Integrations.Obs.WebSocket;

/// <summary>A snapshot of the remote OBS connection, raised through ObsWebSocketClient.StatusChanged.</summary>
public sealed record ObsRemoteStatus(bool Connected, string? CurrentScene, bool StreamActive, string? Error);

/// <summary>
/// obs-websocket 5 client over ClientWebSocket: connects to ws://localhost:{port},
/// answers the Hello challenge via ObsWebSocketAuth, then multiplexes opcode-6
/// requests against opcode-7 responses and mirrors opcode-5 events into
/// ObsRemoteStatus. With <c>autoReconnect: true</c> any non-user-initiated loss
/// (OBS quit, socket drop, rejected auth, failed first connect) schedules an
/// exponential-backoff retry loop (1s → 2s → 4s → … capped at 30s, unlimited
/// attempts) that re-reads the password through the provider on every attempt,
/// re-runs the Identify handshake (which re-establishes the event stream) and
/// refreshes scene/stream state after each successful reconnect. User-initiated
/// Disconnect/Dispose cancels the loop. The password only ever feeds the auth
/// hash — it is never sent in clear and never appears in exceptions, status
/// messages, or logs. Inbound messages over <see cref="MaxInboundMessageBytes"/>
/// are drained and dropped so a hostile or garbling peer cannot balloon memory.
/// </summary>
public sealed class ObsWebSocketClient : IDisposable
{
    private const int HelloOp = 0, IdentifyOp = 1, IdentifiedOp = 2, EventOp = 5, RequestOp = 6, ResponseOp = 7;

    // obs-websocket closes with this code when the authentication hash does not match.
    private const int InvalidAuthCloseStatus = 4009;
    private const int HandshakeTimeoutMs = 5000;
    private const int RequestTimeoutMs = 5000;

    // Sends hold the send lock — a stalled TCP peer must not wedge it forever.
    private const int SendTimeoutMs = 10000;

    // Reconnect backoff ladder: 1s doubling to a 30s ceiling, unlimited attempts.
    private const int ReconnectFirstDelayMs = 1000;
    private const int ReconnectMaxDelayMs = 30000;

    // Legit obs-websocket messages (even huge scene-item lists) stay far below
    // this; anything bigger is treated as a hostile/garbled peer and dropped.
    private const int MaxInboundMessageBytes = 4 * 1024 * 1024;

    private readonly Func<string> passwordProvider;
    private readonly int port;
    private readonly bool autoReconnect;
    private readonly ILogger? logger;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();

    private ClientWebSocket? socket;
    private TaskCompletionSource<bool> handshake = NewTcs();
    private WebSocketCloseStatus? receivedCloseStatus;
    private volatile bool connected;
    private volatile bool streamActive;
    private volatile bool closingByUser;
    private string? currentScene;
    private int closeReported;
    private int disposed;
    private int reconnectLoopActive;
    private CancellationTokenSource? reconnectCts;

    /// <param name="password">Fixed password (no per-reconnect refresh).</param>
    public ObsWebSocketClient(string password, int port = 4455, bool autoReconnect = false, ILogger? logger = null)
    {
        if (password is null)
        {
            throw new ArgumentNullException(nameof(password));
        }

        this.passwordProvider = () => password;
        this.port = port;
        this.autoReconnect = autoReconnect;
        this.logger = logger;
    }

    /// <param name="passwordProvider">
    /// Invoked on EVERY connect/reconnect attempt so an edited OBS password is
    /// picked up live (the value feeds the auth hash only — never log it).
    /// </param>
    public ObsWebSocketClient(Func<string> passwordProvider, int port = 4455, bool autoReconnect = false, ILogger? logger = null)
    {
        this.passwordProvider = passwordProvider ?? throw new ArgumentNullException(nameof(passwordProvider));
        this.port = port;
        this.autoReconnect = autoReconnect;
        this.logger = logger;
    }

    public bool IsConnected => connected;

    /// <summary>Raised (from the threadpool) whenever the remote status changes.</summary>
    public event Action<ObsRemoteStatus>? StatusChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (connected)
        {
            return;
        }

        try
        {
            await ConnectCoreAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (autoReconnect && ex is not OperationCanceledException && disposed == 0 && !closingByUser)
        {
            // A FIRST connect that never got a socket (OBS still starting, its
            // websocket server not listening yet) produces no HandleServerClose —
            // schedule the retry loop here so "OBS is coming up" self-heals.
            // StartReconnectLoop is a no-op when a loop already runs.
            StartReconnectLoop();
            throw;
        }
    }

    /// <summary>
    /// User-initiated disconnect: closes the socket AND cancels the reconnect
    /// loop (a pending retry after a lost connection must not fire afterwards).
    /// </summary>
    public async Task DisconnectAsync()
    {
        closingByUser = true;
        CancelReconnect();
        connected = false;
        FailPending("The OBS connection was closed.");
        var ws = Interlocked.Exchange(ref socket, null);
        if (ws is null)
        {
            return;
        }

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(RequestTimeoutMs);
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // best-effort close — the receive loop tears down the rest
        }
        finally
        {
            ws.Dispose();
        }
    }

    /// <summary>Current program scene, or null when not connected / on failure (surfaced via StatusChanged).</summary>
    public async Task<string?> GetCurrentProgramSceneAsync()
    {
        if (!connected)
        {
            return null;
        }

        try
        {
            var data = await SendRequestAsync("GetCurrentProgramScene", null).ConfigureAwait(false);
            return data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("currentProgramSceneName", out var sceneName)
                && sceneName.ValueKind == JsonValueKind.String
                    ? sceneName.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            RaiseStatus($"Getting the current OBS scene failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Throws InvalidOperationException carrying the OBS requestStatus comment on failure.</summary>
    public async Task SetCurrentProgramSceneAsync(string sceneName)
    {
        ArgumentNullException.ThrowIfNull(sceneName);
        await SendRequestAsync("SetCurrentProgramScene", new { sceneName }).ConfigureAwait(false);
    }

    /// <summary>Starts streaming. "Already active" is not an error — it is surfaced via StatusChanged.</summary>
    public async Task StartStreamAsync()
    {
        try
        {
            await SendRequestAsync("StartStream", null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            RaiseStatus(ex.Message);
        }
    }

    /// <summary>Stops streaming. "Not active" is not an error — it is surfaced via StatusChanged.</summary>
    public async Task StopStreamAsync()
    {
        try
        {
            await SendRequestAsync("StopStream", null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            RaiseStatus(ex.Message);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        closingByUser = true;
        CancelReconnect();
        connected = false;
        FailPending("The OBS client was disposed.");
        socket?.Dispose();
        socket = null;
        sendLock.Dispose();
    }

    // ---------- connection core (shared by ConnectAsync + the retry loop) ----------

    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        handshake = NewTcs();
        Volatile.Write(ref closeReported, 0);
        receivedCloseStatus = null;
        closingByUser = false;

        // A previous generation's socket (e.g. after a 4009 close) must not
        // linger: its receive loop would fire into the fresh handshake.
        if (Interlocked.Exchange(ref socket, null) is { } stale)
        {
            try
            {
                stale.Dispose();
            }
            catch (Exception)
            {
                // Best-effort — the new connection does not depend on it.
            }
        }

        var ws = new ClientWebSocket();
        try
        {
            // ClientWebSocket has no connect timeout of its own — bound it, or a
            // silently dropped handshake lingers for minutes on OS-level timers.
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectTimeout.CancelAfter(HandshakeTimeoutMs);
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}"), connectTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ws.Dispose();
            throw new InvalidOperationException(
                $"Could not reach OBS on port {port} — is OBS running with remote control enabled?");
        }
        catch (Exception ex) when (ex is WebSocketException or IOException)
        {
            ws.Dispose();
            throw new InvalidOperationException(
                $"Could not reach OBS on port {port} — is OBS running with remote control enabled?", ex);
        }

        if (disposed != 0 || closingByUser)
        {
            // Dispose/disconnect raced the TCP handshake — drop the fresh
            // socket instead of resurrecting a connection the user closed.
            ws.Dispose();
            throw new InvalidOperationException(
                $"The OBS connection on port {port} was closed before the handshake finished.");
        }

        socket = ws;
        _ = Task.Run(() => ReceiveLoopAsync(ws));

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HandshakeTimeoutMs);
            await handshake.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // NOTE: not DisconnectAsync — that would set closingByUser and read
            // as "user asked to stop" for the reconnect loop.
            AbortSocket(ws);
            throw new InvalidOperationException(
                $"OBS did not finish the remote-control handshake on port {port} within {HandshakeTimeoutMs / 1000} s — is the WebSocket server enabled?");
        }

        if (!connected)
        {
            // Disconnect was requested while the handshake was still in flight.
            throw new InvalidOperationException($"The OBS connection on port {port} was closed before the handshake finished.");
        }
    }

    // ---------- auto-reconnect ----------

    /// <summary>
    /// Single-flight starter for the retry loop: started from HandleServerClose
    /// (connection lost) — never when the user disconnected or the client died.
    /// </summary>
    private void StartReconnectLoop()
    {
        if (!autoReconnect || disposed != 0 || closingByUser)
        {
            return;
        }

        if (Interlocked.Exchange(ref reconnectLoopActive, 1) != 0)
        {
            return; // already scheduled/running
        }

        var cts = new CancellationTokenSource();
        reconnectCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await ReconnectLoopAsync(cts.Token).ConfigureAwait(false);
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(reconnectCts, cts))
                {
                    reconnectCts = null;
                }

                Volatile.Write(ref reconnectLoopActive, 0);
            }
        });
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var delayMs = ReconnectFirstDelayMs;
        var attempt = 1;
        while (!ct.IsCancellationRequested && !closingByUser && disposed == 0)
        {
            RaiseStatus($"The OBS connection on port {port} was lost — reconnecting in {delayMs / 1000.0:0.#} s…");
            logger?.LogInformation(
                "obs-websocket connection on port {Port} lost — reconnect attempt {Attempt} in {DelayMs} ms",
                port, attempt, delayMs);

            try
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // user disconnect / dispose
            }

            if (closingByUser || disposed != 0 || connected)
            {
                break;
            }

            try
            {
                // ConnectCore re-reads the password via the provider, so an
                // edited OBS password (or a 4009 on the stale one) resolves here.
                await ConnectCoreAsync(ct).ConfigureAwait(false);
                return; // Identified → events re-subscribed + state refreshed
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break; // client disposed mid-reconnect
            }
            catch (Exception)
            {
                // Still unreachable / rejected — back off and try again.
                delayMs = Math.Min(delayMs * 2, ReconnectMaxDelayMs);
                attempt++;
            }
        }
    }

    private void CancelReconnect()
    {
        try
        {
            reconnectCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Loop already ended — nothing to cancel.
        }
    }

    /// <summary>
    /// After every (re)connect the Identify handshake re-establishes the event
    /// stream, but events missed while offline are gone — re-read scene and
    /// stream state so the status reflects now, not the moment of the drop.
    /// </summary>
    private async Task RefreshStateAfterReconnectAsync()
    {
        try
        {
            if (await GetCurrentProgramSceneAsync().ConfigureAwait(false) is { } scene)
            {
                currentScene = scene;
            }
        }
        catch (Exception)
        {
            // Last known scene stands.
        }

        try
        {
            var data = await SendRequestAsync("GetStreamStatus", null).ConfigureAwait(false);
            if (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("outputActive", out var outputActive)
                && outputActive.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                streamActive = outputActive.GetBoolean();
            }
        }
        catch (Exception)
        {
            // Last known stream state stands.
        }

        RaiseStatus(null);
    }

    // ---------- receive loop ----------

    private async Task ReceiveLoopAsync(ClientWebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(ws, buffer).ConfigureAwait(false);
                if (message is null)
                {
                    HandleServerClose(ws);
                    return;
                }

                await HandleMessageAsync(message).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            HandleServerClose(ws);
        }
    }

    /// <summary>
    /// Accumulates frames until EndOfMessage; null once the peer closed. A
    /// message over <see cref="MaxInboundMessageBytes"/> stops being buffered
    /// but is drained to its end so the next message resyncs, and is returned
    /// as an empty string (which fails JSON parsing and is dropped).
    /// </summary>
    private async Task<string?> ReceiveMessageAsync(ClientWebSocket ws, byte[] buffer)
    {
        using var message = new MemoryStream();
        var oversized = false;
        while (true)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                receivedCloseStatus = result.CloseStatus;
                return null;
            }

            if (!oversized)
            {
                if (message.Length + result.Count > MaxInboundMessageBytes)
                {
                    oversized = true;
                    message.SetLength(0);
                    logger?.LogError(
                        "Dropped an inbound obs-websocket message over the {MaxBytes} byte cap on port {Port} — the remote is misbehaving or corrupted.",
                        MaxInboundMessageBytes, port);
                }
                else
                {
                    message.Write(buffer, 0, result.Count);
                }
            }

            if (result.EndOfMessage)
            {
                return oversized ? string.Empty : Encoding.UTF8.GetString(message.ToArray());
            }
        }
    }

    private async Task HandleMessageAsync(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return; // dropped oversized message
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("op", out var opElement)
                || opElement.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("d", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            switch (opElement.GetInt32())
            {
                case HelloOp:
                    await SendIdentifyAsync(data).ConfigureAwait(false);
                    break;
                case IdentifiedOp:
                    connected = true;
                    handshake.TrySetResult(true);
                    RaiseStatus(null);
                    // Fire-and-forget: re-reads scene/stream state missed while
                    // offline (harmless on a first connect, vital on reconnect).
                    _ = RefreshStateAfterReconnectAsync();
                    break;
                case EventOp:
                    OnEvent(data);
                    break;
                case ResponseOp:
                    OnResponse(data);
                    break;
            }
        }
    }

    private async Task SendIdentifyAsync(JsonElement hello)
    {
        string? salt = null;
        string? challenge = null;
        if (hello.TryGetProperty("authentication", out var auth) && auth.ValueKind == JsonValueKind.Object)
        {
            salt = StringOrNull(auth, "salt");
            challenge = StringOrNull(auth, "challenge");
        }

        // Omitting eventSubscriptions subscribes to ALL events — that IS the
        // re-subscription on every reconnect (the Identify is re-sent each time).
        var identify = new Dictionary<string, object?> { ["rpcVersion"] = 1 };
        if (salt is not null && challenge is not null)
        {
            // Fresh read per attempt: an edited OBS password applies to the
            // next reconnect without recreating this client.
            identify["authentication"] = ObsWebSocketAuth.ComputeAuthentication(passwordProvider(), salt, challenge);
        }

        var ws = socket;
        if (ws is null)
        {
            return;
        }

        try
        {
            await SendAsync(ws, JsonSerializer.Serialize(new { op = IdentifyOp, d = identify })).ConfigureAwait(false);
        }
        catch (Exception)
        {
            handshake.TrySetException(new InvalidOperationException($"Could not send the identification to OBS on port {port}."));
        }
    }

    private void OnEvent(JsonElement data)
    {
        var eventType = StringOrNull(data, "eventType");
        if (eventType is null)
        {
            return;
        }

        if (data.TryGetProperty("eventData", out var eventData) && eventData.ValueKind == JsonValueKind.Object)
        {
            if (eventType == "CurrentProgramSceneChanged"
                && eventData.TryGetProperty("sceneName", out var sceneName)
                && sceneName.ValueKind == JsonValueKind.String)
            {
                currentScene = sceneName.GetString();
            }
            else if (eventType == "StreamStateChanged"
                && eventData.TryGetProperty("outputActive", out var outputActive)
                && outputActive.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                streamActive = outputActive.GetBoolean();
            }
        }

        RaiseStatus(null);
    }

    private void OnResponse(JsonElement data)
    {
        var requestId = StringOrNull(data, "requestId");
        if (requestId is null || !pending.TryRemove(requestId, out var tcs))
        {
            return;
        }

        if (!data.TryGetProperty("requestStatus", out var status) || status.ValueKind != JsonValueKind.Object)
        {
            tcs.TrySetException(new InvalidOperationException("OBS sent a malformed request response."));
            return;
        }

        status.TryGetProperty("result", out var result);
        if (result.ValueKind != JsonValueKind.True)
        {
            var comment = StringOrNull(status, "comment");
            var code = status.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : 0;
            tcs.TrySetException(new InvalidOperationException(comment ?? $"OBS rejected the request (code {code})."));
            return;
        }

        tcs.TrySetResult(
            data.TryGetProperty("responseData", out var responseData) && responseData.ValueKind == JsonValueKind.Object
                ? responseData.Clone()
                : default);
    }

    private void HandleServerClose(ClientWebSocket owner)
    {
        var current = Volatile.Read(ref socket);
        if (current is not null && !ReferenceEquals(current, owner))
        {
            // A stale generation closed after a newer attempt replaced it —
            // only dispose the corpse; it must not fail the fresh handshake
            // nor kick off a spurious reconnect round.
            try
            {
                owner.Dispose();
            }
            catch (Exception)
            {
                // Best-effort.
            }

            return;
        }

        connected = false;
        FailPending("The OBS connection was closed.");
        if (Interlocked.Exchange(ref closeReported, 1) != 0)
        {
            return;
        }

        var invalidAuth = receivedCloseStatus is { } closeStatus && (int)closeStatus == InvalidAuthCloseStatus;
        if (invalidAuth)
        {
            handshake.TrySetException(new InvalidOperationException(
                "OBS rejected the remote-control password (4009). Update it in VyralBot's OBS settings."));
            RaiseStatus("OBS rejected the remote-control password (4009).");
        }
        else if (closingByUser)
        {
            handshake.TrySetResult(false);
            RaiseStatus(null);
        }
        else
        {
            var message = $"The OBS connection on port {port} was closed — is OBS still running?";
            handshake.TrySetException(new InvalidOperationException(message));
            RaiseStatus(message);
        }

        // Non-user loss on a reconnect-enabled client → schedule the backoff
        // loop (StartReconnectLoop is a no-op when already running/disabled).
        if (!closingByUser && disposed == 0)
        {
            StartReconnectLoop();
        }
    }

    // ---------- requests ----------

    private async Task<JsonElement> SendRequestAsync(string requestType, object? requestData)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        var ws = socket;
        if (!connected || ws is null || ws.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Not connected to OBS.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[requestId] = tcs;
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["requestType"] = requestType,
                ["requestId"] = requestId,
            };
            if (requestData is not null)
            {
                payload["requestData"] = requestData;
            }

            await SendAsync(ws, JsonSerializer.Serialize(new { op = RequestOp, d = payload })).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(RequestTimeoutMs);
            return await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"OBS did not answer '{requestType}' within {RequestTimeoutMs / 1000} s.");
        }
        finally
        {
            pending.TryRemove(requestId, out _);
        }
    }

    private async Task SendAsync(ClientWebSocket ws, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // No external token here: any cancellation IS the send timeout.
            using var timeout = new CancellationTokenSource(SendTimeoutMs);
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stalled TCP — abort so the receive loop notices and the
                // reconnect loop (when enabled) takes over.
                AbortSocket(ws);
                throw new TimeoutException(
                    $"Sending to OBS on port {port} stalled for over {SendTimeoutMs / 1000} s — the connection will be dropped.");
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    /// <summary>Disposes a socket generation and unlinks it from the field if it is still the current one.</summary>
    private void AbortSocket(ClientWebSocket ws)
    {
        Interlocked.CompareExchange(ref socket, null, ws);
        try
        {
            ws.Dispose();
        }
        catch (Exception)
        {
            // Best-effort.
        }
    }

    // ---------- helpers ----------

    private void RaiseStatus(string? error) =>
        StatusChanged?.Invoke(new ObsRemoteStatus(connected, currentScene, streamActive, error));

    private void FailPending(string message)
    {
        foreach (var entry in pending)
        {
            if (pending.TryRemove(entry.Key, out var tcs))
            {
                tcs.TrySetException(new InvalidOperationException(message));
            }
        }
    }

    private static TaskCompletionSource<bool> NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string? StringOrNull(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
