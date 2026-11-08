using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Input;
using VyralBot.Core.Models;
using VyralBot.Core.Services.Profiles;

namespace VyralBot.UI.ViewModels;

public class ProfileSelectorViewModel : INotifyPropertyChanged
{
    private readonly IProfileService _profileService;
    private readonly ILogger<ProfileSelectorViewModel> _logger;
    private Profile? _selectedProfile;
    private string _enteredPin = string.Empty;
    private string _newProfileName = string.Empty;
    private string _newProfileDisplayName = string.Empty;
    private string _newProfilePin = string.Empty;
    private bool _showCreateDialog;
    private bool _showPinEntry;
    private string _errorMessage = string.Empty;

    public ProfileSelectorViewModel(IProfileService profileService, ILogger<ProfileSelectorViewModel> logger)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        Profiles = new ObservableCollection<Profile>();
        SelectProfileCommand = new RelayCommand(SelectProfile, CanSelectProfile);
        CreateProfileCommand = new RelayCommand(ShowCreateProfileDialog);
        ConfirmCreateCommand = new RelayCommand(async () => await CreateProfileAsync(), CanCreateProfile);
        CancelCreateCommand = new RelayCommand(CancelCreate);
        DeleteProfileCommand = new RelayCommand<Profile>(async (p) => await DeleteProfileAsync(p), CanDeleteProfile);
    }

    public ObservableCollection<Profile> Profiles { get; }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile != value)
            {
                _selectedProfile = value;
                OnPropertyChanged();
                SelectProfileRelayCommand.NotifyCanExecuteChanged();
                
                // Show PIN entry if profile has PIN
                ShowPinEntry = value?.PinCode != null;
                EnteredPin = string.Empty;
                ErrorMessage = string.Empty;
            }
        }
    }

    public string EnteredPin
    {
        get => _enteredPin;
        set
        {
            if (_enteredPin != value)
            {
                _enteredPin = value;
                OnPropertyChanged();
                SelectProfileRelayCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (_newProfileName != value)
            {
                _newProfileName = value;
                OnPropertyChanged();
                ConfirmCreateRelayCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string NewProfileDisplayName
    {
        get => _newProfileDisplayName;
        set
        {
            if (_newProfileDisplayName != value)
            {
                _newProfileDisplayName = value;
                OnPropertyChanged();
            }
        }
    }

    public string NewProfilePin
    {
        get => _newProfilePin;
        set
        {
            if (_newProfilePin != value)
            {
                _newProfilePin = value;
                OnPropertyChanged();
                ConfirmCreateRelayCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ShowCreateDialog
    {
        get => _showCreateDialog;
        set
        {
            if (_showCreateDialog != value)
            {
                _showCreateDialog = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowPinEntry
    {
        get => _showPinEntry;
        set
        {
            if (_showPinEntry != value)
            {
                _showPinEntry = value;
                OnPropertyChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand SelectProfileCommand { get; }
    public ICommand CreateProfileCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    
    private RelayCommand SelectProfileRelayCommand => (RelayCommand)SelectProfileCommand;
    private RelayCommand ConfirmCreateRelayCommand => (RelayCommand)ConfirmCreateCommand;

    public async Task LoadProfilesAsync()
    {
        try
        {
            var profiles = await _profileService.GetProfilesAsync();
            Profiles.Clear();
            
            foreach (var profile in profiles.OrderByDescending(p => p.LastUsedAt))
            {
                Profiles.Add(profile);
            }

            // Auto-select most recently used profile
            SelectedProfile = Profiles.FirstOrDefault();
            
            _logger.LogInformation("Loaded {Count} profiles", Profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profiles");
            ErrorMessage = "Failed to load profiles. Please restart the application.";
        }
    }

    private bool CanSelectProfile()
    {
        _logger.LogInformation("CanSelectProfile called - SelectedProfile: {Profile}, HasPin: {HasPin}", 
            SelectedProfile?.Name ?? "null", 
            !string.IsNullOrEmpty(SelectedProfile?.PinCode));
            
        if (SelectedProfile == null)
        {
            _logger.LogInformation("CanSelectProfile returning false - no profile selected");
            return false;
        }
        
        // If profile has PIN, require PIN entry
        if (!string.IsNullOrEmpty(SelectedProfile.PinCode))
        {
            var canExecute = !string.IsNullOrWhiteSpace(EnteredPin);
            _logger.LogInformation("CanSelectProfile returning {Result} - PIN required, PIN entered: {HasPin}", 
                canExecute, !string.IsNullOrWhiteSpace(EnteredPin));
            return canExecute;
        }
        
        _logger.LogInformation("CanSelectProfile returning true - no PIN required");
        return true;
    }

    private async void SelectProfile()
    {
        _logger.LogInformation("===== SelectProfile STARTED =====");
        _logger.LogInformation("Selected profile: {Profile}", SelectedProfile?.Name ?? "null");
        
        if (SelectedProfile == null)
        {
            _logger.LogWarning("SelectProfile aborted - SelectedProfile is null");
            return;
        }

        try
        {
            _logger.LogInformation("Checking PIN requirement for profile {Profile}", SelectedProfile.Name);
            
            // Validate PIN if profile has one
            if (!string.IsNullOrEmpty(SelectedProfile.PinCode))
            {
                _logger.LogInformation("Profile has PIN, validating...");
                var isValid = await _profileService.ValidatePinAsync(SelectedProfile.Name, EnteredPin);
                
                if (!isValid)
                {
                    _logger.LogWarning("Invalid PIN entered for profile {Profile}", SelectedProfile.Name);
                    ErrorMessage = "Incorrect PIN. Please try again.";
                    EnteredPin = string.Empty;
                    return;
                }
                
                _logger.LogInformation("PIN validated successfully");
            }
            else
            {
                _logger.LogInformation("No PIN required for profile {Profile}", SelectedProfile.Name);
            }

            // Switch to selected profile
            _logger.LogInformation("Calling SwitchProfileAsync for profile {Profile}", SelectedProfile.Name);
            await _profileService.SwitchProfileAsync(SelectedProfile.Name, EnteredPin);
            
            _logger.LogInformation("SwitchProfileAsync completed successfully");
            _logger.LogInformation("Switched to profile: {Profile}", SelectedProfile.Name);
            
            // Signal to close the selector window (handled by the window itself)
            _logger.LogInformation("Invoking ProfileSelected event");
            ProfileSelected?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("ProfileSelected event invoked");
            _logger.LogInformation("===== SelectProfile COMPLETED =====");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXCEPTION in SelectProfile for profile {Profile}", SelectedProfile.Name);
            ErrorMessage = $"Failed to select profile: {ex.Message}";
        }
    }

    private void ShowCreateProfileDialog()
    {
        NewProfileName = string.Empty;
        NewProfileDisplayName = string.Empty;
        NewProfilePin = string.Empty;
        ErrorMessage = string.Empty;
        ShowCreateDialog = true;
    }

    private bool CanCreateProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return false;
        
        // Validate PIN format if provided
        if (!string.IsNullOrWhiteSpace(NewProfilePin))
        {
            return NewProfilePin.Length >= 4 && NewProfilePin.Length <= 6 && NewProfilePin.All(char.IsDigit);
        }
        
        return true;
    }

    private async Task CreateProfileAsync()
    {
        try
        {
            var displayName = string.IsNullOrWhiteSpace(NewProfileDisplayName) ? NewProfileName : NewProfileDisplayName;
            var pin = string.IsNullOrWhiteSpace(NewProfilePin) ? null : NewProfilePin;

            var profile = await _profileService.CreateProfileAsync(NewProfileName, displayName, pin);
            
            Profiles.Add(profile);
            SelectedProfile = profile;
            
            ShowCreateDialog = false;
            ErrorMessage = string.Empty;
            
            _logger.LogInformation("Created new profile: {Profile}", profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile");
            ErrorMessage = $"Failed to create profile: {ex.Message}";
        }
    }

    private void CancelCreate()
    {
        ShowCreateDialog = false;
        ErrorMessage = string.Empty;
    }

    private bool CanDeleteProfile(Profile? profile)
    {
        // Cannot delete if it's the only profile
        return profile != null && Profiles.Count > 1;
    }

    private async Task DeleteProfileAsync(Profile? profile)
    {
        if (profile == null) return;

        var result = MessageBox.Show(
            $"This will permanently delete all data for profile '{profile.DisplayName}'.\n\nAre you sure?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        // PIN-protected profiles must be unlocked before deletion — same masked
        // entry + hash validation as the profile unlock flow.
        if (!string.IsNullOrEmpty(profile.PinCode))
        {
            var pin = PromptForDeletePin(profile.DisplayName);
            if (pin == null)
            {
                _logger.LogInformation("Profile deletion cancelled at PIN prompt for profile {Profile}", profile.Name);
                return;
            }

            var pinValid = await _profileService.ValidatePinAsync(profile.Name, pin);
            if (!pinValid)
            {
                _logger.LogWarning("Incorrect PIN entered when deleting profile {Profile}", profile.Name);
                ErrorMessage = "Incorrect PIN. Please try again.";
                return;
            }

            _logger.LogInformation("PIN validated for deletion of profile {Profile}", profile.Name);
        }

        try
        {
            await _profileService.DeleteProfileAsync(profile.Name);
            Profiles.Remove(profile);

            // Select another profile
            SelectedProfile = Profiles.FirstOrDefault();

            _logger.LogInformation("Deleted profile: {Profile}", profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete profile");
            ErrorMessage = $"Failed to delete profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Masked PIN prompt required before deleting a PIN-protected profile
    /// (mirrors the unlock flow's PasswordBox entry; the returned PIN is
    /// validated against the stored hash via
    /// <see cref="IProfileService.ValidatePinAsync"/> and is never logged).
    /// Returns the entered PIN, or null when the user cancelled.
    /// </summary>
    private static string? PromptForDeletePin(string displayName)
    {
        var title = new TextBlock
        {
            Text = "PIN Required",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var message = new TextBlock
        {
            Text = $"'{displayName}' is PIN-protected. Enter its PIN to delete the profile.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var passwordBox = new PasswordBox
        {
            MaxLength = 6,
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0)
        };
        passwordBox.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
        passwordBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        passwordBox.SetResourceReference(Control.BorderBrushProperty, "BorderLightBrush");

        var deleteButton = new Button
        {
            Content = "Delete",
            Width = 100,
            IsDefault = true,
            Padding = new Thickness(0, 8, 0, 8)
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            IsCancel = true,
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        foreach (var button in new[] { deleteButton, cancelButton })
        {
            button.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
            button.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "BorderLightBrush");
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(title);
        panel.Children.Add(message);
        panel.Children.Add(passwordBox);
        panel.Children.Add(buttons);

        var prompt = new Window
        {
            Title = "PIN Required",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = panel
        };
        prompt.SetResourceReference(Window.BackgroundProperty, "BackgroundDarkBrush");
        prompt.Loaded += (_, _) => passwordBox.Focus();

        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        if (owner != null && owner.IsLoaded)
        {
            prompt.Owner = owner;
        }

        string? pin = null;
        deleteButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(passwordBox.Password))
            {
                return;
            }

            pin = passwordBox.Password;
            prompt.DialogResult = true;
        };
        cancelButton.Click += (_, _) => prompt.DialogResult = false;

        return prompt.ShowDialog() == true ? pin : null;
    }

    public event EventHandler? ProfileSelected;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
