using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using VyralBot.Core.Services;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.YouTube;
using VyralBot.UI.Services;
using VyralBot.UI.Views;
using VyralBot.Workflows.Runtime;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for managing giveaways with weighted entry system
/// </summary>
public partial class GiveawayViewModel : ViewModelBase
{
    private readonly GiveawayService _giveawayService;
    private readonly IDataService _dataService;
    private readonly ILogger<GiveawayViewModel> _logger;

    // Chat announcements (winner draws) — same per-platform routing the
    // workflow sends use. IServiceProvider is required to build the
    // ExecutionContext instances the sender's SendAsync contract expects.
    private readonly WorkflowMessageSender _messageSender;
    private readonly IServiceProvider _serviceProvider;

    // Live update plumbing — giveaway events debounce into a single reload
    // through one DispatcherTimer instance instead of reloading per event.
    private readonly IEventBus _eventBus;
    private readonly DispatcherTimer _reloadTimer;
    private volatile bool _reloadPending;
    private bool _isInitialized;

    // 1s ticker driving the active-giveaway countdown display (banner).
    private readonly DispatcherTimer _countdownTimer;

    private Guid _startedSubscriptionId = Guid.Empty;
    private Guid _entrySubscriptionId = Guid.Empty;
    private Guid _winnersSubscriptionId = Guid.Empty;
    private Guid _cancelledSubscriptionId = Guid.Empty;
    private Guid _intakeModeSubscriptionId = Guid.Empty;

    [ObservableProperty]
    private ObservableCollection<Giveaway> _giveaways = new();

    [ObservableProperty]
    private Giveaway? _selectedGiveaway;

    [ObservableProperty]
    private ObservableCollection<GiveawayEntry> _entries = new();

    // The currently ACTIVE giveaway (whatever is selected) — drives the
    // always-visible banner so the streamer sees the live giveaway even while
    // inspecting a completed one.
    [ObservableProperty]
    private Giveaway? _activeGiveaway;

    // Ticking countdown for the active giveaway's scheduled end
    // ("ends in 00:12:34"), empty when none applies.
    [ObservableProperty]
    private string _activeGiveawayCountdown = string.Empty;

    // Empty-state flags for the list/entries panels.
    [ObservableProperty]
    private bool _hasGiveaways;

    [ObservableProperty]
    private bool _hasEntries;

    // Create giveaway form fields
    [ObservableProperty]
    private string _newGiveawayName = string.Empty;

    [ObservableProperty]
    private string _newGiveawayDescription = string.Empty;

    [ObservableProperty]
    private string _newGiveawayKeyword = "enter";

    [ObservableProperty]
    private int _newGiveawayWinnerCount = 1;

    // 0 means no cap — GiveawayService treats a null MaxEntries as unlimited,
    // so anything <= 0 is passed as null when the giveaway is created.
    [ObservableProperty]
    private int _newGiveawayMaxEntries = 0;

    [ObservableProperty]
    private bool _newGiveawaySubscriberBonusEnabled = false;

    [ObservableProperty]
    private int _newGiveawaySubscriberBonusEntries = 5;

    [ObservableProperty]
    private int _newGiveawayMinWatchtime = 0;

    [ObservableProperty]
    private bool _newGiveawayFollowersOnly = false;

    [ObservableProperty]
    private bool _newGiveawaySubscribersOnly = false;

    // Trailing arg of GiveawayService.CreateGiveawayAsync — enables the
    // follower/sub-tier weighted-entry boosts for the new giveaway.
    [ObservableProperty]
    private bool _newGiveawayOddsBoostEnabled = false;

    [ObservableProperty]
    private string _newGiveawayAllowedPlatforms = "Twitch,Kick";

    [ObservableProperty]
    private DateTime? _newGiveawayScheduledEnd;

    // Statistics
    [ObservableProperty]
    private int _totalEntries;

    [ObservableProperty]
    private int _uniqueParticipants;

    // Winners actually drawn for the selected giveaway (entries flagged
    // IsWinner) — NOT the configured WinnerCount, so the stat card shows 0
    // until a draw has really happened.
    [ObservableProperty]
    private int _drawnWinnersCount;

    // Live intake indicator: "LIVE" accepts entries as they arrive,
    // "TIMED" throttles intake to EntriesPerMinuteDisplay.
    [ObservableProperty]
    private string _intakeModeDisplay = "LIVE";

    [ObservableProperty]
    private int _entriesPerMinuteDisplay;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public GiveawayViewModel(
        GiveawayService giveawayService,
        IDataService dataService,
        ILogger<GiveawayViewModel> logger,
        WorkflowMessageSender messageSender,
        IServiceProvider serviceProvider,
        IEventBus eventBus)
    {
        _giveawayService = giveawayService;
        _dataService = dataService;
        _logger = logger;
        _messageSender = messageSender;
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;

        // Created once in the ctor (UI thread) — event handlers only Start/Stop
        // it, so bursts of giveaway events collapse into a single 1s-delayed reload.
        _reloadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _reloadTimer.Tick += OnReloadTimerTick;

        // Countdown ticker: runs only while the page is alive (started in
        // InitializeAsync, stopped in Cleanup) and updates the banner clock.
        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            // Already wired — just refresh the live intake indicator.
            await UpdateIntakeModeAsync();
            return;
        }

        _isInitialized = true;

        try
        {
            IsLoading = true;
            await SubscribeToGiveawayEventsAsync();
            await UpdateIntakeModeAsync();
            await LoadGiveawaysAsync();
            if (!_countdownTimer.IsEnabled)
            {
                _countdownTimer.Start();
            }
            _logger.LogInformation("GiveawayViewModel initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing GiveawayViewModel");
            StatusMessage = $"Error loading giveaways: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SubscribeToGiveawayEventsAsync()
    {
        try
        {
            // Idempotent across repeated Initialize/Cleanup cycles: drop any
            // subscription that survived a partial (re)subscribe attempt so a
            // retry never double-subscribes.
            Unsubscribe(ref _startedSubscriptionId);
            Unsubscribe(ref _entrySubscriptionId);
            Unsubscribe(ref _winnersSubscriptionId);
            Unsubscribe(ref _cancelledSubscriptionId);
            Unsubscribe(ref _intakeModeSubscriptionId);

            _startedSubscriptionId = await _eventBus.SubscribeAsync<GiveawayStartedEvent>(OnGiveawayStartedAsync);
            _entrySubscriptionId = await _eventBus.SubscribeAsync<GiveawayEntryEvent>(OnGiveawayEntryAsync);
            _winnersSubscriptionId = await _eventBus.SubscribeAsync<GiveawayWinnersDrawnEvent>(OnGiveawayWinnersDrawnAsync);
            _cancelledSubscriptionId = await _eventBus.SubscribeAsync<GiveawayCancelledEvent>(OnGiveawayCancelledAsync);
            _intakeModeSubscriptionId = await _eventBus.SubscribeAsync<GiveawayIntakeModeChangedEvent>(OnGiveawayIntakeModeChangedAsync);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to giveaway events — the view will not auto-refresh");
        }
    }

    /// <summary>
    /// Unsubscribes all EventBus subscriptions and stops the reload timer.
    /// Safe to call at any point (including before InitializeAsync): the
    /// subscription ids stay Guid.Empty until a subscribe actually succeeded.
    /// </summary>
    public void Cleanup()
    {
        Unsubscribe(ref _startedSubscriptionId);
        Unsubscribe(ref _entrySubscriptionId);
        Unsubscribe(ref _winnersSubscriptionId);
        Unsubscribe(ref _cancelledSubscriptionId);
        Unsubscribe(ref _intakeModeSubscriptionId);

        _reloadPending = false;
        _reloadTimer.Stop();
        _countdownTimer.Stop();
        _isInitialized = false;
        _logger.LogInformation("GiveawayViewModel cleaned up");
    }

    private void Unsubscribe(ref Guid subscriptionId)
    {
        if (subscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(subscriptionId);
            subscriptionId = Guid.Empty;
        }
    }

    // All giveaway events funnel into the same debounced reload; the winners
    // event additionally surfaces a status message.
    private Task OnGiveawayStartedAsync(GiveawayStartedEvent evt) => RequestDebouncedReloadAsync();

    private Task OnGiveawayEntryAsync(GiveawayEntryEvent evt) => RequestDebouncedReloadAsync();

    private Task OnGiveawayWinnersDrawnAsync(GiveawayWinnersDrawnEvent evt)
    {
        var message = $"🎉 Winners drawn for '{evt.Title}'";
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => StatusMessage = message);
        return RequestDebouncedReloadAsync();
    }

    private Task OnGiveawayCancelledAsync(GiveawayCancelledEvent evt) => RequestDebouncedReloadAsync();

    private Task OnGiveawayIntakeModeChangedAsync(GiveawayIntakeModeChangedEvent evt) => RequestDebouncedReloadAsync();

    private Task RequestDebouncedReloadAsync()
    {
        _reloadPending = true;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return Task.CompletedTask;
        }

        dispatcher.InvokeAsync(() =>
        {
            if (!_reloadTimer.IsEnabled)
            {
                _reloadTimer.Start();
            }
        });

        return Task.CompletedTask;
    }

    private async void OnReloadTimerTick(object? sender, EventArgs e)
    {
        _reloadTimer.Stop();

        if (!_reloadPending)
        {
            return;
        }

        _reloadPending = false;

        try
        {
            var selectedId = SelectedGiveaway?.Id;

            await LoadGiveawaysAsync();

            if (selectedId != null)
            {
                // LoadGiveawaysAsync swaps in new entity instances which clears
                // the TwoWay selection — re-select by Id so the details panel
                // (and the entries reload below) keeps tracking the same giveaway.
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SelectedGiveaway = Giveaways.FirstOrDefault(g => g.Id == selectedId.Value);
                });
            }

            await LoadEntriesForSelectedGiveawayAsync();
            await UpdateIntakeModeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading giveaways after event");
        }
    }

    private async Task UpdateIntakeModeAsync()
    {
        try
        {
            var mode = _giveawayService.CurrentIntakeMode;
            var entriesPerMinute = await _giveawayService.GetCurrentEntriesPerMinuteAsync();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IntakeModeDisplay = mode == GiveawayIntakeMode.Timed ? "TIMED" : "LIVE";
                EntriesPerMinuteDisplay = entriesPerMinute;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the giveaway intake mode");
        }
    }

    private async Task LoadGiveawaysAsync()
    {
        try
        {
            var giveaways = await _dataService.GetGiveawaysAsync();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Giveaways.Clear();
                foreach (var giveaway in giveaways.OrderByDescending(g => g.CreatedAt))
                {
                    Giveaways.Add(giveaway);
                }

                HasGiveaways = Giveaways.Count > 0;

                // Banner source: the live giveaway, independent of selection.
                ActiveGiveaway = Giveaways.FirstOrDefault(g => g.Status == GiveawayStatus.Active);
                UpdateCountdown();
            });

            _logger.LogInformation("Loaded {Count} giveaways", giveaways.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading giveaways");
            throw;
        }
    }

    /// <summary>
    /// Refreshes the banner countdown for the active giveaway's scheduled
    /// end (UTC, matching the scheduler's comparison). Empty when the active
    /// giveaway has no scheduled end or none is active.
    /// </summary>
    private void UpdateCountdown()
    {
        var active = ActiveGiveaway;
        var endUtc = active?.ScheduledEndTime;
        if (active == null || endUtc == null)
        {
            ActiveGiveawayCountdown = string.Empty;
            return;
        }

        var remaining = endUtc.Value - DateTime.UtcNow;
        ActiveGiveawayCountdown = remaining > TimeSpan.Zero
            ? $"ends in {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : "ending now…";
    }

    partial void OnSelectedGiveawayChanged(Giveaway? value)
    {
        if (value != null)
        {
            _ = LoadEntriesForSelectedGiveawayAsync();
        }
    }

    private async Task LoadEntriesForSelectedGiveawayAsync()
    {
        if (SelectedGiveaway == null) return;

        try
        {
            var entries = await _dataService.GetGiveawayEntriesAsync(SelectedGiveaway.Id);
            var totalEntries = await _dataService.GetGiveawayEntryCountAsync(SelectedGiveaway.Id);
            var uniqueParticipants = await _dataService.GetGiveawayUniqueParticipantsAsync(SelectedGiveaway.Id);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Entries.Clear();
                foreach (var entry in entries.OrderByDescending(e => e.EnteredAt))
                {
                    Entries.Add(entry);
                }

                HasEntries = Entries.Count > 0;
                TotalEntries = totalEntries;
                UniqueParticipants = uniqueParticipants;
                DrawnWinnersCount = entries.Count(e => e.IsWinner);
            });

            _logger.LogInformation("Loaded {Count} entries for giveaway {GiveawayId}", entries.Count(), SelectedGiveaway.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading entries for giveaway {GiveawayId}", SelectedGiveaway?.Id.ToString() ?? "unknown");
            StatusMessage = $"Error loading entries: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateGiveawayAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewGiveawayName))
            {
                StatusMessage = "❌ Giveaway name is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewGiveawayKeyword))
            {
                StatusMessage = "❌ Entry keyword is required";
                return;
            }

            if (NewGiveawayWinnerCount < 1)
            {
                StatusMessage = "❌ Winner count must be at least 1";
                return;
            }

            IsLoading = true;
            StatusMessage = "Creating giveaway...";

            // 0 (or negative) = unlimited entries; GiveawayService only caps when MaxEntries has a value.
            int? maxEntries = NewGiveawayMaxEntries > 0 ? NewGiveawayMaxEntries : null;

            // DatePicker yields a local wall-clock time; normalize to UTC so
            // the scheduler (which compares against DateTime.UtcNow) fires at
            // the time the streamer actually picked.
            var scheduledEndUtc = NewGiveawayScheduledEnd?.ToUniversalTime();

            var giveaway = await _giveawayService.CreateGiveawayAsync(
                NewGiveawayName,
                NewGiveawayDescription,
                NewGiveawayKeyword,
                NewGiveawayWinnerCount,
                maxEntries,
                scheduledEndUtc,
                NewGiveawaySubscriberBonusEnabled,
                NewGiveawaySubscriberBonusEntries,
                NewGiveawayMinWatchtime,
                NewGiveawayAllowedPlatforms,
                NewGiveawayFollowersOnly,
                NewGiveawaySubscribersOnly,
                NewGiveawayOddsBoostEnabled
            );

            StatusMessage = $"✅ Created giveaway: {giveaway.Name}";
            await LoadGiveawaysAsync();
            ClearForm();
            _logger.LogInformation("Created giveaway: {Name}", NewGiveawayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating giveaway");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StartGiveawayAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        var selected = SelectedGiveaway;

        try
        {
            IsLoading = true;
            StatusMessage = "Starting giveaway...";

            var success = await _giveawayService.StartGiveawayAsync(selected.Id);

            if (success)
            {
                StatusMessage = $"✅ Giveaway started";
                await LoadGiveawaysAsync();

                // The reload swaps in new entity instances and clears the
                // TwoWay selection — re-select before reading the property.
                SelectedGiveaway = Giveaways.FirstOrDefault(g => g.Id == selected.Id);

                _logger.LogInformation("Started giveaway: {Name}", selected.Name);
            }
            else
            {
                StatusMessage = $"❌ Failed to start giveaway";
                _logger.LogWarning("Failed to start giveaway");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting giveaway");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DrawWinnersAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        var selected = SelectedGiveaway;

        try
        {
            IsLoading = true;
            StatusMessage = "Drawing winners...";

            var (success, winners, message) = await _giveawayService.DrawWinnersAsync(selected.Id);

            if (success && winners != null)
            {
                var winnerNames = string.Join(", ", winners);
                StatusMessage = $"🎉 Winners: {winnerNames}";

                await LoadGiveawaysAsync();

                // The reload swaps in new entity instances and clears the
                // TwoWay selection — re-select FIRST so the entries refresh
                // below (and the log line) use the live selection.
                SelectedGiveaway = Giveaways.FirstOrDefault(g => g.Id == selected.Id);
                await LoadEntriesForSelectedGiveawayAsync();

                // Announce the winners (with the prize) on the connected
                // platforms — the chat-command draw path announces too, so a
                // UI-drawn draw should be equally visible to viewers.
                await AnnounceToChatAsync($"🎉 Giveaway '{selected.Name}' winner(s): {winnerNames}");

                _logger.LogInformation("Drew {Count} winners for giveaway {Name}: {Winners}",
                    winners.Count, selected.Name, winnerNames);
            }
            else
            {
                StatusMessage = $"❌ {message}";
                _logger.LogWarning("Failed to draw winners: {Message}", message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error drawing winners");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Re-draws winners for the selected COMPLETED giveaway, excluding the
    /// previous winners (the "winner never responded — reroll" path). The
    /// service un-flags the old winners and picks fresh ones from the
    /// remaining entries.
    /// </summary>
    [RelayCommand]
    private async Task RedrawWinnersAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        var selected = SelectedGiveaway;

        if (selected.Status != GiveawayStatus.Completed)
        {
            StatusMessage = "❌ Only completed giveaways can be re-drawn";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Re-drawing winners...";

            var (success, winners, message) = await _giveawayService.RedrawWinnersAsync(selected.Id);

            if (success && winners != null)
            {
                var winnerNames = string.Join(", ", winners);
                StatusMessage = $"🎉 Re-drawn winners: {winnerNames}";

                await LoadGiveawaysAsync();
                SelectedGiveaway = Giveaways.FirstOrDefault(g => g.Id == selected.Id);
                await LoadEntriesForSelectedGiveawayAsync();

                await AnnounceToChatAsync($"🎲 Giveaway '{selected.Name}' re-drawn — new winner(s): {winnerNames}");

                _logger.LogInformation("Re-drew {Count} winners for giveaway {Name}: {Winners}",
                    winners.Count, selected.Name, winnerNames);
            }
            else
            {
                StatusMessage = $"❌ {message}";
                _logger.LogWarning("Failed to re-draw winners: {Message}", message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-drawing winners");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CancelGiveawayAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        var selected = SelectedGiveaway;

        try
        {
            IsLoading = true;
            StatusMessage = "Cancelling giveaway...";

            var success = await _giveawayService.CancelGiveawayAsync(selected.Id);

            if (success)
            {
                StatusMessage = $"✅ Giveaway cancelled";
                await LoadGiveawaysAsync();

                // The reload swaps in new entity instances and clears the
                // TwoWay selection — re-select before reading the property.
                SelectedGiveaway = Giveaways.FirstOrDefault(g => g.Id == selected.Id);

                _logger.LogInformation("Cancelled giveaway: {Name}", selected.Name);
            }
            else
            {
                StatusMessage = $"❌ Failed to cancel giveaway";
                _logger.LogWarning("Failed to cancel giveaway");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling giveaway");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedGiveawayAsync()
    {
        if (SelectedGiveaway == null)
        {
            StatusMessage = "❌ No giveaway selected";
            return;
        }

        var selected = SelectedGiveaway;

        // Deleting an Active giveaway would orphan its live entry intake —
        // it must be cancelled first.
        if (selected.Status == GiveawayStatus.Active)
        {
            StatusMessage = "Cancel the giveaway before deleting it";
            return;
        }

        try
        {
            var dialog = new ConfirmDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            dialog.SetMessage($"Delete giveaway '{selected.Name}' and all its entries?");
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = "Deleting giveaway...";

            var deleted = await _giveawayService.DeleteGiveawayAsync(selected.Id);

            if (deleted)
            {
                await LoadGiveawaysAsync();

                // The reload cleared the (now-deleted) selection — drop the
                // stale entries panel content too.
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SelectedGiveaway = null;
                    Entries.Clear();
                    HasEntries = false;
                    DrawnWinnersCount = 0;
                });

                StatusMessage = $"✅ Deleted giveaway: {selected.Name}";
                _logger.LogInformation("Deleted giveaway: {Name}", selected.Name);
            }
            else
            {
                StatusMessage = "❌ Failed to delete giveaway";
                _logger.LogWarning("Failed to delete giveaway: {Name}", selected.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting giveaway");
            StatusMessage = $"❌ Error deleting giveaway: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing...";

            await LoadGiveawaysAsync();
            
            if (SelectedGiveaway != null)
            {
                await LoadEntriesForSelectedGiveawayAsync();
            }

            StatusMessage = "✅ Refreshed";
            _logger.LogInformation("Refreshed giveaways list");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing giveaways");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearForm()
    {
        NewGiveawayName = string.Empty;
        NewGiveawayDescription = string.Empty;
        NewGiveawayKeyword = "enter";
        NewGiveawayWinnerCount = 1;
        NewGiveawayMaxEntries = 0; // 0 = unlimited entries
        NewGiveawaySubscriberBonusEnabled = false;
        NewGiveawaySubscriberBonusEntries = 5;
        NewGiveawayMinWatchtime = 0;
        NewGiveawayFollowersOnly = false;
        NewGiveawaySubscribersOnly = false;
        NewGiveawayOddsBoostEnabled = false;
        NewGiveawayAllowedPlatforms = "Twitch,Kick";
        NewGiveawayScheduledEnd = null;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Every platform the app has a chat-send service for. TikTok chat is
    /// read-only (no send API anywhere in the app) so it is intentionally
    /// absent; YouTube is sent directly via <see cref="IYouTubeService"/>
    /// because <see cref="WorkflowMessageSender"/> has no YouTube arm.
    /// </summary>
    private static readonly string[] AnnouncementPlatforms = { "Twitch", "Kick", "YouTube", "Discord" };

    /// <summary>
    /// Broadcasts an announcement to every platform the app can send chat
    /// to, skipping (info-logged, never thrown) any platform that is not
    /// connected or fails — one platform's failure never stops the loop, so
    /// the announcement still reaches every other connected platform.
    /// </summary>
    private async Task AnnounceToChatAsync(string message)
    {
        foreach (var platform in AnnouncementPlatforms)
        {
            try
            {
                bool delivered;

                if (string.Equals(platform, "YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    delivered = await SendToYouTubeAsync(message);
                }
                else
                {
                    var context = new VyralBot.Workflows.Runtime.ExecutionContext(_serviceProvider, _logger)
                    {
                        Platform = platform
                    };

                    // SendAsync skips platforms that are not connected and
                    // returns false instead of throwing.
                    delivered = await _messageSender.SendAsync(context, message);
                }

                if (!delivered)
                {
                    _logger.LogInformation("Giveaway announcement skipped on {Platform} (not connected)", platform);
                }
            }
            catch (Exception ex)
            {
                // Per-platform isolation: log and keep going so the remaining
                // platforms still hear the announcement.
                _logger.LogInformation(ex, "Giveaway announcement failed on {Platform}", platform);
            }
        }
    }

    /// <summary>
    /// YouTube send path: <see cref="WorkflowMessageSender"/> routes only
    /// Twitch/Kick/Discord, so announcements go straight through the
    /// singleton <see cref="IYouTubeService"/> — the same service
    /// ChatViewModel's send-to-all-platforms path uses. Returns false (no
    /// throw) when the service is missing or not connected.
    /// </summary>
    private async Task<bool> SendToYouTubeAsync(string message)
    {
        var youtube = _serviceProvider.GetService<IYouTubeService>();
        if (youtube == null || !youtube.IsConnected)
        {
            return false;
        }

        await youtube.SendMessageAsync(message);
        return true;
    }
}
