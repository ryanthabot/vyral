using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Expressions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.UI.Services;

/// <summary>
/// Wires WORKFLOW TRIGGERS into the running app (Context/BotGhostLikeIntegration.md
/// phases 3, 11, 13, 14, 15, 16, 25) by feeding the VyralBot.Workflows engine
/// (<see cref="WorkflowRuntime"/>) from six trigger sources:
///
///  • COMMAND triggers — matched from ChatViewModel's generic chat pipeline via
///    <see cref="TryHandleCommand"/> (one hook, cheaper and simpler than every
///    platform republishing chat through a second subscription). Command names
///    match the Core CommandService convention: "!name", "?name" or ".name",
///    case-insensitive; a match consumes the message (CommandService, currency,
///    giveaways, polls and watchtime never see it), everything else flows on
///    exactly as before. Echo filtering and dedup happen upstream in
///    ChatViewModel, so the bot never triggers itself.
///
///  • DISCORD SLASH COMMAND triggers ("slash_command") — native Discord
///    interaction events arrive through the <see cref="WorkflowDiscordBridge"/>
///    (which translates DiscordService's gateway events into the engine's
///    <see cref="WorkflowSlashInvocation"/> records). <see cref="TryHandleSlashCommand"/>
///    normalizes the invoked command name (same normalization the bot's
///    registration uses), matches ENABLED slash workflows and executes them
///    with Platform="Discord" and per-origin bot routing. A 5-second dedup
///    window per bot|command|user|channel guards against double delivery when
///    more than one path could surface the same user action.
///
///  • DISCORD COMPONENT triggers ("button_click" / "select_menu") — component
///    custom ids use the "vyralwf:{workflowId}(:{extra})?" namespace; the
///    bridge raises <see cref="WorkflowComponentInteraction"/> and
///    <see cref="TryHandleComponent"/> resolves the workflow by EXACT
///    ComponentKey first (component CHILD workflows — button/menu sub-flows
///    the editor materializes as real rows — carry the full custom id in
///    <c>Workflow.ComponentKey</c>), falling back to the classic BY-ID path.
///    Foreign or unknown custom ids are silently ignored (debug log).
///
///  • EVENT triggers — typed EventBus subscriptions (IEventBus.SubscribeAsync)
///    for Twitch follow/subscribe/resub/gift-sub/raid/cheer and Kick
///    follow/subscribe/gift-sub/host records. Workflow EventType values are
///    normalized case-insensitively with a few aliases ("sub"→subscribe,
///    "bits"→cheer, "gift"/"gifted"→giftsub, "hosted"→host, "raided"→raid).
///    Platform filtering: workflow Trigger.Platform null/empty/"Any"/"All"
///    matches every platform, otherwise an exact case-insensitive match is
///    required. YouTube is chat-message-only in V1 (no non-message event
///    records exist) so nothing is subscribed there.
///
///  • TIMER triggers — a 60s internal System.Threading.Timer evaluates each
///    enabled timer workflow's IntervalMinutes. LIMITATION (V1): last-run times
///    live in an in-memory dictionary, so they do not survive restarts or
///    profile switches — a timer whose interval elapsed while the app was down
///    (or that never ran) fires on the first tick after StartAsync, the same
///    first-tick behavior as CommandTimerService.
///
///  • MANUAL triggers — <see cref="RunManual"/> for the editor's Run button,
///    with a synthetic context (username "Manual"). Runs regardless of
///    Enabled so disabled drafts stay debuggable.
///
/// COMMAND OPTIONS (Workflow.Options, authored in the flow editor): cooldowns,
/// channel restrictions and DM/guild scoping are enforced BEFORE an execution
/// is built (see <see cref="EvaluateOptions"/>); "Moderator"/role-name
/// permissions are enforced AFTER the context is built, against the seeded
/// "userRoles"/"isModerator" variables (see <see cref="TryAllowPermission"/>).
/// Every command/slash/component trigger seeds its context with identity
/// variables (userRoles/userId/isBot) supplied by the caller when available.
///
/// SLASH AUTO-REGISTRATION: after every workflow cache load (start + editor
/// reloads) every enabled slash workflow's command name is registered
/// best-effort on every CONNECTED Discord bot — with the workflow's typed
/// parameters as Discord slash options when authored — and re-registered when
/// a bot starts later (manager ServiceStarted). Component child workflows
/// (button_click/select_menu rows with a ComponentKey) are never registered
/// as slash commands. Failures are logged, never thrown.
///
/// Lifecycle mirrors CommandTimerService: MainViewModel.StartAsync starts it
/// once a profile loads and stops it when platforms disconnect for a profile
/// switch, so triggers never fire against the wrong profile.
///
/// The engine runtime is resolved lazily from the service provider so a
/// missing/unconstructable WorkflowRuntime registration disables workflow
/// execution with a logged error instead of breaking app startup.
/// </summary>
public class WorkflowTriggerService
{
    // Default prefix set mirrors Core's CommandService ("!", "?", "."); the
    // active set is loaded from the "Commands.Prefixes" setting so both stay
    // in sync when the user changes it in Settings → Commands.
    private char[] _commandPrefixes = { '!', '?', '.' };
    private static readonly char[] Whitespace = { ' ', '\t' };
    // Default tick; the live interval comes from "Automation.TimerIntervalSeconds".
    private TimeSpan _tickInterval = TimeSpan.FromSeconds(60);
    // Settings → Automation master toggles (loaded in ReloadSettingsAsync).
    private bool _workflowsEnabled = true;
    private bool _commandTriggersEnabled = true;
    private bool _eventTriggersEnabled = true;
    private bool _timerTriggersEnabled = true;
    // Duplicate-delivery window for slash invocations (see TryHandleSlashCommand).
    private static readonly TimeSpan SlashDedupWindow = TimeSpan.FromSeconds(5);
    // Custom-id namespace of workflow components ("vyralwf:{workflowId}(:{extra})?").
    private const string ComponentCustomIdPrefix = "vyralwf:";

    private readonly ILogger<WorkflowTriggerService> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkflowMessageSender _messageSender;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    // Optional: Discord interaction bridge (raises WorkflowSlashInvocation /
    // WorkflowComponentInteraction) plus the bot manager used to enumerate
    // connected bots for slash auto-registration. Null-tolerant — the older
    // construction sites (and tests) keep working without them.
    private readonly WorkflowDiscordBridge? _discordBridge;
    private readonly IDiscordBotManager? _botManager;
    // Optional: live metrics sink for the Settings → Performance tab.
    private readonly VyralBot.Core.Services.Performance.IMetricsRegistry? _metrics;

    private readonly object _sync = new();
    private readonly List<Guid> _subscriptionIds = new();
    // command name (prefix-stripped, case-insensitive) → enabled command workflows
    private readonly Dictionary<string, List<Workflow>> _commandIndex = new(StringComparer.OrdinalIgnoreCase);
    // normalized event type → enabled event workflows
    private readonly Dictionary<string, List<Workflow>> _eventIndex = new(StringComparer.OrdinalIgnoreCase);
    // normalized slash command name → enabled slash_command workflows
    private readonly Dictionary<string, List<Workflow>> _slashIndex = new(StringComparer.Ordinal);
    // workflow id → enabled workflow (button/select interactions resolve BY ID)
    private readonly Dictionary<string, Workflow> _workflowById = new(StringComparer.Ordinal);
    // component custom id → enabled component workflow (button/select CHILD
    // workflows materialized by the editor resolve by their exact ComponentKey)
    private readonly Dictionary<string, Workflow> _workflowByComponentKey = new(StringComparer.Ordinal);
    private readonly List<Workflow> _timerWorkflows = new();
    // In-memory V1 timer bookkeeping: workflow id → last fire (UTC). See class doc.
    private readonly Dictionary<string, DateTime> _lastTimerRun = new(StringComparer.Ordinal);
    // Command-option cooldowns: "workflowId|username" (lowercased) → last run (UTC).
    private readonly Dictionary<string, DateTime> _lastOptionRun = new(StringComparer.Ordinal);
    // Slash dedup guard: "botId|command|username|channel" → first delivery (UTC).
    private readonly ConcurrentDictionary<string, DateTime> _recentSlashInvocations = new(StringComparer.Ordinal);

    private WorkflowRuntime? _runtime;
    private bool _runtimeResolved;
    private Timer? _tickTimer;
    private int _tickRunning; // 1 while a tick is executing (reentrancy guard)
    private bool _started;

    /// <summary>True while the service is started (a profile is active).</summary>
    public bool IsRunning => _started;

    public WorkflowTriggerService(
        ILogger<WorkflowTriggerService> logger,
        IDataService dataService,
        IEventBus eventBus,
        IServiceProvider serviceProvider,
        WorkflowMessageSender messageSender,
        ITwitchService twitchService,
        IKickService kickService,
        // Optional (null-tolerant, keeps old construction sites compiling):
        // Discord interaction bridge + bot manager for slash/component triggers.
        WorkflowDiscordBridge? discordBridge = null,
        IDiscordBotManager? botManager = null,
        VyralBot.Core.Services.Performance.IMetricsRegistry? metrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _discordBridge = discordBridge;
        _botManager = botManager;
        _metrics = metrics;

        // Slash auto-registration for bots that start AFTER the last cache
        // load: the lifecycle events live on the concrete manager (not the
        // interface), so subscribe defensively only when available. Subscribed
        // once for the app lifetime — StartAsync/StopAsync never re-subscribe.
        if (_botManager is DiscordBotManager concreteManager)
        {
            concreteManager.ServiceStarted += OnBotServiceStarted;
        }
    }

    /// <summary>
    /// Loads the workflow cache and activates event/timer triggers. Idempotent —
    /// a second call while running is a no-op. Failures are logged, never thrown:
    /// an unusable engine or data layer leaves the (empty) caches in place and
    /// TryHandleCommand simply matches nothing.
    /// </summary>
    public async Task StartAsync()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }
            _started = true;
        }

        // Resolve the engine runtime and install its outgoing MessageSender.
        // A failure here only disables execution (logged), it never throws.
        _ = ResolveRuntime();

        // Settings → Automation toggles/interval + execution-history retention
        await ReloadSettingsAsync();

        try
        {
            await LoadWorkflowsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkflowTriggerService failed to load workflows — triggers stay offline until the next start/reload");
        }

        await SubscribeToEventsAsync();

        // Discord interaction triggers: hook the bridge's slash/component
        // events (no-op when no bridge was injected).
        AttachDiscordBridge();

        _tickTimer = new Timer(_ => TickTimers(), null, _tickInterval, _tickInterval);

        _logger.LogInformation(
            "WorkflowTriggerService started (command names: {Commands}, event types: {EventTypes}, timer workflows: {Timers}, tick: {Seconds}s, enabled: {Enabled})",
            CommandCount, _eventIndex.Count, _timerWorkflows.Count, _tickInterval.TotalSeconds, _workflowsEnabled);
    }

    /// <summary>Stops timers, unsubscribes every EventBus subscription and clears the cache. Safe to call twice.</summary>
    public Task StopAsync()
    {
        // Unhook the Discord interaction bridge first so no invocation can
        // start a run while the caches are being torn down.
        DetachDiscordBridge();

        lock (_sync)
        {
            _started = false;

            var timer = _tickTimer;
            _tickTimer = null;
            timer?.Dispose();

            foreach (var subscriptionId in _subscriptionIds)
            {
                try
                {
                    _eventBus.Unsubscribe(subscriptionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unsubscribe workflow trigger {SubscriptionId}", subscriptionId);
                }
            }

            _subscriptionIds.Clear();
            _commandIndex.Clear();
            _eventIndex.Clear();
            _slashIndex.Clear();
            _workflowById.Clear();
            _workflowByComponentKey.Clear();
            _timerWorkflows.Clear();
            _lastTimerRun.Clear();
            _lastOptionRun.Clear();
        }

        _recentSlashInvocations.Clear();

        _logger.LogInformation("WorkflowTriggerService stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rebuilds the workflow cache from the database (for the editor UI after
    /// saving a workflow). Does not touch subscriptions — event dispatch does a
    /// cheap index lookup per event.
    /// </summary>
    public async Task ReloadAsync()
    {
        await LoadWorkflowsAsync();
        _logger.LogInformation("Workflow trigger cache reloaded (command names: {Commands}, event types: {EventTypes}, timer workflows: {Timers})",
            CommandCount, _eventIndex.Count, _timerWorkflows.Count);
    }

    /// <summary>
    /// Re-reads the Settings → Automation values (master toggle, per-trigger-type
    /// toggles, timer interval, execution-history retention). Called on StartAsync
    /// and again whenever those settings change, so toggles apply immediately.
    /// </summary>
    public async Task ReloadSettingsAsync()
    {
        try
        {
            _workflowsEnabled = await GetBoolSettingAsync("Automation.WorkflowsEnabled", true);
            _commandTriggersEnabled = await GetBoolSettingAsync("Automation.CommandTriggersEnabled", true);
            _eventTriggersEnabled = await GetBoolSettingAsync("Automation.EventTriggersEnabled", true);
            _timerTriggersEnabled = await GetBoolSettingAsync("Automation.TimerTriggersEnabled", true);

            // Keep the workflow command prefixes in sync with Core's CommandService
            var prefixes = await _dataService.GetConfigValueAsync("Commands.Prefixes");
            if (!string.IsNullOrWhiteSpace(prefixes))
            {
                var chars = prefixes.Where(c => !char.IsWhiteSpace(c)).Distinct().ToArray();
                if (chars.Length > 0)
                {
                    _commandPrefixes = chars;
                }
            }

            var intervalSeconds = await GetIntSettingAsync("Automation.TimerIntervalSeconds", 60);
            _tickInterval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 30, 600));

            // Apply a changed tick interval to a running scheduler
            if (_tickTimer != null)
            {
                _tickTimer.Change(_tickInterval, _tickInterval);
            }

            // Prune execution history older than the retention window
            var retentionDays = await GetIntSettingAsync("Automation.ExecutionHistoryRetentionDays", 90);
            if (retentionDays > 0)
            {
                try
                {
                    await _dataService.DeleteWorkflowExecutionsAsync(olderThan: DateTime.UtcNow.AddDays(-retentionDays));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to prune workflow execution history older than {Days} days", retentionDays);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading automation settings — keeping current values");
        }
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private async Task<int> GetIntSettingAsync(string key, int defaultValue)
    {
        var value = await _dataService.GetConfigValueAsync(key);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private int CommandCount
    {
        get { lock (_sync) return _commandIndex.Values.Sum(list => list.Count); }
    }

    // ======================================================================
    // COMMAND TRIGGERS (called from ChatViewModel's generic pipeline)
    // ======================================================================

    /// <summary>
    /// Command-trigger matching for an incoming chat message. Returns true when
    /// a workflow command trigger claimed the message — the caller must NOT run
    /// the regular command pipeline for it. Returns false for empty messages,
    /// non-command messages (must start with '!', '?' or '.') and names with no
    /// ENABLED command workflow, so the existing pipeline is never disturbed.
    /// Execution is fire-and-forget; matching itself never throws.
    /// </summary>
    /// <param name="platform">Origin platform ("Twitch", "Kick", "YouTube", "Discord", ...).</param>
    /// <param name="username">Sender login name.</param>
    /// <param name="message">Raw message text.</param>
    /// <param name="channel">Channel the message was received in.</param>
    /// <param name="discordBotId">Discord bot origin when applicable (routed back on sends).</param>
    /// <param name="reply">
    /// Optional direct-reply callback supplied by the caller; exposed to the
    /// workflow as the execution's Origin so nodes have a guaranteed-origin
    /// reply path in addition to the runtime's MessageSender. Also receives
    /// cooldown-blocked notices.
    /// </param>
    /// <param name="userRoles">
    /// Optional comma-separated role names of the sender ("moderator,broadcaster",
    /// Discord author role names, Twitch/Kick badge-derived roles). Seeds the
    /// context's "userRoles"/"isModerator" variables for the permission option.
    /// Null where unavailable — permission checks then allow (documented V1).
    /// </param>
    /// <param name="userId">Optional platform user id of the sender; seeds the "userId" variable.</param>
    /// <param name="isBot">
    /// Whether the sender is a known bot account; seeds the "isBot" variable.
    /// Callers already filter their own echoes upstream, so false is the safe default.
    /// </param>
    public bool TryHandleCommand(
        string platform,
        string username,
        string message,
        string channel,
        string? discordBotId,
        Func<string, Task>? reply = null,
        string? userRoles = null,
        string? userId = null,
        bool isBot = false)
    {
        try
        {
            if (!_started || string.IsNullOrWhiteSpace(message) || !_workflowsEnabled || !_commandTriggersEnabled)
            {
                return false;
            }

            var trimmed = message.TrimStart();
            if (trimmed.Length < 2)
            {
                return false; // need a prefix + at least one command character
            }

            var prefix = trimmed[0];
            if (Array.IndexOf(_commandPrefixes, prefix) < 0)
            {
                return false; // not a command message — leave the pipeline alone
            }

            var separatorIndex = trimmed.IndexOfAny(Whitespace);
            var token = separatorIndex < 0 ? trimmed : trimmed.Substring(0, separatorIndex); // e.g. "!hello"
            var name = token.TrimStart(_commandPrefixes); // "hello"
            if (name.Length == 0)
            {
                return false;
            }

            List<Workflow> matches;
            lock (_sync)
            {
                if (!_commandIndex.TryGetValue(name, out var candidates))
                {
                    return false; // no workflow registered this command name
                }

                matches = candidates
                    .Where(w => w.Trigger != null
                        && PlatformMatches(w.Trigger.Platform, platform)
                        && PrefixMatches(w.Trigger.CommandPrefix, prefix))
                    .ToList();
            }

            if (matches.Count == 0)
            {
                return false;
            }

            var args = separatorIndex < 0 ? string.Empty : trimmed.Substring(separatorIndex).Trim();
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["args"] = args,
                ["command"] = name
            };

            // Discord reports DM conversations with the channel name
            // "DirectMessage"; every other platform is a public chat channel.
            var isDirectMessage = IsDirectMessageChannel(channel);

            foreach (var workflow in matches)
            {
                // Command-level options (cooldowns / channel restrictions /
                // DM-guild scoping) gate each workflow BEFORE it runs. A
                // blocked cooldown answers through the origin reply when one
                // exists; every other block is silent.
                var (allowed, blockReply) = EvaluateOptions(workflow, username, channel, isDirectMessage);
                if (!allowed)
                {
                    _ = SendOptionReplyAsync(reply, blockReply, workflow);
                    continue;
                }

                var context = BuildContext(
                    workflow, platform, channel, username, message, discordBotId, reply, variables,
                    userRoles, userId, isBot);

                // Permission option ("Moderator" / role names) is enforced
                // post-context against the seeded identity variables.
                if (!TryAllowPermission(workflow, context))
                {
                    continue;
                }

                ExecuteInBackground(workflow, context);
            }

            _logger.LogInformation("Workflow command trigger {Token} matched {Count} workflow(s) on {Platform}",
                token, matches.Count, platform);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow command trigger matching failed for message: {Message}", message);
            return false; // never break the existing command pipeline
        }
    }

    // ======================================================================
    // DISCORD SLASH COMMAND + COMPONENT TRIGGERS (via WorkflowDiscordBridge)
    // ======================================================================

    /// <summary>
    /// Subscribes the bridge's interaction events. No-op when no bridge was
    /// injected. Called from <see cref="StartAsync"/>; undone in <see cref="StopAsync"/>.
    /// </summary>
    private void AttachDiscordBridge()
    {
        if (_discordBridge == null)
        {
            return;
        }

        _discordBridge.SlashInvocationReceived += OnBridgeSlashInvocation;
        _discordBridge.ComponentInteractionReceived += OnBridgeComponentInteraction;
    }

    /// <summary>Unsubscribes the bridge's interaction events (no-op without a bridge).</summary>
    private void DetachDiscordBridge()
    {
        if (_discordBridge == null)
        {
            return;
        }

        _discordBridge.SlashInvocationReceived -= OnBridgeSlashInvocation;
        _discordBridge.ComponentInteractionReceived -= OnBridgeComponentInteraction;
    }

    private void OnBridgeSlashInvocation(object? sender, WorkflowSlashInvocation invocation)
    {
        try
        {
            TryHandleSlashCommand(invocation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow slash invocation handling failed for '/{Command}'", invocation?.CommandName);
        }
    }

    private void OnBridgeComponentInteraction(object? sender, WorkflowComponentInteraction interaction)
    {
        try
        {
            TryHandleComponent(interaction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow component interaction handling failed for '{CustomId}'", interaction?.CustomId);
        }
    }

    /// <summary>
    /// Slash-command trigger matching for a Discord interaction routed through
    /// the bridge. Returns true when the invocation was CLAIMED — either a
    /// workflow ran (fire-and-forget) or the dedup guard recognized a duplicate
    /// delivery within the last few seconds. Returns false when the command
    /// name matches no ENABLED slash workflow, so foreign commands never
    /// swallow unrelated handling. Matching itself never throws.
    /// </summary>
    /// <remarks>
    /// DEDUP GUARD: the same user action can in principle surface through more
    /// than one path (the native interaction event and any legacy synthetic
    /// "/" delivery). First delivery within a 5-second window per
    /// bot|command|user|channel executes; later duplicates are consumed as
    /// claimed without re-running.
    /// </remarks>
    public bool TryHandleSlashCommand(WorkflowSlashInvocation? invocation)
    {
        if (invocation == null || !_started || !_workflowsEnabled)
        {
            return false;
        }

        // Same normalization the DiscordService registration uses, so the
        // authored CommandName and the invoked name always meet in the middle.
        var name = NormalizeSlashCommandName(invocation.CommandName);
        if (name.Length == 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var dedupKey = $"{invocation.BotId}|{name}|{invocation.Username}|{invocation.ChannelName}".ToLowerInvariant();
        if (IsDuplicateSlashInvocation(dedupKey, now))
        {
            _logger.LogDebug("Duplicate workflow slash invocation '/{Command}' from {Username} suppressed (dedup window)",
                name, invocation.Username);
            return true; // consumed — do not re-run
        }

        List<Workflow> matches;
        lock (_sync)
        {
            if (!_slashIndex.TryGetValue(name, out var candidates))
            {
                return false; // not one of ours — leave any other handling alone
            }

            matches = candidates
                .Where(w => w.Trigger != null && PlatformMatches(w.Trigger.Platform, "Discord"))
                .ToList();
        }

        if (matches.Count == 0)
        {
            return false;
        }

        foreach (var workflow in matches)
        {
            _ = ExecuteSlashWorkflowAsync(workflow, invocation, name);
        }

        _logger.LogInformation("Workflow slash command '/{Name}' matched {Count} workflow(s) (bot {BotId}, user {Username})",
            name, matches.Count, invocation.BotId, invocation.Username);
        return true;
    }

    /// <summary>
    /// Component (button/select-menu) trigger matching for a Discord
    /// interaction. COMPONENT CHILD WORKFLOWS (button/menu sub-flows the editor
    /// materializes as real workflow rows) resolve by EXACT ComponentKey ==
    /// custom id; everything else keeps the "vyralwf:{workflowId}(:{extra})?"
    /// id-based path. Foreign, unknown or non-component workflows are silently
    /// ignored (debug log) and false is returned. Returns true when a workflow
    /// execution was started.
    /// </summary>
    public bool TryHandleComponent(WorkflowComponentInteraction? interaction)
    {
        if (interaction == null || !_started || !_workflowsEnabled)
        {
            return false;
        }

        var customId = interaction.CustomId ?? string.Empty;
        if (!customId.StartsWith(ComponentCustomIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false; // not a workflow component
        }

        var payload = customId.Substring(ComponentCustomIdPrefix.Length);
        var separatorIndex = payload.IndexOf(':');
        var workflowId = (separatorIndex < 0 ? payload : payload.Substring(0, separatorIndex)).Trim();
        var extra = separatorIndex < 0 ? string.Empty : payload.Substring(separatorIndex + 1);
        if (workflowId.Length == 0)
        {
            _logger.LogDebug("Workflow component ignored: no workflow id in custom id '{CustomId}'", customId);
            return false;
        }

        Workflow? workflow;
        lock (_sync)
        {
            // 1) Component child workflows: ComponentKey equals the full custom
            //    id ("vyralwf:{parentId}:{nodeId}:btn{i}" / "...:menu").
            // 2) Otherwise the classic by-id path (parent workflows).
            if (!_workflowByComponentKey.TryGetValue(customId, out workflow))
            {
                _workflowById.TryGetValue(workflowId, out workflow);
            }
        }

        if (workflow == null || !workflow.Enabled)
        {
            _logger.LogDebug("Workflow component ignored: no enabled workflow for id {WorkflowId} ('{CustomId}')",
                workflowId, customId);
            return false;
        }

        var triggerType = (workflow.Trigger?.Type ?? string.Empty).Trim().ToLowerInvariant();
        if (triggerType != WorkflowTriggerTypes.ButtonClick && triggerType != WorkflowTriggerTypes.SelectMenu)
        {
            _logger.LogDebug("Workflow component ignored: workflow {WorkflowId} trigger type '{Type}' is not a component trigger",
                workflowId, triggerType);
            return false;
        }

        _ = ExecuteComponentWorkflowAsync(workflow, interaction, extra);
        return true;
    }

    /// <summary>
    /// Options enforcement + execution for one matched slash workflow
    /// (fire-and-forget; never throws). Options gate BEFORE the run, the
    /// permission option gates post-context, then the workflow runs with
    /// Platform="Discord" and per-origin bot routing.
    /// </summary>
    private async Task ExecuteSlashWorkflowAsync(Workflow workflow, WorkflowSlashInvocation invocation, string name)
    {
        try
        {
            var args = invocation.Arguments ?? string.Empty;
            var message = $"/{name} {args}".Trim();
            var channel = invocation.ChannelName ?? string.Empty;
            var username = invocation.Username ?? string.Empty;
            var isDirectMessage = IsDirectMessageChannel(channel);

            var (allowed, blockReply) = EvaluateOptions(workflow, username, channel, isDirectMessage);
            if (!allowed)
            {
                await SendOptionReplyAsync(invocation.Reply, blockReply, workflow).ConfigureAwait(false);
                return;
            }

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["args"] = args,
                ["command"] = name
            };

            // Typed slash options: one variable per Discord option (plus the
            // mirrored authored parameter names) IN ADDITION to args/command.
            if (invocation.OptionValues != null)
            {
                foreach (var kvp in invocation.OptionValues)
                {
                    variables[kvp.Key] = kvp.Value ?? string.Empty;
                }
            }

            var context = BuildContext(
                workflow, "Discord", channel, username, message, invocation.BotId, invocation, variables);
            if (!string.IsNullOrEmpty(invocation.GuildName))
            {
                context.GuildName = invocation.GuildName;
            }

            if (!TryAllowPermission(workflow, context))
            {
                // Close the deferred interaction with a generic denial notice —
                // the specifics stay in the logs (they can leak moderation
                // config) but the user must not hang in the "thinking" state.
                await SendOptionReplyAsync(invocation.Reply, $"⛔ You are not allowed to use /{name}.", workflow).ConfigureAwait(false);
                return;
            }

            ExecuteInBackground(workflow, context, result =>
                SendInteractionCompletionFollowupAsync(invocation.Reply, $"/{name}", result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow slash execution failed for '/{Name}' (workflow '{WorkflowId}')", name, workflow?.Id);
        }
    }

    /// <summary>
    /// Options enforcement + execution for one matched component interaction
    /// (fire-and-forget; never throws). Seeds {customId}, {buttonId} (the
    /// optional extra segment) and {values} (comma-joined select values).
    /// </summary>
    private async Task ExecuteComponentWorkflowAsync(Workflow workflow, WorkflowComponentInteraction interaction, string extra)
    {
        try
        {
            var customId = interaction.CustomId ?? string.Empty;
            var channel = interaction.ChannelName ?? string.Empty;
            var username = interaction.Username ?? string.Empty;
            var isDirectMessage = IsDirectMessageChannel(channel);

            var (allowed, blockReply) = EvaluateOptions(workflow, username, channel, isDirectMessage);
            if (!allowed)
            {
                await SendOptionReplyAsync(interaction.Reply, blockReply, workflow).ConfigureAwait(false);
                return;
            }

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customId"] = customId,
                ["buttonId"] = extra ?? string.Empty,
                ["values"] = string.Join(",", interaction.Values ?? Array.Empty<string>())
            };

            var context = BuildContext(
                workflow, "Discord", channel, username, customId, interaction.BotId, interaction, variables);
            if (!string.IsNullOrEmpty(interaction.GuildName))
            {
                context.GuildName = interaction.GuildName;
            }

            if (!TryAllowPermission(workflow, context))
            {
                await SendOptionReplyAsync(interaction.Reply, "⛔ You are not allowed to use this.", workflow).ConfigureAwait(false);
                return;
            }

            ExecuteInBackground(workflow, context, result =>
                SendInteractionCompletionFollowupAsync(interaction.Reply, workflow.Name, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow component execution failed for '{CustomId}' (workflow '{WorkflowId}')",
                interaction?.CustomId, workflow?.Id);
        }
    }

    /// <summary>
    /// True when this exact invocation was already delivered within the dedup
    /// window; otherwise records it as the first delivery. Bounded: expired
    /// entries are pruned on each check.
    /// </summary>
    private bool IsDuplicateSlashInvocation(string dedupKey, DateTime now)
    {
        foreach (var kvp in _recentSlashInvocations)
        {
            if (now - kvp.Value >= SlashDedupWindow)
            {
                _recentSlashInvocations.TryRemove(kvp.Key, out _);
            }
        }

        if (_recentSlashInvocations.TryGetValue(dedupKey, out var firstSeen)
            && now - firstSeen < SlashDedupWindow)
        {
            return true;
        }

        _recentSlashInvocations[dedupKey] = now;
        return false;
    }

    // ======================================================================
    // COMMAND OPTIONS (cooldowns / permissions / channel + DM scoping)
    // ======================================================================

    /// <summary>
    /// Pre-context enforcement of <see cref="WorkflowOption"/> constraints.
    /// Cooldowns apply ONLY to user-invoked triggers (command / slash /
    /// button / select — never timer/event/manual); a blocked cooldown yields
    /// a reply message ("{name} is on cooldown ({n}s)") sent through the
    /// origin when a reply path exists, while every other block is silent.
    /// The permission option is deliberately NOT checked here — it needs the
    /// seeded identity variables, so <see cref="TryAllowPermission"/> enforces
    /// it after the context is built. Never throws.
    /// </summary>
    private (bool Allowed, string? BlockReply) EvaluateOptions(
        Workflow workflow,
        string username,
        string channel,
        bool isDirectMessage)
    {
        try
        {
            var options = workflow.Options;
            if (options == null || options.Count == 0)
            {
                return (true, null);
            }

            var triggerType = (workflow.Trigger?.Type ?? string.Empty).Trim().ToLowerInvariant();
            var isUserInvoked = triggerType == WorkflowTriggerTypes.Command
                || triggerType == WorkflowTriggerTypes.SlashCommand
                || triggerType == WorkflowTriggerTypes.ButtonClick
                || triggerType == WorkflowTriggerTypes.SelectMenu;

            foreach (var option in options)
            {
                if (option == null)
                {
                    continue;
                }

                var type = (option.Type ?? string.Empty).Trim();
                var value = (option.Value ?? string.Empty).Trim();
                if (type.Length == 0)
                {
                    continue;
                }

                if (string.Equals(type, WorkflowOptionTypes.Cooldown, StringComparison.OrdinalIgnoreCase))
                {
                    if (!isUserInvoked)
                    {
                        continue; // timer/event/manual runs never cool down
                    }

                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                        || seconds <= 0)
                    {
                        continue; // unparseable value — never block on authoring mistakes
                    }

                    var key = $"{workflow.Id}|{username}".ToLowerInvariant();
                    var now = DateTime.UtcNow;
                    lock (_sync)
                    {
                        if (_lastOptionRun.TryGetValue(key, out var lastRun)
                            && now - lastRun < TimeSpan.FromSeconds(seconds))
                        {
                            _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) blocked for {Username}: cooldown ({Seconds}s)",
                                workflow.Name, workflow.Id, username, seconds);
                            return (false, $"{workflow.Name} is on cooldown ({seconds}s)");
                        }

                        _lastOptionRun[key] = now;
                    }
                }
                else if (string.Equals(type, WorkflowOptionTypes.ChannelRestriction, StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Length == 0)
                    {
                        continue; // empty value = allow all channels
                    }

                    var allowedChannels = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var channelAllowed = allowedChannels.Any(entry => ChannelMatchesRestriction(entry, channel));
                    if (!channelAllowed)
                    {
                        _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) blocked: channel '{Channel}' is not in [{Channels}]",
                            workflow.Name, workflow.Id, channel, value);
                        return (false, null); // silent — the channel simply is not served
                    }
                }
                else if (string.Equals(type, WorkflowOptionTypes.DmOnly, StringComparison.OrdinalIgnoreCase))
                {
                    // LIMITATION (V1): DM-ness is derived from the Discord
                    // "DirectMessage" channel convention; a default of false
                    // is treated as guild context, so dm_only workflows never
                    // fire from non-Discord origins.
                    if (IsTruthyOptionValue(value) && !isDirectMessage)
                    {
                        _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) blocked: usable in DMs only", workflow.Name, workflow.Id);
                        return (false, null);
                    }
                }
                else if (string.Equals(type, WorkflowOptionTypes.GuildOnly, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsTruthyOptionValue(value) && isDirectMessage)
                    {
                        _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) blocked: usable in guilds only", workflow.Name, workflow.Id);
                        return (false, null);
                    }
                }

                // "permission" → post-context (TryAllowPermission);
                // "nsfw" → informational in V1 (no enforcement).
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            // Never let an option-authoring mistake break a trigger.
            _logger.LogWarning(ex, "Workflow option evaluation failed for '{Name}' ({WorkflowId}) — allowing",
                workflow.Name, workflow.Id);
            return (true, null);
        }
    }

    /// <summary>
    /// Post-context enforcement of the permission option. Value "Everyone"
    /// (or unset) allows; "Moderator" requires the seeded "userRoles" to name
    /// moderator/broadcaster OR an "isModerator" variable of "true"; any other
    /// value is treated as a role name checked against "userRoles". When the
    /// caller could not seed roles (unknown platform payload) the check
    /// ALLOWS with a debug log — documented V1 fail-open for unseeded contexts.
    /// </summary>
    private bool TryAllowPermission(Workflow workflow, ExecutionContext context)
    {
        try
        {
            var options = workflow.Options;
            if (options == null)
            {
                return true;
            }

            var permission = options.FirstOrDefault(o =>
                    o != null && string.Equals(o.Type, WorkflowOptionTypes.Permission, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(permission))
            {
                return true; // no permission option authored
            }

            var required = permission.Trim();
            if (string.Equals(required, "Everyone", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var rolesRaw = context.GetVariable("userRoles");
            var hasRoles = !string.IsNullOrWhiteSpace(rolesRaw);
            var roles = hasRoles
                ? rolesRaw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(r => r.ToLowerInvariant())
                    .ToArray()
                : Array.Empty<string>();

            if (string.Equals(required, "Moderator", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(context.GetVariable("isModerator"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!hasRoles)
                {
                    _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) moderator check skipped: no roles seeded for {Username}",
                        workflow.Name, workflow.Id, context.Username);
                    return true; // unseeded context — fail-open (documented)
                }

                if (roles.Contains("moderator") || roles.Contains("broadcaster"))
                {
                    return true;
                }

                _logger.LogInformation("Workflow '{Name}' ({WorkflowId}) denied for {Username}: moderator permission required",
                    workflow.Name, workflow.Id, context.Username);
                return false;
            }

            // Role-name membership (Discord/Twitch role authored verbatim).
            if (!hasRoles)
            {
                _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) role check skipped: no roles seeded for {Username}",
                    workflow.Name, workflow.Id, context.Username);
                return true; // unseeded context — fail-open (documented)
            }

            var requiredRole = required.ToLowerInvariant();
            if (roles.Contains(requiredRole))
            {
                return true;
            }

            _logger.LogInformation("Workflow '{Name}' ({WorkflowId}) denied for {Username}: role '{Role}' required",
                workflow.Name, workflow.Id, context.Username, required);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow permission check failed for '{Name}' ({WorkflowId}) — allowing",
                workflow.Name, workflow.Id);
            return true; // never let a check bug break a trigger
        }
    }

    /// <summary>Sends an option-block notice through the origin reply path (best-effort, never throws).</summary>
    private async Task SendOptionReplyAsync(Func<string, Task>? reply, string? blockReply, Workflow workflow)
    {
        if (reply == null || string.IsNullOrEmpty(blockReply))
        {
            return;
        }

        try
        {
            await reply(blockReply).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to deliver workflow option notice for '{Name}'", workflow.Name);
        }
    }

    /// <summary>Channel-restriction entry match: trims, ignores a leading '#', case-insensitive; unknown origin channels never match.</summary>
    private static bool ChannelMatchesRestriction(string restrictedEntry, string? channel)
    {
        var entry = (restrictedEntry ?? string.Empty).Trim().TrimStart('#');
        var actual = (channel ?? string.Empty).Trim().TrimStart('#');
        if (entry.Length == 0 || actual.Length == 0)
        {
            return false; // unknown channel when a list is set → block
        }

        return string.Equals(entry, actual, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for "true"/"yes"/"1" option values (dm_only/guild_only/nsfw flags).</summary>
    private static bool IsTruthyOptionValue(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v is "true" or "yes" or "1";
    }

    /// <summary>Discord reports DM conversations with the channel name "DirectMessage" (see DiscordService).</summary>
    private static bool IsDirectMessageChannel(string? channel)
    {
        return string.Equals((channel ?? string.Empty).Trim(), "DirectMessage", StringComparison.OrdinalIgnoreCase);
    }

    // ======================================================================
    // MANUAL TRIGGERS (editor "Run" button)
    // ======================================================================

    /// <summary>
    /// Runs a workflow immediately with a synthetic context (username "Manual",
    /// platform from the workflow's trigger, falling back to a connected one).
    /// Intentionally ignores <see cref="Workflow.Enabled"/> so disabled drafts
    /// stay debuggable.
    /// </summary>
    public void RunManual(Workflow workflow)
    {
        if (workflow == null || workflow.Trigger == null)
        {
            _logger.LogWarning("Manual workflow run skipped: workflow or trigger missing");
            return;
        }

        var platform = ResolveTriggerPlatform(workflow.Trigger);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eventType"] = "manual",
            ["command"] = workflow.Trigger.CommandName ?? string.Empty
        };
        var context = BuildContext(workflow, platform, ChannelFor(platform), "Manual", string.Empty, null, workflow, variables);

        ExecuteInBackground(workflow, context);
        _logger.LogInformation("Manual run of workflow '{Name}' ({WorkflowId})", workflow.Name, workflow.Id);
    }

    // ======================================================================
    // TIMER TRIGGERS (60s tick)
    // ======================================================================

    private void TickTimers()
    {
        if (!_started || !_workflowsEnabled || !_timerTriggersEnabled
            || Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            List<Workflow> timers;
            lock (_sync)
            {
                timers = _timerWorkflows.ToList();
            }

            if (timers.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var workflow in timers)
            {
                var trigger = workflow.Trigger;
                var intervalMinutes = trigger?.IntervalMinutes ?? 0;
                if (trigger == null || intervalMinutes <= 0)
                {
                    continue;
                }

                lock (_sync)
                {
                    // In-memory V1 last-run: never-run timers fire on the first tick.
                    if (_lastTimerRun.TryGetValue(workflow.Id ?? string.Empty, out var lastRun)
                        && now - lastRun < TimeSpan.FromMinutes(intervalMinutes))
                    {
                        continue;
                    }
                    _lastTimerRun[workflow.Id ?? string.Empty] = now;
                }

                var platform = ResolveTriggerPlatform(trigger);
                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["eventType"] = "timer"
                };
                var context = BuildContext(workflow, platform, ChannelFor(platform), "Timer", string.Empty, null, null, variables);
                ExecuteInBackground(workflow, context);
                _logger.LogInformation("Timer workflow '{Name}' ({WorkflowId}) fired ({Interval}m interval)",
                    workflow.Name, workflow.Id, intervalMinutes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkflowTriggerService timer tick failed");
        }
        finally
        {
            Volatile.Write(ref _tickRunning, 0);
        }
    }

    // ======================================================================
    // EVENT TRIGGERS (typed EventBus subscriptions)
    // ======================================================================

    private async Task SubscribeToEventsAsync()
    {
        lock (_sync)
        {
            if (_subscriptionIds.Count > 0 || !_started)
            {
                return;
            }
        }

        async Task Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
        {
            try
            {
                var subscriptionId = await _eventBus.SubscribeAsync(handler);
                lock (_sync) _subscriptionIds.Add(subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WorkflowTriggerService failed to subscribe to {EventType}", typeof(TEvent).Name);
            }
        }

        // --- Twitch (phase 14) ---
        await Subscribe<TwitchFollowEvent>(e =>
        {
            DispatchEvent("follow", "Twitch", e.Username, null, e,
                Vars(("userId", e.UserId), ("displayName", e.DisplayName)));
            return Task.CompletedTask;
        });

        await Subscribe<TwitchSubscribeEvent>(e =>
        {
            DispatchEvent("subscribe", "Twitch", e.Username, e.Message, e,
                Vars(("tier", e.Tier), ("months", e.MonthsTotal), ("streak", e.StreakMonths), ("isGift", e.IsGift)));
            return Task.CompletedTask;
        });

        await Subscribe<TwitchResubEvent>(e =>
        {
            DispatchEvent("resub", "Twitch", e.Username, e.Message, e,
                Vars(("tier", e.Tier), ("months", e.CumulativeMonths), ("streak", e.StreakMonths), ("duration", e.DurationMonths)));
            return Task.CompletedTask;
        });

        await Subscribe<TwitchGiftSubEvent>(e =>
        {
            DispatchEvent("giftsub", "Twitch", e.GifterUsername, null, e,
                Vars(("tier", e.Tier), ("count", e.Total), ("total", e.CumulativeTotal), ("isAnonymous", e.IsAnonymous)));
            return Task.CompletedTask;
        });

        await Subscribe<TwitchRaidEvent>(e =>
        {
            DispatchEvent("raid", "Twitch", e.FromBroadcasterUsername, null, e,
                Vars(("viewers", e.Viewers)));
            return Task.CompletedTask;
        });

        await Subscribe<TwitchCheerEvent>(e =>
        {
            DispatchEvent("cheer", "Twitch", e.IsAnonymous ? "Anonymous" : e.Username, e.Message, e,
                Vars(("bits", e.Bits), ("isAnonymous", e.IsAnonymous)));
            return Task.CompletedTask;
        });

        // --- Kick (phase 15) ---
        await Subscribe<KickFollowEvent>(e =>
        {
            DispatchEvent("follow", "Kick", e.Username, null, e,
                Vars(("count", e.FollowerCount)));
            return Task.CompletedTask;
        });

        await Subscribe<KickSubscribeEvent>(e =>
        {
            DispatchEvent("subscribe", "Kick", e.Username, e.Message, e,
                Vars(("months", e.MonthsSubscribed)));
            return Task.CompletedTask;
        });

        await Subscribe<KickGiftSubEvent>(e =>
        {
            DispatchEvent("giftsub", "Kick", e.GifterUsername, null, e,
                Vars(("count", e.Count), ("recipients", string.Join(", ", e.RecipientUsernames))));
            return Task.CompletedTask;
        });

        await Subscribe<KickHostEvent>(e =>
        {
            DispatchEvent("host", "Kick", e.HostUsername, null, e,
                Vars(("viewers", e.ViewerCount)));
            return Task.CompletedTask;
        });

        _logger.LogInformation("WorkflowTriggerService subscribed to {Count} event types", _subscriptionIds.Count);
    }

    /// <summary>
    /// Finds enabled event workflows for <paramref name="eventType"/> on
    /// <paramref name="platform"/> and executes each fire-and-forget with a
    /// context whose Variables carry the eventType plus best-effort event
    /// fields (viewers/bits/months/count/...). Workflows with an authored
    /// Trigger.Conditions guard expression only run when it evaluates true.
    /// Never throws.
    /// </summary>
    private void DispatchEvent(
        string eventType,
        string platform,
        string username,
        string? message,
        object? origin,
        Dictionary<string, string> variables)
    {
        try
        {
            List<Workflow> matches;
            lock (_sync)
            {
                if (!_started || !_workflowsEnabled || !_eventTriggersEnabled
                    || !_eventIndex.TryGetValue(eventType, out var candidates))
                {
                    return;
                }

                matches = candidates
                    .Where(w => w.Trigger != null && PlatformMatches(w.Trigger.Platform, platform))
                    .ToList();
            }

            if (matches.Count == 0)
            {
                return;
            }

            variables["eventType"] = eventType;
            var channel = ChannelFor(platform);

            foreach (var workflow in matches)
            {
                var context = BuildContext(workflow, platform, channel, username, message ?? string.Empty, null, origin, variables);

                // Optional per-trigger guard expression (e.g. "{viewers} > 50"),
                // evaluated against the context variables the trigger produced.
                // Only evaluated when authored — an unset Conditions never blocks.
                var conditions = workflow.Trigger!.Conditions;
                if (!string.IsNullOrWhiteSpace(conditions)
                    && !ExpressionEvaluator.Evaluate(conditions, context))
                {
                    _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) skipped: event conditions not met ({Conditions})",
                        workflow.Name, workflow.Id, conditions);
                    continue;
                }

                ExecuteInBackground(workflow, context);
            }

            _logger.LogInformation("Workflow event trigger {EventType} on {Platform} fired {Count} workflow(s)",
                eventType, platform, matches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow event dispatch failed for {EventType} on {Platform}", eventType, platform);
        }
    }

    // ======================================================================
    // SLASH AUTO-REGISTRATION (best-effort sync to every connected bot)
    // ======================================================================

    /// <summary>
    /// Registers every enabled slash workflow's command name on every
    /// CONNECTED Discord bot (manager bots plus the setup wizard's singleton)
    /// and removes commands whose workflows vanished (deleted, disabled or
    /// renamed — the <paramref name="removedCommands"/> diff). Skips silently
    /// when no bot is running or no slash workflow exists; individual failures
    /// are logged and never thrown. Fired after every cache load (start +
    /// editor reloads); bots that start later are covered by the manager's
    /// ServiceStarted hook (<see cref="OnBotServiceStarted"/>).
    /// </summary>
    private async Task SyncWorkflowSlashCommandsAsync(IReadOnlyCollection<string>? removedCommands = null)
    {
        try
        {
            if (!_started)
            {
                return;
            }

            var services = CollectConnectedDiscordServices();
            if (services.Count == 0)
            {
                _logger.LogDebug("Workflow slash sync skipped: no Discord bot is connected");
                return;
            }

            var commands = SnapshotSlashCommands();
            foreach (var service in services)
            {
                await RegisterSlashCommandsOnServiceAsync(service, commands).ConfigureAwait(false);

                if (removedCommands is { Count: > 0 })
                {
                    await UnregisterSlashCommandsOnServiceAsync(service, removedCommands).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow slash command sync failed (non-fatal)");
        }
    }

    /// <summary>Manager ServiceStarted hook: pushes the current slash commands to a newly started bot.</summary>
    private void OnBotServiceStarted(object? sender, DiscordService service)
    {
        try
        {
            if (service == null || !_started)
            {
                return;
            }

            _ = RegisterSlashCommandsOnServiceAsync(service, SnapshotSlashCommands());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Workflow slash re-registration on bot start failed for {BotId}", service?.BotId);
        }
    }

    /// <summary>
    /// Snapshot of the distinct normalized slash command names, their
    /// user-facing descriptions ("Description ?? '{name} command'") and each
    /// workflow's typed parameters (null when none are authored — the Discord
    /// side then registers the legacy "args" option). Component CHILD
    /// workflows (button_click/select_menu rows with a ComponentKey) dispatch
    /// by custom id and are never registered as slash commands.
    /// </summary>
    private List<(string Name, string Description, IReadOnlyList<WorkflowParameter>? Parameters)> SnapshotSlashCommands()
    {
        lock (_sync)
        {
            return _slashIndex.Values
                .SelectMany(list => list)
                .Where(w => !IsComponentChildWorkflow(w))
                .Select(w => new { Workflow = w, Name = NormalizeSlashCommandName(w.Trigger?.CommandName) })
                .Where(x => x.Name.Length > 0)
                .Select(x => (x.Name,
                    Description: string.IsNullOrWhiteSpace(x.Workflow.Description)
                        ? $"{x.Name} command"
                        : x.Workflow.Description!.Trim(),
                    Parameters: (IReadOnlyList<WorkflowParameter>?)x.Workflow.Parameters))
                .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// True for component CHILD workflows (the editor materializes button/menu
    /// sub-flows as real workflow rows): a component trigger type combined with
    /// a ComponentKey. They are dispatched by their exact component custom id,
    /// never as slash commands.
    /// </summary>
    private static bool IsComponentChildWorkflow(Workflow workflow)
    {
        var triggerType = (workflow.Trigger?.Type ?? string.Empty).Trim().ToLowerInvariant();
        if (triggerType != WorkflowTriggerTypes.ButtonClick && triggerType != WorkflowTriggerTypes.SelectMenu)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(workflow.ComponentKey);
    }

    /// <summary>
    /// Every connected DiscordService reachable right now: the manager's
    /// connected bots (deduplicated by BotId) plus the setup wizard's DI
    /// singleton when connected. Empty list when nothing runs.
    /// </summary>
    private List<DiscordService> CollectConnectedDiscordServices()
    {
        var services = new List<DiscordService>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var connected = _botManager?.Connected;
            if (connected != null)
            {
                foreach (var service in connected)
                {
                    if (service is DiscordService concrete && concrete.IsConnected
                        && seen.Add(concrete.BotId ?? string.Empty))
                    {
                        services.Add(concrete);
                    }
                }
            }

            if (_botManager?.Primary is DiscordService primaryConcrete && primaryConcrete.IsConnected
                && seen.Add(primaryConcrete.BotId ?? string.Empty))
            {
                services.Add(primaryConcrete);
            }

            // The setup wizard's singleton bot is outside the manager's
            // running set until adopted — include it when connected.
            if (_serviceProvider.GetService(typeof(IDiscordService)) is DiscordService wizard
                && wizard.IsConnected && seen.Add(wizard.BotId ?? string.Empty))
            {
                services.Add(wizard);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate connected Discord bots for slash registration");
        }

        return services;
    }

    /// <summary>Registers each snapshot command on one service (best-effort; failures logged, never thrown).</summary>
    private async Task RegisterSlashCommandsOnServiceAsync(
        DiscordService service,
        List<(string Name, string Description, IReadOnlyList<WorkflowParameter>? Parameters)> commands)
    {
        if (service == null || commands.Count == 0)
        {
            return;
        }

        foreach (var (name, description, parameters) in commands)
        {
            try
            {
                var error = await service.RegisterWorkflowSlashCommandAsync(name, description, parameters).ConfigureAwait(false);
                if (error != null)
                {
                    _logger.LogWarning("Workflow slash command '/{Name}' registration failed on bot {BotId}: {Error}",
                        name, service.BotId, error);
                }
                else
                {
                    _logger.LogInformation("Workflow slash command '/{Name}' registered on bot {BotId}",
                        name, service.BotId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow slash command '/{Name}' registration threw on bot {BotId}",
                    name, service.BotId);
            }
        }
    }

    /// <summary>Removes each vanished command from one service (best-effort; failures logged, never thrown).</summary>
    private async Task UnregisterSlashCommandsOnServiceAsync(DiscordService service, IReadOnlyCollection<string> names)
    {
        if (service == null || names.Count == 0)
        {
            return;
        }

        foreach (var name in names)
        {
            try
            {
                var error = await service.UnregisterWorkflowSlashCommandAsync(name).ConfigureAwait(false);
                if (error != null)
                {
                    _logger.LogWarning("Workflow slash command '/{Name}' unregistration failed on bot {BotId}: {Error}",
                        name, service.BotId, error);
                }
                else
                {
                    _logger.LogInformation("Workflow slash command '/{Name}' unregistered on bot {BotId}",
                        name, service.BotId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow slash command '/{Name}' unregistration threw on bot {BotId}",
                    name, service.BotId);
            }
        }
    }

    /// <summary>
    /// Normalizes a workflow slash command name for Discord — the SAME rules
    /// as DiscordService's registration (lowercase, ASCII letters/digits/'-'
    /// /'_', spaces → dashes, trimmed of leading/trailing dashes, 1-32 chars).
    /// Empty when nothing usable remains. Kept local so the UI layer never
    /// depends on the Integrations implementation for the contract.
    /// </summary>
    private static string NormalizeSlashCommandName(string? name)
    {
        var cleaned = (name ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
        var builder = new System.Text.StringBuilder(cleaned.Length);
        foreach (var character in cleaned)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim('-');
        if (result.Length > 32)
        {
            result = result[..32].Trim('-');
        }

        return result;
    }

    // ======================================================================
    // Cache loading / indexing
    // ======================================================================

    private async Task LoadWorkflowsAsync()
    {
        // Engine data layer: rows carry the trigger + node graph as JSON
        // (WorkflowJson.FromRow materializes them). Only ENABLED workflows are
        // cached; disabled ones can never match.
        List<string> previousSlashNames;
        lock (_sync)
        {
            previousSlashNames = _slashIndex.Keys.ToList();
        }

        var rows = await _dataService.GetWorkflowsAsync();

        var commandIndex = new Dictionary<string, List<Workflow>>(StringComparer.OrdinalIgnoreCase);
        var eventIndex = new Dictionary<string, List<Workflow>>(StringComparer.OrdinalIgnoreCase);
        var slashIndex = new Dictionary<string, List<Workflow>>(StringComparer.Ordinal);
        var workflowById = new Dictionary<string, Workflow>(StringComparer.Ordinal);
        var workflowByComponentKey = new Dictionary<string, Workflow>(StringComparer.Ordinal);
        var timers = new List<Workflow>();

        if (rows != null)
        {
            foreach (var row in rows)
            {
                if (row?.Enabled != true)
                {
                    continue;
                }

                Workflow workflow;
                try
                {
                    workflow = WorkflowJson.FromRow(row);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping workflow row {WorkflowId} ('{Name}'): could not parse its trigger/graph JSON",
                        row.Id, row.Name);
                    continue;
                }

                // Every enabled workflow is reachable BY ID (Discord component
                // interactions address "vyralwf:{workflowId}" custom ids).
                var workflowId = workflow.Id ?? string.Empty;
                if (workflowId.Length > 0)
                {
                    workflowById[workflowId] = workflow;
                }

                var trigger = workflow.Trigger;
                if (trigger == null)
                {
                    continue;
                }

                // Component CHILD workflows (button/menu sub-flows the editor
                // materializes as real rows) are reachable by their exact
                // component custom id ("vyralwf:{parentId}:{nodeId}:btn{i}" /
                // "...:menu" — ComponentKey equals it verbatim).
                var triggerTypeKey = (trigger.Type ?? string.Empty).Trim().ToLowerInvariant();
                if ((triggerTypeKey == WorkflowTriggerTypes.ButtonClick || triggerTypeKey == WorkflowTriggerTypes.SelectMenu)
                    && !string.IsNullOrWhiteSpace(workflow.ComponentKey))
                {
                    workflowByComponentKey[workflow.ComponentKey.Trim()] = workflow;
                }

                switch ((trigger.Type ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "command":
                        var name = (trigger.CommandName ?? string.Empty).Trim().TrimStart(_commandPrefixes);
                        if (name.Length > 0)
                        {
                            AddToIndex(commandIndex, name, workflow);
                        }
                        break;

                    case "slash_command":
                        var slashName = NormalizeSlashCommandName(trigger.CommandName);
                        if (slashName.Length > 0)
                        {
                            AddToIndex(slashIndex, slashName, workflow);
                        }
                        break;

                    // "button_click" / "select_menu" are matched from the
                    // component custom id — BY WORKFLOW ID (workflowById) or,
                    // for child sub-flows, by exact ComponentKey
                    // (workflowByComponentKey above).

                    case "event":
                        var eventType = NormalizeEventType(trigger.EventType);
                        if (eventType.Length > 0)
                        {
                            AddToIndex(eventIndex, eventType, workflow);
                        }
                        break;

                    case "timer":
                        if ((trigger.IntervalMinutes ?? 0) > 0)
                        {
                            timers.Add(workflow);
                        }
                        break;

                    // "manual" runs only via RunManual; unknown types are
                    // ignored for forward compatibility.
                }
            }
        }

        lock (_sync)
        {
            _commandIndex.Clear();
            foreach (var kvp in commandIndex) _commandIndex[kvp.Key] = kvp.Value;

            _eventIndex.Clear();
            foreach (var kvp in eventIndex) _eventIndex[kvp.Key] = kvp.Value;

            _slashIndex.Clear();
            foreach (var kvp in slashIndex) _slashIndex[kvp.Key] = kvp.Value;

            _workflowById.Clear();
            foreach (var kvp in workflowById) _workflowById[kvp.Key] = kvp.Value;

            _workflowByComponentKey.Clear();
            foreach (var kvp in workflowByComponentKey) _workflowByComponentKey[kvp.Key] = kvp.Value;

            _timerWorkflows.Clear();
            _timerWorkflows.AddRange(timers);
            // Timer last-run bookkeeping survives a cache reload (same profile).
            // Option cooldowns survive too (a reload must not reset a running
            // cooldown for the same workflow id).
        }

        // SLASH AUTO-REGISTRATION (best-effort, fire-and-forget): push every
        // enabled slash workflow's command name to every connected bot — runs
        // after StartAsync's initial load and after every editor ReloadAsync.
        // Commands that vanished from the cache (deleted, disabled or renamed
        // flows) are removed from Discord too.
        var removedSlashNames = previousSlashNames
            .Where(n => !slashIndex.ContainsKey(n))
            .ToList();
        _ = SyncWorkflowSlashCommandsAsync(removedSlashNames);
    }

    private static void AddToIndex(Dictionary<string, List<Workflow>> index, string key, Workflow workflow)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<Workflow>();
            index[key] = list;
        }
        list.Add(workflow);
    }

    // ======================================================================
    // Context building / execution
    // ======================================================================

    /// <summary>
    /// Builds the engine ExecutionContext (ExecutionId/Variables/Services are
    /// assigned by its constructor). Base variables seeded for every trigger
    /// kind: username, platform, channel, message; <paramref name="extra"/>
    /// adds trigger-specific ones (args/command for commands, eventType plus
    /// event fields for events/timers/manual). The trailing identity params
    /// (userRoles/userId/isBot — supplied by Discord/Twitch/Kick payloads when
    /// available) seed "userRoles", "userId", "isBot" and a derived
    /// "isModerator" variable so the permission option can be enforced
    /// post-context; null/false defaults keep every other call site unchanged.
    /// </summary>
    private ExecutionContext BuildContext(
        Workflow workflow,
        string platform,
        string channel,
        string username,
        string message,
        string? botId,
        object? origin,
        Dictionary<string, string>? extra,
        string? userRoles = null,
        string? userId = null,
        bool isBot = false)
    {
        var context = new ExecutionContext(_serviceProvider, _logger)
        {
            WorkflowId = workflow.Id ?? string.Empty,
            Platform = platform ?? string.Empty,
            Channel = channel ?? string.Empty,
            Username = username ?? string.Empty,
            Message = message ?? string.Empty,
            BotId = botId,
            GuildName = null,
            Origin = origin
        };

        // Variables is get-only (case-insensitive) — populate it after construction.
        context.Variables["username"] = username ?? string.Empty;
        context.Variables["platform"] = platform ?? string.Empty;
        context.Variables["channel"] = channel ?? string.Empty;
        context.Variables["message"] = message ?? string.Empty;

        // Identity seeding (permission option + node conditions). An empty
        // "userRoles" means "unseeded" — TryAllowPermission then fails open.
        context.Variables["userRoles"] = userRoles ?? string.Empty;
        context.Variables["userId"] = userId ?? string.Empty;
        context.Variables["isBot"] = isBot ? "true" : "false";
        var rolesLower = (userRoles ?? string.Empty).ToLowerInvariant();
        context.Variables["isModerator"] =
            rolesLower.Contains("moderator") || rolesLower.Contains("broadcaster") ? "true" : "false";

        if (extra != null)
        {
            foreach (var kvp in extra)
            {
                context.Variables[kvp.Key] = kvp.Value ?? string.Empty;
            }
        }

        return context;
    }

    private void ExecuteInBackground(Workflow workflow, ExecutionContext context, Func<WorkflowExecutionResult, Task>? onCompleted = null)
    {
        var runtime = ResolveRuntime();
        if (runtime == null)
        {
            _logger.LogWarning("Workflow '{Name}' ({WorkflowId}) skipped: WorkflowRuntime is not available",
                workflow.Name, workflow.Id);
            return;
        }

        _ = Task.Run(async () =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await runtime.ExecuteAsync(workflow, context);
                stopwatch.Stop();
                _metrics?.Increment("Workflows", "runs");
                _metrics?.RecordDuration("Workflows", stopwatch.Elapsed.TotalMilliseconds);
                if (result is { IsCompleted: false })
                {
                    _metrics?.Increment("Workflows", "failed");
                    _logger.LogWarning("Workflow '{Name}' ({WorkflowId}) execution {ExecutionId} {Status}: {Error}",
                        workflow.Name, workflow.Id, context.ExecutionId, result.Status, result.Error ?? "(no details)");
                }
                else
                {
                    _logger.LogDebug("Workflow '{Name}' ({WorkflowId}) execution {ExecutionId} completed in {DurationMs}ms",
                        workflow.Name, workflow.Id, context.ExecutionId, result.DurationMs);
                }

                if (onCompleted != null)
                {
                    await onCompleted(result).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metrics?.Increment("Workflows", "runs");
                _metrics?.Increment("Workflows", "failed");
                _metrics?.RecordDuration("Workflows", stopwatch.Elapsed.TotalMilliseconds);
                _logger.LogError(ex, "Workflow '{Name}' ({WorkflowId}) execution {ExecutionId} failed",
                    workflow.Name, workflow.Id, context.ExecutionId);
            }
        });
    }

    /// <summary>
    /// Closes a deferred Discord interaction after its workflow run with an
    /// ephemeral outcome followup — the interaction was acknowledged with the
    /// "thinking" state, which would otherwise hang for up to 15 minutes.
    /// Best-effort; never throws.
    /// </summary>
    /// <param name="reply">Origin interaction reply path (null = nothing to close).</param>
    /// <param name="label">Human-facing label ("/name" for slash, workflow name for components).</param>
    /// <param name="result">Engine run outcome.</param>
    private async Task SendInteractionCompletionFollowupAsync(Func<string, Task>? reply, string label, WorkflowExecutionResult? result)
    {
        if (reply == null)
        {
            return;
        }

        try
        {
            var notice = result is { IsCompleted: false }
                ? $"⚠️ {label} finished with status '{result.Status}'."
                : $"✅ {label} executed.";
            await reply(notice).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to deliver the workflow completion followup for '{Label}'", label);
        }
    }

    /// <summary>
    /// Resolves the engine runtime once (lazily, defensively): a missing or
    /// unconstructable registration disables workflow execution with a logged
    /// error instead of crashing anything.
    /// </summary>
    private WorkflowRuntime? ResolveRuntime()
    {
        if (_runtimeResolved)
        {
            return _runtime;
        }

        try
        {
            _runtime = _serviceProvider.GetService<WorkflowRuntime>();
            if (_runtime != null)
            {
                // Route the engine's sends per platform/bot (ChatViewModel routing).
                _messageSender.Install(_runtime);
            }
            else
            {
                _logger.LogWarning("WorkflowRuntime is not registered — workflow triggers cannot execute");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve WorkflowRuntime — workflow triggers cannot execute");
            _runtime = null;
        }
        finally
        {
            _runtimeResolved = true;
        }

        return _runtime;
    }

    // ======================================================================
    // Matching helpers
    // ======================================================================

    /// <summary>Workflow Platform filter: null/empty/"Any"/"All" matches everything, else exact (case-insensitive).</summary>
    private static bool PlatformMatches(string? triggerPlatform, string platform)
    {
        if (string.IsNullOrWhiteSpace(triggerPlatform))
        {
            return true;
        }

        var p = triggerPlatform.Trim();
        return string.Equals(p, "Any", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, platform, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Optional per-trigger prefix restriction: when Trigger.CommandPrefix is
    /// set (e.g. "!"), only that prefix matches; unset accepts all three
    /// conventional prefixes.
    /// </summary>
    private static bool PrefixMatches(string? commandPrefix, char prefix)
    {
        if (string.IsNullOrWhiteSpace(commandPrefix))
        {
            return true;
        }

        return commandPrefix.Trim()[0] == prefix;
    }

    /// <summary>
    /// Canonical event-type keys (lowercase, separator-free) with aliases so
    /// triggers authored as "Sub", "bits", "gifted", "hosted" etc. still match.
    /// </summary>
    private static string NormalizeEventType(string? eventType)
    {
        var t = (eventType ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0)
        {
            return t;
        }

        return t switch
        {
            "sub" or "subscription" => "subscribe",
            "resub" => "resub",
            "gift" or "giftsub" or "subgift" or "gifted" => "giftsub",
            "bits" or "cheer" => "cheer",
            "host" or "hosted" or "hosting" => "host",
            "raid" or "raided" => "raid",
            "follow" or "followed" => "follow",
            _ => t.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty)
        };
    }

    /// <summary>Live channel name for a platform (best-effort, for context variables).</summary>
    private string ChannelFor(string platform)
    {
        if (string.Equals(platform, "Twitch", StringComparison.OrdinalIgnoreCase))
        {
            return _twitchService.Channel ?? string.Empty;
        }
        if (string.Equals(platform, "Kick", StringComparison.OrdinalIgnoreCase))
        {
            return _kickService.Channel ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// Platform for timer/manual executions: the trigger's own platform when
    /// it names one, else the first connected chat platform ("Twitch" default
    /// when nothing is connected — documented V1 fallback).
    /// </summary>
    private string ResolveTriggerPlatform(WorkflowTrigger trigger)
    {
        if (!string.IsNullOrWhiteSpace(trigger.Platform)
            && !string.Equals(trigger.Platform.Trim(), "Any", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trigger.Platform.Trim(), "All", StringComparison.OrdinalIgnoreCase))
        {
            return trigger.Platform.Trim();
        }

        if (_twitchService.IsConnected)
        {
            return "Twitch";
        }
        if (_kickService.IsConnected)
        {
            return "Kick";
        }
        return "Twitch";
    }

    /// <summary>Builds an ignore-case variable dictionary from key/value pairs (values ToString'd).</summary>
    private static Dictionary<string, string> Vars(params (string Key, object? Value)[] fields)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
        {
            dict[key] = value?.ToString() ?? string.Empty;
        }
        return dict;
    }
}
