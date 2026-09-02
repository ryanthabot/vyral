using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for handling giveaway-related chat commands.
/// Processes !giveaway commands for creating, starting, entering, and drawing winners.
/// </summary>
public class GiveawayCommandService
{
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
                var keyword = parts[0].ToLower();

                // Check if this matches the giveaway's entry keyword
                if (keyword.Equals(activeGiveaway.EntryKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return await HandleEnterCommandAsync(username, platform, isSubscriber, isFollower, watchtimeMinutes);
                }
            }

            return null; // Not a giveaway command
        }

        var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (commandParts.Length < 2)
        {
            return "Usage: !giveaway <start|enter|winner|cancel|status>";
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
                
                "cancel" => isModerator 
                    ? await HandleCancelCommandAsync() 
                    : "⚠️ Only moderators can cancel giveaways.",
                
                "status" => await HandleStatusCommandAsync(),
                
                _ => "Usage: !giveaway <start|enter|winner|cancel|status>"
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

        return success 
            ? $"✅ @{username}, {message}" 
            : $"⚠️ @{username}, {message}";
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

        return $"🎁 {activeGiveaway.Name}: {totalEntries} entries{maxEntriesText}, {uniqueParticipants} participants. " +
               $"Type {activeGiveaway.EntryKeyword} to enter! Drawing {winnersText}.";
    }
}
