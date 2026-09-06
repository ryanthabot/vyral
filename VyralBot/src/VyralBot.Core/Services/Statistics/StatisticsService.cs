using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Statistics;

/// <summary>
/// Service for aggregating and analyzing event statistics.
/// </summary>
public interface IStatisticsService
{
    /// <summary>
    /// Get event count statistics grouped by type and platform.
    /// </summary>
    Task<Dictionary<string, Dictionary<string, int>>> GetEventCountsByTypeAsync(DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Get top contributors for a specific event type (e.g., top gifters, top cheerers).
    /// </summary>
    Task<List<UserStatistic>> GetTopContributorsAsync(string eventType, string? platform = null, int limit = 10, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Get all-time top contributors across all events.
    /// </summary>
    Task<List<UserStatistic>> GetTopUsersAsync(int limit = 10);

    /// <summary>
    /// Export statistics to CSV format.
    /// </summary>
    Task<string> ExportToCsvAsync(DateTime? startDate = null, DateTime? endDate = null, string? platform = null);

    /// <summary>
    /// Get statistics for a specific user.
    /// </summary>
    Task<UserStatisticDetail> GetUserStatisticsAsync(string username);

    /// <summary>
    /// Get event counts by date (for charting).
    /// </summary>
    Task<Dictionary<DateTime, Dictionary<string, int>>> GetEventCountsByDateAsync(DateTime startDate, DateTime endDate);
}

public class StatisticsService : IStatisticsService
{
    private readonly ILogger<StatisticsService> _logger;
    private readonly IDataService _dataService;

    public StatisticsService(ILogger<StatisticsService> logger, IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;
    }

    public async Task<Dictionary<string, Dictionary<string, int>>> GetEventCountsByTypeAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        // Date filtering pushed into the SQL query (with bound DateTime parameters)
        // so LIMIT applies after filtering instead of truncating the window.
        var stats = await _dataService.GetEventStatisticsAsync(limit: 10000, startDate: startDate, endDate: endDate);

        var result = new Dictionary<string, Dictionary<string, int>>();

        foreach (var stat in stats)
        {
            if (!result.ContainsKey(stat.Platform))
                result[stat.Platform] = new Dictionary<string, int>();

            if (!result[stat.Platform].ContainsKey(stat.EventType))
                result[stat.Platform][stat.EventType] = 0;

            result[stat.Platform][stat.EventType]++;
        }

        _logger.LogInformation("Retrieved event counts by type: {Platforms} platforms, {Total} total events",
            result.Keys.Count, stats.Count());

        return result;
    }

    public async Task<List<UserStatistic>> GetTopContributorsAsync(string eventType, string? platform = null, int limit = 10, DateTime? startDate = null, DateTime? endDate = null)
    {
        // eventType/platform/dates are SQL-filtered; SQL is case-insensitive on
        // EventType (UPPER comparison) so legacy rows with different casing match.
        var stats = await _dataService.GetEventStatisticsAsync(platform, eventType, limit: 10000, startDate: startDate, endDate: endDate);

        var userStats = stats
            .GroupBy(s => new { s.Username, s.Platform })
            .Select(g => new UserStatistic
            {
                Username = g.Key.Username,
                Platform = g.Key.Platform,
                EventType = eventType,
                Count = g.Count(),
                TotalValue = g.Sum(s => ExtractValueFromEventData(s.EventData))
            })
            .OrderByDescending(u => u.TotalValue > 0 ? u.TotalValue : u.Count)
            .Take(limit)
            .ToList();

        _logger.LogInformation("Retrieved top {Limit} contributors for {EventType}: {Results} users", 
            limit, eventType, userStats.Count);

        return userStats;
    }

    public async Task<List<UserStatistic>> GetTopUsersAsync(int limit = 10)
    {
        var stats = await _dataService.GetEventStatisticsAsync(limit: 10000);

        var userStats = stats
            .GroupBy(s => new { s.Username, s.Platform })
            .Select(g => new UserStatistic
            {
                Username = g.Key.Username,
                Platform = g.Key.Platform,
                EventType = "All Events",
                Count = g.Count(),
                TotalValue = g.Sum(s => ExtractValueFromEventData(s.EventData))
            })
            .OrderByDescending(u => u.Count)
            .Take(limit)
            .ToList();

        _logger.LogInformation("Retrieved top {Limit} users: {Results} users", limit, userStats.Count);

        return userStats;
    }

    public async Task<string> ExportToCsvAsync(DateTime? startDate = null, DateTime? endDate = null, string? platform = null)
    {
        // Platform and date filters are applied in SQL (limit applies post-filter).
        var stats = await _dataService.GetEventStatisticsAsync(platform: platform, limit: 100000, startDate: startDate, endDate: endDate);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,Platform,EventType,Username,EventData,WasAutoReplied");

        foreach (var stat in stats.OrderBy(s => s.Timestamp))
        {
            var eventDataEscaped = stat.EventData?.Replace("\"", "\"\"") ?? "";
            csv.AppendLine($"\"{stat.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{stat.Platform}\",\"{stat.EventType}\",\"{stat.Username}\",\"{eventDataEscaped}\",{stat.WasAutoReplied}");
        }

        _logger.LogInformation("Exported {Count} event statistics to CSV", stats.Count());

        return csv.ToString();
    }

    public async Task<UserStatisticDetail> GetUserStatisticsAsync(string username)
    {
        var stats = await _dataService.GetEventStatisticsByUsernameAsync(username);

        var detail = new UserStatisticDetail
        {
            Username = username,
            TotalEvents = stats.Count(),
            EventsByType = stats
                .GroupBy(s => s.EventType)
                .ToDictionary(g => g.Key, g => g.Count()),
            EventsByPlatform = stats
                .GroupBy(s => s.Platform)
                .ToDictionary(g => g.Key, g => g.Count()),
            FirstEventDate = stats.Any() ? stats.Min(s => s.Timestamp) : (DateTime?)null,
            LastEventDate = stats.Any() ? stats.Max(s => s.Timestamp) : (DateTime?)null,
            TotalValue = stats.Sum(s => ExtractValueFromEventData(s.EventData))
        };

        _logger.LogInformation("Retrieved statistics for user {Username}: {Total} events", username, detail.TotalEvents);

        return detail;
    }

    public async Task<Dictionary<DateTime, Dictionary<string, int>>> GetEventCountsByDateAsync(DateTime startDate, DateTime endDate)
    {
        var stats = await _dataService.GetEventStatisticsAsync(limit: 100000, startDate: startDate, endDate: endDate);

        var result = new Dictionary<DateTime, Dictionary<string, int>>();

        foreach (var stat in stats)
        {
            var date = stat.Timestamp.Date;

            if (!result.ContainsKey(date))
                result[date] = new Dictionary<string, int>();

            var key = $"{stat.Platform}_{stat.EventType}";

            if (!result[date].ContainsKey(key))
                result[date][key] = 0;

            result[date][key]++;
        }

        _logger.LogInformation("Retrieved event counts by date from {StartDate} to {EndDate}: {Days} days", 
            startDate, endDate, result.Keys.Count);

        return result;
    }

    /// <summary>
    /// Extract numeric value from JSON EventData (e.g., giftsub count, raid viewers, bits amount).
    /// </summary>
    private int ExtractValueFromEventData(string? eventData)
    {
        if (string.IsNullOrEmpty(eventData))
            return 0;

        try
        {
            var json = System.Text.Json.JsonDocument.Parse(eventData);
            var root = json.RootElement;

            // Try common value fields
            if (root.TryGetProperty("count", out var countProp) && countProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                return countProp.GetInt32();

            if (root.TryGetProperty("amount", out var amountProp) && amountProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                return amountProp.GetInt32();

            if (root.TryGetProperty("viewers", out var viewersProp) && viewersProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                return viewersProp.GetInt32();

            if (root.TryGetProperty("bits", out var bitsProp) && bitsProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                return bitsProp.GetInt32();

            if (root.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                return valueProp.GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse EventData JSON: {EventData}", eventData);
        }

        return 0;
    }
}

/// <summary>
/// User statistic summary for leaderboards.
/// </summary>
public class UserStatistic
{
    public string Username { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int Count { get; set; }
    public int TotalValue { get; set; }
}

/// <summary>
/// Detailed statistics for a single user.
/// </summary>
public class UserStatisticDetail
{
    public string Username { get; set; } = string.Empty;
    public int TotalEvents { get; set; }
    public Dictionary<string, int> EventsByType { get; set; } = new();
    public Dictionary<string, int> EventsByPlatform { get; set; } = new();
    public DateTime? FirstEventDate { get; set; }
    public DateTime? LastEventDate { get; set; }
    public int TotalValue { get; set; }
}
