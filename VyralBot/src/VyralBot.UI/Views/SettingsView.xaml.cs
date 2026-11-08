using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SettingsView(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            // Load/Pair may have set the secrets before the view finished
            // loading — push them into the masked boxes now.
            SyncSecretBoxes(viewModel);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The Stream FX secrets are masked in PasswordBoxes (not bindable);
        // mirror view-model changes (Load settings, successful Hue pairing)
        // into the boxes. Setting .Password re-fires PasswordChanged with the
        // same value, so the echo back into the view model is a no-op.
        if (sender is not SettingsViewModel viewModel)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.HueAppKey):
                if (HueAppKeyBox is not null
                    && !string.Equals(HueAppKeyBox.Password, viewModel.HueAppKey, StringComparison.Ordinal))
                {
                    HueAppKeyBox.Password = viewModel.HueAppKey;
                }
                break;
            case nameof(SettingsViewModel.WebhookToken):
                if (WebhookTokenBox is not null
                    && !string.Equals(WebhookTokenBox.Password, viewModel.WebhookToken, StringComparison.Ordinal))
                {
                    WebhookTokenBox.Password = viewModel.WebhookToken;
                }
                break;
        }
    }

    private void SyncSecretBoxes(SettingsViewModel viewModel)
    {
        if (HueAppKeyBox is not null) HueAppKeyBox.Password = viewModel.HueAppKey ?? string.Empty;
        if (WebhookTokenBox is not null) WebhookTokenBox.Password = viewModel.WebhookToken ?? string.Empty;
    }

    private void HueAppKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The Hue app key is masked like the other platform secrets — feed it
        // to the view model manually because PasswordBox.Password is not bindable.
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.HueAppKey = passwordBox.Password;
        }
    }

    private void WebhookTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Webhook bearer token — same masked-secret pattern as the Hue app key.
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.WebhookToken = passwordBox.Password;
        }
    }

    private void CurrentPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.CurrentPin = passwordBox.Password;
        }
    }

    private void NewPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.NewPin = passwordBox.Password;
        }
    }

    private void ConfirmNewPinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.ConfirmNewPin = passwordBox.Password;
        }
    }

    private void NewProfilePinBoxSettings_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.NewProfilePin = passwordBox.Password;
        }
    }

    private void SwitchProfilePinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.SwitchProfilePin = passwordBox.Password;
        }
    }
}
