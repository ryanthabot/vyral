using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class ProfileSelectorWindow : Window
{
    private readonly ILogger<ProfileSelectorWindow>? _logger;
    
    public ProfileSelectorWindow()
    {
        InitializeComponent();
        
        _logger = App.ServiceProvider?.GetService<ILogger<ProfileSelectorWindow>>();
        _logger?.LogInformation("ProfileSelectorWindow constructor started");
        
        var viewModel = App.ServiceProvider?.GetRequiredService<ProfileSelectorViewModel>();
        if (viewModel != null)
        {
            _logger?.LogInformation("ProfileSelectorViewModel resolved, setting DataContext");
            DataContext = viewModel;
            viewModel.ProfileSelected += OnProfileSelected;
            _logger?.LogInformation("ProfileSelected event handler attached");
            Loaded += async (s, e) =>
            {
                _logger?.LogInformation("Window Loaded event fired, loading profiles");
                await viewModel.LoadProfilesAsync();
                _logger?.LogInformation("Profiles loaded");
            };
        }
        else
        {
            _logger?.LogError("Failed to resolve ProfileSelectorViewModel");
        }
        
        _logger?.LogInformation("ProfileSelectorWindow constructor completed");
    }

    private void OnProfileSelected(object? sender, EventArgs e)
    {
        _logger?.LogInformation("===== OnProfileSelected EVENT FIRED =====");
        _logger?.LogInformation("Setting DialogResult = true");
        DialogResult = true;
        _logger?.LogInformation("Calling Close()");
        Close();
        _logger?.LogInformation("Close() returned");
        _logger?.LogInformation("===== OnProfileSelected COMPLETED =====");
    }

    private void PinPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileSelectorViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.EnteredPin = passwordBox.Password;
        }
    }
}
