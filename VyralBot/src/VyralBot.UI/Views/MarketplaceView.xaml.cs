using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>Code-behind for the marketplace listing overlay (logic lives in MarketplaceViewModel).</summary>
public partial class MarketplaceView : UserControl
{
    public MarketplaceView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MarketplaceViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            // Settings may already be loaded if the (singleton) view model was
            // refreshed before this view instance existed — seed the masked box.
            SyncApiKeyBox(viewModel.Settings?.ApiKey);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MarketplaceViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Refresh replaces the Settings instance wholesale — push the loaded key
        // back into the masked box so it survives a settings reload.
        if (e.PropertyName == nameof(MarketplaceViewModel.Settings) &&
            sender is MarketplaceViewModel { Settings: { } settings })
        {
            SyncApiKeyBox(settings.ApiKey);
        }
    }

    private void BotGhostApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The BotGhost API key is a secret — it is masked in a PasswordBox and fed
        // into the settings object manually because PasswordBox.Password is not
        // bindable. The SAVE flow (SaveSettingsCommand) reads this same object.
        if (DataContext is MarketplaceViewModel { Settings: { } settings } && sender is PasswordBox passwordBox)
        {
            settings.ApiKey = passwordBox.Password;
        }
    }

    private void SyncApiKeyBox(string? apiKey)
    {
        if (BotGhostApiKeyBox is null)
        {
            return;
        }

        var key = apiKey ?? string.Empty;
        if (BotGhostApiKeyBox.Password != key)
        {
            BotGhostApiKeyBox.Password = key;
        }
    }
}
