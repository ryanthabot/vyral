using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VyralBot.Core.Services;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for managing polls with voting system
/// </summary>
public partial class PollViewModel : ViewModelBase
{
    private readonly PollService _pollService;
    private readonly IDataService _dataService;
    private readonly ILogger<PollViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<Poll> _polls = new();

    [ObservableProperty]
    private Poll? _selectedPoll;

    [ObservableProperty]
    private ObservableCollection<PollVote> _votes = new();

    // Create poll form fields
    [ObservableProperty]
    private string _newPollQuestion = string.Empty;

    [ObservableProperty]
    private string _newPollOptions = "Option 1\nOption 2\nOption 3";

    [ObservableProperty]
    private bool _newPollAllowMultipleVotes = false;

    [ObservableProperty]
    private int _newPollMinWatchtime = 0;

    [ObservableProperty]
    private bool _newPollFollowersOnly = false;

    [ObservableProperty]
    private bool _newPollSubscribersOnly = false;

    [ObservableProperty]
    private string _newPollAllowedPlatforms = "All";

    [ObservableProperty]
    private DateTime? _newPollScheduledEnd;

    // Statistics
    [ObservableProperty]
    private int _totalVotes;

    [ObservableProperty]
    private string _voteResults = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public PollViewModel(
        PollService pollService,
        IDataService dataService,
        ILogger<PollViewModel> logger)
    {
        _pollService = pollService;
        _dataService = dataService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            await LoadPollsAsync();
            _logger.LogInformation("PollViewModel initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing PollViewModel");
            StatusMessage = $"Error loading polls: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPollsAsync()
    {
        try
        {
            var polls = await _dataService.GetPollsAsync();
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Polls.Clear();
                foreach (var poll in polls.OrderByDescending(p => p.CreatedAt))
                {
                    Polls.Add(poll);
                }
            });

            _logger.LogInformation("Loaded {Count} polls", polls.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading polls");
            throw;
        }
    }

    partial void OnSelectedPollChanged(Poll? value)
    {
        if (value != null)
        {
            _ = LoadVotesForSelectedPollAsync();
        }
    }

    private async Task LoadVotesForSelectedPollAsync()
    {
        if (SelectedPoll == null) return;

        try
        {
            var votes = await _dataService.GetPollVotesAsync(SelectedPoll.Id);
            var totalVotes = await _dataService.GetPollVoteCountAsync(SelectedPoll.Id);
            var results = await _pollService.GetCurrentResultsAsync(SelectedPoll.Id);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Votes.Clear();
                foreach (var vote in votes.OrderByDescending(v => v.VotedAt))
                {
                    Votes.Add(vote);
                }

                TotalVotes = totalVotes;

                // Format results
                if (results != null && results.Count > 0)
                {
                    var resultLines = results.Select(r => $"{r.Key}: {r.Value} votes");
                    VoteResults = string.Join("\n", resultLines);
                }
                else
                {
                    VoteResults = "No votes yet";
                }
            });

            _logger.LogInformation("Loaded {Count} votes for poll {PollId}", votes.Count(), SelectedPoll.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading votes for poll {PollId}", SelectedPoll?.Id.ToString() ?? "unknown");
            StatusMessage = $"Error loading votes: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreatePollAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewPollQuestion))
            {
                StatusMessage = "❌ Poll question is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewPollOptions))
            {
                StatusMessage = "❌ Poll options are required";
                return;
            }

            // Parse options (one per line)
            var options = NewPollOptions.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(o => o.Trim())
                                        .Where(o => !string.IsNullOrEmpty(o))
                                        .ToArray();

            if (options.Length < 2)
            {
                StatusMessage = "❌ Poll must have at least 2 options";
                return;
            }

            IsLoading = true;
            StatusMessage = "Creating poll...";

            var poll = await _pollService.CreatePollAsync(
                NewPollQuestion,
                options,
                "Admin", // Creator username - could get from user context
                NewPollScheduledEnd,
                NewPollAllowMultipleVotes,
                NewPollAllowedPlatforms,
                NewPollFollowersOnly,
                NewPollSubscribersOnly,
                NewPollMinWatchtime);

            await LoadPollsAsync();

            // Clear form
            NewPollQuestion = string.Empty;
            NewPollOptions = "Option 1\nOption 2\nOption 3";
            NewPollAllowMultipleVotes = false;
            NewPollFollowersOnly = false;
            NewPollSubscribersOnly = false;
            NewPollMinWatchtime = 0;
            NewPollAllowedPlatforms = "All";
            NewPollScheduledEnd = null;

            StatusMessage = $"✅ Poll '{poll.Question}' created successfully!";
            _logger.LogInformation("Created poll: {Question}", poll.Question);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating poll");
            StatusMessage = $"❌ Error creating poll: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StartPollAsync()
    {
        if (SelectedPoll == null)
        {
            StatusMessage = "❌ Please select a poll to start";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Starting poll...";

            var success = await _pollService.StartPollAsync(SelectedPoll.Id);

            if (success)
            {
                await LoadPollsAsync();
                StatusMessage = $"✅ Poll '{SelectedPoll.Question}' started!";
                _logger.LogInformation("Started poll: {Question}", SelectedPoll.Question);
            }
            else
            {
                StatusMessage = "❌ Failed to start poll (may already be active or another poll is running)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting poll");
            StatusMessage = $"❌ Error starting poll: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task EndPollAsync()
    {
        if (SelectedPoll == null)
        {
            StatusMessage = "❌ Please select a poll to end";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Ending poll...";

            var (success, results, message) = await _pollService.EndPollAsync(SelectedPoll.Id);

            if (success)
            {
                await LoadPollsAsync();
                await LoadVotesForSelectedPollAsync();
                StatusMessage = $"✅ {message}";
                _logger.LogInformation("Ended poll: {Question}", SelectedPoll.Question);
            }
            else
            {
                StatusMessage = $"❌ {message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending poll");
            StatusMessage = $"❌ Error ending poll: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CancelPollAsync()
    {
        if (SelectedPoll == null)
        {
            StatusMessage = "❌ Please select a poll to cancel";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Cancelling poll...";

            var success = await _pollService.CancelPollAsync(SelectedPoll.Id);

            if (success)
            {
                await LoadPollsAsync();
                StatusMessage = $"✅ Poll '{SelectedPoll.Question}' cancelled";
                _logger.LogInformation("Cancelled poll: {Question}", SelectedPoll.Question);
            }
            else
            {
                StatusMessage = "❌ Failed to cancel poll";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling poll");
            StatusMessage = $"❌ Error cancelling poll: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeletePollAsync()
    {
        if (SelectedPoll == null)
        {
            StatusMessage = "❌ Please select a poll to delete";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Deleting poll...";

            await _dataService.DeletePollAsync(SelectedPoll.Id);
            await LoadPollsAsync();

            StatusMessage = "✅ Poll deleted successfully";
            _logger.LogInformation("Deleted poll: {Question}", SelectedPoll.Question);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting poll");
            StatusMessage = $"❌ Error deleting poll: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadPollsAsync();
        if (SelectedPoll != null)
        {
            await LoadVotesForSelectedPollAsync();
        }
        StatusMessage = "✅ Refreshed";
    }
}
