using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Discord;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Services;

/// <summary>
/// Discord EXECUTION + INTERACTION plumbing for the workflow engine: implements
/// <see cref="VyralBot.Workflows.Abstractions.IDiscordWorkflowBridge"/> on top
/// of the app's Discord services.
///
/// LAYERING: <see cref="DiscordService"/> owns the Discord.Net gateway and
/// raises NATIVE events (<see cref="IDiscordService.SlashCommandInvoked"/> /
/// <see cref="IDiscordService.ComponentInteractionInvoked"/>, with native
/// <c>DiscordSlashInvocation</c>/<c>DiscordComponentInteraction</c> payloads);
/// this bridge translates them into the ENGINE's records
/// (<see cref="WorkflowSlashInvocation"/> / <see cref="WorkflowComponentInteraction"/>)
/// and re-raises them for the trigger plumbing. The engine never sees
/// Discord.Net types; the Integrations layer references Workflows only for the
/// typed slash-option model (<c>WorkflowParameter</c>).
///
/// ROUTING: every method resolves the bot via <see cref="IDiscordBotManager"/> —
/// an explicit botId first (managed bots carry their DiscordBots row id), then
/// the first connected (primary) bot, then the setup wizard's singleton
/// service, mirroring <see cref="WorkflowMessageSender"/>'s resolution order.
/// A missing service or any failure logs and returns false / an empty list —
/// never a throw, so a failed node result never breaks a workflow run.
///
/// EVENT ATTACHMENT: bot services are created/destroyed dynamically by the
/// manager, so attachment is defensive — (1) the DI host may call
/// <see cref="Attach"/>/<see cref="Detach"/> for a specific service, and
/// (2) when the manager is the concrete <see cref="DiscordBotManager"/>, the
/// bridge subscribes to its <c>ServiceStarted</c>/<c>ServiceStopped</c>
/// lifecycle events and attaches/detaches automatically. The setup wizard's
/// singleton bot (outside the manager's running set until adopted) is attached
/// eagerly in the constructor.
/// </summary>
public class WorkflowDiscordBridge : IDiscordWorkflowBridge
{
    private readonly ILogger<WorkflowDiscordBridge> _logger;
    private readonly IDiscordBotManager _manager;
    private readonly IDiscordService? _wizardService;
    private readonly object _sync = new();
    private readonly HashSet<DiscordService> _attached = new();

    /// <summary>Raised when a workflow-registered slash command is invoked on Discord.</summary>
    public event EventHandler<WorkflowSlashInvocation>? SlashInvocationReceived;

    /// <summary>Raised when a "vyralwf:"-prefixed component is clicked/selected on Discord.</summary>
    public event EventHandler<WorkflowComponentInteraction>? ComponentInteractionReceived;

    public WorkflowDiscordBridge(
        IDiscordBotManager manager,
        ILogger<WorkflowDiscordBridge> logger,
        IDiscordService? wizardService = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _wizardService = wizardService;

        // Auto-attach via the manager's lifecycle events when available
        // (the events live on the concrete manager, not the interface).
        if (_manager is DiscordBotManager concreteManager)
        {
            concreteManager.ServiceStarted += OnManagerServiceStarted;
            concreteManager.ServiceStopped += OnManagerServiceStopped;
        }

        // The setup wizard's singleton bot is outside the manager's running
        // set until adopted, so attach it eagerly.
        if (_wizardService is DiscordService wizardConcrete)
        {
            Attach(wizardConcrete);
        }
    }

    /// <summary>
    /// Hooks a service's workflow interaction events (idempotent). Called by
    /// the DI host and/or automatically via the manager's ServiceStarted event.
    /// </summary>
    public void Attach(DiscordService service)
    {
        if (service == null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_attached.Add(service))
            {
                return;
            }
        }

        service.SlashCommandInvoked += OnServiceSlashCommandInvoked;
        service.ComponentInteractionInvoked += OnServiceComponentInteractionInvoked;
        _logger.LogDebug("WorkflowDiscordBridge attached to Discord bot {BotId}", service.BotId);
    }

    /// <summary>Unhooks a service's workflow interaction events (idempotent, safe on unknown services).</summary>
    public void Detach(DiscordService service)
    {
        if (service == null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_attached.Remove(service))
            {
                return;
            }
        }

        service.SlashCommandInvoked -= OnServiceSlashCommandInvoked;
        service.ComponentInteractionInvoked -= OnServiceComponentInteractionInvoked;
        _logger.LogDebug("WorkflowDiscordBridge detached from Discord bot {BotId}", service.BotId);
    }

    private void OnManagerServiceStarted(object? sender, DiscordService service)
    {
        try
        {
            Attach(service);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to attach the workflow bridge to Discord bot {BotId}", service?.BotId);
        }
    }

    private void OnManagerServiceStopped(object? sender, DiscordService service)
    {
        try
        {
            Detach(service);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detach the workflow bridge from Discord bot {BotId}", service?.BotId);
        }
    }

    private void OnServiceSlashCommandInvoked(object? sender, DiscordSlashInvocation e)
    {
        try
        {
            var handlers = SlashInvocationReceived;
            if (handlers == null)
            {
                return;
            }

            var invocation = new WorkflowSlashInvocation
            {
                BotId = e?.BotId ?? string.Empty,
                CommandName = e?.CommandName ?? string.Empty,
                Arguments = e?.Arguments ?? string.Empty,
                OptionValues = e?.OptionValues ?? new Dictionary<string, string>(),
                Username = e?.Username ?? string.Empty,
                ChannelName = e?.ChannelName ?? string.Empty,
                GuildName = e?.GuildName,
                Reply = e?.Respond
            };

            handlers.Invoke(this, invocation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow slash invocation dispatch failed for '/{Command}'", e?.CommandName);
        }
    }

    private void OnServiceComponentInteractionInvoked(object? sender, DiscordComponentInteraction e)
    {
        try
        {
            var handlers = ComponentInteractionReceived;
            if (handlers == null)
            {
                return;
            }

            var interaction = new WorkflowComponentInteraction
            {
                BotId = e?.BotId ?? string.Empty,
                CustomId = e?.CustomId ?? string.Empty,
                Username = e?.Username ?? string.Empty,
                ChannelName = e?.ChannelName ?? string.Empty,
                GuildName = e?.GuildName,
                Values = e?.Values ?? Array.Empty<string>(),
                Reply = e?.Respond
            };

            handlers.Invoke(this, interaction);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow component interaction dispatch failed for '{CustomId}'", e?.CustomId);
        }
    }

    // ==================================================================
    // IDiscordWorkflowBridge — every method resolves the service, then
    // delegates; failures are logged and returned, never thrown.
    // ==================================================================

    /// <inheritdoc />
    public async Task<bool> SendToChannelAsync(string? botId, string? guild, string? channelOverride, string content, string? embedJson)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord send skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            // Empty override = origin channel (the service resolves it)
            return await service.SendToChannelAsync(channelOverride ?? string.Empty, content, embedJson, guild).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord send failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> SendToChannelWithComponentsAsync(
        string? botId,
        string? guild,
        string? channelOverride,
        string content,
        string? embedJson,
        string buttonsJson,
        string menuJson)
    {
        // The engine-facing contract cannot carry the "vyralwf:{workflowId}:{nodeId}"
        // custom-id stem that interactive components require — callers that want
        // clickable components must use the componentPrefix overload below.
        return SendToChannelWithComponentsAsync(
            botId, guild, channelOverride, content, embedJson, buttonsJson, menuJson, componentPrefix: null);
    }

        /// <inheritdoc />
    public async Task<bool> UserHasPermissionAsync(string? botId, string username, string permission)
    {
        var service = ResolveService(botId);
        if (service is null)
        {
            return false;
        }

        return await service.UserHasPermissionAsync(username, permission).ConfigureAwait(false);
    }

/// <summary>
    /// Component-aware send (Buttons / Menu node config JSON). Interactive
    /// parts REQUIRE <paramref name="componentPrefix"/> — the
    /// "vyralwf:{workflowId}:{nodeId}" stem that becomes the components'
    /// custom ids ("...:btn{i}" / "...:menu") so clicks dispatch back into the
    /// workflow; when it is missing the service drops the components (logged)
    /// and still delivers the text/embed. Never throws.
    /// </summary>
    public async Task<bool> SendToChannelWithComponentsAsync(
        string? botId,
        string? guild,
        string? channelOverride,
        string content,
        string? embedJson,
        string buttonsJson,
        string menuJson,
        string? componentPrefix)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord component send skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            // Empty override = origin channel (the service resolves it)
            return await service.SendToChannelWithComponentsAsync(
                channelOverride ?? string.Empty,
                content,
                embedJson,
                buttonsJson,
                menuJson,
                componentPrefix,
                guild).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord component send failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendDmAsync(string? botId, string username, string content, string? embedJson)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord DM skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.SendDmAsync(username, content, embedJson).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord DM to '{Username}' failed (bot {BotId})", username, botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AddRoleAsync(string? botId, string username, string roleIdOrName, string? reason)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord role grant skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.AddRoleToUserAsync(username, roleIdOrName, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord role grant failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRoleAsync(string? botId, string username, string roleIdOrName, string? reason)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord role removal skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.RemoveRoleFromUserAsync(username, roleIdOrName, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord role removal failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CreateChannelAsync(string? botId, string name, string channelType, string? category)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord channel creation skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.CreateWorkflowChannelAsync(name, channelType, category).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord channel creation failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMessageAsync(string? botId, string scope)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord message delete skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.DeleteMessageByScopeAsync(scope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord message delete failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> EditLastBotMessageAsync(string? botId, string newContent, string? embedJson)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord message edit skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.EditLastBotMessageAsync(newContent, embedJson).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord message edit failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReactAsync(string? botId, string emoji, string scope)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord reaction skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.ReactToMessageAsync(emoji, scope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord reaction failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CreateThreadAsync(string? botId, string threadName, int autoArchiveMinutes)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord thread creation skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.CreateThreadFromLastAsync(threadName, autoArchiveMinutes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord thread creation failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetChannelLockAsync(string? botId, bool locked, string? reason)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord channel lock skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.SetChannelLockAsync(locked, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord channel lock failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(string? botId)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord role picker skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return Array.Empty<string>();
            }

            return await service.GetWorkflowRoleNamesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord role listing failed (bot {BotId})", botId ?? "(primary)");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetChannelNamesAsync(string? botId)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord channel picker skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return Array.Empty<string>();
            }

            return await service.GetWorkflowChannelNamesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord channel listing failed (bot {BotId})", botId ?? "(primary)");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UserHasRoleAsync(string? botId, string username, string roleIdOrName)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord role check skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.UserHasRoleAsync(username, roleIdOrName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord role check failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    // ==================================================================
    // Moderation — delegates to the service's name-based methods (the
    // service owns the username → snowflake resolution and the actual
    // moderation calls); failures are logged there and returned.
    // ==================================================================

    /// <inheritdoc />
    public async Task<bool> KickUserAsync(string? botId, string username, string? reason)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord kick skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.KickMemberByNameAsync(username, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord kick of '{Username}' failed (bot {BotId})", username, botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> BanUserAsync(string? botId, string username, string? reason)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord ban skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.BanMemberByNameAsync(username, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord ban of '{Username}' failed (bot {BotId})", username, botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnbanUserAsync(string? botId, string username)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord unban skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.UnbanMemberByNameAsync(username).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord unban of '{Username}' failed (bot {BotId})", username, botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TimeoutUserAsync(string? botId, string username, int minutes, string? reason)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord timeout skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.TimeoutMemberByNameAsync(username, minutes, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord timeout of '{Username}' failed (bot {BotId})", username, botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveTimeoutUserAsync(string? botId, string username)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord timeout removal skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.RemoveTimeoutMemberByNameAsync(username).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord timeout removal for '{Username}' failed (bot {BotId})", username, botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> PurgeChannelAsync(string? botId, int count)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord purge skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.PurgeOriginChannelAsync(count).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord purge failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetSlowmodeAsync(string? botId, int seconds)
    {
        try
        {
            var service = ResolveService(botId);
            if (service == null)
            {
                _logger.LogWarning("Workflow Discord slowmode skipped: no connected bot for BotId {BotId}", botId ?? "(primary)");
                return false;
            }

            return await service.SetSlowmodeOnOriginChannelAsync(seconds).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow Discord slowmode failed (bot {BotId})", botId ?? "(primary)");
            return false;
        }
    }

    // ==================================================================
    // Resolution
    // ==================================================================

    /// <summary>
    /// Bot for a workflow call: an explicit bot id first (managed bots carry
    /// their DiscordBots row id), then the first connected manager bot, then
    /// the setup wizard's singleton service — the same order as
    /// <see cref="WorkflowMessageSender"/>. Null when nothing is connected.
    /// </summary>
    private IDiscordService? ResolveService(string? botId)
    {
        if (!string.IsNullOrWhiteSpace(botId) && !string.Equals(botId, "primary", StringComparison.Ordinal))
        {
            var byId = _manager.Get(botId);
            if (byId != null && byId.IsConnected)
            {
                return byId;
            }
        }

        if (string.Equals(botId, "primary", StringComparison.Ordinal) && _wizardService is { IsConnected: true })
        {
            return _wizardService;
        }

        if (_manager.Primary is { IsConnected: true } primary)
        {
            return primary;
        }

        return _wizardService is { IsConnected: true } ? _wizardService : null;
    }
}
