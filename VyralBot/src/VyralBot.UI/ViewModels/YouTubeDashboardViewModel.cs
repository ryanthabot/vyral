using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.YouTube.Events;
using VyralBot.UI.Models;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

public partial class YouTubeDashboardViewModel : ViewModelBase
{
    private readonly ILogger<YouTubeDashboardViewModel> _logger;
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
    private bool _isEditStreamInfoOpen = false;

    [ObservableProperty]
    private string _editStreamTitle = "";

    [ObservableProperty]
    private string _editStreamDescription = "";

    public YouTubeDashboardViewModel(
        ILogger<YouTubeDashboardViewModel> logger,
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
        _logger.LogInformation("Initializing YouTube settings");

        await SubscribeToActivityEventsAsync();

        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");

            if (platform != null && platform.Status == "Connected")
            {
                Username = platform.Username ?? "";
                ChannelName = platform.Username ?? "";
                IsConnected = true;
                Status = $"Connected as {Username}";
                StreamUrl = $"https://www.youtube.com/@{ChannelName}/live";
            }
            else
            {
                Status = "Not connected";
                IsConnected = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading YouTube settings");
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PreviewStreamAsync()
    {
        _logger.LogInformation("Opening YouTube live preview for channel: {Channel}", ChannelName);
        
        if (string.IsNullOrEmpty(ChannelName))
        {
            _logger.LogWarning("No YouTube channel name provided");
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
            // YouTube has no handle-based embed; the live watch page works in WebView2.
            var embedUrl = $"https://www.youtube.com/@{ChannelName}/live";
            _logger.LogInformation("▶️ Loading YouTube live page: {Url}", embedUrl);
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
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");

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
            _logger.LogError(ex, "Error saving YouTube settings");
            Status = $"Error saving: {ex.Message}";
        }
    }

    // ======================================================================
    // Stream Management quick actions
    // ======================================================================

    [RelayCommand]
    private void ToggleEditStreamInfo() => IsEditStreamInfoOpen = !IsEditStreamInfoOpen;

    [RelayCommand]
    private async Task ApplyStreamInfoAsync()
    {
        var title = string.IsNullOrWhiteSpace(EditStreamTitle) ? null : EditStreamTitle.Trim();
        var description = string.IsNullOrWhiteSpace(EditStreamDescription) ? null : EditStreamDescription.Trim();

        if (title is null && description is null)
        {
            const string text = "⚠️ Enter a new title or description first";
            AddActivity(text);
            StatusMessage = text;
            return;
        }

        await RunStreamActionAsync("✏️", () => _streamActions.YouTubeUpdateStreamInfoAsync(title, description));
    }

    /// <summary>
    /// Executes a stream action and surfaces the outcome in the Activity Feed and StatusMessage.
    /// Returns null when the action was not executed (not connected) or threw.
    /// </summary>
    private async Task<StreamActionResult?> RunStreamActionAsync(string emoji, Func<Task<StreamActionResult>> action)
    {
        if (!IsConnected)
        {
            const string text = "⚠️ Connect YouTube to use stream actions";
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
            _logger.LogError(ex, "YouTube stream action failed");
            var text = $"⚠️ YouTube action failed: {ex.Message}";
            AddActivity(text);
            StatusMessage = text;
            return null;
        }
    }

    // ======================================================================
    // Activity Feed (live chat messages + live-chat state changes)
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
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<YouTubeChatMessageEvent>(OnChatMessageAsync));
            _subscriptionIds.Add(await _eventBus.SubscribeAsync<YouTubeLiveChatStateEvent>(OnLiveChatStateAsync));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to YouTube dashboard activity events");
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

    private Task OnChatMessageAsync(YouTubeChatMessageEvent e)
    {
        var text = e.Message.Length > 120
            ? $"💬 {e.DisplayName}: {e.Message[..120]}…"
            : $"💬 {e.DisplayName}: {e.Message}";
        AddActivity(text);
        return Task.CompletedTask;
    }

    private Task OnLiveChatStateAsync(YouTubeLiveChatStateEvent e)
    {
        // "Listening" means the service is polling an active live chat => the channel is live
        IsLive = string.Equals(e.State, "Listening", StringComparison.OrdinalIgnoreCase);
        AddActivity($"📺 {e.Message}");
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
                    _logger.LogWarning(ex, "Failed to add YouTube dashboard activity entry");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to marshal YouTube dashboard activity to the UI thread");
        }
    }
}
