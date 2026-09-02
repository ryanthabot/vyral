using Microsoft.Extensions.ObjectPool;

namespace VyralBot.Core.Services.Performance;

/// <summary>
/// Object pool for frequently allocated event objects.
/// Eliminates GC pressure from high-frequency chat message events.
/// Per updated_performance_upgrade.md: TIER 4 optimization.
/// </summary>
public static class EventPool
{
    /// <summary>
    /// Pool policy for resettable events
    /// </summary>
    private class ResettablePoolPolicy<T> : IPooledObjectPolicy<T> where T : class, IResettable, new()
    {
        public T Create() => new T();
        
        public bool Return(T obj)
        {
            return obj.TryReset();
        }
    }
    
    // Pools for common event types - lazily initialized
    private static ObjectPool<PooledChatMessageEvent>? _chatMessagePool;
    private static readonly object _chatLock = new();
    
    /// <summary>
    /// Rent a chat message event from the pool
    /// </summary>
    public static PooledChatMessageEvent RentChatMessage()
    {
        EnsureChatMessagePoolInitialized();
        return _chatMessagePool!.Get();
    }
    
    /// <summary>
    /// Return a chat message event to the pool
    /// </summary>
    public static void Return(PooledChatMessageEvent evt)
    {
        EnsureChatMessagePoolInitialized();
        _chatMessagePool!.Return(evt);
    }
    
    private static void EnsureChatMessagePoolInitialized()
    {
        if (_chatMessagePool != null) return;
        
        lock (_chatLock)
        {
            _chatMessagePool ??= new DefaultObjectPool<PooledChatMessageEvent>(
                new ResettablePoolPolicy<PooledChatMessageEvent>(),
                maximumRetained: 100
            );
        }
    }
}

/// <summary>
/// Interface for objects that can be reset and returned to a pool
/// </summary>
public interface IResettable
{
    /// <summary>
    /// Reset the object to its initial state for reuse.
    /// Returns false if the object cannot be reused.
    /// </summary>
    bool TryReset();
}

/// <summary>
/// Pooled chat message event to avoid allocations on hot path.
/// Use EventPool.RentChatMessage() to get an instance.
/// </summary>
public sealed class PooledChatMessageEvent : IResettable
{
    public string Platform { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsModerator { get; set; }
    public bool IsSubscriber { get; set; }
    public bool IsOwner { get; set; }
    public bool IsVerified { get; set; }
    public int? SubscriberMonths { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string>? Badges { get; set; }
    
    /// <summary>
    /// Copy data from this pooled event to a new event object.
    /// Use when you need to store the event beyond the current scope.
    /// </summary>
    public ChatMessageData ToData()
    {
        return new ChatMessageData
        {
            Platform = Platform,
            Channel = Channel,
            Username = Username,
            UserId = UserId,
            DisplayName = DisplayName,
            Message = Message,
            Color = Color,
            IsModerator = IsModerator,
            IsSubscriber = IsSubscriber,
            IsOwner = IsOwner,
            IsVerified = IsVerified,
            SubscriberMonths = SubscriberMonths,
            Timestamp = Timestamp,
            Badges = Badges != null ? new Dictionary<string, string>(Badges) : null
        };
    }
    
    public bool TryReset()
    {
        Platform = string.Empty;
        Channel = string.Empty;
        Username = string.Empty;
        UserId = string.Empty;
        DisplayName = string.Empty;
        Message = string.Empty;
        Color = null;
        IsModerator = false;
        IsSubscriber = false;
        IsOwner = false;
        IsVerified = false;
        SubscriberMonths = null;
        Timestamp = default;
        Badges?.Clear();
        return true;
    }
}

/// <summary>
/// Immutable chat message data for storage/UI.
/// Copy from PooledChatMessageEvent when persistence is needed.
/// </summary>
public sealed record ChatMessageData
{
    public string Platform { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Color { get; init; }
    public bool IsModerator { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsOwner { get; init; }
    public bool IsVerified { get; init; }
    public int? SubscriberMonths { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, string>? Badges { get; init; }
}
