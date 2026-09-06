using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Published when a link code is redeemed and platform accounts are merged
/// under one unified viewer identity.
/// </summary>
public record AccountsLinkedEvent : EventBase
{
    /// <summary>Unified viewer identity the accounts now share.</summary>
    public required string ViewerId { get; init; }

    /// <summary>Username that redeemed the link code.</summary>
    public required string Username { get; init; }

    /// <summary>Platform the link code was redeemed on.</summary>
    public required string Platform { get; init; }

    /// <summary>Comma-separated platform names now linked under the viewer identity.</summary>
    public required string LinkedPlatformNames { get; init; }
}

/// <summary>
/// Published when a viewer's per-platform status changes
/// (a subscription or follow was recorded).
/// </summary>
public record ViewerStatusUpdatedEvent : EventBase
{
    /// <summary>Username the status belongs to.</summary>
    public required string Username { get; init; }

    /// <summary>Platform identity string ("Twitch", "Kick", "YouTube", ...).</summary>
    public required string Platform { get; init; }

    /// <summary>Subscription tier: 0 none, 1..3.</summary>
    public required int SubTier { get; init; }

    /// <summary>Whether the viewer is a subscriber on the platform.</summary>
    public required bool IsSubscriber { get; init; }

    /// <summary>Whether the viewer is a follower on the platform.</summary>
    public required bool IsFollower { get; init; }
}

/// <summary>
/// Published when a viewer completes a broadcaster-defined social goal
/// (see VyralBot.Core.Services.Linking.SocialGoalService).
/// </summary>
public record ViewerGoalCompletedEvent : EventBase
{
    /// <summary>Unified viewer identity of the completer.</summary>
    public required string ViewerId { get; init; }

    /// <summary>Username that completed the goal.</summary>
    public required string Username { get; init; }

    /// <summary>Platform the completion was evaluated on.</summary>
    public required string Platform { get; init; }

    /// <summary>Name of the completed SocialGoal.</summary>
    public required string GoalName { get; init; }

    /// <summary>Human-readable description of the granted reward.</summary>
    public required string RewardDescription { get; init; }
}
