using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VyralBot.Integrations.Obs.WebSocket;
using Xunit;

namespace VyralBot.UI.Tests;

public class ObsWebSocketClientTests
{
    // ---------- ObsWebSocketAuth ----------

    [Fact]
    public void ComputeAuthentication_MatchesIndependentImplementation()
    {
        // Independent re-implementation of the obs-websocket 5 chain:
        // base64(SHA256(base64(SHA256(password + salt)) + challenge)), UTF-8 throughout.
        static string Expected(string password, string salt, string challenge)
        {
            var inner = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inner + challenge)));
        }

        var actual = ObsWebSocketAuth.ComputeAuthentication("s3cret", "c2FsdA==", "Y2hhbGw=");

        Assert.Equal(Expected("s3cret", "c2FsdA==", "Y2hhbGw="), actual);
        Assert.Equal(32, Convert.FromBase64String(actual).Length);
    }

    // ---------- ObsWebSocketConfigReader ----------

    [Fact]
    public void ConfigReader_ReadsDocumentedKeys()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(Path.Combine(dir.FullPath, "config.json"),
            """{"server_enabled": true, "server_port": 4456, "server_password": "stream-secret", "alert_unknown_key": 1}""");

        var config = ObsWebSocketConfigReader.Read(dir.FullPath);

        Assert.NotNull(config);
        Assert.Equal("stream-secret", config.Password);
        Assert.Equal(4456, config.Port);
        Assert.True(config.ServerEnabled);
    }

    [Fact]
    public void ConfigReader_NoPassword_StillParsesWithDefaults()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(Path.Combine(dir.FullPath, "config.json"), """{"server_enabled": false}""");

        var config = ObsWebSocketConfigReader.Read(dir.FullPath);

        Assert.NotNull(config);
        Assert.Null(config.Password);
        Assert.Equal(4455, config.Port);
        Assert.False(config.ServerEnabled);
    }

    [Fact]
    public void ConfigReader_MissingFile_ReturnsNull()
    {
        Assert.Null(ObsWebSocketConfigReader.Read(
            Path.Combine(Path.GetTempPath(), "vyralbot-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ConfigReader_CorruptJson_ReturnsNull()
    {
        using var dir = new TempConfigDir();
        File.WriteAllText(Path.Combine(dir.FullPath, "config.json"), """{ "server_password": "x", """);

        Assert.Null(ObsWebSocketConfigReader.Read(dir.FullPath));
    }

    // ---------- ObsWebSocketClient (no network) ----------

    [Fact]
    public async Task Client_NotConnected_GetCurrentReturnsNull()
    {
        using var client = new ObsWebSocketClient("whatever");

        var scene = await client.GetCurrentProgramSceneAsync();

        Assert.False(client.IsConnected);
        Assert.Null(scene);
    }

    // ---------- ObsWebSocketClient (loopback obs-websocket stand-in) ----------

    [Fact]
    public async Task Client_ConnectRefused_ThrowsFriendly()
    {
        var deadPort = FreePort();
        using var client = new ObsWebSocketClient("pw", deadPort);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        Assert.Contains("Could not reach OBS", ex.Message, StringComparison.Ordinal);
        Assert.Contains(deadPort.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_WrongPassword_ThrowsFriendly4009()
    {
        using var server = new FakeObsServer(rejectIdentify: true);
        await server.StartAsync();
        using var client = new ObsWebSocketClient("the-wrong-password", server.Port);
        var statuses = new List<ObsRemoteStatus>();
        client.StatusChanged += s => { lock (statuses) statuses.Add(s); };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());

        Assert.Contains("4009", ex.Message, StringComparison.Ordinal);
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The password must never leak into an exception message.
        Assert.DoesNotContain("the-wrong-password", ex.Message, StringComparison.Ordinal);
        Assert.False(client.IsConnected);
        Assert.True(WaitUntil(() =>
        {
            lock (statuses) return statuses.Any(s => !s.Connected && s.Error is not null && s.Error.Contains("4009", StringComparison.Ordinal));
        }));
    }

    [Fact]
    public async Task Client_Loopback_Connects_AnswersRequests_RaisesEvents()
    {
        using var server = new FakeObsServer();
        await server.StartAsync();
        using var client = new ObsWebSocketClient("right-password", server.Port);
        var statuses = new List<ObsRemoteStatus>();
        client.StatusChanged += s => { lock (statuses) statuses.Add(s); };

        await client.ConnectAsync();

        Assert.True(client.IsConnected);
        var scene = await client.GetCurrentProgramSceneAsync();
        if (scene is null)
        {
            lock (statuses)
                Assert.Fail("GetCurrentProgramSceneAsync returned null. Statuses: "
                            + string.Join(" | ", statuses.Select(s => $"{s.Connected}/{s.Error}")));
        }
        Assert.Equal("Game", scene);

        // Identify carried the expected authentication hash and rpcVersion.
        var expectedAuth = ObsWebSocketAuth.ComputeAuthentication("right-password", FakeObsServer.Salt, FakeObsServer.Challenge);
        var identify = Assert.Single(server.Snapshot(), m => m.StartsWith("""{"op":1,""", StringComparison.Ordinal));
        using (var identifyDoc = JsonDocument.Parse(identify))
        {
            Assert.Equal(1, identifyDoc.RootElement.GetProperty("d").GetProperty("rpcVersion").GetInt32());
            Assert.Equal(expectedAuth, identifyDoc.RootElement.GetProperty("d").GetProperty("authentication").GetString());
        }

        server.SendEvent("""{"op":5,"d":{"eventType":"CurrentProgramSceneChanged","eventData":{"sceneName":"Starting Soon"}}}""");
        server.SendEvent("""{"op":5,"d":{"eventType":"StreamStateChanged","eventData":{"outputActive":true,"outputState":"OBS_WEBSOCKET_OUTPUT_STARTED"}}}""");

        Assert.True(WaitUntil(() =>
        {
            lock (statuses) return statuses.Any(s => s.Connected && s.CurrentScene == "Starting Soon" && s.StreamActive);
        }));
    }

    [Fact]
    public async Task Client_Loopback_FailingRequest_ThrowsComment()
    {
        using var server = new FakeObsServer();
        server.Responder = (requestType, requestId) => requestType == "SetCurrentProgramScene"
            ? FakeObsServer.Failure(requestId, 500, "Scene 'Nope' not found")
            : FakeObsServer.Success(requestId, "{}");
        await server.StartAsync();
        using var client = new ObsWebSocketClient("right-password", server.Port);
        await client.ConnectAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SetCurrentProgramSceneAsync("Nope"));

        Assert.Equal("Scene 'Nope' not found", ex.Message);
    }

    [Fact]
    public async Task Client_Loopback_StartStreamWhileActive_NoThrowSurfacesComment()
    {
        using var server = new FakeObsServer();
        server.Responder = (requestType, requestId) => requestType == "StartStream"
            ? FakeObsServer.Failure(requestId, 500, "The stream is already active")
            : FakeObsServer.Success(requestId, "{}");
        await server.StartAsync();
        using var client = new ObsWebSocketClient("right-password", server.Port);
        var statuses = new List<ObsRemoteStatus>();
        client.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        await client.ConnectAsync();

        await client.StartStreamAsync();

        Assert.True(WaitUntil(() =>
        {
            lock (statuses) return statuses.Any(s => s.Error is not null && s.Error.Contains("already active", StringComparison.OrdinalIgnoreCase));
        }));
    }

    // ---------- ObsWebSocketClient: auto-reconnect / frame cap ----------

    [Fact]
    public async Task Client_AutoReconnect_RetriesUntilFreshPasswordAccepted()
    {
        using var server = new ResilientFakeObsServer("fresh-password", FreePort());
        server.Start();

        // The "stored token": stale at first, edited (in OBS settings) right
        // after the first rejection — the retry must read it fresh per attempt.
        var storedPassword = "stale-password";
        using var client = new ObsWebSocketClient(() => storedPassword, server.Port, autoReconnect: true);

        // First connect is rejected with 4009 (stale password).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
        Assert.Contains("4009", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("stale-password", ex.Message, StringComparison.Ordinal);

        storedPassword = "fresh-password"; // user fixed the password in OBS

        Assert.True(WaitUntil(() => client.IsConnected, 15000),
            $"client did not reconnect with the fresh password (connections seen: {server.ConnectionCount})");
        Assert.True(server.ConnectionCount >= 2);
    }

    [Fact]
    public async Task Client_AutoReconnect_UserDisconnect_CancelsRetryLoop()
    {
        using var server = new ResilientFakeObsServer("right-password", FreePort());
        server.Start();
        using var client = new ObsWebSocketClient("right-password", server.Port, autoReconnect: true);
        await client.ConnectAsync();
        Assert.True(client.IsConnected);

        server.KillCurrentConnection(); // OBS "crashes" — schedules a retry
        Assert.True(WaitUntil(() => !client.IsConnected));

        await client.DisconnectAsync(); // user-initiated — must cancel the retry
        var count = server.ConnectionCount;

        await Task.Delay(2600); // an uncancelled loop would have retried by now

        Assert.Equal(count, server.ConnectionCount);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Client_OversizedInboundMessage_Dropped_ConnectionSurvives()
    {
        using var server = new ResilientFakeObsServer("right-password", FreePort());
        server.Start();
        using var client = new ObsWebSocketClient("right-password", server.Port);
        var statuses = new List<ObsRemoteStatus>();
        client.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        await client.ConnectAsync();

        // One 5 MiB single message (over the 4 MiB inbound cap) followed by a
        // legitimate event — the huge frame must be drained+dropped and the
        // connection must stay usable, not OOM or desync.
        await server.SendHugeMessageAsync(5 * 1024 * 1024);
        server.SendEvent("""{"op":5,"d":{"eventType":"CurrentProgramSceneChanged","eventData":{"sceneName":"Starting Soon"}}}""");

        Assert.True(WaitUntil(() =>
        {
            lock (statuses) return statuses.Any(s => s.Connected && s.CurrentScene == "Starting Soon");
        }, 10000));
        Assert.True(client.IsConnected);
    }

    // ---------- helpers ----------

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private sealed class TempConfigDir : IDisposable
    {
        public string FullPath { get; } = Directory.CreateTempSubdirectory("vyralbot-obs-ws-").FullName;

        public void Dispose() => Directory.Delete(FullPath, recursive: true);
    }

    /// <summary>
    /// Minimal obs-websocket 5 lookalike over HttpListener: Hello with fixed
    /// salt/challenge, Identified (or a 4009 close), and pluggable request answers.
    /// </summary>
    private sealed class FakeObsServer : IDisposable
    {
        public const string Salt = "l9ad0nRh4XGP0pzMg7FarFBoAL9fJ6BiEJuuJUX3sSk=";
        public const string Challenge = "+IxJeJTabZvYTVp6HmLNfqQ0uYqKT3V4y5ZBcUjq1Mw=";

        private readonly SemaphoreSlim sendLock = new(1, 1);
        private readonly HttpListener listener = new();
        private WebSocket? socket;

        public FakeObsServer(bool rejectIdentify = false)
        {
            RejectIdentify = rejectIdentify;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var candidate = FreePort();
                try
                {
                    listener.Prefixes.Add($"http://localhost:{candidate}/");
                    listener.Start();
                    Port = candidate;
                    return;
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                    listener = new HttpListener();
                }
            }
            throw new InvalidOperationException("Could not start the loopback OBS test listener.");
        }

        public bool RejectIdentify { get; }

        public int Port { get; }

        private readonly List<string> received = [];

        public List<string> Snapshot()
        {
            lock (received) return [.. received];
        }

        public Func<string, string, string?> Responder { get; set; } = static (requestType, requestId) =>
            requestType == "GetCurrentProgramScene"
                ? Success(requestId, """{"currentProgramSceneName":"Game"}""")
                : Success(requestId, "{}");

        // Built by concatenation — the JSON tail's }} runs fight raw-string interpolation.
        public static string Success(string requestId, string responseDataJson) =>
            "{\"op\":7,\"d\":{\"requestType\":\"?\",\"requestId\":\"" + requestId
            + "\",\"requestStatus\":{\"result\":true,\"code\":100},\"responseData\":" + responseDataJson + "}}";

        public static string Failure(string requestId, int code, string comment) =>
            "{\"op\":7,\"d\":{\"requestType\":\"?\",\"requestId\":\"" + requestId
            + "\",\"requestStatus\":{\"result\":false,\"code\":" + code + ",\"comment\":\"" + comment + "\"}}}";

        public Task StartAsync()
        {
            _ = ServeAsync(); // serve loop runs until the client disconnects
            return Task.CompletedTask;
        }

        public void SendEvent(string json) => _ = SendAsync(json);

        private async Task ServeAsync()
        {
            try
            {
                var http = await listener.GetContextAsync();
                socket = (await http.AcceptWebSocketAsync(null)).WebSocket;

                // obs-websocket always opens with Hello carrying the auth challenge.
                await SendAsync("{\"op\":0,\"d\":{\"obsWebSocketVersion\":\"5.4.2-test\",\"rpcVersion\":1,\"authentication\":{\"challenge\":\""
                    + Challenge + "\",\"salt\":\"" + Salt + "\"}}}");

                while (socket.State == WebSocketState.Open)
                {
                    var message = await ReceiveAsync(socket);
                    if (message is null) break;
                    lock (received) received.Add(message);

                    using var doc = JsonDocument.Parse(message);
                    var op = doc.RootElement.GetProperty("op").GetInt32();
                    if (op == 1)
                    {
                        if (RejectIdentify)
                        {
                            await socket.CloseOutputAsync((WebSocketCloseStatus)4009, "Invalid authentication", CancellationToken.None);
                            break;
                        }
                        await SendAsync("""{"op":2,"d":{"negotiatedRpcVersion":1}}""");
                    }
                    else if (op == 6)
                    {
                        var d = doc.RootElement.GetProperty("d");
                        var response = Responder(d.GetProperty("requestType").GetString()!, d.GetProperty("requestId").GetString()!);
                        if (response is not null) await SendAsync(response);
                    }
                }
            }
            catch
            {
                // test double — shutdown races are fine
            }
        }

        private async Task SendAsync(string json)
        {
            var ws = socket;
            if (ws is null) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
                // test double — client already gone
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static async Task<string?> ReceiveAsync(WebSocket ws)
        {
            var buffer = new byte[16 * 1024];
            using var message = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
            }
        }

        public void Dispose()
        {
            try { socket?.Abort(); } catch { /* already gone */ }
            try { listener.Close(); } catch { /* already gone */ }
        }
    }

    /// <summary>
    /// Multi-connection obs-websocket stand-in for the reconnect/frame-cap
    /// tests: accepts connections one after another, authenticates against a
    /// mutable expected password (4009 close otherwise) and answers the
    /// requests the client issues after (re)connecting.
    /// </summary>
    private sealed class ResilientFakeObsServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private WebSocket? socket;
        private int connectionCount;

        public ResilientFakeObsServer(string expectedPassword, int port)
        {
            ExpectedPassword = expectedPassword;
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            Port = port;
        }

        public string ExpectedPassword { get; set; }

        public int Port { get; }

        public int ConnectionCount => Volatile.Read(ref connectionCount);

        public void Start() => _ = Task.Run(AcceptLoopAsync);

        public void KillCurrentConnection()
        {
            try { socket?.Abort(); } catch { /* already gone */ }
        }

        public void SendEvent(string json) => _ = SendAsync(json);

        /// <summary>One huge invalid-JSON message of <paramref name="totalBytes"/>, split into 64 KiB frames.</summary>
        public async Task SendHugeMessageAsync(int totalBytes)
        {
            var ws = socket;
            if (ws is null || ws.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("No connection to flood.");
            }

            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'x');
            chunk[0] = (byte)'{';
            await sendLock.WaitAsync();
            try
            {
                var sent = 0;
                while (sent < totalBytes)
                {
                    var n = Math.Min(chunk.Length, totalBytes - sent);
                    var last = sent + n >= totalBytes;
                    await ws.SendAsync(new ArraySegment<byte>(chunk, 0, n), WebSocketMessageType.Text, last, CancellationToken.None);
                    sent += n;
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                HttpListenerContext http;
                try
                {
                    http = await listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return; // listener disposed — shut down
                }

                try
                {
                    var ws = (await http.AcceptWebSocketAsync(null)).WebSocket;
                    Interlocked.Increment(ref connectionCount);
                    await ServeConnectionAsync(ws);
                }
                catch (Exception)
                {
                    // connection-level failure — accept the next one
                }
            }
        }

        private async Task ServeConnectionAsync(WebSocket ws)
        {
            socket = ws;
            var hello = "{\"op\":0,\"d\":{\"obsWebSocketVersion\":\"5.4.2-test\",\"rpcVersion\":1,\"authentication\":{\"challenge\":\""
                + FakeObsServer.Challenge + "\",\"salt\":\"" + FakeObsServer.Salt + "\"}}}";
            await SendAsync(hello);

            var buffer = new byte[16 * 1024];
            while (ws.State == WebSocketState.Open)
            {
                string? message;
                try
                {
                    message = await ReceiveAsync(ws, buffer);
                }
                catch (Exception)
                {
                    return; // aborted mid-read
                }

                if (message is null)
                {
                    return;
                }

                using var doc = JsonDocument.Parse(message);
                var op = doc.RootElement.GetProperty("op").GetInt32();
                if (op == 1)
                {
                    var auth = doc.RootElement.GetProperty("d").GetProperty("authentication").GetString();
                    var expected = ObsWebSocketAuth.ComputeAuthentication(ExpectedPassword, FakeObsServer.Salt, FakeObsServer.Challenge);
                    if (!string.Equals(auth, expected, StringComparison.Ordinal))
                    {
                        await ws.CloseOutputAsync((WebSocketCloseStatus)4009, "Invalid authentication", CancellationToken.None);
                        return;
                    }

                    await SendAsync("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}");
                }
                else if (op == 6)
                {
                    var d = doc.RootElement.GetProperty("d");
                    var requestType = d.GetProperty("requestType").GetString()!;
                    var requestId = d.GetProperty("requestId").GetString()!;
                    var responseData = requestType switch
                    {
                        "GetCurrentProgramScene" => "{\"currentProgramSceneName\":\"Game\"}",
                        "GetStreamStatus" => "{\"outputActive\":true}",
                        _ => "{}",
                    };
                    await SendAsync(FakeObsServer.Success(requestId, responseData));
                }
            }
        }

        private async Task SendAsync(string json)
        {
            var ws = socket;
            if (ws is null || ws.State != WebSocketState.Open)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync();
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // client already gone
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static async Task<string?> ReceiveAsync(WebSocket ws, byte[] buffer)
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
            }
        }

        public void Dispose()
        {
            try { socket?.Abort(); } catch { /* already gone */ }
            try { listener.Close(); } catch { /* already gone */ }
        }
    }
}
