using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Discord Bots page: the multi-bot home. Lists configured bots with their
/// live status, quick-adds new bots, and opens the per-bot dashboard.
/// Initialization is driven by MainViewModel navigation (InitializeAsync).
/// </summary>
public partial class DiscordBotsView : UserControl
{
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
}
