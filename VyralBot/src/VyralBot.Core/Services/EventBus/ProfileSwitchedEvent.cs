namespace VyralBot.Core.Services.EventBus;

/// <summary>
/// Event fired when the active profile is switched.
/// </summary>
public record ProfileSwitchedEvent : EventBase
{
    public required string OldProfileName { get; init; }
    public required string NewProfileName { get; init; }
    
    public ProfileSwitchedEvent()
    {
        Source = "System";
    }
}

public record PlatformChatRoutingChangedEvent : EventBase
{
    public string Platform { get; init; } = "";
    public bool Enabled { get; init; }
}

public record PlatformConnectionChangedEvent : EventBase
{
    public string Platform { get; init; } = "";
    public bool Connected { get; init; }
    public string? Channel { get; init; }
    public string? BotUsername { get; init; }
}
