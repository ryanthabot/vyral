using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Loyalty seasons (competitive resets): starting a season closes the previous
/// one with a top-20 leaderboard snapshot and optionally zeroes every viewer
/// balance so the next race starts from zero. Season rows are append-only
/// history and currency transactions are never deleted.
/// </summary>
public interface ISeasonService
{
    /// <summary>
    /// Starts a new season. The current top-20 leaderboard is snapshotted into
    /// the PREVIOUS season row (closing it with its end date) before any
    /// write, then — when <paramref name="resetBalances"/> is true — every
    /// UserCurrency balance is zeroed in the same transaction as the close +
    /// insert. Points-earned history (lifetime aggregates and the
    /// CurrencyTransactions audit trail) is never touched.
    /// </summary>
    /// <param name="name">Display name (e.g. "Season 3"); trimmed, must not be empty.</param>
    /// <param name="resetBalances">Zero all viewer balances (competitive reset).</param>
    Task<SeasonResult> StartSeasonAsync(string name, bool resetBalances);

    /// <summary>Season history, newest first (the open season is item 1).</summary>
    Task<IReadOnlyList<CurrencySeason>> GetSeasonsAsync(int limit = 20);

    /// <summary>The open season, or null before the first season is started.</summary>
    Task<CurrencySeason?> GetCurrentSeasonAsync();

    /// <summary>
    /// Live status for the "!season" reply and the Currency page card:
    /// name, day number (1-based) and the previous season's winner (null when
    /// there is no prior season or it had an empty leaderboard). Null when no
    /// season has been started.
    /// </summary>
    Task<SeasonStatus?> GetStatusAsync();

    /// <summary>
    /// The "!season" chat line: "Season: {name} — day {n}. Previous winner:
    /// {user}." (the winner sentence is omitted when there is none). Null when
    /// no season has been started yet — callers decide the fallback wording.
    /// </summary>
    Task<string?> GetSeasonMessageAsync();
}

/// <summary>Outcome of <see cref="ISeasonService.StartSeasonAsync"/>.</summary>
/// <param name="Success">False when the name was empty — nothing was written.</param>
/// <param name="Message">Ready-to-display status line for the Currency page.</param>
/// <param name="Season">The new season row (null when Success is false).</param>
/// <param name="ResetBalances">Number of viewer balances zeroed (0 when the reset was off).</param>
public sealed record SeasonResult(bool Success, string Message, CurrencySeason? Season, int ResetBalances);

/// <summary>Live season status (<see cref="ISeasonService.GetStatusAsync"/>).</summary>
/// <param name="Name">Current season name.</param>
/// <param name="Day">1-based day number of the current season.</param>
/// <param name="PreviousWinner">Winner (top balance) of the previous season, null when there is none.</param>
public sealed record SeasonStatus(string Name, int Day, string? PreviousWinner);

/// <summary>One entry of a season's closing top-leaderboard snapshot (CurrencySeason.TopCount JSON).</summary>
public sealed record SeasonLeaderEntry(string Username, string Platform, int Balance);
