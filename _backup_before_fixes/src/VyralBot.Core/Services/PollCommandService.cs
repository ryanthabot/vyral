using Microsoft.Extensions.Logging;
using System.Text;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for handling poll-related chat commands.
/// Processes !poll commands for creating, voting, and viewing results.
/// </summary>
public class PollCommandService
{
    private readonly ILogger<PollCommandService> _logger;
    private readonly PollService _pollService;

    public PollCommandService(
        ILogger<PollCommandService> logger,
        PollService pollService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollService = pollService ?? throw new ArgumentNullException(nameof(pollService));
    }

    /// <summary>
    /// Processes poll commands and returns a response message.
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
        if (string.IsNullOrWhiteSpace(command) || !command.StartsWith("!poll"))
            return null;

        var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (commandParts.Length < 2)
        {
            return "Usage: !poll <start|vote|results|end|cancel|status>";
        }

        var subCommand = commandParts[1].ToLower();

        try
        {
            return subCommand switch
            {
                "start" => isModerator
                    ? await HandleStartCommandAsync()
                    : "⚠️ Only moderators can start polls.",

                "vote" => await HandleVoteCommandAsync(commandParts, username, platform, isSubscriber, isFollower, watchtimeMinutes),

                "results" => await HandleResultsCommandAsync(),

                "end" => isModerator
                    ? await HandleEndCommandAsync()
                    : "⚠️ Only moderators can end polls.",

                "cancel" => isModerator
                    ? await HandleCancelCommandAsync()
                    : "⚠️ Only moderators can cancel polls.",

                "status" => await HandleStatusCommandAsync(),

                _ => "Usage: !poll <start|vote|results|end|cancel|status>"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing poll command: {Command}", command);
            return "❌ An error occurred while processing the poll command.";
        }
    }

    private async Task<string> HandleStartCommandAsync()
    {
        // Check if there's already an active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll != null)
        {
            return $"⚠️ A poll is already active: {activePoll.Question}. Use !poll end to close it first.";
        }

        // Get all pending polls
        var allPolls = await _pollService.GetAllPollsAsync();
        var pendingPoll = allPolls.FirstOrDefault(p => p.Status == Data.Entities.PollStatus.Pending);

        if (pendingPoll == null)
        {
            return "⚠️ No poll configured. Create one in the bot UI first!";
        }

        // Start the poll
        var success = await _pollService.StartPollAsync(pendingPoll.Id);
        if (success)
        {
            // Parse options to show in message
            var options = System.Text.Json.JsonSerializer.Deserialize<string[]>(pendingPoll.Options) ?? Array.Empty<string>();
            var optionsText = new StringBuilder();
            for (int i = 0; i < options.Length; i++)
            {
                optionsText.Append($" ({i + 1}) {options[i]}");
            }

            return $"📊 Poll started: {pendingPoll.Question} - Options:{optionsText} - Vote with !poll vote <number>";
        }

        return "❌ Failed to start the poll.";
    }

    private async Task<string> HandleVoteCommandAsync(
        string[] commandParts,
        string username,
        string platform,
        bool isSubscriber,
        bool isFollower,
        int watchtimeMinutes)
    {
        // Get active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll == null)
        {
            return "⚠️ No active poll right now.";
        }

        // Parse vote option
        if (commandParts.Length < 3)
        {
            return "⚠️ Please specify your vote: !poll vote <option number>";
        }

        if (!int.TryParse(commandParts[2], out int optionNumber) || optionNumber < 1)
        {
            return "⚠️ Please provide a valid option number (e.g., !poll vote 1)";
        }

        // Convert 1-based to 0-based index
        int optionIndex = optionNumber - 1;

        // Process vote
        var (success, message) = await _pollService.VoteAsync(
            activePoll.Id,
            username,
            platform,
            optionIndex,
            null, // platformUserId (can be added later)
            isSubscriber,
            isFollower,
            watchtimeMinutes);

        return success
            ? $"✅ @{username}, {message}"
            : $"⚠️ @{username}, {message}";
    }

    private async Task<string> HandleResultsCommandAsync()
    {
        // Get active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll == null)
        {
            // Try to get most recent completed poll
            var allPolls = await _pollService.GetAllPollsAsync();
            var lastPoll = allPolls.FirstOrDefault(p => p.Status == Data.Entities.PollStatus.Completed);

            if (lastPoll == null)
            {
                return "⚠️ No poll results available.";
            }

            activePoll = lastPoll;
        }

        // Get current results
        var results = await _pollService.GetCurrentResultsAsync(activePoll.Id);
        if (results == null || results.Count == 0)
        {
            return "⚠️ No votes yet.";
        }

        // Format results
        var resultsText = new StringBuilder();
        resultsText.AppendLine($"📊 {activePoll.Question}");
        
        int totalVotes = results.Values.Sum();
        int optionNum = 1;
        foreach (var (option, votes) in results.OrderByDescending(r => r.Value))
        {
            double percentage = totalVotes > 0 ? (votes * 100.0 / totalVotes) : 0;
            resultsText.AppendLine($"  ({optionNum}) {option}: {votes} votes ({percentage:F1}%)");
            optionNum++;
        }

        resultsText.Append($"Total: {totalVotes} votes");

        return resultsText.ToString();
    }

    private async Task<string> HandleEndCommandAsync()
    {
        // Get active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll == null)
        {
            return "⚠️ No active poll to end.";
        }

        // End it and get results
        var (success, results, message) = await _pollService.EndPollAsync(activePoll.Id);

        if (success)
        {
            var totalVotes = results.Values.Sum();
            return $"🏁 Poll ended! {message} Total votes: {totalVotes}";
        }

        return "❌ Failed to end the poll.";
    }

    private async Task<string> HandleCancelCommandAsync()
    {
        // Get active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll == null)
        {
            return "⚠️ No active poll to cancel.";
        }

        // Cancel it
        var success = await _pollService.CancelPollAsync(activePoll.Id);
        if (success)
        {
            return $"❌ Poll '{activePoll.Question}' has been cancelled.";
        }

        return "❌ Failed to cancel the poll.";
    }

    private async Task<string> HandleStatusCommandAsync()
    {
        // Get active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll == null)
        {
            return "ℹ️ No active poll right now.";
        }

        // Parse options
        var options = System.Text.Json.JsonSerializer.Deserialize<string[]>(activePoll.Options) ?? Array.Empty<string>();
        
        // Build status message
        var optionsText = new StringBuilder();
        for (int i = 0; i < options.Length; i++)
        {
            if (i > 0) optionsText.Append(", ");
            optionsText.Append($"({i + 1}) {options[i]}");
        }

        return $"📊 Active Poll: {activePoll.Question} - Options: {optionsText} - " +
               $"Total votes: {activePoll.TotalVotes}. Vote with !poll vote <number>";
    }
}
