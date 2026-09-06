using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text.Json;
using VyralBot.Core.Services.Moderation;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One entry of the Moderator page's !mod command builder: which parameters the
/// template needs and the verb emitted into the composed command line.
/// </summary>
public sealed class ModCommandTemplate
{
    public ModCommandTemplate(string name, string verb, bool needsTarget, bool needsAmount, bool needsReason, string? amountLabel = null)
    {
        Name = name;
        Verb = verb;
        NeedsTarget = needsTarget;
        NeedsAmount = needsAmount;
        NeedsReason = needsReason;
        AmountLabel = amountLabel ?? "Amount";
    }

    /// <summary>Display name shown in the template ComboBox.</summary>
    public string Name { get; }

    /// <summary>Verb after the !mod prefix (e.g. "addcurrency").</summary>
    public string Verb { get; }

    public bool NeedsTarget { get; }
    public bool NeedsAmount { get; }
    public bool NeedsReason { get; }

    /// <summary>Label for the amount field (points / minutes / winner count / messages).</summary>
    public string AmountLabel { get; }

    public override string ToString() => Name;
}

/// <summary>
/// Moderator dashboard: mod channel setup (create + config), mod role
/// assignments, the moderation action log and the interactive !mod command
/// builder (compose → preview → send to the configured mod channel, with
/// save-as-favorite persistence in Config key "Mod.FavoriteCommands").
/// </summary>
public partial class ModeratorViewModel : ViewModelBase
{
    private const string ChannelIdConfigKey = "Mod.ChannelId";
    private const string AutoCreateConfigKey = "Mod.AutoCreateChannel";
    private const string ModRoleNameConfigKey = "Mod.ModRoleName";
    private const string FavoriteCommandsConfigKey = "Mod.FavoriteCommands";
    private const string DefaultModRoleName = "Vyral Mod";

    private static readonly JsonSerializerOptions FavoriteJsonOptions = new();

    private readonly ILogger<ModeratorViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IDiscordBotManager _botManager;
    private readonly IModRoleService _modRoleService;

    private bool _loaded;

    public ModeratorViewModel(
        ILogger<ModeratorViewModel> logger,
        IDataService dataService,
        IDiscordBotManager botManager,
        IModRoleService modRoleService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));
        _modRoleService = modRoleService ?? throw new ArgumentNullException(nameof(modRoleService));
    }

    // ======================================================================
    // Section 1 — Mod channel
    // ======================================================================

    /// <summary>Status line under the Mod Channel card ("configured" vs "not created").</summary>
    [ObservableProperty]
    private string _modChannelStatus = "Mod channel: not created";

    [ObservableProperty]
    private bool _autoCreateModChannel = true;

    [ObservableProperty]
    private string _modRoleName = DefaultModRoleName;

    partial void OnAutoCreateModChannelChanged(bool value)
    {
        if (_loaded)
        {
            _ = SaveConfigAsync(AutoCreateConfigKey, value.ToString(), "Mod");
        }
    }

    partial void OnModRoleNameChanged(string value)
    {
        if (_loaded)
        {
            _ = SaveConfigAsync(ModRoleNameConfigKey, value, "Mod");
        }
    }

    /// <summary>
    /// Creates the mod channel on the primary running bot via the Discord API,
    /// persists the resulting channel id into Config "Mod.ChannelId" and the
    /// bot's DiscordBots row (ModChannelId).
    /// </summary>
    [RelayCommand]
    private async Task CreateModChannelAsync()
    {
        var bot = _botManager.Primary;
        if (bot is null)
        {
            StatusMessage = "No Discord bot is connected — start a bot on the Discord Bots page first.";
            return;
        }

        var roleName = string.IsNullOrWhiteSpace(ModRoleName) ? DefaultModRoleName : ModRoleName.Trim();

        try
        {
            IsBusy = true;
            StatusMessage = "Creating mod channel…";

            var (success, channelId, error) = await bot.CreateModChannelAsync(roleName);

            if (!success || string.IsNullOrWhiteSpace(channelId))
            {
                StatusMessage = string.IsNullOrWhiteSpace(error)
                    ? "Mod channel creation failed."
                    : $"Mod channel creation failed: {error}";
                _logger.LogWarning("Mod channel creation failed on bot {BotId}: {Error}", bot.BotId, error);
                return;
            }

            // Persist into the config key the mod engine reads AND the bot row,
            // so per-bot mod routing (watchtime-gating style) sees it too.
            await _dataService.SetConfigValueAsync(ChannelIdConfigKey, channelId, "Mod");

            var rows = await _dataService.GetDiscordBotsAsync();
            var row = rows.FirstOrDefault(b => string.Equals(b.Id, bot.BotId, StringComparison.Ordinal));
            if (row is not null)
            {
                row.ModChannelId = channelId;
                await _dataService.SaveDiscordBotAsync(row);
            }

            UpdateChannelStatus(channelId);
            StatusMessage = $"Mod channel created ({channelId}).";
            _logger.LogInformation("Mod channel {ChannelId} created via bot {BotId}", channelId, bot.BotId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating the mod channel");
            StatusMessage = $"Error creating mod channel: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ======================================================================
    // Section 2 — Mod roles
    // ======================================================================

    public ObservableCollection<ModRoleAssignment> ModAssignments { get; } = new();

    /// <summary>The four mod tiers (ModTiers rank values).</summary>
    public IReadOnlyList<string> Tiers { get; } = new[] { "TrialMod", "Mod", "SeniorMod", "Admin" };

    [ObservableProperty]
    private string _newModDiscordUserId = string.Empty;

    [ObservableProperty]
    private string _newModUsername = string.Empty;

    [ObservableProperty]
    private string _selectedModTier = "TrialMod";

    [RelayCommand]
    private async Task AssignModRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewModDiscordUserId))
        {
            StatusMessage = "Enter the Discord user ID of the mod to assign.";
            return;
        }

        try
        {
            IsBusy = true;
            await _modRoleService.AssignAsync(
                NewModDiscordUserId.Trim(),
                string.IsNullOrWhiteSpace(NewModUsername) ? NewModDiscordUserId.Trim() : NewModUsername.Trim(),
                SelectedModTier,
                "Dashboard");

            NewModDiscordUserId = string.Empty;
            NewModUsername = string.Empty;
            StatusMessage = $"Mod role assigned ({SelectedModTier}).";
            await LoadAssignmentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning mod role");
            StatusMessage = $"Error assigning mod role: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveModRoleAsync(ModRoleAssignment? assignment)
    {
        if (assignment is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _modRoleService.UnassignAsync(assignment.DiscordUserId);
            StatusMessage = $"Mod role removed from {assignment.Username}.";
            await LoadAssignmentsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing mod role for {DiscordUserId}", assignment.DiscordUserId);
            StatusMessage = $"Error removing mod role: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ======================================================================
    // Section 3 — Action log
    // ======================================================================

    public ObservableCollection<ModAction> ModActions { get; } = new();

    [RelayCommand]
    private async Task RefreshModActionsAsync()
    {
        try
        {
            var actions = await _dataService.GetModActionsAsync(100);

            // Collection writes are marshaled to the UI thread (loads can be
            // triggered while other EventBus/refresh work is in flight).
            RunOnUi(() =>
            {
                ModActions.Clear();
                foreach (var action in actions)
                {
                    ModActions.Add(action);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the mod action log");
            StatusMessage = $"Error loading action log: {ex.Message}";
        }
    }

    // ======================================================================
    // Section 4 — Command builder
    // ======================================================================

    public IReadOnlyList<ModCommandTemplate> ActionTemplates { get; } = new[]
    {
        new ModCommandTemplate("Add currency", "addcurrency", needsTarget: true, needsAmount: true, needsReason: false, amountLabel: "Amount"),
        new ModCommandTemplate("Remove currency", "removecurrency", needsTarget: true, needsAmount: true, needsReason: false, amountLabel: "Amount"),
        new ModCommandTemplate("Set balance", "setbalance", needsTarget: true, needsAmount: true, needsReason: false, amountLabel: "Amount"),
        new ModCommandTemplate("Start giveaway", "startgiveaway", needsTarget: false, needsAmount: false, needsReason: true),
        new ModCommandTemplate("Cancel giveaway", "cancelgiveaway", needsTarget: false, needsAmount: false, needsReason: false),
        new ModCommandTemplate("Draw winners", "drawwinners", needsTarget: false, needsAmount: true, needsReason: false, amountLabel: "Winner count"),
        new ModCommandTemplate("Start poll", "startpoll", needsTarget: false, needsAmount: false, needsReason: true),
        new ModCommandTemplate("End poll", "endpoll", needsTarget: false, needsAmount: false, needsReason: false),
        new ModCommandTemplate("Timeout", "timeout", needsTarget: true, needsAmount: true, needsReason: true, amountLabel: "Minutes"),
        new ModCommandTemplate("Kick", "kick", needsTarget: true, needsAmount: false, needsReason: true),
        new ModCommandTemplate("Ban", "ban", needsTarget: true, needsAmount: false, needsReason: true),
        new ModCommandTemplate("Purge", "purge", needsTarget: false, needsAmount: true, needsReason: false, amountLabel: "Messages (1-100)"),
    };

    [ObservableProperty]
    private ModCommandTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _builderTarget = string.Empty;

    [ObservableProperty]
    private string _builderAmount = string.Empty;

    [ObservableProperty]
    private string _builderReason = string.Empty;

    /// <summary>Live-composed !mod command line (read-only preview in the view).</summary>
    [ObservableProperty]
    private string _composedPreview = "!mod";

    /// <summary>Whether the builder's target field applies to the selected template.</summary>
    public bool IsTargetVisible => SelectedTemplate?.NeedsTarget == true;

    public bool IsAmountVisible => SelectedTemplate?.NeedsAmount == true;

    public bool IsReasonVisible => SelectedTemplate?.NeedsReason == true;

    public string AmountLabel => SelectedTemplate?.AmountLabel ?? "Amount";

    partial void OnSelectedTemplateChanged(ModCommandTemplate? value)
    {
        OnPropertyChanged(nameof(IsTargetVisible));
        OnPropertyChanged(nameof(IsAmountVisible));
        OnPropertyChanged(nameof(IsReasonVisible));
        OnPropertyChanged(nameof(AmountLabel));
        UpdateComposedPreview();
    }

    partial void OnBuilderTargetChanged(string value) => UpdateComposedPreview();

    partial void OnBuilderAmountChanged(string value) => UpdateComposedPreview();

    partial void OnBuilderReasonChanged(string value) => UpdateComposedPreview();

    private void UpdateComposedPreview()
    {
        var parts = new List<string> { "!mod" };

        if (SelectedTemplate is not null)
        {
            parts.Add(SelectedTemplate.Verb);
        }

        if (SelectedTemplate?.NeedsTarget == true && !string.IsNullOrWhiteSpace(BuilderTarget))
        {
            parts.Add(BuilderTarget.Trim());
        }

        if (SelectedTemplate?.NeedsAmount == true && !string.IsNullOrWhiteSpace(BuilderAmount))
        {
            parts.Add(BuilderAmount.Trim());
        }

        if (SelectedTemplate?.NeedsReason == true && !string.IsNullOrWhiteSpace(BuilderReason))
        {
            parts.Add(BuilderReason.Trim());
        }

        ComposedPreview = string.Join(' ', parts);
    }

    /// <summary>
    /// Sends the composed !mod command into the configured mod channel via the
    /// primary running bot's SendToChannelAsync — the Discord-side mod engine's
    /// moderation gating applies to it exactly like a typed message.
    /// </summary>
    [RelayCommand]
    private async Task RunModCommandAsync()
    {
        var command = ComposedPreview?.Trim();
        if (string.IsNullOrWhiteSpace(command) || command.Equals("!mod", StringComparison.Ordinal))
        {
            StatusMessage = "Compose a command first.";
            return;
        }

        var bot = _botManager.Primary;
        if (bot is null)
        {
            StatusMessage = "No Discord bot is connected — start a bot on the Discord Bots page first.";
            return;
        }

        try
        {
            IsBusy = true;

            var channelId = await ResolveModChannelIdAsync();
            if (string.IsNullOrWhiteSpace(channelId))
            {
                StatusMessage = "No mod channel configured — create one in the Mod Channel section first.";
                return;
            }

            var sent = await bot.SendToChannelAsync(channelId, command, null);
            StatusMessage = sent
                ? "Command sent to the mod channel."
                : "The bot could not post into the mod channel (missing access?).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running mod command '{Command}'", command);
            StatusMessage = $"Error running command: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ObservableCollection<string> FavoriteCommands { get; } = new();

    [RelayCommand]
    private async Task SaveFavoriteAsync()
    {
        var command = ComposedPreview?.Trim();
        if (string.IsNullOrWhiteSpace(command) || command.Equals("!mod", StringComparison.Ordinal))
        {
            StatusMessage = "Compose a command before saving it as a favorite.";
            return;
        }

        if (FavoriteCommands.Contains(command))
        {
            StatusMessage = "That command is already saved.";
            return;
        }

        FavoriteCommands.Add(command);
        await PersistFavoritesAsync();
        StatusMessage = "Favorite saved.";
    }

    [RelayCommand]
    private async Task RunFavoriteAsync(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var bot = _botManager.Primary;
        if (bot is null)
        {
            StatusMessage = "No Discord bot is connected — start a bot on the Discord Bots page first.";
            return;
        }

        try
        {
            IsBusy = true;

            var channelId = await ResolveModChannelIdAsync();
            if (string.IsNullOrWhiteSpace(channelId))
            {
                StatusMessage = "No mod channel configured — create one in the Mod Channel section first.";
                return;
            }

            var sent = await bot.SendToChannelAsync(channelId, command, null);
            StatusMessage = sent
                ? "Favorite sent to the mod channel."
                : "The bot could not post into the mod channel (missing access?).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running favorite command '{Command}'", command);
            StatusMessage = $"Error running favorite: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteFavoriteAsync(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || !FavoriteCommands.Contains(command))
        {
            return;
        }

        FavoriteCommands.Remove(command);
        await PersistFavoritesAsync();
        StatusMessage = "Favorite removed.";
    }

    // ======================================================================
    // Status / busy
    // ======================================================================

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    // ======================================================================
    // Init + helpers
    // ======================================================================

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing Moderator view");

            // SelectedTemplate has no parameterless default — pick the first
            // template BEFORE _loaded flips so the change handler stays cheap.
            SelectedTemplate = ActionTemplates[0];
            UpdateComposedPreview();

            var channelId = await _dataService.GetConfigValueAsync(ChannelIdConfigKey);
            UpdateChannelStatus(channelId);

            AutoCreateModChannel = await GetBoolAsync(AutoCreateConfigKey, defaultValue: true);
            ModRoleName = await _dataService.GetConfigValueAsync(ModRoleNameConfigKey) ?? DefaultModRoleName;

            await LoadAssignmentsAsync();
            await RefreshModActionsCommand.ExecuteAsync(null);
            await LoadFavoritesAsync();

            _loaded = true;
            _logger.LogInformation("Moderator view initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing the Moderator view");
            StatusMessage = $"Error initializing: {ex.Message}";
        }
    }

    private async Task LoadAssignmentsAsync()
    {
        var assignments = await _modRoleService.GetAllAsync();
        var ordered = assignments.OrderByDescending(a => a.AssignedAt).ToList();

        RunOnUi(() =>
        {
            ModAssignments.Clear();
            foreach (var assignment in ordered)
            {
                ModAssignments.Add(assignment);
            }
        });
    }

    private async Task LoadFavoritesAsync()
    {
        var raw = await _dataService.GetConfigValueAsync(FavoriteCommandsConfigKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            var favorites = JsonSerializer.Deserialize<List<string>>(raw, FavoriteJsonOptions) ?? new List<string>();
            RunOnUi(() =>
            {
                FavoriteCommands.Clear();
                foreach (var favorite in favorites.Where(f => !string.IsNullOrWhiteSpace(f)))
                {
                    FavoriteCommands.Add(favorite);
                }
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Favorite commands config contains invalid JSON — ignoring");
        }
    }

    private async Task PersistFavoritesAsync()
    {
        var json = JsonSerializer.Serialize(FavoriteCommands.ToList(), FavoriteJsonOptions);
        await _dataService.SetConfigValueAsync(FavoriteCommandsConfigKey, json, "Mod");
    }

    /// <summary>
    /// Resolves the mod channel the builder should post into: Config
    /// "Mod.ChannelId" first (what the UI creates/saves), falling back to the
    /// primary bot's DiscordBots row ModChannelId.
    /// </summary>
    private async Task<string?> ResolveModChannelIdAsync()
    {
        var configured = await _dataService.GetConfigValueAsync(ChannelIdConfigKey);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var primaryBotId = _botManager.Primary?.BotId;
        if (string.IsNullOrWhiteSpace(primaryBotId))
        {
            return null;
        }

        var rows = await _dataService.GetDiscordBotsAsync();
        return rows.FirstOrDefault(b => string.Equals(b.Id, primaryBotId, StringComparison.Ordinal))?.ModChannelId;
    }

    private void UpdateChannelStatus(string? channelId)
    {
        ModChannelStatus = string.IsNullOrWhiteSpace(channelId)
            ? "Mod channel: not created"
            : $"Mod channel: {channelId}";
    }

    private async Task SaveConfigAsync(string key, string value, string category)
    {
        try
        {
            await _dataService.SetConfigValueAsync(key, value, category);
            StatusMessage = "Setting saved";
            _logger.LogInformation("{Key} saved", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving {Key}", key);
            StatusMessage = $"Error saving setting: {ex.Message}";
        }
    }

    private async Task<bool> GetBoolAsync(string key, bool defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    /// <summary>Marshals collection/status mutations onto the UI thread.</summary>
    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }
}
