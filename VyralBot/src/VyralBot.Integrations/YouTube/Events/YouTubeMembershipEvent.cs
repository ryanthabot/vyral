using VyralBot.Core.Services.EventBus;

namespace VyralBot.Integrations.YouTube.Events;

/// <summary>
/// Event fired when a YouTube membership event occurs — a new member joining
/// ("newSponsorEvent") or an existing member reaching a milestone
/// ("memberMilestoneChatEvent"). Published alongside the synthetic chat text so
/// the UI can render member badges/highlights without parsing display strings.
/// </summary>
public record YouTubeMembershipEvent : EventBase
{
    public required string Username { get; init; }
    public required string UserId { get; init; }
    public required string Level { get; init; }
    public bool IsMilestone { get; init; }
}
