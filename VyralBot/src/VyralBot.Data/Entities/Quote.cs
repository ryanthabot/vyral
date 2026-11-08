namespace VyralBot.Data.Entities;

/// <summary>
/// One row of the Quotes table: a chat quote (streamer.bot staple) added via
/// "!quote add &lt;text&gt;" on any platform or manually from the Commands page
/// QUOTES tab. Number is a small stable autoincrement per profile database so
/// viewers can recall quotes with "!quote &lt;n&gt;".
/// </summary>
public class Quote
{
    /// <summary>Stable small per-profile number (SQLite autoincrement PK) used by !quote &lt;n&gt;.</summary>
    public int Number { get; set; }

    /// <summary>The quote text itself.</summary>
    public string Text { get; set; } = "";

    /// <summary>Who added the quote (chat username, or "Manual" from the dashboard).</summary>
    public string AddedBy { get; set; } = "";

    /// <summary>Platform the quote was added from (Twitch/Kick/YouTube/Discord, or "Manual").</summary>
    public string Platform { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
