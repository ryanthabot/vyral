namespace VyralBot.Data.Entities;

public class WebServerSettings
{
    public int Id { get; set; } = 1; // Single row
    public bool IsEnabled { get; set; }
    public int Port { get; set; } = 5000;
    public string NgrokRegion { get; set; } = "us";
    public string? CurrentTunnelUrl { get; set; }
    public bool AutoStartOnLaunch { get; set; } = false;
    public DateTime? LastStarted { get; set; }
    public DateTime? LastStopped { get; set; }
}

public class WebhookSubscription
{
    public string Id { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int EventVersion { get; set; } = 1;
    public string? BroadcasterUserId { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastEventReceived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
