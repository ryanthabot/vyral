using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Kick.Events;

namespace VyralBot.Api.Controllers;

[ApiController]
[Route("webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly ILogger<WebhooksController> _logger;
    private readonly IEventBus _eventBus;

    public WebhooksController(ILogger<WebhooksController> logger, IEventBus eventBus)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    [HttpPost("kick")]
    public async Task<IActionResult> KickWebhook()
    {
        // Note: Real-time chat messages are received via Pusher WebSocket in KickService.
        // Webhooks are used for platform events (follows, subscriptions, raids, etc.)

        try
        {
            // Read request body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // Extract headers
            var eventType = Request.Headers["Kick-Event-Type"].FirstOrDefault();
            var eventVersion = Request.Headers["Kick-Event-Version"].FirstOrDefault();
            var signature = Request.Headers["Kick-Event-Signature"].FirstOrDefault();
            var messageId = Request.Headers["Kick-Event-Message-Id"].FirstOrDefault();
            var timestamp = Request.Headers["Kick-Event-Message-Timestamp"].FirstOrDefault();

            _logger.LogInformation("Received Kick webhook: Type={EventType}, Version={EventVersion}, MessageId={MessageId}",
                eventType, eventVersion, messageId);

            // Validate required headers
            if (string.IsNullOrEmpty(eventType))
            {
                _logger.LogWarning("Missing Kick-Event-Type header");
                return BadRequest("Missing Kick-Event-Type header");
            }

            // TODO: Validate signature (will be implemented in middleware)

            // Parse and process based on event type
            var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            switch (eventType)
            {
                case "chat.message.sent":
                    // No longer processing chat messages via webhooks - handled by Pusher WebSocket
                    _logger.LogInformation("Received chat.message.sent webhook (ignoring - using Pusher WebSocket)");
                    break;

                case "channel.followed":
                    _logger.LogInformation("Received follow event - processing and publishing to EventBus");
                    await ProcessFollowAsync(root, messageId, timestamp);
                    break;

                case "channel.unfollowed":
                case "channel.unfollow":
                    _logger.LogInformation("Received unfollow event - processing and publishing to EventBus");
                    await ProcessUnfollowAsync(root, messageId, timestamp);
                    break;

                case "channel.subscription.new":
                case "channel.subscription.renewal":
                    _logger.LogInformation("Received subscription event - processing and publishing to EventBus");
                    await ProcessSubscriptionAsync(root, messageId, timestamp, eventType);
                    break;

                case "channel.subscription.gifts":
                    _logger.LogInformation("Received subscription gifts event - processing and publishing to EventBus");
                    await ProcessSubscriptionGiftAsync(root, messageId, timestamp);
                    break;

                case "livestream.status.updated":
                    _logger.LogInformation("Received livestream status event - processing and publishing to EventBus");
                    await ProcessLivestreamStatusAsync(root, messageId, timestamp);
                    break;

                case "livestream.metadata.updated":
                    // Subscribed by WebhookCoordinatorService (KickLib LivestreamMetadataUpdated),
                    // but there is no in-app consumer yet - acknowledge so logs are honest
                    // and Kick receives a 200 instead of this falling into the unknown bucket.
                    _logger.LogInformation("Received livestream.metadata.updated webhook - metadata update ignored (no consumer)");
                    break;

                case "moderation.banned":
                    _logger.LogInformation("Received moderation event - processing and publishing to EventBus");
                    await ProcessModerationBanAsync(root, messageId, timestamp);
                    break;

                case "kicks.gifted":
                    _logger.LogInformation("Received gift event - processing and publishing to EventBus");
                    await ProcessGiftAsync(root, messageId, timestamp);
                    break;

                default:
                    _logger.LogWarning("Unknown event type: {EventType}", eventType);
                    break;
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Kick webhook");
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task ProcessFollowAsync(JsonElement root, string? webhookMessageId, string? timestamp)
    {
        try
        {
            // Attempt to find follower info in common fields
            JsonElement followerEl;
            bool found = false;

            if (root.TryGetProperty("follower", out followerEl)) found = true;
            else if (root.TryGetProperty("user", out followerEl)) found = true;
            else if (root.TryGetProperty("sender", out followerEl)) found = true;
            else if (root.TryGetProperty("actor", out followerEl)) found = true;

            string followerUsername = "Unknown";
            string followerId = "0";
            string displayName = "Unknown";

            if (found)
            {
                if (followerEl.ValueKind == JsonValueKind.Object)
                {
                    if (followerEl.TryGetProperty("username", out var u)) followerUsername = u.GetString() ?? followerUsername;
                    if (followerEl.TryGetProperty("slug", out var s)) displayName = s.GetString() ?? followerUsername;
                    if (followerEl.TryGetProperty("id", out var idp)) followerId = idp.ToString() ?? followerId;
                    if (displayName == "Unknown") displayName = followerUsername;
                }
                else if (followerEl.ValueKind == JsonValueKind.String)
                {
                    followerUsername = followerEl.GetString() ?? followerUsername;
                    displayName = followerUsername;
                }
            }

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            // Publish dedicated KickFollowEvent (not chat message) to maintain proper event typing
            // This prevents duplicate auto-responses and ensures consistent username display
            var followEvent = new KickFollowEvent
            {
                Source = "Kick",
                Username = followerUsername,
                UserId = followerId,
                FollowerCount = 0, // Follower count not provided in webhook payload
                FollowedAt = createdAt ?? DateTime.UtcNow
            };

            _logger.LogInformation("📤 Publishing KickFollowEvent: {Username} (ID: {UserId})", followerUsername, followerId);
            await _eventBus.PublishAsync(followEvent);
            _logger.LogInformation("✅ Follow event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing follow webhook");
        }
    }

    private async Task ProcessUnfollowAsync(JsonElement root, string? webhookMessageId, string? timestamp)
    {
        try
        {
            // Attempt to find unfollower info in common fields
            JsonElement userEl;
            bool found = false;

            if (root.TryGetProperty("user", out userEl)) found = true;
            else if (root.TryGetProperty("follower", out userEl)) found = true;
            else if (root.TryGetProperty("sender", out userEl)) found = true;
            else if (root.TryGetProperty("actor", out userEl)) found = true;

            string username = "Unknown";
            string userId = "0";

            if (found)
            {
                if (userEl.ValueKind == JsonValueKind.Object)
                {
                    if (userEl.TryGetProperty("username", out var u)) username = u.GetString() ?? username;
                    if (userEl.TryGetProperty("id", out var idp)) userId = idp.ToString() ?? userId;
                }
                else if (userEl.ValueKind == JsonValueKind.String)
                {
                    username = userEl.GetString() ?? username;
                }
            }

            var messageText = $"{username} unfollowed the channel";

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            string channel = "Unknown";
            if (root.TryGetProperty("channel_slug", out var ch)) channel = ch.GetString() ?? channel;
            else if (root.TryGetProperty("channel", out var ch2) && ch2.ValueKind == JsonValueKind.String) channel = ch2.GetString() ?? channel;

            var kickEvent = new KickChatMessageEvent
            {
                Source = "Kick",
                Channel = channel,
                Username = "Vyral.Bot",
                UserId = userId,
                Message = messageText,
                DisplayName = "Vyral.Bot",
                Color = "#AAAAAA",
                IsSubscriber = false,
                IsModerator = false,
                IsOwner = false,
                IsVerified = false,
                MessageId = webhookMessageId,
                CreatedAt = createdAt,
                Badges = new Dictionary<string, string>
                {
                    { "vyral", "1" }
                }
            };

            _logger.LogInformation("📤 Publishing unfollow as KickChatMessageEvent: {Message}", messageText);
            await _eventBus.PublishAsync(kickEvent);
            _logger.LogInformation("✅ Unfollow event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing unfollow webhook");
        }
    }

    private async Task ProcessSubscriptionAsync(JsonElement root, string? webhookMessageId, string? timestamp, string eventType)
    {
        try
        {
            // Attempt to find subscriber info
            JsonElement subEl;
            bool found = false;

            if (root.TryGetProperty("subscriber", out subEl)) found = true;
            else if (root.TryGetProperty("user", out subEl)) found = true;
            else if (root.TryGetProperty("sender", out subEl)) found = true;
            else if (root.TryGetProperty("actor", out subEl)) found = true;

            string subscriberUsername = "Unknown";
            string subscriberId = "0";

            if (found)
            {
                if (subEl.ValueKind == JsonValueKind.Object)
                {
                    if (subEl.TryGetProperty("username", out var u)) subscriberUsername = u.GetString() ?? subscriberUsername;
                    if (subEl.TryGetProperty("id", out var idp)) subscriberId = idp.ToString() ?? subscriberId;
                }
                else if (subEl.ValueKind == JsonValueKind.String)
                {
                    subscriberUsername = subEl.GetString() ?? subscriberUsername;
                }
            }

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            // Publish dedicated KickSubscribeEvent to maintain proper event typing
            var subscribeEvent = new KickSubscribeEvent
            {
                Source = "Kick",
                Username = subscriberUsername,
                UserId = subscriberId,
                MonthsSubscribed = 1, // Webhook doesn't provide months count for initial sub
                SubscribedAt = createdAt ?? DateTime.UtcNow,
                Message = null
            };

            _logger.LogInformation("📤 Publishing KickSubscribeEvent: {Username}", subscriberUsername);
            await _eventBus.PublishAsync(subscribeEvent);
            _logger.LogInformation("✅ Subscription event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing subscription webhook");
        }
    }

    private async Task ProcessSubscriptionGiftAsync(JsonElement root, string? webhookMessageId, string? timestamp)
    {
        try
        {
            // Attempt to find gifter and amount
            string gifter = "Unknown";
            int amount = 1;

            if (root.TryGetProperty("gifter", out var gifterEl))
            {
                if (gifterEl.ValueKind == JsonValueKind.Object && gifterEl.TryGetProperty("username", out var u)) gifter = u.GetString() ?? gifter;
                else if (gifterEl.ValueKind == JsonValueKind.String) gifter = gifterEl.GetString() ?? gifter;
            }

            if (root.TryGetProperty("amount", out var amountEl) && amountEl.TryGetInt32(out var amt)) amount = amt;

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            // Publish dedicated KickGiftSubEvent to maintain proper event typing
            var giftSubEvent = new KickGiftSubEvent
            {
                Source = "Kick",
                GifterUsername = gifter,
                GifterUserId = "0", // Webhook doesn't provide gifter ID
                Count = amount,
                RecipientUsernames = new List<string>(), // Webhook doesn't provide recipient list
                GiftedAt = createdAt ?? DateTime.UtcNow
            };

            _logger.LogInformation("📤 Publishing KickGiftSubEvent: {Gifter} gifted {Amount} subs", gifter, amount);
            await _eventBus.PublishAsync(giftSubEvent);
            _logger.LogInformation("✅ Gift sub event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing subscription gift webhook");
        }
    }

    private async Task ProcessLivestreamStatusAsync(JsonElement root, string? webhookMessageId, string? timestamp)
    {
        try
        {
            // Try to extract useful info such as "is_live" or title
            string statusText = "Stream status updated";
            if (root.TryGetProperty("is_live", out var liveProp) && liveProp.ValueKind == JsonValueKind.True)
            {
                statusText = "Stream went live";
            }
            else if (root.TryGetProperty("is_live", out liveProp) && liveProp.ValueKind == JsonValueKind.False)
            {
                statusText = "Stream went offline";
            }

            if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
            {
                var title = titleProp.GetString();
                if (!string.IsNullOrEmpty(title)) statusText += $": {title}";
            }

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            string channel = "Unknown";
            if (root.TryGetProperty("channel_slug", out var ch)) channel = ch.GetString() ?? channel;

            var kickEvent = new KickChatMessageEvent
            {
                Source = "Kick",
                Channel = channel,
                Username = "Vyral.Bot",
                UserId = "0",
                Message = statusText,
                DisplayName = "Vyral.Bot",
                Color = "#FFFFFF",
                IsSubscriber = false,
                IsModerator = false,
                IsOwner = false,
                IsVerified = false,
                MessageId = webhookMessageId,
                CreatedAt = createdAt
                ,
                Badges = new Dictionary<string, string>
                {
                    { "vyral", "1" }
                }
            };

            _logger.LogInformation("📤 Publishing livestream status as KickChatMessageEvent: {Message}", statusText);
            await _eventBus.PublishAsync(kickEvent);
            _logger.LogInformation("✅ Livestream status event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing livestream status webhook");
        }
    }

    private async Task ProcessModerationBanAsync(JsonElement root, string? webhookMessageId, string? timestamp)
    {
        try
        {
            string target = "Unknown";
            string moderator = "Unknown";

            if (root.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                if (t.TryGetProperty("username", out var u)) target = u.GetString() ?? target;
            }

            if (root.TryGetProperty("moderator", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                if (m.TryGetProperty("username", out var mu)) moderator = mu.GetString() ?? moderator;
            }

            var messageText = $"{target} was banned by {moderator}";

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            string channel = "Unknown";
            if (root.TryGetProperty("channel_slug", out var ch)) channel = ch.GetString() ?? channel;

            var kickEvent = new KickChatMessageEvent
            {
                Source = "Kick",
                Channel = channel,
                Username = "Vyral.Bot",
                UserId = "0",
                Message = messageText,
                DisplayName = "Vyral.Bot",
                Color = "#FF5555",
                IsSubscriber = false,
                IsModerator = false,
                IsOwner = false,
                IsVerified = false,
                MessageId = webhookMessageId,
                CreatedAt = createdAt
                ,
                Badges = new Dictionary<string, string>
                {
                    { "vyral", "1" }
                }
            };

            _logger.LogInformation("📤 Publishing moderation ban as KickChatMessageEvent: {Message}", messageText);
            await _eventBus.PublishAsync(kickEvent);
            _logger.LogInformation("✅ Moderation ban event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing moderation ban webhook");
        }
    }

    private async Task ProcessGiftAsync(JsonElement root, string? webhookMessageId, string? timestamp)
    {
        try
        {
            string giver = "Unknown";
            int amount = 1;

            if (root.TryGetProperty("giver", out var g) && g.ValueKind == JsonValueKind.Object)
            {
                if (g.TryGetProperty("username", out var gu)) giver = gu.GetString() ?? giver;
            }

            if (root.TryGetProperty("amount", out var amountEl) && amountEl.TryGetInt32(out var a)) amount = a;

            var messageText = $"{giver} gifted {amount} item(s)";

            DateTime? createdAt = null;
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var parsed)) createdAt = parsed;

            string channel = "Unknown";
            if (root.TryGetProperty("channel_slug", out var ch)) channel = ch.GetString() ?? channel;

            var kickEvent = new KickChatMessageEvent
            {
                Source = "Kick",
                Channel = channel,
                Username = "Vyral.Bot",
                UserId = "0",
                Message = messageText,
                DisplayName = "Vyral.Bot",
                Color = "#FFFFFF",
                IsSubscriber = false,
                IsModerator = false,
                IsOwner = false,
                IsVerified = false,
                MessageId = webhookMessageId,
                CreatedAt = createdAt
                ,
                Badges = new Dictionary<string, string>
                {
                    { "vyral", "1" }
                }
            };

            _logger.LogInformation("📤 Publishing gift as KickChatMessageEvent: {Message}", messageText);
            await _eventBus.PublishAsync(kickEvent);
            _logger.LogInformation("✅ Gift event published to EventBus");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing gift webhook");
        }
    }

    private async Task ProcessChatMessage(JsonElement root)
    {
        try
        {
            // Extract message_id and created_at
            var messageId = root.TryGetProperty("message_id", out var msgIdProp) ? msgIdProp.GetString() : null;
            DateTime? createdAt = null;
            if (root.TryGetProperty("created_at", out var createdAtProp))
            {
                var createdAtStr = createdAtProp.GetString();
                if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var parsedDate))
                {
                    createdAt = parsedDate;
                }
            }

            // Extract sender information
            if (!root.TryGetProperty("sender", out var senderProp))
            {
                _logger.LogWarning("Chat message missing sender property");
                return;
            }

            var senderId = senderProp.TryGetProperty("user_id", out var idProp) ? idProp.GetInt32().ToString() : "0";
            var username = senderProp.TryGetProperty("username", out var userProp) ? userProp.GetString() ?? "Unknown" : "Unknown";
            var displayName = senderProp.TryGetProperty("slug", out var slugProp) ? slugProp.GetString() ?? username : username;
            var isVerified = senderProp.TryGetProperty("is_verified", out var verifiedProp) && verifiedProp.GetBoolean();
            var profilePicture = senderProp.TryGetProperty("profile_picture", out var profilePicProp) ? profilePicProp.GetString() : null;
            var channelSlug = senderProp.TryGetProperty("channel_slug", out var channelSlugProp) ? channelSlugProp.GetString() : null;

            // Extract message content
            var message = root.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? "" : "";
            var channel = channelSlug ?? "Unknown";

            // Extract identity (color and badges)
            string? color = null;
            var badges = new Dictionary<string, string>();
            var isSubscriber = false;
            var isModerator = false;
            var isOwner = false;
            int? subscriberMonths = null;

            if (senderProp.TryGetProperty("identity", out var identityProp))
            {
                // Extract username color
                color = identityProp.TryGetProperty("username_color", out var colorProp) ? colorProp.GetString() : null;

                // Extract badges
                if (identityProp.TryGetProperty("badges", out var badgesProp) && badgesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var badge in badgesProp.EnumerateArray())
                    {
                        var type = badge.TryGetProperty("type", out var badgeType) ? badgeType.GetString() ?? string.Empty : string.Empty;

                        switch (type)
                        {
                            case "subscriber":
                                isSubscriber = true;
                                if (badge.TryGetProperty("count", out var countProp))
                                {
                                    subscriberMonths = countProp.GetInt32();
                                    badges["subscriber"] = subscriberMonths.Value.ToString();
                                }
                                else
                                {
                                    badges["subscriber"] = "1";
                                }
                                break;

                            case "moderator":
                                isModerator = true;
                                badges["moderator"] = "1";
                                break;

                            case "owner":
                                isOwner = true;
                                badges["broadcaster"] = "1";
                                break;

                            case "verified":
                                badges["verified"] = "1";
                                break;

                            case "sub_gifter":
                                badges["sub-gifter"] = "1";
                                break;
                        }
                    }
                }
            }

            // Extract emotes
            var emotes = new List<KickEmote>();
            if (root.TryGetProperty("emotes", out var emotesProp) && emotesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var emote in emotesProp.EnumerateArray())
                {
                    var emoteId = emote.TryGetProperty("emote_id", out var emoteIdProp) ? emoteIdProp.GetString() : null;
                    if (string.IsNullOrEmpty(emoteId)) continue;

                    var positions = new List<EmotePosition>();
                    if (emote.TryGetProperty("positions", out var positionsProp) && positionsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var position in positionsProp.EnumerateArray())
                        {
                            var start = position.TryGetProperty("start", out var startProp) ? startProp.GetInt32() : 0;
                            var end = position.TryGetProperty("end", out var endProp) ? endProp.GetInt32() : 0;
                            positions.Add(new EmotePosition { Start = start, End = end });
                        }
                    }

                    emotes.Add(new KickEmote { EmoteId = emoteId, Positions = positions });
                }
            }

            // Extract reply information
            KickReplyInfo? replyInfo = null;
            if (root.TryGetProperty("replies_to", out var replyToProp) && replyToProp.ValueKind != JsonValueKind.Null)
            {
                var replyMessageId = replyToProp.TryGetProperty("message_id", out var replyMsgIdProp) ? replyMsgIdProp.GetString() : null;
                var replyContent = replyToProp.TryGetProperty("content", out var replyContentProp) ? replyContentProp.GetString() : null;
                var replyUsername = replyToProp.TryGetProperty("username", out var replyUserProp) ? replyUserProp.GetString() : null;
                var replyUserId = replyToProp.TryGetProperty("user_id", out var replyUserIdProp) ? replyUserIdProp.GetInt32().ToString() : null;

                if (!string.IsNullOrEmpty(replyMessageId) && !string.IsNullOrEmpty(replyContent) &&
                    !string.IsNullOrEmpty(replyUsername) && !string.IsNullOrEmpty(replyUserId))
                {
                    replyInfo = new KickReplyInfo
                    {
                        MessageId = replyMessageId,
                        Content = replyContent,
                        Username = replyUsername,
                        UserId = replyUserId
                    };
                }
            }

            // Create and publish event
            var kickEvent = new KickChatMessageEvent
            {
                Source = "Kick",
                Channel = channel,
                Username = username,
                UserId = senderId,
                Message = message,
                DisplayName = displayName,
                Color = color,
                IsSubscriber = isSubscriber,
                IsModerator = isModerator,
                IsOwner = isOwner,
                IsVerified = isVerified,
                SubscriberMonths = subscriberMonths,
                Badges = badges,
                MessageId = messageId,
                ProfilePicture = profilePicture,
                ChannelSlug = channelSlug,
                CreatedAt = createdAt,
                Emotes = emotes,
                ReplyTo = replyInfo
            };

            _logger.LogInformation("📤 Publishing KickChatMessageEvent to EventBus...");
            _logger.LogInformation("   Event details: Username={Username}, Message={Message}", username, message);

            await _eventBus.PublishAsync(kickEvent);

            _logger.LogInformation("✅ KickChatMessageEvent published successfully!");
            _logger.LogInformation("   🎯 ChatViewModel should receive this event now (shared EventBus)");
            _logger.LogInformation("Published Kick chat message event from {Username}", username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Kick chat message");
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "webhooks"
        });
    }
}
