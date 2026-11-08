using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Counters;
using VyralBot.Core.Services.Songs;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Data;

namespace VyralBot.UI.Services.VyralDeck;

/// <summary>
/// LAN-only companion server for the VyralDeck mobile app. Own Kestrel host
/// (separate from the webhook web server) listening on all interfaces so a
/// phone on the same network can reach it — it is OFF by default, started from
/// the StreamDeck page, and every WebSocket connection must carry a paired
/// device token (PIN pairing, see <see cref="DeckPairingStore"/>).
///
/// Endpoints:
///   GET  /vyraldeck/info          identity JSON (no auth — lets the phone verify the host)
///   POST /vyraldeck/pair          {pin, deviceName} → {token} (rate-limited)
///   GET  /vyraldeck/ws?token=…    the authenticated session WebSocket
/// Plus a UDP discovery responder on port 5198 ("VYRALDECK_DISCOVER" broadcast).
/// </summary>
public sealed class DeckServer : IDisposable
{
    public const int DefaultPort = 5199;
    public const int DiscoveryPort = 5198;
    private const int ProtocolVersion = 1;

    private static readonly string[] TrackedPlatforms = ["Twitch", "Kick", "YouTube"];

    // Web defaults camel-case dictionary keys too, which would rename the
    // live-state keys "Twitch"/"Kick"/"YouTube" to "twitch"/"kick"/"youTube" —
    // the mobile app looks them up with their PascalCase names (PROTOCOL.md),
    // so dictionary keys must keep their .NET spelling. Class properties still
    // serialize camelCase per the protocol.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DictionaryKeyPolicy = null,
    };

    private readonly ILogger<DeckServer> _logger;
    private readonly DeckPairingStore _pairing;
    private readonly DeckLayoutStore _layout;
    private readonly DeckActionExecutor _executor;
    private readonly IStreamLiveRegistry _liveRegistry;

    // Stream-context sources for the live-state payload. Resolved lazily and
    // optionally: when a service is not registered the matching state key is
    // simply omitted (older/newer clients both tolerate that).
    private readonly IServiceProvider _services;
    private ISongRequestService? _songRequests;
    private IDataService? _dataService;
    private IDeathCounterService? _deathCounter;

    private WebApplication? _app;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private Task? _discoveryTask;
    private UdpClient? _discoveryUdp;
    private CancellationTokenSource? _discoveryCts;
    private Timer? _statePump;
    private int _port;
    private volatile bool _isRunning;

    private readonly ConcurrentDictionary<DeckClientConnection, byte> _clients = new();
    private readonly object _stateLock = new();
    private string _lastStateJson = "";

    public DeckServer(
        ILogger<DeckServer> logger,
        DeckPairingStore pairing,
        DeckLayoutStore layout,
        DeckActionExecutor executor,
        IStreamLiveRegistry liveRegistry,
        IServiceProvider services)
    {
        _logger = logger;
        _pairing = pairing;
        _layout = layout;
        _executor = executor;
        _liveRegistry = liveRegistry;
        _services = services;

        // Layout edits (desktop or phone) rebroadcast to every connected client.
        _layout.LayoutChanged += OnLayoutChanged;
        _liveRegistry.StreamLiveChanged += OnLiveChanged;

        // Unpairing a device must also drop its live WebSocket session.
        _pairing.DeviceRemoved += OnDeviceRemoved;
    }

    public bool IsRunning => _isRunning;
    public int Port => _port;
    public int ClientCount => _clients.Count;

    /// <summary>e.g. "192.168.1.20" — the address phones should connect to.</summary>
    public string LanAddress => GetPrimaryLanAddress();

    public event EventHandler<bool>? IsRunningChanged;
    public event EventHandler<int>? ClientsChanged;
    public event Action<string>? LogLine;

    // ── lifecycle ────────────────────────────────────────────────────────

    public async Task<bool> StartAsync(int port = DefaultPort)
    {
        if (_isRunning)
        {
            return true;
        }

        if (!IsTcpPortFree(port))
        {
            Log($"Port {port} is already in use — pick another in the port box.");
            return false;
        }

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;
                });
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            _app = builder.Build();
            _app.UseWebSockets();

            _app.Map("/vyraldeck/info", async (HttpContext context) =>
            {
                context.Response.ContentType = "application/json";
                var devices = await _pairing.GetDevicesAsync().ConfigureAwait(false);
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    app = "VyralDeck",
                    serverName = Environment.MachineName,
                    protocol = ProtocolVersion,
                    tcpPort = _port,
                    discoveryPort = DiscoveryPort,
                    pairedDevices = devices.Count,
                }, Json), context.RequestAborted).ConfigureAwait(false);
            });

            _app.MapPost("/vyraldeck/pair", async (HttpContext context) =>
            {
                PairRequest? request = null;
                try
                {
                    request = await context.Request.ReadFromJsonAsync<PairRequest>(Json, context.RequestAborted)
                        .ConfigureAwait(false);
                }
                catch (System.Text.Json.JsonException)
                {
                }

                if (request is null)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                var (device, error) = await _pairing.TryPairAsync(request.Pin, request.DeviceName).ConfigureAwait(false);
                context.Response.ContentType = "application/json";
                if (device is null)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = error ?? "Pairing failed.",
                    }, Json), context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                Log($"Paired new device '{device.Name}'.");
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    ok = true,
                    token = device.Token,
                    serverName = Environment.MachineName,
                    protocol = ProtocolVersion,
                }, Json), context.RequestAborted).ConfigureAwait(false);
            });

            _app.Map("/vyraldeck/ws", async (HttpContext context) =>
            {
                var token = context.Request.Query["token"].ToString();
                var device = await _pairing.ValidateTokenAsync(token).ConfigureAwait(false);
                if (device is null || !context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = device is null ? 401 : 400;
                    return;
                }

                // The mobile client offers the "vyraldeck" sub-protocol; echo it
                // back when present (dart:io tolerates its absence, but strict
                // clients — e.g. browsers — refuse a handshake that ignores it).
                var offeredProtocols = context.Request.Headers.SecWebSocketProtocol.ToString();
                var subProtocol = offeredProtocols.Contains("vyraldeck", StringComparison.OrdinalIgnoreCase)
                    ? "vyraldeck"
                    : null;
                var ws = await context.WebSockets.AcceptWebSocketAsync(subProtocol).ConfigureAwait(false);
                await HandleClientAsync(ws, device, context.RequestAborted).ConfigureAwait(false);
            });

            _cts = new CancellationTokenSource();
            _port = port;
            _runTask = Task.Run(async () =>
            {
                try
                {
                    await _app.RunAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "VyralDeck server crashed");
                    Log($"Server error: {ex.Message}");
                }
                finally
                {
                    _isRunning = false;
                }
            }, _cts.Token);

            StartDiscoveryResponder();
            StartStatePump();

            // Give Kestrel a beat to bind before reporting success.
            await Task.Delay(300).ConfigureAwait(false);
            _isRunning = true;
            IsRunningChanged?.Invoke(this, true);
            Log($"Server running on {LanAddress}:{port} (LAN only).");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start VyralDeck server on port {Port}", port);
            Log($"Could not start server: {ex.Message}");
            _isRunning = false;
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning || _cts is null)
        {
            return;
        }

        _isRunning = false;

        StopStatePump();

        foreach (var client in _clients.Keys)
        {
            await client.CloseAsync().ConfigureAwait(false);
        }
        _clients.Clear();
        ClientsChanged?.Invoke(this, 0);

        _cts.Cancel();
        if (_runTask is { } run)
        {
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        StopDiscoveryResponder();

        if (_app is not null)
        {
            try
            {
                await _app.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _runTask = null;
        _app = null;
        IsRunningChanged?.Invoke(this, false);
        Log("Server stopped.");
    }

    // ── client session ───────────────────────────────────────────────────

    private async Task HandleClientAsync(WebSocket socket, PairedDevice device, CancellationToken ct)
    {
        var connection = new DeckClientConnection(socket, device.Token, device.Name);
        _clients[connection] = 0;
        ClientsChanged?.Invoke(this, _clients.Count);
        Log($"'{device.Name}' connected ({_clients.Count} client(s)).");

        try
        {
            var layout = await _layout.LoadAsync().ConfigureAwait(false);
            var state = await LiveStateAsync().ConfigureAwait(false);
            await connection.SendAsync(new
            {
                type = "welcome",
                serverName = Environment.MachineName,
                protocol = ProtocolVersion,
                layout,
                state,
                catalog = CatalogPayload(),
            }, ct).ConfigureAwait(false);

            // The welcome state becomes the dedupe baseline for the pump.
            lock (_stateLock)
            {
                _lastStateJson = JsonSerializer.Serialize(state, Json);
            }

            var buffer = new byte[256 * 1024];
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, buffer, ct).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                await HandleMessageAsync(connection, message, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or System.IO.IOException)
        {
            // normal disconnect paths
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VyralDeck client session error");
        }
        finally
        {
            _clients.TryRemove(connection, out _);
            ClientsChanged?.Invoke(this, _clients.Count);
            Log($"'{device.Name}' disconnected ({_clients.Count} client(s)).");
        }
    }

    private async Task HandleMessageAsync(DeckClientConnection connection, string message, CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            await connection.SendAsync(new { type = "error", message = "Malformed JSON." }, ct).ConfigureAwait(false);
            return;
        }

        using (document)
        {
            var type = document.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "press":
                {
                    var buttonId = document.RootElement.TryGetProperty("buttonId", out var id) ? id.GetString() : null;
                    var button = buttonId is null ? null : _layout.Cached.FindButton(buttonId);
                    if (button is null)
                    {
                        await connection.SendAsync(new
                        {
                            type = "press_result",
                            buttonId,
                            ok = false,
                            message = "Button not found (the deck may have changed — reconnect or refresh).",
                        }, ct).ConfigureAwait(false);
                        return;
                    }

                    var result = await _executor.ExecuteAsync(button).ConfigureAwait(false);
                    await connection.SendAsync(new
                    {
                        type = "press_result",
                        buttonId,
                        ok = result.Ok,
                        message = result.Message,
                    }, ct).ConfigureAwait(false);
                    return;
                }

                case "action":
                {
                    // Ad-hoc tool execution from the mobile Tools tab (no saved button).
                    var actionName = document.RootElement.TryGetProperty("actionType", out var at) ? at.GetString() : null;
                    var param1 = document.RootElement.TryGetProperty("param1", out var p1) ? p1.GetString() ?? "" : "";
                    var param2 = document.RootElement.TryGetProperty("param2", out var p2) ? p2.GetString() ?? "" : "";

                    if (!Enum.TryParse<DeckActionType>(actionName, ignoreCase: true, out var actionType))
                    {
                        await connection.SendAsync(new { type = "press_result", ok = false, message = $"Unknown action '{actionName}'." }, ct)
                            .ConfigureAwait(false);
                        return;
                    }

                    var temp = new DeckButton { Label = actionName, ActionType = actionType, Param1 = param1, Param2 = param2 };
                    var result = await _executor.ExecuteAsync(temp).ConfigureAwait(false);
                    await connection.SendAsync(new
                    {
                        type = "press_result",
                        ok = result.Ok,
                        message = result.Message,
                    }, ct).ConfigureAwait(false);
                    return;
                }

                case "config_set":
                {
                    try
                    {
                        var element = document.RootElement.GetProperty("layout");
                        var layout = element.Deserialize<DeckLayout>(Json);
                        if (layout is null)
                        {
                            throw new JsonException("layout was null");
                        }

                        // Persisting fires LayoutChanged → rebroadcast to every client.
                        await _layout.SaveAsync(layout).ConfigureAwait(false);
                        await connection.SendAsync(new { type = "config_saved", ok = true }, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
                    {
                        await connection.SendAsync(new { type = "error", message = "Could not apply the layout: " + ex.Message }, ct)
                            .ConfigureAwait(false);
                    }

                    return;
                }

                case "ping":
                    await connection.SendAsync(new { type = "pong" }, ct).ConfigureAwait(false);
                    return;

                default:
                    await connection.SendAsync(new { type = "error", message = $"Unknown message type '{type}'." }, ct)
                        .ConfigureAwait(false);
                    return;
            }
        }
    }

    // ── push / broadcast ─────────────────────────────────────────────────

    private void OnDeviceRemoved(string token) => _ = DisconnectDeviceAsync(token);

    /// <summary>Closes every live session belonging to a just-unpaired device.</summary>
    private async Task DisconnectDeviceAsync(string token)
    {
        foreach (var client in _clients.Keys.Where(c => c.Token == token).ToList())
        {
            await client.CloseAsync().ConfigureAwait(false);
            if (_clients.TryRemove(client, out _))
            {
                ClientsChanged?.Invoke(this, _clients.Count);
            }

            Log($"Closed the session of unpaired device '{client.DeviceName}'.");
        }
    }

    /// <summary>True while a paired device has a live WebSocket session.</summary>
    public bool IsDeviceConnected(string token) =>
        _clients.Keys.Any(c => string.Equals(c.Token, token, StringComparison.Ordinal));

    private void OnLayoutChanged(DeckLayout layout)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        _ = BroadcastAsync(new { type = "config", layout });
    }

    private void OnLiveChanged(object? sender, string platform) => _ = PushStateAsync();

    /// <summary>
    /// The live-state payload: the per-platform live flags (PascalCase keys per
    /// PROTOCOL.md) plus optional stream-context strings — `nowPlaying` (song
    /// queue), `uptimeText` (same "Stream.StartedAtUtc" source the !uptime
    /// command reads), `deathCount` (the shared !death counter; unknown keys
    /// are ignorable by older clients). Every context key is omitted when its
    /// backing service/data is unavailable, so clients only ever render what
    /// exists.
    /// </summary>
    private async Task<Dictionary<string, object?>> LiveStateAsync()
    {
        var state = new Dictionary<string, object?>();
        foreach (var platform in TrackedPlatforms)
        {
            state[platform] = _liveRegistry.IsLive(platform);
        }

        var nowPlaying = await TryGetNowPlayingAsync().ConfigureAwait(false);
        if (nowPlaying is not null)
        {
            state["nowPlaying"] = nowPlaying;
        }

        var uptimeText = await TryGetUptimeTextAsync().ConfigureAwait(false);
        if (uptimeText is not null)
        {
            state["uptimeText"] = uptimeText;
        }

        var deathCount = await TryGetDeathCountAsync().ConfigureAwait(false);
        if (deathCount is not null)
        {
            state["deathCount"] = deathCount.Value.ToString(CultureInfo.InvariantCulture);
        }

        return state;
    }

    private async Task<int?> TryGetDeathCountAsync()
    {
        if (DeathCounter is not { } counters)
        {
            return null;
        }

        try
        {
            return await counters.GetCountAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "VyralDeck: could not read the death counter");
            return null;
        }
    }

    private async Task<string?> TryGetNowPlayingAsync()
    {
        if (SongRequests is not { } songs)
        {
            return null;
        }

        try
        {
            var current = await songs.GetCurrentSongRequestAsync().ConfigureAwait(false);
            var title = current?.Title?.Trim();
            return string.IsNullOrEmpty(title) ? null : title;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "VyralDeck: could not read the current song request");
            return null;
        }
    }

    /// <summary>Compact "2h 14m" uptime — same source !uptime reads.</summary>
    private async Task<string?> TryGetUptimeTextAsync()
    {
        if (DataService is not { } data)
        {
            return null;
        }

        try
        {
            var startedAtRaw = await data.GetConfigValueAsync("Stream.StartedAtUtc").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(startedAtRaw))
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(startedAtRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var startedAt)
                || startedAt >= DateTimeOffset.UtcNow)
            {
                return null;
            }

            var uptime = DateTimeOffset.UtcNow - startedAt;
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "VyralDeck: could not compute stream uptime");
            return null;
        }
    }

    /// <summary>Recomputes the live state and rebroadcasts it when it changed.</summary>
    private async Task PushStateAsync()
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var state = await LiveStateAsync().ConfigureAwait(false);
        var json = JsonSerializer.Serialize(state, Json);
        lock (_stateLock)
        {
            if (json == _lastStateJson)
            {
                return;
            }

            _lastStateJson = json;
        }

        await BroadcastAsync(new { type = "state", state }).ConfigureAwait(false);
    }

    // ── state pump ───────────────────────────────────────────────────────

    /// <summary>
    /// Live flags push on StreamLiveChanged, but the context keys change on
    /// their own (next song, minute rollover) — refresh periodically while
    /// the server runs. The dedupe guard keeps idle ticks off the wire.
    /// </summary>
    private void StartStatePump()
    {
        _statePump = new Timer(
            _ => _ = PushStateAsync(),
            null,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20));
    }

    private void StopStatePump()
    {
        _statePump?.Dispose();
        _statePump = null;
        lock (_stateLock)
        {
            _lastStateJson = "";
        }
    }

    private ISongRequestService? SongRequests =>
        _songRequests ??= _services.GetService<ISongRequestService>();

    private IDataService? DataService =>
        _dataService ??= _services.GetService<IDataService>();

    /// <summary>
    /// The death counter resolves lazily and optionally; the first successful
    /// resolution also hooks <c>CountChanged</c> so the mobile chip ticks the
    /// moment a death is counted (the 20s pump is only the fallback).
    /// </summary>
    private IDeathCounterService? DeathCounter
    {
        get
        {
            if (_deathCounter is { } cached)
            {
                return cached;
            }

            var service = _services.GetService<IDeathCounterService>();
            if (service is null)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref _deathCounter, service, null) is { } winner)
            {
                return winner;
            }

            service.CountChanged += OnDeathCountChanged;
            return service;
        }
    }

    private void OnDeathCountChanged(object? sender, int count) => _ = PushStateAsync();

    private static List<object> CatalogPayload() =>
        DeckActionCatalog.Actions
            .Select(a => (object)new
            {
                type = a.Type.ToString(),
                name = a.DisplayName,
                category = a.Category,
                param1Label = a.Param1Label,
                param2Label = a.Param2Label,
                param1Placeholder = a.Param1Placeholder,
                param2Placeholder = a.Param2Placeholder,
            })
            .ToList();

    private async Task BroadcastAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        foreach (var client in _clients.Keys)
        {
            try
            {
                await client.SendRawAsync(json, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    // ── UDP discovery ────────────────────────────────────────────────────

    private void StartDiscoveryResponder()
    {
        try
        {
            var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _discoveryUdp = udp;
            _discoveryCts = new CancellationTokenSource();
            var token = _discoveryCts.Token;

            _discoveryTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
                        var text = Encoding.UTF8.GetString(result.Buffer);
                        if (!text.StartsWith("VYRALDECK_DISCOVER", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var reply = JsonSerializer.Serialize(new
                        {
                            app = "VyralDeck",
                            serverName = Environment.MachineName,
                            tcpPort = _port,
                        }, Json);
                        var bytes = Encoding.UTF8.GetBytes(reply);
                        await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        // transient ICMP/again errors — keep serving
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "VyralDeck discovery responder error");
                    }
                }
            }, token);

            Log($"Discovery responder listening on UDP {DiscoveryPort}.");
        }
        catch (Exception ex)
        {
            // Discovery is a convenience — manual IP entry still works.
            _logger.LogWarning(ex, "Could not start UDP discovery responder on port {Port}", DiscoveryPort);
            Log($"Discovery unavailable (port {DiscoveryPort} busy) — manual IP entry still works.");
        }
    }

    private void StopDiscoveryResponder()
    {
        try
        {
            _discoveryCts?.Cancel();
            _discoveryUdp?.Dispose();
        }
        catch (Exception)
        {
        }

        _discoveryCts = null;
        _discoveryUdp = null;
        _discoveryTask = null;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        var sb = new StringBuilder();
        bool endOfMessage;
        do
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            endOfMessage = result.EndOfMessage;
        }
        while (!endOfMessage);

        return sb.ToString();
    }

    private static bool IsTcpPortFree(int port)
    {
        try
        {
            var probe = new TcpListener(IPAddress.Any, port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string GetPrimaryLanAddress()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString());

            var address = candidates.FirstOrDefault(IsPrivateIpv4) ?? candidates.FirstOrDefault();
            return address ?? "127.0.0.1";
        }
        catch (Exception)
        {
            return "127.0.0.1";
        }
    }

    private static bool IsPrivateIpv4(string address)
    {
        if (IPAddress.TryParse(address, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            return bytes.Length == 4
                && (bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168));
        }

        return false;
    }

    private void Log(string message)
    {
        _logger.LogInformation("VyralDeck: {Message}", message);
        LogLine?.Invoke(message);
    }

    public void Dispose()
    {
        _layout.LayoutChanged -= OnLayoutChanged;
        _liveRegistry.StreamLiveChanged -= OnLiveChanged;
        _pairing.DeviceRemoved -= OnDeviceRemoved;
        if (_deathCounter is { } deaths)
        {
            deaths.CountChanged -= OnDeathCountChanged;
        }

        if (_isRunning)
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }

    private sealed record PairRequest(string? Pin, string? DeviceName);

    /// <summary>One connected phone; serializes concurrent sends onto its socket.</summary>
    private sealed class DeckClientConnection
    {
        private readonly WebSocket _socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public DeckClientConnection(WebSocket socket, string token, string deviceName)
        {
            _socket = socket;
            Token = token;
            DeviceName = deviceName;
        }

        public string Token { get; }

        public string DeviceName { get; }

        public async Task SendAsync(object payload, CancellationToken ct) =>
            await SendRawAsync(JsonSerializer.Serialize(payload, Json), ct).ConfigureAwait(false);

        public async Task SendRawAsync(string json, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_socket.State != WebSocketState.Open)
                {
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                await _socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server stopping",
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
