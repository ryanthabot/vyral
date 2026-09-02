using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for Twitch platform configuration settings.
/// Accessible via Settings → Platforms → Twitch card.
/// </summary>
public partial class TwitchSettingsViewModel : ViewModelBase
{
    private readonly ILogger<TwitchSettingsViewModel> _logger;

    [ObservableProperty]
    private string _statusMessage = "Twitch configuration settings coming soon...";

    public TwitchSettingsViewModel(ILogger<TwitchSettingsViewModel> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Twitch settings");
        await Task.CompletedTask;
    }
}
