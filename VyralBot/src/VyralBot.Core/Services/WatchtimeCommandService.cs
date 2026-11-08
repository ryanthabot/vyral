using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for handling watchtime-related chat commands.
/// Processes !watchtime commands for viewing user watchtime and leaderboards.
/// </summary>
public class WatchtimeCommandService
{
    private readonly ILogger<WatchtimeCommandService> _logger;
    private readonly WatchtimeService _watchtimeService;

    // Per-user anti-spam cooldown. !watchtime is invoked directly from the chat
    // pipeline, NOT through the DB-backed CommandService cooldown path, so
    // without this a viewer typing "!watchtime" 10x/second gets 10 replies.
    // Silent-skip matches CommandService's cooldown behavior.
    private static readonly TimeSpan PerUserCooldown = TimeSpan.FromSeconds(10);
    private static readonly int CooldownCapacity = 10_000; // bounded: reset when full
    private readonly ConcurrentDictionary<string, DateTime> _lastAnswered = new();

    public WatchtimeCommandService(
        ILogger<WatchtimeCommandService> logger,
        WatchtimeService watchtimeService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _watchtimeService = watchtimeService ?? throw new ArgumentNullException(nameof(watchtimeService));
    }

    /// <summary>
    /// Processes watchtime commands and returns a response message.
    /// </summary>
    public async Task<string?> ProcessCommandAsync(
        string command,
        string username,
        string platform)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Exact-token match: StartsWith("!watchtime") made "!watchtimerig" and
        // other typos silently answer as a self-lookup.
        if (!commandParts[0].Equals("!watchtime", StringComparison.OrdinalIgnoreCase))
            return null;

        if (IsThrottled(username, platform))
        {
            _logger.LogDebug("Watchtime command throttled for {Username} ({Platform})", username, platform);
            return null;
        }

        try
        {
            // !watchtime - show user's own watchtime
            if (commandParts.Length == 1)
            {
                return await HandleUserWatchtimeAsync(username, platform);
            }

            var subCommand = commandParts[1].ToLower();

            return subCommand switch
            {
                "top" or "leaderboard" or "lb" => await HandleLeaderboardAsync(commandParts),
                _ => await HandleTargetUserWatchtimeAsync(commandParts[1].TrimStart('@'), platform)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing watchtime command: {Command}", command);
            return "❌ An error occurred while processing the watchtime command.";
        }
    }

    private bool IsThrottled(string username, string platform)
    {
        if (_lastAnswered.Count > CooldownCapacity)
        {
            // Bounded eviction: a full reset at worst drops pending cooldowns.
            _lastAnswered.Clear();
        }

        var key = $"{platform}|{username}".ToLowerInvariant();
        var now = DateTime.UtcNow;
        while (true)
        {
            var last = _lastAnswered.GetOrAdd(key, _ => DateTime.MinValue);
            if (now - last < PerUserCooldown)
                return true;
            if (_lastAnswered.TryUpdate(key, now, last))
                return false;
            // Another thread raced us — re-read and re-check.
        }
    }

    private static string TruncateName(string name, int max = 20) =>
        name.Length <= max ? name : name[..(max - 1)] + "…";

    private async Task<string> HandleUserWatchtimeAsync(string username, string platform)
    {
        var watchtime = await _watchtimeService.GetUserWatchtimeAsync(username, platform);

        if (watchtime == null || watchtime.TotalMinutes == 0)
        {
            // Discord NEVER accrues watchtime (WatchtimeService.ShouldAccrueWatchtime),
            // so "keep watching" would be a lie there — say what actually applies.
            if (string.Equals(platform, "Discord", StringComparison.OrdinalIgnoreCase))
            {
                return $"@{username}, watchtime is tracked on live platforms (e.g. Twitch, Kick, YouTube) — it doesn't accrue on Discord. Ask from a stream chat!";
            }

            return $"@{username}, you haven't been tracked yet. Keep watching to build up watchtime!";
        }

        var formattedTime = WatchtimeService.FormatWatchtime(watchtime.TotalMinutes);
        var sessionTime = WatchtimeService.FormatWatchtime(watchtime.StreamSessionMinutes);

        return $"@{username}, Total watchtime: {formattedTime} | This stream: {sessionTime}";
    }

    private async Task<string> HandleTargetUserWatchtimeAsync(string targetUsername, string platform)
    {
        // Check watchtime for another user
        var watchtime = await _watchtimeService.GetUserWatchtimeAsync(targetUsername, platform);

        if (watchtime == null || watchtime.TotalMinutes == 0)
        {
            if (string.Equals(platform, "Discord", StringComparison.OrdinalIgnoreCase))
            {
                return $"@{targetUsername} has no watchtime here — watchtime only accrues on live platforms, not Discord.";
            }

            return $"@{targetUsername} hasn't been tracked yet.";
        }

        var formattedTime = WatchtimeService.FormatWatchtime(watchtime.TotalMinutes);
        return $"@{targetUsername} - Total watchtime: {formattedTime}";
    }

    private async Task<string> HandleLeaderboardAsync(string[] commandParts)
    {
        // Parse limit (default 5, max 10)
        int limit = 5;
        if (commandParts.Length > 2 && int.TryParse(commandParts[2], out int parsedLimit))
        {
            limit = Math.Clamp(parsedLimit, 1, 10);
        }

        var topUsers = (await _watchtimeService.GetTopWatchtimeUsersAsync(limit)).ToList();

        if (topUsers.Count == 0)
        {
            return "📊 No watchtime data available yet.";
        }

        var result = new StringBuilder();
        result.Append($"📊 Top {topUsers.Count} Watchtime Leaderboard: ");

        for (int i = 0; i < topUsers.Count; i++)
        {
            var user = topUsers[i];
            var formattedTime = WatchtimeService.FormatWatchtime(user.TotalMinutes);
            // Names are clamped so a max-size board (10 x 25-char names) stays
            // within platform message limits (YouTube clips hard around 200).
            result.Append($"#{i + 1} {TruncateName(user.Username)} ({formattedTime})");
            
            if (i < topUsers.Count - 1)
                result.Append(" | ");
        }

        return result.ToString();
    }
}
