using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using VyralBot.Workflows.Persistence;
using VyralBot.Workflows.Runtime;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One entry of the step-type dropdown. Built dynamically from the engine's
/// <see cref="NodeRegistry"/> (plus a hardcoded fallback should the registry
/// ever be empty), so plugin-registered nodes show up too.
/// </summary>
public class NodeTypeOption
{
    public string Label { get; }
    public string Value { get; }
    public string Category { get; }

    public NodeTypeOption(string label, string value, string category)
    {
        Label = label;
        Value = value;
        Category = category;
    }

    public override string ToString() => Label;
}

/// <summary>
/// One row of the structured step editor. Mirrors a <see cref="WorkflowNode"/>:
/// the type dropdown picks the engine node type, the per-type fields map onto
/// the node's Config keys, and If steps own TRUE/FALSE sub-lists (one rendered
/// level; deeper structures load, round-trip and re-save but are not drawn).
/// </summary>
public partial class WorkflowStepViewModel : ObservableObject
{
    /// <summary>Stable node id — kept from the loaded node so links survive a round trip.</summary>
    public string NodeId { get; }

    [ObservableProperty]
    private NodeTypeOption _type;

    // Per-type Config-backed fields (strings so half-typed numbers never crash the UI).
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private string _messagePlatform = "Any";
    [ObservableProperty] private string _seconds = "1";
    [ObservableProperty] private string _expression = string.Empty;
    [ObservableProperty] private string _varName = string.Empty;
    [ObservableProperty] private string _varValue = string.Empty;
    [ObservableProperty] private string _minValue = "0";
    [ObservableProperty] private string _maxValue = "100";
    [ObservableProperty] private string _storeInto = string.Empty;
    [ObservableProperty] private string _amount = "100";
    [ObservableProperty] private string _currencyUser = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;

    /// <summary>TRUE-branch steps (If steps only), chained sequentially in run order.</summary>
    public ObservableCollection<WorkflowStepViewModel> TrueSteps { get; } = new();

    /// <summary>FALSE-branch steps (If steps only), chained sequentially in run order.</summary>
    public ObservableCollection<WorkflowStepViewModel> FalseSteps { get; } = new();

    /// <summary>
    /// Config keys the UI does not edit (e.g. log "level") — preserved so a
    /// load → save round trip never silently drops settings.
    /// </summary>
    private readonly Dictionary<string, string> _rawConfig = new(StringComparer.Ordinal);

    /// <summary>Parent hook: fired whenever anything that affects the path preview changes.</summary>
    public Action? PreviewInvalidated { get; set; }

    public WorkflowStepViewModel(NodeTypeOption type, string? nodeId = null, IReadOnlyDictionary<string, string>? config = null)
    {
        NodeId = string.IsNullOrWhiteSpace(nodeId) ? Guid.NewGuid().ToString("N") : nodeId;
        _type = type;

        if (config != null)
        {
            foreach (var kvp in config)
            {
                _rawConfig[kvp.Key] = kvp.Value ?? string.Empty;
            }

            LoadFieldsFromConfig();
        }

        TrueSteps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTrueSteps));
            PreviewInvalidated?.Invoke();
        };
        FalseSteps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFalseSteps));
            PreviewInvalidated?.Invoke();
        };
    }

    // --- Type flags (drive the per-type field visibility in the editor) -----

    public bool IsSendMessage => Type.Value == "send_message";
    public bool IsWait => Type.Value == "wait";
    public bool IsIf => Type.Value == "if";
    public bool IsSetVariable => Type.Value == "set_variable";
    public bool IsRandom => Type.Value == "random_number";
    public bool IsAddCurrency => Type.Value == "add_currency";
    public bool IsRemoveCurrency => Type.Value == "remove_currency";
    public bool IsCurrency => IsAddCurrency || IsRemoveCurrency;
    public bool IsLog => Type.Value == "log";

    public bool HasTrueSteps => TrueSteps.Count > 0;
    public bool HasFalseSteps => FalseSteps.Count > 0;

    partial void OnTypeChanged(NodeTypeOption value)
    {
        // Switching type starts fresh — no stale keys from the previous type.
        _rawConfig.Clear();
        OnPropertyChanged(nameof(IsSendMessage));
        OnPropertyChanged(nameof(IsWait));
        OnPropertyChanged(nameof(IsIf));
        OnPropertyChanged(nameof(IsSetVariable));
        OnPropertyChanged(nameof(IsRandom));
        OnPropertyChanged(nameof(IsAddCurrency));
        OnPropertyChanged(nameof(IsRemoveCurrency));
        OnPropertyChanged(nameof(IsCurrency));
        OnPropertyChanged(nameof(IsLog));
        OnPropertyChanged(nameof(IsComplete));
        PreviewInvalidated?.Invoke();
    }

    private void OnFieldChanged()
    {
        OnPropertyChanged(nameof(IsComplete));
        PreviewInvalidated?.Invoke();
    }

    partial void OnMessageTextChanged(string value) => OnFieldChanged();
    partial void OnMessagePlatformChanged(string value) => OnFieldChanged();
    partial void OnSecondsChanged(string value) => OnFieldChanged();
    partial void OnExpressionChanged(string value) => OnFieldChanged();
    partial void OnVarNameChanged(string value) => OnFieldChanged();
    partial void OnVarValueChanged(string value) => OnFieldChanged();
    partial void OnMinValueChanged(string value) => OnFieldChanged();
    partial void OnMaxValueChanged(string value) => OnFieldChanged();
    partial void OnStoreIntoChanged(string value) => OnFieldChanged();
    partial void OnAmountChanged(string value) => OnFieldChanged();
    partial void OnCurrencyUserChanged(string value) => OnFieldChanged();
    partial void OnLogTextChanged(string value) => OnFieldChanged();

    /// <summary>True when the step has everything its type needs to run.</summary>
    public bool IsComplete => Type.Value switch
    {
        "send_message" => !string.IsNullOrWhiteSpace(MessageText),
        "wait" => int.TryParse(Seconds, out var s) && s >= 0,
        "if" => !string.IsNullOrWhiteSpace(Expression),
        "set_variable" => !string.IsNullOrWhiteSpace(VarName),
        "random_number" => int.TryParse(MinValue, out _) && int.TryParse(MaxValue, out _),
        "add_currency" or "remove_currency" => int.TryParse(Amount, out var a) && a > 0,
        "log" => !string.IsNullOrWhiteSpace(LogText),
        _ => true // unknown/plugin types carry their own config
    };

    /// <summary>Short run-order summary used by the live path preview.</summary>
    public string Summary => Type.Value switch
    {
        "send_message" => MessagePlatform == "Any" ? "Send Message" : $"Send Message ({MessagePlatform})",
        "wait" => int.TryParse(Seconds, out var s) ? $"Wait {s}s" : "Wait",
        "if" => string.IsNullOrWhiteSpace(Expression) ? "If (…)" : $"If ({Expression.Trim()})",
        "set_variable" => string.IsNullOrWhiteSpace(VarName) ? "Set Variable" : $"Set {VarName.Trim()} = {VarValue.Trim()}",
        "random_number" => string.IsNullOrWhiteSpace(StoreInto)
            ? $"Random {MinValue}-{MaxValue}"
            : $"Random {MinValue}-{MaxValue} → {StoreInto.Trim()}",
        "add_currency" => $"Add Currency {Amount}",
        "remove_currency" => $"Remove Currency {Amount}",
        "log" => "Log",
        _ => Type.Label
    };

    private void LoadFieldsFromConfig()
    {
        string Get(string key, string fallback = "") =>
            _rawConfig.TryGetValue(key, out var value) ? value : fallback;

        switch (Type.Value)
        {
            case "send_message":
                MessageText = Get("text");
                var platform = Get("platform").Trim();
                MessagePlatform = string.IsNullOrWhiteSpace(platform) ? "Any" : platform;
                break;
            case "wait":
                Seconds = Get("seconds", "1");
                break;
            case "if":
                Expression = Get("expression");
                break;
            case "set_variable":
                VarName = Get("name");
                VarValue = Get("value");
                break;
            case "random_number":
                MinValue = Get("min", "0");
                MaxValue = Get("max", "100");
                StoreInto = Get("store");
                break;
            case "add_currency":
            case "remove_currency":
                Amount = Get("amount", "100");
                CurrencyUser = Get("username");
                break;
            case "log":
                LogText = Get("text");
                break;
        }
    }

    /// <summary>
    /// Builds the engine node: edited keys are written, unedited extras are
    /// preserved, "Any" message platform omits the override key entirely.
    /// </summary>
    public WorkflowNode ToNode()
    {
        var config = new Dictionary<string, string>(_rawConfig, StringComparer.Ordinal);

        switch (Type.Value)
        {
            case "send_message":
                config["text"] = MessageText ?? string.Empty;
                if (string.IsNullOrWhiteSpace(MessagePlatform) || MessagePlatform == "Any")
                    config.Remove("platform");
                else
                    config["platform"] = MessagePlatform;
                break;
            case "wait":
                config["seconds"] = Seconds ?? string.Empty;
                break;
            case "if":
                config["expression"] = Expression ?? string.Empty;
                break;
            case "set_variable":
                config["name"] = VarName ?? string.Empty;
                config["value"] = VarValue ?? string.Empty;
                break;
            case "random_number":
                config["min"] = MinValue ?? "0";
                config["max"] = MaxValue ?? "100";
                if (string.IsNullOrWhiteSpace(StoreInto))
                    config.Remove("store");
                else
                    config["store"] = StoreInto.Trim();
                break;
            case "add_currency":
            case "remove_currency":
                config["amount"] = Amount ?? string.Empty;
                if (string.IsNullOrWhiteSpace(CurrencyUser))
                    config.Remove("username");
                else
                    config["username"] = CurrencyUser.Trim();
                break;
            case "log":
                config["text"] = LogText ?? string.Empty;
                break;
        }

        return new WorkflowNode
        {
            Id = NodeId,
            Type = Type.Value,
            Config = config
        };
    }
}

/// <summary>
/// One workflow card in the list: display fields plus the enabled toggle the
/// row binds to. Carries the live <see cref="Workflow"/> model so RUN can hand
/// it straight to the trigger service.
/// </summary>
public partial class WorkflowListItemViewModel : ObservableObject
{
    public Workflow Workflow { get; }

    public string Id => Workflow.Id;
    public string Name => Workflow.Name;
    public string? Description => Workflow.Description;

    [ObservableProperty]
    private bool _isEnabled;

    public string TriggerSummary { get; }
    public string StepSummary { get; }

    public WorkflowListItemViewModel(Workflow workflow)
    {
        Workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _isEnabled = workflow.Enabled;
        TriggerSummary = WorkflowRuntime.SummarizeTrigger(workflow.Trigger);
        StepSummary = $"{CountNodes(Workflow.Nodes)} step{(CountNodes(Workflow.Nodes) == 1 ? string.Empty : "s")}";
    }

    private static int CountNodes(List<WorkflowNode>? nodes) => nodes?.Count ?? 0;
}

/// <summary>One row of the execution-history grid.</summary>
public class WorkflowExecutionItemViewModel
{
    public string ExecutedAtText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public bool IsFailed { get; init; }
    public string DurationText { get; init; } = string.Empty;
    public string PathText { get; init; } = string.Empty;
    public string ErrorText { get; init; } = string.Empty;
}

/// <summary>
/// Automation page view model: the BotGhost-style STRUCTURED workflow builder
/// (ordered steps with If branches — the infinite-canvas editor is a later
/// phase). Lists workflows, edits trigger + step chain, runs manual triggers,
/// and shows per-workflow execution history.
/// </summary>
public partial class AutomationViewModel : ViewModelBase
{
    /// <summary>Display order of the core node types in the dropdown.</summary>
    private static readonly string[] KnownNodeOrder =
    {
        "send_message", "wait", "if", "set_variable", "random_number", "add_currency", "remove_currency", "log"
    };

    /// <summary>Hardcoded fallback palette if the registry is ever empty.</summary>
    private static readonly (string Label, string Value, string Category)[] FallbackNodeTypes =
    {
        ("Send Message", "send_message", "Messaging"),
        ("Wait", "wait", "Timers"),
        ("If Condition", "if", "Logic"),
        ("Set Variable", "set_variable", "Variables"),
        ("Random Number", "random_number", "Random"),
        ("Add Currency", "add_currency", "Economy"),
        ("Remove Currency", "remove_currency", "Economy"),
        ("Log", "log", "Logic")
    };

    private const string TriggerLabelCommand = "Command";
    private const string TriggerLabelTimer = "Timer";
    private const string TriggerLabelEvent = "Event";
    private const string TriggerLabelManual = "Manual";

    /// <summary>Event types the trigger service actually subscribes to.</summary>
    private static readonly string[] SupportedEventTypes =
    {
        "follow", "subscribe", "resub", "giftsub", "raid", "cheer", "host"
    };

    private readonly ILogger<AutomationViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly WorkflowTriggerService _triggerService;

    /// <summary>The workflow open in the builder (null while creating a new one).</summary>
    private Workflow? _editingWorkflow;

    /// <summary>Workflow whose history card is open (null when hidden).</summary>
    private string? _historyWorkflowId;

    // ======================================================================
    // List
    // ======================================================================

    [ObservableProperty]
    private ObservableCollection<WorkflowListItemViewModel> _workflows = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasWorkflows => Workflows.Count > 0;

    partial void OnWorkflowsChanged(ObservableCollection<WorkflowListItemViewModel> value)
        => OnPropertyChanged(nameof(HasWorkflows));

    // ======================================================================
    // Builder form
    // ======================================================================

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNewWorkflow = true;

    [ObservableProperty]
    private string _editHeader = "New workflow";

    [ObservableProperty]
    private string _workflowName = string.Empty;

    [ObservableProperty]
    private string _workflowDescription = string.Empty;

    [ObservableProperty]
    private bool _workflowEnabled = true;

    /// <summary>The list row currently open in the builder (drives the builder DELETE button).</summary>
    [ObservableProperty]
    private WorkflowListItemViewModel? _selectedListEntry;

    // Trigger form

    public ObservableCollection<string> TriggerTypeOptions { get; } =
        new() { TriggerLabelCommand, TriggerLabelTimer, TriggerLabelEvent, TriggerLabelManual };

    [ObservableProperty]
    private string _selectedTriggerType = TriggerLabelManual;

    [ObservableProperty]
    private string _triggerCommandName = string.Empty;

    [ObservableProperty]
    private string _triggerIntervalMinutes = "15";

    public ObservableCollection<string> EventTypeOptions { get; } = new(SupportedEventTypes);

    [ObservableProperty]
    private string _selectedEventType = "follow";

    public ObservableCollection<string> PlatformOptions { get; } = new() { "Any", "Twitch", "Kick", "Discord" };

    [ObservableProperty]
    private string _selectedTriggerPlatform = "Any";

    public bool IsTriggerCommand => SelectedTriggerType == TriggerLabelCommand;
    public bool IsTriggerTimer => SelectedTriggerType == TriggerLabelTimer;
    public bool IsTriggerEvent => SelectedTriggerType == TriggerLabelEvent;
    public bool IsTriggerManual => SelectedTriggerType == TriggerLabelManual;

    partial void OnSelectedTriggerTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsTriggerCommand));
        OnPropertyChanged(nameof(IsTriggerTimer));
        OnPropertyChanged(nameof(IsTriggerEvent));
        OnPropertyChanged(nameof(IsTriggerManual));
    }

    // Step chain

    /// <summary>Every registered node type (engine registry + plugins), core order first.</summary>
    public ObservableCollection<NodeTypeOption> NodeTypes { get; } = new();

    /// <summary>Branch-step types: everything except "if" (one nesting level in V1).</summary>
    public ObservableCollection<NodeTypeOption> BranchNodeTypes { get; } = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowStepViewModel> _steps = new();

    [ObservableProperty]
    private string _pathPreview = "Start → End";

    public bool HasSteps => Steps.Count > 0;

    // ======================================================================
    // History
    // ======================================================================

    [ObservableProperty]
    private bool _isHistoryVisible;

    [ObservableProperty]
    private string _historyTitle = "Execution history";

    [ObservableProperty]
    private ObservableCollection<WorkflowExecutionItemViewModel> _executions = new();

    public bool HasExecutions => Executions.Count > 0;

    partial void OnExecutionsChanged(ObservableCollection<WorkflowExecutionItemViewModel> value)
        => OnPropertyChanged(nameof(HasExecutions));

    public AutomationViewModel(
        ILogger<AutomationViewModel> logger,
        IDataService dataService,
        WorkflowTriggerService triggerService,
        NodeRegistry nodeRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));

        BuildNodeTypePalette(nodeRegistry);

        // Swap in a fresh collection so OnStepsChanged hooks its watcher (the
        // field initializer's instance never raises the changed callback).
        Steps = new ObservableCollection<WorkflowStepViewModel>();
    }

    /// <summary>
    /// Steps is replaced wholesale on new/load — re-hook the collection watcher
    /// every time so HasSteps and the path preview stay live.
    /// </summary>
    partial void OnStepsChanged(ObservableCollection<WorkflowStepViewModel> value)
    {
        if (value != null)
        {
            value.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasSteps));
                RebuildPreview();
            };
        }

        OnPropertyChanged(nameof(HasSteps));
        RebuildPreview();
    }

    /// <summary>Builds the step-type dropdowns from the engine registry (dynamic, with fallback).</summary>
    private void BuildNodeTypePalette(NodeRegistry? registry)
    {
        var options = new List<NodeTypeOption>();

        var registered = registry?.GetAll() ?? Array.Empty<INode>();
        if (registered.Count > 0)
        {
            options.AddRange(registered
                .Where(n => KnownNodeOrder.Contains(n.Type))
                .OrderBy(n => Array.IndexOf(KnownNodeOrder, n.Type))
                .Select(n => new NodeTypeOption(n.Title, n.Type, n.Category)));
            options.AddRange(registered
                .Where(n => !KnownNodeOrder.Contains(n.Type))
                .OrderBy(n => n.Category)
                .ThenBy(n => n.Title)
                .Select(n => new NodeTypeOption(n.Title, n.Type, n.Category)));
        }
        else
        {
            options.AddRange(FallbackNodeTypes.Select(t => new NodeTypeOption(t.Label, t.Value, t.Category)));
        }

        NodeTypes.Clear();
        foreach (var option in options)
        {
            NodeTypes.Add(option);
        }

        BranchNodeTypes.Clear();
        foreach (var option in options.Where(o => o.Value != "if"))
        {
            BranchNodeTypes.Add(option);
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadWorkflowsAsync();
            _logger.LogInformation("AutomationViewModel initialized ({Count} workflows)", Workflows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AutomationViewModel");
            StatusMessage = "Failed to load workflows — hit REFRESH to try again.";
        }
    }

    // ======================================================================
    // Loading
    // ======================================================================

    [RelayCommand]
    public async Task LoadWorkflowsAsync()
    {
        try
        {
            IsBusy = true;

            var rows = await _dataService.GetWorkflowsAsync();
            var loaded = (rows ?? Enumerable.Empty<WorkflowEntity>())
                .Select(WorkflowJson.FromRow)
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Workflows = new ObservableCollection<WorkflowListItemViewModel>(
                loaded.Select(w => new WorkflowListItemViewModel(w)));

            // Drop references to workflows that no longer exist.
            if (SelectedListEntry != null && Workflows.All(w => w.Id != SelectedListEntry.Id))
            {
                SelectedListEntry = null;
            }

            if (IsHistoryVisible && _historyWorkflowId != null && Workflows.All(w => w.Id != _historyWorkflowId))
            {
                CloseHistory();
            }

            StatusMessage = loaded.Count == 0
                ? "No workflows yet — build your first one."
                : $"{loaded.Count} workflow{(loaded.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workflows");
            StatusMessage = "Failed to load workflows.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ======================================================================
    // Builder open/close
    // ======================================================================

    [RelayCommand]
    public void NewWorkflow()
    {
        _editingWorkflow = null;
        IsNewWorkflow = true;
        EditHeader = "New workflow";
        WorkflowName = string.Empty;
        WorkflowDescription = string.Empty;
        WorkflowEnabled = true;
        SelectedListEntry = null;

        SelectedTriggerType = TriggerLabelManual;
        TriggerCommandName = string.Empty;
        TriggerIntervalMinutes = "15";
        SelectedEventType = "follow";
        SelectedTriggerPlatform = "Any";

        Steps = new ObservableCollection<WorkflowStepViewModel>();
        RebuildPreview();

        IsEditing = true;
        StatusMessage = "New workflow — pick a trigger, stack some steps, then SAVE.";
    }

    [RelayCommand]
    public void EditWorkflow(WorkflowListItemViewModel item)
    {
        if (item == null)
        {
            return;
        }

        var workflow = item.Workflow;

        _editingWorkflow = workflow;
        IsNewWorkflow = false;
        EditHeader = $"Editing: {workflow.Name}";
        WorkflowName = workflow.Name;
        WorkflowDescription = workflow.Description ?? string.Empty;
        WorkflowEnabled = workflow.Enabled;
        SelectedListEntry = item;

        LoadTriggerIntoForm(workflow.Trigger);
        LoadStepsFromWorkflow(workflow);

        IsEditing = true;
        StatusMessage = $"Editing \"{workflow.Name}\" — SAVE keeps the same workflow id and history.";
    }

    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
        _editingWorkflow = null;
        SelectedListEntry = null;
    }

    private void LoadTriggerIntoForm(WorkflowTrigger trigger)
    {
        SelectedTriggerType = (trigger.Type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            WorkflowTriggerTypes.Command => TriggerLabelCommand,
            WorkflowTriggerTypes.Timer => TriggerLabelTimer,
            WorkflowTriggerTypes.Event => TriggerLabelEvent,
            _ => TriggerLabelManual
        };

        TriggerCommandName = trigger.CommandName ?? string.Empty;
        TriggerIntervalMinutes = (trigger.IntervalMinutes ?? 15).ToString();
        SelectedEventType = string.IsNullOrWhiteSpace(trigger.EventType) ? "follow" : trigger.EventType.Trim().ToLowerInvariant();
        if (!EventTypeOptions.Contains(SelectedEventType))
        {
            EventTypeOptions.Add(SelectedEventType);
        }
        SelectedTriggerPlatform = string.IsNullOrWhiteSpace(trigger.Platform) ? "Any" : trigger.Platform.Trim();
        if (!PlatformOptions.Contains(SelectedTriggerPlatform))
        {
            PlatformOptions.Add(SelectedTriggerPlatform);
        }
    }

    /// <summary>
    /// Loads the node graph into the step editor: the entry chain (Nodes[0] →
    /// Next…) becomes the main list, If branches become TRUE/FALSE sub-lists.
    /// Join points (branch tails pointing back at the main chain) stop each
    /// walk; a guard cap protects against corrupt/cyclic graphs.
    /// </summary>
    private void LoadStepsFromWorkflow(Workflow workflow)
    {
        var nodes = workflow.Nodes ?? new List<WorkflowNode>();
        var nodesById = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Id))
            {
                nodesById.TryAdd(node.Id, node);
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var main = nodes.Count > 0 ? WalkChain(nodesById, nodes[0].Id, visited) : new List<WorkflowStepViewModel>();

        foreach (var step in main)
        {
            LoadBranches(step, nodesById, visited);
        }

        Steps = new ObservableCollection<WorkflowStepViewModel>(main);
        RebuildPreview();
    }

    private void LoadBranches(WorkflowStepViewModel step, Dictionary<string, WorkflowNode> nodesById, HashSet<string> visited)
    {
        if (!step.IsIf || !nodesById.TryGetValue(step.NodeId, out var node))
        {
            return;
        }

        foreach (var branchStep in WalkChain(nodesById, node.BranchTrue, visited))
        {
            step.TrueSteps.Add(branchStep);
        }
        foreach (var branchStep in WalkChain(nodesById, node.BranchFalse, visited))
        {
            step.FalseSteps.Add(branchStep);
        }

        foreach (var nested in step.TrueSteps.Concat(step.FalseSteps))
        {
            LoadBranches(nested, nodesById, visited);
        }
    }

    private List<WorkflowStepViewModel> WalkChain(
        Dictionary<string, WorkflowNode> nodesById,
        string? startId,
        HashSet<string> visited)
    {
        var result = new List<WorkflowStepViewModel>();
        var currentId = startId;
        var guard = 0;

        while (!string.IsNullOrWhiteSpace(currentId) && guard++ < WorkflowRuntime.MaxSteps)
        {
            // Already-loaded ids are join points back into an outer chain — stop here.
            if (visited.Contains(currentId!) || !nodesById.TryGetValue(currentId!, out var node))
            {
                break;
            }
            visited.Add(currentId!);
            result.Add(CreateStepViewModel(node));
            currentId = node.Next;
        }

        return result;
    }

    private WorkflowStepViewModel CreateStepViewModel(WorkflowNode node)
    {
        var option = NodeTypes.FirstOrDefault(t => string.Equals(t.Value, node.Type, StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            // Unknown/plugin node type: add it to the palettes so the dropdown
            // displays what was loaded instead of silently blanking.
            option = new NodeTypeOption(node.Type, node.Type, "Other");
            NodeTypes.Add(option);
            BranchNodeTypes.Add(option);
        }

        return new WorkflowStepViewModel(option, node.Id, node.Config)
        {
            PreviewInvalidated = RebuildPreview
        };
    }

    private WorkflowStepViewModel CreateDefaultStep(bool forBranch = false)
    {
        var palette = forBranch && BranchNodeTypes.Count > 0 ? BranchNodeTypes : NodeTypes;
        var option = palette.FirstOrDefault(t => t.Value == "send_message") ?? palette[0];
        return new WorkflowStepViewModel(option)
        {
            PreviewInvalidated = RebuildPreview
        };
    }

    // ======================================================================
    // Step commands
    // ======================================================================

    [RelayCommand]
    public void AddStep()
    {
        Steps.Add(CreateDefaultStep());
    }

    [RelayCommand]
    public void AddTrueStep(WorkflowStepViewModel ifStep)
    {
        if (ifStep?.IsIf == true)
        {
            ifStep.TrueSteps.Add(CreateDefaultStep(forBranch: true));
        }
    }

    [RelayCommand]
    public void AddFalseStep(WorkflowStepViewModel ifStep)
    {
        if (ifStep?.IsIf == true)
        {
            ifStep.FalseSteps.Add(CreateDefaultStep(forBranch: true));
        }
    }

    [RelayCommand]
    public void MoveStepUp(WorkflowStepViewModel step)
    {
        var collection = FindContainingCollection(step);
        var index = collection?.IndexOf(step) ?? -1;
        if (index > 0 && collection != null)
        {
            collection.Move(index, index - 1);
        }
    }

    [RelayCommand]
    public void MoveStepDown(WorkflowStepViewModel step)
    {
        var collection = FindContainingCollection(step);
        var index = collection?.IndexOf(step) ?? -1;
        if (collection != null && index >= 0 && index < collection.Count - 1)
        {
            collection.Move(index, index + 1);
        }
    }

    [RelayCommand]
    public void RemoveStep(WorkflowStepViewModel step)
    {
        var collection = FindContainingCollection(step);
        collection?.Remove(step);
    }

    /// <summary>Finds the collection (main list or any TRUE/FALSE sub-list, any depth) holding the step.</summary>
    private ObservableCollection<WorkflowStepViewModel>? FindContainingCollection(WorkflowStepViewModel step)
    {
        if (Steps.Contains(step))
        {
            return Steps;
        }

        foreach (var owner in Steps)
        {
            var found = FindInBranches(owner, step);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static ObservableCollection<WorkflowStepViewModel>? FindInBranches(WorkflowStepViewModel owner, WorkflowStepViewModel target)
    {
        if (owner.TrueSteps.Contains(target))
        {
            return owner.TrueSteps;
        }
        if (owner.FalseSteps.Contains(target))
        {
            return owner.FalseSteps;
        }

        foreach (var nested in owner.TrueSteps.Concat(owner.FalseSteps))
        {
            var found = FindInBranches(nested, target);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // ======================================================================
    // Path preview
    // ======================================================================

    private void RebuildPreview()
    {
        var parts = new List<string> { "Start" };
        AppendPreview(Steps, parts);
        parts.Add("End");
        PathPreview = string.Join(" → ", parts);
    }

    private static void AppendPreview(IReadOnlyList<WorkflowStepViewModel> chain, List<string> parts)
    {
        foreach (var step in chain)
        {
            parts.Add(step.Summary);
            if (step.IsIf)
            {
                var trueSide = step.TrueSteps.Count > 0
                    ? string.Join(" → ", step.TrueSteps.Select(s => s.Summary))
                    : "—";
                var falseSide = step.FalseSteps.Count > 0
                    ? string.Join(" → ", step.FalseSteps.Select(s => s.Summary))
                    : "—";
                parts.Add($"[T] {trueSide} / [F] {falseSide}");
            }
        }
    }

    // ======================================================================
    // Save / delete / toggle / run
    // ======================================================================

    [RelayCommand]
    public async Task SaveWorkflowAsync()
    {
        var validationError = ValidateForm();
        if (validationError != null)
        {
            StatusMessage = validationError;
            return;
        }

        try
        {
            IsBusy = true;

            var workflow = BuildWorkflow();
            await _dataService.SaveWorkflowAsync(WorkflowJson.ToRow(workflow));

            // Refresh the trigger service cache so command/event/timer wiring
            // picks the change up immediately.
            await _triggerService.ReloadAsync();

            _logger.LogInformation("Saved workflow '{Name}' ({WorkflowId}, {NodeCount} nodes)",
                workflow.Name, workflow.Id, workflow.Nodes.Count);

            IsEditing = false;
            _editingWorkflow = null;
            SelectedListEntry = null;
            await LoadWorkflowsAsync();
            StatusMessage = $"\"{workflow.Name}\" saved and live — triggers reloaded.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save workflow");
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Returns the first validation problem, or null when the form is saveable.</summary>
    private string? ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(WorkflowName))
        {
            return "Give the workflow a name first.";
        }

        switch (SelectedTriggerType)
        {
            case TriggerLabelCommand when string.IsNullOrWhiteSpace(TriggerCommandName):
                return "Command triggers need a command name (e.g. !hello).";
            case TriggerLabelTimer when (!int.TryParse(TriggerIntervalMinutes, out var minutes) || minutes < 1):
                return "Timer triggers need an interval of at least 1 minute.";
            case TriggerLabelEvent when string.IsNullOrWhiteSpace(SelectedEventType):
                return "Event triggers need an event type.";
        }

        var incomplete = FindIncompleteStep(Steps, "1");
        if (incomplete != null)
        {
            return incomplete;
        }

        return null;
    }

    private string? FindIncompleteStep(IReadOnlyList<WorkflowStepViewModel> chain, string position)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            var step = chain[i];
            var where = $"Step {position}.{i + 1} ({step.Type.Label})";
            if (!step.IsComplete)
            {
                return $"{where} is missing something — fill in its fields or remove it.";
            }

            var trueProblem = FindIncompleteStep(step.TrueSteps, $"{position}.{i + 1}T");
            if (trueProblem != null)
            {
                return trueProblem;
            }
            var falseProblem = FindIncompleteStep(step.FalseSteps, $"{position}.{i + 1}F");
            if (falseProblem != null)
            {
                return falseProblem;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the engine model: the main list becomes the entry chain; each If
    /// step's TRUE/FALSE sub-chains link back into the next main step (the
    /// runtime follows BranchTrue/BranchFalse only, so both branch tails point
    /// at the continuation to rejoin the flow).
    /// </summary>
    private Workflow BuildWorkflow()
    {
        var now = DateTime.UtcNow;
        var nodes = new List<WorkflowNode>();
        AppendChain(Steps, null, nodes);

        // Deterministic pseudo-layout for the future canvas (unused by the runtime).
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].X = 0;
            nodes[i].Y = i * 120;
        }

        return new Workflow
        {
            Id = _editingWorkflow?.Id ?? Guid.NewGuid().ToString("N"),
            Name = WorkflowName.Trim(),
            Description = string.IsNullOrWhiteSpace(WorkflowDescription) ? null : WorkflowDescription.Trim(),
            Enabled = WorkflowEnabled,
            Category = _editingWorkflow?.Category,
            Trigger = BuildTrigger(),
            Nodes = nodes,
            CreatedAt = _editingWorkflow?.CreatedAt ?? now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Writes a chain of steps into <paramref name="sink"/>. Every step links to
    /// the next; the chain's tail links to <paramref name="tailNextId"/> (the
    /// continuation in the owning chain, null = end). Returns nothing — node ids
    /// are stable on the step VMs, so links reference chain[i+1].NodeId directly.
    /// </summary>
    private void AppendChain(IReadOnlyList<WorkflowStepViewModel> chain, string? tailNextId, List<WorkflowNode> sink)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            var step = chain[i];
            var afterId = i + 1 < chain.Count ? chain[i + 1].NodeId : tailNextId;

            var node = step.ToNode();
            sink.Add(node); // entry node must stay first in the list order

            if (step.IsIf)
            {
                // Empty branches fall straight through to the continuation.
                node.BranchTrue = AppendBranchChain(step.TrueSteps, afterId, sink);
                node.BranchFalse = AppendBranchChain(step.FalseSteps, afterId, sink);
                // The runtime routes if-nodes by branch only (Next is ignored for
                // "if"), but recording the linear continuation here lets the
                // loader walk the main chain straight through the branch.
                node.Next = afterId;
            }
            else
            {
                node.Next = afterId;
            }
        }
    }

    /// <summary>Writes a branch sub-chain and returns the id the parent If should jump to.</summary>
    private string? AppendBranchChain(IReadOnlyList<WorkflowStepViewModel> chain, string? tailNextId, List<WorkflowNode> sink)
    {
        if (chain.Count == 0)
        {
            return tailNextId;
        }

        AppendChain(chain, tailNextId, sink);
        return chain[0].NodeId;
    }

    private WorkflowTrigger BuildTrigger()
    {
        var trigger = new WorkflowTrigger();

        switch (SelectedTriggerType)
        {
            case TriggerLabelCommand:
                trigger.Type = WorkflowTriggerTypes.Command;
                var token = (TriggerCommandName ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    var wordBreak = token.IndexOfAny(new[] { ' ', '\t' });
                    if (wordBreak > 0)
                    {
                        token = token[..wordBreak];
                    }
                    if (token.Length > 1 && token[0] is '!' or '?' or '.')
                    {
                        trigger.CommandPrefix = token[0].ToString();
                        trigger.CommandName = token[1..];
                    }
                    else
                    {
                        trigger.CommandName = token; // prefix unset: matches !, ? and .
                    }
                }
                break;

            case TriggerLabelTimer:
                trigger.Type = WorkflowTriggerTypes.Timer;
                trigger.IntervalMinutes = int.TryParse(TriggerIntervalMinutes, out var minutes) && minutes > 0
                    ? minutes
                    : null;
                break;

            case TriggerLabelEvent:
                trigger.Type = WorkflowTriggerTypes.Event;
                trigger.EventType = SelectedEventType?.Trim().ToLowerInvariant();
                trigger.Platform = string.IsNullOrWhiteSpace(SelectedTriggerPlatform) ? "Any" : SelectedTriggerPlatform;
                break;

            default:
                trigger.Type = WorkflowTriggerTypes.Manual;
                break;
        }

        return trigger;
    }

    [RelayCommand]
    public async Task DeleteWorkflowAsync(WorkflowListItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await _dataService.DeleteWorkflowAsync(item.Id);
            await _triggerService.ReloadAsync();

            // Close any UI still pointing at the deleted workflow.
            if (IsEditing && string.Equals(_editingWorkflow?.Id, item.Id, StringComparison.Ordinal))
            {
                CancelEdit();
            }
            if (IsHistoryVisible && string.Equals(_historyWorkflowId, item.Id, StringComparison.Ordinal))
            {
                CloseHistory();
            }

            _logger.LogInformation("Deleted workflow '{Name}' ({WorkflowId})", item.Name, item.Id);
            await LoadWorkflowsAsync();
            StatusMessage = $"\"{item.Name}\" deleted — its triggers are offline.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workflow {WorkflowId}", item.Id);
            StatusMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ToggleWorkflowEnabledAsync(WorkflowListItemViewModel item)
    {
        if (item == null)
        {
            return;
        }

        try
        {
            item.Workflow.Enabled = item.IsEnabled;
            await _dataService.SaveWorkflowAsync(WorkflowJson.ToRow(item.Workflow));
            await _triggerService.ReloadAsync();
            StatusMessage = item.IsEnabled
                ? $"\"{item.Name}\" armed — its trigger is live."
                : $"\"{item.Name}\" disarmed — it will not fire until re-enabled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle workflow {WorkflowId}", item.Id);
            StatusMessage = $"Could not save the toggle: {ex.Message}";
            item.IsEnabled = !item.IsEnabled; // roll the checkbox back
        }
    }

    [RelayCommand]
    public async Task RunWorkflowAsync(WorkflowListItemViewModel item)
    {
        if (item == null)
        {
            return;
        }

        // Fire-and-forget in the trigger service; runs even while disabled so
        // drafts stay testable.
        _triggerService.RunManual(item.Workflow);
        StatusMessage = $"\"{item.Name}\" fired — give it a second, then check HISTORY.";

        if (IsHistoryVisible && string.Equals(_historyWorkflowId, item.Id, StringComparison.Ordinal))
        {
            await Task.Delay(1200);
            if (IsHistoryVisible && string.Equals(_historyWorkflowId, item.Id, StringComparison.Ordinal))
            {
                await LoadHistoryCoreAsync();
            }
        }
    }

    // ======================================================================
    // History
    // ======================================================================

    [RelayCommand]
    public async Task ViewHistoryAsync(WorkflowListItemViewModel item)
    {
        if (item == null)
        {
            return;
        }

        _historyWorkflowId = item.Id;
        HistoryTitle = $"HISTORY — {item.Name}";
        IsHistoryVisible = true;
        await LoadHistoryCoreAsync();
    }

    [RelayCommand]
    public async Task RefreshHistoryAsync()
    {
        await LoadHistoryCoreAsync();
    }

    [RelayCommand]
    public void CloseHistory()
    {
        IsHistoryVisible = false;
        _historyWorkflowId = null;
        Executions = new ObservableCollection<WorkflowExecutionItemViewModel>();
    }

    private async Task LoadHistoryCoreAsync()
    {
        if (_historyWorkflowId == null)
        {
            return;
        }

        try
        {
            var rows = await _dataService.GetWorkflowExecutionsAsync(_historyWorkflowId, 25);

            var items = (rows ?? Enumerable.Empty<WorkflowExecution>())
                .OrderByDescending(e => e.StartedAt)
                .Select(e => new WorkflowExecutionItemViewModel
                {
                    ExecutedAtText = e.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    StatusText = string.IsNullOrWhiteSpace(e.Status) ? "?" : e.Status,
                    IsSuccess = string.Equals(e.Status, "Completed", StringComparison.OrdinalIgnoreCase),
                    IsFailed = string.Equals(e.Status, "Failed", StringComparison.OrdinalIgnoreCase),
                    DurationText = $"{e.DurationMs} ms",
                    PathText = PrettifyPath(e.NodePath),
                    ErrorText = e.Error ?? string.Empty
                })
                .ToList();

            Executions = new ObservableCollection<WorkflowExecutionItemViewModel>(items);
            StatusMessage = items.Count == 0
                ? "No executions recorded yet."
                : $"{items.Count} recent run{(items.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load execution history for {WorkflowId}", _historyWorkflowId);
            StatusMessage = "Failed to load the execution history.";
        }
    }

    /// <summary>Turns "send_message:abc123 -> wait:def456" into "send_message → wait".</summary>
    private static string PrettifyPath(string? nodePath)
    {
        if (string.IsNullOrWhiteSpace(nodePath))
        {
            return string.Empty;
        }

        var types = nodePath
            .Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry =>
            {
                var colon = entry.IndexOf(':');
                return colon > 0 ? entry[..colon] : entry;
            });

        return string.Join(" → ", types);
    }
}
