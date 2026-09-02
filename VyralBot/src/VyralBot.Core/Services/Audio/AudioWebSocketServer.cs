using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.Audio;

/// <summary>
/// WebSocket server for sending audio events to browser sources.
/// Also serves sound files via HTTP on the same port.
/// </summary>
public class AudioWebSocketServer : IAudioWebSocketServer, IDisposable
{
    private readonly ILogger<AudioWebSocketServer> _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private readonly ConcurrentDictionary<Guid, WebSocket> _connectedClients = new();
    private int _port;
    private string _soundsDirectory = "";

    public bool IsRunning => _listener?.IsListening ?? false;
    public int ConnectedClients => _connectedClients.Count;

    public AudioWebSocketServer(ILogger<AudioWebSocketServer> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(int port = 8080)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Audio WebSocket server is already running on port {Port}", _port);
            return Task.CompletedTask;
        }

        _port = port;
        _soundsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
        
        // Ensure Sounds directory exists
        Directory.CreateDirectory(_soundsDirectory);

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);

            _logger.LogInformation("🎵 Audio WebSocket server started on port {Port}", port);
            _logger.LogInformation("📁 Serving sounds from: {SoundsDirectory}", _soundsDirectory);
            _logger.LogInformation("🌐 Browser source URL: http://localhost:{Port}/audio.html", port);
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Audio WebSocket server on port {Port}", port);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        _logger.LogInformation("Stopping Audio WebSocket server...");

        _cts?.Cancel();

        // Close all client connections
        foreach (var client in _connectedClients.Values)
        {
            try
            {
                if (client.State == WebSocketState.Open)
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                }
                client.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing client WebSocket");
            }
        }

        _connectedClients.Clear();

        _listener?.Stop();
        _listener?.Close();

        if (_serverTask != null)
        {
            await _serverTask;
        }

        _logger.LogInformation("Audio WebSocket server stopped");
    }

    public async Task SendAudioEventAsync(AudioEvent audioEvent)
    {
        if (_connectedClients.IsEmpty)
        {
            _logger.LogDebug("No browser sources connected - audio event not sent");
            return;
        }

        var json = JsonSerializer.Serialize(audioEvent);
        var buffer = Encoding.UTF8.GetBytes(json);

        var disconnectedClients = new List<Guid>();

        foreach (var kvp in _connectedClients)
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.SendAsync(
                        new ArraySegment<byte>(buffer),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None
                    );
                }
                else
                {
                    disconnectedClients.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sending audio event to client {ClientId}", kvp.Key);
                disconnectedClients.Add(kvp.Key);
            }
        }

        // Clean up disconnected clients
        foreach (var clientId in disconnectedClients)
        {
            _connectedClients.TryRemove(clientId, out var client);
            client?.Dispose();
            _logger.LogInformation("Removed disconnected browser source client {ClientId}", clientId);
        }

        _logger.LogDebug("📢 Sent audio event to {Count} browser source(s): {EventType} - {SoundFile}", 
            _connectedClients.Count, audioEvent.EventType, audioEvent.SoundFile);
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);

                // Handle WebSocket upgrade requests
                if (context.Request.IsWebSocketRequest)
                {
                    _ = Task.Run(() => HandleWebSocketAsync(context), cancellationToken);
                }
                // Handle HTTP requests for sound files and HTML
                else
                {
                    _ = Task.Run(() => HandleHttpRequestAsync(context), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context)
    {
        WebSocket? webSocket = null;
        var clientId = Guid.NewGuid();

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            webSocket = wsContext.WebSocket;

            _connectedClients[clientId] = webSocket;
            _logger.LogInformation("🌐 Browser source connected - Client ID: {ClientId} (Total: {Count})", 
                clientId, _connectedClients.Count);

            // Keep connection alive and listen for messages (browser might send ping/pong)
            var buffer = new byte[1024];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSocket connection error for client {ClientId}", clientId);
        }
        finally
        {
            if (_connectedClients.TryRemove(clientId, out _))
            {
                _logger.LogInformation("🔌 Browser source disconnected - Client ID: {ClientId} (Remaining: {Count})", 
                    clientId, _connectedClients.Count);
            }

            webSocket?.Dispose();
        }
    }

    private async Task HandleHttpRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.LocalPath ?? "";

            // Serve audio.html
            if (path == "/" || path == "/audio.html")
            {
                await ServeAudioHtmlAsync(context);
            }
            // Serve sound files
            else if (path.StartsWith("/sounds/"))
            {
                await ServeSoundFileAsync(context, path);
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling HTTP request: {Path}", context.Request.Url?.LocalPath);
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    private async Task ServeAudioHtmlAsync(HttpListenerContext context)
    {
        var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserSource", "audio.html");

        if (!File.Exists(htmlPath))
        {
            _logger.LogWarning("audio.html not found at {Path}", htmlPath);
            context.Response.StatusCode = 404;
            var errorBytes = Encoding.UTF8.GetBytes("audio.html not found");
            await context.Response.OutputStream.WriteAsync(errorBytes);
            context.Response.Close();
            return;
        }

        context.Response.ContentType = "text/html";
        var htmlBytes = await File.ReadAllBytesAsync(htmlPath);
        await context.Response.OutputStream.WriteAsync(htmlBytes);
        context.Response.Close();
        
        _logger.LogDebug("Served audio.html to {RemoteEndPoint}", context.Request.RemoteEndPoint);
    }

    private async Task ServeSoundFileAsync(HttpListenerContext context, string path)
    {
        var soundFileName = path.Replace("/sounds/", "").Trim('/');
        var soundPath = Path.Combine(_soundsDirectory, soundFileName);

        if (!File.Exists(soundPath))
        {
            _logger.LogWarning("Sound file not found: {Path}", soundPath);
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        // Determine content type based on file extension
        var extension = Path.GetExtension(soundFileName).ToLowerInvariant();
        context.Response.ContentType = extension switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            _ => "application/octet-stream"
        };

        var soundBytes = await File.ReadAllBytesAsync(soundPath);
        await context.Response.OutputStream.WriteAsync(soundBytes);
        context.Response.Close();

        _logger.LogDebug("Served sound file: {FileName} ({Size} bytes)", soundFileName, soundBytes.Length);
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _cts?.Dispose();
        _listener?.Close();
    }
}
