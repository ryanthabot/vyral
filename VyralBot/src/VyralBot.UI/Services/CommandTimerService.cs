using Microsoft.Extensions.Logging;
using System.Threading;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;

namespace VyralBot.UI.Services;

/// <summary>
/// Fires configured commands on a schedule (Commands page → Timers tab).
/// Registered as a UI singleton because it needs every platform sender.
///
/// Lifecycle: MainViewModel starts it after a profile loads and stops it when
/// platforms disconnect for a profile switch — timers ONLY fire while the
/// service is running, so no profile means no scheduled sends.
///
/// Each tick (every 30s, reentrancy-guarded) loads the CommandTimers rows and,
/// for every enabled timer whose interval has elapsed since LastRunAt,
/// resolves the command by name, runs its reply (the single Response or a
/// random ResponsesJson entry) through the variable replacement service with
/// a minimal context — or, for action-sequence commands, executes the steps
/// via CommandActionRunner — and sends it to the target platform. LastRunAt
/// is stamped ONLY when something was actually sent. All failures are logged
/// and never crash the tick.
/// </summary>
public class CommandTimerService
{
    private readonly ILogger<CommandTimerService> _logger;
    private readonly IDataService _dataService;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IDiscordBotManager _discordBotManager;
    // Same singleton the Platforms page connects, so timer messages land in the
    // connected YouTube channel's live chat.
    private readonly IYouTubeService _youTubeService;
    private readonly IVariableReplacementService _variableReplacementService;
    // Runs action-sequence commands (Command.ActionsJson) — the same executor
    // the chat command path uses. Null when DI skipped it: those commands are
    // then skipped with a warning instead of crashing the tick.
    private readonly CommandActionRunner? _commandActionRunner;

    private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(30);
    private Timer? _tickTimer;
    private int _tickRunning; // 1 while a tick is executing (reentrancy guard)

    /// <summary>True while the scheduler is started (a profile is active).</summary>
    public bool IsRunning => _tickTimer != null;

    public CommandTimerService(
        ILogger<CommandTimerService> logger,
        IDataService dataService,
        ITwitchService twitchService,
        IKickService kickService,
        IDiscordBotManager discordBotManager,
        IYouTubeService youTubeService,
        IVariableReplacementService variableReplacementService,
        CommandActionRunner? commandActionRunner = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _youTubeService = youTubeService ?? throw new ArgumentNullException(nameof(youTubeService));
        _variableReplacementService = variableReplacementService ?? throw new ArgumentNullException(nameof(variableReplacementService));
        _commandActionRunner = commandActionRunner;
    }

    /// <summary>Starts the scheduler. Idempotent — a second call is a no-op.</summary>
    public Task StartAsync()
    {
        if (_tickTimer != null)
        {
            return Task.CompletedTask;
        }

        _tickTimer = new Timer(_ => _ = TickAsync(), null, _tickInterval, _tickInterval);
        _logger.LogInformation("CommandTimerService started (checking timers every {Seconds}s)", _tickInterval.TotalSeconds);
        return Task.CompletedTask;
    }

    /// <summary>Stops the scheduler and disposes the tick timer. Safe to call twice.</summary>
    public Task StopAsync()
    {
        var timer = _tickTimer;
        _tickTimer = null;
        timer?.Dispose();

        _logger.LogInformation("CommandTimerService stopped");
        return Task.CompletedTask;
    }

    private async Task TickAsync()
    {
        // Paused: no profile loaded (service stopped between profiles) or a
        // previous tick still running.
        if (_tickTimer == null || Interlocked.CompareExchange(ref _tickRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // Master toggle (Settings → Timers): disabled means no timer fires,
            // but the scheduler keeps running so a re-enable is instant.
            var enabled = await _dataService.GetConfigValueAsync("Timers.CommandTimersEnabled");
            if (bool.TryParse(enabled, out var isEnabled) && !isEnabled)
            {
                return;
            }

            var timers = await _dataService.GetCommandTimersAsync();
            var now = DateTime.UtcNow;

            foreach (var timer in timers.Where(t => t.IsEnabled))
            {
                // Never-run timers fire on the first tick after start.
                var lastRun = timer.LastRunAt ?? DateTime.MinValue;
                if (now - lastRun < TimeSpan.FromMinutes(timer.IntervalMinutes))
                {
                    continue;
                }

                try
                {
                    await FireTimerAsync(timer, now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Command timer {TimerName} failed to fire {CommandName}",
                        timer.Name, timer.CommandName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommandTimerService tick failed");
        }
        finally
        {
            Volatile.Write(ref _tickRunning, 0);
        }
    }

    private async Task FireTimerAsync(CommandTimer timer, DateTime now)
    {
        var command = await _dataService.GetCommandByNameAsync(timer.CommandName);

        if (command == null || !command.IsEnabled)
        {
            _logger.LogWarning(
                "Command timer {TimerName} skipped: command {CommandName} is missing or disabled",
                timer.Name, timer.CommandName);

            // Nothing was sent — LastRunAt stays untouched so the slot keeps
            // its place instead of being treated as consumed.
            return;
        }

        // Minimal variable context: the bot itself is the "user". Channel
        // comes from the target service when it exposes one (Twitch/Kick).
        var channel = timer.TargetPlatform switch
        {
            "Twitch" => _twitchService.Channel,
            "Kick" => _kickService.Channel,
            "YouTube" => _youTubeService.Channel,
            _ => null
        };

        var contextVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = "Vyral",
            ["platform"] = timer.TargetPlatform,
            ["channel"] = channel ?? string.Empty
        };

        bool sent;
        if (!string.IsNullOrWhiteSpace(command.ActionsJson))
        {
            // Action-sequence command: run the steps via the same runner the
            // chat command path uses — its "send" steps do the messaging, so
            // the plain Response is NOT sent separately (mirrors ChatViewModel).
            sent = await RunActionsAsync(timer, command, channel);
        }
        else
        {
            // Random replies win when configured (same pick as
            // CommandService.PickResponse), falling back to the single Response.
            var responses = CommandService.ParseResponses(command.ResponsesJson);
            var response = responses.Count > 0
                ? responses[Random.Shared.Next(responses.Count)]
                : command.Response;

            response = await _variableReplacementService.ReplaceVariablesAsync(
                response, contextVariables, timer.TargetPlatform);

            sent = await SendAsync(timer, response);
        }

        // Only stamp the slot when a send actually happened — a skipped send
        // (command missing/disabled, platform offline) retries on the next
        // 30s tick instead of waiting another full interval.
        if (!sent)
        {
            return;
        }

        await MarkRanAsync(timer, now);

        _logger.LogInformation("Command timer {TimerName} fired {CommandName} on {Platform}",
            timer.Name, timer.CommandName, timer.TargetPlatform);
    }

    /// <summary>
    /// Runs a command's action sequence via <see cref="CommandActionRunner"/>
    /// (same executor as ChatViewModel.RunCommandActionsAsync). Returns true
    /// when at least one step ran.
    /// </summary>
    private async Task<bool> RunActionsAsync(CommandTimer timer, Command command, string? channel)
    {
        if (_commandActionRunner == null)
        {
            _logger.LogWarning("Command timer {TimerName} skipped: action-sequence runner unavailable", timer.Name);
            return false;
        }

        var context = new CommandContext
        {
            Username = "Vyral",
            Platform = timer.TargetPlatform,
            Channel = channel ?? string.Empty
        };

        var summary = await _commandActionRunner.RunAsync(command, context);
        if (summary == null)
        {
            _logger.LogWarning("Command timer {TimerName}: command {CommandName} has no runnable action steps",
                timer.Name, timer.CommandName);
            return false;
        }

        _logger.LogInformation("Command timer {TimerName} ran action sequence for {CommandName}: {Summary}",
            timer.Name, timer.CommandName, summary);
        return true;
    }

    private async Task<bool> SendAsync(CommandTimer timer, string message)
    {
        switch (timer.TargetPlatform)
        {
            case "Twitch":
                if (!_twitchService.IsConnected)
                {
                    _logger.LogWarning("Command timer {TimerName} skipped: Twitch is not connected", timer.Name);
                    return false;
                }

                await _twitchService.SendMessageAsync(message);
                return true;

            case "Kick":
                if (!_kickService.IsConnected)
                {
                    _logger.LogWarning("Command timer {TimerName} skipped: Kick is not connected", timer.Name);
                    return false;
                }

                await _kickService.SendMessageAsync(message);
                return true;

            case "YouTube":
                if (!_youTubeService.IsConnected)
                {
                    _logger.LogWarning("Command timer {TimerName} skipped: YouTube is not connected", timer.Name);
                    return false;
                }

                // Throws (caught by the tick's per-timer handler) when the
                // channel isn't live — LastRunAt stays unstamped so the timer
                // retries on the next tick once the broadcast starts.
                await _youTubeService.SendMessageAsync(message);
                return true;

            case "Discord":
                // Pinned bot first, falling back to the manager's Primary bot
                // (same routing as manual chat sends).
                var target = !string.IsNullOrEmpty(timer.BotId)
                    ? _discordBotManager.Get(timer.BotId) ?? _discordBotManager.Primary
                    : _discordBotManager.Primary;

                if (target == null || !target.IsConnected)
                {
                    _logger.LogWarning("Command timer {TimerName} skipped: no connected Discord bot (BotId {BotId})",
                        timer.Name, timer.BotId ?? "(primary)");
                    return false;
                }

                await target.SendMessageAsync(message);
                return true;

            default:
                _logger.LogWarning("Command timer {TimerName} skipped: unknown platform {Platform}",
                    timer.Name, timer.TargetPlatform);
                return false;
        }
    }

    private async Task MarkRanAsync(CommandTimer timer, DateTime now)
    {
        timer.LastRunAt = now;
        timer.UpdatedAt = now;
        await _dataService.SaveCommandTimerAsync(timer);
    }
}
