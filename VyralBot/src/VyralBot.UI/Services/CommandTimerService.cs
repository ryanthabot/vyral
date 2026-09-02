using Microsoft.Extensions.Logging;
using System.Threading;
using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;

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
/// resolves the command by name, runs its Response through the variable
/// replacement service with a minimal context, sends it to the target
/// platform and persists the new LastRunAt. All failures are logged and never
/// crash the tick.
/// </summary>
public class CommandTimerService
{
    private readonly ILogger<CommandTimerService> _logger;
    private readonly IDataService _dataService;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IDiscordBotManager _discordBotManager;
    private readonly IVariableReplacementService _variableReplacementService;

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
        IVariableReplacementService variableReplacementService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _variableReplacementService = variableReplacementService ?? throw new ArgumentNullException(nameof(variableReplacementService));
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

            // Mark the slot as consumed so the warning logs once per interval
            // instead of every 30s tick.
            await MarkRanAsync(timer, now);
            return;
        }

        // Minimal variable context: the bot itself is the "user". Channel
        // comes from the target service when it exposes one (Twitch/Kick).
        var channel = timer.TargetPlatform switch
        {
            "Twitch" => _twitchService.Channel,
            "Kick" => _kickService.Channel,
            _ => null
        };

        var contextVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = "Vyral",
            ["platform"] = timer.TargetPlatform,
            ["channel"] = channel ?? string.Empty
        };

        var response = await _variableReplacementService.ReplaceVariablesAsync(
            command.Response, contextVariables, timer.TargetPlatform);

        var sent = await SendAsync(timer, response);

        await MarkRanAsync(timer, now);

        if (sent)
        {
            _logger.LogInformation("Command timer {TimerName} fired {CommandName} on {Platform}",
                timer.Name, timer.CommandName, timer.TargetPlatform);
        }
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
