using Microsoft.Extensions.Logging;
using System.Text.Json;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for managing polls with cross-platform voting support.
/// Handles poll lifecycle, vote validation, and result calculation.
/// </summary>
public class PollService
{
    private readonly IDataService _dataService;
    private readonly ILogger<PollService> _logger;

    public PollService(IDataService dataService, ILogger<PollService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // Check if user already voted
        var existingVote = await _dataService.GetPollVoteAsync(poll.Id, username, platform);
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

    /// <summary>
    /// Ends an active poll and calculates results
    /// </summary>
    public async Task<(bool Success, Dictionary<string, int> Results, string Message)> EndPollAsync(Guid pollId)
    {
        var poll = await _dataService.GetPollAsync(pollId);
        if (poll == null)
        {
            return (false, new Dictionary<string, int>(), "Poll not found.");
        }

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
        await _dataService.SavePollAsync(poll);

        _logger.LogInformation("Poll completed: {Question} - Total votes: {TotalVotes}", 
            poll.Question, poll.TotalVotes);

        // Format results message
        var winner = results.OrderByDescending(r => r.Value).First();
        var message = $"📊 Poll Results: {winner.Key} wins with {winner.Value} votes!";

        return (true, results, message);
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
