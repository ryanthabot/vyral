using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Twitch.Events;

namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Twitch IRC client service using lightweight IRC implementation.
/// </summary>
public class TwitchService : ITwitchService, IDisposable
{
    private readonly ILogger<TwitchService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEventBus _eventBus;
    private readonly TwitchOAuthService _oAuthService;
    private readonly IChatMessageStore? _chatMessageStore;
    private readonly IDataService? _dataService;
    private readonly ITwitchEventSubService? _eventSubService;
    private LightweightTwitchIrc? _client;
    private string? _channel;
    private string? _botUsername;
    private string? _accessToken;
    private string? _refreshToken;
    private bool _disposed;
    private bool _intentionalDisconnect;
    private CancellationTokenSource? _reconnectCts;
    private Platform? _platform;

    public bool IsConnected => _client?.IsConnected ?? false;
    public string? Channel => _channel;
    public string? BotUsername => _botUsername;

    public TwitchService(
        ILogger<TwitchService> logger,
        ILoggerFactory loggerFactory,
        IEventBus eventBus,
        TwitchOAuthService oAuthService,
        IChatMessageStore? chatMessageStore = null,
        IDataService? dataService = null,
        ITwitchEventSubService? eventSubService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _oAuthService = oAuthService ?? throw new ArgumentNullException(nameof(oAuthService));
        _chatMessageStore = chatMessageStore;
        _dataService = dataService;
        _eventSubService = eventSubService;
    }

    /// <summary>
    /// Connects to Twitch IRC chat.
    /// </summary>
    public async Task ConnectAsync(string username, string accessToken, string channel)
    {
        await ConnectAsync(username, accessToken, null, channel);
    }

    /// <summary>
    /// Connects to Twitch IRC chat with optional refresh token.
    /// </summary>
    public async Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel)
    {
        if (string.IsNullOrEmpty(username)) throw new ArgumentNullException(nameof(username));
        if (string.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));
        if (string.IsNullOrEmpty(channel)) throw new ArgumentNullException(nameof(channel));

        _logger.LogInformation("Connecting to Twitch as {Username} in #{Channel}", username, channel);

        // Load platform settings for filter checks
        if (_dataService != null)
        {
            try
            {
                _platform = await _dataService.GetPlatformByNameAsync("Twitch");
                _logger.LogInformation("Loaded Twitch platform settings: TwitchChatEnabled={Enabled}", _platform?.TwitchChatEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Twitch platform settings");
            }
        }

        // Store credentials for reconnection
        _botUsername = username;
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _channel = channel;
        _intentionalDisconnect = false;

        // Create lightweight IRC client
        var ircLogger = _loggerFactory.CreateLogger<LightweightTwitchIrc>();
        _client = new LightweightTwitchIrc(ircLogger);

        // Wire up events
        _client.Connected += OnConnected;
        _client.MessageReceived += OnMessageReceived;
        _client.Disconnected += OnDisconnected;
        _client.AuthenticationFailed += OnAuthenticationFailed;

        // Connect (non-blocking)
        await _client.ConnectAsync(username, accessToken, channel);

        _logger.LogInformation("Successfully connected to Twitch IRC #{Channel}", channel);

        // Connect EventSub WebSocket if available
        if (_eventSubService != null && _dataService != null)
        {
            try
            {
                var twitchPlatform = await _dataService.GetPlatformByNameAsync("Twitch");
                if (twitchPlatform != null && !string.IsNullOrEmpty(twitchPlatform.UserId) && !string.IsNullOrEmpty(twitchPlatform.ClientId))
                {
                    _logger.LogInformation("Connecting Twitch EventSub for user {UserId}", twitchPlatform.UserId);
                    await _eventSubService.ConnectAsync(twitchPlatform.ClientId, accessToken, twitchPlatform.UserId);
                    _logger.LogInformation("Twitch EventSub connected successfully");
                }
                else
                {
                    _logger.LogWarning("Cannot connect EventSub - missing UserId or ClientId in Platform settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect Twitch EventSub - events will not be available");
            }
        }
    }

    /// <summary>
    /// Disconnects from Twitch chat.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();

        if (_eventSubService != null)
        {
            try
            {
                await _eventSubService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting EventSub");
            }
        }

        if (_client != null)
        {
            _logger.LogInformation("Disconnecting from Twitch");
            _client.Disconnect();
        }
    }

    /// <summary>
    /// Sends a chat message.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to Twitch");
        }

        if (string.IsNullOrEmpty(_channel))
        {
            throw new InvalidOperationException("No channel connected");
        }

        _logger.LogDebug("Sending message to #{Channel}: {Message}", _channel, message);
        await _client!.SendMessageAsync(message);
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Twitch client connected");
    }

    private void OnAuthenticationFailed(object? sender, EventArgs e)
    {
        _logger.LogWarning("Twitch authentication failed - attempting token refresh");

        // Attempt token refresh if we have a refresh token
        if (!string.IsNullOrEmpty(_refreshToken))
        {
            _ = Task.Run(async () => await RefreshAndReconnectAsync());
        }
        else
        {
            _logger.LogError("No refresh token available - cannot refresh Twitch token");
        }
    }

    private async Task RefreshAndReconnectAsync()
    {
        try
        {
            _logger.LogInformation("Refreshing Twitch access token...");
            // Ensure OAuth client credentials are loaded before attempting a refresh
            try
            {
                await _oAuthService.LoadCredentialsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Twitch client credentials before refresh; proceeding anyway");
            }

            var result = await _oAuthService.RefreshAccessTokenAsync(_refreshToken!);
            if (result.HasValue)
            {
                _accessToken = result.Value.AccessToken;
                _refreshToken = result.Value.RefreshToken;

                // Save the new tokens
                await _oAuthService.SaveTokensAsync(_botUsername!, _accessToken, _refreshToken);

                _logger.LogInformation("Successfully refreshed Twitch access token, reconnecting...");

                // Disconnect old client
                if (_client != null)
                {
                    _client.Connected -= OnConnected;
                    _client.MessageReceived -= OnMessageReceived;
                    _client.Disconnected -= OnDisconnected;
                    _client.AuthenticationFailed -= OnAuthenticationFailed;
                    _client.Dispose();
                }

                // Recreate client with new token
                var ircLogger = _loggerFactory.CreateLogger<LightweightTwitchIrc>();
                _client = new LightweightTwitchIrc(ircLogger);

                _client.Connected += OnConnected;
                _client.MessageReceived += OnMessageReceived;
                _client.Disconnected += OnDisconnected;
                _client.AuthenticationFailed += OnAuthenticationFailed;

                await _client.ConnectAsync(_botUsername!, _accessToken, _channel!);

                _logger.LogInformation("Successfully reconnected to Twitch with refreshed token");
            }
            else
            {
                _logger.LogError("Failed to refresh Twitch access token");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Twitch token");
        }
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Twitch client disconnected");

        // Attempt reconnection if not intentional disconnect
        if (!_intentionalDisconnect && !_disposed &&
            !string.IsNullOrEmpty(_botUsername) &&
            !string.IsNullOrEmpty(_accessToken) &&
            !string.IsNullOrEmpty(_channel))
        {
            _logger.LogInformation("Attempting to reconnect to Twitch...");
            _reconnectCts = new CancellationTokenSource();
            _ = Task.Run(async () => await ReconnectAsync(_reconnectCts.Token));
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 5;
        int retryCount = 0;

        while (!cancellationToken.IsCancellationRequested && retryCount < maxRetries)
        {
            try
            {
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Min(retryCount * 5, 30)); // Exponential backoff up to 30 seconds

                _logger.LogInformation("Reconnection attempt {Attempt}/{MaxRetries} in {Delay} seconds",
                    retryCount, maxRetries, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);

                if (cancellationToken.IsCancellationRequested) break;

                // Clean up old client
                if (_client != null)
                {
                    _client.Connected -= OnConnected;
                    _client.MessageReceived -= OnMessageReceived;
                    _client.Disconnected -= OnDisconnected;
                    _client.AuthenticationFailed -= OnAuthenticationFailed;
                    _client.Dispose();
                }

                // Recreate client and reconnect
                var ircLogger = _loggerFactory.CreateLogger<LightweightTwitchIrc>();
                _client = new LightweightTwitchIrc(ircLogger);

                _client.Connected += OnConnected;
                _client.MessageReceived += OnMessageReceived;
                _client.Disconnected += OnDisconnected;
                _client.AuthenticationFailed += OnAuthenticationFailed;

                await _client.ConnectAsync(_botUsername!, _accessToken!, _channel!);

                _logger.LogInformation("Successfully reconnected to Twitch");
                return; // Success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconnection attempt {Attempt} failed", retryCount);
            }
        }

        if (retryCount >= maxRetries)
        {
            _logger.LogError("Failed to reconnect to Twitch after {MaxRetries} attempts", maxRetries);
        }
    }

    private void OnMessageReceived(object? sender, TwitchMessageEventArgs e)
    {
        _logger.LogInformation("Received Twitch message from {Username}: {Message}",
            e.Username, e.Message);

        try
        {
            var chatEvent = new TwitchChatMessageEvent
            {
                Source = "Twitch",
                Channel = e.Channel,
                Username = e.Username,
                UserId = e.UserId ?? "",
                DisplayName = e.DisplayName ?? e.Username,
                Message = e.Message,
                Color = e.Color ?? "",
                IsSubscriber = e.IsSubscriber,
                IsModerator = e.IsModerator,
                IsBroadcaster = e.IsBroadcaster,
                IsVip = e.IsVip,
                SubscriberMonths = 0,
                Badges = e.Badges,
                Emotes = new List<string>()
            };

            // Publish to EventBus using fire-and-forget to avoid blocking the WebSocket read thread
            // CRITICAL: .GetAwaiter().GetResult() was blocking the IRC read loop and preventing message delivery
            _ = Task.Run(async () =>
            {
                try
                {
                    await _eventBus.PublishAsync(chatEvent);
                    _logger.LogDebug("Published TwitchChatMessageEvent from {Username}", e.Username);
                }
                catch (Exception pubEx)
                {
                    _logger.LogError(pubEx, "Failed to publish TwitchChatMessageEvent to EventBus");
                }
            });

            // Store to database (always store - TwitchChatEnabled controls display, not storage)
            if (_platform?.TwitchChatEnabled ?? true)
            {
                _logger.LogDebug("💾 Storing Twitch message in database for channel: {Channel}", _channel ?? "Unknown");
                _chatMessageStore?.AddAsync("Twitch", _channel ?? "Unknown", chatEvent);
            }
            else
            {
                // Still store even when disabled - user may want to load history later
                _logger.LogDebug("💾 Storing Twitch message (TwitchChatEnabled=false): {Username}", e.Username);
                _chatMessageStore?.AddAsync("Twitch", _channel ?? "Unknown", chatEvent);
            }

            _logger.LogInformation("✅ Twitch chat message processed: {Username}: {Message}", e.Username, e.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Twitch chat message");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();

        // Dispose EventSub
        if (_eventSubService != null)
        {
            try
            {
                _eventSubService.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing EventSub");
            }
        }

        // Unsubscribe from events
        if (_client != null)
        {
            _client.Connected -= OnConnected;
            _client.MessageReceived -= OnMessageReceived;
            _client.Disconnected -= OnDisconnected;
            _client.AuthenticationFailed -= OnAuthenticationFailed;

            _client.Dispose();
            _client = null;
        }

        _disposed = true;

        _logger.LogInformation("TwitchService disposed");
    }
}
