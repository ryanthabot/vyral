using Microsoft.Extensions.Logging;
using System.Text.Json;
using VyralBot.Core.Services.Currency;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for managing polls with cross-platform voting support.
/// Handles poll lifecycle, vote validation, and result calculation.
/// One-vote-per-viewer is enforced per (username, platform) row AND across
/// linked identities: a viewer who linked their Twitch and Discord accounts
/// gets exactly one vote, not two.
/// </summary>
public class PollService
{
    private readonly IDataService _dataService;
    private readonly ILogger<PollService> _logger;
    private readonly ILinkedAccountService? _linkedAccounts;

    // Serializes the vote persist against poll completion so a vote can
    // never land after the results snapshot (the vote would be stored but
    // not counted) — same pattern as the giveaway draw gate.
    private readonly SemaphoreSlim _endGate = new(1, 1);

    public PollService(
        IDataService dataService,
        ILogger<PollService> logger,
        ILinkedAccountService? linkedAccounts = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _linkedAccounts = linkedAccounts;
    }

    /// <summary>
    /// Creates a new poll
    /// </summary>
    public async Task<Poll> CreatePollAsync(
        string question,
        string[] options,
        string creatorUsername,
        DateTime? scheduledEndTime = null,
        bool allowMultipleVotes = false,
        string allowedPlatforms = "All",
        bool followersOnly = false,
        bool subscribersOnly = false,
        int minWatchtimeMinutes = 0)
    {
        if (options.Length < 2)
        {
            throw new ArgumentException("Poll must have at least 2 options.", nameof(options));
        }

        var poll = new Poll
        {
            Question = question,
            Options = JsonSerializer.Serialize(options),
            CreatorUsername = creatorUsername,
            Status = PollStatus.Pending,
            ScheduledEndTime = scheduledEndTime,
            AllowMultipleVotes = allowMultipleVotes,
            AllowedPlatforms = allowedPlatforms,
            FollowersOnly = followersOnly,
            SubscribersOnly = subscribersOnly,
            MinWatchtimeMinutes = minWatchtimeMinutes
        };

        await _dataService.SavePollAsync(poll);
        _logger.LogInformation("Created poll: {Question} (ID: {Id})", question, poll.Id);

        return poll;
    }

    /// <summary>
    /// Starts an existing poll and begins accepting votes
    /// </summary>
    public async Task<bool> StartPollAsync(Guid pollId)
    {
        var poll = await _dataService.GetPollAsync(pollId);
        if (poll == null)
        {
            _logger.LogWarning("Poll not found: {Id}", pollId);
            return false;
        }

        if (poll.Status != PollStatus.Pending)
        {
            _logger.LogWarning("Cannot start poll in status: {Status}", poll.Status);
            return false;
        }

        // Check if there's already an active poll
        var activePoll = await _dataService.GetActivePollAsync();
        if (activePoll != null)
        {
            _logger.LogWarning("Cannot start poll - another is already active: {Question}", activePoll.Question);
            return false;
        }

        poll.Status = PollStatus.Active;
        poll.StartedAt = DateTime.UtcNow;
        await _dataService.SavePollAsync(poll);

        _logger.LogInformation("Started poll: {Question} (ID: {Id})", poll.Question, poll.Id);
        return true;
    }

    /// <summary>
    /// Processes a user vote in the active poll
    /// </summary>
    public async Task<(bool Success, string Message)> VoteAsync(
        Guid pollId,
        string username,
        string platform,
        int optionIndex,
        string? platformUserId = null,
        bool isSubscriber = false,
        bool isFollower = false,
        int watchtimeMinutes = 0)
    {
        var poll = await _dataService.GetPollAsync(pollId);
        if (poll == null)
        {
            return (false, "Poll not found.");
        }

        if (poll.Status != PollStatus.Active)
        {
            return (false, "This poll is not currently active.");
        }

        // Validate option index
        var options = JsonSerializer.Deserialize<string[]>(poll.Options) ?? Array.Empty<string>();
        if (optionIndex < 0 || optionIndex >= options.Length)
        {
            return (false, $"Invalid option. Please choose 1-{options.Length}.");
        }

        // Check platform eligibility
        if (poll.AllowedPlatforms != "All")
        {
            var allowedPlatforms = poll.AllowedPlatforms.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (!allowedPlatforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
            {
                return (false, $"This poll is not open to {platform} users.");
            }
        }

        // Check follower requirement
        if (poll.FollowersOnly && !isFollower)
        {
            return (false, "You must be a follower to vote in this poll.");
        }

        // Check subscriber requirement
        if (poll.SubscribersOnly && !isSubscriber)
        {
            return (false, "You must be a subscriber to vote in this poll.");
        }

        // Check watchtime requirement
        if (poll.MinWatchtimeMinutes > 0 && watchtimeMinutes < poll.MinWatchtimeMinutes)
        {
            return (false, $"You need at least {poll.MinWatchtimeMinutes} minutes of watchtime to vote.");
        }

        await _endGate.WaitAsync();
        try
        {
            // Re-validate INSIDE the gate: a poll that ended while this vote
            // waited on the gate is rejected here instead of being stored
            // after the results snapshot (stored-but-never-counted).
            var fresh = await _dataService.GetPollAsync(pollId);
            if (fresh == null || fresh.Status != PollStatus.Active)
            {
                return (false, "This poll is not currently active.");
            }

            // Check if user already voted
            var existingVote = await _dataService.GetPollVoteAsync(poll.Id, username, platform);

            // Cross-platform identity: a viewer with linked accounts gets ONE
            // vote — a vote cast from any linked (username, platform) pair
            // counts as this viewer's vote (change-or-reject below applies).
            if (existingVote == null && _linkedAccounts != null)
            {
                existingVote = await FindLinkedVoteAsync(poll.Id, username, platform);
            }

            if (existingVote != null)
            {
                if (!poll.AllowMultipleVotes)
                {
                    return (false, $"You have already voted for option {existingVote.OptionIndex + 1}!");
                }

                // Update existing vote
                existingVote.OptionIndex = optionIndex;
                existingVote.VotedAt = DateTime.UtcNow;
                await _dataService.SavePollVoteAsync(existingVote);

                _logger.LogInformation("User changed vote: {Username} ({Platform}) - Option {Option}",
                    username, platform, optionIndex + 1);

                return (true, $"Vote changed to option {optionIndex + 1}: {options[optionIndex]}");
            }

            // Create new vote
            var vote = new PollVote
            {
                PollId = poll.Id,
                Username = username,
                Platform = platform,
                PlatformUserId = platformUserId,
                OptionIndex = optionIndex,
                IsSubscriber = isSubscriber,
                IsFollower = isFollower,
                WatchtimeMinutes = watchtimeMinutes
            };

            await _dataService.SavePollVoteAsync(vote);

            // Update poll stats
            poll.TotalVotes = await _dataService.GetPollVoteCountAsync(poll.Id);
            await _dataService.SavePollAsync(poll);

            _logger.LogInformation("User voted in poll: {Username} ({Platform}) - Option {Option}",
                username, platform, optionIndex + 1);

            return (true, $"Vote recorded for option {optionIndex + 1}: {options[optionIndex]}");
        }
        finally
        {
            _endGate.Release();
        }
    }

    /// <summary>
    /// Looks for an existing vote in this poll from any OTHER account linked
    /// to the same unified viewer identity (e.g. the viewer's Discord account
    /// when they are voting from Twitch). Link lookups are best-effort: a
    /// failure is logged and voting continues on the direct pair only.
    /// </summary>
    private async Task<PollVote?> FindLinkedVoteAsync(Guid pollId, string username, string platform)
    {
        try
        {
            var viewerId = await _linkedAccounts!.GetViewerIdAsync(username, platform);
            if (string.IsNullOrEmpty(viewerId))
            {
                return null;
            }

            var links = await _linkedAccounts.GetLinkedAccountsAsync(viewerId);
            foreach (var link in links)
            {
                if (link.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                    && link.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // The direct pair — already checked by the caller.
                }

                var vote = await _dataService.GetPollVoteAsync(pollId, link.Username, link.Platform);
                if (vote != null)
                {
                    _logger.LogInformation(
                        "Cross-platform vote dedup: {Username} ({Platform}) linked to {OtherUsername} ({OtherPlatform}) who already voted",
                        username, platform, link.Username, link.Platform);
                    return vote;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve linked vote identity for {Username} ({Platform}); falling back to direct-pair dedup",
                username, platform);
        }

        return null;
    }

    /// <summary>
    /// Ends an active poll and calculates results. The vote/end gate is held
    /// across the results snapshot and the completion save, so votes racing
    /// the end either land in the results or are rejected (never
    /// stored-but-uncounted). Ties are announced as ties; a poll with zero
    /// votes completes without claiming a winner.
    /// </summary>
    public async Task<(bool Success, Dictionary<string, int> Results, string Message)> EndPollAsync(Guid pollId)
    {
        await _endGate.WaitAsync();
        try
        {
            var poll = await _dataService.GetPollAsync(pollId);
            if (poll == null)
            {
                return (false, new Dictionary<string, int>(), "Poll not found.");
            }

            // Re-checked INSIDE the gate: an end that completed while this
            // call waited on the gate is rejected here, not double-processed.
            if (poll.Status != PollStatus.Active)
            {
                return (false, new Dictionary<string, int>(), "This poll is not active.");
            }

            var options = JsonSerializer.Deserialize<string[]>(poll.Options) ?? Array.Empty<string>();
            var voteResults = await _dataService.GetPollVoteResultsAsync(poll.Id);

            // Build results dictionary
            var results = new Dictionary<string, int>();
            for (int i = 0; i < options.Length; i++)
            {
                results[options[i]] = voteResults.ContainsKey(i) ? voteResults[i] : 0;
            }

            // End poll
            poll.Status = PollStatus.Completed;
            poll.EndedAt = DateTime.UtcNow;
            poll.VoteResults = JsonSerializer.Serialize(voteResults);
            poll.TotalVotes = voteResults.Values.Sum();
            await _dataService.SavePollAsync(poll);

            _logger.LogInformation("Poll completed: {Question} - Total votes: {TotalVotes}",
                poll.Question, poll.TotalVotes);

            string message = FormatResultsMessage(poll.Question, results);

            return (true, results, message);
        }
        finally
        {
            _endGate.Release();
        }
    }

    /// <summary>
    /// Builds the end-of-poll announcement: an explicit no-votes notice, a
    /// tie call-out listing every tied option, or the plain winner line.
    /// </summary>
    private static string FormatResultsMessage(string question, Dictionary<string, int> results)
    {
        int totalVotes = results.Values.Sum();

        if (totalVotes == 0)
        {
            return $"📊 Poll Results — {question}: no votes were cast.";
        }

        int topVotes = results.Values.Max();
        var topOptions = results.Where(r => r.Value == topVotes).Select(r => r.Key).ToList();

        if (topOptions.Count > 1)
        {
            return $"📊 Poll Results — {question}: it's a tie between {string.Join(" and ", topOptions)} ({topVotes} votes each)!";
        }

        return $"📊 Poll Results: {topOptions[0]} wins with {topVotes} votes!";
    }

    /// <summary>
    /// Gets the current results for an active poll
    /// </summary>
    public async Task<Dictionary<string, int>?> GetCurrentResultsAsync(Guid pollId)
    {
        var poll = await _dataService.GetPollAsync(pollId);
        if (poll == null)
        {
            return null;
        }

        var options = JsonSerializer.Deserialize<string[]>(poll.Options) ?? Array.Empty<string>();
        var voteResults = await _dataService.GetPollVoteResultsAsync(poll.Id);

        var results = new Dictionary<string, int>();
        for (int i = 0; i < options.Length; i++)
        {
            results[options[i]] = voteResults.ContainsKey(i) ? voteResults[i] : 0;
        }

        return results;
    }

    /// <summary>
    /// Cancels an active poll
    /// </summary>
    public async Task<bool> CancelPollAsync(Guid pollId)
    {
        var poll = await _dataService.GetPollAsync(pollId);
        if (poll == null)
        {
            return false;
        }

        if (poll.Status == PollStatus.Completed || poll.Status == PollStatus.Cancelled)
        {
            _logger.LogWarning("Cannot cancel poll in status: {Status}", poll.Status);
            return false;
        }

        poll.Status = PollStatus.Cancelled;
        poll.EndedAt = DateTime.UtcNow;
        await _dataService.SavePollAsync(poll);

        _logger.LogInformation("Cancelled poll: {Question} (ID: {Id})", poll.Question, poll.Id);
        return true;
    }

    /// <summary>
    /// Gets the currently active poll
    /// </summary>
    public async Task<Poll?> GetActivePollAsync()
    {
        return await _dataService.GetActivePollAsync();
    }

    /// <summary>
    /// Gets all polls ordered by creation date
    /// </summary>
    public async Task<IEnumerable<Poll>> GetAllPollsAsync()
    {
        return await _dataService.GetPollsAsync();
    }

    /// <summary>
    /// Gets votes for a specific poll
    /// </summary>
    public async Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId)
    {
        return await _dataService.GetPollVotesAsync(pollId);
    }
}
