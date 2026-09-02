using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace VyralBot.Api.Services;

public class WebServerService : IDisposable
{
    private readonly ILogger<WebServerService> _logger;
    private readonly IServiceProvider _parentServices; // Shared services from WPF app
    private readonly VyralBot.Core.Services.Performance.IMetricsRegistry? _metrics;
    private WebApplication? _app;
    private IHostApplicationLifetime? _appLifetime;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _currentPort;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public int Port => _currentPort;
    public string? LocalUrl => _isRunning ? $"http://localhost:{_currentPort}" : null;

    public WebServerService(ILogger<WebServerService> logger, IServiceProvider parentServices,
        VyralBot.Core.Services.Performance.IMetricsRegistry? metrics = null)
    {
        _logger = logger;
        _parentServices = parentServices;
        _metrics = metrics;
    }

    public async Task<bool> StartAsync(int port = 5000)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Web server is already running on port {Port}", _currentPort);
            return true;
        }

        // Check if port is available
        if (!IsPortAvailable(port))
        {
            _logger.LogError("Port {Port} is already in use. Cannot start web server.", port);
            return false;
        }

        try
        {
            _logger.LogInformation("Starting embedded web server on port {Port}...", port);

            var builder = WebApplication.CreateBuilder();

            // Configure Kestrel
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenLocalhost(port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                });
            });

            // Add services from parent DI container (includes EventBus, KickService, etc.)
            builder.Services.AddSingleton(_parentServices.GetRequiredService<VyralBot.Core.Services.EventBus.IEventBus>());
            builder.Services.AddSingleton(_parentServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());

            // Add ASP.NET Core services
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(VyralBot.Api.Controllers.WebhooksController).Assembly);
            builder.Services.AddEndpointsApiExplorer();

            // Add middleware
            builder.Services.AddSingleton<VyralBot.Api.Middleware.KickWebhookValidationMiddleware>();

            builder.Logging.SetMinimumLevel(LogLevel.Information);

            // Add static files and WebSockets

            _app = builder.Build();

            // Store lifetime for clean shutdown
            _appLifetime = _app.Services.GetService<IHostApplicationLifetime>();

            // Configure middleware
            _app.Use(async (context, next) =>
            {
                _metrics?.Increment("WebServer", "requests");
                try
                {
                    await next();
                    if (context.Response.StatusCode >= 400)
                    {
                        _metrics?.Increment("WebServer", "errors");
                    }
                }
                catch (Exception)
                {
                    _metrics?.Increment("WebServer", "errors");
                    throw;
                }
            });
            _app.UseWebSockets();
            _app.UseStaticFiles(); // Serve wwwroot (audio.html, sounds)
            _app.UseRouting();
            _app.UseMiddleware<VyralBot.Api.Middleware.KickWebhookValidationMiddleware>();
            _app.MapControllers();

            // Minimal API for /audio-ws WebSocket endpoint
            var audioClients = new System.Collections.Concurrent.ConcurrentDictionary<System.Net.WebSockets.WebSocket, bool>();
            _app.Map("/audio-ws", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var ws = await context.WebSockets.AcceptWebSocketAsync();
                    audioClients[ws] = true;
                    VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.ClientConnected();
                    _logger.LogInformation("Audio WebSocket client connected. Total: {Count}", audioClients.Count);
                    var buffer = new byte[1024 * 4];
                    try
                    {
                        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                        {
                            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                                break;
                            // No incoming messages expected from client
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _logger.LogWarning(ex, "Audio WebSocket error"); }
                    finally
                    {
                        audioClients.TryRemove(ws, out _);
                        VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.ClientDisconnected();
                        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Closing", context.RequestAborted);
                        _logger.LogInformation("Audio WebSocket client disconnected. Total: {Count}", audioClients.Count);
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            });

            // Expose a static method to broadcast to all audio clients
            VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.BroadcastAsync = async (string message) =>
            {
                var tasks = audioClients.Keys
                    .Where(ws => ws.State == System.Net.WebSockets.WebSocketState.Open)
                    .Select(ws => ws.SendAsync(
                        new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(message)),
                        System.Net.WebSockets.WebSocketMessageType.Text,
                        true,
                        System.Threading.CancellationToken.None));
                await Task.WhenAll(tasks);
            };

            _cts = new CancellationTokenSource();
            _currentPort = port;

            // Start the server in background
            _runTask = Task.Run(async () =>
            {
                try
                {
                    await _app.RunAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Web server stopped gracefully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Web server encountered an error");
                }
                finally
                {
                    _isRunning = false;
                }
            }, _cts.Token);

            // Wait a bit to ensure server is up
            await Task.Delay(500);

            _isRunning = true;
            _logger.LogInformation("Web server started successfully on http://localhost:{Port}", port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start web server on port {Port}", port);
            _isRunning = false;
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning || _cts == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Stopping web server...");
            _isRunning = false;

            // Cancel the token — RunAsync internally calls Host.StopAsync when its
            // token fires, so we must NOT call _app.StopAsync() ourselves or it
            // deadlocks (Host.StopAsync is not re-entrant).
            _cts.Cancel();

            // Wait for RunAsync to finish its internal shutdown
            if (_runTask != null)
            {
                try { await _runTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Web server RunAsync did not complete within timeout");
                }
                catch { }
            }

            // Dispose releases Kestrel listeners. Safe to call after RunAsync completes.
            if (_app != null)
            {
                try { await _app.DisposeAsync(); }
                catch { }
            }

            _logger.LogInformation("Web server stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping web server");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
            _app = null;
            _appLifetime = null;

            // The audio client set died with the app instance; reset the public counter
            VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.ResetClientCount();
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_isRunning)
        {
            StopAsync().GetAwaiter().GetResult();
        }
        GC.SuppressFinalize(this);
    }
}
