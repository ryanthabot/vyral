using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Loyalty seasons (competitive resets) on top of the append-only
/// CurrencySeasons table. Starting a season reads the live top-20 leaderboard
/// FIRST (so the closing snapshot is taken before any write, preserving the
/// winners even when the same call zeroes every balance), then performs the
/// close + insert + optional balance reset in one transaction at the data
/// layer. Points-earned history — TotalEarned/TotalSpent aggregates and the
/// CurrencyTransactions audit trail — is never modified or deleted.
/// </summary>
public class SeasonService : ISeasonService
{
    /// <summary>Rows captured in each season's closing snapshot (spec: top-20).</summary>
    public const int SnapshotSize = 20;

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<SeasonService> _logger;
    private readonly IDataService _dataService;

    public SeasonService(ILogger<SeasonService> logger, IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task<SeasonResult> StartSeasonAsync(string name, bool resetBalances)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new SeasonResult(false, "Enter a season name before starting.", null, 0);
        }

        // Snapshot BEFORE any write: read the live top-20 balances now, so the
        // closing season records the real final standings even when this same
        // call resets every balance to zero.
        var leaders = await _dataService.GetCurrencyLeaderboardAsync(SnapshotSize);
        var snapshot = new List<SeasonLeaderEntry>(capacity: SnapshotSize);
        foreach (var leader in leaders)
        {
            snapshot.Add(new SeasonLeaderEntry(leader.Username, leader.Platform, leader.Balance));
        }
        var topJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);

        var (season, resetCount) = await _dataService.StartCurrencySeasonAsync(trimmed, topJson, resetBalances);

        _logger.LogInformation(
            "Started currency season {SeasonName} (Id {SeasonId}); {ResetCount} balances zeroed (reset requested: {ResetRequested})",
            trimmed, season.Id, resetCount, resetBalances);

        var message = resetBalances
            ? $"Season \"{trimmed}\" started - previous season closed and {resetCount:N0} balances reset to zero."
            : $"Season \"{trimmed}\" started - previous season closed.";
        return new SeasonResult(true, message, season, resetCount);
    }

    public async Task<IReadOnlyList<CurrencySeason>> GetSeasonsAsync(int limit = 20)
    {
        var seasons = await _dataService.GetCurrencySeasonsAsync(limit);
        return new List<CurrencySeason>(seasons);
    }

    public Task<CurrencySeason?> GetCurrentSeasonAsync()
        => _dataService.GetCurrentCurrencySeasonAsync();

    public async Task<SeasonStatus?> GetStatusAsync()
    {
        var current = await _dataService.GetCurrentCurrencySeasonAsync();
        if (current is null)
        {
            return null;
        }

        // Day 1 on the start date, then one per elapsed 24h.
        var elapsed = DateTime.UtcNow - current.StartedAt;
        var day = Math.Max(1, (int)elapsed.TotalDays + 1);

        // Season history is newest-first with the open season at index 0; the
        // first CLOSED row is the previous season.
        string? previousWinner = null;
        var history = await _dataService.GetCurrencySeasonsAsync(2);
        foreach (var season in history)
        {
            if (season.EndedAt is not null && season.Id != current.Id)
            {
                var top = ParseTopCount(season.TopCount);
                if (top is { Count: > 0 })
                {
                    previousWinner = top[0].Username;
                }
                break;
            }
        }

        return new SeasonStatus(current.Name, day, previousWinner);
    }

    public async Task<string?> GetSeasonMessageAsync()
    {
        var status = await GetStatusAsync();
        if (status is null)
        {
            return null;
        }

        var previous = status.PreviousWinner is null
            ? string.Empty
            : $" Previous winner: {status.PreviousWinner}.";
        return $"Season: {status.Name} — day {status.Day}.{previous}";
    }

    /// <summary>
    /// Parses a season's closing leaderboard snapshot (CurrencySeason.TopCount
    /// JSON) into entries, or null when the column is empty/unreadable. Used
    /// by the Currency page history rows; tolerant of a null/blank column.
    /// </summary>
    public static List<SeasonLeaderEntry>? ParseTopCount(string? topCountJson)
    {
        if (string.IsNullOrWhiteSpace(topCountJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<SeasonLeaderEntry>>(topCountJson, SnapshotJsonOptions);
        }
        catch (JsonException)
        {
            // A hand-edited or corrupted snapshot must never break the page.
            return null;
        }
    }
}
