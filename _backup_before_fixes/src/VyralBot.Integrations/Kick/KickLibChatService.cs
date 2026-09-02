using KickLib;
using KickLib.Client;
using KickLib.Client.Interfaces;
using KickLib.Client.Models.Args;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using VyralBot.Core.Services;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Kick.Events;

namespace VyralBot.Integrations.Kick;

/// <summary>
/// KickLib-based chat service that replaces the WebView2-based KickService.
/// Uses official KickLib IKickClient for real-time chat via Pusher WebSocket.
/// Eliminates 40-60MB WebView2 overhead.
/// </summary>
public class KickLibChatService : IKickService, IDisposable
{
    private readonly ILogger<KickLibChatService> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly IChatMessageStore? _chatMessageStore;
    private readonly IKickClient _kickClient;
    private readonly IKickApi _kickApi;

    private Platform? _platform;
    private int _chatroomId;
    private int _channelId;
    private string? _currentChannel;
    private string? _botUsername;
    private bool _disposed;

    public bool IsConnected => _kickClient.IsConnected;
    public string? Channel => _currentChannel;
    public string? BotUsername => _botUsername;

    public KickLibChatService(
        ILogger<KickLibChatService> logger,
        IDataService dataService,
        IEventBus eventBus,
        IChatMessageStore? chatMessageStore = null)
    {
        _logger = logger;
        _dataService = dataService;
        _eventBus = eventBus;
        _chatMessageStore = chatMessageStore;

        // Create KickLib client (no authentication needed for reading chat)
        _kickClient = new KickClient(_logger);

        // Create KickLib API (for sending messages)
        _kickApi = KickApi.Create(loggerFactory: LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        }));

        // Subscribe to client events
        _kickClient.OnMessage += OnChatMessage;
        _kickClient.OnSubscription += OnSubscription;
        _kickClient.OnGiftedSubscription += OnGiftedSubscription;
        _kickClient.OnUserBanned += OnUserBanned;
        _kickClient.OnUserUnbanned += OnUserUnbanned;
        _kickClient.OnMessageDeleted += OnMessageDeleted;
        _kickClient.OnStreamStatusChanged += OnStreamStatusChanged;
        _kickClient.OnStreamHost += OnStreamHost;
        _kickClient.OnFollowersUpdated += OnFollowersUpdated;
        _kickClient.OnRewardRedeemed += OnRewardRedeemed;
        _kickClient.OnKicksGifted += OnKicksGifted;
        _kickClient.OnConnected += OnConnected;
        _kickClient.OnDisconnected += OnDisconnected;
    }

    /// <summary>
    /// Connect using username and credentials from database.
    /// This matches the existing IKickService signature for compatibility.
    /// </summary>
    public async Task ConnectAsync(string username, string accessToken, string channelName, int broadcasterUserId)
    {
        await ConnectInternalAsync(username, accessToken, channelName);
    }

    /// <summary>
    /// Connect with optional refresh token.
    /// This matches the existing IKickService signature for compatibility.
    /// </summary>
    public async Task ConnectAsync(string username, string accessToken, string? refreshToken, string channelName, int broadcasterUserId)
    {
        await ConnectInternalAsync(username, accessToken, channelName);
    }

    private async Task ConnectInternalAsync(string username, string accessToken, string channelName)
    {
        try
        {
            _logger.LogInformation("🚀 Connecting to Kick using KickLib - Username: {Username}, Channel: {Channel}",
                username, channelName);

            _botUsername = username;
            _currentChannel = channelName;

            // Get platform from database to access credentials and channel IDs
            _platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (_platform == null)
            {
                _logger.LogError("❌ Kick platform not found in database");
                throw new InvalidOperationException("Kick platform not configured");
            }

            _logger.LogInformation("📂 Loaded Platform from DB - ID: {Id}, ChannelId: {ChannelId}, ChatroomId: {ChatroomId}",
                _platform.Id, _platform.ChannelId ?? "null", _platform.ChatroomId ?? "null");

            // Load channel ID and chatroom ID from database
            if (string.IsNullOrEmpty(_platform.ChannelId) || string.IsNullOrEmpty(_platform.ChatroomId))
            {
                _logger.LogError("❌ Channel ID or Chatroom ID not found in database. Please reconfigure Kick platform.");
                throw new InvalidOperationException("Kick channel IDs not configured. Please run the setup wizard again.");
            }

            _channelId = int.Parse(_platform.ChannelId);
            _chatroomId = int.Parse(_platform.ChatroomId);

            _logger.LogInformation("🔌 Using channel ID: {ChannelId}, chatroom ID: {ChatroomId}",
                _channelId, _chatroomId);

            // Listen to chatroom events (messages only - other events handled via webhooks)
            _logger.LogInformation("🔌 Subscribing to chatroom events (chat messages)...");
            _logger.LogInformation("📡 Starting to listen to chatroom ID: {ChatroomId}", _chatroomId);
            await _kickClient.ListenToChatRoomAsync(_chatroomId);
            _logger.LogInformation("✅ Now listening to chatroom ID: {ChatroomId}", _chatroomId);

            // NOTE: Channel events (follows, subs, stream status, etc.) are now handled via official Kick webhooks
            // in WebhookCoordinatorService to avoid duplicate events. Only chat messages use Pusher.

            // Connect to Pusher WebSocket
            _logger.LogInformation("🔌 Connecting to Pusher WebSocket...");
            await _kickClient.ConnectAsync();

            _logger.LogInformation("✅ Pusher WebSocket connected! IsConnected: {IsConnected}", _kickClient.IsConnected);
            _logger.LogInformation("✅ Successfully connected to Kick chat (KickLib) - Chatroom: {ChatroomId}", _chatroomId);
            _logger.LogInformation("⏳ Waiting for messages on chatroom {ChatroomId}...", _chatroomId);

            // Publish platform connection event
            await _eventBus.PublishAsync(new PlatformConnectionChangedEvent
            {
                Source = "KickLibChatService",
                Platform = "Kick",
                Connected = true,
                Channel = _currentChannel,
                BotUsername = _botUsername
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to connect to Kick chat using KickLib");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_kickClient.IsConnected)
            {
                _logger.LogInformation("🔌 Disconnecting from Kick chat...");
                await _kickClient.DisconnectAsync();
                _logger.LogInformation("✅ Disconnected from Kick chat");
            }

            // Publish platform disconnection event
            await _eventBus.PublishAsync(new PlatformConnectionChangedEvent
            {
                Source = "KickLibChatService",
                Platform = "Kick",
                Connected = false,
                Channel = _currentChannel,
                BotUsername = _botUsername
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to disconnect from Kick chat");
            throw;
        }
    }

    public async Task SendMessageAsync(string message)
    {
        try
        {
            if (_platform?.AccessToken == null)
            {
                _logger.LogError("❌ No access token available for sending message");
                throw new InvalidOperationException("No access token available");
            }

            // Check if token is expired and refresh if needed
            if (_platform.TokenExpiresAt.HasValue && _platform.TokenExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogInformation("🔄 Access token expired, refreshing...");
                var refreshed = await RefreshTokenAsync();
                if (!refreshed)
                {
                    _logger.LogError("❌ Failed to refresh token");
                    throw new InvalidOperationException("Failed to refresh token");
                }
            }

            // Send message as user (not bot) using KickLib API
            // This requires the broadcaster USER ID (not channel ID)
            if (_platform.UserId == null)
            {
                _logger.LogError("❌ User ID not available in database");
                throw new InvalidOperationException("User ID not found - please reconnect to Kick");
            }

            var broadcasterUserId = int.Parse(_platform.UserId);
            var result = await _kickApi.Chat.SendMessageAsUserAsync(broadcasterUserId, message, _platform.AccessToken);

            if (!result.IsSuccess)
            {
                var error = result.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                _logger.LogError("❌ Failed to send message: {Error}", error);
                throw new InvalidOperationException($"Failed to send message: {error}");
            }

            _logger.LogInformation("✅ Message sent successfully as user: {Message}", message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Exception sending message");
            throw;
        }
    }

    private async Task<bool> RefreshTokenAsync()
    {
        try
        {
            if (_platform?.RefreshToken == null || _platform.ClientId == null || _platform.ClientSecret == null)
            {
                _logger.LogWarning("⚠️ Missing credentials for token refresh");
                return false;
            }

            var oauthGenerator = new KickLib.Auth.KickOAuthGenerator();
            var result = await oauthGenerator.RefreshAccessTokenAsync(
                _platform.RefreshToken,
                _platform.ClientId,
                _platform.ClientSecret);

            if (!result.IsSuccess)
            {
                var error = result.Errors.FirstOrDefault()?.Message ?? "Unknown error";
                _logger.LogError("❌ Token refresh failed: {Error}", error);
                return false;
            }

            var tokenData = result.Value;

            // Update platform with new tokens
            _platform.AccessToken = tokenData.AccessToken;
            _platform.RefreshToken = tokenData.RefreshToken;
            _platform.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn);
            _platform.UpdatedAt = DateTime.UtcNow;

            await _dataService.SavePlatformAsync(_platform);

            _logger.LogInformation("✅ Access token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Exception during token refresh");
            return false;
        }
    }

    #region Event Handlers - Convert KickLib events to VyralBot events

    private void OnChatMessage(object? sender, ChatMessageEventArgs e)
    {
        _logger.LogInformation("🎯 OnChatMessage event FIRED! Sender: {Sender}", sender?.GetType().Name ?? "null");

        var msg = e.Data;
        _logger.LogInformation("💬 Received message from {Username} (ID: {UserId}): {Message}",
            msg.Sender.Username, msg.Sender.Id, msg.Content);

        var chatEvent = new KickChatMessageEvent
        {
            Source = "Kick",
            Channel = _currentChannel ?? "Unknown",
            Username = msg.Sender.Username,
            UserId = msg.Sender.Id.ToString(),
            Message = msg.Content,
            DisplayName = msg.Sender.Username,
            Color = msg.Sender.Identity?.Color,
            Badges = msg.Sender.Identity?.Badges?.ToDictionary(b => b.Type.ToString(), b => b.Text) ?? new Dictionary<string, string>(),
            IsSubscriber = msg.Sender.Identity?.Badges?.Any(b => b.Type.ToString().ToLower() == "subscriber") ?? false,
            IsModerator = msg.Sender.Identity?.Badges?.Any(b => b.Type.ToString().ToLower() == "moderator") ?? false,
            IsOwner = msg.Sender.Identity?.Badges?.Any(b => b.Type.ToString().ToLower() == "broadcaster") ?? false,
            CreatedAt = msg.CreatedAt
        };

        // Publish to EventBus (triggers ChatViewModel, TriggerEngine, etc.)
        // Use Task.Run to avoid blocking the Pusher event thread and ensure delivery
        _logger.LogInformation("📢 Publishing KickChatMessageEvent to EventBus for {Username}", msg.Sender.Username);
        _ = Task.Run(async () =>
        {
            try
            {
                await _eventBus.PublishAsync(chatEvent);
                _logger.LogDebug("✅ Published KickChatMessageEvent to EventBus for {Username}", msg.Sender.Username);
            }
            catch (Exception pubEx)
            {
                _logger.LogError(pubEx, "❌ Failed to publish KickChatMessageEvent to EventBus for {Username}", msg.Sender.Username);
            }
        });

        // Store in database (always store for history, regardless of KickChatEnabled)
        _logger.LogDebug("💾 Storing message in database for channel: {Channel}", _currentChannel ?? "Unknown");
        _chatMessageStore?.AddAsync("Kick", _currentChannel ?? "Unknown", chatEvent);

        _logger.LogInformation("✅ Chat message processed: {Username}: {Message}", msg.Sender.Username, msg.Content);
    }

    private void OnSubscription(object? sender, SubscriptionEventArgs e)
    {
        var sub = e.Data;

        // Publish dedicated Subscribe event
        var subscribeEvent = new KickSubscribeEvent
        {
            Source = "Kick",
            Username = sub.Username,
            UserId = "0", // KickLib's SubscriptionEvent doesn't provide user ID
            MonthsSubscribed = sub.Months
        };
        _ = _eventBus.PublishAsync(subscribeEvent);

        // Also create chat message for display
        var chatEvent = new KickChatMessageEvent
        {
            Source = "Kick",
            Channel = _currentChannel ?? "Unknown",
            Username = "Vyral.Bot",
            UserId = "0",
            DisplayName = "Vyral.Bot",
            Message = $"🎉 {sub.Username} just subscribed! (Month {sub.Months})"
        };

        _ = _eventBus.PublishAsync(chatEvent);

        if (_platform?.KickChatEnabled ?? true)
        {
            _chatMessageStore?.AddAsync("Kick", _currentChannel ?? "Unknown", chatEvent);
        }

        _logger.LogInformation("🎉 Subscription: {Username} (Month {Months})", sub.Username, sub.Months);
    }

    private void OnGiftedSubscription(object? sender, GiftedSubscriptionsEventArgs e)
    {
        var gift = e.Data;

        // Publish dedicated GiftSub event
        var giftSubEvent = new KickGiftSubEvent
        {
            Source = "Kick",
            GifterUsername = gift.GifterUsername,
            GifterUserId = "0", // KickLib doesn't provide gifter ID
            Count = gift.GiftedTo.Count,
            RecipientUsernames = gift.GiftedTo.ToList()
        };
        _ = _eventBus.PublishAsync(giftSubEvent);

        // Also create chat message for display
        var chatEvent = new KickChatMessageEvent
        {
            Source = "Kick",
            Channel = _currentChannel ?? "Unknown",
            Username = "Vyral.Bot",
            UserId = "0",
            DisplayName = "Vyral.Bot",
            Message = $"🎁 {gift.GifterUsername} gifted {gift.GiftedTo.Count} subscriptions to the community!"
        };

        _ = _eventBus.PublishAsync(chatEvent);

        if (_platform?.KickChatEnabled ?? true)
        {
            _chatMessageStore?.AddAsync("Kick", _currentChannel ?? "Unknown", chatEvent);
        }

        _logger.LogInformation("🎁 Gifted subs: {Gifter} gifted {Count} subs", gift.GifterUsername, gift.GiftedTo.Count);
    }

    private void OnUserBanned(object? sender, UserBannedEventArgs e)
    {
        var ban = e.Data;
        _logger.LogInformation("🔨 User banned: {Username} by {Moderator} (Permanent: {Permanent})",
            ban.User.Username, ban.BannedBy.Username, ban.Permanent);
    }

    private void OnUserUnbanned(object? sender, UserUnbannedEventArgs e)
    {
        var unban = e.Data;
        _logger.LogInformation("✅ User unbanned: {Username} by {Moderator}",
            unban.User.Username, unban.User.Username);
    }

    private void OnMessageDeleted(object? sender, MessageDeletedEventArgs e)
    {
        _logger.LogInformation("🗑️ Message deleted: {MessageId}", e.Data.Message.Id);
    }

    private void OnStreamStatusChanged(object? sender, StreamStateChangedArgs e)
    {
        var status = e.IsLive ? "🔴 ONLINE" : "⚫ OFFLINE";
        _logger.LogInformation("📡 Stream status changed: {Status}", status);

        if (e.IsLive && e.Data != null)
        {
            var chatEvent = new KickChatMessageEvent
            {
                Source = "Kick",
                Channel = _currentChannel ?? "Unknown",
                Username = "Vyral.Bot",
                UserId = "0",
                DisplayName = "Vyral.Bot",
                Message = $"🔴 Stream is now LIVE! - {e.Data.SessionTitle}"
            };

            _ = _eventBus.PublishAsync(chatEvent);
            _chatMessageStore?.AddAsync("Kick", _currentChannel ?? "Unknown", chatEvent);
        }
    }

    private void OnStreamHost(object? sender, StreamHostEventArgs e)
    {
        var host = e.Data;
        _logger.LogInformation("🎯 Stream hosted by: {Username}", host.HostUsername);

        var chatEvent = new KickChatMessageEvent
        {
            Source = "Kick",
            Channel = _currentChannel ?? "Unknown",
            Username = "Vyral.Bot",
            UserId = "0",
            DisplayName = "Vyral.Bot",
            Message = $"🎯 Hosted by {host.HostUsername} with {host.NumberViewers} viewers!"
        };

        _ = _eventBus.PublishAsync(chatEvent);
        _chatMessageStore?.AddAsync("Kick", _currentChannel ?? "Unknown", chatEvent);
    }

    private void OnFollowersUpdated(object? sender, FollowersUpdatedEventArgs e)
    {
        // Update variable in database
        _ = Task.Run(async () =>
        {
            try
            {
                await _dataService.UpdateVariableValueAsync("kick-followercount", e.Data.FollowersCount.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update kick-followercount variable");
            }
        });

        // Publish dedicated Follow event from Pusher (takes priority over webhooks)
        // Note: KickLib Pusher doesn't provide follower username in this event
        var followEvent = new KickFollowEvent
        {
            Source = "Kick",
            Username = "Unknown", // KickLib limitation - username not provided
            UserId = "0",
            FollowerCount = e.Data.FollowersCount
        };
        _ = _eventBus.PublishAsync(followEvent);

        _logger.LogInformation("👥 New follower! Total followers: {Count}", e.Data.FollowersCount);
    }

    private void OnRewardRedeemed(object? sender, RewardRedeemedEventArgs e)
    {
        var reward = e.Data;

        // Publish dedicated RewardRedeemed event
        var rewardEvent = new KickRewardRedeemedEvent
        {
            Source = "Kick",
            Username = reward.Username,
            UserId = reward.UserId.ToString(),
            RewardName = reward.RewardTitle,
            Cost = 0, // KickLib doesn't provide cost
            UserInput = reward.UserInput
        };
        _ = _eventBus.PublishAsync(rewardEvent);

        _logger.LogInformation("🏆 Reward redeemed: {Reward} by {Username}", reward.RewardTitle, reward.Username);
    }

    private void OnKicksGifted(object? sender, KicksGiftedEventArgs e)
    {
        var gift = e.Data;

        // Publish dedicated KicksGifted event
        var kicksEvent = new KickKicksGiftedEvent
        {
            Source = "Kick",
            SenderUsername = gift.Sender.Username,
            SenderUserId = gift.Sender.Id.ToString(),
            Amount = gift.Gift.Amount,
            Message = gift.Message
        };
        _ = _eventBus.PublishAsync(kicksEvent);

        _logger.LogInformation("💰 Kicks gifted: {Amount} by {Username}", gift.Gift.Amount, gift.Sender.Username);
    }

    private void OnConnected(object? sender, ClientConnectedArgs e)
    {
        _logger.LogInformation("✅ KickLib client connected to Pusher (Socket ID: {SocketId})", e.SocketId);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("⚠️ KickLib client disconnected from Pusher");
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // Unsubscribe from events
            _kickClient.OnMessage -= OnChatMessage;
            _kickClient.OnSubscription -= OnSubscription;
            _kickClient.OnGiftedSubscription -= OnGiftedSubscription;
            _kickClient.OnUserBanned -= OnUserBanned;
            _kickClient.OnUserUnbanned -= OnUserUnbanned;
            _kickClient.OnMessageDeleted -= OnMessageDeleted;
            _kickClient.OnStreamStatusChanged -= OnStreamStatusChanged;
            _kickClient.OnStreamHost -= OnStreamHost;
            _kickClient.OnFollowersUpdated -= OnFollowersUpdated;
            _kickClient.OnRewardRedeemed -= OnRewardRedeemed;
            _kickClient.OnKicksGifted -= OnKicksGifted;
            _kickClient.OnConnected -= OnConnected;
            _kickClient.OnDisconnected -= OnDisconnected;

            // Disconnect if still connected
            if (_kickClient.IsConnected)
            {
                _kickClient.DisconnectAsync().GetAwaiter().GetResult();
            }

            _disposed = true;
            _logger.LogInformation("🧹 KickLibChatService disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error disposing KickLibChatService");
        }
    }
}
