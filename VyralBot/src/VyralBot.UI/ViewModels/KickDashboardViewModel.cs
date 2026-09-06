using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Kick.Events;
using VyralBot.UI.Models;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Dashboard ViewModel for Kick platform operational controls.
/// Accessible via Main Navigation → Platforms → Kick button.
/// </summary>
public partial class KickDashboardViewModel : ViewModelBase
{
    private readonly ILogger<KickDashboardViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IKickService _kickService;
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
    private bool _isSlowMode = false;

    public KickDashboardViewModel(
        ILogger<KickDashboardViewModel> logger,
        IDataService dataService,
        IKickService kickService,
        IEventBus eventBus,
        IStreamActionsService streamActions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _streamActions = streamActions ?? throw new ArgumentNullException(nameof(streamActions));
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Kick dashboard");

        await SubscribeToActivityEventsAsync();

        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");

            if (platform != null && platform.Status == "Connected")
            {
                Username = platform.Username ?? "";
                ChannelName = platform.Username ?? "";
                IsConnected = _kickService.IsConnected;
                Status = IsConnected 
                    ? $"Connected as {Username}" 
                    : "Disconnected (reconnect on app restart)";
                StreamUrl = $"https://player.kick.com/{ChannelName}?parent=localhost";
            }
            else
            {
                Status = "Not connected";
                IsConnected = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Kick dashboard");
            Status = $"Error: {ex.Message}";
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
            var embedUrl = $"https://player.kick.com/{ChannelName}?parent=localhost&muted=false";
            _logger.LogInformation("▶️ Loading Kick player: {Url}", embedUrl);
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
        _logger.LogInformation("Saving Kick dashboard settings");
        
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");

            if (platform != null)
            {
                platform.Username = Username;
                platform.UpdatedAt = DateTime.UtcNow;
                
                await _dataService.SavePlatformAsync(platform);
                
                Status = "Settings saved successfully";
                _logger.LogInformation("Kick settings saved");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Kick settings");
            Status = $"Error saving: {ex.Message}";
        }
    }

    // ======================================================================
    // Stream Management quick actions
    // ======================================================================

    [RelayCommand]
    private async Task ClearChatAsync()
        => await RunStreamActionAsync("🧹", () => _streamActions.KickClearChatAsync());

    [RelayCommand]
    private async Task ToggleEmoteOnlyAsync(bool enabled)
    {
        // Note: the ToggleButton's IsChecked binding already flipped IsEmoteOnly
        // before this command runs — 'enabled' IS the new value, so do not re-invert it.
        var result = await RunStreamActionAsync("😊", () => _streamActions.KickSetEmoteOnlyAsync(enabled));
        if (result is null || !result.Success)
        {
            IsEmoteOnly = !enabled; // Revert so the switch shows the real state
        }
    }

    [RelayCommand]
    private async Task ToggleFollowerOnlyAsync(bool enabled)
    {
        // Note: the ToggleButton's IsChecked binding already flipped IsFollowerOnly
        // before this command runs — 'enabled' IS the new value, so do not re-invert it.
        var result = await RunStreamActionAsync("👥", () => _streamActions.KickSetFollowerOnlyAsync(enabled));
        if (result is null || !result.Success)
        {
            IsFollowerOnly = !enabled; // Revert so the switch shows the real state
        }
    }

    [RelayCommand]
    private async Task ToggleSlowModeAsync(bool enabled)
    {
        // Note: the ToggleButton's IsChecked binding already flipped IsSlowMode
        // before this command runs — 'enabled' IS the new value, so do not re-invert it.
        var result = await RunStreamActionAsync("🐢", () => _streamActions.KickSetSlowModeAsync(enabled));
        if (result is null || !result.Success)
        {
            IsSlowMode = !enabled; // Revert so the switch shows the real state
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
            const string text = "⚠️ Connect Kick to use stream actions";
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
            _logger.LogError(ex, "Kick stream action failed");
            var text = $"⚠️ Kick action failed: {ex.Message}";
            AddActivity(text);
            StatusMessage = text;
            return null;
        }
    }

    // ======================================================================
    // Activity Feed (follows / subs / gift subs / gifted Kicks / rewards / hosts)
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
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickFollowEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickSubscribeEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickGiftSubEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickKicksGiftedEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickRewardRedeemedEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickHostEvent>(OnActivityEventAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<KickStreamStatusEvent>(OnStreamStatusAsync));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to Kick dashboard activity events");
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
            KickFollowEvent e => $"🌱 New follower! ({e.FollowerCount} total)",
            KickSubscribeEvent e => $"⭐ {e.Username} subscribed (Month {e.MonthsSubscribed})",
            KickGiftSubEvent e => $"🎁 {e.GifterUsername} gifted {e.Count} subscription(s)!",
            KickKicksGiftedEvent e => $"💰 {e.SenderUsername} gifted {e.Amount} Kicks!",
            KickRewardRedeemedEvent e => $"🏆 {e.Username} redeemed \"{e.RewardName}\"",
            KickHostEvent e => $"🎯 {e.HostUsername} is hosting with {e.ViewerCount} viewer(s)!",
            _ => $"🔔 {typeof(TEvent).Name.Replace("Kick", "").Replace("Event", "")} received"
        };

        AddActivity(text);
        return Task.CompletedTask;
    }

    private Task OnStreamStatusAsync(KickStreamStatusEvent e)
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
                    _logger.LogWarning(ex, "Failed to add Kick dashboard activity entry");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to marshal Kick dashboard activity to the UI thread");
        }
    }
}
