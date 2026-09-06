using Microsoft.Extensions.Logging;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets.Core.Models;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Integrations.Twitch.Events;

namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Service for managing Twitch EventSub WebSocket connections and publishing events.
/// </summary>
public class TwitchEventSubService : ITwitchEventSubService
{
    private readonly ILogger<TwitchEventSubService> _logger;
    private readonly IEventBus _eventBus;
    private readonly IDataService _dataService;
    private readonly EventSubWebsocketClient _eventSubClient;
    private readonly TwitchAPI _twitchApi;
    private bool _isConnected;
    private string? _userId;
    private string? _accessToken;

    public TwitchEventSubService(
        ILogger<TwitchEventSubService> logger,
        IEventBus eventBus,
        IDataService dataService,
        EventSubWebsocketClient eventSubClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventSubClient = eventSubClient ?? throw new ArgumentNullException(nameof(eventSubClient));
        
        _twitchApi = new TwitchAPI();

        // Subscribe to connection events
        _eventSubClient.WebsocketConnected += async (sender, args) => await OnWebsocketConnected(sender, args);
        _eventSubClient.WebsocketDisconnected += async (sender, args) => await OnWebsocketDisconnected(sender, args);
        _eventSubClient.ErrorOccurred += async (sender, args) => await OnErrorOccurred(sender, args);

        // Subscribe to Twitch events
        _eventSubClient.ChannelFollow += async (sender, args) => await OnChannelFollow(sender, args);
        _eventSubClient.ChannelSubscribe += async (sender, args) => await OnChannelSubscribe(sender, args);
        _eventSubClient.ChannelSubscriptionMessage += async (sender, args) => await OnChannelSubscriptionMessage(sender, args);
        _eventSubClient.ChannelSubscriptionGift += async (sender, args) => await OnChannelSubscriptionGift(sender, args);
        _eventSubClient.ChannelRaid += async (sender, args) => await OnChannelRaid(sender, args);
        _eventSubClient.ChannelCheer += async (sender, args) => await OnChannelCheer(sender, args);
        _eventSubClient.StreamOnline += async (sender, args) => await OnStreamOnline(sender, args);
        _eventSubClient.StreamOffline += async (sender, args) => await OnStreamOffline(sender, args);
    }

    public async Task ConnectAsync(string clientId, string accessToken, string userId)
    {
        if (_isConnected)
        {
            _logger.LogWarning("TwitchEventSub already connected");
            return;
        }

        _userId = userId;
        _accessToken = accessToken;
        
        _twitchApi.Settings.ClientId = clientId;
        _twitchApi.Settings.AccessToken = accessToken;

        _logger.LogInformation("Connecting to Twitch EventSub WebSocket...");
        await _eventSubClient.ConnectAsync();
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected)
        {
            _logger.LogWarning("TwitchEventSub not connected");
            return;
        }

        _logger.LogInformation("Disconnecting from Twitch EventSub...");
        await _eventSubClient.DisconnectAsync();
        _isConnected = false;
    }

    private async Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        _logger.LogInformation("Twitch EventSub WebSocket connected! SessionId: {SessionId}", _eventSubClient.SessionId);
        _isConnected = true;

        if (!e.IsRequestedReconnect && !string.IsNullOrEmpty(_userId))
        {
            await SubscribeToEventsAsync();
        }
    }

    private async Task SubscribeToEventsAsync()
    {
        try
        {
            var condition = new Dictionary<string, string> 
            { 
                { "broadcaster_user_id", _userId! },
                { "moderator_user_id", _userId! }
            };

            _logger.LogInformation("Subscribing to Twitch EventSub topics...");

            // Subscribe to Follow events
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.follow", "2", 
                new Dictionary<string, string> 
                { 
                    { "broadcaster_user_id", _userId! },
                    { "moderator_user_id", _userId! }
                },
                EventSubTransportMethod.Websocket, 
                _eventSubClient.SessionId, 
                accessToken: _accessToken);
            _logger.LogInformation("✅ Subscribed to channel.follow");

            // Subscribe to Subscribe events
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.subscribe", "1", 
                new Dictionary<string, string> { { "broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket, 
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to channel.subscribe");

            // Subscribe to Subscription Message events (resubs)
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.subscription.message", "1", 
                new Dictionary<string, string> { { "broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket, 
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to channel.subscription.message");

            // Subscribe to Gift Subscription events
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.subscription.gift", "1", 
                new Dictionary<string, string> { { "broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket, 
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to channel.subscription.gift");

            // Subscribe to Raid events
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.raid", "1", 
                new Dictionary<string, string> { { "to_broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket, 
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to channel.raid");

            // Subscribe to Cheer events
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.cheer", "1",
                new Dictionary<string, string> { { "broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to channel.cheer");

            // Subscribe to Stream Online events (channel went live)
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "stream.online", "1",
                new Dictionary<string, string> { { "broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to stream.online");

            // Subscribe to Stream Offline events (channel went offline)
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "stream.offline", "1",
                new Dictionary<string, string> { { "broadcaster_user_id", _userId! } },
                EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId);
            _logger.LogInformation("✅ Subscribed to stream.offline");

            _logger.LogInformation("🎉 Successfully subscribed to all Twitch EventSub topics");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to Twitch EventSub topics");
        }
    }

    private async Task OnWebsocketDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Twitch EventSub WebSocket disconnected");
        _isConnected = false;

        // Attempt reconnection
        _logger.LogInformation("Attempting to reconnect...");
        await Task.Delay(2000);
        
        if (await _eventSubClient.ReconnectAsync())
        {
            _logger.LogInformation("Reconnection successful");
        }
        else
        {
            _logger.LogError("Reconnection failed");
        }
    }

    private Task OnErrorOccurred(object? sender, ErrorOccuredArgs e)
    {
        _logger.LogError("Twitch EventSub error occurred: {Error}", e.Exception?.Message ?? e.Message);
        return Task.CompletedTask;
    }

    // Event Handlers
    private async Task OnChannelFollow(object? sender, ChannelFollowArgs e)
    {
        // Note: EventSub doesn't provide follower count in the event
        // Would need to query Twitch API separately to get current count
        // For now, just increment existing value if present
        try
        {
            var currentVar = await _dataService.GetVariableByNameAsync("twitch-followercount");
            if (currentVar?.Value != null && int.TryParse(currentVar.Value, out var count))
            {
                await _dataService.UpdateVariableValueAsync("twitch-followercount", (count + 1).ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update twitch-followercount variable");
        }

        var evt = new TwitchFollowEvent
        {
            Source = "Twitch",
            Username = e.Notification.Payload.Event.UserName,
            UserId = e.Notification.Payload.Event.UserId,
            DisplayName = e.Notification.Payload.Event.UserName
        };

        _logger.LogInformation("Twitch Follow: {Username}", evt.Username);
        await _eventBus.PublishAsync(evt);
    }

    private async Task OnChannelSubscribe(object? sender, ChannelSubscribeArgs e)
    {
        // Note: EventSub doesn't provide subscriber count in the event
        // Would need to query Twitch API separately to get current count
        // For now, just increment existing value if present
        try
        {
            var currentVar = await _dataService.GetVariableByNameAsync("twitch-subscribercount");
            if (currentVar?.Value != null && int.TryParse(currentVar.Value, out var count))
            {
                await _dataService.UpdateVariableValueAsync("twitch-subscribercount", (count + 1).ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update twitch-subscribercount variable");
        }

        var evt = new TwitchSubscribeEvent
        {
            Source = "Twitch",
            Username = e.Notification.Payload.Event.UserName,
            UserId = e.Notification.Payload.Event.UserId,
            DisplayName = e.Notification.Payload.Event.UserName,
            Tier = e.Notification.Payload.Event.Tier,
            IsGift = e.Notification.Payload.Event.IsGift
        };

        _logger.LogInformation("Twitch Subscribe: {Username} (Tier: {Tier}, Gift: {IsGift})", 
            evt.Username, evt.Tier, evt.IsGift);
        await _eventBus.PublishAsync(evt);
    }

    private async Task OnChannelSubscriptionMessage(object? sender, ChannelSubscriptionMessageArgs e)
    {
        var evt = new TwitchResubEvent
        {
            Source = "Twitch",
            Username = e.Notification.Payload.Event.UserName,
            UserId = e.Notification.Payload.Event.UserId,
            Tier = e.Notification.Payload.Event.Tier,
            CumulativeMonths = e.Notification.Payload.Event.CumulativeMonths,
            StreakMonths = e.Notification.Payload.Event.StreakMonths ?? 0,
            DurationMonths = e.Notification.Payload.Event.DurationMonths,
            Message = e.Notification.Payload.Event.Message.Text,
            ResubscribedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Twitch Resub: {Username} ({Months} months)", evt.Username, evt.CumulativeMonths);
        await _eventBus.PublishAsync(evt);
    }

    private async Task OnChannelSubscriptionGift(object? sender, ChannelSubscriptionGiftArgs e)
    {
        var evt = new TwitchGiftSubEvent
        {
            Source = "Twitch",
            GifterUsername = e.Notification.Payload.Event.UserName ?? "Anonymous",
            GifterUserId = e.Notification.Payload.Event.UserId ?? "anonymous",
            Tier = e.Notification.Payload.Event.Tier,
            Total = e.Notification.Payload.Event.Total,
            CumulativeTotal = e.Notification.Payload.Event.CumulativeTotal ?? 0,
            IsAnonymous = e.Notification.Payload.Event.IsAnonymous,
            GiftedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Twitch Gift Sub: {Gifter} gifted {Total} subs", evt.GifterUsername, evt.Total);
        await _eventBus.PublishAsync(evt);
    }

    private Task OnChannelRaid(object? sender, ChannelRaidArgs e)
    {
        var evt = new TwitchRaidEvent
        {
            Source = "Twitch",
            FromBroadcasterUsername = e.Notification.Payload.Event.FromBroadcasterUserName,
            FromBroadcasterUserId = e.Notification.Payload.Event.FromBroadcasterUserId,
            ToBroadcasterUsername = e.Notification.Payload.Event.ToBroadcasterUserName,
            ToBroadcasterUserId = e.Notification.Payload.Event.ToBroadcasterUserId,
            Viewers = e.Notification.Payload.Event.Viewers,
            RaidedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Twitch Raid: {FromUser} raided with {Viewers} viewers", 
            evt.FromBroadcasterUsername, evt.Viewers);
        return _eventBus.PublishAsync(evt);
    }

    private Task OnChannelCheer(object? sender, ChannelCheerArgs e)
    {
        var evt = new TwitchCheerEvent
        {
            Source = "Twitch",
            Username = e.Notification.Payload.Event.IsAnonymous ? "Anonymous" : e.Notification.Payload.Event.UserName,
            UserId = e.Notification.Payload.Event.UserId ?? "anonymous",
            Bits = e.Notification.Payload.Event.Bits,
            IsAnonymous = e.Notification.Payload.Event.IsAnonymous,
            Message = e.Notification.Payload.Event.Message,
            CheeredAt = DateTime.UtcNow
        };

        _logger.LogInformation("Twitch Cheer: {Username} cheered {Bits} bits", evt.Username, evt.Bits);
        return _eventBus.PublishAsync(evt);
    }

    private async Task OnStreamOnline(object? sender, StreamOnlineArgs e)
    {
        var evt = new TwitchStreamStatusEvent
        {
            Source = "Twitch",
            IsLive = true,
            StartedAtUtc = e.Notification.Payload.Event.StartedAt.UtcDateTime
        };

        _logger.LogInformation("Twitch Stream Online (started at {StartedAt})", evt.StartedAtUtc);
        await _eventBus.PublishAsync(evt);
    }

    private Task OnStreamOffline(object? sender, StreamOfflineArgs e)
    {
        var evt = new TwitchStreamStatusEvent
        {
            Source = "Twitch",
            IsLive = false,
            StartedAtUtc = null
        };

        _logger.LogInformation("Twitch Stream Offline");
        return _eventBus.PublishAsync(evt);
    }

    public void Dispose()
    {
        // Note: Cannot unsubscribe lambda event handlers
        // The EventSubWebsocketClient will be disposed by DI container
        _logger.LogInformation("TwitchEventSubService disposed");
    }
}
