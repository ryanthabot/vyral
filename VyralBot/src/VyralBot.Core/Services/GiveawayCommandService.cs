using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for handling giveaway-related chat commands.
/// Processes !giveaway commands for creating, starting, entering, and drawing winners.
/// </summary>
public class GiveawayCommandService
{
    private static readonly TimeSpan TimedConfirmationWindow = TimeSpan.FromSeconds(60);

    // "username:platform" → last timed-mode confirmation time (static per brief).
    private static readonly ConcurrentDictionary<string, DateTime> RecentTimedConfirmations =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<GiveawayCommandService> _logger;
    private readonly GiveawayService _giveawayService;

    public GiveawayCommandService(
        ILogger<GiveawayCommandService> logger,
        GiveawayService giveawayService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _giveawayService = giveawayService ?? throw new ArgumentNullException(nameof(giveawayService));
    }

    /// <summary>
    /// Processes giveaway commands and returns a response message.
    /// Also handles custom entry keywords like !enter for active giveaways.
    /// </summary>
    public async Task<string?> ProcessCommandAsync(
        string command, 
        string username, 
        string platform, 
        bool isModerator,
        bool isSubscriber = false,
        bool isFollower = false,
        int watchtimeMinutes = 0)
    {
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("!"))
            return null;

        // Handle custom entry keywords (e.g., !enter, !join) for active giveaways
        if (!command.StartsWith("!giveaway"))
        {
            var activeGiveaway = await _giveawayService.GetActiveGiveawayAsync();
            if (activeGiveaway != null)
            {
                var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // The chat message keeps its prefix ("!enter") while the
                // stored keyword does not ("enter") — strip the leading '!'
                // from BOTH sides before comparing, or the match can never
                // succeed and viewers get no entry and no reply.
                var keyword = parts[0].TrimStart('!').Trim();
                var storedKeyword = activeGiveaway.EntryKeyword?.Trim().TrimStart('!');

                // Check if this matches the giveaway's entry keyword
                if (!string.IsNullOrWhiteSpace(storedKeyword)
                    && keyword.Equals(storedKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleEnterCommandAsync(username, platform, isSubscriber, isFollower, watchtimeMinutes);
                }
            }

            return null; // Not a giveaway command
        }

        var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (commandParts.Length < 2)
        {
            return "Usage: !giveaway <start|enter|winner|redraw|cancel|status>";
        }

        var subCommand = commandParts[1].ToLower();

        try
        {
            return subCommand switch
            {
                "start" => isModerator
                    ? await HandleStartCommandAsync()
                    : "⚠️ Only moderators can start giveaways.",

                "enter" => await HandleEnterCommandAsync(username, platform, isSubscriber, isFollower, watchtimeMinutes),

                "winner" or "draw" => isModerator
                    ? await HandleWinnerCommandAsync()
                    : "⚠️ Only moderators can draw winners.",

                "redraw" or "reroll" => isModerator
                    ? await HandleRedrawCommandAsync()
                    : "⚠️ Only moderators can re-draw winners.",

                "cancel" => isModerator
                    ? await HandleCancelCommandAsync()
                    : "⚠️ Only moderators can cancel giveaways.",

                "status" => await HandleStatusCommandAsync(),

                _ => "Usage: !giveaway <start|enter|winner|redraw|cancel|status>"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing giveaway command: {Command}", command);
            return "❌ An error occurred while processing the giveaway command.";
        }
    }

    private async Task<string> HandleStartCommandAsync()
    {
        // Check if there's already an active giveaway
        var activeGiveaway = await _giveawayService.GetActiveGiveawayAsync();
        if (activeGiveaway != null)
        {
            return $"⚠️ A giveaway is already active: {activeGiveaway.Name}. Use !giveaway cancel to end it first.";
        }

        // Get all pending giveaways
        var allGiveaways = await _giveawayService.GetAllGiveawaysAsync();
        var pendingGiveaway = allGiveaways.FirstOrDefault(g => g.Status == Data.Entities.GiveawayStatus.Pending);

        if (pendingGiveaway == null)
        {
            return "⚠️ No giveaway configured. Create one in the bot UI first!";
        }

        // Start the giveaway
        var success = await _giveawayService.StartGiveawayAsync(pendingGiveaway.Id);
        if (success)
        {
            return $"🎉 {pendingGiveaway.Name} has started! Type {pendingGiveaway.EntryKeyword} to enter!";
        }

        return "❌ Failed to start the giveaway.";
    }

    private async Task<string> HandleEnterCommandAsync(
        string username, 
        string platform, 
        bool isSubscriber, 
        bool isFollower,
        int watchtimeMinutes)
    {
        // Get active giveaway
        var activeGiveaway = await _giveawayService.GetActiveGiveawayAsync();
        if (activeGiveaway == null)
        {
            return "⚠️ No active giveaway right now.";
        }

        // Process entry
        var (success, message) = await _giveawayService.EnterGiveawayAsync(
            activeGiveaway.Id,
            username,
            platform,
            null, // platformUserId (can be added later)
            isSubscriber,
            isFollower,
            watchtimeMinutes);

        // Timed intake: throttle per-entry confirmations so a flood of entries
        // doesn't flood chat — each viewer gets at most one confirmation per 60s.
        if (success && _giveawayService.CurrentIntakeMode == GiveawayIntakeMode.Timed)
        {
            if (WasRecentlyConfirmed(username, platform))
            {
                return null;
            }

            RecentTimedConfirmations[$"{username}:{platform}"] = DateTime.UtcNow;
        }

        return success
            ? $"✅ @{username}, {message}"
            : $"⚠️ @{username}, {message}";
    }

    /// <summary>
    /// Returns whether this username+platform was confirmed within the timed-mode
    /// confirmation window, pruning the expired entry if present.
    /// </summary>
    private static bool WasRecentlyConfirmed(string username, string platform)
    {
        var key = $"{username}:{platform}";

        if (RecentTimedConfirmations.Count > 1000)
        {
            // Opportunistic prune so the map cannot grow unbounded.
            var now = DateTime.UtcNow;
            foreach (var pair in RecentTimedConfirmations)
            {
                if (now - pair.Value >= TimedConfirmationWindow)
                {
                    RecentTimedConfirmations.TryRemove(pair.Key, out _);
                }
            }
        }

        if (!RecentTimedConfirmations.TryGetValue(key, out var lastConfirmed))
        {
            return false;
        }

        if (DateTime.UtcNow - lastConfirmed < TimedConfirmationWindow)
        {
            return true;
        }

        RecentTimedConfirmations.TryRemove(key, out _);
        return false;
    }

    private async Task<string> HandleWinnerCommandAsync()
    {
        // Get active giveaway
        var activeGiveaway = await _giveawayService.GetActiveGiveawayAsync();
        if (activeGiveaway == null)
        {
            return "⚠️ No active giveaway to draw from.";
        }

        // Draw winners
        var (success, winners, message) = await _giveawayService.DrawWinnersAsync(activeGiveaway.Id);

        if (success && winners.Count > 0)
        {
            var entries = await _giveawayService.GetGiveawayEntriesAsync(activeGiveaway.Id);
            var totalEntries = entries.Sum(e => e.EntryCount);
            var uniqueParticipants = entries.Count();

            return $"🎊 {message} (Total entries: {totalEntries}, Participants: {uniqueParticipants})";
        }

        return "❌ Failed to draw winners or no entries found.";
    }

    private async Task<string> HandleRedrawCommandAsync()
    {
        // Redraw applies to the most recent completed giveaway (a live
        // giveaway must be drawn first; previous winners are excluded).
        var allGiveaways = await _giveawayService.GetAllGiveawaysAsync();
        var completedGiveaway = allGiveaways
            .Where(g => g.Status == Data.Entities.GiveawayStatus.Completed)
            .OrderByDescending(g => g.EndedAt ?? g.CreatedAt)
            .FirstOrDefault();

        if (completedGiveaway == null)
        {
            return "⚠️ No completed giveaway to re-draw. Draw winners first with !giveaway winner.";
        }

        var (success, winners, message) = await _giveawayService.RedrawWinnersAsync(completedGiveaway.Id);

        if (success && winners.Count > 0)
        {
            return $"🎊 {message} (Previous winners were excluded.)";
        }

        return $"❌ {message}";
    }

    private async Task<string> HandleCancelCommandAsync()
    {
        // Get active giveaway
        var activeGiveaway = await _giveawayService.GetActiveGiveawayAsync();
        if (activeGiveaway == null)
        {
            return "⚠️ No active giveaway to cancel.";
        }

        // Cancel it
        var success = await _giveawayService.CancelGiveawayAsync(activeGiveaway.Id);
        if (success)
        {
            return $"❌ Giveaway '{activeGiveaway.Name}' has been cancelled.";
        }

        return "❌ Failed to cancel the giveaway.";
    }

    private async Task<string> HandleStatusCommandAsync()
    {
        // Get active giveaway
        var activeGiveaway = await _giveawayService.GetActiveGiveawayAsync();
        if (activeGiveaway == null)
        {
            return "ℹ️ No active giveaway right now.";
        }

        // Get entry stats
        var entries = await _giveawayService.GetGiveawayEntriesAsync(activeGiveaway.Id);
        var totalEntries = entries.Sum(e => e.EntryCount);
        var uniqueParticipants = entries.Count();

        var maxEntriesText = activeGiveaway.MaxEntries.HasValue 
            ? $" (Max: {activeGiveaway.MaxEntries})" 
            : "";

        var winnersText = activeGiveaway.WinnerCount == 1 
            ? "1 winner" 
            : $"{activeGiveaway.WinnerCount} winners";

        var entriesPerMinute = await _giveawayService.GetCurrentEntriesPerMinuteAsync();
        var modeText = $" Mode: {_giveawayService.CurrentIntakeMode} ({entriesPerMinute} entries/min).";

        return $"🎁 {activeGiveaway.Name}: {totalEntries} entries{maxEntriesText}, {uniqueParticipants} participants. " +
               $"Type {activeGiveaway.EntryKeyword} to enter! Drawing {winnersText}.{modeText}";
    }
}
