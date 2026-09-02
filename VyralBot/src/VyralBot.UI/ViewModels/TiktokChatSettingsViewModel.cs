using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class TiktokChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<TiktokChatSettingsViewModel> _logger;
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

    public TiktokChatSettingsViewModel(ILogger<TiktokChatSettingsViewModel> logger, IDataService dataService, IEventBus eventBus)
    {
        _logger = logger;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");

            if (platform != null)
            {
                _isInitialized = false;
                IsChatEnabled = platform.TiktokChatEnabled;
                AutoStartChat = platform.TiktokChatAutoStart;

                if (!platform.TiktokChatEnabled && platform.Status == "Connected")
                {
                    IsChatEnabled = true;
                    platform.TiktokChatEnabled = true;
                    platform.UpdatedAt = DateTime.UtcNow;
                    await _dataService.SavePlatformAsync(platform);
                }

                _isInitialized = true;
            }
            else
            {
                _isInitialized = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing TikTok chat settings");
        }
    }

    [RelayCommand]
    private Task SaveSettingsAsync()
    {
        Status = "Saved";
        return Task.CompletedTask;
    }

    partial void OnIsChatEnabledChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistPlatformFlagAsync("TikTok", value);
        }
    }

    partial void OnAutoStartChatChanged(bool value)
    {
        if (_isInitialized)
        {
            _ = PersistAutoStartFlagAsync(value);
        }
    }

    /// <summary>Persists the TiktokChatAutoStart flag consumed at app launch (Settings → Platforms).</summary>
    private async Task PersistAutoStartFlagAsync(bool value)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("TikTok");
            if (platform != null)
            {
                platform.TiktokChatAutoStart = value;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting TikTok auto-start flag");
        }
    }

    private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform != null)
            {
                platform.TiktokChatEnabled = enabled;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting TikTok chat enabled flag");
        }
    }
}
