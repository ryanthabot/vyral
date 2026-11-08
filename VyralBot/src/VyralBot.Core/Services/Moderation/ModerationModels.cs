namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// One incoming chat message projected for the auto-moderator. The caller
/// (chat pipeline / test UI) fills the platform badges it knows; missing flags
/// default to false, which is the safest (strictest) interpretation.
/// </summary>
public sealed record ModerationIncomingMessage
{
    /// <summary>Platform name as used across the app ("Twitch", "Kick", "YouTube", "Discord", "TikTok").</summary>
    public required string Platform { get; init; }

    /// <summary>Viewer display name (moderation + escalation key part).</summary>
    public required string Username { get; init; }

    /// <summary>Platform user id when known (action APIs prefer ids).</summary>
    public string UserId { get; init; } = "";

    /// <summary>Raw message text.</summary>
    public required string Message { get; init; }

    /// <summary>Platform message id when known (delete-by-id APIs).</summary>
    public string MessageId { get; init; } = "";

    public bool IsModerator { get; init; }

    public bool IsVip { get; init; }

    public bool IsSubscriber { get; init; }

    /// <summary>The channel broadcaster — always exempt from auto-mod.</summary>
    public bool IsBroadcaster { get; init; }
}

/// <summary>
/// The engine's decision for one message: which rule tripped, the action to
/// execute (after escalation), and a human-readable reason for the mod log.
/// </summary>
public sealed record ModerationVerdict(
    ModerationRuleType Rule,
    ModerationActionKind Action,
    int TimeoutSeconds,
    string Reason,
    int Strikes);

/// <summary>
/// The escalation ladder's answer for one strike count. Rule violations and
/// manual !warn strikes climb the SAME ladder, so both ask this question of
/// the same rungs: which action the ladder prescribes at
/// <see cref="Strikes"/> strikes, and the timeout length when that action is
/// a Timeout (base seconds doubling per strike past the timeout rung).
/// </summary>
public sealed record LadderAction(int Strikes, ModerationActionKind Action, int TimeoutSeconds);
