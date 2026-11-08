using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Profiles;
using VyralBot.Core.Models;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Data;
using VyralBot.Integrations.Lights;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Hosting;
using VyralBot.Integrations.StreamDeck;
using VyralBot.UI.Models;
using VyralBot.UI.Services;
using VyralBot.UI.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for application settings including profile management.
/// </summary>
public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IProfileService _profileService;
    private readonly IDataService _dataService;
    private readonly WebServerSettingsViewModel _webServerSettingsViewModel;
    private readonly VariablesViewModel _variablesViewModel;
    private readonly PerformanceViewModel _performanceViewModel;
    private readonly ViewerHubViewModel _viewerHubViewModel;
    private readonly PlatformStartupSettingsViewModel _platformStartupSettingsViewModel;
    private readonly DataManagementViewModel _dataManagementViewModel;
    private readonly Core.Services.Commands.ICommandService _commandService;
    private readonly WorkflowTriggerService _workflowTriggerService;
    private readonly StreamEffectsConfigService _streamEffectsConfigService;
    private readonly ILightsClient _lightsClient;
    private readonly IStreamDeckService _streamDeckService;
    private readonly ObsBundledCollectionService _bundledCollectionService;
    private readonly ObsCollectionUpdateService _collectionUpdateService;
    private readonly ObsInstallLocator _obsInstallLocator;
    private readonly ObsEngine _obsEngine;
    private readonly ObsStudioService _obsStudioService;
    private readonly IProcessManager _processManager;
    private bool _categorySettingsLoaded;

    public WebServerSettingsViewModel WebServerSettingsViewModel => _webServerSettingsViewModel;
    public VariablesViewModel VariablesViewModel => _variablesViewModel;
    public PerformanceViewModel PerformanceViewModel => _performanceViewModel;
    public ViewerHubViewModel ViewerHubViewModel => _viewerHubViewModel;
    public PlatformStartupSettingsViewModel PlatformStartupSettingsViewModel => _platformStartupSettingsViewModel;
    public DataManagementViewModel DataManagementViewModel => _dataManagementViewModel;

    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private Profile? _currentProfile;

    [ObservableProperty]
    private bool _autoLogin;

    [ObservableProperty]
    private string _selectedCategory = "Profile";

    partial void OnSelectedCategoryChanged(string value)
    {
        // Clear platform settings view when navigating away from Platforms
        if (value != "Platforms")
        {
            SelectedPlatformViewModel = null;
            ShowPlatformGrid = true;
        }

        // Run the ViewerHub pending-codes poller (30s DispatcherTimer) only while
        // its section is visible. ViewerHubViewModel is a singleton that outlives
        // this page, so without this stop it would poll SQLite for the whole app
        // lifetime. Cleanup/StartCodeRefresh are both idempotent, and the timer
        // restarts on the next Settings load via ViewerHubViewModel.InitializeAsync
        // (which also covers re-entry while SelectedCategory is still "ViewerHub").
        if (value == "ViewerHub")
        {
            _viewerHubViewModel.StartCodeRefresh();
        }
        else
        {
            _viewerHubViewModel.Cleanup();
        }
    }

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private bool _showChangePinDialog;

    [ObservableProperty]
    private string _currentPin = string.Empty;

    [ObservableProperty]
    private string _newPin = string.Empty;

    [ObservableProperty]
    private string _confirmNewPin = string.Empty;

    [ObservableProperty]
    private bool _showCreateProfileDialog;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileDisplayName = string.Empty;

    [ObservableProperty]
    private string _newProfilePin = string.Empty;

    [ObservableProperty]
    private bool _showSwitchProfileDialog;

    [ObservableProperty]
    private string _switchProfilePin = string.Empty;

    /// <summary>
    /// Display name of the profile pending a switch (backing the switch dialog text —
    /// bound in XAML, so it must be a public property, unlike _profileToSwitchTo).
    /// </summary>
    [ObservableProperty]
    private string? _pendingSwitchProfileName;

    [ObservableProperty]
    private object? _selectedPlatformViewModel;

    [ObservableProperty]
    private bool _showPlatformGrid = true;

    // ===== Theme picker state =====

    public ObservableCollection<ThemeOption> Themes { get; } = new(ThemeCatalog.BuiltInThemes);

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private bool _customIsLight;

    [ObservableProperty]
    private string _customPrimaryHex = "#14B8A6";

    [ObservableProperty]
    private string _customLogoVariant = "native";

    /// <summary>
    /// Visual color picker state (System.Windows.Media.Color), kept in sync with
    /// <see cref="_customPrimaryHex"/> in both directions. TwoWay-bound to the picker control;
    /// when the PICKER changes this value it propagates into CustomPrimaryHex and triggers the
    /// debounced live accent apply. External hex changes (hex box, swatches, built-in theme
    /// syncs) flow back into the picker through here.
    /// </summary>
    [ObservableProperty]
    private System.Windows.Media.Color _customPrimaryHexPickerColor = System.Windows.Media.Color.FromRgb(0x14, 0xB8, 0xA6);

    private bool _pickerDrivingHex;
    private bool _syncingHexToPicker;
    private System.Windows.Threading.DispatcherTimer? _customAccentDebounce;

    /// <summary>~120ms debounce so continuous picker drags don't storm app resources / settings I/O.</summary>
    private const int CustomAccentDebounceMs = 120;

    partial void OnCustomPrimaryHexChanged(string value)
    {
        // Mirror external hex changes (hex box, swatches, built-in theme syncs) into the visual picker.
        // The sync is flagged so the picker handler does not echo back into CustomPrimaryHex
        // (which would canonicalize the stored value and could queue a spurious live apply).
        if (ColorShadeGenerator.TryParseHex(value, out var color) && color != CustomPrimaryHexPickerColor)
        {
            _syncingHexToPicker = true;
            try
            {
                CustomPrimaryHexPickerColor = color;
            }
            finally
            {
                _syncingHexToPicker = false;
            }
        }

        // Only picker-driven changes go through the live apply path: hex box edits keep the
        // explicit "Apply Custom Theme" button, and switching a built-in theme (which syncs
        // CustomPrimaryHex) must NOT re-apply a custom accent on top of it.
        if (_pickerDrivingHex)
            QueueCustomAccentApply();
    }

    partial void OnCustomPrimaryHexPickerColorChanged(System.Windows.Media.Color value)
    {
        if (_syncingHexToPicker) return; // VM-driven sync — never echo back

        // The picker control is the source → propagate as the custom accent (debounced).
        _pickerDrivingHex = true;
        try
        {
            CustomPrimaryHex = ColorShadeGenerator.ToHex(value);
        }
        finally
        {
            _pickerDrivingHex = false;
        }
    }

    /// <summary>Restarts the ~120ms debounce; the tick pushes the accent into ThemeManager live.</summary>
    private void QueueCustomAccentApply()
    {
        if (!ColorShadeGenerator.TryParseHex(CustomPrimaryHex, out _))
            return; // never push garbage — picker output is always valid, this is belt-and-braces

        _customAccentDebounce ??= CreateCustomAccentDebounce();
        _customAccentDebounce.Stop();
        _customAccentDebounce.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateCustomAccentDebounce()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CustomAccentDebounceMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ColorShadeGenerator.TryParseHex(CustomPrimaryHex, out _)) return;

            // Accent-only live update (no base dictionary swap) — smooth while dragging.
            ThemeManager.Instance.UpdateCustomAccent(CustomPrimaryHex);
            SelectedTheme = ThemeCatalog.CustomOption; // no-op after the first tick (early-returns for custom)
            StatusMessage = "Custom accent applied";
            _ = ClearStatusAfterDelayAsync();
        };
        return timer;
    }

    // ===== Brightness levels (-100 darker .. +100 lighter, 0 = native) =====

    [ObservableProperty]
    private int _darkShadeLevel;

    [ObservableProperty]
    private int _lightShadeLevel;

    public string DarkShadeLevelText => DescribeShadeLevel(DarkShadeLevel);

    public string LightShadeLevelText => DescribeShadeLevel(LightShadeLevel);

    private static string DescribeShadeLevel(int value)
        => value == 0 ? "Native" : $"{(value > 0 ? "+" : string.Empty)}{value} {(value > 0 ? "lighter" : "darker")}";

    private int _pendingDarkShade;
    private int _pendingLightShade;
    private System.Windows.Threading.DispatcherTimer? _darkShadeDebounce;
    private System.Windows.Threading.DispatcherTimer? _lightShadeDebounce;
    private bool _suppressShadeApply;

    partial void OnDarkShadeLevelChanged(int value)
    {
        OnPropertyChanged(nameof(DarkShadeLevelText));
        QueueShadeApply(isLightBase: false, value);
    }

    partial void OnLightShadeLevelChanged(int value)
    {
        OnPropertyChanged(nameof(LightShadeLevelText));
        QueueShadeApply(isLightBase: true, value);
    }

    /// <summary>Debounces rapid slider drags (~150ms) before touching app resources and settings.</summary>
    private void QueueShadeApply(bool isLightBase, int value)
    {
        if (_suppressShadeApply) return; // init population must not re-apply or persist

        if (isLightBase)
        {
            _pendingLightShade = value;
            _lightShadeDebounce ??= CreateShadeDebounce(isLightBase: true);
            _lightShadeDebounce.Stop();
            _lightShadeDebounce.Start();
        }
        else
        {
            _pendingDarkShade = value;
            _darkShadeDebounce ??= CreateShadeDebounce(isLightBase: false);
            _darkShadeDebounce.Stop();
            _darkShadeDebounce.Start();
        }
    }

    private System.Windows.Threading.DispatcherTimer CreateShadeDebounce(bool isLightBase)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var themeManager = ThemeManager.Instance;
            var value = isLightBase ? _pendingLightShade : _pendingDarkShade;
            themeManager.ApplyShadeLevel(isLightBase, value);

            // The slider edits the OTHER base while a mismatched theme is active —
            // the value was persisted, but nothing changed on screen.
            if (isLightBase != themeManager.IsLightBaseActive())
            {
                StatusMessage = $"Saved — applies when a {(isLightBase ? "light" : "dark")} theme is active";
                _ = ClearStatusAfterDelayAsync();
            }
        };
        return timer;
    }

    public IReadOnlyList<string> LogoVariants => ThemeCatalog.ValidLogoVariants;

    public IReadOnlyList<string> CustomAccentSwatches { get; } = new[]
    {
        "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#84CC16", "#10B981", "#14B8A6", "#06B6D4",
        "#0EA5E9", "#3B82F6", "#6366F1", "#8B5CF6", "#A855F7", "#EC4899", "#F43F5E", "#94A3B8",
    };

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null || value.IsBuiltIn == false) return;
        CustomIsLight = value.IsLight;
        CustomPrimaryHex = value.AccentHex;
        CustomLogoVariant = value.LogoVariant;
    }

    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private bool _isKickConnected;

    [ObservableProperty]
    private bool _isYouTubeConnected;

    [ObservableProperty]
    private bool _isTikTokConnected;

    [ObservableProperty]
    private bool _isDiscordConnected;

    [ObservableProperty]
    private bool _isOBSConnected;

    private Profile? _profileToDelete;
    private Profile? _profileForPinChange;
    private Profile? _profileToSwitchTo;

    private readonly IServiceProvider _serviceProvider;

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        IProfileService profileService,
        IDataService dataService,
        WebServerSettingsViewModel webServerSettingsViewModel,
        VariablesViewModel variablesViewModel,
        PerformanceViewModel performanceViewModel,
        ViewerHubViewModel viewerHubViewModel,
        PlatformStartupSettingsViewModel platformStartupSettingsViewModel,
        DataManagementViewModel dataManagementViewModel,
        Core.Services.Commands.ICommandService commandService,
        WorkflowTriggerService workflowTriggerService,
        StreamEffectsConfigService streamEffectsConfigService,
        ILightsClient lightsClient,
        IStreamDeckService streamDeckService,
        ObsBundledCollectionService bundledCollectionService,
        ObsCollectionUpdateService collectionUpdateService,
        ObsInstallLocator obsInstallLocator,
        ObsEngine obsEngine,
        ObsStudioService obsStudioService,
        IProcessManager processManager,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _webServerSettingsViewModel = webServerSettingsViewModel ?? throw new ArgumentNullException(nameof(webServerSettingsViewModel));
        _variablesViewModel = variablesViewModel ?? throw new ArgumentNullException(nameof(variablesViewModel));
        _performanceViewModel = performanceViewModel ?? throw new ArgumentNullException(nameof(performanceViewModel));
        _viewerHubViewModel = viewerHubViewModel ?? throw new ArgumentNullException(nameof(viewerHubViewModel));
        _platformStartupSettingsViewModel = platformStartupSettingsViewModel ?? throw new ArgumentNullException(nameof(platformStartupSettingsViewModel));
        _dataManagementViewModel = dataManagementViewModel ?? throw new ArgumentNullException(nameof(dataManagementViewModel));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _workflowTriggerService = workflowTriggerService ?? throw new ArgumentNullException(nameof(workflowTriggerService));
        _streamEffectsConfigService = streamEffectsConfigService ?? throw new ArgumentNullException(nameof(streamEffectsConfigService));
        _lightsClient = lightsClient ?? throw new ArgumentNullException(nameof(lightsClient));
        _streamDeckService = streamDeckService ?? throw new ArgumentNullException(nameof(streamDeckService));
        _bundledCollectionService = bundledCollectionService ?? throw new ArgumentNullException(nameof(bundledCollectionService));
        _collectionUpdateService = collectionUpdateService ?? throw new ArgumentNullException(nameof(collectionUpdateService));
        _obsInstallLocator = obsInstallLocator ?? throw new ArgumentNullException(nameof(obsInstallLocator));
        _obsEngine = obsEngine ?? throw new ArgumentNullException(nameof(obsEngine));
        _obsStudioService = obsStudioService ?? throw new ArgumentNullException(nameof(obsStudioService));
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Settings view");
        try
        {
            await LoadProfilesAsync();
            await LoadSettingsAsync();
            await LoadPlatformConnectionStatusAsync();
            await _variablesViewModel.InitializeAsync();
            await _viewerHubViewModel.InitializeAsync();
            await _platformStartupSettingsViewModel.InitializeAsync();
            await _dataManagementViewModel.InitializeAsync();
            await LoadCategorySettingsAsync();

            // Load current theme state for the picker
            var themeManager = ThemeManager.Instance;
            SelectedTheme = ThemeCatalog.Find(themeManager.CurrentThemeId)
                            ?? ThemeCatalog.Find(ThemeCatalog.NativeId);
            CustomIsLight = string.Equals(themeManager.Preferences.Custom.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase);
            CustomPrimaryHex = themeManager.Preferences.Custom.PrimaryHex;
            CustomLogoVariant = themeManager.Preferences.Custom.LogoVariant;

            // Populate the brightness sliders from persisted preferences (guarded so
            // population itself does not re-apply or re-persist).
            _suppressShadeApply = true;
            DarkShadeLevel = themeManager.GetShadeLevel(isLightBase: false);
            LightShadeLevel = themeManager.GetShadeLevel(isLightBase: true);
            _suppressShadeApply = false;

            // Note: WebServerSettingsViewModel is already initialized via DI
            // No need to reinitialize since we dispose/recreate SettingsViewModel on each navigation

            _logger.LogInformation("Settings view initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing settings");
            StatusMessage = $"Error initializing settings: {ex.Message}";
        }
    }

    private async Task LoadPlatformConnectionStatusAsync()
    {
        try
        {
            var twitchPlatform = await _dataService.GetPlatformByNameAsync("Twitch");
            IsTwitchConnected = twitchPlatform?.Status == "Connected";

            var kickPlatform = await _dataService.GetPlatformByNameAsync("Kick");
            IsKickConnected = kickPlatform?.Status == "Connected";

            var youtubePlatform = await _dataService.GetPlatformByNameAsync("YouTube");
            IsYouTubeConnected = youtubePlatform?.Status == "Connected";

            var tiktokPlatform = await _dataService.GetPlatformByNameAsync("TikTok");
            IsTikTokConnected = tiktokPlatform?.Status == "Connected";

            var discordPlatform = await _dataService.GetPlatformByNameAsync("Discord");
            IsDiscordConnected = discordPlatform?.Status == "Connected";

            var obsPlatform = await _dataService.GetPlatformByNameAsync("OBS");
            IsOBSConnected = obsPlatform?.Status == "Connected";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading platform connection status");
        }
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
        _logger.LogInformation("Selected settings category: {Category}", category);
    }

    [RelayCommand]
    private void ShowSwitchProfileDialogForProfile(Profile profile)
    {
        _logger.LogInformation("User clicked: Switch to Profile '{DisplayName}' ({Name})", profile.DisplayName, profile.Name);
        
        if (profile.Name == CurrentProfile?.Name)
        {
            StatusMessage = "Already using this profile.";
            return;
        }

        _profileToSwitchTo = profile;
        PendingSwitchProfileName = profile.DisplayName;
        SwitchProfilePin = string.Empty;
        ShowSwitchProfileDialog = true;
        StatusMessage = null;
    }

    [RelayCommand]
    private void CancelSwitchProfile()
    {
        _logger.LogInformation("User clicked: Cancel Switch Profile");
        ShowSwitchProfileDialog = false;
        _profileToSwitchTo = null;
        PendingSwitchProfileName = null;
        SwitchProfilePin = string.Empty;
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmSwitchProfileAsync()
    {
        if (_profileToSwitchTo == null) return;

        _logger.LogInformation("User clicked: Confirm Switch to Profile '{Name}'", _profileToSwitchTo.Name);

        try
        {
            // Validate PIN if profile has one
            if (!string.IsNullOrEmpty(_profileToSwitchTo.PinCode))
            {
                _logger.LogInformation("Profile has PIN, validating...");
                var isValid = await _profileService.ValidatePinAsync(_profileToSwitchTo.Name, SwitchProfilePin);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid PIN provided for profile switch");
                    StatusMessage = "Invalid PIN.";
                    return;
                }
                _logger.LogInformation("PIN validated successfully");
            }
            else
            {
                _logger.LogInformation("Profile has no PIN, switching directly");
            }

            await _profileService.SwitchProfileAsync(_profileToSwitchTo.Name, string.IsNullOrEmpty(SwitchProfilePin) ? null : SwitchProfilePin);
            
            _logger.LogInformation("Successfully switched to profile '{Name}'", _profileToSwitchTo.Name);
            StatusMessage = "Profile switched successfully!";
            ShowSwitchProfileDialog = false;
            _profileToSwitchTo = null;
            PendingSwitchProfileName = null;
            SwitchProfilePin = string.Empty;

            // Reload settings with new profile
            await LoadProfilesAsync();
            await LoadSettingsAsync();

            // Reload the config-table-backed category settings for the new profile
            // (reset the loaded flag first so repopulation doesn't auto-save back)
            _categorySettingsLoaded = false;
            await LoadCategorySettingsAsync();

            await Task.Delay(1500);
            StatusMessage = null;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Unauthorized access when switching profile");
            StatusMessage = "Invalid PIN.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching profile '{ProfileName}'", _profileToSwitchTo?.Name);
            StatusMessage = $"Error switching profile: {ex.Message}";
        }
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            var profilesList = await _profileService.GetProfilesAsync();
            Profiles = new ObservableCollection<Profile>(profilesList);

            var current = await _profileService.GetCurrentProfileAsync();
            // GetCurrentProfileAsync returns null on first run / missing current_profile.txt
            CurrentProfile = current is null ? null : Profiles.FirstOrDefault(p => p.Name == current.Name);
            
            _logger.LogInformation("Loaded {Count} profiles", Profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading profiles");
            StatusMessage = $"Error loading profiles: {ex.Message}";
        }
    }

    [ObservableProperty]
    private string? _discordInviteUrl;

    [ObservableProperty]
    private string? _ideasUrl;

    private async Task LoadSettingsAsync()
    {
        try
        {
            var currentProfile = await _profileService.GetCurrentProfileAsync();
            if (currentProfile != null && currentProfile.Settings.TryGetValue("AutoLogin", out var autoLoginValue))
            {
                AutoLogin = bool.TryParse(autoLoginValue, out var enabled) && enabled;
                _logger.LogInformation("Loaded auto-login setting: {AutoLogin}", AutoLogin);
            }
            else
            {
                AutoLogin = false;
            }

            DiscordInviteUrl = await _dataService.GetConfigValueAsync("Support.DiscordUrl");
            IdeasUrl = await _dataService.GetConfigValueAsync("Support.IdeasUrl");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
            AutoLogin = false;
        }
    }

    [RelayCommand]
    private async Task SaveSupportLinksAsync()
    {
        try
        {
            await _dataService.SetConfigValueAsync("Support.DiscordUrl", DiscordInviteUrl?.Trim() ?? string.Empty, "Support");
            await _dataService.SetConfigValueAsync("Support.IdeasUrl", IdeasUrl?.Trim() ?? string.Empty, "Support");

            StatusMessage = "Support links saved";
            _logger.LogInformation("Support links saved (Discord set: {Discord}, Ideas set: {Ideas})",
                !string.IsNullOrWhiteSpace(DiscordInviteUrl), !string.IsNullOrWhiteSpace(IdeasUrl));

            await Task.Delay(2000);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving support links");
            StatusMessage = "Failed to save support links";
        }
    }

    [RelayCommand]
    private async Task SaveAutoLoginAsync()
    {
        _logger.LogInformation("User clicked: Save Auto-Login (Enabled: {Enabled})", AutoLogin);
        try
        {
            var currentProfile = await _profileService.GetCurrentProfileAsync();
            if (currentProfile != null)
            {
                currentProfile.Settings["AutoLogin"] = AutoLogin.ToString();
                await _profileService.UpdateProfileAsync(currentProfile);
                
                StatusMessage = $"Auto-login {(AutoLogin ? "enabled" : "disabled")}";
                _logger.LogInformation("Auto-login setting updated: {AutoLogin}", AutoLogin);
                
                await Task.Delay(2000);
                StatusMessage = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving auto-login setting");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RequestDeleteProfile(Profile profile)
    {
        _logger.LogInformation("User clicked: Delete Profile '{DisplayName}' ({Name})", profile.DisplayName, profile.Name);
        
        if (profile == CurrentProfile)
        {
            _logger.LogWarning("Cannot delete currently active profile");
            StatusMessage = "Cannot delete the currently active profile.";
            return;
        }

        _profileToDelete = profile;
        ShowDeleteConfirmation = true;
    }

    [RelayCommand]
    private void ShowChangePinDialogForProfile(Profile profile)
    {
        _logger.LogInformation("User clicked: Change PIN for Profile '{DisplayName}' ({Name})", profile.DisplayName, profile.Name);
        
        if (profile != CurrentProfile)
        {
            StatusMessage = "You can only change the PIN for the currently active profile.";
            return;
        }

        _profileForPinChange = profile;
        CurrentPin = string.Empty;
        NewPin = string.Empty;
        ConfirmNewPin = string.Empty;
        ShowChangePinDialog = true;
        StatusMessage = null;
    }

    [RelayCommand]
    private void CancelChangePin()
    {
        _logger.LogInformation("User clicked: Cancel Change PIN");
        ShowChangePinDialog = false;
        _profileForPinChange = null;
        CurrentPin = string.Empty;
        NewPin = string.Empty;
        ConfirmNewPin = string.Empty;
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmChangePinAsync()
    {
        if (_profileForPinChange == null) return;

        _logger.LogInformation("User clicked: Confirm Change PIN for '{ProfileName}'", _profileForPinChange.Name);

        try
        {
            // Validate new PIN format
            if (!string.IsNullOrEmpty(NewPin))
            {
                if (NewPin != ConfirmNewPin)
                {
                    _logger.LogWarning("New PIN and confirmation don't match");
                    StatusMessage = "New PIN and confirmation don't match.";
                    return;
                }

                if (NewPin.Length < 4 || NewPin.Length > 6)
                {
                    _logger.LogWarning("New PIN must be 4-6 digits");
                    StatusMessage = "PIN must be 4-6 digits.";
                    return;
                }
            }

            // Update PIN using the new service method
            await _profileService.UpdateProfilePinAsync(
                _profileForPinChange.Name,
                string.IsNullOrEmpty(CurrentPin) ? null : CurrentPin,
                string.IsNullOrEmpty(NewPin) ? null : NewPin
            );

            _logger.LogInformation("Successfully updated PIN for profile '{ProfileName}'", _profileForPinChange.Name);
            StatusMessage = "PIN updated successfully!";
            ShowChangePinDialog = false;
            _profileForPinChange = null;
            CurrentPin = string.Empty;
            NewPin = string.Empty;
            ConfirmNewPin = string.Empty;
            
            await LoadProfilesAsync();
            
            await Task.Delay(2000);
            StatusMessage = null;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Invalid current PIN provided");
            StatusMessage = "Current PIN is incorrect.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing PIN for profile '{ProfileName}'", _profileForPinChange?.Name);
            StatusMessage = $"Error changing PIN: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowCreateProfileDialogForSettings()
    {
        _logger.LogInformation("User clicked: Create New Profile from Settings");
        NewProfileName = string.Empty;
        NewProfileDisplayName = string.Empty;
        NewProfilePin = string.Empty;
        ShowCreateProfileDialog = true;
        StatusMessage = null;
    }

    [RelayCommand]
    private void CancelCreateProfile()
    {
        _logger.LogInformation("User clicked: Cancel Create Profile");
        ShowCreateProfileDialog = false;
        NewProfileName = string.Empty;
        NewProfileDisplayName = string.Empty;
        NewProfilePin = string.Empty;
        StatusMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmCreateProfileAsync()
    {
        _logger.LogInformation("User clicked: Confirm Create New Profile (Name: '{Name}')", NewProfileName);

        try
        {
            // Validate profile name
            if (string.IsNullOrWhiteSpace(NewProfileName))
            {
                StatusMessage = "Profile name is required.";
                _logger.LogWarning("Profile creation failed: Name is empty");
                return;
            }

            // Create the profile
            var displayName = string.IsNullOrWhiteSpace(NewProfileDisplayName) ? NewProfileName : NewProfileDisplayName;
            var pin = string.IsNullOrWhiteSpace(NewProfilePin) ? null : NewProfilePin;

            _logger.LogInformation("Creating profile '{Name}' with display name '{DisplayName}'", NewProfileName, displayName);
            var profile = await _profileService.CreateProfileAsync(NewProfileName, displayName, pin);

            _logger.LogInformation("Successfully created profile '{Name}'", profile.Name);
            StatusMessage = $"Profile '{profile.DisplayName}' created successfully!";
            ShowCreateProfileDialog = false;

            // Refresh profiles list
            await LoadProfilesAsync();

            await Task.Delay(2000);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating profile '{Name}'", NewProfileName);
            StatusMessage = $"Error creating profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _logger.LogInformation("User clicked: Cancel Delete Profile");
        _profileToDelete = null;
        ShowDeleteConfirmation = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_profileToDelete == null) return;

        _logger.LogInformation("User clicked: Confirm Delete Profile '{ProfileName}'", _profileToDelete.Name);

        // PIN-protected profiles require their PIN before deletion (the
        // profile's data can hold tokens and currency balances).
        if (!string.IsNullOrEmpty(_profileToDelete.PinCode))
        {
            var pin = PromptForDeletePin(_profileToDelete.DisplayName);
            if (pin == null)
            {
                _logger.LogInformation("Profile deletion cancelled at PIN prompt for profile {Profile}", _profileToDelete.Name);
                ShowDeleteConfirmation = false;
                return;
            }

            if (!await _profileService.ValidatePinAsync(_profileToDelete.Name, pin))
            {
                _logger.LogWarning("Incorrect PIN entered when deleting profile {Profile}", _profileToDelete.Name);
                StatusMessage = "Incorrect PIN. Profile not deleted.";
                ShowDeleteConfirmation = false;
                return;
            }
        }

        try
        {
            _logger.LogInformation("Deleting profile: {ProfileName}", _profileToDelete.Name);
            
            // Clear database connection to release file locks
            if (_dataService is UI.Services.ProfileAwareDataService profileAwareService)
            {
                profileAwareService.ClearConnection();
            }
            
            // Give the system time to release file handles
            await Task.Delay(200);
            
            await _profileService.DeleteProfileAsync(_profileToDelete.Name);
            
            Profiles.Remove(_profileToDelete);
            StatusMessage = $"Profile '{_profileToDelete.DisplayName}' deleted successfully.";
            
            _profileToDelete = null;
            ShowDeleteConfirmation = false;
            
            await Task.Delay(2000);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile");
            StatusMessage = $"Error deleting profile: {ex.Message}";
            ShowDeleteConfirmation = false;
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync(Profile profile)
    {
        _logger.LogInformation("User clicked: Export Profile '{DisplayName}' ({Name})", profile.DisplayName, profile.Name);
        
        try
        {
            // TODO: Add file dialog to choose export location
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var exportPath = Path.Combine(documentsPath, $"{profile.Name}_backup.zip");
            
            var resultPath = await _profileService.ExportProfileAsync(profile.Name, exportPath);
            StatusMessage = $"Profile exported to: {resultPath}";
            _logger.LogInformation("Profile exported: {Path}", resultPath);
            
            await Task.Delay(3000);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting profile");
            StatusMessage = $"Error exporting profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NavigateToTwitchSettings()
    {
        _logger.LogInformation("User clicked: Navigate to Twitch Settings from Platforms panel");
        var twitchSettingsViewModel = _serviceProvider.GetRequiredService<TwitchSettingsViewModel>();
        SelectedPlatformViewModel = twitchSettingsViewModel;
        ShowPlatformGrid = false;
        await twitchSettingsViewModel.InitializeAsync();
    }

    [RelayCommand]
    private async Task NavigateToKickSettings()
    {
        _logger.LogInformation("User clicked: Navigate to Kick Settings from Platforms panel");
        var kickSettingsViewModel = _serviceProvider.GetRequiredService<KickSettingsViewModel>();
        SelectedPlatformViewModel = kickSettingsViewModel;
        ShowPlatformGrid = false;
        await kickSettingsViewModel.InitializeAsync();
    }

    [RelayCommand]
    private void BackToPlatformGrid()
    {
        _logger.LogInformation("User navigated back to platform selection grid");
        SelectedPlatformViewModel = null;
        ShowPlatformGrid = true;
    }

    // ===== Coming-soon platform cards (Facebook Gaming / Rumble) =====
    // Bound by SettingsView's platform grid; account linking already supports
    // both platforms, so only the full integration settings pages are pending.

    [RelayCommand]
    private async Task SelectFacebookAsync()
    {
        _logger.LogInformation("User selected the Facebook Gaming (coming soon) platform card");
        StatusMessage = "Facebook Gaming support is coming soon — account linking is already compatible.";
        await ClearStatusAfterDelayAsync();
    }

    [RelayCommand]
    private async Task SelectRumbleAsync()
    {
        _logger.LogInformation("User selected the Rumble (coming soon) platform card");
        StatusMessage = "Rumble support is coming soon — account linking is already compatible.";
        await ClearStatusAfterDelayAsync();
    }

    // ======================================================================
    // Notifications / Commands / Timers / Automation / Security / Advanced
    // (config-table backed; auto-saved on change)
    // ======================================================================

    // ----- Notifications -----

    [ObservableProperty]
    private bool _showEventsInChat = true;

    [ObservableProperty]
    private bool _eventSoundsEnabled = true;

    [ObservableProperty]
    private bool _doNotDisturb;

    partial void OnShowEventsInChatChanged(bool value)
    {
        if (_categorySettingsLoaded)
        {
            _ = SaveNotificationSettingAsync("Notifications.ShowEventsInChat", value, reloadChat: true);
        }
    }

    partial void OnEventSoundsEnabledChanged(bool value)
    {
        if (_categorySettingsLoaded)
        {
            _ = SaveNotificationSettingAsync("Notifications.EventSoundsEnabled", value, reloadChat: false);
        }
    }

    partial void OnDoNotDisturbChanged(bool value)
    {
        if (_categorySettingsLoaded)
        {
            // Focus mode: SoundService and AlertService read this key on every
            // playback/show, so the mute takes effect immediately (no restart).
            _ = SaveNotificationSettingAsync("Notifications.DoNotDisturb", value, reloadChat: false);
        }
    }

    private async Task SaveNotificationSettingAsync(string key, bool value, bool reloadChat)
    {
        try
        {
            await _dataService.SetConfigValueAsync(key, value.ToString(), "Notifications");

            if (reloadChat)
            {
                // SoundService reads its config on every playback; ChatViewModel
                // caches the display toggle, so ask it to re-read now.
                var chatViewModel = _serviceProvider.GetService<ChatViewModel>();
                if (chatViewModel != null)
                {
                    await chatViewModel.ReloadEventDisplayToggleAsync();
                }
            }

            StatusMessage = "Setting saved";
            _logger.LogInformation("{Key} saved: {Value}", key, value);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving {Key}", key);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PreviewNotificationAsync()
    {
        try
        {
            if (DoNotDisturb)
            {
                StatusMessage = "Do-not-disturb is on - notifications are muted right now";
                _logger.LogInformation("Notification preview skipped: do-not-disturb active");
                await ClearStatusAfterDelayAsync();
                return;
            }

            // Preview through the real pipelines so it is gated exactly like a live
            // event: the overlay goes through ShowAlertAsync(applySettings: true)
            // (Alert Settings master + per-event toggles) and the sound through
            // PlayEventSoundAsync, which re-reads the config on every playback.
            var alertService = _serviceProvider.GetService<IAlertService>();
            if (alertService != null)
            {
                await alertService.ShowAlertAsync("New Follower!", "PreviewFan");
            }

            var soundService = _serviceProvider.GetService<Core.Services.Audio.ISoundService>();
            if (soundService != null)
            {
                await soundService.PlayEventSoundAsync("follow", "Twitch");
            }

            StatusMessage = "Preview sent - if you saw/heard nothing, alerts or sounds are disabled";
            _logger.LogInformation("Notification preview sent (alert overlay + event sound)");
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification preview");
            StatusMessage = $"Error sending preview: {ex.Message}";
        }
    }

    // ----- Commands -----

    [ObservableProperty]
    private string _commandPrefixes = "!?.";

    [ObservableProperty]
    private bool _commandsEnabledOnTwitch = true;

    [ObservableProperty]
    private bool _commandsEnabledOnKick = true;

    [ObservableProperty]
    private bool _commandsEnabledOnDiscord = true;

    [RelayCommand]
    private async Task SaveCommandPrefixesAsync()
    {
        try
        {
            var prefixes = new string(CommandPrefixes.Where(c => !char.IsWhiteSpace(c)).Distinct().ToArray());
            if (prefixes.Length == 0)
            {
                StatusMessage = "Enter at least one command prefix character";
                return;
            }

            CommandPrefixes = prefixes;
            await _dataService.SetConfigValueAsync("Commands.Prefixes", prefixes, "Commands");
            await _commandService.InitializeAsync();
            await _workflowTriggerService.ReloadSettingsAsync();

            StatusMessage = $"Command prefixes saved: {prefixes}";
            _logger.LogInformation("Command prefixes saved: {Prefixes}", prefixes);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving command prefixes");
            StatusMessage = $"Error saving prefixes: {ex.Message}";
        }
    }

    partial void OnCommandsEnabledOnTwitchChanged(bool value) => SavePlatformCommandToggleAsync("Twitch", value);
    partial void OnCommandsEnabledOnKickChanged(bool value) => SavePlatformCommandToggleAsync("Kick", value);
    partial void OnCommandsEnabledOnDiscordChanged(bool value) => SavePlatformCommandToggleAsync("Discord", value);

    private void SavePlatformCommandToggleAsync(string platform, bool value)
    {
        if (!_categorySettingsLoaded)
        {
            return;
        }

        _ = SavePlatformCommandToggleCoreAsync(platform, value);
    }

    private async Task SavePlatformCommandToggleCoreAsync(string platform, bool value)
    {
        try
        {
            await _dataService.SetConfigValueAsync($"Commands.Enabled.{platform}", value.ToString(), "Commands");
            await _commandService.InitializeAsync();
            StatusMessage = $"Commands {(value ? "enabled" : "disabled")} on {platform}";
            _logger.LogInformation("Commands enabled on {Platform}: {Value}", platform, value);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving commands toggle for {Platform}", platform);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    // ----- Timers -----

    [ObservableProperty]
    private bool _commandTimersEnabled = true;

    partial void OnCommandTimersEnabledChanged(bool value)
    {
        if (_categorySettingsLoaded)
        {
            _ = SaveSimpleBoolSettingAsync("Timers.CommandTimersEnabled", value, "Timers");
        }
    }

    // ----- Automation -----

    [ObservableProperty]
    private bool _workflowsEnabled = true;

    [ObservableProperty]
    private bool _workflowCommandTriggersEnabled = true;

    [ObservableProperty]
    private bool _workflowEventTriggersEnabled = true;

    [ObservableProperty]
    private bool _workflowTimerTriggersEnabled = true;

    [ObservableProperty]
    private int _workflowTimerIntervalSeconds = 60;

    [ObservableProperty]
    private int _workflowHistoryRetentionDays = 90;

    partial void OnWorkflowsEnabledChanged(bool value) => SaveAutomationSettingAsync();
    partial void OnWorkflowCommandTriggersEnabledChanged(bool value) => SaveAutomationSettingAsync();
    partial void OnWorkflowEventTriggersEnabledChanged(bool value) => SaveAutomationSettingAsync();
    partial void OnWorkflowTimerTriggersEnabledChanged(bool value) => SaveAutomationSettingAsync();
    partial void OnWorkflowTimerIntervalSecondsChanged(int value) => SaveAutomationSettingAsync();
    partial void OnWorkflowHistoryRetentionDaysChanged(int value) => SaveAutomationSettingAsync();

    private bool _savingAutomationSettings;

    private async void SaveAutomationSettingAsync()
    {
        if (!_categorySettingsLoaded || _savingAutomationSettings)
        {
            return;
        }

        try
        {
            _savingAutomationSettings = true;
            await _dataService.SetConfigValueAsync("Automation.WorkflowsEnabled", WorkflowsEnabled.ToString(), "Automation");
            await _dataService.SetConfigValueAsync("Automation.CommandTriggersEnabled", WorkflowCommandTriggersEnabled.ToString(), "Automation");
            await _dataService.SetConfigValueAsync("Automation.EventTriggersEnabled", WorkflowEventTriggersEnabled.ToString(), "Automation");
            await _dataService.SetConfigValueAsync("Automation.TimerTriggersEnabled", WorkflowTimerTriggersEnabled.ToString(), "Automation");
            await _dataService.SetConfigValueAsync("Automation.TimerIntervalSeconds", Math.Clamp(WorkflowTimerIntervalSeconds, 30, 600).ToString(), "Automation");
            await _dataService.SetConfigValueAsync("Automation.ExecutionHistoryRetentionDays", Math.Max(0, WorkflowHistoryRetentionDays).ToString(), "Automation");

            await _workflowTriggerService.ReloadSettingsAsync();

            StatusMessage = "Automation settings saved";
            _logger.LogInformation("Automation settings saved (enabled={Enabled}, interval={Interval}s, retention={Retention}d)",
                WorkflowsEnabled, WorkflowTimerIntervalSeconds, WorkflowHistoryRetentionDays);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving automation settings");
            StatusMessage = $"Error saving automation settings: {ex.Message}";
        }
        finally
        {
            _savingAutomationSettings = false;
        }
    }

    private async Task SaveSimpleBoolSettingAsync(string key, bool value, string category)
    {
        try
        {
            await _dataService.SetConfigValueAsync(key, value.ToString(), category);
            StatusMessage = "Setting saved";
            _logger.LogInformation("{Key} saved: {Value}", key, value);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving {Key}", key);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    private async Task LoadCategorySettingsAsync()
    {
        try
        {
            ShowEventsInChat = await GetBoolAsync("Notifications.ShowEventsInChat", true);
            EventSoundsEnabled = await GetBoolAsync("Notifications.EventSoundsEnabled", true);
            DoNotDisturb = await GetBoolAsync("Notifications.DoNotDisturb", false);

            CommandPrefixes = await _dataService.GetConfigValueAsync("Commands.Prefixes") ?? "!?.";
            CommandsEnabledOnTwitch = await GetBoolAsync("Commands.Enabled.Twitch", true);
            CommandsEnabledOnKick = await GetBoolAsync("Commands.Enabled.Kick", true);
            CommandsEnabledOnDiscord = await GetBoolAsync("Commands.Enabled.Discord", true);

            CommandTimersEnabled = await GetBoolAsync("Timers.CommandTimersEnabled", true);

            WorkflowsEnabled = await GetBoolAsync("Automation.WorkflowsEnabled", true);
            WorkflowCommandTriggersEnabled = await GetBoolAsync("Automation.CommandTriggersEnabled", true);
            WorkflowEventTriggersEnabled = await GetBoolAsync("Automation.EventTriggersEnabled", true);
            WorkflowTimerTriggersEnabled = await GetBoolAsync("Automation.TimerTriggersEnabled", true);
            WorkflowTimerIntervalSeconds = await GetIntAsync("Automation.TimerIntervalSeconds", 60);
            WorkflowHistoryRetentionDays = await GetIntAsync("Automation.ExecutionHistoryRetentionDays", 90);

            ModChannelId = await _dataService.GetConfigValueAsync("Mod.ChannelId") ?? string.Empty;
            ModAutoCreateChannel = await GetBoolAsync("Mod.AutoCreateChannel", true);
            ModRoleName = await _dataService.GetConfigValueAsync("Mod.ModRoleName") ?? "Vyral Mod";

            // Security panel info — GetCurrentProfileAsync can return null (first run /
            // missing current_profile.txt); skip the pin check instead of throwing, a
            // throw here would leave _categorySettingsLoaded false and kill the
            // auto-save toggles for the whole session.
            var current = await _profileService.GetCurrentProfileAsync();
            CurrentProfileHasPin = current is not null && !string.IsNullOrEmpty(current.PinCode);

            // Stream FX has its own config store (StreamEffectsConfigService); load it
            // alongside the other config-backed categories (own try/catch, cannot break the rest).
            await LoadStreamFxSettingsAsync();

            // Vyral starter collection status (Advanced panel) — own try/catch,
            // cannot break the rest.
            await LoadVyralCollectionStatusAsync();

            _categorySettingsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading category settings");
        }
    }

    private async Task<bool> GetBoolAsync(string key, bool defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    // ----- Moderator -----

    [ObservableProperty]
    private string _modChannelId = string.Empty;

    [ObservableProperty]
    private bool _modAutoCreateChannel = true;

    [ObservableProperty]
    private string _modRoleName = "Vyral Mod";

    partial void OnModAutoCreateChannelChanged(bool value)
    {
        if (_categorySettingsLoaded)
        {
            _ = SaveSimpleBoolSettingAsync("Mod.AutoCreateChannel", value, "Mod");
        }
    }

    [RelayCommand]
    private async Task SaveModeratorSettingsAsync()
    {
        try
        {
            var roleId = ModChannelId?.Trim() ?? string.Empty;
            var roleName = string.IsNullOrWhiteSpace(ModRoleName) ? "Vyral Mod" : ModRoleName.Trim();

            await _dataService.SetConfigValueAsync("Mod.ChannelId", roleId, "Mod");
            await _dataService.SetConfigValueAsync("Mod.ModRoleName", roleName, "Mod");

            ModChannelId = roleId;
            ModRoleName = roleName;

            StatusMessage = "Moderator settings saved";
            _logger.LogInformation("Moderator settings saved (channel={ChannelId}, role={RoleName})", roleId, roleName);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving moderator settings");
            StatusMessage = $"Error saving moderator settings: {ex.Message}";
        }
    }

    private async Task<int> GetIntAsync(string key, int defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    // ----- Security -----

    [ObservableProperty]
    private bool _currentProfileHasPin;

    public string TokenStorageInfo =>
        "Platform tokens are stored in this profile's database (see Data Management for the exact location). " +
        "Twitch tokens are encrypted with Windows DPAPI for your user account; Kick tokens are managed by KickLib; " +
        "YouTube and Discord bot tokens are stored as-is in the profile database. " +
        "Anyone with access to this PC's user account can use them — protect the PC, not just the app.";

    // ======================================================================
    // Stream FX (lights providers, flash defaults, Stream Deck actions)
    // (config KV backed via StreamEffectsConfigService; explicit Save button)
    // ======================================================================

    private static readonly JsonSerializerOptions StreamFxJsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Sidebar ComboBox options for the lights provider (matches LightsManager routing).</summary>
    public IReadOnlyList<string> LightProviderOptions { get; } = new[]
    {
        StreamEffectsConfigService.DefaultProvider, "Hue", "Webhook"
    };

    [ObservableProperty]
    private string? _streamFxStatus;

    [ObservableProperty]
    private string _selectedLightProvider = StreamEffectsConfigService.DefaultProvider;

    [ObservableProperty]
    private string _hueBridgeIp = string.Empty;

    [ObservableProperty]
    private string _hueAppKey = string.Empty;

    [ObservableProperty]
    private string _webhookUrl = string.Empty;

    [ObservableProperty]
    private string _webhookToken = string.Empty;

    [ObservableProperty]
    private string _flashColor = StreamEffectsConfigService.DefaultFlashColor;

    [ObservableProperty]
    private int _flashTimes = StreamEffectsConfigService.DefaultFlashTimes;

    [ObservableProperty]
    private int _flashIntervalMs = StreamEffectsConfigService.DefaultFlashIntervalMs;

    [ObservableProperty]
    private string _newDeckActionName = string.Empty;

    [ObservableProperty]
    private string _newDeckActionUri = string.Empty;

    public ObservableCollection<HueLightRow> HueLights { get; } = new();

    public ObservableCollection<DeckActionRow> DeckActions { get; } = new();

    // Only overwrite the stored light-selection CSV once the rows actually came
    // from the bridge; before that a Save must preserve the stored selection.
    private bool _hueLightsLoaded;

    // Snapshot read at load time; Save preserves its LightIds while lights are
    // not loaded, and it supplies the stored CSV when re-ticking loaded rows.
    private StreamEffectsSnapshot? _streamFxSnapshot;

    [RelayCommand]
    private async Task LoadStreamFxSettingsAsync()
    {
        try
        {
            var snapshot = await _streamEffectsConfigService.GetSnapshotAsync();
            _streamFxSnapshot = snapshot;

            SelectedLightProvider = snapshot.Provider;
            HueBridgeIp = snapshot.BridgeIp ?? string.Empty;
            HueAppKey = snapshot.AppKey ?? string.Empty;
            WebhookUrl = snapshot.WebhookUrl;
            WebhookToken = snapshot.WebhookToken ?? string.Empty;
            FlashColor = snapshot.FlashColor;
            FlashTimes = snapshot.FlashTimes;
            FlashIntervalMs = snapshot.FlashIntervalMs;

            DeckActions.Clear();
            foreach (var action in ParseDeckActionsJson(snapshot.DeckActionsJson))
            {
                DeckActions.Add(new DeckActionRow { Id = action.Id, Name = action.Name, Uri = action.Uri });
            }

            // Re-tick loaded rows from the stored CSV (no-op until lights were loaded).
            foreach (var row in HueLights)
            {
                row.Enabled = snapshot.LightIds.Contains(row.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Stream FX settings");
            StreamFxStatus = $"Error loading Stream FX settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveStreamFxSettingsAsync()
    {
        try
        {
            var lightIds = _hueLightsLoaded
                ? (IReadOnlyList<string>)HueLights.Where(l => l.Enabled).Select(l => l.Id).ToList()
                : _streamFxSnapshot?.LightIds ?? [];

            var toSave = new StreamEffectsSnapshot(
                SelectedLightProvider,
                NullIfWhitespace(HueBridgeIp),
                NullIfWhitespace(HueAppKey),
                lightIds,
                WebhookUrl?.Trim() ?? string.Empty,
                NullIfWhitespace(WebhookToken),
                FlashTimes,
                FlashIntervalMs,
                string.IsNullOrWhiteSpace(FlashColor) ? StreamEffectsConfigService.DefaultFlashColor : FlashColor.Trim(),
                JsonSerializer.Serialize(
                    DeckActions.Select(d => new StreamDeckAction(d.Id, d.Name, d.Uri)).ToList(),
                    StreamFxJsonOpts));

            await _streamEffectsConfigService.SaveSnapshotCoreSettingsAsync(toSave);
            _streamFxSnapshot = toSave;

            StreamFxStatus = "Stream FX settings saved.";
            _logger.LogInformation(
                "Stream FX settings saved (provider={Provider}, lights={LightCount}, deckActions={DeckCount})",
                SelectedLightProvider, lightIds.Count, DeckActions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Stream FX settings");
            StreamFxStatus = $"Error saving Stream FX settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DiscoverHueBridgeAsync()
    {
        try
        {
            using var http = CreateStreamFxHttpClient();
            var json = await http.GetStringAsync("https://discovery.meethue.com");
            var bridges = JsonSerializer.Deserialize<List<HueBridgeDiscovery>>(json, StreamFxJsonOpts);
            var ip = bridges?.FirstOrDefault()?.InternalIpAddress;

            if (string.IsNullOrWhiteSpace(ip))
            {
                StreamFxStatus = "No Hue bridge responded — enter the IP manually.";
                return;
            }

            HueBridgeIp = ip;
            StreamFxStatus = $"Bridge found at {ip}.";
            _logger.LogInformation("Hue bridge discovered at {BridgeIp}", ip);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hue bridge discovery failed");
            StreamFxStatus = "Could not discover a Hue bridge (check network) — enter the IP manually.";
        }
    }

    [RelayCommand]
    private async Task PairHueBridgeAsync()
    {
        var ip = HueBridgeIp?.Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            StreamFxStatus = "Enter the bridge IP (or press Discover) before pairing.";
            return;
        }

        try
        {
            using var http = CreateStreamFxHttpClient();
            var body = JsonSerializer.Serialize(new { devicetype = "vyralbot#streamfx", generateclientkey = true });
            var response = await http.PostAsync(
                $"http://{ip}/api",
                new StringContent(body, Encoding.UTF8, "application/json"));
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Response is [{"success":{"username":"...","clientkey":"..."}}] or
            // [{"error":{"type":101,...}}] (101 = link button not pressed).
            var first = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().FirstOrDefault()
                : default;

            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("success", out var success))
            {
                var username = success.TryGetProperty("username", out var usernameProp) ? usernameProp.GetString() : null;
                if (!string.IsNullOrWhiteSpace(username))
                {
                    HueAppKey = username;
                    StreamFxStatus = "Bridge paired.";
                    _logger.LogInformation("Hue bridge paired (ip={BridgeIp})", ip);
                    return;
                }
            }

            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("error", out var error))
            {
                var type = error.TryGetProperty("type", out var typeProp) && typeProp.TryGetInt32(out var parsed)
                    ? parsed
                    : 0;
                StreamFxStatus = type == 101
                    ? "Press the link button on your Hue bridge, then Pair again."
                    : $"Bridge rejected pairing (error {type}).";
                return;
            }

            StreamFxStatus = "Unexpected bridge response — try pairing again.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hue bridge pairing failed (ip={BridgeIp})", ip);
            StreamFxStatus = $"Could not pair with the bridge: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadHueLightsAsync()
    {
        var ip = HueBridgeIp?.Trim();
        var appKey = HueAppKey?.Trim();
        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(appKey))
        {
            StreamFxStatus = "Enter the bridge IP and app key (Discover + Pair) before loading lights.";
            return;
        }

        try
        {
            // Enumerate against a throwaway client built from the CURRENT UI
            // values — Save stays the only writer of the provider/bridge
            // config, so a Webhook-configured user browsing lights here is not
            // silently flipped to Hue.
            var enabledIds = (IReadOnlyList<string>)HueLights
                .Where(l => l.Enabled)
                .Select(l => l.Id)
                .ToList();
            var enumerationClient = new HueLightsClient(
                () => Task.FromResult<(string?, string?, IReadOnlyList<string>)>((ip, appKey, enabledIds)));
            var lights = await enumerationClient.GetLightsAsync();

            var storedIds = _streamFxSnapshot?.LightIds ?? [];
            HueLights.Clear();
            foreach (var light in lights)
            {
                HueLights.Add(new HueLightRow
                {
                    Id = light.Id,
                    Name = light.Name,
                    Reachable = light.Reachable,
                    Enabled = storedIds.Contains(light.Id)
                });
            }
            _hueLightsLoaded = true;

            StreamFxStatus = $"Loaded {HueLights.Count} light(s) — tick the flash targets, then press Save.";
            _logger.LogInformation("Loaded {Count} Hue lights", HueLights.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Hue lights");
            StreamFxStatus = $"Could not load lights: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddDeckActionAsync()
    {
        try
        {
            var result = await _streamDeckService.SaveActionAsync(NewDeckActionName, NewDeckActionUri);
            StreamFxStatus = result.Message;
            if (!result.Success)
            {
                return;
            }

            NewDeckActionName = string.Empty;
            NewDeckActionUri = string.Empty;
            await RefreshDeckRowsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Stream Deck action");
            StreamFxStatus = $"Error saving Stream Deck action: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveDeckActionAsync(DeckActionRow? row)
    {
        if (row is null) return;

        try
        {
            await _streamDeckService.DeleteActionAsync(row.Id);
            DeckActions.Remove(row);
            StreamFxStatus = $"Removed Stream Deck action '{row.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing Stream Deck action '{Name}'", row.Name);
            StreamFxStatus = $"Error removing action: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestDeckActionAsync(DeckActionRow? row)
    {
        if (row is null) return;

        try
        {
            StreamFxStatus = await _streamDeckService.TriggerAsync(row.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering Stream Deck action '{Name}'", row.Name);
            StreamFxStatus = $"Error triggering action: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestFlashAsync()
    {
        try
        {
            StreamFxStatus = await _lightsClient.FlashAsync(FlashColor?.Trim(), FlashTimes, FlashIntervalMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running test flash");
            StreamFxStatus = $"Test flash failed: {ex.Message}";
        }
    }

    /// <summary>Re-reads saved actions so row Ids match the store (SaveActionAsync assigns new Guid ids).</summary>
    private async Task RefreshDeckRowsAsync()
    {
        var actions = await _streamDeckService.GetActionsAsync();
        DeckActions.Clear();
        foreach (var action in actions)
        {
            DeckActions.Add(new DeckActionRow { Id = action.Id, Name = action.Name, Uri = action.Uri });
        }
    }

    private static List<StreamDeckAction> ParseDeckActionsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<StreamDeckAction>>(json, StreamFxJsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HttpClient CreateStreamFxHttpClient() => new() { Timeout = TimeSpan.FromSeconds(5) };

    private static string? NullIfWhitespace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Discovery response entry: [{"id":"...","internalipaddress":"x.x.x.x"}].</summary>
    private sealed record HueBridgeDiscovery(string? Id, string? InternalIpAddress);

    // ----- Advanced -----

    public string AppVersion =>
        $"VyralBot {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · .NET {Environment.Version}";

    // ===== Vyral scene collection (GitHub) =====

    [ObservableProperty]
    private string? _vyralCollectionStatus;

    /// <summary>
    /// Advanced-panel status of the Vyral collection for the user's OBS install.
    /// Always produces a line (problems included) — the card itself is never hidden.
    /// </summary>
    private async Task LoadVyralCollectionStatusAsync()
    {
        try
        {
            VyralCollectionStatus = await BuildVyralCollectionStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading the Vyral collection status failed");
            VyralCollectionStatus = "Status unavailable — could not inspect the OBS install.";
        }
    }

    private async Task<string> BuildVyralCollectionStatusAsync()
    {
        string local = await BuildLocalVyralCollectionStatusAsync();
        return local + await DescribeGitHubAvailabilityAsync();
    }

    private async Task<string> BuildLocalVyralCollectionStatusAsync()
    {
        if (_obsInstallLocator.Locate() is not { } install)
        {
            return "OBS Studio is not installed — the starter collection imports once OBS is installed.";
        }

        if (_bundledCollectionService.GetBundledPath() is null)
        {
            return "No bundled collection in this build — updates still work from GitHub.";
        }

        BundledImportState state = await _bundledCollectionService.DecideAsync(install.SceneCollectionsDir);
        string? storedHash = await _bundledCollectionService.GetStoredHashAsync();
        string targetPath = Path.Combine(
            install.SceneCollectionsDir, ObsBundledCollectionService.BundledCollectionFileName);

        if (string.IsNullOrEmpty(storedHash))
        {
            return File.Exists(targetPath)
                ? "Not imported yet — a Vyral.json already exists in your OBS (Re-import overwrites it deliberately)."
                : "Not imported yet";
        }

        if (state.ImportedFileMissingFromObs)
        {
            return "Deleted from OBS — can be re-imported";
        }

        return state.ShouldImport
            ? $"Imported (version {storedHash[..8]}) — a new bundled version is ready to import"
            : $"Imported (version {storedHash[..8]})";
    }

    /// <summary>
    /// Compares the upstream GitHub file's hash against the applied hash. Runs
    /// only on category load / button click — never on a timer.
    /// </summary>
    private async Task<string> DescribeGitHubAvailabilityAsync()
    {
        if (_obsInstallLocator.Locate() is null)
        {
            return string.Empty; // the local text already explains the situation
        }

        try
        {
            string? storedHash = await _bundledCollectionService.GetStoredHashAsync();
            if (string.IsNullOrEmpty(storedHash))
            {
                return string.Empty; // nothing applied yet — the local text covers this state
            }

            string upstream = await _collectionUpdateService.FetchUpstreamAsync();
            return _bundledCollectionService.ComputeContentHash(upstream) == storedHash
                ? " Up to date with GitHub."
                : " A NEW version is available on GitHub — press \"Update from GitHub\" (your settings are preserved).";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GitHub availability check for the Vyral collection failed");
            return " (GitHub unreachable — update check skipped)";
        }
    }

    /// <summary>Warning appended after a merge while obs64.exe is running: OBS caches
    /// collections in memory and rewrites the file with its in-memory state on exit.</summary>
    private const string ObsRunningWarning =
        " OBS is running — restart OBS (or switch scene collections in OBS) to load the updated collection.";

    [RelayCommand]
    private async Task UpdateVyralCollectionAsync()
    {
        try
        {
            VyralCollectionStatus = "Checking GitHub for collection updates…";
            ObsUpdateResult result = await _collectionUpdateService.UpdateFromGitHubAsync();
            VyralCollectionStatus = result.Success && ObsCollectionUpdateService.IsObsRunning()
                ? result.Message + ObsRunningWarning
                : result.Message;

            if (!result.Success)
            {
                return;
            }

            _logger.LogInformation(
                "Settings: updated the Vyral collection from GitHub (+{Scenes} scenes, +{Sources} sources, +{Items} scene items)",
                result.AddedScenes, result.AddedSources, result.AddedSceneItems);

            // Reflect the merged file in the embedded studio immediately when its engine is up.
            if (_obsEngine.IsReady
                && _collectionUpdateService.TryGetActiveCollectionPath() is { } activePath
                && File.Exists(activePath))
            {
                string json = await File.ReadAllTextAsync(activePath, Encoding.UTF8);
                var loadResult = await _obsStudioService.LoadCollectionAsync(json);
                _logger.LogInformation(
                    "Settings: loaded the updated collection into the embedded studio ({Message})", loadResult.Message);
            }

            StatusMessage = "Vyral scene collection updated from GitHub";
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Updating the Vyral collection from GitHub failed");
            VyralCollectionStatus = $"Update failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReimportVyralCollectionAsync()
    {
        try
        {
            if (_obsInstallLocator.Locate() is not { } install)
            {
                StatusMessage = "OBS Studio is not installed — install it first.";
                return;
            }

            // 1) Deliberate restore of OUR Vyral.json — still strictly additive
            //    (writes/overwrites ONLY Vyral.json; every other collection untouched).
            await _bundledCollectionService.ImportToObsAsync(install.SceneCollectionsDir);
            _logger.LogInformation("Settings: re-imported the Vyral starter collection into OBS");

            // 2) Bring the (possibly restored) ACTIVE collection up to GitHub:
            //    a file deleted from OBS comes back as a fresh install, an
            //    existing one gets the same non-destructive merge as Update.
            ObsUpdateResult update = await _collectionUpdateService.UpdateFromGitHubAsync();
            if (!update.Success)
            {
                _logger.LogInformation(
                    "Settings: GitHub refresh after re-import skipped ({Message})", update.Message);
            }

            // Reflect it in the embedded studio immediately when its engine is up.
            if (_obsEngine.IsReady
                && _collectionUpdateService.TryGetActiveCollectionPath() is { } activePath
                && File.Exists(activePath))
            {
                string json = await File.ReadAllTextAsync(activePath, Encoding.UTF8);
                var result = await _obsStudioService.LoadCollectionAsync(json);
                _logger.LogInformation(
                    "Settings: loaded the Vyral collection into the embedded studio ({Message})", result.Message);
            }

            await LoadVyralCollectionStatusAsync();
            StatusMessage = "Vyral scene collection imported into OBS";
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-importing the Vyral starter collection failed");
            StatusMessage = $"Re-import failed: {ex.Message}";
        }
    }

    private static string WebView2CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VyralBot", "WebView2Data");

    private static string LocalAppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vyral");

    [RelayCommand]
    private async Task OpenAppdataFolder()
    {
        // Same discovery as UiPreferencesService: walk up from the exe until an
        // "appdata" folder appears (portable layout).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !string.Equals(dir.Name, "appdata", StringComparison.OrdinalIgnoreCase))
        {
            dir = dir.Parent!;
        }

        if (dir != null && dir.Exists)
        {
            await OpenFolderInExplorerAsync(dir.FullName);
        }
        else
        {
            StatusMessage = "App data folder not found";
        }
    }

    [RelayCommand]
    private async Task OpenLogsFolder() => await OpenFolderInExplorerAsync(Path.Combine(AppContext.BaseDirectory, "logs"));

    [RelayCommand]
    private async Task OpenWebView2Folder() => await OpenFolderInExplorerAsync(WebView2CachePath);

    [RelayCommand]
    private async Task OpenLocalAppDataFolder() => await OpenFolderInExplorerAsync(LocalAppDataRoot);

    private async Task OpenFolderInExplorerAsync(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                await _processManager.StartProcessAsync(
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
        }
        catch
        {
            // Best effort
        }
    }

    [RelayCommand]
    private async Task ClearWebView2CacheAsync()
    {
        await ClearDirectoryWithConfirmAsync(
            WebView2CachePath,
            "Delete the WebView2 browser cache? OAuth sign-in windows will rebuild it as needed (custom signed-in sessions may need re-auth).");
    }

    [RelayCommand]
    private async Task ClearFlowDraftsAsync()
    {
        await ClearDirectoryWithConfirmAsync(
            Path.Combine(LocalAppDataRoot, "FlowDrafts"),
            "Delete all unsaved flow editor drafts? Unsaved automation changes will be lost; saved workflows are not affected.");
    }

    [RelayCommand]
    private async Task ClearBlockTemplatesAsync()
    {
        await ClearDirectoryWithConfirmAsync(
            Path.Combine(LocalAppDataRoot, "BlockTemplates"),
            "Delete all saved flow editor block templates?");
    }

    private async Task ClearDirectoryWithConfirmAsync(string path, string confirmMessage)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                StatusMessage = "Nothing to clear — folder does not exist";
                return;
            }

            var dialog = new ConfirmDialog
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            dialog.SetMessage(confirmMessage);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            Directory.Delete(path, recursive: true);
            StatusMessage = $"Cleared {Path.GetFileName(path)}";
            _logger.LogInformation("Cleared cache directory {Path}", path);
            await ClearStatusAfterDelayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing {Path}", path);
            StatusMessage = $"Error clearing folder: {ex.Message}";
        }
    }

    // ===== Restart =====

    [RelayCommand]
    private void RestartApplication()
    {
        if (App.PendingRestart)
        {
            return; // restart already in flight — ignore extra clicks
        }

        _logger.LogInformation("User requested an application restart");
        StatusMessage = "Shutting down and restarting Vyral…";
        App.PendingRestart = true;

        // Graceful shutdown FIRST (bots disconnect, host stops, mutex releases).
        // App.OnExit spawns the new instance only AFTER cleanup finishes.
        System.Windows.Application.Current.Shutdown();
    }

    // ===== Theme commands =====

    [RelayCommand]
    private void SelectTheme(ThemeOption? theme)
    {
        if (theme is null) return;

        _logger.LogInformation("User selected theme: {ThemeId}", theme.Id);
        SelectedTheme = theme;
        ThemeManager.Instance.ApplyTheme(theme.Id);
        StatusMessage = $"{theme.DisplayName} theme applied";
        _ = ClearStatusAfterDelayAsync();
    }

    [RelayCommand]
    private void ApplyCustomTheme()
    {
        if (!ColorShadeGenerator.TryParseHex(CustomPrimaryHex, out _))
        {
            StatusMessage = "Invalid hex color - use #RRGGBB";
            _ = ClearStatusAfterDelayAsync();
            return;
        }

        _logger.LogInformation("Applying custom theme: base={Base}, accent={Accent}, logo={Logo}",
            CustomIsLight ? "Light" : "Dark", CustomPrimaryHex, CustomLogoVariant);
        ThemeManager.Instance.ApplyCustomTheme(
            CustomIsLight ? "Light" : "Dark", CustomPrimaryHex, CustomLogoVariant);
        SelectedTheme = ThemeCatalog.CustomOption;
        StatusMessage = "Custom theme applied";
        _ = ClearStatusAfterDelayAsync();
    }

    [RelayCommand]
    private void SelectCustomAccent(string hex)
    {
        CustomPrimaryHex = hex;
        ApplyCustomTheme();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(2000);
        StatusMessage = null;
    }

    /// <summary>
    /// Stops the Viewer Hub pending-codes poller. The category-leave path in
    /// <see cref="OnSelectedCategoryChanged"/> already handles switching away
    /// from the ViewerHub section, but nothing fired when the user left the
    /// Settings PAGE entirely (SelectedCategory stays "ViewerHub") — and
    /// ViewerHubViewModel is a singleton that outlives this scope, so its 30s
    /// DispatcherTimer would keep polling SQLite for the rest of the session.
    /// The DI container calls this when the Settings scope is disposed
    /// (MainViewModel dispose-block / re-navigation).
    /// </summary>
    public void Dispose()
    {
        _viewerHubViewModel.Cleanup();
    }
    /// <summary>
    /// Themed PIN prompt for deleting a PIN-protected profile (mirrors
    /// ProfileSelectorViewModel.PromptForDeletePin). Returns the entered PIN
    /// or null when cancelled.
    /// </summary>
    private static string? PromptForDeletePin(string displayName)
    {
        var title = new System.Windows.Controls.TextBlock
        {
            Text = "PIN Required",
            FontSize = 16,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        };
        title.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var message = new System.Windows.Controls.TextBlock
        {
            Text = $"'{displayName}' is PIN-protected. Enter its PIN to delete the profile.",
            FontSize = 12,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new System.Windows.Thickness(0, 0, 0, 4)
        };
        message.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var passwordBox = new System.Windows.Controls.PasswordBox
        {
            MaxLength = 6,
            FontSize = 14,
            Padding = new System.Windows.Thickness(8),
            Margin = new System.Windows.Thickness(0, 8, 0, 0)
        };
        passwordBox.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CardBackgroundBrush");
        passwordBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
        passwordBox.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderLightBrush");

        var deleteButton = new System.Windows.Controls.Button
        {
            Content = "Delete",
            Width = 100,
            IsDefault = true,
            Padding = new System.Windows.Thickness(0, 8, 0, 8)
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 100,
            IsCancel = true,
            Padding = new System.Windows.Thickness(0, 8, 0, 8),
            Margin = new System.Windows.Thickness(0, 0, 8, 0)
        };
        foreach (var button in new[] { deleteButton, cancelButton })
        {
            button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CardBackgroundBrush");
            button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
            button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderLightBrush");
        }

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new System.Windows.Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);

        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
        panel.Children.Add(title);
        panel.Children.Add(message);
        panel.Children.Add(passwordBox);
        panel.Children.Add(buttons);

        var window = new System.Windows.Window
        {
            Title = "PIN Required",
            Width = 360,
            SizeToContent = System.Windows.SizeToContent.Height,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current?.MainWindow,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            Content = panel
        };
        window.SetResourceReference(System.Windows.Window.BackgroundProperty, "BackgroundPrimaryBrush");

        return window.ShowDialog() == true ? passwordBox.Password : null;
    }

}
