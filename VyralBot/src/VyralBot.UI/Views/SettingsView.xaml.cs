using System.Windows.Controls;
using System.Windows;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public SettingsView(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
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
