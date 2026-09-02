namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Interface for Twitch EventSub WebSocket service.
/// </summary>
public interface ITwitchEventSubService : IDisposable
{
    /// <summary>
    /// Connects to Twitch EventSub and subscribes to events.
    /// </summary>
    Task ConnectAsync(string clientId, string accessToken, string userId);

    /// <summary>
    /// Disconnects from Twitch EventSub.
    /// </summary>
    Task DisconnectAsync();
}
