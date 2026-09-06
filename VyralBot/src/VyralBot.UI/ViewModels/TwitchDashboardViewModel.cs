using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.UI.Models;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

public partial class TwitchDashboardViewModel : ViewModelBase
{
    private readonly ILogger<TwitchDashboardViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly IStreamActionsService _streamActions;

    private readonly List<Guid> _subscriptionIds = new();
    private bool _isSubscribed;

    /// <summary>Max entries kept in the Activity Feed (oldest dropped).</summary>
    private const int MaxActivityItems = 50;

    /// <summary>Live activity feed shown in the dashboard's Activity Feed panel.</summary>
    public ObservableCollection<ActivityItem> RecentActivity { get; } = new();

    public WebView2? StreamPreviewWebView { get; set; }

    public event EventHandler? WebView2InitializeRequested;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _channelName = "";

    [ObservableProperty]
    private string _status = "Loading...";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _showStreamPreview = false;

    [ObservableProperty]
    private bool _showPreviewPanel = true;

    [ObservableProperty]
    private bool _isLive = false;

    [ObservableProperty]
    private string _streamUrl = "";

    [ObservableProperty]
    private string _statusMessage = "";

    // ── Stream Management quick actions ──────────────────────────────
    [ObservableProperty]
    private bool _isEmoteOnly = false;

    [ObservableProperty]
    private bool _isFollowerOnly = false;

    [ObservableProperty]
    private bool _isShieldMode = false;

    [ObservableProperty]
    private bool _isEditStreamInfoOpen = false;

    [ObservableProperty]
    private string _editStreamTitle = "";

    [ObservableProperty]
    private string _editStreamCategory = "";

    [ObservableProperty]
    private bool _isRaidOpen = false;

    [ObservableProperty]
    private string _raidTarget = "";

    public TwitchDashboardViewModel(
        ILogger<TwitchDashboardViewModel> logger,
        IDataService dataService,
        IEventBus eventBus,
        IStreamActionsService streamActions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _streamActions = streamActions ?? throw new ArgumentNullException(nameof(streamActions));
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Twitch settings");

        await SubscribeToActivityEventsAsync();

        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");

            if (platform != null && platform.Status == "Connected")
            {
                Username = platform.Username ?? "";
                ChannelName = platform.Username ?? "";
                IsConnected = true;
                Status = $"Connected as {Username}";
                StreamUrl = $"https://player.twitch.tv/?channel={ChannelName}&parent=localhost";
            }
            else
            {
                Status = "Not connected";
                IsConnected = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Twitch settings");
            Status = $"Error: {ex.Message}";
        }

        // Seed the moderation toggles with the channel's current state (best effort)
        if (IsConnected)
        {
            await RefreshChatSettingsAsync();
        }
    }

    [RelayCommand]
    private async Task PreviewStreamAsync()
    {
        _logger.LogInformation("Opening stream preview for channel: {Channel}", ChannelName);
        
        if (string.IsNullOrEmpty(ChannelName))
        {
            _logger.LogWarning("No channel name provided");
            return;
        }
        
        // Request WebView2 initialization ONLY when user clicks play (on-demand)
        _logger.LogInformation("🎬 Initializing WebView2 on-demand for stream preview...");
        WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
        
        // Wait a moment for initialization
        await Task.Delay(500);
        
        if (StreamPreviewWebView?.CoreWebView2 != null)
        {
            ShowStreamPreview = true;
            var embedUrl = $"https://player.twitch.tv/?channel={ChannelName}&parent=localhost&muted=false";
            _logger.LogInformation("▶️ Loading Twitch player: {Url}", embedUrl);
            StreamPreviewWebView.CoreWebView2.Navigate(embedUrl);
        }
        else
        {
            _logger.LogWarning("WebView2 not initialized");
        }
    }

    [RelayCommand]
    private void CloseStreamPreview()
    {
        _logger.LogInformation("Closing stream preview");
        ShowStreamPreview = false;
        
        // Dispose WebView2 when closing preview to free resources immediately
        if (StreamPreviewWebView != null)
        {
            try
            {
                _logger.LogInformation("🗑️ Disposing WebView2 to free memory...");
                StreamPreviewWebView.Dispose();
                StreamPreviewWebView = null;
                GC.Collect(); // Force cleanup
                _logger.LogInformation("✅ Stream preview WebView2 disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose stream preview WebView2");
            }
        }
    }

    [RelayCommand]
    private void TogglePreviewPanel()
    {
        ShowPreviewPanel = !ShowPreviewPanel;
        if (!ShowPreviewPanel)
        {
            ShowStreamPreview = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _logger.LogInformation("Saving Twitch settings");
        
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");

            if (platform != null)
            {
                platform.Username = Username;
                platform.UpdatedAt = DateTime.UtcNow;
                
                await _dataService.SavePlatformAsync(platform);
                
                Status = "Settings saved successfully";
                _logger.LogInformation("Twitch settings saved");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Twitch settings");
            Status = $"Error saving: {ex.Message}";
        }
    }

    // ======================================================================
    // Stream Management quick actions
    // ======================================================================

    [RelayCommand]
    private void ToggleEditStreamInfo() => IsEditStreamInfoOpen = !IsEditStreamInfoOpen;

    [RelayCommand]
    private void ToggleRaid() => IsRaidOpen = !IsRaidOpen;

    [RelayCommand]
    private async Task ApplyStreamInfoAsync()
    {
        var title = string.IsNullOrWhiteSpace(EditStreamTitle) ? null : EditStreamTitle.Trim();
        var category = string.IsNullOrWhiteSpace(EditStreamCategory) ? null : EditStreamCategory.Trim();

        if (title is null && category is null)
        {
            const string text = "⚠️ Enter a new title or category first";
            AddActivity(text);
            StatusMessage = text;
            return;
        }

        await RunStreamActionAsync("✏️", () => _streamActions.TwitchEditStreamInfoAsync(title, category));
    }

    [RelayCommand]
    private async Task AddMarkerAsync()
        => await RunStreamActionAsync("📍", () => _streamActions.TwitchCreateMarkerAsync(null));

    [RelayCommand]
    private async Task CreateClipAsync()
        => await RunStreamActionAsync("🎬", () => _streamActions.TwitchCreateClipAsync());

    [RelayCommand]
    private async Task RaidAsync()
    {
        if (string.IsNullOrWhiteSpace(RaidTarget))
        {
            const string text = "⚠️ Enter a channel name to raid";
            AddActivity(text);
            StatusMessage = text;
            return;
        }

        await RunStreamActionAsync("🚀", () => _streamActions.TwitchRaidAsync(RaidTarget.Trim()));
    }

    [RelayCommand]
    private async Task StopRaidAsync()
        => await RunStreamActionAsync("🛑", () => _streamActions.TwitchStopRaidAsync());

    [RelayCommand]
    private async Task ClearChatAsync()
        => await RunStreamActionAsync("🧹", () => _streamActions.TwitchClearChatAsync());

    [RelayCommand]
    private async Task ToggleEmoteOnlyAsync(bool enabled)
    {
        // Note: the ToggleButton's IsChecked binding already flipped IsEmoteOnly before
        // this command runs — 'enabled' IS the new value, so do not re-invert it here.
        var result = await RunStreamActionAsync("😊", () => _streamActions.TwitchSetEmoteOnlyAsync(enabled));
        if (result is null || !result.Success)
        {
            IsEmoteOnly = !enabled; // Revert so the switch shows the real state
            return;
        }
        await RefreshChatSettingsAsync();
    }

    [RelayCommand]
    private async Task ToggleFollowerOnlyAsync(bool enabled)
    {
        // Note: the ToggleButton's IsChecked binding already flipped IsFollowerOnly
        // before this command runs — 'enabled' IS the new value, so do not re-invert it.
        var result = await RunStreamActionAsync("👥", () => _streamActions.TwitchSetFollowerOnlyAsync(enabled));
        if (result is null || !result.Success)
        {
            IsFollowerOnly = !enabled; // Revert so the switch shows the real state
            return;
        }
        await RefreshChatSettingsAsync();
    }

    [RelayCommand]
    private async Task ToggleShieldModeAsync(bool enabled)
    {
        // Note: the ToggleButton's IsChecked binding already flipped IsShieldMode
        // before this command runs — 'enabled' IS the new value, so do not re-invert it.
        var result = await RunStreamActionAsync("🛡️", () => _streamActions.TwitchSetShieldModeAsync(enabled));
        if (result is null || !result.Success)
        {
            IsShieldMode = !enabled; // Revert so the switch shows the real state
            return;
        }
        await RefreshChatSettingsAsync();
    }

    /// <summary>Reloads the channel's moderation modes into the toggles (best effort, never throws).</summary>
    private async Task RefreshChatSettingsAsync()
    {
        try
        {
            var state = await _streamActions.TwitchGetChatSettingsAsync();
            if (state is not null)
            {
                IsEmoteOnly = state.EmoteOnly;
                IsFollowerOnly = state.FollowerOnly;
                IsShieldMode = state.ShieldMode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh Twitch chat moderation settings");
        }
    }

    /// <summary>
    /// Executes a stream action and surfaces the outcome in the Activity Feed and StatusMessage.
    /// Returns null when the action was not executed (not connected) or threw.
    /// </summary>
    private async Task<StreamActionResult?> RunStreamActionAsync(string emoji, Func<Task<StreamActionResult>> action)
    {
        if (!IsConnected)
        {
            const string text = "⚠️ Connect Twitch to use stream actions";
            AddActivity(text);
            StatusMessage = text;
            return null;
        }

        try
        {
            var result = await action();
            var text = result.Success ? $"{emoji} {result.Message}" : $"⚠️ {result.Message}";
            AddActivity(text);
            StatusMessage = text;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitch stream action failed");
            var text = $"⚠️ Twitch action failed: {ex.Message}";
            AddActivity(text);
            StatusMessage = text;
            return null;
        }
    }

    // ======================================================================
    // Activity Feed (live follows / subs / resubs / gift subs / raids / cheers)
    // ======================================================================

    private async Task SubscribeToActivityEventsAsync()
    {
        if (_isSubscribed)
        {
            return; // Prevent duplicate subscriptions when re-initializing
        }
        _isSubscribed = true;

        try
        {
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchFollowEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchSubscribeEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchResubEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchGiftSubEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchRaidEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchCheerEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<TwitchStreamStatusEvent>(OnStreamStatusAsync));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to Twitch dashboard activity events");
            _isSubscribed = false;
        }
    }

    /// <summary>Unsubscribes from activity events. Called by the view's Unloaded handler.</summary>
    public void Cleanup()
    {
        foreach (var id in _subscriptionIds)
        {
            try
            {
                _eventBus.Unsubscribe(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe activity subscription {SubscriptionId}", id);
            }
        }
        _subscriptionIds.Clear();
        _isSubscribed = false;
    }

    private Task OnActivityEventAsync<TEvent>(TEvent evt) where TEvent : IEvent
    {
        var text = evt switch
        {
            TwitchFollowEvent e => $"🌱 {e.DisplayName} just followed!",
            TwitchSubscribeEvent e => e.IsGift
                ? $"🎁 {e.DisplayName} received a gift sub (Tier {e.Tier})"
                : $"⭐ {e.DisplayName} subscribed (Tier {e.Tier}, {e.MonthsTotal} months)",
            TwitchResubEvent e => $"⭐ {e.Username} resubscribed for {e.CumulativeMonths} months (Tier {e.Tier})",
            TwitchGiftSubEvent e => e.IsAnonymous
                ? $"🎁 Anonymous gifted {e.Total} subs (Tier {e.Tier})!"
                : $"🎁 {e.GifterUsername} gifted {e.Total} subs (Tier {e.Tier})!",
            TwitchRaidEvent e => $"🚀 {e.FromBroadcasterUsername} raided with {e.Viewers} viewers!",
            TwitchCheerEvent e => e.IsAnonymous
                ? $"💎 Anonymous cheered {e.Bits} bits!"
                : $"💎 {e.Username} cheered {e.Bits} bits!",
            _ => $"🔔 {typeof(TEvent).Name.Replace("Twitch", "").Replace("Event", "")} received"
        };

        AddActivity(text);
        return Task.CompletedTask;
    }

    private Task OnStreamStatusAsync(TwitchStreamStatusEvent e)
    {
        IsLive = e.IsLive;
        AddActivity(e.IsLive
            ? "🔴 Stream is now LIVE!"
            : "⚫ Stream went offline");
        return Task.CompletedTask;
    }

    private void AddActivity(string text)
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    RecentActivity.Insert(0, new ActivityItem(DateTime.Now, text));
                    while (RecentActivity.Count > MaxActivityItems)
                    {
                        RecentActivity.RemoveAt(RecentActivity.Count - 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add Twitch dashboard activity entry");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to marshal Twitch dashboard activity to the UI thread");
        }
    }
}
