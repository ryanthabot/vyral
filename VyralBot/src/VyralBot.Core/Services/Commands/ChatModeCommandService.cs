using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.Commands;

/// <summary>
/// Implementation of <see cref="IChatModeCommandService"/> — the Twitch chat-mode
/// protection commands with AUTO-REVERT: <c>!emoteonly</c>, <c>!subonly</c>,
/// <c>!followers</c>, <c>!slow</c>. Enabling a mode arms a timed revert (default
/// 10 minutes) so a raid-shielded chat always opens up again; <c>!&lt;mode&gt; off</c>
/// disables immediately and cancels the pending revert.
/// </summary>
/// <remarks>
/// <para>
/// AUTO-REVERT design: a bounded registry (one entry per
/// <see cref="TwitchChatMode"/> — at most 4, the dictionary key set is closed)
/// maps mode → due UTC time. Re-issuing a command re-arms (replaces) its entry,
/// so there is ever only ONE pending revert per mode. A single one-shot
/// <see cref="System.Threading.Timer"/> (the scheduled-messages pattern from
/// <c>DiscordBotManager</c>) checks due reverts every 30 seconds; it is created
/// disarmed and only ticks while a revert is pending. Due reverts are attempted
/// once — a failed Twitch call drops the entry with an info log (a mod can
/// always re-issue <c>!&lt;mode&gt; off</c>). The service cannot send chat lines, so
/// the enable reply itself carries the window ("… for 10 minutes.").
/// Pending reverts are in-memory only: they die with the process and the modes
/// stay as-is on Twitch after a shutdown.
/// </para>
/// <para>
/// Twitch is reached through the Core-owned <see cref="ITwitchChatModeApplier"/>
/// seam (implemented by the Integrations TwitchService) because Core cannot
/// reference Integrations. All commands are moderator-only and Twitch-only.
/// </para>
/// </remarks>
public class ChatModeCommandService : IChatModeCommandService, IDisposable
{
    private const string ModOnly = "That's a moderator-only command.";
    private const string NotConnected = "Twitch is not connected.";

    /// <summary>Auto-revert window (minutes) when a command gives none.</summary>
    private const int DefaultRevertMinutes = 10;

    /// <summary>Upper bound for an explicit auto-revert window (minutes).</summary>
    private const int MaxRevertMinutes = 120;

    /// <summary>Helix slow-mode wait bounds (seconds).</summary>
    private const int MinSlowSeconds = 3;

    /// <summary>Helix slow-mode wait bounds (seconds).</summary>
    private const int MaxSlowSeconds = 120;

    /// <summary>Helix follower-age bound (minutes) — 90 days.</summary>
    private const int MaxFollowerMinutes = 129600;

    /// <summary>
    /// Revert-check cadence: a due revert fires within one tick after its time
    /// (at most ~30 seconds late).
    /// </summary>
    private static readonly TimeSpan RevertTickInterval = TimeSpan.FromSeconds(30);

    private readonly ITwitchChatModeApplier? _twitch;
    private readonly ILogger<ChatModeCommandService> _logger;

    /// <summary>Clock seam (tests advance time instead of sleeping).</summary>
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// Pending auto-reverts (mode → due UTC). Bounded by construction: the key
    /// set is the closed <see cref="TwitchChatMode"/> enum, so at most 4 entries.
    /// </summary>
    private readonly Dictionary<TwitchChatMode, DateTime> _pendingReverts =
        new(Enum.GetValues<TwitchChatMode>().Length);

    private readonly object _sync = new();
    private readonly System.Threading.Timer? _revertTimer;
    private int _revertTickInProgress;
    private bool _disposed;

    public ChatModeCommandService(
        ITwitchChatModeApplier? twitchChatModes,
        ILogger<ChatModeCommandService> logger,
        Func<DateTime>? utcNow = null)
    {
        _twitch = twitchChatModes;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _utcNow = utcNow ?? (static () => DateTime.UtcNow);

        // Single one-shot timer re-armed after every tick (scheduled-messages
        // pattern). Created disarmed — zero ticking while no revert is pending.
        _revertTimer = new System.Threading.Timer(
            _ => _ = RunRevertTickAsync(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// How many auto-reverts are currently armed (0–4). Public on the concrete
    /// class (not part of <see cref="IChatModeCommandService"/>) because Core
    /// grants no InternalsVisibleTo to the test project — test seam.
    /// </summary>
    public int PendingRevertCount
    {
        get
        {
            lock (_sync)
            {
                return _pendingReverts.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("!"))
            return null;

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        string? result;
        try
        {
            result = command switch
            {
                "!emoteonly" => isModerator
                    ? await HandleSimpleModeAsync(platform, parts, TwitchChatMode.EmoteOnly, "Emote-only", "!emoteonly")
                    : ModOnly,
                "!subonly" => isModerator
                    ? await HandleSimpleModeAsync(platform, parts, TwitchChatMode.SubscriberOnly, "Subscribers-only", "!subonly")
                    : ModOnly,
                "!followers" => isModerator ? await HandleFollowersAsync(platform, parts) : ModOnly,
                "!slow" => isModerator ? await HandleSlowAsync(platform, parts) : ModOnly,
                _ => null
            };
        }
        catch (Exception ex)
        {
            // A chat command must never throw into the chat pipeline — degrade
            // to a short failure reply (the applier contract already returns
            // false instead of throwing; this guards the parse/registry rest).
            _logger.LogInformation("Chat-mode command {Command} by {Username} on {Platform} failed: {Error}",
                command, username, platform, ex.Message);
            return "That command failed — check the logs.";
        }

        // Unmatched messages fall through (null) so other command services can
        // take them; matched ones are info-logged for the audit trail.
        if (result is not null)
        {
            _logger.LogInformation("Chat-mode command {Command} by {Username} on {Platform}",
                command, username, platform);
        }

        return result;
    }

    // ── !emoteonly / !subonly ───────────────────────────────────────────
    // The two boolean-only modes share one shape: [minutes] | off.
    private async Task<string> HandleSimpleModeAsync(
        string platform, string[] parts, TwitchChatMode mode, string label, string commandName)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return $"{commandName} is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        if (parts.Length > 1 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            return await DisableModeAsync(twitch, mode, label);

        var minutes = DefaultRevertMinutes;
        if (parts.Length > 1)
        {
            if (!int.TryParse(parts[1], out var requested) || requested < 1)
                return $"Usage: {commandName} [minutes] (or {commandName} off)";
            minutes = Math.Min(requested, MaxRevertMinutes);
        }

        if (!await twitch.ApplyChatModeAsync(mode, true))
            return $"{label} update failed.";

        ArmRevert(mode, minutes);
        return $"{label} enabled for {minutes} minute{(minutes == 1 ? string.Empty : "s")}.";
    }

    // ── !followers ──────────────────────────────────────────────────────
    // !followers [follow minutes|hours, e.g. 30 or 2h] [revert minutes]
    // Follow age defaults to 0 (any follower); revert window to 10 minutes.
    private async Task<string> HandleFollowersAsync(string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "!followers is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        if (parts.Length > 1 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            return await DisableModeAsync(twitch, TwitchChatMode.FollowerOnly, "Followers-only");

        const string usage = "Usage: !followers [follow minutes|hours, e.g. 30 or 2h] [revert minutes] (or !followers off)";

        var followMinutes = 0;
        var revertMinutes = DefaultRevertMinutes;
        if (parts.Length > 1)
        {
            if (!TryParseDuration(parts[1], out followMinutes) || followMinutes < 0)
                return usage;
            followMinutes = Math.Min(followMinutes, MaxFollowerMinutes);

            if (parts.Length > 2)
            {
                if (!int.TryParse(parts[2], out revertMinutes) || revertMinutes < 1)
                    return usage;
                revertMinutes = Math.Min(revertMinutes, MaxRevertMinutes);
            }
        }

        if (!await twitch.ApplyChatModeAsync(TwitchChatMode.FollowerOnly, true, followMinutes))
            return "Followers-only update failed.";

        ArmRevert(TwitchChatMode.FollowerOnly, revertMinutes);
        var ageText = followMinutes > 0 ? $" (must have followed for {HumanizeMinutes(followMinutes)})" : string.Empty;
        return $"Followers-only enabled{ageText} for {revertMinutes} minute{(revertMinutes == 1 ? string.Empty : "s")}.";
    }

    // ── !slow ───────────────────────────────────────────────────────────
    // !slow <seconds> [minutes] — seconds are required and bounded by Helix.
    private async Task<string> HandleSlowAsync(string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "!slow is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        if (parts.Length > 1 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            return await DisableModeAsync(twitch, TwitchChatMode.Slow, "Slow mode");

        var usage = $"Usage: !slow <{MinSlowSeconds}-{MaxSlowSeconds} seconds> [minutes] (or !slow off)";
        if (parts.Length < 2 || !int.TryParse(parts[1], out var seconds) ||
            seconds < MinSlowSeconds || seconds > MaxSlowSeconds)
            return usage;

        var revertMinutes = DefaultRevertMinutes;
        if (parts.Length > 2)
        {
            if (!int.TryParse(parts[2], out revertMinutes) || revertMinutes < 1)
                return usage;
            revertMinutes = Math.Min(revertMinutes, MaxRevertMinutes);
        }

        if (!await twitch.ApplyChatModeAsync(TwitchChatMode.Slow, true, seconds))
            return "Slow mode update failed.";

        ArmRevert(TwitchChatMode.Slow, revertMinutes);
        return $"Slow mode enabled ({seconds}s) for {revertMinutes} minute{(revertMinutes == 1 ? string.Empty : "s")}.";
    }

    // ── auto-revert plumbing ────────────────────────────────────────────

    /// <summary>
    /// Disables a mode now and cancels any pending auto-revert for it (a late
    /// tick must never keep a reverted entry armed).
    /// </summary>
    private async Task<string> DisableModeAsync(ITwitchChatModeApplier twitch, TwitchChatMode mode, string label)
    {
        lock (_sync)
        {
            _pendingReverts.Remove(mode);
        }

        return await twitch.ApplyChatModeAsync(mode, false)
            ? $"{label} disabled."
            : $"{label} update failed.";
    }

    /// <summary>
    /// Arms (or re-arms — one pending revert per mode) the auto-revert of a
    /// just-enabled mode and wakes the checker timer.
    /// </summary>
    private void ArmRevert(TwitchChatMode mode, int minutes)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _pendingReverts[mode] = _utcNow().AddMinutes(minutes);
            ArmTimerLocked();
        }
    }

    /// <summary>Timer arm call that survives a concurrent <see cref="Dispose"/>.</summary>
    private void ArmTimerLocked()
    {
        if (_revertTimer is null)
            return;

        try
        {
            _revertTimer.Change(RevertTickInterval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Service disposed mid-arm — nothing left to arm.
        }
    }

    /// <summary>
    /// Timer callback: guarded against overlap, never throws, re-arms itself
    /// only while reverts are still pending.
    /// </summary>
    private async Task RunRevertTickAsync()
    {
        if (Interlocked.CompareExchange(ref _revertTickInProgress, 1, 0) != 0)
            return;

        try
        {
            await ProcessDueRevertsAsync();
        }
        catch (Exception ex)
        {
            // Fire-and-forget tick must never surface as an unobserved task exception.
            _logger.LogInformation(ex, "Chat-mode auto-revert tick failed");
        }
        finally
        {
            Interlocked.Exchange(ref _revertTickInProgress, 0);

            bool rearm;
            lock (_sync)
            {
                rearm = !_disposed && _pendingReverts.Count > 0;
                if (rearm)
                {
                    ArmTimerLocked();
                }
            }
        }
    }

    /// <summary>
    /// Reverts every pending mode whose due time has passed (single attempt:
    /// a failed Twitch call drops the entry with an info log — a mod can
    /// always re-issue <c>!&lt;mode&gt; off</c>). Public on the concrete class
    /// (not part of <see cref="IChatModeCommandService"/>) because Core grants
    /// no InternalsVisibleTo to the test project — the Wave-16 tests drive it
    /// directly instead of waiting on the 30-second timer.
    /// </summary>
    /// <returns>How many modes were actually reverted.</returns>
    public async Task<int> ProcessDueRevertsAsync()
    {
        List<KeyValuePair<TwitchChatMode, DateTime>> due;
        lock (_sync)
        {
            if (_disposed)
                return 0;

            var now = _utcNow();
            due = new List<KeyValuePair<TwitchChatMode, DateTime>>(_pendingReverts.Count);
            foreach (var entry in _pendingReverts)
            {
                if (entry.Value <= now)
                    due.Add(entry);
            }

            foreach (var entry in due)
                _pendingReverts.Remove(entry.Key);

            // Nothing left pending — stop ticking until the next command arms it.
            if (_pendingReverts.Count == 0 && _revertTimer is not null)
            {
                try
                {
                    _revertTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                    // Disposed mid-tick — nothing left to disarm.
                }
            }
        }

        var reverted = 0;
        foreach (var entry in due)
        {
            var twitch = _twitch;
            var ok = twitch is { IsConnected: true } && await twitch.ApplyChatModeAsync(entry.Key, false);
            if (ok)
            {
                reverted++;
                _logger.LogInformation("Auto-reverted Twitch {Mode} chat mode", entry.Key);
            }
            else
            {
                _logger.LogInformation(
                    "Auto-revert of Twitch {Mode} chat mode failed - a mod can disable it manually", entry.Key);
            }
        }

        return reverted;
    }

    /// <summary>The connected Twitch seam, or null when absent/disconnected.</summary>
    private ITwitchChatModeApplier? ConnectedTwitch() => _twitch is { IsConnected: true } ? _twitch : null;

    /// <summary>
    /// Parses a follow-age argument: a bare number is minutes, an optional
    /// <c>h</c>/<c>m</c> suffix selects hours/minutes ("2h" → 120).
    /// </summary>
    private static bool TryParseDuration(string text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        var suffix = char.ToLowerInvariant(text[^1]);
        var isHours = suffix == 'h';
        var isMinutes = suffix == 'm';
        var number = isHours || isMinutes ? text[..^1] : text;
        if (!int.TryParse(number, out var value))
            return false;

        minutes = isHours ? value * 60 : value;
        return true;
    }

    /// <summary>Largest two non-zero units ("2 hours", "3 months, 2 days").</summary>
    private static string HumanizeMinutes(int totalMinutes)
    {
        var span = TimeSpan.FromMinutes(totalMinutes);
        var years = span.Days / 365;
        var remainingDays = span.Days % 365;
        var months = remainingDays / 30;
        var days = remainingDays % 30;

        // Ordered coarse-to-fine; take the first two non-zero entries.
        (int Value, string Singular)[] units =
        [
            (years, "year"), (months, "month"), (days, "day"),
            (span.Hours, "hour"), (span.Minutes, "minute")
        ];

        var picked = new List<string>(2);
        foreach (var (value, singular) in units)
        {
            if (value <= 0)
                continue;
            picked.Add(value == 1 ? $"1 {singular}" : $"{value} {singular}s");
            if (picked.Count == 2)
                break;
        }

        return picked.Count > 0 ? string.Join(", ", picked) : "less than a minute";
    }

    /// <summary>
    /// Kills the revert checker. Pending reverts are in-memory only — they die
    /// here and the modes stay as-is on Twitch (a mod re-issues
    /// <c>!&lt;mode&gt; off</c> after a restart).
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _revertTimer?.Dispose();
    }
}
