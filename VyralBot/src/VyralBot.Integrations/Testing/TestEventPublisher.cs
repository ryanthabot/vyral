using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Integrations.Discord.Events;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.YouTube.Events;

namespace VyralBot.Integrations.Testing;

/// <summary>
/// Publishes canned test events to the <see cref="IEventBus"/> so handlers, UI,
/// auto-replies and alerts can be exercised without a live platform connection.
/// Extracted from <c>EventTestingViewModel</c> so the Event Testing page and the
/// token-gated HTTP control API (<c>POST /api/events/test</c>) fire the exact
/// same payloads — one source of truth for what a "test event" looks like.
///
/// Lives in VyralBot.Integrations (not Core/UI) because only this project can
/// reference every platform's event types.
/// </summary>
public sealed class TestEventPublisher
{
    public const string KickFollow = "kick.follow";
    public const string KickSubscribe = "kick.subscribe";
    public const string KickGiftSub = "kick.giftsub";
    public const string KickHost = "kick.host";
    public const string KickKicksGifted = "kick.kicksgifted";
    public const string KickRewardRedeemed = "kick.rewardredeemed";
    public const string TwitchFollow = "twitch.follow";
    public const string TwitchSubscribe = "twitch.subscribe";
    public const string TwitchResub = "twitch.resub";
    public const string TwitchGiftSub = "twitch.giftsub";
    public const string TwitchRaid = "twitch.raid";
    public const string TwitchCheer = "twitch.cheer";
    public const string TwitchChannelPointsRedeem = "twitch.channelpointsredeem";
    public const string YouTubeSuperChat = "youtube.superchat";
    public const string YouTubeMembership = "youtube.membership";
    public const string DiscordMemberJoined = "discord.memberjoined";
    public const string DiscordBoost = "discord.boost";
    public const string FirstMessage = "firstmessage.fire";

    /// <summary>All event type ids accepted by <see cref="PublishTestEventAsync"/>.</summary>
    public static readonly IReadOnlyList<string> SupportedEventTypes = new[]
    {
        KickFollow, KickSubscribe, KickGiftSub, KickHost, KickKicksGifted, KickRewardRedeemed,
        TwitchFollow, TwitchSubscribe, TwitchResub, TwitchGiftSub, TwitchRaid, TwitchCheer,
        TwitchChannelPointsRedeem, YouTubeSuperChat, YouTubeMembership,
        DiscordMemberJoined, DiscordBoost, FirstMessage
    };

    public const string DefaultUsername = "TestUser123";
    public const string DefaultMessage = "This is a test message";

    private readonly IEventBus _eventBus;
    private readonly ILogger<TestEventPublisher>? _logger;

    public TestEventPublisher(IEventBus eventBus, ILogger<TestEventPublisher>? logger = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger;
    }

    /// <summary>
    /// Publishes the named test event. Payload values match the Event Testing
    /// page defaults. Unknown type ids throw <see cref="ArgumentException"/>.
    /// </summary>
    public Task PublishTestEventAsync(string eventType, string? username = null, string? message = null)
    {
        var user = string.IsNullOrWhiteSpace(username) ? DefaultUsername : username.Trim();
        var msg = string.IsNullOrWhiteSpace(message) ? DefaultMessage : message.Trim();
        var now = DateTime.UtcNow;

        switch (eventType)
        {
            case KickFollow:
                return _eventBus.PublishAsync(new KickFollowEvent
                {
                    Source = "Kick",
                    Username = user,
                    UserId = "12345",
                    FollowerCount = 42,
                    Timestamp = now
                });

            case KickSubscribe:
                return _eventBus.PublishAsync(new KickSubscribeEvent
                {
                    Source = "Kick",
                    Username = user,
                    UserId = "12345",
                    MonthsSubscribed = 3,
                    Message = msg,
                    Timestamp = now
                });

            case KickGiftSub:
                return _eventBus.PublishAsync(new KickGiftSubEvent
                {
                    Source = "Kick",
                    GifterUsername = user,
                    GifterUserId = "12345",
                    Count = 5,
                    RecipientUsernames = new List<string> { "Recipient1", "Recipient2", "Recipient3", "Recipient4", "Recipient5" },
                    Timestamp = now
                });

            case KickHost:
                return _eventBus.PublishAsync(new KickHostEvent
                {
                    Source = "Kick",
                    HostUsername = user,
                    HostUserId = "12345",
                    ViewerCount = 150,
                    HostedAt = now,
                    Timestamp = now
                });

            case KickKicksGifted:
                return _eventBus.PublishAsync(new KickKicksGiftedEvent
                {
                    Source = "Kick",
                    SenderUsername = user,
                    SenderUserId = "12345",
                    Amount = 1000,
                    Message = msg,
                    Timestamp = now
                });

            case KickRewardRedeemed:
                return _eventBus.PublishAsync(new KickRewardRedeemedEvent
                {
                    Source = "Kick",
                    Username = user,
                    UserId = "12345",
                    RewardName = "Test Reward",
                    Cost = 500,
                    UserInput = msg,
                    Timestamp = now
                });

            case TwitchFollow:
                return _eventBus.PublishAsync(new TwitchFollowEvent
                {
                    Source = "Twitch",
                    Username = user,
                    UserId = "12345",
                    DisplayName = user,
                    Timestamp = now
                });

            case TwitchSubscribe:
                return _eventBus.PublishAsync(new TwitchSubscribeEvent
                {
                    Source = "Twitch",
                    Username = user,
                    UserId = "12345",
                    DisplayName = user,
                    Tier = "1000",
                    IsGift = false,
                    MonthsTotal = 1,
                    StreakMonths = 1,
                    Timestamp = now
                });

            case TwitchResub:
                return _eventBus.PublishAsync(new TwitchResubEvent
                {
                    Source = "Twitch",
                    Username = user,
                    UserId = "12345",
                    Tier = "1000",
                    CumulativeMonths = 6,
                    StreakMonths = 4,
                    DurationMonths = 1,
                    Message = msg,
                    ResubscribedAt = now,
                    Timestamp = now
                });

            case TwitchGiftSub:
                return _eventBus.PublishAsync(new TwitchGiftSubEvent
                {
                    Source = "Twitch",
                    GifterUsername = user,
                    GifterUserId = "12345",
                    Tier = "1000",
                    Total = 5,
                    CumulativeTotal = 25,
                    IsAnonymous = false,
                    GiftedAt = now,
                    Timestamp = now
                });

            case TwitchRaid:
                return _eventBus.PublishAsync(new TwitchRaidEvent
                {
                    Source = "Twitch",
                    FromBroadcasterUsername = user,
                    FromBroadcasterUserId = "12345",
                    ToBroadcasterUsername = "YourChannel",
                    ToBroadcasterUserId = "67890",
                    Viewers = 200,
                    RaidedAt = now,
                    Timestamp = now
                });

            case TwitchCheer:
                return _eventBus.PublishAsync(new TwitchCheerEvent
                {
                    Source = "Twitch",
                    Username = user,
                    UserId = "12345",
                    Bits = 100,
                    Message = msg,
                    IsAnonymous = false,
                    CheeredAt = now,
                    Timestamp = now
                });

            // Channel-points redemption — mirrors the TwitchEventSubService
            // payload shape. ChatViewModel plays the "redemption" sound, shows
            // the "Reward Redeemed!" alert and writes a Twitch/RewardRedeemed
            // statistics row; WorkflowTriggerService dispatches the canonical
            // "rewardredeemed" key (shared with Kick redemptions).
            case TwitchChannelPointsRedeem:
                return _eventBus.PublishAsync(new TwitchChannelPointsRedeemEvent
                {
                    Source = "Twitch",
                    Username = user,
                    UserId = "12345",
                    RewardId = "test-reward-0001",
                    RewardTitle = "Hydrate Reminder",
                    RewardCost = 500,
                    UserInput = msg,
                    Status = "unfulfilled",
                    RedeemedAt = now
                });

            // Super Chat ($5.00) — YouTube's cheer equivalent. Sound key
            // "cheer"/YouTube, currency bonus of AmountMicros/10_000 cents
            // (500), YouTube/Cheer statistics row and the dedicated
            // "superchat" workflow key (separate from Twitch bits' "cheer").
            case YouTubeSuperChat:
                return _eventBus.PublishAsync(new YouTubeSuperChatEvent
                {
                    Source = "YouTube",
                    Username = user,
                    UserId = "12345",
                    AmountMicros = 5_000_000, // $5.00 in YouTube "amountMicros"
                    AmountDisplayString = "$5.00",
                    Currency = "USD",
                    Message = msg,
                    IsSuperSticker = false,
                    CheeredAt = now
                });

            // New membership — YouTube's subscribe equivalent. "New
            // Subscriber!" alert, viewer-status subscription record and a
            // YouTube/Subscribe statistics row. WorkflowTriggerService does
            // not subscribe to membership records in V1, so no workflow key
            // dispatches for this one.
            case YouTubeMembership:
                return _eventBus.PublishAsync(new YouTubeMembershipEvent
                {
                    Source = "YouTube",
                    Username = user,
                    UserId = "12345",
                    Level = "Tier 1",
                    IsMilestone = false
                });

            // Guild member join — Discord's "new follower" moment.
            // "follow"/Discord sound, "New Member!" alert, Discord/MemberJoined
            // statistics row and the "discordmemberjoined" workflow key.
            case DiscordMemberJoined:
                return _eventBus.PublishAsync(new DiscordMemberJoinedEvent
                {
                    Source = "Discord",
                    BotId = "primary",
                    GuildId = "123456789012345678",
                    GuildName = "Vyral Test Server",
                    Username = user,
                    DisplayName = user,
                    UserId = "12345"
                });

            // Server boost — Discord's paid-support celebration.
            // "subscribe"/Discord sound, "Server Boosted!" alert,
            // Discord/Boost statistics row and the "discordboost" workflow key.
            case DiscordBoost:
                return _eventBus.PublishAsync(new DiscordBoostEvent
                {
                    Source = "Discord",
                    BotId = "primary",
                    GuildId = "123456789012345678",
                    GuildName = "Vyral Test Server",
                    Username = user,
                    DisplayName = user,
                    UserId = "12345",
                    PremiumSince = DateTimeOffset.UtcNow
                });

            // First-time chatter (welcome-newcomer retention). Published
            // directly as FirstTimeChatterEvent (Twitch example payload — the
            // live FirstTimeChatterService publishes it for whichever platform
            // the newcomer chatted on), so welcome alerts/replies and the
            // "firstmessage" workflow key fire without any database state
            // (detection is bypassed by construction; the event is published,
            // not detected).
            case FirstMessage:
                return _eventBus.PublishAsync(new FirstTimeChatterEvent
                {
                    Source = "Twitch",
                    Platform = "Twitch",
                    Username = user,
                    Message = msg,
                    DisplayName = user
                });

            default:
                _logger?.LogWarning("Unknown test event type requested: {EventType}", eventType);
                throw new ArgumentException($"Unknown test event type '{eventType}'.", nameof(eventType));
        }
    }
}
