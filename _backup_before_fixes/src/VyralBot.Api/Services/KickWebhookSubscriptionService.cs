using Microsoft.Extensions.Logging;
using KickLib;
using KickLib.Models.v1.EventSubscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace VyralBot.Api.Services;

public class KickWebhookSubscriptionService
{
    private readonly ILogger<KickWebhookSubscriptionService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public KickWebhookSubscriptionService(
        ILogger<KickWebhookSubscriptionService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<List<(EventType EventType, string SubscriptionId)>> SubscribeToEventsAsync(string accessToken, List<EventType> eventTypes)
    {
        var subscriptions = new List<(EventType, string)>();

        try
        {
            _logger.LogInformation("🔔 Subscribing to {Count} Kick events using KickLib", eventTypes.Count);

            // Create a scope to resolve IKickApi (scoped service)
            using var scope = _serviceProvider.CreateScope();
            var kickApi = scope.ServiceProvider.GetRequiredService<IKickApi>();

            // Subscribe to events one by one to avoid rate limiting
            foreach (var eventType in eventTypes)
            {
                try
                {
                    _logger.LogInformation("📡 Subscribing to {EventType}...", eventType);

                    // Use KickLib's EventSubscriptions API to subscribe to single event
                    var result = await kickApi.EventSubscriptions.SubscribeToEventsAsync(
                        new List<EventType> { eventType },
                        accessToken);

                    if (result.IsFailed)
                    {
                        _logger.LogError("❌ Failed to subscribe to {EventType}: {Errors}", 
                            eventType, string.Join(", ", result.Errors.Select(e => e.Message)));
                        continue; // Continue with other events
                    }

                    // Process subscription response
                    var subscription = result.Value.FirstOrDefault();
                    if (subscription != null && subscription.HasSubscribed)
                    {
                        subscriptions.Add((eventType, subscription.SubscriptionId!));
                        _logger.LogInformation("✅ Subscribed to {EventType} (v{Version}), ID: {SubscriptionId}", 
                            subscription.Name, subscription.Version, subscription.SubscriptionId);
                    }
                    else
                    {
                        _logger.LogError("❌ Failed to subscribe to {EventType}: {Error}", 
                            eventType, subscription?.Error ?? "Unknown error");
                    }

                    // Add delay between subscriptions to avoid rate limiting
                    await Task.Delay(1000); // 1 second delay
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error subscribing to {EventType}", eventType);
                    // Continue with other events
                }
            }
            
            _logger.LogInformation("✅ Successfully subscribed to {Count}/{Total} events", 
                subscriptions.Count, eventTypes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error subscribing to Kick events");
        }

        return subscriptions;
    }

    public async Task<bool> UnsubscribeAsync(string subscriptionId, string accessToken)
    {
        try
        {
            _logger.LogInformation("🔕 Unsubscribing from Kick webhook: {SubscriptionId}", subscriptionId);

            using var scope = _serviceProvider.CreateScope();
            var kickApi = scope.ServiceProvider.GetRequiredService<IKickApi>();

            var result = await kickApi.EventSubscriptions.UnsubscribeEventsAsync(subscriptionId, accessToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("✅ Successfully unsubscribed from webhook: {SubscriptionId}", subscriptionId);
                return true;
            }
            else
            {
                _logger.LogError("❌ Failed to unsubscribe from webhook {SubscriptionId}: {Errors}", 
                    subscriptionId, string.Join(", ", result.Errors.Select(e => e.Message)));
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error unsubscribing from webhook: {SubscriptionId}", subscriptionId);
            return false;
        }
    }

    public async Task<bool> UnsubscribeAllAsync(List<string> subscriptionIds, string accessToken)
    {
        try
        {
            _logger.LogInformation("🔕 Unsubscribing from {Count} Kick webhooks", subscriptionIds.Count);

            using var scope = _serviceProvider.CreateScope();
            var kickApi = scope.ServiceProvider.GetRequiredService<IKickApi>();

            var result = await kickApi.EventSubscriptions.UnsubscribeEventsAsync(subscriptionIds, accessToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("✅ Successfully unsubscribed from all webhooks");
                return true;
            }
            else
            {
                _logger.LogError("❌ Failed to unsubscribe from webhooks: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Message)));
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error unsubscribing from webhooks");
            return false;
        }
    }

    public async Task<List<EventSubscriptionResponse>> GetActiveSubscriptionsAsync(string accessToken)
    {
        try
        {
            _logger.LogInformation("📋 Retrieving active Kick webhook subscriptions");

            using var scope = _serviceProvider.CreateScope();
            var kickApi = scope.ServiceProvider.GetRequiredService<IKickApi>();

            var result = await kickApi.EventSubscriptions.GetEventSubscriptionsAsync(accessToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("✅ Retrieved {Count} active webhook subscriptions", result.Value.Count);
                return result.Value.ToList();
            }
            else
            {
                _logger.LogError("❌ Failed to retrieve subscriptions: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Message)));
                return new List<EventSubscriptionResponse>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error retrieving webhook subscriptions");
            return new List<EventSubscriptionResponse>();
        }
    }

    public async Task<bool> ResubscribeToNewUrlAsync(string accessToken, List<EventType> eventTypes)
    {
        try
        {
            _logger.LogInformation("🔄 Resubscribing to webhook events (URL change detected)");

            // Get active subscriptions
            var activeSubscriptions = await GetActiveSubscriptionsAsync(accessToken);

            if (activeSubscriptions.Count > 0)
            {
                // Unsubscribe from old webhooks
                var subscriptionIds = activeSubscriptions.Select(s => s.Id).ToList();
                await UnsubscribeAllAsync(subscriptionIds, accessToken);
            }

            // Subscribe to new URL (webhook URL is set in Kick developer portal)
            var newSubscriptionIds = await SubscribeToEventsAsync(accessToken, eventTypes);

            var success = newSubscriptionIds.Count > 0;
            if (success)
            {
                _logger.LogInformation("✅ Successfully resubscribed {Count} webhooks", newSubscriptionIds.Count);
            }
            else
            {
                _logger.LogError("❌ Failed to resubscribe webhooks");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error resubscribing webhooks");
            return false;
        }
    }
}
