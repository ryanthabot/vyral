using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Published when the scheduler automatically ends a giveaway at its
/// ScheduledEndTime. <see cref="SummaryText"/> is a ready-to-send chat
/// announcement (winner congratulations or a no-entries notice).
/// </summary>
public record GiveawayAutoEndedEvent : EventBase
{
    /// <summary>Display name of the giveaway that was auto-ended.</summary>
    public required string Title { get; init; }

    /// <summary>Ready-to-send chat announcement of the giveaway result.</summary>
    public required string SummaryText { get; init; }

    /// <summary>Platforms the giveaway was open to ("All" or comma-separated, e.g. "Kick,Twitch").</summary>
    public required string AllowedPlatforms { get; init; }
}

/// <summary>
/// Published when the scheduler automatically ends a poll at its
/// ScheduledEndTime. <see cref="SummaryText"/> is a ready-to-send chat
/// announcement containing the final vote results.
/// </summary>
public record PollAutoEndedEvent : EventBase
{
    /// <summary>Question of the poll that was auto-ended.</summary>
    public required string Question { get; init; }

    /// <summary>Ready-to-send chat announcement of the poll results.</summary>
    public required string SummaryText { get; init; }

    /// <summary>Platforms the poll was open to ("All" or comma-separated, e.g. "Kick,Twitch").</summary>
    public required string AllowedPlatforms { get; init; }
}
