using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class YoutubeChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<YoutubeChatSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private bool _isInitialized;

    [ObservableProperty]
    private bool _isChatEnabled;

    [ObservableProperty]
    private bool _autoStartChat;

    [ObservableProperty]
    private bool _autoRepliesEnabled = true;

    [ObservableProperty]
    private string _status = "";

    public YoutubeChatSettingsViewModel(ILogger<YoutubeChatSettingsViewModel> logger, IDataService dataService, IEventBus eventBus)
    {
        _logger = logger;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");

            if (platform != null)
            {
                // Just load the persisted values — no force-enable: the user's
                // "chat disabled" choice must survive reconnects.
                _isInitialized = false;
                IsChatEnabled = platform.YoutubeChatEnabled;
                AutoStartChat = platform.YoutubeChatAutoStart;
                _isInitialized = true;
            }
            else
            {
                _isInitialized = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing YouTube chat settings");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            // Persist the current values via the same paths the toggles use
            // (PersistPlatformFlagAsync also publishes the routing-changed event).
            await PersistPlatformFlagAsync("YouTube", IsChatEnabled);
            await PersistAutoStartFlagAsync(AutoStartChat);

            Status = "Saved";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving YouTube chat settings");
            Status = "Failed to save settings";
        }
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistPlatformFlagAsync("YouTube", value);
        }
    }

    partial void OnAutoStartChatChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistAutoStartFlagAsync(value);
        }
    }

    /// <summary>Persists the YoutubeChatAutoStart flag consumed at app launch (Settings → Platforms).</summary>
    private async Task PersistAutoStartFlagAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform != null)
            {
                platform.YoutubeChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting YouTube auto-start flag");
        }
    }

    private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform != null)
            {
                platform.YoutubeChatEnabled = enabled;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting YouTube chat enabled flag");
        }
    }
}
