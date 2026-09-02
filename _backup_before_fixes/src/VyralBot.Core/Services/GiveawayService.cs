using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for managing giveaways with cross-platform entry support.
/// Handles giveaway lifecycle, entry validation, and winner selection.
/// </summary>
public class GiveawayService
{
    private readonly IDataService _dataService;
    private readonly ILogger<GiveawayService> _logger;
    private readonly Random _random = new();

    public GiveawayService(IDataService dataService, ILogger<GiveawayService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new giveaway
    /// </summary>
    public async Task<Giveaway> CreateGiveawayAsync(
        string name,
        string description,
        string entryKeyword,
        int winnerCount = 1,
        int? maxEntries = null,
        DateTime? scheduledEndTime = null,
        bool subscriberBonusEnabled = false,
        int subscriberBonusEntries = 1,
        int minWatchtimeMinutes = 0,
        string allowedPlatforms = "All",
        bool followersOnly = false,
        bool subscribersOnly = false)
    {
        var giveaway = new Giveaway
        {
            Name = name,
            Description = description,
            EntryKeyword = entryKeyword,
            Status = GiveawayStatus.Pending,
            WinnerCount = winnerCount,
            MaxEntries = maxEntries,
            ScheduledEndTime = scheduledEndTime,
            SubscriberBonusEnabled = subscriberBonusEnabled,
            SubscriberBonusEntries = subscriberBonusEntries,
            MinWatchtimeMinutes = minWatchtimeMinutes,
            AllowedPlatforms = allowedPlatforms,
            FollowersOnly = followersOnly,
            SubscribersOnly = subscribersOnly
        };

        await _dataService.SaveGiveawayAsync(giveaway);
        _logger.LogInformation("Created giveaway: {Name} (ID: {Id})", name, giveaway.Id);

        return giveaway;
    }

    /// <summary>
    /// Starts an existing giveaway and begins accepting entries
    /// </summary>
    public async Task<bool> StartGiveawayAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            _logger.LogWarning("Giveaway not found: {Id}", giveawayId);
            return false;
        }

        if (giveaway.Status != GiveawayStatus.Pending)
        {
            _logger.LogWarning("Cannot start giveaway in status: {Status}", giveaway.Status);
            return false;
        }

        // Check if there's already an active giveaway
        var activeGiveaway = await _dataService.GetActiveGiveawayAsync();
        if (activeGiveaway != null)
        {
            _logger.LogWarning("Cannot start giveaway - another is already active: {Name}", activeGiveaway.Name);
            return false;
        }

        giveaway.Status = GiveawayStatus.Active;
        giveaway.StartedAt = DateTime.UtcNow;
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("Started giveaway: {Name} (ID: {Id})", giveaway.Name, giveaway.Id);
        return true;
    }

    /// <summary>
    /// Processes a user entry into the active giveaway
    /// </summary>
    public async Task<(bool Success, string Message)> EnterGiveawayAsync(
        Guid giveawayId,
        string username,
        string platform,
        string? platformUserId = null,
        bool isSubscriber = false,
        bool isFollower = false,
        int watchtimeMinutes = 0,
        string? message = null)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            return (false, "Giveaway not found.");
        }

        if (giveaway.Status != GiveawayStatus.Active)
        {
            return (false, "This giveaway is not currently active.");
        }

        // Check platform eligibility
        if (giveaway.AllowedPlatforms != "All")
        {
            var allowedPlatforms = giveaway.AllowedPlatforms.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (!allowedPlatforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
            {
                return (false, $"This giveaway is not open to {platform} users.");
            }
        }

        // Check follower requirement
        if (giveaway.FollowersOnly && !isFollower)
        {
            return (false, "You must be a follower to enter this giveaway.");
        }

        // Check subscriber requirement
        if (giveaway.SubscribersOnly && !isSubscriber)
        {
            return (false, "You must be a subscriber to enter this giveaway.");
        }

        // Check watchtime requirement
        if (giveaway.MinWatchtimeMinutes > 0 && watchtimeMinutes < giveaway.MinWatchtimeMinutes)
        {
            return (false, $"You need at least {giveaway.MinWatchtimeMinutes} minutes of watchtime to enter.");
        }

        // Check if user already entered
        var existingEntry = await _dataService.GetGiveawayEntryAsync(giveaway.Id, username, platform);
        if (existingEntry != null)
        {
            return (false, "You have already entered this giveaway!");
        }

        // Check max entries limit
        if (giveaway.MaxEntries.HasValue)
        {
            var currentEntryCount = await _dataService.GetGiveawayEntryCountAsync(giveaway.Id);
            if (currentEntryCount >= giveaway.MaxEntries.Value)
            {
                return (false, "This giveaway has reached its maximum number of entries.");
            }
        }

        // Calculate entry count (base + subscriber bonus)
        int entryCount = 1;
        if (giveaway.SubscriberBonusEnabled && isSubscriber)
        {
            entryCount += giveaway.SubscriberBonusEntries;
        }

        // Create entry
        var entry = new GiveawayEntry
        {
            GiveawayId = giveaway.Id,
            Username = username,
            Platform = platform,
            PlatformUserId = platformUserId,
            EntryCount = entryCount,
            IsSubscriber = isSubscriber,
            IsFollower = isFollower,
            WatchtimeMinutes = watchtimeMinutes,
            Message = message
        };

        await _dataService.SaveGiveawayEntryAsync(entry);

        // Update giveaway stats
        giveaway.TotalEntries = await _dataService.GetGiveawayEntryCountAsync(giveaway.Id);
        giveaway.UniqueParticipants = await _dataService.GetGiveawayUniqueParticipantsAsync(giveaway.Id);
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("User entered giveaway: {Username} ({Platform}) - {EntryCount} entries", 
            username, platform, entryCount);

        string responseMessage = entryCount > 1
            ? $"You're entered! You have {entryCount} entries (subscriber bonus)."
            : "You're entered! Good luck!";

        return (true, responseMessage);
    }

    /// <summary>
    /// Draws random winner(s) from the active giveaway entries
    /// </summary>
    public async Task<(bool Success, List<string> Winners, string Message)> DrawWinnersAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            return (false, new List<string>(), "Giveaway not found.");
        }

        if (giveaway.Status != GiveawayStatus.Active)
        {
            return (false, new List<string>(), "This giveaway is not active.");
        }

        var entries = (await _dataService.GetGiveawayEntriesAsync(giveaway.Id)).ToList();
        if (entries.Count == 0)
        {
            return (false, new List<string>(), "No entries to draw from!");
        }

        // Create weighted pool (each entry gets EntryCount tickets)
        var entryPool = new List<GiveawayEntry>();
        foreach (var entry in entries)
        {
            for (int i = 0; i < entry.EntryCount; i++)
            {
                entryPool.Add(entry);
            }
        }

        var winners = new List<string>();
        var winnerEntries = new HashSet<Guid>(); // Track to prevent duplicate winners

        int winnersToDraw = Math.Min(giveaway.WinnerCount, entries.Count);

        for (int i = 0; i < winnersToDraw; i++)
        {
            GiveawayEntry? winnerEntry = null;
            int attempts = 0;
            const int maxAttempts = 100;

            // Draw until we find a unique winner or hit max attempts
            while (winnerEntry == null && attempts < maxAttempts)
            {
                int randomIndex = _random.Next(entryPool.Count);
                var candidate = entryPool[randomIndex];

                if (!winnerEntries.Contains(candidate.Id))
                {
                    winnerEntry = candidate;
                }

                attempts++;
            }

            if (winnerEntry != null)
            {
                winnerEntries.Add(winnerEntry.Id);
                winners.Add($"{winnerEntry.Username} ({winnerEntry.Platform})");

                // Mark as winner
                winnerEntry.IsWinner = true;
                winnerEntry.WonAt = DateTime.UtcNow;
                await _dataService.SaveGiveawayEntryAsync(winnerEntry);
            }
        }

        // End giveaway
        giveaway.Status = GiveawayStatus.Completed;
        giveaway.EndedAt = DateTime.UtcNow;
        giveaway.Winners = string.Join(", ", winners);
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("Giveaway completed: {Name} - Winners: {Winners}", giveaway.Name, giveaway.Winners);

        string message = winners.Count == 1
            ? $"🎉 Winner: {winners[0]}!"
            : $"🎉 Winners: {string.Join(", ", winners)}!";

        return (true, winners, message);
    }

    /// <summary>
    /// Cancels an active giveaway
    /// </summary>
    public async Task<bool> CancelGiveawayAsync(Guid giveawayId)
    {
        var giveaway = await _dataService.GetGiveawayAsync(giveawayId);
        if (giveaway == null)
        {
            return false;
        }

        if (giveaway.Status == GiveawayStatus.Completed || giveaway.Status == GiveawayStatus.Cancelled)
        {
            _logger.LogWarning("Cannot cancel giveaway in status: {Status}", giveaway.Status);
            return false;
        }

        giveaway.Status = GiveawayStatus.Cancelled;
        giveaway.EndedAt = DateTime.UtcNow;
        await _dataService.SaveGiveawayAsync(giveaway);

        _logger.LogInformation("Cancelled giveaway: {Name} (ID: {Id})", giveaway.Name, giveaway.Id);
        return true;
    }

    /// <summary>
    /// Gets the currently active giveaway
    /// </summary>
    public async Task<Giveaway?> GetActiveGiveawayAsync()
    {
        return await _dataService.GetActiveGiveawayAsync();
    }

    /// <summary>
    /// Gets all giveaways ordered by creation date
    /// </summary>
    public async Task<IEnumerable<Giveaway>> GetAllGiveawaysAsync()
    {
        return await _dataService.GetGiveawaysAsync();
    }

    /// <summary>
    /// Gets entries for a specific giveaway
    /// </summary>
    public async Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId)
    {
        return await _dataService.GetGiveawayEntriesAsync(giveawayId);
    }
}
