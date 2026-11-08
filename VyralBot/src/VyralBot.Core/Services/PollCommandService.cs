using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace VyralBot.Core.Services;

/// <summary>
/// Service for handling poll-related chat commands.
/// Processes !poll commands for creating, voting, and viewing results, plus
/// the short !vote <c>&lt;n&gt;</c> alias (wave 16) that everyone can use
/// while a poll is active.
/// </summary>
public partial class PollCommandService
{
    /// <summary>Poll question length cap — keeps the started announcement inside one chat line.</summary>
    private const int MaxQuestionLength = 200;

    /// <summary>Per-option length cap for the same reason as the question cap.</summary>
    private const int MaxOptionLength = 100;

    /// <summary>Ad-hoc chat polls accept 2-6 options (UI-created polls are unrestricted).</summary>
    private const int MinChatOptions = 2;
    private const int MaxChatOptions = 6;

    /// <summary>Shortest allowed !poll start duration (the scheduler ticks every 30s).</summary>
    private static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(5);

    /// <summary>Longest allowed !poll start duration — a day is plenty for any stream poll.</summary>
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

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
    /// Processes poll commands and returns a response message. Returns null
    /// for messages that are neither "!poll …" nor "!vote &lt;n&gt;" so the
    /// chat router passes them through untouched.
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
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var commandParts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // !vote <n> alias (everyone): strict first-token match so !votes-style
        // commands from other features are never swallowed. Case-insensitive
        // because chat platforms are inconsistent about command casing.
        if (commandParts[0].Equals("!vote", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await HandleVoteAliasAsync(commandParts, username, platform, isSubscriber, isFollower, watchtimeMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing vote command: {Command}", command);
                return "❌ An error occurred while processing the vote command.";
            }
        }

        if (!command.StartsWith("!poll"))
            return null;

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
                    ? await HandleStartCommandAsync(commandParts, username)
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

    /// <summary>
    /// Duration token for <c>!poll start</c>: one or more h/m/s segments like
    /// "5m", "90s", "1h", "1h30m". Declared with [GeneratedRegex] (partial
    /// class) so it is compiled once, not re-parsed per message.
    /// </summary>
    [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationTokenRegex();

    /// <summary>
    /// Parses a duration token ("5m", "90s", "1h30m"); null when the token is
    /// not a duration at all (it is then part of the question). Throws no
    /// exceptions — out-of-range durations return the range error so the mod
    /// can fix the command.
    /// </summary>
    private static bool TryParseDuration(string token, out TimeSpan? duration, out string? error)
    {
        duration = null;
        error = null;

        var match = DurationTokenRegex().Match(token);
        if (!match.Success
            || (string.IsNullOrEmpty(match.Groups[1].Value)
                && string.IsNullOrEmpty(match.Groups[2].Value)
                && string.IsNullOrEmpty(match.Groups[3].Value)))
        {
            return false; // not a duration token — caller treats it as question text
        }

        var total = TimeSpan.Zero;
        if (int.TryParse(match.Groups[1].Value, out var hours)) total += TimeSpan.FromHours(hours);
        if (int.TryParse(match.Groups[2].Value, out var minutes)) total += TimeSpan.FromMinutes(minutes);
        if (int.TryParse(match.Groups[3].Value, out var seconds)) total += TimeSpan.FromSeconds(seconds);

        if (total < MinDuration || total > MaxDuration)
        {
            error = $"⚠️ Poll duration must be between {MinDuration.TotalSeconds:F0}s and {MaxDuration.TotalHours:F0}h.";
            return true;
        }

        duration = total;
        return true;
    }

    private async Task<string> HandleStartCommandAsync(string[] commandParts, string username)
    {
        // Check if there's already an active poll
        var activePoll = await _pollService.GetActivePollAsync();
        if (activePoll != null)
        {
            return $"⚠️ A poll is already active: {activePoll.Question}. Use !poll end to close it first.";
        }

        // "!poll start <anything>" creates and starts an ad-hoc chat poll
        // (wave 16); bare "!poll start" keeps the legacy behavior of starting
        // the next pending UI-configured poll.
        if (commandParts.Length > 2)
        {
            return await StartAdHocPollAsync(commandParts, username);
        }

        // Get all pending polls
        var allPolls = await _pollService.GetAllPollsAsync();
        var pendingPoll = allPolls.FirstOrDefault(p => p.Status == Data.Entities.PollStatus.Pending);

        if (pendingPoll == null)
        {
            return "⚠️ No poll configured. Create one in the bot UI first, or start one from chat: !poll start [5m] Question | Option A | Option B";
        }

        // Start the poll
        var success = await _pollService.StartPollAsync(pendingPoll.Id);
        if (success)
        {
            return FormatStartedMessage(pendingPoll.Question,
                System.Text.Json.JsonSerializer.Deserialize<string[]>(pendingPoll.Options) ?? Array.Empty<string>(),
                pendingPoll.ScheduledEndTime);
        }

        return "❌ Failed to start the poll.";
    }

    /// <summary>
    /// Creates + starts an ad-hoc poll from
    /// <c>!poll start [5m] Question? | Option A | Option B [| ...]</c>.
    /// When a duration is given the poll's ScheduledEndTime is set to
    /// now+duration; the existing GiveawayPollSchedulerService then auto-ends
    /// it and announces on every connected platform via PollAutoEndedEvent —
    /// no new timer lives here.
    /// </summary>
    private async Task<string> StartAdHocPollAsync(string[] commandParts, string username)
    {
        const string usage = "Usage: !poll start [5m] Question | Option A | Option B (2-6 options, pipe-separated)";

        // Optional duration is the FIRST token after "start" — anything that
        // doesn't parse as one is treated as the start of the question.
        var remainderParts = commandParts.Skip(2).ToList();
        TimeSpan? duration = null;
        if (TryParseDuration(remainderParts[0], out var parsed, out var durationError))
        {
            if (durationError != null)
            {
                return durationError;
            }

            duration = parsed;
            remainderParts.RemoveAt(0);
            if (remainderParts.Count == 0)
            {
                return usage;
            }
        }

        // Re-join with single spaces (the tokenizer collapsed runs of spaces;
        // option text lives between pipes so inner spaces are preserved).
        var body = string.Join(' ', remainderParts);
        var segments = body.Split('|').Select(s => s.Trim()).ToList();

        var question = segments.FirstOrDefault() ?? string.Empty;
        var options = segments.Skip(1).ToList();

        if (string.IsNullOrWhiteSpace(question) || question.Length > MaxQuestionLength)
        {
            return $"⚠️ The question is required (max {MaxQuestionLength} characters). {usage}";
        }

        if (options.Count < MinChatOptions)
        {
            return $"⚠️ A poll needs at least {MinChatOptions} options. {usage}";
        }

        if (options.Count > MaxChatOptions)
        {
            return $"⚠️ Chat polls support at most {MaxChatOptions} options. {usage}";
        }

        if (options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > MaxOptionLength))
        {
            return $"⚠️ Every option needs text (max {MaxOptionLength} characters). {usage}";
        }

        var scheduledEnd = duration != null ? DateTime.UtcNow + duration : (DateTime?)null;

        var poll = await _pollService.CreatePollAsync(question, options.ToArray(), username, scheduledEnd);
        var started = await _pollService.StartPollAsync(poll.Id);
        if (!started)
        {
            return "❌ Failed to start the poll.";
        }

        _logger.LogInformation("Ad-hoc chat poll started by {Username}: {Question} ({Options} options, duration {Duration})",
            username, question, options.Count, duration);

        return FormatStartedMessage(question, options.ToArray(), scheduledEnd);
    }

    /// <summary>Shared "poll started" announcement for the legacy and ad-hoc start paths.</summary>
    private static string FormatStartedMessage(string question, string[] options, DateTime? scheduledEnd)
    {
        var optionsText = new StringBuilder();
        for (int i = 0; i < options.Length; i++)
        {
            optionsText.Append($" ({i + 1}) {options[i]}");
        }

        var durationText = scheduledEnd != null
            ? $" - ends in {(scheduledEnd.Value - DateTime.UtcNow).TotalMinutes:F0}m"
            : string.Empty;

        return $"📊 Poll started: {question} - Options:{optionsText}{durationText} - Vote with !poll vote <n> or !vote <n>";
    }

    /// <summary>
    /// !vote &lt;n&gt; — the everyone alias for !poll vote. Shares the exact
    /// vote path (eligibility, linked-account dedup, vote-change rules) so
    /// the two spellings can never disagree.
    /// </summary>
    private async Task<string> HandleVoteAliasAsync(
        string[] commandParts,
        string username,
        string platform,
        bool isSubscriber,
        bool isFollower,
        int watchtimeMinutes)
    {
        if (commandParts.Length < 2)
        {
            return "⚠️ Please specify your vote: !vote <option number>";
        }

        if (!int.TryParse(commandParts[1], out int optionNumber) || optionNumber < 1)
        {
            return "⚠️ Please provide a valid option number (e.g., !vote 1)";
        }

        // Reuse the full vote pipeline by delegating with the canonical spelling.
        return await HandleVoteCommandAsync(
            new[] { "!poll", "vote", commandParts[1] },
            username, platform, isSubscriber, isFollower, watchtimeMinutes);
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
