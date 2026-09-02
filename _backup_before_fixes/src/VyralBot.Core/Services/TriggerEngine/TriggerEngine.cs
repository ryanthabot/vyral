using Microsoft.Extensions.Logging;
using VyralBot.Core.Models;
using VyralBot.Core.Services.ActionDispatcher;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.TriggerEngine;

/// <summary>
/// Monitors events and executes triggers when conditions match.
/// </summary>
public class TriggerEngine : ITriggerEngine
{
    private readonly ILogger<TriggerEngine> _logger;
    private readonly IEventBus _eventBus;
    private readonly IActionDispatcher _actionDispatcher;
    private readonly List<Guid> _subscriptionIds = new();
    private bool _isRunning;

    public int ActiveTriggerCount { get; private set; }

    public TriggerEngine(
        ILogger<TriggerEngine> logger,
        IEventBus eventBus,
        IActionDispatcher actionDispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _actionDispatcher = actionDispatcher ?? throw new ArgumentNullException(nameof(actionDispatcher));
    }

    /// <summary>
    /// Starts monitoring events.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("TriggerEngine is already running");
            return;
        }

        _logger.LogInformation("Starting TriggerEngine...");

        await ReloadTriggersAsync();

        // Subscribe to all event types
        // TODO: Subscribe based on trigger configurations loaded from database
        // For now, subscribe to base IEvent type
        var subscriptionId = await _eventBus.SubscribeAsync<IEvent>(OnEventReceivedAsync, cancellationToken);
        _subscriptionIds.Add(subscriptionId);

        _isRunning = true;
        _logger.LogInformation("TriggerEngine started with {Count} active triggers", ActiveTriggerCount);
    }

    /// <summary>
    /// Stops the trigger engine.
    /// </summary>
    public Task StopAsync()
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Stopping TriggerEngine...");

        // Unsubscribe from all events
        foreach (var subscriptionId in _subscriptionIds)
        {
            _eventBus.Unsubscribe(subscriptionId);
        }
        _subscriptionIds.Clear();

        _isRunning = false;
        _logger.LogInformation("TriggerEngine stopped");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Manually fires a trigger.
    /// </summary>
    public async Task FireTriggerAsync(Guid triggerId, Dictionary<string, object>? variables = null)
    {
        _logger.LogInformation("Manually firing trigger {TriggerId}", triggerId);

        var context = new ActionContext
        {
            TriggerId = triggerId,
            Platform = "Manual",
            CancellationToken = CancellationToken.None
        };

        if (variables != null)
        {
            foreach (var (key, value) in variables)
            {
                context.SetVariable(key, value);
            }
        }

        // TODO: Load action sequence from database
        // For now, execute placeholder
        var actionIds = new[] { Guid.NewGuid() };
        await _actionDispatcher.ExecuteSequenceAsync(actionIds, context);
    }

    /// <summary>
    /// Reloads triggers from database.
    /// </summary>
    public Task ReloadTriggersAsync()
    {
        // TODO: Load triggers from database
        // For now, set placeholder count
        ActiveTriggerCount = 0;

        _logger.LogDebug("Reloaded {Count} triggers from database", ActiveTriggerCount);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Event handler called when any event is published.
    /// </summary>
    private async Task OnEventReceivedAsync(IEvent @event)
    {
        _logger.LogTrace("Event received: {EventType} from {Source}", @event.GetType().Name, @event.Source);

        // TODO: Match event to triggers based on:
        // - Event type
        // - Source platform
        // - Conditions (regex, comparisons)
        // - Cooldowns
        // - Permissions

        // For now, just log
        await Task.CompletedTask;
    }
}
