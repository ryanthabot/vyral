using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.Commands;

/// <summary>
/// Implementation of <see cref="IFunCommandService"/> — the zero-state
/// community fun pack: <c>!8ball</c>, <c>!dice</c>/<c>!roll</c>,
/// <c>!coinflip</c>/<c>!flip</c>, <c>!lurk</c>, <c>!hug</c>, <c>!rng</c>.
/// Everyone, all platforms, replies short, no database — the only state is an
/// in-memory per-user spam guard.
/// </summary>
/// <remarks>
/// <para>
/// SPAM GUARD: one 5-second slot per username (any fun command refreshes it).
/// A second fun command from the same user inside the window is dropped
/// silently (<c>null</c>) so the bot never amplifies spam. The registry is a
/// plain dictionary capped at 256 entries: every insert past the cap first
/// prunes expired timestamps, then evicts the oldest — bounded by
/// construction, and short-lived anyway (timestamps expire after 5s).
/// </para>
/// <para>
/// The <see cref="Random"/> and the clock are constructor seams so tests seed
/// sequences and advance the spam window deterministically instead of sleeping
/// (same pattern as <c>ChatModeCommandService</c>'s <c>utcNow</c>).
/// </para>
/// </remarks>
public class FunCommandService : IFunCommandService
{
    /// <summary>The 20 classic Magic 8-Ball answers (10 affirmative, 5 non-committal, 5 negative).</summary>
    private static readonly string[] EightBallResponses =
    [
        "It is certain.",
        "It is decidedly so.",
        "Without a doubt.",
        "Yes, definitely.",
        "You may rely on it.",
        "As I see it, yes.",
        "Most likely.",
        "Outlook good.",
        "Yes.",
        "Signs point to yes.",
        "Reply hazy, try again.",
        "Ask again later.",
        "Better not tell you now.",
        "Cannot predict now.",
        "Concentrate and ask again.",
        "Don't count on it.",
        "My reply is no.",
        "My sources say no.",
        "Outlook not so good.",
        "Very doubtful."
    ];

    /// <summary>Dice-count bounds for <c>!dice</c>/<c>!roll</c> (default is 1).</summary>
    private const int MinDice = 1;

    /// <summary>Dice-count bounds for <c>!dice</c>/<c>!roll</c>.</summary>
    private const int MaxDice = 10;

    /// <summary>Upper bound on a <c>!rng</c> span (min..max distance) — keeps <c>max+1</c> in int range after validation.</summary>
    private const long MaxRngSpan = 1_000_000_000;

    /// <summary>Spam-guard window per user.</summary>
    private static readonly TimeSpan SpamWindow = TimeSpan.FromSeconds(5);

    /// <summary>Spam-guard registry cap (bounded — see remarks).</summary>
    private const int SpamCapacity = 256;

    private readonly ILogger<FunCommandService> _logger;
    private readonly Random _random;

    /// <summary>Clock seam (tests advance time instead of sleeping).</summary>
    private readonly Func<DateTime> _utcNow;

    /// <summary>username → UTC time of their last matched fun command.</summary>
    private readonly Dictionary<string, DateTime> _lastFunUse = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    public FunCommandService(
        ILogger<FunCommandService> logger,
        Random? random = null,
        Func<DateTime>? utcNow = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = random ?? new Random();
        _utcNow = utcNow ?? (static () => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("!"))
            return Task.FromResult<string?>(null);

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        // Membership first: unmatched messages must never consume a user's
        // spam slot (a !hello between two !dice must not silence the second).
        if (command is not ("!8ball" or "!dice" or "!roll" or "!coinflip" or "!flip"
                or "!lurk" or "!hug" or "!rng"))
            return Task.FromResult<string?>(null);

        if (!TryAcquireSpamSlot(username))
            return Task.FromResult<string?>(null);

        string? result;
        try
        {
            result = command switch
            {
                "!8ball" => HandleEightBall(parts),
                "!dice" or "!roll" => HandleDice(parts),
                "!coinflip" or "!flip" => HandleCoinFlip(),
                "!lurk" => HandleLurk(username),
                "!hug" => HandleHug(username, parts),
                "!rng" => HandleRng(parts),
                _ => null
            };
        }
        catch (Exception ex)
        {
            // A chat command must never throw into the chat pipeline — degrade
            // to a short failure reply (nothing here is expected to throw; the
            // guard mirrors the other command services).
            _logger.LogInformation("Fun command {Command} by {Username} on {Platform} failed: {Error}",
                command, username, platform, ex.Message);
            return Task.FromResult<string?>("That command failed — check the logs.");
        }

        // Matched commands are info-logged for the audit trail (no reply text
        // — the responses are pure fun, the command name is the signal).
        _logger.LogInformation("Fun command {Command} by {Username} on {Platform}", command, username, platform);
        return Task.FromResult<string?>(result);
    }

    // ── spam guard ─────────────────────────────────────────────────────

    /// <summary>
    /// Takes the user's 5-second slot; <c>false</c> means they fired a fun
    /// command within the window and must be dropped silently. Any matched
    /// attempt (including usage errors) consumes the slot.
    /// </summary>
    private bool TryAcquireSpamSlot(string? username)
    {
        var key = string.IsNullOrWhiteSpace(username) ? "anonymous" : username;
        var now = _utcNow();
        lock (_sync)
        {
            if (_lastFunUse.TryGetValue(key, out var last) && now - last < SpamWindow)
                return false;

            _lastFunUse[key] = now;
            if (_lastFunUse.Count > SpamCapacity)
                PruneLocked(now);
            return true;
        }
    }

    /// <summary>Caller holds <see cref="_sync"/>: drop expired entries, then oldest until back under the cap.</summary>
    private void PruneLocked(DateTime now)
    {
        foreach (var stale in _lastFunUse.Where(kv => now - kv.Value >= SpamWindow).Select(kv => kv.Key).ToList())
            _lastFunUse.Remove(stale);

        while (_lastFunUse.Count > SpamCapacity)
        {
            var oldest = _lastFunUse.Aggregate((a, b) => a.Value <= b.Value ? a : b).Key;
            _lastFunUse.Remove(oldest);
        }
    }

    // ── !8ball ─────────────────────────────────────────────────────────

    private string HandleEightBall(string[] parts)
    {
        if (parts.Length < 2)
            return "Usage: !8ball <question>";
        return $"🔮 {EightBallResponses[_random.Next(EightBallResponses.Length)]}";
    }

    // ── !dice / !roll ──────────────────────────────────────────────────

    private string HandleDice(string[] parts)
    {
        var count = 1;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out count) || count < MinDice || count > MaxDice))
            return $"Usage: !dice [{MinDice}-{MaxDice}]";

        var rolls = new int[count];
        for (var i = 0; i < count; i++)
            rolls[i] = _random.Next(1, 7);

        return count == 1
            ? $"🎲 {rolls[0]}"
            : $"🎲 {string.Join(", ", rolls)} = {rolls.Sum()}";
    }

    // ── !coinflip / !flip ──────────────────────────────────────────────

    private string HandleCoinFlip() => _random.Next(2) == 0 ? "🪙 Heads!" : "🪙 Tails!";

    // ── !lurk ──────────────────────────────────────────────────────────

    private static string HandleLurk(string? username) =>
        $"{(string.IsNullOrWhiteSpace(username) ? "Someone" : username)} slips into the shadows… 👋 (welcome back anytime)";

    // ── !hug ───────────────────────────────────────────────────────────

    private string HandleHug(string? username, string[] parts)
    {
        var target = SanitizeUser(parts.Length > 1 ? parts[1] : null);
        if (target.Length == 0)
            return "Usage: !hug <user>";
        return $"{(string.IsNullOrWhiteSpace(username) ? "Someone" : username)} gives {target} a big hug! 🤗";
    }

    // ── !rng ───────────────────────────────────────────────────────────

    private string HandleRng(string[] parts)
    {
        if (parts.Length != 3 || !int.TryParse(parts[1], out var min) || !int.TryParse(parts[2], out var max))
            return "Usage: !rng <min> <max>";
        if (min > max)
            return "Min can't be greater than max.";
        if ((long)max - min + 1 > MaxRngSpan)
            return "Keep the range under a billion.";

        // Next's upper bound is exclusive — +1 makes max inclusive, except at
        // int.MaxValue where +1 would overflow: shift min down instead (safe:
        // the span cap means min stays far above int.MinValue here).
        var value = max == int.MaxValue
            ? _random.Next(min - 1, max) + 1
            : _random.Next(min, max + 1);
        return $"🎯 {value}";
    }

    /// <summary>Strips '@'/'#' decorations and keeps only platform-legal username characters (the !so sanitizer).</summary>
    private static string SanitizeUser(string? raw)
    {
        var trimmed = raw?.Trim().TrimStart('@', '#').Trim() ?? string.Empty;
        return new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-').ToArray());
    }
}
