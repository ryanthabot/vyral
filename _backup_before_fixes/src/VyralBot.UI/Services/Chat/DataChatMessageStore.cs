using VyralBot.Core.Services.Chat;
using VyralBot.Data;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.Services.Chat;

/// <summary>
/// Adapter that implements IChatMessageStore using IDataService (Dapper) for persistence.
/// Maps DB rows to platform-specific chat events for compatibility with EventBus consumers.
/// </summary>
public class DataChatMessageStore : IChatMessageStore
{
    private readonly IDataService _dataService;

    public DataChatMessageStore(IDataService dataService)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task<bool> AddAsync(string platform, string channel, IEvent evt)
    {
        if (evt is KickChatMessageEvent k)
        {
            var entity = new VyralBot.Data.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                Platform = platform,
                Channel = channel,
                MessageId = k.MessageId,
                Username = k.Username,
                DisplayName = k.DisplayName,
                Content = k.Message,
                Timestamp = k.CreatedAt ?? k.Timestamp,
                Color = k.Color,
                IsModerator = k.IsModerator ? 1 : 0,
                IsSubscriber = k.IsSubscriber ? 1 : 0,
                IsOwner = k.IsOwner ? 1 : 0,
                BadgesJson = System.Text.Json.JsonSerializer.Serialize(k.Badges ?? new Dictionary<string,string>()),
                CreatedAt = DateTime.UtcNow
            };
            return await _dataService.SaveChatMessageAsync(entity);
        }
        else if (evt is TwitchChatMessageEvent t)
        {
            var entity = new VyralBot.Data.Entities.ChatMessage
            {
                Id = Guid.NewGuid(),
                Platform = platform,
                Channel = channel,
                MessageId = null, // Twitch IRC doesn't provide message IDs
                UserId = t.UserId,
                Username = t.Username,
                DisplayName = t.DisplayName,
                Content = t.Message,
                Timestamp = t.Timestamp,
                Color = t.Color,
                IsModerator = t.IsModerator ? 1 : 0,
                IsSubscriber = t.IsSubscriber ? 1 : 0,
                IsOwner = t.IsBroadcaster ? 1 : 0,
                BadgesJson = System.Text.Json.JsonSerializer.Serialize(t.Badges ?? new Dictionary<string,string>()),
                CreatedAt = DateTime.UtcNow
            };
            return await _dataService.SaveChatMessageAsync(entity);
        }
        else
        {
            // Unsupported event type
            return false;
        }
    }

    public async Task<IReadOnlyList<IEvent>> GetRecentAsync(string platform, string channel, int limit = 200)
    {
        var rows = await _dataService.GetChatMessagesAsync(platform, channel, limit);
        var list = new List<IEvent>();
        foreach (var r in rows)
        {
            var badges = new Dictionary<string,string>();
            try
            {
                if (!string.IsNullOrEmpty(r.BadgesJson))
                    badges = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(r.BadgesJson) ?? new();
            }
            catch { }

            // Return platform-specific event type
            if (string.Equals(platform, "Twitch", StringComparison.OrdinalIgnoreCase))
            {
                var evt = new TwitchChatMessageEvent
                {
                    Source = "Twitch",
                    Channel = r.Channel ?? channel,
                    Username = r.Username ?? "",
                    UserId = r.UserId ?? "",
                    Message = r.Content ?? "",
                    DisplayName = r.DisplayName ?? r.Username ?? "",
                    Color = r.Color,
                    IsSubscriber = r.IsSubscriber == 1,
                    IsModerator = r.IsModerator == 1,
                    IsBroadcaster = r.IsOwner == 1,
                    IsVip = false,
                    Badges = badges
                };
                list.Add(evt);
            }
            else // Kick or others
            {
                var evt = new KickChatMessageEvent
                {
                    Source = "Kick",
                    Channel = r.Channel ?? channel,
                    Username = r.Username ?? "",
                    UserId = r.UserId ?? "",
                    Message = r.Content ?? "",
                    DisplayName = r.DisplayName ?? r.Username ?? "",
                    Color = r.Color,
                    IsSubscriber = r.IsSubscriber == 1,
                    IsModerator = r.IsModerator == 1,
                    IsOwner = r.IsOwner == 1,
                    Badges = badges,
                    MessageId = r.MessageId,
                    CreatedAt = r.Timestamp
                };
                list.Add(evt);
            }
        }

        return list.AsReadOnly();
    }

    public Task DeleteAsync(string platform, string channel)
    {
        return _dataService.DeleteChatMessagesAsync(platform, channel);
    }
}
