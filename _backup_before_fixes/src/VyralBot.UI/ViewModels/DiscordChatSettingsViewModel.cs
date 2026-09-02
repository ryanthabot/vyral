using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VyralBot.Data;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.UI.ViewModels;

public partial class DiscordChatSettingsViewModel : ViewModelBase
{
    private readonly ILogger<DiscordChatSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;

    [ObservableProperty]
    private bool _isChatEnabled;

    [ObservableProperty]
    private bool _autoStartChat;

    [ObservableProperty]
    private bool _autoRepliesEnabled = true;

    [ObservableProperty]
    private string _status = "";

    public DiscordChatSettingsViewModel(ILogger<DiscordChatSettingsViewModel> logger, IDataService dataService, IEventBus eventBus)
    {
        _logger = logger;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task InitializeAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");

            if (platform != null)
            {
                IsChatEnabled = platform.DiscordChatEnabled;

                if (!platform.DiscordChatEnabled && platform.Status == "Connected")
                {
                    IsChatEnabled = true;
                    platform.DiscordChatEnabled = true;
                    platform.UpdatedAt = DateTime.UtcNow;
                    await _dataService.SavePlatformAsync(platform);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing Discord chat settings");
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
        _ = PersistPlatformFlagAsync("Discord", value);
    }

    private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform != null)
            {
                platform.DiscordChatEnabled = enabled;
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
            await _eventBus.PublishAsync(evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error persisting Discord chat enabled flag");
        }
    }
}
