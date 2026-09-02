using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Platform connection management view.
/// </summary>
public partial class PlatformsView : UserControl
{
    private TwitchWebSetupView? _twitchSetupView;
    private KickWebSetupView? _kickSetupView;
    private YouTubeWebSetupView? _youtubeSetupView;
    private TikTokWebSetupView? _tikTokSetupView;
    private DiscordWebSetupView? _discordSetupView;

    public PlatformsView()
    {
        System.Diagnostics.Debug.WriteLine("⚙️ PlatformsView constructor called - setup views will be lazy-loaded");
        InitializeComponent();
        System.Diagnostics.Debug.WriteLine("⚙️ PlatformsView.InitializeComponent() completed");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Watch for visibility changes to lazy-load setup views (prevents WebView2 processes at startup)
        TwitchSetupContainer.IsVisibleChanged += TwitchSetupContainer_IsVisibleChanged;
        KickSetupContainer.IsVisibleChanged += KickSetupContainer_IsVisibleChanged;
        YouTubeSetupContainer.IsVisibleChanged += YouTubeSetupContainer_IsVisibleChanged;
        TikTokSetupContainer.IsVisibleChanged += TikTokSetupContainer_IsVisibleChanged;
        DiscordSetupContainer.IsVisibleChanged += DiscordSetupContainer_IsVisibleChanged;
        System.Diagnostics.Debug.WriteLine("⚙️ PlatformsView lazy-load handlers attached");
    }

    private void TwitchSetupContainer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && TwitchSetupContainer.Content == null && _twitchSetupView == null)
        {
            // Create setup view only when it becomes visible
            var app = System.Windows.Application.Current as App;
            if (app?.Host != null)
            {
                try
                {
                    var setupViewModel = app.Host.Services.GetRequiredService<TwitchWebSetupViewModel>();
                    _twitchSetupView = new TwitchWebSetupView { DataContext = setupViewModel };
                    TwitchSetupContainer.Content = _twitchSetupView;
                    setupViewModel.PropertyChanged += OnSetupViewModelPropertyChanged;
                }
                catch { }
            }
        }
    }

    private void KickSetupContainer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && KickSetupContainer.Content == null && _kickSetupView == null)
        {
            // Create setup view only when it becomes visible
            var app = System.Windows.Application.Current as App;
            if (app?.Host != null)
            {
                try
                {
                    var setupViewModel = app.Host.Services.GetRequiredService<KickWebSetupViewModel>();
                    _kickSetupView = new KickWebSetupView { DataContext = setupViewModel };
                    KickSetupContainer.Content = _kickSetupView;
                }
                catch { }
            }
        }
    }

    private void YouTubeSetupContainer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && YouTubeSetupContainer.Content == null && _youtubeSetupView == null)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.Host != null)
            {
                try
                {
                    var setupViewModel = app.Host.Services.GetRequiredService<YouTubeWebSetupViewModel>();
                    _youtubeSetupView = new YouTubeWebSetupView { DataContext = setupViewModel };
                    YouTubeSetupContainer.Content = _youtubeSetupView;
                    setupViewModel.PropertyChanged += OnSetupViewModelPropertyChanged;
                }
                catch { }
            }
        }
    }

    private void TikTokSetupContainer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && TikTokSetupContainer.Content == null && _tikTokSetupView == null)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.Host != null)
            {
                try
                {
                    var setupViewModel = app.Host.Services.GetRequiredService<TikTokWebSetupViewModel>();
                    _tikTokSetupView = new TikTokWebSetupView { DataContext = setupViewModel };
                    TikTokSetupContainer.Content = _tikTokSetupView;
                    setupViewModel.PropertyChanged += OnSetupViewModelPropertyChanged;
                }
                catch { }
            }
        }
    }

    private void DiscordSetupContainer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && DiscordSetupContainer.Content == null && _discordSetupView == null)
        {
            var app = System.Windows.Application.Current as App;
            if (app?.Host != null)
            {
                try
                {
                    var setupViewModel = app.Host.Services.GetRequiredService<DiscordWebSetupViewModel>();
                    _discordSetupView = new DiscordWebSetupView { DataContext = setupViewModel };
                    DiscordSetupContainer.Content = _discordSetupView;
                    setupViewModel.PropertyChanged += OnSetupViewModelPropertyChanged;
                }
                catch { }
            }
        }
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PlatformsViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in PlatformsView.OnLoaded: {ex.Message}");
            }
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Detach IsVisibleChanged handlers to prevent WeakEventTable stale references on shutdown
        TwitchSetupContainer.IsVisibleChanged -= TwitchSetupContainer_IsVisibleChanged;
        KickSetupContainer.IsVisibleChanged -= KickSetupContainer_IsVisibleChanged;
        YouTubeSetupContainer.IsVisibleChanged -= YouTubeSetupContainer_IsVisibleChanged;
        TikTokSetupContainer.IsVisibleChanged -= TikTokSetupContainer_IsVisibleChanged;
        DiscordSetupContainer.IsVisibleChanged -= DiscordSetupContainer_IsVisibleChanged;

        // Clean up ViewModel event handlers
        if (_twitchSetupView?.DataContext is TwitchWebSetupViewModel twitchSetupViewModel)
        {
            twitchSetupViewModel.PropertyChanged -= OnSetupViewModelPropertyChanged;
        }
        if (_youtubeSetupView?.DataContext is YouTubeWebSetupViewModel youtubeSetupViewModel)
        {
            youtubeSetupViewModel.PropertyChanged -= OnSetupViewModelPropertyChanged;
        }
        if (_tikTokSetupView?.DataContext is TikTokWebSetupViewModel tikTokSetupViewModel)
        {
            tikTokSetupViewModel.PropertyChanged -= OnSetupViewModelPropertyChanged;
        }
        if (_discordSetupView?.DataContext is DiscordWebSetupViewModel discordSetupViewModel)
        {
            discordSetupViewModel.PropertyChanged -= OnSetupViewModelPropertyChanged;
        }

        // Null out content controls so WPF doesn't hold stale Loaded event references
        TwitchSetupContainer.Content = null;
        KickSetupContainer.Content = null;
        YouTubeSetupContainer.Content = null;
        TikTokSetupContainer.Content = null;
        DiscordSetupContainer.Content = null;
        _twitchSetupView = null;
        _kickSetupView = null;
        _youtubeSetupView = null;
        _tikTokSetupView = null;
        _discordSetupView = null;

        // Detach own event handlers
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private async void OnSetupViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When setup completes and returns to initial state, refresh platform view
        bool shouldRefresh = false;

        if (e.PropertyName == nameof(TwitchWebSetupViewModel.ShowSuccessScreen) &&
            sender is TwitchWebSetupViewModel twitchVm &&
            !twitchVm.ShowSuccessScreen && twitchVm.CurrentStep == 1)
        {
            shouldRefresh = true;
        }
        else if (e.PropertyName == nameof(YouTubeWebSetupViewModel.ShowSuccessScreen) &&
            sender is YouTubeWebSetupViewModel youtubeVm &&
            !youtubeVm.ShowSuccessScreen && youtubeVm.CurrentStep == 1)
        {
            shouldRefresh = true;
        }
        else if (e.PropertyName == nameof(TikTokWebSetupViewModel.ShowSuccessScreen) &&
            sender is TikTokWebSetupViewModel tikTokVm &&
            !tikTokVm.ShowSuccessScreen && tikTokVm.CurrentStep == 1)
        {
            shouldRefresh = true;
        }
        else if (e.PropertyName == nameof(DiscordWebSetupViewModel.ShowSuccessScreen) &&
            sender is DiscordWebSetupViewModel discordVm &&
            !discordVm.ShowSuccessScreen && discordVm.CurrentStep == 1)
        {
            shouldRefresh = true;
        }

        if (shouldRefresh && DataContext is PlatformsViewModel platformsViewModel)
        {
            await platformsViewModel.InitializeAsync();
        }
    }
}
