using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Profiles;
using VyralBot.Core.Models;
using VyralBot.Data;
using VyralBot.UI.Views;
using System.Collections.ObjectModel;
using System.IO;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for application settings including profile management.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IProfileService _profileService;
    private readonly IDataService _dataService;
    private readonly WebServerSettingsViewModel _webServerSettingsViewModel;
    private readonly VariablesViewModel _variablesViewModel;

    public WebServerSettingsViewModel WebServerSettingsViewModel => _webServerSettingsViewModel;
    public VariablesViewModel VariablesViewModel => _variablesViewModel;

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

    [ObservableProperty]
    private object? _selectedPlatformViewModel;

    [ObservableProperty]
    private bool _showPlatformGrid = true;

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
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _webServerSettingsViewModel = webServerSettingsViewModel ?? throw new ArgumentNullException(nameof(webServerSettingsViewModel));
        _variablesViewModel = variablesViewModel ?? throw new ArgumentNullException(nameof(variablesViewModel));
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
            
            // Note: WebServerSettingsViewModel is already initialized via DI
            // No need to reinitialize since we dispose/recreate SettingsViewModel on each navigation
            
            _logger.LogInformation("Settings view initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing settings");
            StatusMessage = $"Error creating profile: {ex.Message}";
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
            SwitchProfilePin = string.Empty;
            
            // Reload settings with new profile
            await LoadProfilesAsync();
            await LoadSettingsAsync();
            
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
            CurrentProfile = Profiles.FirstOrDefault(p => p.Name == current.Name);
            
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
}
