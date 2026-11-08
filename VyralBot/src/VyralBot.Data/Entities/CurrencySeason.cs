namespace VyralBot.Data.Entities;

/// <summary>
/// One row of the CurrencySeasons table: a loyalty season (competitive reset).
/// Rows are append-only history — starting a new season closes the currently
/// open row (writes <see cref="EndedAt"/> plus the top-leaderboard snapshot in
/// <see cref="TopCount"/>) and inserts the next one. Season rows are never
/// deleted, and the reset never touches CurrencyTransactions history.
/// </summary>
public class CurrencySeason
{
    /// <summary>Autoincrement row id; also the season order (higher = newer).</summary>
    public int Id { get; set; }

    /// <summary>Display name of the season (e.g. "Season 3").</summary>
    public required string Name { get; set; }

    /// <summary>UTC start of the season.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC end of the season (null while the season is running).</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// JSON snapshot of the top-20 leaderboard captured when the season was
    /// closed: [{"username":"...","platform":"...","balance":123}] (see
    /// SeasonService.SeasonLeaderEntry). Null when the season closed with an
    /// empty leaderboard or has not closed yet.
    /// </summary>
    public string? TopCount { get; set; }
}
