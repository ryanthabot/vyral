using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Manual moderator warnings that feed the auto-mod escalation ladder
/// (!warn / !warnings / !clearwarn). Mirrors the QuoteService chat-pipeline
/// contract — <see cref="ProcessCommandAsync"/> returns the ready-to-send
/// reply, or null when the message is not a warning command (ChatViewModel
/// falls through to the next handler).
///
/// Strikes live in the SAME per-viewer escalation state as rule violations
/// (<see cref="IChatModerationEngine"/>): a manual warn advances the ladder,
/// so the viewer's NEXT rule violation escalates per the shared rungs
/// (Timeout at <c>StrikesToTimeout</c>, Ban at <c>StrikesToBan</c>) through
/// the normal action pipeline. When a manual warn itself tips the viewer onto
/// a Timeout/Ban rung, that action fires IMMEDIATELY through the same
/// <see cref="ModerationActionService.ExecuteAsync"/> pipeline (executed,
/// degraded and audited by it, actor "AutoMod") — one punishment per strike,
/// never doubled. Warning-level strikes are audited directly through the
/// <see cref="IDataService.SaveModActionAsync"/> save path the action service
/// uses (actor = the issuing mod, the ModCommandEngine precedent): routing a
/// Warn verdict through the action pipeline would double-post a chat notice
/// next to this command's own reply.
///
/// PERSISTENCE (accepted trade-off): warning counts are the engine's
/// in-memory escalation state (5k-viewer cap, strike-decay window) —
/// restarting VyralBot clears them. Only the ModActions audit rows survive
/// restarts; the Moderator dashboard's Action Log shows every warn.
/// </summary>
public interface IWarningCommandService
{
    /// <summary>
    /// Processes a manual warning command (!warn / !warnings / !clearwarn)
    /// and returns the chat reply, or null when the message is not a warning
    /// command.
    /// </summary>
    /// <param name="username">Sender's username (the acting moderator, recorded in the audit log).</param>
    /// <param name="platform">Originating platform (moderation + escalation key part).</param>
    /// <param name="isModerator">Sender's moderator/broadcaster status — gates every command.</param>
    /// <param name="message">Raw chat message text.</param>
    Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message);
}

/// <summary>
/// Default <see cref="IWarningCommandService"/>. Commands (moderator-only):
/// <c>!warn &lt;user&gt; [reason]</c>, <c>!warnings &lt;user&gt;</c>,
/// <c>!clearwarn &lt;user&gt;</c>. See the interface docs for the ladder and
/// persistence contract.
/// </summary>
public class WarningCommandService : IWarningCommandService
{
    private const string NoReasonText = "no reason given";
    private const int MaxReasonLength = 200;

    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    private readonly IChatModerationEngine _engine;
    private readonly ModerationActionService _actions;
    private readonly IDataService _dataService;
    private readonly ILogger<WarningCommandService> _logger;

    public WarningCommandService(
        IChatModerationEngine engine,
        IDataService dataService,
        ModerationActionService? actionService = null,
        ILogger<WarningCommandService>? logger = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        // Default to the process-wide instance the chat pipelines execute on.
        _actions = actionService ?? ModerationActionService.Shared;
        _logger = logger ?? NullLogger<WarningCommandService>.Instance;
    }

    public async Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message[0] != '!')
        {
            return null;
        }

        var parts = message.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        if (command is not ("!warn" or "!warnings" or "!clearwarn"))
        {
            return null; // not ours — let the pipeline continue
        }

        if (!isModerator)
        {
            return "❌ Only moderators can manage warnings.";
        }

        try
        {
            switch (command)
            {
                case "!warn":
                    return await HandleWarnAsync(username, platform, parts).ConfigureAwait(false);
                case "!warnings":
                    return HandleWarnings(platform, parts);
                default:
                    return await HandleClearWarnAsync(username, platform, parts).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing the warning command '{Command}' from {Username}", message, username);
            return "❌ Something went wrong while processing that warning command.";
        }
    }

    // ------------------------------------------------------------------
    // !warn <user> [reason]
    // ------------------------------------------------------------------

    private async Task<string?> HandleWarnAsync(string moderator, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "❌ Usage: !warn <user> [reason]";
        }

        var target = NormalizeTarget(parts[1]);
        if (target.Length == 0)
        {
            return "❌ Usage: !warn <user> [reason]";
        }

        var reason = Truncate(string.Join(' ', parts.Skip(2)).Trim(), MaxReasonLength);
        var reasonText = reason.Length > 0 ? reason : NoReasonText;

        var ladder = _engine.RegisterManualWarning(target, platform, reasonText);
        var next = _engine.ResolveLadderAction(ladder.Strikes + 1);

        _logger.LogInformation(
            "Manual warning for {Platform}/{Target} by {Moderator} (strike {Strikes}): {Reason}",
            platform, target, moderator, ladder.Strikes, reasonText);

        if (ladder.Action is ModerationActionKind.Warn)
        {
            // Warning-level strike: record + audit only. The viewer's next rule
            // violation escalates through the normal pipeline because the
            // strike lives in the SAME engine state.
            await SaveAuditAsync(moderator, target, platform, "Warn",
                $"manual warning (strike {ladder.Strikes}) — {reasonText}").ConfigureAwait(false);
            return $"⚠ {target} warned ({ladder.Strikes} total) — next: {DescribeAction(next)}";
        }

        // The ladder escalates at this strike count (timeout/ban): fire through
        // the NORMAL action pipeline exactly like a rule violation — it
        // executes with graceful degradation and writes its own audit row.
        var verdict = new ModerationVerdict(
            ModerationRuleType.ManualWarning,
            ladder.Action,
            ladder.TimeoutSeconds,
            $"manual warning by {moderator}: {reasonText}",
            ladder.Strikes);
        var executed = await _actions.ExecuteAsync(new ModerationIncomingMessage
        {
            Platform = platform,
            Username = target,
            Message = "!warn (manual)"
        }, verdict).ConfigureAwait(false);

        return $"⚠ {target} warned ({ladder.Strikes} total) — " +
               $"escalated: {DescribeExecuted(executed, ladder.TimeoutSeconds)} — " +
               $"next: {DescribeAction(next)}";
    }

    // ------------------------------------------------------------------
    // !warnings <user>
    // ------------------------------------------------------------------

    private string HandleWarnings(string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "❌ Usage: !warnings <user>";
        }

        var target = NormalizeTarget(parts[1]);
        if (target.Length == 0)
        {
            return "❌ Usage: !warnings <user>";
        }

        var strikes = _engine.GetStrikeCount(platform, target);
        if (strikes == 0)
        {
            return $"✅ {target} has no active warnings.";
        }

        var escalation = _engine.Rules.Escalation;
        var next = _engine.ResolveLadderAction(strikes + 1);
        var ladderNote = escalation.Enabled
            ? $" (timeout at {escalation.StrikesToTimeout}, ban at {escalation.StrikesToBan}, {escalation.StrikeWindowMinutes}m decay window)"
            : " (escalation off)";
        return $"⚠ {target}: {strikes} warning(s) — next: {DescribeAction(next)}{ladderNote}";
    }

    // ------------------------------------------------------------------
    // !clearwarn <user>
    // ------------------------------------------------------------------

    private async Task<string?> HandleClearWarnAsync(string moderator, string platform, string[] parts)
    {
        if (parts.Length < 2)
        {
            return "❌ Usage: !clearwarn <user>";
        }

        var target = NormalizeTarget(parts[1]);
        if (target.Length == 0)
        {
            return "❌ Usage: !clearwarn <user>";
        }

        var strikes = _engine.GetStrikeCount(platform, target);
        _engine.ResetViewer(platform, target);

        _logger.LogInformation(
            "Warnings cleared for {Platform}/{Target} by {Moderator} (had {Strikes})",
            platform, target, moderator, strikes);
        await SaveAuditAsync(moderator, target, platform, "ClearWarn",
            $"cleared {strikes} warning(s)").ConfigureAwait(false);
        return $"✅ Warnings cleared for {target} (had {strikes}).";
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private async Task SaveAuditAsync(string moderator, string target, string platform, string action, string details)
    {
        try
        {
            // Same save path + ModAction shape the ModerationActionService log
            // uses for rule-violation actions (platform-prefixed details).
            await _dataService.SaveModActionAsync(new ModAction
            {
                ModUsername = moderator,
                ModUserId = "",
                TargetUsername = target,
                TargetUserId = "",
                Action = action,
                Details = $"{platform}: {details}"
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist the warning audit row for {Target}", target);
        }
    }

    private static string NormalizeTarget(string raw)
        => (raw ?? "").Trim().TrimStart('@');

    private static string DescribeAction(LadderAction rung) => rung.Action switch
    {
        ModerationActionKind.Ban => "ban",
        ModerationActionKind.Timeout => $"timeout {rung.TimeoutSeconds}s",
        ModerationActionKind.Delete => "delete",
        _ => "warning"
    };

    private static string DescribeExecuted(ModerationActionKind executed, int timeoutSeconds) => executed switch
    {
        ModerationActionKind.Ban => "ban",
        ModerationActionKind.Timeout => $"timeout {timeoutSeconds}s",
        ModerationActionKind.Delete => "delete",
        // Platform without an enforcement surface — the action is logged only.
        _ => "warning only (logged)"
    };

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
}
