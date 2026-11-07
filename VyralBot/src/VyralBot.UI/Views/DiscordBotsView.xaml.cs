using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Discord Bots page: the multi-bot home. Lists configured bots with their
/// live status, quick-adds new bots, and hosts the setup-bot dashboard
/// (DiscordDashboardView) behind the DASHBOARD header toggle. Initialization
/// is driven by MainViewModel navigation (InitializeAsync); the dashboard view
/// model is resolved from DI here because it has no navigation path of its own.
/// </summary>
public partial class DiscordBotsView : UserControl
{
    private bool _dashboardShown;
    private bool _dashboardCreated;

    public DiscordBotsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscordBotsViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscordBotsViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The bot token is masked in a PasswordBox (not bindable); clear the box
        // when the view model resets the token after a successful add.
        if (e.PropertyName == nameof(DiscordBotsViewModel.NewBotToken) &&
            sender is DiscordBotsViewModel { NewBotToken: "" })
        {
            NewBotTokenBox?.Clear();
        }
    }

    private void NewBotTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The bot token is masked like the other platform secrets — feed it to the
        // view model manually because PasswordBox.Password is not bindable.
        if (DataContext is DiscordBotsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.NewBotToken = passwordBox.Password;
        }
    }

    /// <summary>
    /// Toggles between the bot list and the setup-bot dashboard tab. The
    /// DiscordDashboardView is created lazily on first open; adding it to the
    /// visible tree fires its Loaded handler, which calls the dashboard view
    /// model's InitializeAsync (connects the bot, loads servers).
    /// </summary>
    private void DashboardToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _dashboardShown = !_dashboardShown;

        if (_dashboardShown && !_dashboardCreated)
        {
            var viewModel = App.ServiceProvider?.GetService<DiscordDashboardViewModel>();
            if (viewModel is null)
            {
                // DI not ready — stay on the bot list rather than showing an empty host.
                _dashboardShown = false;
                return;
            }

            DashboardHost.Content = new DiscordDashboardView { DataContext = viewModel };
            _dashboardCreated = true;
        }

        BotsContent.Visibility = _dashboardShown ? Visibility.Collapsed : Visibility.Visible;
        AddBotButton.Visibility = _dashboardShown ? Visibility.Collapsed : Visibility.Visible;
        DashboardHost.Visibility = _dashboardShown ? Visibility.Visible : Visibility.Collapsed;
        DashboardToggleButton.Content = _dashboardShown ? "BACK TO BOTS" : "DASHBOARD";
    }
}
