using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using VyralBot.Core.Services.Channels;
using VyralBot.Core.Services.Moderation;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One row of the Moderator Commands card: a stored mod_command workflow with
/// its "!mod verb (tier)" display text and the enable-toggle state.
/// </summary>
public partial class ModCommandWorkflowRow : ObservableObject
{
    public ModCommandWorkflowRow(Workflow workflow)
    {
        Workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _isEnabled = workflow.Enabled;
    }

    public Workflow Workflow { get; }

    public string Id => Workflow.Id;

    public string Name => Workflow.Name;

    /// <summary>"!mod {verb} ({tier})" — same shape as the flow editor's row label.</summary>
    public string CommandLabel
    {
        get
        {
            var verb = string.IsNullOrWhiteSpace(Workflow.Trigger.CommandName) ? "?" : Workflow.Trigger.CommandName.Trim();
            var tier = ModTiers.NormalizeTier(Workflow.Trigger.MinimumTier) ?? "Mod";
            return $"!mod {verb} ({tier})";
        }
    }

    /// <summary>Step count of the flow (0 for a still-empty canvas).</summary>
    public string StepSummary
    {
        get
        {
            var count = Workflow.Nodes?.Count ?? 0;
            return $"{count} step{(count == 1 ? string.Empty : "s")}";
        }
    }

    /// <summary>Row toggle state (mirrors Workflow.Enabled; persisted by the VM).</summary>
    [ObservableProperty]
    private bool _isEnabled;
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
/// One row of the Auto-Moderation card: observable edit state for one
/// <see cref="ModerationRuleConfig"/> slot. Rows hold no services — the VM
/// copies the observable values back into the rule set on save
/// (<see cref="ApplyToConfig"/>), and the labels vary per rule type
/// (e.g. Threshold = "Max caps %" for Caps but "Repeats" for RepeatedMessage).
/// </summary>
public partial class AutoModRuleRow : ObservableObject
{
    public AutoModRuleRow(
        ModerationRuleType type,
        string displayName,
        string description,
        string thresholdLabel,
        string minLengthLabel,
        ModerationRuleConfig config)
    {
        Type = type;
        DisplayName = displayName;
        Description = description;
        ThresholdLabel = thresholdLabel;
        MinLengthLabel = minLengthLabel;
        Config = config;
        _isEnabled = config.Enabled;
        _action = config.Action.ToString();
        _threshold = config.Threshold;
        _minLength = config.MinLength;
        _timeoutSeconds = config.TimeoutSeconds;
    }

    public ModerationRuleType Type { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string ThresholdLabel { get; }

    public string MinLengthLabel { get; }

    /// <summary>The live config object edited by this row (copied out on save).</summary>
    public ModerationRuleConfig Config { get; }

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>One of "Delete" / "Warn" / "Timeout" / "Ban" (AutoModActionOptions).</summary>
    [ObservableProperty]
    private string _action;

    [ObservableProperty]
    private int _threshold;

    [ObservableProperty]
    private int _minLength;

    [ObservableProperty]
    private int _timeoutSeconds;

    /// <summary>Copies the row's observable values into its config slot.</summary>
    public void ApplyToConfig()
    {
        Config.Enabled = IsEnabled;
        Config.Action = Enum.TryParse<ModerationActionKind>(Action, out var parsed)
            ? parsed
            : ModerationActionKind.Delete;
        Config.Threshold = Threshold;
        Config.MinLength = MinLength;
        Config.TimeoutSeconds = TimeoutSeconds;
    }
}

/// <summary>
/// Moderator dashboard: mod channel setup (create + config), mod role
/// assignments, the command-channel list, the Discord role→tier mapping
/// editor, the moderation action log, the Auto-Moderation card (chat content
/// filter) and the Moderator Commands card — custom
/// "!mod verb" workflows authored in the visual Flow editor (the page's
/// "Add moderator command" button opens that editor as an overlay; the host
/// lives in <see cref="Views.ModeratorView"/>'s code-behind).
/// </summary>
public partial class ModeratorViewModel : ViewModelBase
{
    private const string ChannelIdConfigKey = "Mod.ChannelId";
    private const string AutoCreateConfigKey = "Mod.AutoCreateChannel";
    private const string ModRoleNameConfigKey = "Mod.ModRoleName";
    private const string DefaultModRoleName = "Vyral Mod";

    private readonly ILogger<ModeratorViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IDiscordBotManager _botManager;
    private readonly IModRoleService _modRoleService;
    private readonly IConnectedChannelService _connectedChannelService;
    private readonly VyralBot.UI.Services.WorkflowTriggerService _triggerService;
    private readonly ModerationRulesRepository _autoModRepository;
    private readonly ModerationActionService _autoModActions;

    private bool _loaded;

    public ModeratorViewModel(
        ILogger<ModeratorViewModel> logger,
        IDataService dataService,
        IDiscordBotManager botManager,
        IModRoleService modRoleService,
        IConnectedChannelService connectedChannelService,
        VyralBot.UI.Services.WorkflowTriggerService triggerService,
        FlowEditorViewModel flowEditor,
        ModerationRulesRepository autoModRepository,
        ModerationActionService autoModActions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _botManager = botManager ?? throw new ArgumentNullException(nameof(botManager));
        _modRoleService = modRoleService ?? throw new ArgumentNullException(nameof(modRoleService));
        _connectedChannelService = connectedChannelService ?? throw new ArgumentNullException(nameof(connectedChannelService));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _autoModRepository = autoModRepository ?? throw new ArgumentNullException(nameof(autoModRepository));
        _autoModActions = autoModActions ?? throw new ArgumentNullException(nameof(autoModActions));
        FlowEditor = flowEditor ?? throw new ArgumentNullException(nameof(flowEditor));
    }

    /// <summary>The singleton visual Flow editor (DI) — the overlay host uses it.</summary>
    public FlowEditorViewModel FlowEditor { get; }

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

    /// <summary>Unfiltered batch backing the action log (re-filtered in place).</summary>
    private List<ModAction> _modActionCache = new();

    /// <summary>
    /// Free-text filter over the loaded action-log batch: matches the action
    /// kind, target username or mod username (e.g. "AutoMod", "ban", a viewer).
    /// </summary>
    [ObservableProperty]
    private string? _modActionFilter;

    partial void OnModActionFilterChanged(string? value) => ApplyModActionFilter();

    [RelayCommand]
    private async Task RefreshModActionsAsync()
    {
        try
        {
            var actions = await _dataService.GetModActionsAsync(100);
            _modActionCache = actions.ToList();
            ApplyModActionFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the mod action log");
            StatusMessage = $"Error loading action log: {ex.Message}";
        }
    }

    /// <summary>Re-applies <see cref="ModActionFilter"/> over the cached batch.</summary>
    private void ApplyModActionFilter()
    {
        var filter = (ModActionFilter ?? "").Trim();

        // Collection writes are marshaled to the UI thread (loads can be
        // triggered while other EventBus/refresh work is in flight).
        RunOnUi(() =>
        {
            ModActions.Clear();
            foreach (var action in _modActionCache)
            {
                if (filter.Length == 0 ||
                    Contains(action.Action, filter) ||
                    Contains(action.TargetUsername, filter) ||
                    Contains(action.ModUsername, filter) ||
                    Contains(action.Details, filter))
                {
                    ModActions.Add(action);
                }
            }
        });
    }

    private static bool Contains(string? value, string filter)
        => value is not null && value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    // ======================================================================
    // Section 4b — Auto-moderation (chat content filter)
    // ======================================================================

    /// <summary>
    /// Edit rows for the six detection rules (caps / links / emojis / banned
    /// words / repeats / ASCII art) — see <see cref="ChatModerationEngine"/>.
    /// </summary>
    public ObservableCollection<AutoModRuleRow> AutoModRules { get; } = new();

    /// <summary>Action options for every rule row (ModerationActionKind names).</summary>
    public IReadOnlyList<string> AutoModActionOptions { get; } = Enum.GetNames<ModerationActionKind>();

    /// <summary>Per-platform enforcement parity lines (adapters vs built-ins).</summary>
    public ObservableCollection<string> AutoModPlatformStatus { get; } = new();

    [ObservableProperty]
    private bool _autoModEnabled;

    // One set of exemption toggles for ALL rules (kept uniform on save; the
    // underlying config stores them per rule for future fine-grained UIs).
    [ObservableProperty]
    private bool _autoModExemptModerators = true;

    [ObservableProperty]
    private bool _autoModExemptVips;

    [ObservableProperty]
    private bool _autoModExemptSubscribers;

    /// <summary>Comma-separated platform filter; empty = every platform.</summary>
    [ObservableProperty]
    private string _autoModPlatformsText = string.Empty;

    [ObservableProperty]
    private bool _escalationEnabled = true;

    [ObservableProperty]
    private int _escalationStrikesToTimeout = 2;

    [ObservableProperty]
    private int _escalationStrikesToBan = 4;

    [ObservableProperty]
    private int _escalationWindowMinutes = 15;

    [ObservableProperty]
    private int _escalationBaseTimeoutSeconds = 120;

    /// <summary>One domain per line ("twitch.tv" also allows subdomains).</summary>
    [ObservableProperty]
    private string _linkAllowlistText = string.Empty;

    /// <summary>One word or phrase per line (phrases match as substrings).</summary>
    [ObservableProperty]
    private string _bannedWordsText = string.Empty;

    /// <summary>One username per line — never auto-moderated.</summary>
    [ObservableProperty]
    private string _exemptUsersText = string.Empty;

    [ObservableProperty]
    private string _autoModTestMessage = string.Empty;

    [ObservableProperty]
    private string? _autoModTestResult;

    [ObservableProperty]
    private string? _autoModStatus;

    /// <summary>The rule set currently being edited (loaded by LoadAutoModConfigAsync).</summary>
    private ModerationRuleSet? _autoModRules;

    /// <summary>
    /// Loads the persisted rule JSON via the DI-registered repository (which
    /// applies it to the shared engine) and fills the card. The action
    /// service is configured with persistence + logging ONCE at app startup
    /// (see App.OnStartup) — re-configuring here would drop its logger.
    /// </summary>
    [RelayCommand]
    private async Task LoadAutoModConfigAsync()
    {
        try
        {
            var rules = await _autoModRepository.LoadAsync();
            PopulateAutoMod(rules);
            RefreshAutoModPlatformStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading auto-mod rules");
            StatusMessage = $"Error loading auto-mod rules: {ex.Message}";
        }
    }

    /// <summary>
    /// Normalizes the card state, persists it and applies it to the shared
    /// engine (chat pipelines evaluate against the new rules immediately).
    /// </summary>
    [RelayCommand]
    private async Task SaveAutoModConfigAsync()
    {
        try
        {
            IsBusy = true;

            var rules = _autoModRules ?? ModerationRuleSet.CreateDefault();
            rules.MasterEnabled = AutoModEnabled;
            rules.Platforms = SplitList(AutoModPlatformsText);

            foreach (var row in AutoModRules)
            {
                row.ApplyToConfig();
            }

            // The card keeps exemptions uniform across all six rules.
            foreach (var config in new[]
                     {
                         rules.BannedWords, rules.Links, rules.Caps,
                         rules.Emojis, rules.RepeatedMessage, rules.AsciiArt
                     })
            {
                config.ExemptModerators = AutoModExemptModerators;
                config.ExemptVips = AutoModExemptVips;
                config.ExemptSubscribers = AutoModExemptSubscribers;
            }

            rules.Escalation = new ModerationEscalationConfig
            {
                Enabled = EscalationEnabled,
                StrikesToTimeout = EscalationStrikesToTimeout,
                StrikesToBan = EscalationStrikesToBan,
                StrikeWindowMinutes = EscalationWindowMinutes,
                BaseTimeoutSeconds = EscalationBaseTimeoutSeconds
            };
            rules.LinkAllowlist = SplitList(LinkAllowlistText);
            rules.BannedWordList = SplitList(BannedWordsText);
            rules.ExemptUsers = SplitList(ExemptUsersText);

            await _autoModRepository.SaveAsync(rules);
            _autoModRules = rules.Clone();

            AutoModStatus = "Auto-mod rules saved — the engine evaluates with them now.";
            _logger.LogInformation("Auto-mod rules saved from the Moderator dashboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving auto-mod rules");
            AutoModStatus = $"Error saving auto-mod rules: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Runs the sample message through the shared engine (as a plain viewer on
    /// "Twitch") and shows which rule would trip — no action is executed.
    /// </summary>
    [RelayCommand]
    private void TestAutoModMessage()
    {
        if (string.IsNullOrWhiteSpace(AutoModTestMessage))
        {
            AutoModTestResult = "Type a sample message to test.";
            return;
        }

        var verdict = ChatModerationEngine.Shared.Evaluate(new ModerationIncomingMessage
        {
            Platform = "Twitch",
            Username = "auto-mod-test",
            Message = AutoModTestMessage.Trim()
        });

        AutoModTestResult = verdict is null
            ? "Clean — no rule triggered."
            : $"{verdict.Rule} → {verdict.Action}" +
              (verdict.Action == ModerationActionKind.Timeout ? $" ({verdict.TimeoutSeconds}s)" : "") +
              $": {verdict.Reason} [strike {verdict.Strikes}]";
    }

    /// <summary>Clears every viewer's repeat + escalation state.</summary>
    [RelayCommand]
    private void ResetAutoModEscalation()
    {
        var tracked = ChatModerationEngine.Shared.TrackedViewerCount;
        ChatModerationEngine.Shared.ResetAll();
        AutoModStatus = $"Escalation state cleared ({tracked} viewer(s) were tracked).";
    }

    private void PopulateAutoMod(ModerationRuleSet rules)
    {
        _autoModRules = rules;

        var rows = new[]
        {
            new AutoModRuleRow(ModerationRuleType.BannedWords, "Banned words",
                "Words/phrases from the banned list (word allowlist overrides; phrases match as substrings).",
                "—", "—", rules.BannedWords),
            new AutoModRuleRow(ModerationRuleType.Links, "Links",
                "Links whose domain is not on the allowlist; threshold = tolerated link count.",
                "Max links", "—", rules.Links),
            new AutoModRuleRow(ModerationRuleType.Caps, "Excessive caps",
                "Upper-case share of the message's letters; short messages are ignored.",
                "Max caps %", "Min letters", rules.Caps),
            new AutoModRuleRow(ModerationRuleType.Emojis, "Emojis",
                "Emoji count in one message (unicode emoji, symbols and arrows).",
                "Max emojis", "—", rules.Emojis),
            new AutoModRuleRow(ModerationRuleType.RepeatedMessage, "Repeated message",
                "The same message repeated by the same viewer within the window.",
                "Repeats", "Window (s)", rules.RepeatedMessage),
            new AutoModRuleRow(ModerationRuleType.AsciiArt, "ASCII art",
                "Box-drawing / block / geometric-shape character share of the message.",
                "Max art %", "Min length", rules.AsciiArt)
        };

        var platformLines = ModerationPlatformParity.Describe(_autoModActions).ToList();

        RunOnUi(() =>
        {
            AutoModEnabled = rules.MasterEnabled;
            AutoModExemptModerators = rules.Caps.ExemptModerators;
            AutoModExemptVips = rules.Caps.ExemptVips;
            AutoModExemptSubscribers = rules.Caps.ExemptSubscribers;
            AutoModPlatformsText = string.Join(", ", rules.Platforms);

            EscalationEnabled = rules.Escalation.Enabled;
            EscalationStrikesToTimeout = rules.Escalation.StrikesToTimeout;
            EscalationStrikesToBan = rules.Escalation.StrikesToBan;
            EscalationWindowMinutes = rules.Escalation.StrikeWindowMinutes;
            EscalationBaseTimeoutSeconds = rules.Escalation.BaseTimeoutSeconds;

            LinkAllowlistText = string.Join(Environment.NewLine, rules.LinkAllowlist);
            BannedWordsText = string.Join(Environment.NewLine, rules.BannedWordList);
            ExemptUsersText = string.Join(Environment.NewLine, rules.ExemptUsers);

            AutoModRules.Clear();
            foreach (var row in rows)
            {
                AutoModRules.Add(row);
            }

            AutoModPlatformStatus.Clear();
            foreach (var line in platformLines)
            {
                AutoModPlatformStatus.Add(line);
            }
        });
    }

    private void RefreshAutoModPlatformStatus()
    {
        var lines = ModerationPlatformParity.Describe(_autoModActions).ToList();
        RunOnUi(() =>
        {
            AutoModPlatformStatus.Clear();
            foreach (var line in lines)
            {
                AutoModPlatformStatus.Add(line);
            }
        });
    }

    /// <summary>Splits a textbox blob into clean list entries (newline or comma separated).</summary>
    private static List<string> SplitList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ======================================================================
    // Section 5 — Moderator command workflows (Flow editor)
    // ======================================================================

    /// <summary>Raised when the page should open the Flow editor overlay (the view hosts it).</summary>
    public event Action? FlowEditorRequested;

    public ObservableCollection<ModCommandWorkflowRow> ModCommandWorkflows { get; } = new();

    [ObservableProperty]
    private string? _modCommandStatus;

    /// <summary>
    /// "Add moderator command": asks the view to open the visual Flow editor
    /// overlay on the mod_command preset (verb + minimum tier pre-filled).
    /// </summary>
    [RelayCommand]
    private void OpenModeratorCommandEditor() => FlowEditorRequested?.Invoke();

    /// <summary>
    /// Reloads the mod_command workflow rows from the Workflows table (page
    /// init and every editor-overlay close).
    /// </summary>
    public async Task LoadModCommandWorkflowsAsync()
    {
        try
        {
            var rows = await _dataService.GetWorkflowsAsync();
            var modCommands = rows
                .Select(WorkflowJson.FromRow)
                .Where(w => string.Equals(w.Trigger.Type, WorkflowTriggerTypes.ModCommand, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(w => w.UpdatedAt)
                .ToList();

            RunOnUi(() =>
            {
                ModCommandWorkflows.Clear();
                foreach (var workflow in modCommands)
                {
                    ModCommandWorkflows.Add(new ModCommandWorkflowRow(workflow));
                }

                ModCommandStatus = ModCommandWorkflows.Count == 0
                    ? "No moderator command workflows yet — add one above (verb, tier and node logic live in the Flow editor)."
                    : null;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mod_command workflows");
            ModCommandStatus = $"Error loading moderator commands: {ex.Message}";
        }
    }

    /// <summary>
    /// Row toggle: persists Enabled and reloads the trigger service — the same
    /// path the Automation page's toggle uses; a failure rolls the box back.
    /// </summary>
    [RelayCommand]
    public async Task ToggleModCommandWorkflowAsync(ModCommandWorkflowRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            row.Workflow.Enabled = row.IsEnabled;
            row.Workflow.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveWorkflowAsync(WorkflowJson.ToRow(row.Workflow));
            await _triggerService.ReloadAsync();
            StatusMessage = row.IsEnabled
                ? $"\"{row.Workflow.Name}\" armed — {row.CommandLabel} is live."
                : $"\"{row.Workflow.Name}\" disarmed — it will not fire until re-enabled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle mod_command workflow {WorkflowId}", row.Id);
            ModCommandStatus = $"Could not save the toggle: {ex.Message}";
            row.IsEnabled = !row.IsEnabled; // roll the checkbox back
        }
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
            await LoadAutoModConfigCommand.ExecuteAsync(null);
            await LoadModCommandWorkflowsAsync();
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
