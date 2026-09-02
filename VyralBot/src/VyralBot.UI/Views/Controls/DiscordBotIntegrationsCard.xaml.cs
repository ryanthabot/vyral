using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views.Controls;

/// <summary>
/// Self-contained "Integrations" card for the per-bot Discord dashboard
/// (<see cref="DiscordBotDetailView"/>). Registers external bot services
/// (BotGhost, MEE6, Dyno, Carl-bot or custom), opens their dashboards to sign
/// in, lists the commands they run, and flags conflicts with Vyral's own
/// commands so the user can disable the Vyral copy.
/// </summary>
/// <remarks>
/// The card builds its own view model from <see cref="App.ServiceProvider"/> so
/// the hosting view needs no DI wiring, and it stays fully collapsed while its
/// data context is null / until <see cref="Load"/> is called. When hosted
/// anywhere inside DiscordBotDetailView it also auto-follows the detail view
/// model's BotId, so loading works with or without an explicit Load call.
/// </remarks>
public partial class DiscordBotIntegrationsCard : UserControl
{
    private DiscordBotDetailViewModel? _followedDetail;

    /// <summary>
    /// The card's view model; null until the DI container is available (the
    /// card then remains inert and collapsed).
    /// </summary>
    public DiscordBotIntegrationsViewModel? ViewModel { get; private set; }

    public DiscordBotIntegrationsCard()
    {
        InitializeComponent();

        // Self-wire the view model from the app container. This control is
        // instantiated by XAML (not DI), so resolve its two dependencies here.
        try
        {
            var provider = App.ServiceProvider;
            var dataService = provider?.GetService<IDataService>();
            if (dataService != null)
            {
                var logger = provider!.GetService<ILoggerFactory>()?.CreateLogger<DiscordBotIntegrationsViewModel>()
                             ?? NullLogger<DiscordBotIntegrationsViewModel>.Instance;
                ViewModel = new DiscordBotIntegrationsViewModel(logger, dataService);
                CardRoot.DataContext = ViewModel;
            }
        }
        catch
        {
            // Design-time or pre-startup: the card stays inert (collapsed)
            // until a host wires a view model.
        }

        Loaded += OnCardLoaded;
        Unloaded += OnCardUnloaded;
        DataContextChanged += OnCardDataContextChanged;
    }

    /// <summary>
    /// Loads the integrations for one Discord bot. Called by the hosting
    /// DiscordBotDetailView (e.g. `IntegrationsCard?.Load(BotId)` from the
    /// detail view model's LoadAsync). Safe to call repeatedly — each call
    /// reloads from storage.
    /// </summary>
    public void Load(string botId)
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(botId))
            return;

        _ = ViewModel.LoadAsync(botId);
    }

    // When the card is hosted inside DiscordBotDetailView it inherits that
    // page's DataContext (the detail view model). Follow its BotId so the card
    // loads and reloads automatically, without any manual wiring.
    private void OnCardLoaded(object sender, RoutedEventArgs e)
        => HookDetailViewModel(DataContext as DiscordBotDetailViewModel);

    private void OnCardUnloaded(object sender, RoutedEventArgs e)
        => UnhookDetailViewModel();

    private void OnCardDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnhookDetailViewModel();
        HookDetailViewModel(e.NewValue as DiscordBotDetailViewModel);
    }

    private void HookDetailViewModel(DiscordBotDetailViewModel? detail)
    {
        if (detail is null || ReferenceEquals(_followedDetail, detail))
            return;

        UnhookDetailViewModel();
        _followedDetail = detail;
        detail.PropertyChanged += OnFollowedDetailPropertyChanged;

        if (!string.IsNullOrEmpty(detail.BotId))
            Load(detail.BotId);
    }

    private void UnhookDetailViewModel()
    {
        if (_followedDetail is null)
            return;

        _followedDetail.PropertyChanged -= OnFollowedDetailPropertyChanged;
        _followedDetail = null;
    }

    private void OnFollowedDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiscordBotDetailViewModel.BotId))
            Load(_followedDetail?.BotId ?? string.Empty);
    }
}
