using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One card on the Discord Bots page: a configured bot, its live connection
/// state, and the per-card UI state for the busy spinner and inline rename.
/// </summary>
public partial class DiscordBotCardViewModel : ObservableObject
{
    /// <summary>Latest known config for this bot (used for renames/persist).</summary>
    public DiscordBotConfig Config { get; set; }

    public string Id => Config.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Offline";

    [ObservableProperty]
    private string _botUsername = "";

    /// <summary>True when this bot starts automatically at app launch (drives the card's rocket badge).</summary>
    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private string _serverCountText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = "";

    /// <summary>
    /// Detected identity line for the card's muted secondary text:
    /// "{BotUsername ?? "connecting…"} · ID {ApplicationId ?? "—"}". Sourced
    /// from the persisted config row (populated when the bot connects), so it
    /// degrades gracefully for a bot that has never connected.
    /// </summary>
    [ObservableProperty]
    private string _identityText = "";

    public DiscordBotCardViewModel(DiscordBotConfig config)
    {
        Config = config;
        _name = config.Name;
        _autoStart = config.AutoStart;
        _identityText = BuildIdentityText(config);
    }

    private static string BuildIdentityText(DiscordBotConfig config)
    {
        var username = string.IsNullOrWhiteSpace(config.BotUsername) ? "connecting…" : config.BotUsername!;
        var applicationId = string.IsNullOrWhiteSpace(config.ApplicationId) ? "—" : config.ApplicationId!;
        return $"🤖 {username} · ID {applicationId}";
    }
}

/// <summary>
/// Discord Bots page: the multi-bot home. Lists every configured Discord bot
/// with its live status, quick-adds new bots from a name + token, and opens
/// the per-bot dashboard. Patterned on PlatformsViewModel (callbacks wired by
/// MainViewModel) — no WebView2 involved.
/// </summary>
public partial class DiscordBotsViewModel : ViewModelBase
{
    private readonly ILogger<DiscordBotsViewModel> _logger;
    private readonly IDiscordBotManager _botManager;

    private Action<string>? _openDetail;
    private Action? _openWizard;

    [ObservableProperty]
    private string _statusMessage = "Loading bots...";

    [ObservableProperty]
    private bool _hasBots;

    [ObservableProperty]
    private bool _isAddPanelVisible = true;

    [ObservableProperty]
    private bool _isAdding;

    [ObservableProperty]
    private string _newBotName = "";

    [ObservableProperty]
    private string _newBotToken = "";

    [ObservableProperty]
    private string _addStatus = "";

    public ObservableCollection<DiscordBotCardViewModel> Bots { get; } = new();

    public DiscordBotsViewModel(
        ILogger<DiscordBotsViewModel> logger,
        IDiscordBotManager botManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));

        // The manager raises this from background threads whenever a bot is
        // added, removed, started or stopped — hop to the UI thread to refresh.
        _botManager.BotsChanged += OnBotsChanged;
    }

    /// <summary>
    /// Sets a callback invoked when a bot card's OPEN button is clicked.
    /// MainViewModel wires this to the per-bot dashboard navigation.
    /// </summary>
    public void SetOpenDetailCallback(Action<string> openDetail)
    {
        _openDetail = openDetail;
    }

    /// <summary>
    /// Sets a callback invoked when the user chooses the full setup wizard.
    /// </summary>
    public void SetOpenWizardCallback(Action openWizard)
    {
        _openWizard = openWizard;
    }

    private void OnBotsChanged()
    {
        var app = Application.Current;
        if (app == null)
            return;

        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing Discord bots after change");
            }
        });
    }

    /// <summary>
    /// Initializes the page. Call after the ViewModel is created/resolved.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshAsync();

        // Default the quick-add panel to open when there is nothing to show yet
        if (!HasBots)
        {
            IsAddPanelVisible = true;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var configs = await _botManager.LoadBotsAsync();

            Bots.Clear();
            foreach (var config in configs)
            {
                var card = new DiscordBotCardViewModel(config);
                ApplyLiveState(card);
                Bots.Add(card);
            }

            HasBots = Bots.Count > 0;
            StatusMessage = Bots.Count == 0
                ? "No Discord bots yet — add one below to get started"
                : $"{Bots.Count} bot{(Bots.Count == 1 ? string.Empty : "s")} configured";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Discord bots");
            StatusMessage = $"Error loading bots: {ex.Message}";
        }
    }

    /// <summary>
    /// True when another configured bot whose gateway session is currently
    /// running shares this card's token (StartBotAsync refuses those starts so
    /// one token never opens two gateway sessions).
    /// </summary>
    private async Task<bool> IsTokenUsedByRunningBotAsync(DiscordBotCardViewModel card)
    {
        try
        {
            var token = card.Config.Token;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var configs = await _botManager.LoadBotsAsync();
            return configs.Any(other =>
                other.Id != card.Id
                && string.Equals(other.Token, token, StringComparison.Ordinal)
                && _botManager.Get(other.Id)?.IsConnected == true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check duplicate-token state for Discord bot {BotId}", card.Id);
            return false;
        }
    }

    private void ApplyLiveState(DiscordBotCardViewModel card)
    {
        var service = _botManager.Get(card.Id);
        card.IsConnected = service?.IsConnected == true;
        card.BotUsername = service?.BotUsername ?? "";

        var guildCount = service?.ConnectedGuilds.Count ?? 0;
        card.ServerCountText = service == null
            ? string.Empty
            : guildCount == 1 ? "1 server" : $"{guildCount} servers";

        card.StatusText = card.IsConnected
            ? (string.IsNullOrEmpty(card.BotUsername) ? "Connected" : $"Connected as {card.BotUsername}")
            : "Offline";
    }

    [RelayCommand]
    private void OpenBot(DiscordBotCardViewModel? card)
    {
        if (card == null)
            return;

        _openDetail?.Invoke(card.Id);
    }

    [RelayCommand]
    private async Task StartBotAsync(DiscordBotCardViewModel? card)
    {
        if (card == null || card.IsBusy)
            return;

        card.IsBusy = true;
        card.StatusText = "Starting...";
        try
        {
            var started = await _botManager.StartBotAsync(card.Id);
            if (!started)
            {
                StatusMessage = await IsTokenUsedByRunningBotAsync(card)
                    ? $"Could not start {card.Name} — its token is already in use by another bot"
                    : $"Could not start {card.Name} — check the bot token and try again";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Discord bot {BotId}", card.Id);
            StatusMessage = $"Error starting {card.Name}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task StopBotAsync(DiscordBotCardViewModel? card)
    {
        if (card == null || card.IsBusy)
            return;

        card.IsBusy = true;
        card.StatusText = "Stopping...";
        try
        {
            await _botManager.StopBotAsync(card.Id);
            StatusMessage = $"{card.Name} stopped";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Discord bot {BotId}", card.Id);
            StatusMessage = $"Error stopping {card.Name}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveBotAsync(DiscordBotCardViewModel? card)
    {
        if (card == null || card.IsBusy)
            return;

        var confirmed = MessageBox.Show(
            $"Remove '{card.Name}' from Vyral? The bot will be stopped and its saved configuration deleted.",
            "Vyral - Remove Discord Bot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmed != MessageBoxResult.Yes)
            return;

        card.IsBusy = true;
        try
        {
            await _botManager.RemoveBotAsync(card.Id);
            StatusMessage = $"{card.Name} removed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing Discord bot {BotId}", card.Id);
            StatusMessage = $"Error removing {card.Name}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private void BeginRename(DiscordBotCardViewModel? card)
    {
        if (card == null)
            return;

        card.RenameText = card.Name;
        card.IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename(DiscordBotCardViewModel? card)
    {
        if (card == null)
            return;

        card.IsRenaming = false;
    }

    [RelayCommand]
    private async Task SaveRenameAsync(DiscordBotCardViewModel? card)
    {
        if (card == null)
            return;

        var newName = card.RenameText?.Trim();
        card.IsRenaming = false;

        if (string.IsNullOrEmpty(newName) || newName == card.Name)
            return;

        try
        {
            var updated = card.Config with { Name = newName };
            var saved = await _botManager.UpdateBotAsync(updated);
            if (saved)
            {
                card.Config = updated;
                card.Name = newName;
                StatusMessage = $"Bot renamed to {newName}";
            }
            else
            {
                StatusMessage = $"Could not rename {card.Name} — a bot with that name may already exist";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming Discord bot {BotId}", card.Id);
            StatusMessage = $"Error renaming {card.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleAddBot()
    {
        IsAddPanelVisible = !IsAddPanelVisible;
    }

    /// <summary>
    /// Quick Add: saves the bot, then starts it — starting validates the
    /// token by connecting the Discord gateway.
    /// </summary>
    [RelayCommand]
    private async Task AddBotAsync()
    {
        if (IsAdding)
            return;

        var name = NewBotName?.Trim();
        var token = NewBotToken?.Trim();

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token))
        {
            AddStatus = "Give the bot a name and paste its token to add it";
            return;
        }

        IsAdding = true;
        AddStatus = $"Connecting {name} to verify the token...";
        try
        {
            var bot = new DiscordBotConfig(
                Id: Guid.NewGuid().ToString("N"),
                Name: name,
                Token: token,
                Status: null,
                SelectedChannelId: null);

            var added = await _botManager.AddBotAsync(bot);
            if (!added)
            {
                AddStatus = $"Could not save {name} — a bot with this name may already exist";
                return;
            }

            // Starting the bot validates the token by connecting the gateway
            var started = await _botManager.StartBotAsync(bot.Id);
            AddStatus = started
                ? $"{name} added and connected — open it to pick an active channel"
                : $"{name} saved, but its token was rejected — start it again after checking the token";

            NewBotName = string.Empty;
            NewBotToken = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding Discord bot {BotName}", name);
            AddStatus = $"Error adding {name}: {ex.Message}";
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    /// Secondary add path: the existing full-page Discord setup wizard.
    /// For now the wizard still configures the single setup bot; the bots
    /// list refreshes when it completes.
    /// </summary>
    [RelayCommand]
    private void RunFullWizard()
    {
        _openWizard?.Invoke();
    }
}
