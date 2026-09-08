namespace VyralBot.Data.Entities;

/// <summary>A Discord channel a bot monitors, tagged with purpose CSV ("Moderation,Support,Logging").</summary>
public class ConnectedChannel
{
    public int Id { get; set; }
    public string BotId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string GuildName { get; set; } = "";
    public string Purposes { get; set; } = "";
    public bool MonitoringEnabled { get; set; } = true;
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}
