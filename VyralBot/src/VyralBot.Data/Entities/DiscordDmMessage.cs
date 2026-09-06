using System;

namespace VyralBot.Data.Entities;

/// <summary>
/// One persisted Discord direct message involving a bot account — either
/// received from a user or sent by the bot. DM history is unlimited and is
/// rebuilt into the chat inbox from these rows.
/// </summary>
public class DiscordDmMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Manager row id of the bot involved ("primary" for the wizard session).</summary>
    public string BotId { get; set; } = "";

    /// <summary>Detected bot username at send/receive time, when known.</summary>
    public string? BotUsername { get; set; }

    /// <summary>DM channel snowflake.</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>The human participant's username (conversation key).</summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// The human participant's Discord snowflake user id, when known —
    /// lets replies reach the user by id even when the DM channel is not
    /// gateway-cached, and keeps history searchable by id.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>"Incoming" (user → bot) or "Outgoing" (bot → user).</summary>
    public string Direction { get; set; } = "Incoming";

    /// <summary>Message text.</summary>
    public string Content { get; set; } = "";

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
