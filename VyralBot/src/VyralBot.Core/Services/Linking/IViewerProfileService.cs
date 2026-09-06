using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Immutable, bindable snapshot of one linked account (username+platform) inside
/// a viewer profile. Aggregates are already resolved per account so the UI binds
/// plain rows directly.
/// </summary>
public sealed record ViewerProfileAccountSnapshot
{
    /// <summary>Account user name.</summary>
    public required string Username { get; init; }

    /// <summary>Platform identity string ("Twitch", "Kick", ...).</summary>
    public required string Platform { get; init; }

    /// <summary>True when this is the (username, platform) pair the profile was opened for.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>Whether the link was verified (see LinkedAccount.IsVerified).</summary>
    public bool IsVerified { get; init; }

    /// <summary>When the account joined the link group (LinkedAccount.CreatedAt).</summary>
    public DateTime? LinkedAt { get; init; }

    /// <summary>Cached per-platform status (sub tier / follower), or null.</summary>
    public ViewerPlatformStatus? Status { get; init; }

    /// <summary>Currency balance for this account, or null when never seen.</summary>
    public UserCurrency? Currency { get; init; }

    /// <summary>Most recent currency transactions for this account (newest first, max 25).</summary>
    public IReadOnlyList<CurrencyTransaction> Transactions { get; init; } = Array.Empty<CurrencyTransaction>();

    /// <summary>Watchtime row for this account, or null.</summary>
    public UserWatchtime? Watchtime { get; init; }

    /// <summary>Stream-attendance streak row for this account, or null.</summary>
    public ViewerStreak? Streak { get; init; }

    /// <summary>Active purchased perks for this account.</summary>
    public IReadOnlyList<ViewerPerk> Perks { get; init; } = Array.Empty<ViewerPerk>();

    /// <summary>Giveaway entries submitted from this account (newest first).</summary>
    public IReadOnlyList<ViewerProfileGiveawayEntrySnapshot> GiveawayEntries { get; init; }
        = Array.Empty<ViewerProfileGiveawayEntrySnapshot>();
}

/// <summary>
/// One giveaway entry joined with its giveaway display name.
/// </summary>
public sealed record ViewerProfileGiveawayEntrySnapshot(
    string GiveawayName,
    string Platform,
    DateTime EnteredAt,
    int EntryCount,
    bool IsWinner);

/// <summary>
/// One completed Vyral goal joined with the goal's display name.
/// </summary>
public sealed record ViewerProfileGoalSnapshot(
    string GoalName,
    DateTime CompletedAt,
    string Platform);

/// <summary>
/// One event-type highlight row for the profile's event activity.
/// </summary>
public sealed record ViewerProfileEventStatRow(
    string EventType,
    int Count);

/// <summary>
/// Immutable aggregate of everything the viewer profile window shows for one
/// (username, platform) request. Plain rows the UI binds directly.
/// </summary>
public sealed record ViewerProfileSnapshot
{
    /// <summary>The username the profile was requested for.</summary>
    public required string RequestedUsername { get; init; }

    /// <summary>The platform the profile was requested for.</summary>
    public required string RequestedPlatform { get; init; }

    /// <summary>Unified viewer identity, or null when the account is not linked.</summary>
    public string? ViewerId { get; init; }

    /// <summary>False renders the "not linked" state in the UI.</summary>
    public bool IsLinked => !string.IsNullOrEmpty(ViewerId);

    /// <summary>All accounts in the link group (just the requested account when not linked).</summary>
    public IReadOnlyList<ViewerProfileAccountSnapshot> Accounts { get; init; }
        = Array.Empty<ViewerProfileAccountSnapshot>();

    /// <summary>Completed Vyral goals (newest first).</summary>
    public IReadOnlyList<ViewerProfileGoalSnapshot> GoalsCompleted { get; init; }
        = Array.Empty<ViewerProfileGoalSnapshot>();

    /// <summary>Total event-statistics rows recorded for the requested username.</summary>
    public int TotalEvents { get; init; }

    /// <summary>Event-count highlights grouped by event type (descending).</summary>
    public IReadOnlyList<ViewerProfileEventStatRow> EventsByType { get; init; }
        = Array.Empty<ViewerProfileEventStatRow>();

    /// <summary>Timestamp of the user's first recorded event, or null.</summary>
    public DateTime? FirstEventAt { get; init; }

    /// <summary>Timestamp of the user's most recent event, or null.</summary>
    public DateTime? LastEventAt { get; init; }
}

/// <summary>
/// Read-model service that aggregates everything the admin viewer-profile
/// window shows about a single viewer identity.
/// </summary>
public interface IViewerProfileService
{
    /// <summary>
    /// Builds the full profile snapshot for username+platform. Never throws for
    /// "missing" data — an unlinked account yields a snapshot with
    /// <see cref="ViewerProfileSnapshot.ViewerId"/> = null and just the raw
    /// account info.
    /// </summary>
    Task<ViewerProfileSnapshot> GetProfileAsync(string username, string platform);
}
