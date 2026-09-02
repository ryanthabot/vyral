using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class YouTubeDashboardViewModel : ViewModelBase
{
    private readonly ILogger<YouTubeDashboardViewModel> _logger;
    private readonly IDataService _dataService;

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

    public YouTubeDashboardViewModel(
        ILogger<YouTubeDashboardViewModel> logger,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing YouTube settings");
        
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
            _logger.LogError(ex, "Error saving Twitch settings");
            Status = $"Error saving: {ex.Message}";
        }
    }
}
