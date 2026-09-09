using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text.Json;
using VyralBot.Core.Services.Channels;
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
/// One Discord role of the role→tier mapping editor: the immutable role
/// identity plus the tier selected in the row's ComboBox.
/// </summary>
public partial class RoleTierRow : ObservableObject
{
    public string DiscordRoleId { get; init; } = "";
    public string RoleName { get; init; } = "";
    public uint ColorRaw { get; init; }
    public int Position { get; init; }

    /// <summary>null = unmapped (no tier).</summary>
    [ObservableProperty]
    private string? _selectedTier;

    /// <summary>
    /// Wired by the VM AFTER the stored tier is applied, so persistence stays
    /// in the VM (rows hold no services) and the load-time overlay never
    /// writes back — only later user changes raise it.
    /// </summary>
    public Action<RoleTierRow>? SaveRequested { get; set; }

    partial void OnSelectedTierChanged(string? value) => SaveRequested?.Invoke(this);
}

/// <summary>
/// Moderator dashboard: mod channel setup (create + config), mod role
/// assignments, the command-channel list, the Discord role→tier mapping
/// editor, the moderation action log and the "Add moderator command"
/// dialog (compose → preview → send to the configured mod channel, with
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
    private readonly IConnectedChannelService _connectedChannelService;

    private bool _loaded;

    public ModeratorViewModel(
        ILogger<ModeratorViewModel> logger,
        IDataService dataService,
        IDiscordBotManager botManager,
        IModRoleService modRoleService,
        IConnectedChannelService connectedChannelService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));
        _modRoleService = modRoleService ?? throw new ArgumentNullException(nameof(modRoleService));
        _connectedChannelService = connectedChannelService ?? throw new ArgumentNullException(nameof(connectedChannelService));
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
    // Section 3 — Command channels + Discord role tiers
    // ======================================================================

    public ObservableCollection<RoleTierRow> RoleTiers { get; } = new();

    /// <summary>Tier options for the role→tier ComboBox; "None" = unmapped.</summary>
    public IReadOnlyList<string> RoleTierOptions { get; } =
        new[] { "None" }.Concat(ModTiers.TierNames).ToArray();

    public ObservableCollection<string> CommandChannels { get; } = new();

    private const string NoCommandChannelsPlaceholder =
        "No command channels configured — commands work in every channel (legacy mode)";

    [ObservableProperty]
    private string? _roleTierStatus;

    /// <summary>
    /// Loads the primary bot's first guild's roles and overlays the stored
    /// role→tier assignments. Needs a connected bot inside a guild.
    /// </summary>
    [RelayCommand]
    private async Task LoadRolesAsync()
    {
        var bot = _botManager.Primary;
        var guild = bot?.ConnectedGuilds.FirstOrDefault();
        if (bot is null || guild is null)
        {
            RunOnUi(RoleTiers.Clear);
            RoleTierStatus = "Connect the bot to load roles";
            return;
        }

        try
        {
            IsBusy = true;
            RoleTierStatus = "Loading Discord roles…";

            var roles = await bot.GetGuildRolesAsync(guild.Id);
            var stored = await _dataService.GetRoleTierAssignmentsAsync(bot.BotId);

            RunOnUi(() =>
            {
                RoleTiers.Clear();
                foreach (var role in roles)
                {
                    // @everyone carries the guild id as its role id — not mappable.
                    if (string.Equals(role.Id, guild.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // SelectedTier is applied BEFORE SaveRequested is wired so
                    // the overlay itself never writes a row back.
                    var row = new RoleTierRow
                    {
                        DiscordRoleId = role.Id,
                        RoleName = role.Name,
                        ColorRaw = role.ColorRaw,
                        Position = role.Position,
                        SelectedTier = stored.FirstOrDefault(a => string.Equals(a.DiscordRoleId, role.Id, StringComparison.Ordinal))?.Tier
                    };
                    row.SaveRequested = OnRoleTierSaveRequested;
                    RoleTiers.Add(row);
                }

                // Inside the marshaled lambda so the count is the populated one.
                RoleTierStatus = $"Loaded {RoleTiers.Count} roles from {guild.Name}.";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Discord roles for the role-tier editor");
            RoleTierStatus = $"Error loading roles: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Persists one row's tier selection: a known tier is upserted, "None" or
    /// empty deletes the mapping, an unknown string is skipped with a status.
    /// </summary>
    [RelayCommand]
    private async Task SaveRoleTierAsync(RoleTierRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.DiscordRoleId))
        {
            return;
        }

        var bot = _botManager.Primary;
        if (bot is null)
        {
            RoleTierStatus = "Connect the bot to save role tiers.";
            return;
        }

        try
        {
            var selected = row.SelectedTier;
            if (string.IsNullOrWhiteSpace(selected) || selected.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                await _dataService.DeleteRoleTierAssignmentAsync(bot.BotId, row.DiscordRoleId);
                RoleTierStatus = $"{row.RoleName}: unmapped.";
                return;
            }

            var tier = ModTiers.NormalizeTier(selected);
            if (tier is null)
            {
                RoleTierStatus = $"'{selected.Trim()}' is not a mod tier — nothing saved.";
                return;
            }

            await _dataService.UpsertRoleTierAssignmentAsync(new DiscordRoleTierAssignment
            {
                BotId = bot.BotId,
                GuildId = bot.ConnectedGuilds.FirstOrDefault()?.Id ?? "",
                DiscordRoleId = row.DiscordRoleId,
                RoleName = row.RoleName,
                Tier = tier
            });
            RoleTierStatus = $"{row.RoleName}: {tier}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving role tier for {DiscordRoleId}", row.DiscordRoleId);
            RoleTierStatus = $"Error saving role tier: {ex.Message}";
        }
    }

    private void OnRoleTierSaveRequested(RoleTierRow row) => _ = SaveRoleTierAsync(row);

    /// <summary>
    /// Fills the command-channel list from the monitored channels tagged
    /// Moderation/Admin; a single placeholder line when none are configured
    /// (the legacy "commands work in every channel" mode).
    /// </summary>
    [RelayCommand]
    private async Task LoadCommandChannelsAsync()
    {
        var bot = _botManager.Primary;
        if (bot is null)
        {
            RunOnUi(() =>
            {
                CommandChannels.Clear();
                CommandChannels.Add(NoCommandChannelsPlaceholder);
            });
            return;
        }

        try
        {
            var moderation = await _connectedChannelService.GetByPurposeAsync(bot.BotId, ChannelPurposes.Moderation);
            var admin = await _connectedChannelService.GetByPurposeAsync(bot.BotId, ChannelPurposes.Admin);

            RunOnUi(() =>
            {
                CommandChannels.Clear();
                foreach (var channel in moderation)
                {
                    CommandChannels.Add($"#{channel.ChannelName} — {ChannelPurposes.Moderation} ({channel.GuildName})");
                }

                foreach (var channel in admin)
                {
                    CommandChannels.Add($"#{channel.ChannelName} — {ChannelPurposes.Admin} ({channel.GuildName})");
                }

                if (CommandChannels.Count == 0)
                {
                    CommandChannels.Add(NoCommandChannelsPlaceholder);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading command channels for bot {BotId}", bot.BotId);
            StatusMessage = $"Error loading command channels: {ex.Message}";
        }
    }

    // ======================================================================
    // Section 4 — Action log
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
    // Section 5 — Command builder dialog
    // ======================================================================

    /// <summary>
    /// The per-action parameter metadata the builder dialog renders (verbs,
    /// required target/amount/reason, amount labels). The dialog groups it
    /// into Moderation / Currency / Engagement / Stream FX categories.
    /// </summary>
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
        new ModCommandTemplate("Flash stream lights", "flashlights", needsTarget: false, needsAmount: true, needsReason: false, amountLabel: "Hex color + times (1-20)"),
        new ModCommandTemplate("Stream lights on", "lightson", needsTarget: false, needsAmount: false, needsReason: false),
        new ModCommandTemplate("Stream lights off", "lightsoff", needsTarget: false, needsAmount: false, needsReason: false),
        new ModCommandTemplate("Stream Deck action", "streamdeck", needsTarget: false, needsAmount: true, needsReason: false, amountLabel: "Action name"),
    };

    /// <summary>
    /// Opens the visual !mod command builder. The dialog composes and
    /// validates the command; Run reuses SendModCommandAsync (the same
    /// channel/audit path the old inline builder used) and Save reuses the
    /// favorites persistence below.
    /// </summary>
    [RelayCommand]
    private void OpenCommandBuilder()
    {
        var dialog = new Views.ModeratorCommandBuilderDialog
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = new ModeratorCommandBuilderViewModel(this)
        };

        dialog.ShowDialog();

        var command = dialog.Builder.ComposedPreview.Trim();
        if (!dialog.RunRequested && !dialog.SaveRequested || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (dialog.SaveRequested)
        {
            _ = SaveFavoriteAsync(command);
        }

        if (dialog.RunRequested)
        {
            _ = SendModCommandAsync(command);
        }
    }

    /// <summary>
    /// Sends a composed !mod command into the configured mod channel via the
    /// primary running bot's SendToChannelAsync — the Discord-side mod engine's
    /// moderation gating applies to it exactly like a typed message.
    /// </summary>
    public async Task SendModCommandAsync(string? command)
    {
        command = command?.Trim();
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

    /// <summary>Saves a composed command into the favorites store (Config "Mod.FavoriteCommands").</summary>
    public async Task SaveFavoriteAsync(string? command)
    {
        command = command?.Trim();
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

            var channelId = await _dataService.GetConfigValueAsync(ChannelIdConfigKey);
            UpdateChannelStatus(channelId);

            AutoCreateModChannel = await GetBoolAsync(AutoCreateConfigKey, defaultValue: true);
            ModRoleName = await _dataService.GetConfigValueAsync(ModRoleNameConfigKey) ?? DefaultModRoleName;

            await LoadAssignmentsAsync();
            await RefreshModActionsCommand.ExecuteAsync(null);
            await LoadFavoritesAsync();
            await LoadCommandChannelsCommand.ExecuteAsync(null);

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
