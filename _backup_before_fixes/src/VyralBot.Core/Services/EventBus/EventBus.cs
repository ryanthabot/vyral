using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.EventBus;

/// <summary>
/// High-performance event bus using System.Threading.Channels.
/// Provides concurrent, async pub/sub messaging with bounded capacity and concurrency limiting.
/// Compliant with CONTEXT_MASTER.md requirements: 500 capacity, max 4 concurrent handlers.
/// </summary>
public class EventBus : IEventBus, IDisposable
{
    private readonly ILogger<EventBus> _logger;
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Delegate>> _subscriptions = new();
    private readonly Channel<(IEvent Event, Type EventType)> _eventChannel;
    private readonly SemaphoreSlim _handlerSemaphore;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;
    private bool _disposed;
    private const int MaxConcurrentHandlers = 4;
    private const int ChannelCapacity = 500;

    public int SubscriptionCount => _subscriptions.Sum(kvp => kvp.Value.Count);

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create BOUNDED channel with DropOldest policy for backpressure
        _eventChannel = Channel.CreateBounded<(IEvent, Type)>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        // Limit concurrent handler execution to 4
        _handlerSemaphore = new SemaphoreSlim(MaxConcurrentHandlers, MaxConcurrentHandlers);

        // Start background processing task
        _processingTask = Task.Run(ProcessEventsAsync);

        _logger.LogInformation("EventBus started (Capacity: {Capacity}, MaxConcurrent: {MaxConcurrent})", 
            ChannelCapacity, MaxConcurrentHandlers);
    }

    /// <summary>
    /// Publishes an event to all registered subscribers.
    /// </summary>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        _logger.LogDebug("Publishing event {EventType} ({EventId}) from {Source}",
            typeof(TEvent).Name, @event.EventId, @event.Source);

        // Write to channel (non-blocking)
        await _eventChannel.Writer.WriteAsync((@event, typeof(TEvent)), cancellationToken);
    }

    /// <summary>
    /// Subscribes to events of a specific type.
    /// </summary>
    public Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var subscriptionId = Guid.NewGuid();
        var eventType = typeof(TEvent);

        var typeSubscriptions = _subscriptions.GetOrAdd(eventType, _ => new ConcurrentDictionary<Guid, Delegate>());
        typeSubscriptions.TryAdd(subscriptionId, handler);

        _logger.LogDebug("Subscribed to {EventType} (Subscription: {SubscriptionId})", eventType.Name, subscriptionId);

        return Task.FromResult(subscriptionId);
    }

    /// <summary>
    /// Unsubscribes from events.
    /// </summary>
    public void Unsubscribe(Guid subscriptionId)
    {
        foreach (var typeSubscriptions in _subscriptions.Values)
        {
            if (typeSubscriptions.TryRemove(subscriptionId, out _))
            {
                _logger.LogDebug("Unsubscribed (Subscription: {SubscriptionId})", subscriptionId);
                return;
            }
        }

        _logger.LogWarning("Subscription {SubscriptionId} not found", subscriptionId);
    }

    /// <summary>
    /// Background task that processes events from the channel.
    /// </summary>
    private async Task ProcessEventsAsync()
    {
        _logger.LogDebug("EventBus processing started");

        try
        {
            await foreach (var (eventInstance, eventType) in _eventChannel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await ProcessEventAsync(eventInstance, eventType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event {EventType} ({EventId})",
                        eventType.Name, eventInstance.EventId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("EventBus processing stopped");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "EventBus processing failed");
        }
    }

    /// <summary>
    /// Processes a single event by invoking all registered handlers with concurrency limiting.
    /// Maximum 4 handlers execute concurrently as per CONTEXT_MASTER.md requirements.
    /// </summary>
    private async Task ProcessEventAsync(IEvent eventInstance, Type eventType)
    {
        if (!_subscriptions.TryGetValue(eventType, out var typeSubscriptions) || typeSubscriptions.IsEmpty)
        {
            _logger.LogTrace("No subscribers for {EventType}", eventType.Name);
            return;
        }

        // Snapshot subscriptions to avoid long lock
        var handlers = typeSubscriptions.ToArray();
        var tasks = new List<Task>(handlers.Length);

        foreach (var (subscriptionId, handler) in handlers)
        {
            // Throttle handler execution to max 4 concurrent
            await _handlerSemaphore.WaitAsync(_cts.Token);
            
            var task = Task.Run(async () =>
            {
                try
                {
                    // Invoke handler asynchronously
                    var handlerTask = (Task)handler.DynamicInvoke(eventInstance)!;
                    await handlerTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error invoking handler for {EventType} (Subscription: {SubscriptionId})",
                        eventType.Name, subscriptionId);
                }
                finally
                {
                    _handlerSemaphore.Release();
                }
            }, _cts.Token);
            
            tasks.Add(task);
        }

        // Wait for all handlers to complete
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
            _logger.LogTrace("Processed {EventType} with {HandlerCount} handlers", eventType.Name, tasks.Count);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("EventBus stopping...");

        _cts.Cancel();
        _eventChannel.Writer.Complete();
        
        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error waiting for EventBus processing to complete");
        }

        _handlerSemaphore?.Dispose();
        _cts.Dispose();
        _disposed = true;

        _logger.LogInformation("EventBus stopped");
    }
}
