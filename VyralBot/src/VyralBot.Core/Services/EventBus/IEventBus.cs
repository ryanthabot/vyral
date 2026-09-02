namespace VyralBot.Core.Services.EventBus;

/// <summary>
/// Event bus for publishing and subscribing to events.
/// Provides type-safe, async pub/sub messaging.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to all registered subscribers.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// Subscribes to events of a specific type.
    /// </summary>
    /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
    /// <param name="handler">Handler function to process events.</param>
    /// <param name="cancellationToken">Cancellation token for the subscription.</param>
    /// <returns>Subscription ID that can be used to unsubscribe.</returns>
    Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// Unsubscribes from events.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID returned from SubscribeAsync.</param>
    void Unsubscribe(Guid subscriptionId);

    /// <summary>
    /// Gets the count of active subscriptions.
    /// </summary>
    int SubscriptionCount { get; }
}
