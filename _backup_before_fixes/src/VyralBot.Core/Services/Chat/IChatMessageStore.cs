using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Simple interface for storing recent chat events per platform/channel.
/// Used to persist history that arrives before UI subscribes to the EventBus.
/// </summary>
public interface IChatMessageStore
{
    Task<bool> AddAsync(string platform, string channel, IEvent evt);
    Task<IReadOnlyList<IEvent>> GetRecentAsync(string platform, string channel, int limit = 200);
    Task DeleteAsync(string platform, string channel);
}
