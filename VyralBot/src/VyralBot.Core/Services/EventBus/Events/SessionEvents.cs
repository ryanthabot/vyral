using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.Statistics;

namespace VyralBot.Core.Services.EventBus.Events;

/// <summary>
/// Published when a stream session ends (every tracked platform went offline,
/// see <see cref="SessionSummaryService"/>) and its
/// <see cref="StreamSessionSummary"/> has been compiled and persisted.
/// Future consumers (overlays, workflows, notifications) subscribe to this
/// instead of polling the statistics store.
/// </summary>
public record SessionEndedEvent : EventBase
{
    /// <summary>The compiled post-stream report (already persisted as one of the last 20 session summaries).</summary>
    public required StreamSessionSummary Summary { get; init; }
}
