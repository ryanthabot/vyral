using System.Collections.Concurrent;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Thread-safe in-memory bounded store for recent chat events.
/// Keeps messages per platform+channel and returns them in insertion order (oldest->newest).
/// </summary>
public class InMemoryChatMessageStore : IChatMessageStore
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<IEvent>> _store = new();
    private readonly int _maxPerKey;

    public InMemoryChatMessageStore(int maxPerKey = 500)
    {
        _maxPerKey = maxPerKey;
    }

    private string Key(string platform, string channel) => $"{platform}:{channel}".ToLowerInvariant();

    public Task<bool> AddAsync(string platform, string channel, IEvent evt)
    {
        if (platform == null) throw new ArgumentNullException(nameof(platform));
        if (channel == null) throw new ArgumentNullException(nameof(channel));
        if (evt == null) throw new ArgumentNullException(nameof(evt));

        var q = _store.GetOrAdd(Key(platform, channel), _ => new ConcurrentQueue<IEvent>());

        // Attempt deduplication based on a 'MessageId' property if present (reflection to avoid project coupling)
        try
        {
            var msgIdProp = evt.GetType().GetProperty("MessageId");
            if (msgIdProp != null)
            {
                var idVal = msgIdProp.GetValue(evt) as string;
                if (!string.IsNullOrEmpty(idVal))
                {
                    // Enumerate the queue directly (thread-safe, no snapshot
                    // allocation) — AddAsync runs per chat message.
                    foreach (var existing in q)
                    {
                        try
                        {
                            var existingProp = existing.GetType().GetProperty("MessageId");
                            if (existingProp == null) continue;
                            var existingId = existingProp.GetValue(existing) as string;
                            if (!string.IsNullOrEmpty(existingId) && existingId == idVal)
                                return Task.FromResult(false);
                        }
                        catch { /* ignore individual reflection errors */ }
                    }
                }
            }
        }
        catch
        {
            // ignore any reflection/type issues and fall back to enqueue
        }

        q.Enqueue(evt);

        // Trim if over capacity
        while (q.Count > _maxPerKey && q.TryDequeue(out _)) { }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<IEvent>> GetRecentAsync(string platform, string channel, int limit = 200)
    {
        if (platform == null) throw new ArgumentNullException(nameof(platform));
        if (channel == null) throw new ArgumentNullException(nameof(channel));

        var key = Key(platform, channel);
        if (!_store.TryGetValue(key, out var q) || q.IsEmpty)
            return Task.FromResult((IReadOnlyList<IEvent>)Array.Empty<IEvent>());

        // Copy to array to avoid enumeration while modified
        var arr = q.ToArray();

        // Return up to 'limit' most recent messages (preserve order oldest->newest)
        var skip = Math.Max(0, arr.Length - limit);
        var result = arr.Skip(skip).ToList().AsReadOnly();

        return Task.FromResult((IReadOnlyList<IEvent>)result);
    }

    public Task DeleteAsync(string platform, string channel)
    {
        var key = Key(platform, channel);
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
