using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Lightweight Twitch IRC client using WebSocket (wss://irc-ws.chat.twitch.tv).
/// Replaces TwitchLib to reduce memory footprint by ~30MB.
/// </summary>
public sealed class LightweightTwitchIrc : IDisposable
{
    private readonly ILogger<LightweightTwitchIrc> _logger;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private Task? _keepAliveTask;
    private DateTime _lastMessageTime = DateTime.UtcNow;

    public string Username { get; private set; } = "";
    public string Channel { get; private set; } = "";
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public event EventHandler<TwitchMessageEventArgs>? MessageReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler? AuthenticationFailed;

    public LightweightTwitchIrc(ILogger<LightweightTwitchIrc> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ConnectAsync(string username, string oauthToken, string channel)
    {
        if (IsConnected)
        {
            _logger.LogWarning("Already connected to Twitch IRC");
            return;
        }

        Username = username;
        Channel = channel.TrimStart('#').ToLower();

        try
        {
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            // Connect to Twitch IRC WebSocket
            await _webSocket.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), _cts.Token);

            // Authenticate
            _logger.LogInformation("Sending authentication...");
            await SendRawAsync($"PASS oauth:{oauthToken}");
            await SendRawAsync($"NICK {username}");

            // Request capabilities
            await SendRawAsync("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");

            // Join channel
            await SendRawAsync($"JOIN #{Channel}");

            _logger.LogInformation("Authentication and JOIN sent, starting read loop...");

            _readTask = Task.Run(() => ReadMessagesAsync(_cts.Token));
            _keepAliveTask = Task.Run(() => KeepAliveAsync(_cts.Token));

            _logger.LogInformation("Connected to Twitch IRC WebSocket: {Channel}", Channel);
            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Twitch IRC WebSocket");
            Dispose();
            throw;
        }
    }

    private async Task SendRawAsync(string message)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\r\n");
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            _lastMessageTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send IRC command");
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            await SendRawAsync($"PRIVMSG #{Channel} :{message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message");
        }
    }

    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);

                // Send PING if no activity in last 60 seconds
                if ((DateTime.UtcNow - _lastMessageTime).TotalSeconds >= 60)
                {
                    await SendRawAsync("PING :tmi.twitch.tv");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KeepAlive task failed");
        }
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        var messageBuffer = new StringBuilder(capacity: 512);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer, 0, 4096), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close message received");
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageBuffer.Append(text);
                _lastMessageTime = DateTime.UtcNow;

                // Process complete IRC messages (ending with \r\n)
                var messages = messageBuffer.ToString();
                var lines = messages.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                // Keep incomplete message in buffer
                if (!messages.EndsWith("\r\n"))
                {
                    messageBuffer.Clear();
                    messageBuffer.Append(lines[^1]);
                    lines = lines[..^1];
                }
                else
                {
                    messageBuffer.Clear();
                }

                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // Log ALL IRC messages temporarily for debugging
                        _logger.LogInformation("IRC RX: {Line}", line);
                        ProcessIrcMessage(line);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error reading IRC WebSocket messages");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _logger.LogInformation("IRC WebSocket read loop ended");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ProcessIrcMessage(string line)
    {
        // Check for authentication failure
        if (line.Contains("Login authentication failed") || line.Contains("Login unsuccessful"))
        {
            _logger.LogError("❌ TWITCH AUTHENTICATION FAILED - OAuth token is invalid or expired. Please re-authenticate in the Platforms tab.");
            AuthenticationFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Temporary logging to debug PING/PONG
        if (line.Contains("PING") || line.Contains("PONG"))
        {
            _logger.LogInformation("IRC PING/PONG: {Line}", line);
        }

        // Respond to PING
        if (line.StartsWith("PING"))
        {
            var server = line.Split(' ')[1];
            _ = SendRawAsync($"PONG {server}");
            _logger.LogInformation("Sent PONG response to {Server}", server);
            return;
        }

        // Parse PRIVMSG
        if (line.Contains("PRIVMSG"))
        {
            try
            {
                // IRC format with tags: @tags :user!user@user.tmi.twitch.tv PRIVMSG #channel :message
                // IRC format without tags: :user!user@user.tmi.twitch.tv PRIVMSG #channel :message

                string username = "";
                string message = "";

                // Find the user part (starts with : after tags or at the beginning)
                int userStart = line.IndexOf(" :");
                if (userStart == -1) userStart = 0;
                else userStart += 2; // Skip " :"

                // Extract username from :username!username@username.tmi.twitch.tv
                int userEnd = line.IndexOf('!', userStart);
                if (userEnd > userStart)
                {
                    username = line.Substring(userStart, userEnd - userStart);
                }

                // Find message (after "PRIVMSG #channel :")
                var privmsgIndex = line.IndexOf("PRIVMSG");
                if (privmsgIndex != -1)
                {
                    var messageStartIndex = line.IndexOf(':', privmsgIndex) + 1;
                    if (messageStartIndex > 0 && messageStartIndex < line.Length)
                    {
                        message = line.Substring(messageStartIndex);
                    }
                }

                // Extract tags for badges, display-name, color, user-id
                var badges = new Dictionary<string, string>();
                var isMod = false;
                var isSub = false;
                var isVip = false;
                var isBroadcaster = false;
                string? displayName = null;
                string? color = null;
                string? userId = null;

                // Parse all IRC tags: @badges=broadcaster/1;color=#FF0000;display-name=User;user-id=12345;...
                if (line.StartsWith("@"))
                {
                    var tagsEnd = line.IndexOf(" :");
                    if (tagsEnd > 0)
                    {
                        var tagsSection = line.Substring(1, tagsEnd - 1);
                        var tags = tagsSection.Split(';');

                        foreach (var tag in tags)
                        {
                            if (tag.StartsWith("badges="))
                            {
                                var badgesValue = tag.Substring(7); // Remove "badges="
                                if (!string.IsNullOrEmpty(badgesValue))
                                {
                                    var badgePairs = badgesValue.Split(',');
                                    foreach (var badgePair in badgePairs)
                                    {
                                        var parts = badgePair.Split('/');
                                        if (parts.Length == 2)
                                        {
                                            badges[parts[0]] = parts[1];

                                            // Set boolean flags
                                            if (parts[0] == "broadcaster") isBroadcaster = true;
                                            if (parts[0] == "moderator") isMod = true;
                                            if (parts[0] == "subscriber") isSub = true;
                                            if (parts[0] == "vip") isVip = true;
                                        }
                                    }
                                }
                            }
                            else if (tag.StartsWith("display-name="))
                            {
                                displayName = tag.Substring(13); // Remove "display-name="
                            }
                            else if (tag.StartsWith("color="))
                            {
                                color = tag.Substring(6); // Remove "color="
                            }
                            else if (tag.StartsWith("user-id="))
                            {
                                userId = tag.Substring(8); // Remove "user-id="
                            }
                            else if (tag.StartsWith("mod="))
                            {
                                if (tag == "mod=1") isMod = true;
                            }
                            else if (tag.StartsWith("subscriber="))
                            {
                                if (tag == "subscriber=1") isSub = true;
                            }
                            else if (tag.StartsWith("first-msg=1"))
                            {
                                badges["first-time-chatter"] = "1";
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(message))
                {
                    MessageReceived?.Invoke(this, new TwitchMessageEventArgs
                    {
                        Username = username,
                        DisplayName = displayName ?? username,
                        UserId = userId ?? "",
                        Message = message,
                        Color = color ?? "",
                        IsModerator = isMod,
                        IsSubscriber = isSub,
                        IsVip = isVip,
                        IsBroadcaster = isBroadcaster,
                        Badges = badges,
                        Channel = Channel
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse message: {Line}", line);
            }
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _keepAliveTask?.Wait(TimeSpan.FromSeconds(2));
        _readTask?.Wait(TimeSpan.FromSeconds(2));
        Dispose();
        _logger.LogInformation("Disconnected from Twitch IRC WebSocket");
    }

    public void Dispose()
    {
        _cts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing WebSocket gracefully");
            }
        }

        _webSocket?.Dispose();
        _cts?.Dispose();

        _webSocket = null;
        _cts = null;
    }
}

public class TwitchMessageEventArgs : EventArgs
{
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Message { get; init; } = "";
    public string Channel { get; init; } = "";
    public string Color { get; init; } = "";
    public bool IsModerator { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsVip { get; init; }
    public bool IsBroadcaster { get; init; }
    public Dictionary<string, string> Badges { get; init; } = new();
}
