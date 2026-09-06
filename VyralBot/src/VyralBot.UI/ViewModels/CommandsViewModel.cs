using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using VyralBot.Core.Services.Commands;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.UI.Services;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One entry in the "Bot scope" selector on the Commands page: either the
/// all-bots view (null id) or one configured Discord bot. Commands pinned to
/// the selected bot plus unscoped ("all bots") commands are shown when a bot
/// is selected.
/// </summary>
public partial class DiscordBotScopeOption : ObservableObject
{
    public string? Id { get; }

    public string Name { get; }

    public bool IsAllBots => string.IsNullOrEmpty(Id);

    public DiscordBotScopeOption(string? id, string name)
    {
        Id = string.IsNullOrEmpty(id) ? null : id;
        Name = name;
    }

    public override string ToString() => Name;
}

/// <summary>
/// Commands page view model: the command LIST (built-in + custom), the bot
/// scope bar, enable/platform toggles, usage stats and the timers tab. Every
/// EDIT and NEW COMMAND opens the visual flow editor (FlowEditorView overlay,
/// hosted by <see cref="Views.CommandsView"/>) — the old form-based builder
/// was retired: saving a seeded flow ADOPTS the command row (the flow editor
/// disables it), so this page stays the management surface for the list.
/// </summary>
public partial class CommandsViewModel : ViewModelBase
{
    private readonly ILogger<CommandsViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IDiscordBotManager? _discordBotManager;
    private readonly IDataService? _dataService;

    /// <summary>
    /// Trigger-service reload after deleting a flow that adopted a command, so
    /// the removed flow stops firing immediately (null when DI skipped it).
    /// </summary>
    private readonly WorkflowTriggerService? _triggerService;

    // Master lists (unfiltered); the observable collections mirror these
    // filtered by the selected bot scope.
    private List<Command> _allBuiltIn = new();
    private List<Command> _allCustom = new();

    [ObservableProperty]
    private ObservableCollection<Command> _builtInCommands = new();

    [ObservableProperty]
    private ObservableCollection<Command> _customCommands = new();

    [ObservableProperty]
    private Command? _selectedCommand;

    /// <summary>Retained for the delete flow (was the form builder's visibility flag).</summary>
    [ObservableProperty]
    private bool _isEditingCommand;

    [ObservableProperty]
    private string _statusMessage = "";

    // --- Bot scope state -------------------------------------------------

    /// <summary>Bot-name lookup shared with the grid converters (singleton VM).</summary>
    internal static readonly Dictionary<string, string> BotNamesById = new(StringComparer.Ordinal);

    /// <summary>
    /// Ids of Command rows currently adopted by a flow
    /// (<see cref="Workflow.SourceCommandId"/>), shared with the grid tooltip
    /// converter (singleton VM, same pattern as <see cref="BotNamesById"/>).
    /// </summary>
    internal static readonly HashSet<string> FlowManagedCommandIds = new(StringComparer.Ordinal);

    [ObservableProperty]
    private bool _hasDiscordBots;

    [ObservableProperty]
    private DiscordBotScopeOption? _selectedBotScope;

    /// <summary>True while a specific bot (not "All bots") is selected.</summary>
    [ObservableProperty]
    private bool _isSpecificBotScope;

    /// <summary>Explains what the current scope shows ("All bots" / bot name).</summary>
    [ObservableProperty]
    private string _scopeHint = "Showing commands for every bot and platform.";

    // --- Timers tab state --------------------------------------------------

    [ObservableProperty]
    private ObservableCollection<CommandTimer> _timers = new();

    [ObservableProperty]
    private CommandTimer? _selectedTimer;

    [ObservableProperty]
    private bool _isEditingTimer;

    /// <summary>True while the timer form edits an EXISTING timer (Delete available).</summary>
    [ObservableProperty]
    private bool _isCustomTimerEdit;

    [ObservableProperty]
    private string _timerName = "";

    [ObservableProperty]
    private string _timerCommandName = "";

    [ObservableProperty]
    private int _timerIntervalMinutes = 30;

    [ObservableProperty]
    private string _timerTargetPlatform = "Twitch";

    [ObservableProperty]
    private DiscordBotScopeOption? _selectedTimerBot;

    [ObservableProperty]
    private bool _timerIsEnabled = true;

    /// <summary>Timer form header: "New timer" or "Editing X".</summary>
    [ObservableProperty]
    private string _timerEditHeader = "New timer";

    /// <summary>Bot dropdown is relevant: Discord picked AND bots are configured.</summary>
    [ObservableProperty]
    private bool _showTimerBotPicker;

    /// <summary>Scope selector entries: "All bots" first, then one per configured bot.</summary>
    public ObservableCollection<DiscordBotScopeOption> BotScopes { get; } = new();

    /// <summary>Timer platform choices.</summary>
    public ObservableCollection<string> TimerPlatforms { get; } = new() { "Twitch", "Kick", "Discord" };

    /// <summary>Every enabled command name (built-in + custom) a timer can fire.</summary>
    public ObservableCollection<string> TimerCommandNames { get; } = new();

    /// <summary>Discord send target for timers: "Primary bot" plus each configured bot.</summary>
    public ObservableCollection<DiscordBotScopeOption> TimerBotOptions { get; } = new();

    public CommandsViewModel(
        ILogger<CommandsViewModel> logger,
        ICommandService commandService,
        IDiscordBotManager? discordBotManager = null,
        IDataService? dataService = null,
        WorkflowTriggerService? triggerService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _discordBotManager = discordBotManager;
        _dataService = dataService;
        _triggerService = triggerService;

        if (_discordBotManager != null)
        {
            // Raised from background threads when bots are added/removed or
            // change state — hop to the UI thread to refresh the scope list.
            _discordBotManager.BotsChanged += OnBotsChanged;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadCommandsAsync();
            await RefreshBotsAsync();
            await LoadTimersAsync();
            _logger.LogInformation("CommandsViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CommandsViewModel");
            StatusMessage = "Failed to load the command list";
        }
    }

    private void OnBotsChanged()
    {
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            return;
        }

        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await RefreshBotsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error refreshing bot scopes after change");
            }
        });
    }

    // ----------------------------------------------------------------------
    // Loading
    // ----------------------------------------------------------------------

    [RelayCommand]
    public async Task LoadCommandsAsync()
    {
        try
        {
            var allCommands = await _commandService.GetAllCommandsAsync();

            _allBuiltIn = allCommands.Where(c => c.IsBuiltIn).OrderBy(c => c.Name).ToList();
            _allCustom = allCommands.Where(c => !c.IsBuiltIn).OrderBy(c => c.Name).ToList();

            ApplyBotScope();
            await RefreshFlowManagedIdsAsync();

            // Timers can fire any ENABLED command (built-in or custom) —
            // refresh the picker list whenever the command set changes.
            // Disabled commands (including flow-adopted rows, which the flow
            // editor disables) can never fire, so keep them out of the list.
            TimerCommandNames.Clear();
            foreach (var name in allCommands
                .Where(c => c.IsEnabled)
                .Select(c => c.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                TimerCommandNames.Add(name);
            }

            _logger.LogInformation("Loaded {BuiltInCount} built-in and {CustomCount} custom commands",
                _allBuiltIn.Count, _allCustom.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load commands");
            StatusMessage = "Failed to load commands";
        }
    }

    /// <summary>
    /// Refreshes the set of Command row ids currently adopted by a flow (first
    /// match on Workflow.SourceCommandId). Drives the "managed by flow" name
    /// tooltips and the flow-aware delete. Best-effort: without a data service
    /// (or on failure) the set simply stays empty.
    /// </summary>
    private async Task RefreshFlowManagedIdsAsync()
    {
        if (_dataService == null)
        {
            return;
        }

        try
        {
            var managedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in await _dataService.GetWorkflowsAsync())
            {
                var flow = WorkflowJson.FromRow(row);
                if (!string.IsNullOrWhiteSpace(flow.SourceCommandId))
                {
                    managedIds.Add(flow.SourceCommandId);
                }
            }

            FlowManagedCommandIds.Clear();
            foreach (var id in managedIds)
            {
                FlowManagedCommandIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve flow-adopted commands for the managed hints");
        }
    }

    /// <summary>
    /// Finds the id of the flow that adopted a command row (first match on
    /// Workflow.SourceCommandId), or null when none does.
    /// </summary>
    private async Task<string?> FindAdoptingFlowIdAsync(string commandId)
    {
        if (_dataService == null)
        {
            return null;
        }

        foreach (var row in await _dataService.GetWorkflowsAsync())
        {
            var flow = WorkflowJson.FromRow(row);
            if (string.Equals(flow.SourceCommandId, commandId, StringComparison.Ordinal))
            {
                return row.Id;
            }
        }

        return null;
    }

    /// <summary>Deletes a flow that adopted a command and reloads the trigger service.</summary>
    private async Task DeleteAdoptingFlowAsync(string flowId, string commandName)
    {
        if (_dataService == null)
        {
            return;
        }

        await _dataService.DeleteWorkflowAsync(flowId);
        if (_triggerService != null)
        {
            await _triggerService.ReloadAsync();
        }

        _logger.LogInformation("Deleted flow {FlowId} (was managing {CommandName})", flowId, commandName);
    }

    /// <summary>
    /// Loads the bot scope selector: "All bots" plus one entry per configured
    /// Discord bot. The previous selection survives the refresh.
    /// </summary>
    [RelayCommand]
    public async Task RefreshBotsAsync()
    {
        if (_discordBotManager == null)
        {
            return;
        }

        try
        {
            var bots = await _discordBotManager.LoadBotsAsync();

            var previousId = SelectedBotScope?.Id;

            BotNamesById.Clear();
            foreach (var bot in bots)
            {
                BotNamesById[bot.Id] = bot.Name;
            }

            BotScopes.Clear();
            BotScopes.Add(new DiscordBotScopeOption(null, "All bots"));
            foreach (var bot in bots)
            {
                BotScopes.Add(new DiscordBotScopeOption(bot.Id, bot.Name));
            }

            HasDiscordBots = bots.Count > 0;

            // Restore the previous selection when the bot still exists,
            // otherwise fall back to "All bots".
            SelectedBotScope = BotScopes.FirstOrDefault(s => s.Id == previousId)
                ?? BotScopes.FirstOrDefault(s => s.IsAllBots)
                ?? BotScopes.FirstOrDefault();

            // Timer Discord send targets: Primary bot + each configured bot.
            // Keep the previous pick when the bot still exists.
            var previousTimerBotId = SelectedTimerBot?.Id;
            TimerBotOptions.Clear();
            TimerBotOptions.Add(new DiscordBotScopeOption(null, "Primary bot"));
            foreach (var bot in bots)
            {
                TimerBotOptions.Add(new DiscordBotScopeOption(bot.Id, bot.Name));
            }

            SelectedTimerBot = TimerBotOptions.FirstOrDefault(b => b.Id == previousTimerBotId)
                ?? TimerBotOptions.FirstOrDefault();

            _logger.LogInformation("Loaded {BotCount} Discord bot scope option(s)", bots.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Discord bots for command scoping");
            StatusMessage = "Could not load Discord bots — showing all commands";
        }
    }

    // ----------------------------------------------------------------------
    // Bot scope filtering
    // ----------------------------------------------------------------------

    partial void OnSelectedBotScopeChanged(DiscordBotScopeOption? value)
    {
        IsSpecificBotScope = value is { IsAllBots: false };
        ScopeHint = value == null || value.IsAllBots
            ? "Showing commands for every bot and platform."
            : $"Showing {value.Name}'s commands plus ones marked \"All bots\" — other bots' commands are hidden.";
        ApplyBotScope();
    }

    /// <summary>
    /// Rebuilds the visible lists from the master lists using the current
    /// scope: a specific bot sees its own pinned commands plus unscoped ones.
    /// </summary>
    private void ApplyBotScope()
    {
        var scopeId = SelectedBotScope?.Id;

        BuiltInCommands.Clear();
        foreach (var command in _allBuiltIn.Where(c => IsInScope(c, scopeId)))
        {
            BuiltInCommands.Add(command);
        }

        CustomCommands.Clear();
        foreach (var command in _allCustom.Where(c => IsInScope(c, scopeId)))
        {
            CustomCommands.Add(command);
        }
    }

    private static bool IsInScope(Command command, string? scopeId)
    {
        if (string.IsNullOrEmpty(scopeId))
        {
            return true; // "All bots" view shows everything
        }

        // Unscoped commands ("all bots") plus this bot's pinned ones.
        return string.IsNullOrEmpty(command.BotId)
            || string.Equals(command.BotId, scopeId, StringComparison.Ordinal);
    }

    /// <summary>Display name for a bot id, or "All bots" when unscoped.</summary>
    internal static string BotDisplayName(string? botId)
    {
        if (string.IsNullOrEmpty(botId))
        {
            return "All bots";
        }

        return BotNamesById.TryGetValue(botId, out var name) ? name : botId;
    }

    /// <summary>
    /// Row action: flips a command between "this bot only" and "all bots" for
    /// the selected scope. Only meaningful while a specific bot is selected.
    /// </summary>
    [RelayCommand]
    private async Task ToggleBotScopeAsync(Command? command)
    {
        if (command == null)
        {
            StatusMessage = "No command selected";
            return;
        }

        if (command.IsBuiltIn)
        {
            StatusMessage = "Built-in commands keep their bot scope locked — edit their response instead";
            return;
        }

        var scopeId = SelectedBotScope?.Id;
        if (string.IsNullOrEmpty(scopeId))
        {
            StatusMessage = "Pick a bot in the scope bar first, then flip commands to \"this bot only\"";
            return;
        }

        try
        {
            command.BotId = string.Equals(command.BotId, scopeId, StringComparison.Ordinal)
                ? null
                : scopeId;
            command.UpdatedAt = DateTime.UtcNow;
            await _commandService.UpdateCommandAsync(command);

            StatusMessage = command.BotId == null
                ? $"{command.Name} now runs on every bot"
                : $"{command.Name} now runs only on {BotDisplayName(command.BotId)}";
            _logger.LogInformation("Toggled bot scope for {CommandName} to {BotId}",
                command.Name, command.BotId ?? "(all bots)");

            await LoadCommandsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle bot scope for {CommandName}", command.Name);
            StatusMessage = $"Failed to change bot scope: {ex.Message}";
        }
    }

    /// <summary>Clears the current selection (kept for the delete flow).</summary>
    [RelayCommand]
    private void Close()
    {
        IsEditingCommand = false;
        SelectedCommand = null;
        StatusMessage = "";
    }

    /// <summary>
    /// Row action: deletes a command — and, when a flow adopted it (first
    /// match on Workflow.SourceCommandId), that flow is deleted too so the
    /// command fully goes away. Deleting a built-in that a flow owns removes
    /// ONLY the flow and re-enables the original row instead (built-ins never
    /// vanish); plain built-ins stay blocked as before.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync(Command? command)
    {
        command ??= SelectedCommand;

        if (command == null)
        {
            StatusMessage = "No command selected";
            return;
        }

        try
        {
            string? owningFlowId = await FindAdoptingFlowIdAsync(command.Id);

            if (command.IsBuiltIn)
            {
                // Built-ins never vanish: a flow-managed built-in loses its
                // flow and comes back live; an untouched one stays protected.
                if (owningFlowId == null)
                {
                    StatusMessage = "Built-in commands cannot be deleted";
                    return;
                }

                if (!Confirm($"Delete the flow that adopted \"{command.Name}\"? The built-in command itself stays and becomes live again."))
                {
                    StatusMessage = "Delete cancelled";
                    return;
                }

                await DeleteAdoptingFlowAsync(owningFlowId, command.Name);

                command.IsEnabled = true;
                command.UpdatedAt = DateTime.UtcNow;
                await _commandService.UpdateCommandAsync(command);

                StatusMessage = $"{command.Name} flow deleted — the built-in command is live again";
                _logger.LogInformation("Deleted the flow adopting built-in command {CommandName}", command.Name);

                if (IsEditingCommand && SelectedCommand == command)
                {
                    Close();
                }

                await LoadCommandsAsync();
                return;
            }

            var confirmMessage = owningFlowId == null
                ? $"Delete command \"{command.Name}\"? This cannot be undone."
                : $"Delete command \"{command.Name}\" AND the flow that adopted it? This cannot be undone.";
            if (!Confirm(confirmMessage))
            {
                StatusMessage = "Delete cancelled";
                return;
            }

            // DeleteCommandAsync returns false when the row is already gone
            // (e.g. a second Delete click on a stale selection).
            var deleted = await _commandService.DeleteCommandAsync(command.Id);
            if (!deleted)
            {
                StatusMessage = $"{command.Name} no longer exists";
                _logger.LogWarning("Delete skipped: command {CommandName} no longer exists", command.Name);
                return;
            }

            if (owningFlowId != null)
            {
                await DeleteAdoptingFlowAsync(owningFlowId, command.Name);
            }

            StatusMessage = owningFlowId == null
                ? $"{command.Name} deleted"
                : $"{command.Name} deleted — its flow was removed too";
            _logger.LogInformation("Deleted command: {CommandName} (owning flow: {FlowId})",
                command.Name, owningFlowId ?? "none");

            if (IsEditingCommand && SelectedCommand == command)
            {
                Close();
            }

            // Clear the stale selection so a second Delete click cannot
            // target the removed row again.
            SelectedCommand = null;

            await LoadCommandsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete command: {CommandName}", command.Name);
            StatusMessage = $"Failed to delete command: {ex.Message}";
        }
    }

    /// <summary>
    /// Modal OK/cancel confirmation (same pattern as SettingsViewModel's
    /// data-management confirmations). True only when the user confirmed.
    /// </summary>
    private static bool Confirm(string message)
    {
        var dialog = new Views.ConfirmDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        dialog.SetMessage(message);
        return dialog.ShowDialog() == true;
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(Command? command)
    {
        if (command == null) return;

        try
        {
            // Note: CheckBox IsChecked binding already toggled the value —
            // flipping it again here would undo the change before the save,
            // so the persisted value would never move.
            command.UpdatedAt = DateTime.UtcNow;
            await _commandService.UpdateCommandAsync(command);

            StatusMessage = $"{command.Name} {(command.IsEnabled ? "enabled" : "disabled")}";
            _logger.LogInformation("Toggled command {CommandName} to {Status}",
                command.Name, command.IsEnabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle command: {CommandName}", command.Name);
            StatusMessage = $"Failed to toggle command: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TogglePlatformAsync(Command? command)
    {
        if (command == null) return;

        try
        {
            // Note: CheckBox IsChecked binding already toggled the value
            command.UpdatedAt = DateTime.UtcNow;
            await _commandService.UpdateCommandAsync(command);

            StatusMessage = $"Updated {command.Name} platform settings";
            _logger.LogInformation("Updated platform settings for command {CommandName}", command.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle platform for command");
            StatusMessage = $"Failed to update platform settings: {ex.Message}";
        }
    }

    // ----------------------------------------------------------------------
    // Timers tab
    // ----------------------------------------------------------------------

    partial void OnTimerTargetPlatformChanged(string value)
    {
        UpdateTimerBotPickerVisibility();
    }

    partial void OnHasDiscordBotsChanged(bool value)
    {
        UpdateTimerBotPickerVisibility();
    }

    /// <summary>The bot dropdown matters only for Discord timers with bots configured.</summary>
    private void UpdateTimerBotPickerVisibility()
    {
        ShowTimerBotPicker = string.Equals(TimerTargetPlatform, "Discord", StringComparison.OrdinalIgnoreCase)
            && HasDiscordBots;
    }

    /// <summary>Loads every timer row into the Timers grid.</summary>
    private async Task LoadTimersAsync()
    {
        if (_dataService == null)
        {
            return;
        }

        try
        {
            var timers = await _dataService.GetCommandTimersAsync();

            Timers.Clear();
            foreach (var timer in timers.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                Timers.Add(timer);
            }

            _logger.LogInformation("Loaded {TimerCount} command timer(s)", Timers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load command timers");
            StatusMessage = "Failed to load timers";
        }
    }

    [RelayCommand]
    private void NewTimer()
    {
        IsEditingTimer = true;
        IsCustomTimerEdit = false;
        SelectedTimer = null;
        TimerName = "";
        TimerCommandName = TimerCommandNames.FirstOrDefault() ?? "";
        TimerIntervalMinutes = 30;
        TimerTargetPlatform = "Twitch";
        SelectedTimerBot = TimerBotOptions.FirstOrDefault(); // "Primary bot"
        TimerIsEnabled = true;
        TimerEditHeader = "New timer";
        StatusMessage = "";
    }

    [RelayCommand]
    private void EditTimer(CommandTimer? timer)
    {
        timer ??= SelectedTimer;

        if (timer == null)
        {
            StatusMessage = "Pick a timer in the table first";
            return;
        }

        IsEditingTimer = true;
        IsCustomTimerEdit = true;
        SelectedTimer = timer;
        TimerName = timer.Name;
        TimerCommandName = timer.CommandName;
        TimerIntervalMinutes = timer.IntervalMinutes;
        TimerTargetPlatform = timer.TargetPlatform;
        SelectedTimerBot = TimerBotOptions.FirstOrDefault(b => b.Id == timer.BotId)
            ?? TimerBotOptions.FirstOrDefault();
        TimerIsEnabled = timer.IsEnabled;
        TimerEditHeader = $"Editing {timer.Name}";
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task SaveTimerAsync()
    {
        if (_dataService == null)
        {
            StatusMessage = "Data service unavailable — cannot save timers";
            return;
        }

        var name = TimerName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Timer name cannot be empty";
            return;
        }

        var typedCommand = TimerCommandName.Trim();
        if (string.IsNullOrWhiteSpace(typedCommand))
        {
            StatusMessage = "Pick the command this timer fires";
            return;
        }

        // The picker is free text — resolve to the canonical stored casing.
        var commandName = TimerCommandNames.FirstOrDefault(
            n => string.Equals(n, typedCommand, StringComparison.OrdinalIgnoreCase));
        if (commandName == null)
        {
            StatusMessage = $"Unknown command {typedCommand} — pick one from the list";
            return;
        }

        if (TimerIntervalMinutes < 5)
        {
            StatusMessage = "Interval must be at least 5 minutes";
            return;
        }

        var platform = TimerPlatforms.Contains(TimerTargetPlatform) ? TimerTargetPlatform : "Twitch";

        try
        {
            var isNew = SelectedTimer == null;
            var timer = new CommandTimer
            {
                Id = SelectedTimer?.Id ?? Guid.NewGuid().ToString(),
                Name = name,
                CommandName = commandName,
                IntervalMinutes = TimerIntervalMinutes,
                TargetPlatform = platform,
                // Discord only: null means the manager's Primary bot sends it.
                BotId = platform == "Discord" ? SelectedTimerBot?.Id : null,
                IsEnabled = TimerIsEnabled,
                LastRunAt = SelectedTimer?.LastRunAt,
                CreatedAt = SelectedTimer?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _dataService.SaveCommandTimerAsync(timer);

            StatusMessage = isNew
                ? $"{timer.Name} added — fires {timer.CommandName} every {timer.IntervalMinutes}m on {timer.TargetPlatform}"
                : $"{timer.Name} saved";
            _logger.LogInformation("{Action} command timer {TimerName} -> {CommandName} every {Interval}m on {Platform}",
                isNew ? "Added" : "Updated", timer.Name, timer.CommandName, timer.IntervalMinutes, timer.TargetPlatform);

            await LoadTimersAsync();
            CloseTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save timer: {TimerName}", TimerName);
            StatusMessage = $"Failed to save timer: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseTimer()
    {
        IsEditingTimer = false;
        IsCustomTimerEdit = false;
        SelectedTimer = null;
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task DeleteTimerAsync(CommandTimer? timer)
    {
        if (_dataService == null)
        {
            StatusMessage = "Data service unavailable — cannot delete timers";
            return;
        }

        timer ??= SelectedTimer;

        if (timer == null)
        {
            StatusMessage = "No timer selected";
            return;
        }

        try
        {
            await _dataService.DeleteCommandTimerAsync(timer.Id);
            StatusMessage = $"{timer.Name} deleted";
            _logger.LogInformation("Deleted command timer: {TimerName}", timer.Name);

            if (IsEditingTimer && SelectedTimer == timer)
            {
                CloseTimer();
            }

            await LoadTimersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete timer: {TimerName}", timer.Name);
            StatusMessage = $"Failed to delete timer: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleTimerEnabledAsync(CommandTimer? timer)
    {
        if (timer == null || _dataService == null)
        {
            return;
        }

        try
        {
            // Note: CheckBox IsChecked binding already toggled the value
            timer.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveCommandTimerAsync(timer);

            StatusMessage = $"{timer.Name} {(timer.IsEnabled ? "enabled" : "disabled")}";
            _logger.LogInformation("Toggled timer {TimerName} to {Status}",
                timer.Name, timer.IsEnabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle timer: {TimerName}", timer.Name);
            StatusMessage = $"Failed to toggle timer: {ex.Message}";
        }
    }
}

/// <summary>
/// Bot column display: Command.BotId -> "All bots" or the bot's name
/// (falls back to the raw id for bots deleted since the last refresh).
/// </summary>
public class CommandBotNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return CommandsViewModel.BotDisplayName(value as string);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Scope toggle button label: shows the action clicking performs — "THIS BOT"
/// for unscoped commands, "ALL BOTS" for ones pinned to the selected bot.
/// </summary>
public class CommandScopeActionLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? "THIS BOT" : "ALL BOTS";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Command column tooltip: the plain name by default; rows adopted by a flow
/// get a subtle "managed by a flow" hint so the Visual Builder ownership is
/// visible without adding a column.
/// </summary>
public class CommandFlowManagedToolTipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Command command)
        {
            return string.Empty;
        }

        return CommandsViewModel.FlowManagedCommandIds.Contains(command.Id)
            ? $"{command.Name} — 🎨 managed by a flow (edits open in the Visual Builder)"
            : command.Name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Timers grid "Last Run" column: null -> "Never", otherwise the UTC stamp
/// shown in local time.
/// </summary>
public class CommandTimerLastRunConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DateTime utc
            ? utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "Never";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Timers grid "Bot" column: "—" for non-Discord rows, "Primary" for unscoped
/// Discord timers, otherwise the pinned bot's display name.
/// </summary>
public class CommandTimerBotLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CommandTimer timer)
        {
            return "—";
        }

        if (!string.Equals(timer.TargetPlatform, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            return "—";
        }

        return string.IsNullOrEmpty(timer.BotId)
            ? "Primary"
            : CommandsViewModel.BotDisplayName(timer.BotId);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
