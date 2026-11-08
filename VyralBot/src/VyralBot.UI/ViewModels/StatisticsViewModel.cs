using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VyralBot.Core.Services.Statistics;
using VyralBot.UI.Controls.Charts;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// ViewModel for event statistics and analytics.
/// </summary>
public partial class StatisticsViewModel : ViewModelBase
{
    private readonly ILogger<StatisticsViewModel> _logger;
    private readonly IStatisticsService _statisticsService;
    private readonly ISessionSummaryService _sessionSummaryService;

    [ObservableProperty]
    private ObservableCollection<EventCountSummary> _eventCounts = new();

    [ObservableProperty]
    private ObservableCollection<UserStatistic> _topContributors = new();

    [ObservableProperty]
    private ObservableCollection<UserStatistic> _topUsers = new();

    /// <summary>Overview chart bars: top event types in the selected range (main tab).</summary>
    [ObservableProperty]
    private ObservableCollection<BarChartItem> _eventTypeChartItems = new();

    /// <summary>Top-contributor chart bars (Top Contributors tab).</summary>
    [ObservableProperty]
    private ObservableCollection<BarChartItem> _contributorChartItems = new();

    [ObservableProperty]
    private string _selectedEventType = "All";

    [ObservableProperty]
    private string? _selectedPlatform;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalEvents;

    /// <summary>Result of the last export attempt, shown under the filter card.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableEventTypes = new()
    {
        "All",
        // Exact pipeline event-type names (writers store Title Case); shown as-is
        // so the SQL UPPER(EventType) = UPPER(@EventType) match is 1:1.
        "Follow",
        "Subscribe",
        "Resub",
        "GiftSub",
        "Raid",
        "Cheer",
        "Host",
        "KicksGifted",
        "RewardRedeemed",
        // Discord pipeline events (writers store these exact Title Case names).
        "MemberJoined",
        "Boost"
    };

    [ObservableProperty]
    private ObservableCollection<string?> _availablePlatforms = new() { null, "Kick", "Twitch", "YouTube", "Discord", "TikTok" };

    /// <summary>Most recent stream session summary (Sessions tab "Last session" card).</summary>
    [ObservableProperty]
    private StreamSessionSummary? _lastSessionSummary;

    /// <summary>True when a persisted session summary exists (drives the card vs. empty state).</summary>
    [ObservableProperty]
    private bool _hasLastSession;

    /// <summary>Recent session summaries, newest first (Sessions tab list).</summary>
    [ObservableProperty]
    private ObservableCollection<StreamSessionSummary> _recentSessions = new();

    /// <summary>Formatted "Type ×N" rows for the last session's top-3 event types.</summary>
    [ObservableProperty]
    private ObservableCollection<string> _lastSessionTopEvents = new();

    /// <summary>True when at least one persisted session summary exists.</summary>
    [ObservableProperty]
    private bool _hasRecentSessions;

    public StatisticsViewModel(
        ILogger<StatisticsViewModel> logger,
        IStatisticsService statisticsService,
        ISessionSummaryService sessionSummaryService)
    {
        _logger = logger;
        _statisticsService = statisticsService;
        _sessionSummaryService = sessionSummaryService;
        
        // Default to last 30 days. EndDate must be midnight of TOMORROW (exclusive
        // upper bound in the SQL <= comparison) or everything after midnight today
        // would be excluded from the default range.
        EndDate = DateTime.Today.AddDays(1);
        StartDate = DateTime.Today.AddDays(-30);
    }

    /// <summary>
    /// Initialize the ViewModel by loading statistics.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadStatisticsAsync();
    }

    /// <summary>
    /// Load all statistics data.
    /// </summary>
    [RelayCommand]
    private async Task LoadStatisticsAsync()
    {
        IsLoading = true;
        try
        {
            var (startDate, endDate) = ClampDateRange();

            // Load event counts
            var counts = await _statisticsService.GetEventCountsByTypeAsync(startDate, endDate);
            EventCounts.Clear();
            TotalEvents = 0;

            foreach (var platform in counts)
            {
                // Apply the platform filter to the event-count grid too
                // (GetEventCountsByTypeAsync has no platform argument).
                if (SelectedPlatform != null &&
                    !platform.Key.Equals(SelectedPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var eventType in platform.Value)
                {
                    EventCounts.Add(new EventCountSummary
                    {
                        Platform = platform.Key,
                        EventType = eventType.Key,
                        Count = eventType.Value
                    });
                    TotalEvents += eventType.Value;
                }
            }

            // Overview chart: top-8 event types by aggregated count (same rows as
            // the grid above — no extra query).
            EventTypeChartItems.Clear();
            foreach (var bar in StatisticsChartBuilder.BuildEventTypeChartItems(EventCounts))
            {
                EventTypeChartItems.Add(bar);
            }

            // Load top contributors for selected event type
            await LoadTopContributorsAsync();

            // Load top users
            // GetTopUsersAsync has no platform argument, so the filter is applied
            // client-side. Extra rows are fetched so the list still fills up to 20
            // after filtering.
            var topUsers = await _statisticsService.GetTopUsersAsync(limit: 100);
            TopUsers.Clear();
            foreach (var user in topUsers)
            {
                if (SelectedPlatform != null &&
                    !user.Platform.Equals(SelectedPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TopUsers.Count >= 20)
                {
                    break;
                }

                TopUsers.Add(user);
            }

            _logger.LogInformation("Statistics loaded successfully: {TotalEvents} events", TotalEvents);

            // Stream session summaries (Sessions tab): most recent + the list.
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading statistics");
            MessageBox.Show($"Error loading statistics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load the persisted stream session summaries (Statistics → Sessions tab).
    /// Failures are logged only — the analytics grids must keep working when
    /// the summary store is unreadable.
    /// </summary>
    private async Task LoadSessionsAsync()
    {
        try
        {
            var summaries = await _sessionSummaryService.GetRecentSummariesAsync(limit: 20);
            RecentSessions.Clear();
            foreach (var summary in summaries)
            {
                RecentSessions.Add(summary);
            }

            LastSessionSummary = summaries.Count > 0 ? summaries[0] : null;
            HasLastSession = LastSessionSummary is not null;
            HasRecentSessions = summaries.Count > 0;

            // Top-3 event types of the last session ("Follow × 12"), value
            // descending with an ordinal label tie-break (chart-builder rule).
            LastSessionTopEvents.Clear();
            if (LastSessionSummary is not null)
            {
                foreach (var row in LastSessionSummary.EventCounts
                             .Where(kvp => kvp.Value > 0)
                             .OrderByDescending(kvp => kvp.Value)
                             .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                             .Take(3))
                {
                    LastSessionTopEvents.Add($"{row.Key} × {row.Value}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading stream session summaries");
            LastSessionSummary = null;
            HasLastSession = false;
            HasRecentSessions = false;
            LastSessionTopEvents.Clear();
        }
    }

    /// <summary>
    /// Load top contributors for the selected event type.
    /// </summary>
    private async Task LoadTopContributorsAsync()
    {
        if (SelectedEventType == "All")
        {
            TopContributors.Clear();
            ContributorChartItems.Clear();
            return;
        }

        var contributors = await _statisticsService.GetTopContributorsAsync(SelectedEventType, SelectedPlatform, limit: 20, startDate: StartDate, endDate: EndDate);
        TopContributors.Clear();
        foreach (var contributor in contributors)
        {
            TopContributors.Add(contributor);
        }

        // Contributor chart reuses the rows just loaded into the grid.
        ContributorChartItems.Clear();
        foreach (var bar in StatisticsChartBuilder.BuildContributorChartItems(TopContributors))
        {
            ContributorChartItems.Add(bar);
        }
    }

    /// <summary>
    /// Handle event type selection change.
    /// </summary>
    partial void OnSelectedEventTypeChanged(string value)
    {
        _ = LoadTopContributorsAsync();
    }

    /// <summary>
    /// Handle platform selection change.
    /// </summary>
    partial void OnSelectedPlatformChanged(string? value)
    {
        _ = LoadStatisticsAsync();
    }

    /// <summary>
    /// Export the current filter's aggregated rows (Platform/EventType/Count,
    /// same data as the Event Counts grid) to CSV. UTF-8 with BOM so Excel
    /// opens it directly; every field quoted and comma-escaped.
    /// </summary>
    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Export Statistics to CSV",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"EventStatistics_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveDialog.ShowDialog() != true)
            {
                return;
            }

            IsLoading = true;
            var (startDate, endDate) = ClampDateRange();

            // Re-aggregate from the service with the CURRENT filters so the file
            // matches the grid even if the user changed a picker without Refresh.
            var counts = await _statisticsService.GetEventCountsByTypeAsync(startDate, endDate);

            var csv = new StringBuilder();
            csv.AppendLine("Platform,EventType,Count");

            var rowCount = 0;
            foreach (var platform in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (SelectedPlatform != null &&
                    !platform.Key.Equals(SelectedPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var eventType in platform.Value.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    csv.AppendLine($"{Csv(platform.Key)},{Csv(eventType.Key)},{eventType.Value}");
                    rowCount++;
                }
            }

            // UTF-8 with BOM (encoderShouldEmitUTF8Identifier) — File.WriteAllTextAsync
            // without an encoding writes UTF-8 WITHOUT BOM, which Excel misreads.
            await File.WriteAllTextAsync(saveDialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            StatusMessage = $"Exported {rowCount} aggregated rows to {saveDialog.FileName}";
            _logger.LogInformation("Statistics exported to {FileName} ({Rows} rows)", saveDialog.FileName, rowCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting statistics");
            StatusMessage = $"Export failed: {ex.Message}";
            MessageBox.Show($"Error exporting statistics: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Normalize the custom date range: clamp the end to tomorrow-midnight
    /// (the exclusive upper bound — nothing future exists yet) and force
    /// start ≤ end. Writes clamped values back so the pickers reflect them.
    /// </summary>
    private (DateTime Start, DateTime End) ClampDateRange()
    {
        var maxEnd = DateTime.Today.AddDays(1);
        var end = EndDate ?? maxEnd;
        if (end > maxEnd)
        {
            end = maxEnd;
        }

        var start = StartDate ?? end.AddDays(-30);
        if (start > end)
        {
            start = end;
        }

        if (EndDate != end)
        {
            EndDate = end;
        }

        if (StartDate != start)
        {
            StartDate = start;
        }

        return (start, end);
    }

    /// <summary>Quote a CSV field, escaping embedded quotes (RFC 4180).</summary>
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    /// <summary>
    /// Refresh statistics with current filters.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadStatisticsAsync();
    }
}

/// <summary>
/// Summary of event counts by type and platform.
/// </summary>
public class EventCountSummary
{
    public string Platform { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Pure aggregation/normalization for the statistics bar charts (KI-54).
/// No data access — builds <see cref="BarChartItem"/> rows from collections
/// the view model has already loaded, so it is directly unit-testable.
/// </summary>
public static class StatisticsChartBuilder
{
    /// <summary>
    /// Overview chart: group the event-count grid rows by event type (summing
    /// counts across platforms when "All" is selected); each bar is colored by
    /// the platform that contributed the most rows for that type. Top-N by
    /// total count, bar width normalized to the largest total.
    /// </summary>
    public static IReadOnlyList<BarChartItem> BuildEventTypeChartItems(IEnumerable<EventCountSummary> rows, int maxItems = 8)
    {
        var grouped = rows
            .GroupBy(r => r.EventType, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Label: g.Key,
                          Platform: g.OrderByDescending(r => r.Count)
                                     .ThenBy(r => r.Platform, StringComparer.Ordinal)
                                     .First().Platform,
                          Value: g.Sum(r => r.Count)));

        return ToBars(grouped, maxItems);
    }

    /// <summary>
    /// Top-contributor chart: one bar per user (label = username, color =
    /// platform), top-N by contribution count, width normalized to the leader.
    /// </summary>
    public static IReadOnlyList<BarChartItem> BuildContributorChartItems(IEnumerable<UserStatistic> contributors, int maxItems = 8)
        => ToBars(contributors.Select(c => (c.Username, c.Platform, c.Count)), maxItems);

    /// <summary>
    /// Shared bar math: drop zero-count rows, order by value descending with an
    /// ordinal label tie-break (deterministic), cap at <paramref name="maxItems"/>,
    /// then normalize widths to the largest remaining value (0..1, 0 when empty).
    /// </summary>
    private static IReadOnlyList<BarChartItem> ToBars(IEnumerable<(string Label, string Platform, int Value)> rows, int maxItems)
    {
        var ordered = rows
            .Where(r => r.Value > 0)
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Label, StringComparer.Ordinal)
            .Take(Math.Max(0, maxItems))
            .ToList();

        var max = ordered.Count > 0 ? ordered[0].Value : 0;

        return ordered
            .Select(r => new BarChartItem
            {
                Label = r.Label,
                Platform = StatisticsChartBuilder.CanonicalPlatform(r.Platform),
                Value = r.Value,
                Fraction = max > 0 ? (double)r.Value / max : 0d
            })
            .ToList();
    }

    /// <summary>
    /// Normalize platform casing for the chart's DataTriggers (case-sensitive
    /// matches on Twitch/Kick/YouTube/Discord/TikTok); unknown platforms pass
    /// through unchanged and fall back to PrimaryBrush.
    /// </summary>
    public static string CanonicalPlatform(string? platform)
    {
        return platform?.ToLowerInvariant() switch
        {
            "twitch" => "Twitch",
            "kick" => "Kick",
            "youtube" => "YouTube",
            "discord" => "Discord",
            "tiktok" => "TikTok",
            _ => platform ?? string.Empty
        };
    }
}
