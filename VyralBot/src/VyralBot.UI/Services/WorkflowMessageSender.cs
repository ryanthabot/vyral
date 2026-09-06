using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.UI.Services;

/// <summary>
/// Outgoing message routing for workflow executions: installs itself as the
/// <see cref="WorkflowRuntime"/>'s <c>MessageSender</c> so every "send message"
/// style node speaks through the same platform services the rest of the app
/// uses, mirroring ChatViewModel's reply routing:
///
///  • Twitch  → <see cref="ITwitchService.SendMessageAsync"/> (single connected channel)
///  • Kick    → <see cref="IKickService.SendMessageAsync"/> (single connected channel)
///  • Discord → the bot flagged with the Giveaway role when connected, else
///              the bot that originated the trigger (<c>ctx.BotId</c>) via
///              <see cref="IDiscordBotManager.Get"/>, falling back to
///              <see cref="IDiscordBotManager.Primary"/> and then the setup
///              wizard's singleton <see cref="IDiscordService"/> — the same
///              resolution order as ChatViewModel.ResolveDiscordSendTarget.
///
/// Returns true when the message was actually handed to a connected service;
/// false (logged, never thrown) otherwise, so a failed send never breaks a
/// workflow run.
///
/// Limitation (V1, matches ChatViewModel's command-response path): YouTube and
/// TikTok have no send target here — sends targeting those platforms are
/// logged and skipped.
/// </summary>
public class WorkflowMessageSender
{
    private readonly ILogger<WorkflowMessageSender> _logger;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IDiscordBotManager _discordBotManager;
    private readonly IDiscordService _discordService;
    private readonly VyralBot.Core.Services.Performance.IMetricsRegistry? _metrics;

    public WorkflowMessageSender(
        ILogger<WorkflowMessageSender> logger,
        ITwitchService twitchService,
        IKickService kickService,
        IDiscordBotManager discordBotManager,
        IDiscordService discordService,
        VyralBot.Core.Services.Performance.IMetricsRegistry? metrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _metrics = metrics;
    }

    /// <summary>
    /// Wires this sender into the workflow runtime's MessageSender hook.
    /// Idempotent — the last install wins.
    /// </summary>
    public void Install(WorkflowRuntime runtime)
    {
        if (runtime == null) throw new ArgumentNullException(nameof(runtime));
        runtime.MessageSender = SendAsync;
        _logger.LogDebug("WorkflowMessageSender installed into WorkflowRuntime");
    }

    /// <summary>
    /// Send implementation matching the runtime contract
    /// (<c>Func&lt;ExecutionContext, string, Task&lt;bool&gt;&gt;</c>).
    /// </summary>
    public async Task<bool> SendAsync(ExecutionContext context, string message)
    {
        if (context == null || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var platform = context.Platform?.Trim();
        var normalizedPlatform = string.IsNullOrWhiteSpace(platform) ? "(none)" : platform.ToLowerInvariant();

        try
        {
            switch (platform?.ToLowerInvariant())
            {
                case "twitch":
                    if (!_twitchService.IsConnected)
                    {
                        _metrics?.Increment("Sends:" + normalizedPlatform, "failed");
                        _logger.LogWarning("Workflow send to Twitch skipped: not connected (workflow {WorkflowId})", context.WorkflowId);
                        return false;
                    }

                    await _twitchService.SendMessageAsync(message);
                    _metrics?.Increment("Sends:" + normalizedPlatform, "sent");
                    return true;

                case "kick":
                    if (!_kickService.IsConnected)
                    {
                        _metrics?.Increment("Sends:" + normalizedPlatform, "failed");
                        _logger.LogWarning("Workflow send to Kick skipped: not connected (workflow {WorkflowId})", context.WorkflowId);
                        return false;
                    }

                    await _kickService.SendMessageAsync(message);
                    _metrics?.Increment("Sends:" + normalizedPlatform, "sent");
                    return true;

                case "discord":
                {
                    // Prefer the bot flagged with the Giveaway role so giveaway
                    // announcements originate from the dedicated giveaway bot;
                    // fall back to the origin-aware resolution below when no bot
                    // carries the role or that bot is not connected.
                    var giveawayBot = await ResolveGiveawayRoleTargetAsync();
                    var target = giveawayBot ?? ResolveDiscordTarget(context.BotId);
                    if (target == null || !target.IsConnected)
                    {
                        _metrics?.Increment("Sends:" + normalizedPlatform, "failed");
                        _logger.LogWarning(
                            "Workflow send to Discord skipped: no connected bot for BotId {BotId} (workflow {WorkflowId})",
                            context.BotId ?? "(primary)", context.WorkflowId);
                        return false;
                    }

                    // Origin-aware send: explicit ctx.Channel → tracked interaction
                    // origin → the bot's selected dashboard channel. The plain
                    // SendMessageAsync ONLY knows the selected channel, so replies
                    // to slash commands in any other channel would be dropped
                    // whenever no dashboard channel is configured.
                    var delivered = await target.SendToChannelAsync(
                        context.Channel ?? string.Empty, message, null, context.GuildName);
                    _metrics?.Increment("Sends:" + normalizedPlatform, delivered ? "sent" : "failed");
                    return delivered;
                }

                default:
                    _metrics?.Increment("Sends:" + normalizedPlatform, "failed");
                    _logger.LogWarning(
                        "Workflow send skipped: no sender for platform {Platform} (workflow {WorkflowId})",
                        platform ?? "(none)", context.WorkflowId);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _metrics?.Increment("Sends:" + normalizedPlatform, "failed");
            _logger.LogError(ex, "Workflow send to {Platform} failed (workflow {WorkflowId}, execution {ExecutionId})",
                platform, context.WorkflowId, context.ExecutionId);
            return false;
        }
    }

    /// <summary>
    /// The live service of the bot flagged with the "Giveaway" role
    /// (<see cref="DiscordBotRoles.Giveaway"/>), or null when no bot carries the
    /// role or that bot is not running — callers then fall back to
    /// <see cref="ResolveDiscordTarget"/>.
    /// </summary>
    private async Task<IDiscordService?> ResolveGiveawayRoleTargetAsync()
    {
        var botId = await _discordBotManager.ResolveBotIdForRoleAsync(DiscordBotRoles.Giveaway);
        if (string.IsNullOrWhiteSpace(botId))
        {
            return null;
        }

        var byRole = _discordBotManager.Get(botId);
        return byRole != null && byRole.IsConnected ? byRole : null;
    }

    /// <summary>
    /// Discord send target for a workflow execution: the bot whose id the
    /// trigger context carries (managed bots carry their DiscordBots row id),
    /// else the first connected manager bot, else the setup wizard's singleton
    /// bot. Mirrors ChatViewModel.ResolveDiscordSendTarget.
    /// </summary>
    private IDiscordService? ResolveDiscordTarget(string? botId)
    {
        if (!string.IsNullOrWhiteSpace(botId) && !string.Equals(botId, "primary", StringComparison.Ordinal))
        {
            var byId = _discordBotManager.Get(botId);
            if (byId != null && byId.IsConnected)
            {
                return byId;
            }
        }

        if (string.Equals(botId, "primary", StringComparison.Ordinal) && _discordService.IsConnected)
        {
            return _discordService;
        }

        return _discordBotManager.Primary ?? (_discordService.IsConnected ? _discordService : null);
    }
}
