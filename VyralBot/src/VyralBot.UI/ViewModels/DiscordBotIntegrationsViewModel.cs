using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VyralBot.Integrations.Discord;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Quick-add preset for a well-known external bot service. "Other" carries a
/// null <see cref="WebsiteUrl"/> and means the user fills in name + URL.
/// </summary>
public sealed record IntegrationPreset(string Name, string? WebsiteUrl);

/// <summary>
/// One registered external bot service shown as a row in the Integrations card
/// (e.g. BotGhost with the commands the user says it runs).
/// </summary>
public sealed class IntegrationItemViewModel
{
    public IntegrationItemViewModel(string name, string websiteUrl, IReadOnlyList<string> commandNames)
    {
        Name = name;
        WebsiteUrl = websiteUrl;
        CommandNames = commandNames;
    }

    /// <summary>Display name, e.g. "BotGhost".</summary>
    public string Name { get; }

    /// <summary>Dashboard website URL used by OPEN SITE ("" when none saved).</summary>
    public string WebsiteUrl { get; }

    /// <summary>Command names the user registered, as entered (e.g. "!rank").</summary>
    public IReadOnlyList<string> CommandNames { get; }

    public bool HasWebsite => !string.IsNullOrWhiteSpace(WebsiteUrl);

    public string CommandCountText => CommandNames.Count == 1 ? "1 command" : $"{CommandNames.Count} commands";

    public string CommandListText => CommandNames.Count == 0
        ? "No commands registered yet — EDIT to add the commands this service runs"
        : string.Join(", ", CommandNames);
}

/// <summary>
/// One row of the conflicts panel: a command name registered to an external
/// service that matches something Vyral runs for this bot — either a built-in
/// moderation command (cannot be disabled in V1) or a custom Command row
/// (can be disabled/re-enabled from here).
/// </summary>
public partial class ConflictRowViewModel : ObservableObject
{
    /// <summary>The command name as registered with the external service, e.g. "!rank".</summary>
    public string CommandDisplay { get; init; } = "";

    /// <summary>The external service the command was registered from.</summary>
    public string ServiceName { get; init; } = "";

    /// <summary>True when the clash is with a built-in moderation command, not a custom command.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Describes the Vyral side, e.g. "Vyral custom command 'rank' (all bots)".</summary>
    public string VyralTargetText { get; init; } = "";

    /// <summary>Id of the matching custom Command row; null for built-in clashes.</summary>
    public string? CustomCommandId { get; init; }

    /// <summary>Explains why a built-in clash cannot be turned off in V1.</summary>
    public string BuiltInStatusText { get; init; } = "";

    [ObservableProperty]
    private bool _canDisable;

    [ObservableProperty]
    private bool _canReenable;
}

/// <summary>
/// View model behind the per-bot "Integrations" card
/// (<see cref="VyralBot.UI.Views.Controls.DiscordBotIntegrationsCard"/>). The
/// user registers external bot services (BotGhost, MEE6, Dyno, Carl-bot or a
/// custom one), opens their dashboard to sign in, and lists the command names
/// those services run. Any name that matches a Vyral custom command for this
/// bot (or an unscoped "all bots" command) or a built-in moderation command is
/// shown as a conflict that can be disabled on the Vyral side.
/// </summary>
/// <remarks>
/// <para>
/// Honest V1 scope: BotGhost/MEE6 &amp; co. expose no public API to read a
/// user's command list, so the user enters the names manually. Storage is two
/// Configs rows per bot via <see cref="IDataService"/>:
/// "DiscordBot.{botId}.Integrations" = JSON array of
/// {Name, WebsiteUrl, CommandNames[]} and "DiscordBot.{botId}.DisabledBuiltIns"
/// = JSON array of built-in command names registered as disabled.
/// </para>
/// <para>
/// The DisabledBuiltIns list is REGISTERED STORAGE ONLY in V1 — nothing reads
/// it at runtime yet. Teaching <c>DiscordModCommandHandler</c> to consult it
/// per bot is a follow-up task.
/// </para>
/// </remarks>
public partial class DiscordBotIntegrationsViewModel : ViewModelBase
{
    /// <summary>
    /// Built-in moderation command names claimed by
    /// <c>DiscordModCommandHandler</c> (kick, ban, unban, mute, unmute,
    /// slowmode, purge/clear, help) — used for conflict matching.
    /// </summary>
    public static readonly string[] BuiltInModerationCommandNames =
    {
        "kick", "ban", "unban", "mute", "unmute", "slowmode", "purge", "clear", "help"
    };

    /// <summary>Quick-add presets; "Other" (null URL) means custom name + URL.</summary>
    public static readonly IReadOnlyList<IntegrationPreset> Presets = new[]
    {
        new IntegrationPreset("BotGhost", "https://botghost.com/dashboard"),
        new IntegrationPreset("MEE6", "https://mee6.xyz/dashboard"),
        new IntegrationPreset("Dyno", "https://dyno.gg/dashboard"),
        new IntegrationPreset("Carl-bot", "https://carl.gg/dashboard"),
        new IntegrationPreset("Other", null)
    };

    private readonly ILogger _logger;
    private readonly IDataService _dataService;

    private List<IntegrationRecord> _records = new();
    private List<Command> _customCommands = new();
    private HashSet<string> _disabledBuiltIns = new(StringComparer.OrdinalIgnoreCase);
    private IntegrationRecord? _editingRecord;

    public DiscordBotIntegrationsViewModel(ILogger<DiscordBotIntegrationsViewModel>? logger, IDataService dataService)
    {
        _logger = logger ?? NullLogger<DiscordBotIntegrationsViewModel>.Instance;
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    [ObservableProperty]
    private string _botId = "";

    /// <summary>Flips true after the first successful load; the card is hidden until then.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    public ObservableCollection<IntegrationItemViewModel> Integrations { get; } = new();

    public ObservableCollection<ConflictRowViewModel> Conflicts { get; } = new();

    // --- Auto-detect state (seen bots + help-command capture) ------------------

    /// <summary>Bots detected in this bot's servers (from the gateway message stream).</summary>
    public ObservableCollection<DiscordSeenBot> DetectedBots { get; } = new();

    [ObservableProperty]
    private DiscordSeenBot? _selectedDetectedBot;

    /// <summary>The command sent to the external bot to make it list its commands.</summary>
    [ObservableProperty]
    private string _discoveryCommand = "!help";

    [ObservableProperty]
    private bool _hasDetectedBots;

    [ObservableProperty]
    private string _detectStatus = "";

    [ObservableProperty]
    private bool _hasIntegrations;

    [ObservableProperty]
    private bool _hasConflicts;

    // --- Add / edit form state -------------------------------------------------

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isAddMode;

    [ObservableProperty]
    private string _formHeaderText = "Add integration";

    [ObservableProperty]
    private IntegrationPreset? _selectedPreset;

    [ObservableProperty]
    private string _editName = "";

    [ObservableProperty]
    private string _editUrl = "";

    [ObservableProperty]
    private string _editCommands = "";

    /// <summary>Last save/load/error message shown at the bottom of the card.</summary>
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _showEmptyIntegrationsHint = true;

    [ObservableProperty]
    private bool _showNoConflictsHint;

    /// <summary>Presets bound to the add-form picker.</summary>
    public IReadOnlyList<IntegrationPreset> AvailablePresets => Presets;

    partial void OnSelectedPresetChanged(IntegrationPreset? value)
    {
        // Prefill name + URL for known services; "Other" leaves both for the user.
        if (value?.WebsiteUrl is { } url)
        {
            EditName = value.Name;
            EditUrl = url;
        }
    }

    partial void OnIsEditingChanged(bool value) => UpdateCompositeVisibility();

    partial void OnHasIntegrationsChanged(bool value) => UpdateCompositeVisibility();

    partial void OnHasConflictsChanged(bool value) => UpdateCompositeVisibility();

    private void UpdateCompositeVisibility()
    {
        ShowEmptyIntegrationsHint = !IsEditing && !HasIntegrations;
        ShowNoConflictsHint = HasIntegrations && !HasConflicts;
    }

    /// <summary>
    /// Loads the registered integrations, the disabled built-ins list and this
    /// bot's custom commands, then recomputes the conflict view. Safe to call
    /// repeatedly (the hosting view reloads on every navigation).
    /// </summary>
    public async Task LoadAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
            return;

        BotId = botId;
        IsBusy = true;
        try
        {
            await LoadIntegrationsAsync();
            await LoadDisabledBuiltInsAsync();
            await LoadCustomCommandsAsync();
            RecomputeConflicts();
            IsLoaded = true;
            StatusText = "";
            RefreshDetectedBots();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading integrations for bot {BotId}", BotId);
            StatusText = $"Could not load integrations: {ex.Message}";
            IsLoaded = true; // still reveal the card so the error is visible
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BeginAdd()
    {
        IsAddMode = true;
        FormHeaderText = "Add integration";
        _editingRecord = null;
        SelectedPreset = Presets[0]; // prefills name + dashboard URL
        EditCommands = "";
        IsEditing = true;
        StatusText = "";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        _editingRecord = null;
        StatusText = "";
    }

    [RelayCommand]
    private async Task SaveIntegrationAsync()
    {
        if (IsBusy)
            return;

        var name = EditName.Trim();
        if (name.Length == 0)
        {
            StatusText = "Enter a name for the integration.";
            return;
        }

        var isDuplicate = _records.Any(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
            !ReferenceEquals(r, _editingRecord));
        if (isDuplicate)
        {
            StatusText = $"'{name}' is already registered for this bot.";
            return;
        }

        var commands = ParseCommandNames(EditCommands);
        var url = NormalizeWebsiteUrl(EditUrl);

        var record = _editingRecord ?? new IntegrationRecord();
        record.Name = name;
        record.WebsiteUrl = url;
        record.CommandNames = commands;
        if (_editingRecord is null)
            _records.Add(record);

        try
        {
            IsBusy = true;
            await _dataService.SetConfigValueAsync(
                IntegrationsConfigKey(BotId), JsonSerializer.Serialize(_records));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving integrations for bot {BotId}", BotId);
            StatusText = $"Could not save: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        RebuildIntegrationRows();
        RecomputeConflicts();
        IsEditing = false;
        _editingRecord = null;
        StatusText = commands.Count == 0
            ? $"Saved '{name}' (no commands yet — edit it to list the commands it runs)."
            : $"Saved '{name}' with {commands.Count} command(s).";
    }

    [RelayCommand]
    private void EditIntegration(IntegrationItemViewModel? item)
    {
        if (item is null)
            return;

        var record = _records.FirstOrDefault(r =>
            string.Equals(r.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return;

        IsAddMode = false;
        FormHeaderText = "Edit integration";
        _editingRecord = record;

        // Select the matching preset when name + URL match one, else "Other".
        SelectedPreset = Presets.FirstOrDefault(p =>
            string.Equals(p.Name, item.Name, StringComparison.Ordinal) &&
            string.Equals(p.WebsiteUrl ?? "", item.WebsiteUrl ?? "", StringComparison.Ordinal))
            ?? Presets.First(p => p.WebsiteUrl is null);

        EditName = record.Name;
        EditUrl = record.WebsiteUrl;
        EditCommands = string.Join(Environment.NewLine, record.CommandNames);
        IsEditing = true;
        StatusText = "";
    }

    [RelayCommand]
    private async Task RemoveIntegrationAsync(IntegrationItemViewModel? item)
    {
        if (IsBusy || item is null)
            return;

        var record = _records.FirstOrDefault(r =>
            string.Equals(r.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return;

        _records.Remove(record);
        try
        {
            IsBusy = true;
            await _dataService.SetConfigValueAsync(
                IntegrationsConfigKey(BotId), JsonSerializer.Serialize(_records));
        }
        catch (Exception ex)
        {
            _records.Add(record); // rollback the in-memory removal
            _logger.LogError(ex, "Error removing integration '{Name}' for bot {BotId}", item.Name, BotId);
            StatusText = $"Could not remove '{item.Name}': {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (ReferenceEquals(_editingRecord, record))
            CancelEdit();

        RebuildIntegrationRows();
        RecomputeConflicts();
        StatusText = $"Removed '{item.Name}'.";
    }

    [RelayCommand]
    private void OpenSite(IntegrationItemViewModel? item)
    {
        if (item is null)
            return;

        if (string.IsNullOrWhiteSpace(item.WebsiteUrl))
        {
            StatusText = $"No website saved for '{item.Name}' — EDIT the integration to add one.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = item.WebsiteUrl, UseShellExecute = true });
            StatusText = $"Opened {item.WebsiteUrl} — sign in, then list the commands '{item.Name}' runs.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open integration site {Url}", item.WebsiteUrl);
            StatusText = $"Could not open {item.WebsiteUrl}.";
        }
    }

    [RelayCommand]
    private async Task DisableInVyralAsync(ConflictRowViewModel? row)
    {
        if (row?.CustomCommandId is null || IsBusy)
            return;

        var command = _customCommands.FirstOrDefault(c => c.Id == row.CustomCommandId);
        if (command is null)
        {
            StatusText = "That command no longer exists — refreshing the conflict list.";
            await LoadCustomCommandsAsync();
            RecomputeConflicts();
            return;
        }

        command.IsEnabled = false;
        command.UpdatedAt = DateTime.UtcNow;
        try
        {
            IsBusy = true;
            await _dataService.SaveCommandAsync(command);
        }
        catch (Exception ex)
        {
            command.IsEnabled = true; // rollback
            _logger.LogError(ex, "Error disabling command {CommandName} for bot {BotId}", command.Name, BotId);
            StatusText = $"Could not disable '{command.Name}': {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        RecomputeConflicts();
        StatusText = $"Disabled Vyral command '{command.Name}' — the external service can own it now.";
    }

    [RelayCommand]
    private async Task ReenableInVyralAsync(ConflictRowViewModel? row)
    {
        if (row?.CustomCommandId is null || IsBusy)
            return;

        var command = _customCommands.FirstOrDefault(c => c.Id == row.CustomCommandId);
        if (command is null)
        {
            StatusText = "That command no longer exists — refreshing the conflict list.";
            await LoadCustomCommandsAsync();
            RecomputeConflicts();
            return;
        }

        command.IsEnabled = true;
        command.UpdatedAt = DateTime.UtcNow;
        try
        {
            IsBusy = true;
            await _dataService.SaveCommandAsync(command);
        }
        catch (Exception ex)
        {
            command.IsEnabled = false; // rollback
            _logger.LogError(ex, "Error re-enabling command {CommandName} for bot {BotId}", command.Name, BotId);
            StatusText = $"Could not re-enable '{command.Name}': {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        RecomputeConflicts();
        StatusText = $"Re-enabled Vyral command '{command.Name}'.";
    }

    /// <summary>
    /// Registers a built-in moderation command name (e.g. "ban") as disabled for
    /// this bot. V1 STORAGE ONLY: the list is persisted under
    /// "DiscordBot.{botId}.DisabledBuiltIns" but nothing enforces it at runtime
    /// yet — <c>DiscordModCommandHandler</c> must learn to read it per bot.
    /// No UI calls this yet; it exists so enforcement and a future toggle have
    /// a ready writer.
    /// </summary>
    public async Task DisableBuiltInAsync(string commandName)
    {
        var normalized = NormalizeCommandName(commandName);
        if (normalized.Length == 0 || !IsBuiltInName(normalized) || !_disabledBuiltIns.Add(normalized))
            return;

        await SaveDisabledBuiltInsAsync();
        RecomputeConflicts();
    }

    /// <summary>Undoes <see cref="DisableBuiltInAsync"/> (storage only in V1).</summary>
    public async Task ReenableBuiltInAsync(string commandName)
    {
        if (!_disabledBuiltIns.Remove(NormalizeCommandName(commandName)))
            return;

        await SaveDisabledBuiltInsAsync();
        RecomputeConflicts();
    }

    // --- Loading helpers -------------------------------------------------------

    private async Task LoadIntegrationsAsync()
    {
        var json = await _dataService.GetConfigValueAsync(IntegrationsConfigKey(BotId));
        _records = new List<IntegrationRecord>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                _records = JsonSerializer.Deserialize<List<IntegrationRecord>>(json) ?? new List<IntegrationRecord>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Ignoring malformed integrations JSON for bot {BotId}", BotId);
                StatusText = "Saved integrations could not be read — starting fresh.";
            }
        }

        RebuildIntegrationRows();
    }

    private async Task LoadDisabledBuiltInsAsync()
    {
        _disabledBuiltIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var json = await _dataService.GetConfigValueAsync(DisabledBuiltInsConfigKey(BotId));
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(json);
            if (names is null)
                return;

            foreach (var name in names)
            {
                var normalized = NormalizeCommandName(name ?? "");
                if (normalized.Length > 0)
                    _disabledBuiltIns.Add(normalized);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring malformed disabled built-ins JSON for bot {BotId}", BotId);
        }
    }

    private async Task SaveDisabledBuiltInsAsync()
    {
        var json = JsonSerializer.Serialize(_disabledBuiltIns
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList());
        await _dataService.SetConfigValueAsync(DisabledBuiltInsConfigKey(BotId), json);
    }

    private async Task LoadCustomCommandsAsync()
    {
        var all = await _dataService.GetCommandsAsync();
        _customCommands = all
            .Where(c => !c.IsBuiltIn)
            .Where(c => string.IsNullOrEmpty(c.BotId) ||
                        string.Equals(c.BotId, BotId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void RebuildIntegrationRows()
    {
        Integrations.Clear();
        foreach (var record in _records)
            Integrations.Add(new IntegrationItemViewModel(record.Name, record.WebsiteUrl, record.CommandNames));
        HasIntegrations = Integrations.Count > 0;
    }

    /// <summary>
    /// Rebuilds the conflicts panel: every registered external command name that
    /// matches a built-in moderation name or one of this bot's (or an
    /// unscoped "all bots") custom commands becomes a row.
    /// </summary>
    private void RecomputeConflicts()
    {
        Conflicts.Clear();

        var customByName = new Dictionary<string, List<Command>>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in _customCommands)
        {
            var key = NormalizeCommandName(command.Name);
            if (key.Length == 0)
                continue;

            if (!customByName.TryGetValue(key, out var matches))
                customByName[key] = matches = new List<Command>();
            matches.Add(command);
        }

        foreach (var record in _records)
        {
            foreach (var raw in record.CommandNames)
            {
                var normalized = NormalizeCommandName(raw);
                if (normalized.Length == 0)
                    continue;

                if (IsBuiltInName(normalized))
                {
                    Conflicts.Add(new ConflictRowViewModel
                    {
                        CommandDisplay = raw,
                        ServiceName = record.Name,
                        IsBuiltIn = true,
                        VyralTargetText = "Vyral built-in moderation command",
                        BuiltInStatusText = _disabledBuiltIns.Contains(normalized)
                            ? "Registered disabled — runtime enforcement pending (V1)"
                            : "Built-in — cannot disable yet (V1)"
                    });
                }

                if (customByName.TryGetValue(normalized, out var commands))
                {
                    foreach (var command in commands)
                    {
                        Conflicts.Add(new ConflictRowViewModel
                        {
                            CommandDisplay = raw,
                            ServiceName = record.Name,
                            IsBuiltIn = false,
                            VyralTargetText = command.BotId is null
                                ? $"Vyral custom command '{command.Name}' (all bots)"
                                : $"Vyral custom command '{command.Name}' (this bot)",
                            CustomCommandId = command.Id,
                            CanDisable = command.IsEnabled,
                            CanReenable = !command.IsEnabled
                        });
                    }
                }
            }
        }

        HasConflicts = Conflicts.Count > 0;
    }

    // --- Static helpers --------------------------------------------------------

    private static string IntegrationsConfigKey(string botId) => $"DiscordBot.{botId}.Integrations";

    private static string DisabledBuiltInsConfigKey(string botId) => $"DiscordBot.{botId}.DisabledBuiltIns";

    private static bool IsBuiltInName(string normalizedCommandName) =>
        BuiltInModerationCommandNames.Contains(normalizedCommandName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Strips an optional '!' or '.' prefix (mirroring
    /// <c>DiscordModCommandHandler.Tokenize</c>) and lowercases, so "!Rank",
    /// ".rank" and "rank" all compare equal.
    /// </summary>
    internal static string NormalizeCommandName(string? raw)
    {
        var name = (raw ?? "").Trim();
        while (name.Length > 0 && (name[0] == '!' || name[0] == '.'))
            name = name[1..].Trim();
        return name.ToLowerInvariant();
    }

    /// <summary>Splits free-text command input on commas and newlines; dedupes case-insensitively.</summary>
    internal static List<string> ParseCommandNames(string text)
    {
        return text
            .Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeWebsiteUrl(string url)
    {
        var trimmed = (url ?? "").Trim();
        if (trimmed.Length == 0)
            return "";

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "https://" + trimmed;

        return trimmed;
    }

    /// <summary>JSON shape persisted under "DiscordBot.{botId}.Integrations".</summary>
    // --- Auto-detect: seen bots + help-command capture -------------------------

    private IDiscordService? ResolveBotService()
    {
        try
        {
            var manager = App.ServiceProvider?.GetService(typeof(IDiscordBotManager)) as IDiscordBotManager;
            return manager?.Get(BotId);
        }
        catch
        {
            return null;
        }
    }

    private void RefreshDetectedBots()
    {
        DetectedBots.Clear();
        var service = ResolveBotService();
        if (service?.IsConnected != true)
        {
            HasDetectedBots = false;
            return;
        }

        foreach (var bot in service.SeenBots.Take(30))
        {
            DetectedBots.Add(bot);
        }

        HasDetectedBots = DetectedBots.Count > 0;
        SelectedDetectedBot ??= DetectedBots.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshDetected()
    {
        RefreshDetectedBots();
        DetectStatus = HasDetectedBots
            ? ""
            : "No other bots detected yet — they appear here once they send a message in a server this bot is in.";
    }

    [RelayCommand]
    private async Task DetectCommandsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var service = ResolveBotService();
        if (service?.IsConnected != true)
        {
            DetectStatus = "This bot is not connected.";
            return;
        }

        var target = SelectedDetectedBot;
        if (target == null)
        {
            DetectStatus = "Pick a detected bot first.";
            return;
        }

        var channelId = service.SelectedChannelId;
        if (string.IsNullOrEmpty(channelId))
        {
            DetectStatus = "Set an active channel for this bot first (Servers card above).";
            return;
        }

        var command = string.IsNullOrWhiteSpace(DiscoveryCommand) ? "!help" : DiscoveryCommand.Trim();
        IsBusy = true;
        DetectStatus = $"Sending {command} and waiting for {target.Username} to reply (up to 20s)...";
        try
        {
            var reply = await service.SendAndCaptureReplyAsync(channelId, command, target.BotId, TimeSpan.FromSeconds(20));
            if (reply == null)
            {
                DetectStatus = $"No reply captured from {target.Username}. Try a different discovery command (its help/prefix command).";
                return;
            }

            var names = VyralBot.Integrations.Discord.DiscordCommandNameParser.ParseCommandNames(reply);
            if (names.Count == 0)
            {
                DetectStatus = $"{target.Username} replied, but no command names could be parsed from it. Paste them manually below.";
                return;
            }

            // Pre-fill the add/edit commands box with the discovered names
            EditCommands = string.Join(Environment.NewLine, names.Select(n => "!" + n));
            if (_editingRecord != null)
            {
                _editingRecord.CommandNames = names.ToList();
            }

            DetectStatus = $"Detected {names.Count} commands from {target.Username} — review them in the commands box and save.";
        }
        catch (Exception ex)
        {
            DetectStatus = $"Detection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }


    private sealed class IntegrationRecord
    {
        public string Name { get; set; } = "";
        public string WebsiteUrl { get; set; } = "";
        public List<string> CommandNames { get; set; } = new();
    }
}
