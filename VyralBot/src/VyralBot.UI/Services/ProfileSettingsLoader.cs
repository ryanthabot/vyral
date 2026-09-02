using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Services;

/// <summary>
/// Centralized service to load all profile settings when a profile is initialized.
/// Ensures all ViewModels have their settings loaded from the profile database.
/// </summary>
public class ProfileSettingsLoader
{
    private readonly ILogger<ProfileSettingsLoader> _logger;
    private readonly IDataService _dataService;

    public ProfileSettingsLoader(
        ILogger<ProfileSettingsLoader> logger,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    /// <summary>
    /// Loads all profile settings into ViewModels. Called after profile is selected/loaded.
    /// This ensures ALL settings are loaded BEFORE any auto-connections or feature initialization.
    /// </summary>
    public async Task LoadAllSettingsAsync(
        PlatformsViewModel? platformsViewModel,
        KickSettingsViewModel kickSettingsViewModel,
        WebServerSettingsViewModel webServerSettingsViewModel,
        ChatViewModel chatViewModel)
    {
        _logger.LogInformation("🔄 Loading ALL profile settings (Twitch, Kick, ngrok, chat, webhooks)...");

        try
        {
            // 1. Load platform connection settings (tokens, credentials) - only if Platforms view is open
            if (platformsViewModel != null)
            {
                _logger.LogInformation("📋 Loading platform settings...");
                await LoadPlatformSettingsAsync(platformsViewModel);
            }
            else
            {
                _logger.LogInformation("📋 Skipping platform settings (lazy-load on demand)");
            }

            // 2. Load Kick-specific settings (chat, webhooks, auto-replies)
            _logger.LogInformation("⚙️ Loading Kick settings...");
            await kickSettingsViewModel.InitializeAsync();

            // 3. Load web server/ngrok settings (auth token, port, region, auto-start)
            _logger.LogInformation("🌐 Loading web server/ngrok settings...");
            await webServerSettingsViewModel.InitializeAsync();

            // 4. Initialize chat with loaded settings (enables message display immediately)
            _logger.LogInformation("💬 Initializing chat with loaded settings...");
            await chatViewModel.InitializeAsync();

            _logger.LogInformation("✅ All profile settings loaded successfully - app ready for auto-connections");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error loading profile settings");
            throw;
        }
    }

    private async Task LoadPlatformSettingsAsync(PlatformsViewModel platformsViewModel)
    {
        _logger.LogInformation("Loading platform credentials and connection state...");

        // Load Twitch platform data (tokens, username, chat settings)
        await platformsViewModel.LoadTwitchPlatformAsync();
        
        // Load Kick platform data (session cookies, username, chat settings, webhook config)
        await platformsViewModel.LoadKickPlatformAsync();

        _logger.LogInformation("Platform settings loaded from profile database");
    }
}
