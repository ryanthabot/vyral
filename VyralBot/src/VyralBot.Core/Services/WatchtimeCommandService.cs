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
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("!watchtime"))
            return null;

        var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

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
                _ => await HandleTargetUserWatchtimeAsync(commandParts[1], platform)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing watchtime command: {Command}", command);
            return "❌ An error occurred while processing the watchtime command.";
        }
    }

    private async Task<string> HandleUserWatchtimeAsync(string username, string platform)
    {
        var watchtime = await _watchtimeService.GetUserWatchtimeAsync(username, platform);

        if (watchtime == null || watchtime.TotalMinutes == 0)
        {
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
            result.Append($"#{i + 1} {user.Username} ({formattedTime})");
            
            if (i < topUsers.Count - 1)
                result.Append(" | ");
        }

        return result.ToString();
    }
}
