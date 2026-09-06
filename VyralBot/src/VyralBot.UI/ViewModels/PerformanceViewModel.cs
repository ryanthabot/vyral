using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Api.Services;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.Performance;
using VyralBot.Data;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Live performance monitor for the Settings → Performance tab. Pulls one
/// snapshot per refresh from <see cref="IMetricsRegistry"/> (activity
/// counters) and <see cref="PerformanceTelemetryService"/> (process/child
/// process metrics), computes per-second rates from the delta, and exposes
/// summary tiles, sparklines and platform/component/process rows.
///
/// The refresh timer only runs while the tab is visible
/// (<see cref="Start"/>/<see cref="Stop"/> from the view's Loaded/Unloaded);
/// the underlying counters keep accumulating app-wide either way.
/// </summary>
public partial class PerformanceViewModel : ViewModelBase
{
    private static readonly string[] KnownPlatforms = { "Twitch", "Kick", "YouTube", "TikTok", "Discord" };

    private readonly ILogger<PerformanceViewModel> _logger;
    private readonly IMetricsRegistry _metrics;
    private readonly PerformanceTelemetryService _telemetry;
    private readonly IEventBus _eventBus;
    private readonly IDataService _dataService;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IYouTubeService _youTubeService;
    private readonly IDiscordService _discordService;
    private readonly IDiscordBotManager _discordBotManager;
    private readonly WebServerService _webServerService;
    private readonly WorkflowTriggerService _workflowTriggerService;

    private readonly IReadOnlyDictionary<string, PlatformPerformanceRow> _platformRows;
    private readonly ComponentPerformanceRow _eventBusRow = new("Event Bus");
    private readonly ComponentPerformanceRow _workflowRow = new("Workflow Engine");
    private readonly ComponentPerformanceRow _webServerRow = new("Web Server");

    private DispatcherTimer? _timer;
    private MetricsSnapshot _previousSnapshot = new()
    {
        TimestampUtc = DateTime.UtcNow,
        Counters = new Dictionary<string, IReadOnlyDictionary<string, long>>(),
        Durations = new Dictionary<string, Core.Services.Performance.DurationStats>(),
    };
    private DateTime _previousTickUtc = DateTime.UtcNow;
    private int _tickCount;
    private int _dbPollRunning;

    // ===== Summary tiles =====

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryMb;
    [ObservableProperty] private double _privateMemoryMb;
    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _handleCount;
    [ObservableProperty] private long _gcTotalCollections;
    [ObservableProperty] private double _allocationRateMbPerSec;
    [ObservableProperty] private int _threadPoolThreads;
    [ObservableProperty] private long _threadPoolPending;
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private double _eventsPerSecond;
    [ObservableProperty] private double _workflowsPerSecond;
    [ObservableProperty] private double _requestsPerSecond;
    [ObservableProperty] private string _workflowAvgDuration = "—";
    [ObservableProperty] private long _workflowFailures;
    [ObservableProperty] private long _eventHandlerErrors;
    [ObservableProperty] private long _eventBusPending;
    [ObservableProperty] private bool _isPaused;

    // ===== Sparklines =====

    [ObservableProperty] private System.Windows.Media.PointCollection _cpuHistoryPoints = new();
    [ObservableProperty] private System.Windows.Media.PointCollection _memoryHistoryPoints = new();
    [ObservableProperty] private string _cpuPeakText = "—";
    [ObservableProperty] private string _memoryPeakText = "—";

    // ===== Refresh control =====

    public IReadOnlyList<int> RefreshIntervalOptions { get; } = new[] { 1, 2, 5 };

    [ObservableProperty]
    private int _refreshIntervalSeconds = 1;

    partial void OnRefreshIntervalSecondsChanged(int value)
    {
        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromSeconds(value);
        }
    }

    public ObservableCollection<PlatformPerformanceRow> PlatformRows { get; }
    public ObservableCollection<ComponentPerformanceRow> ComponentRows { get; }
    public ObservableCollection<ChildProcessRow> ProcessRows { get; } = new();

    public PerformanceViewModel(
        ILogger<PerformanceViewModel> logger,
        IMetricsRegistry metrics,
        PerformanceTelemetryService telemetry,
        IEventBus eventBus,
        IDataService dataService,
        ITwitchService twitchService,
        IKickService kickService,
        IYouTubeService youTubeService,
        IDiscordService discordService,
        IDiscordBotManager discordBotManager,
        WebServerService webServerService,
        WorkflowTriggerService workflowTriggerService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _youTubeService = youTubeService ?? throw new ArgumentNullException(nameof(youTubeService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _webServerService = webServerService ?? throw new ArgumentNullException(nameof(webServerService));
        _workflowTriggerService = workflowTriggerService ?? throw new ArgumentNullException(nameof(workflowTriggerService));

        var twitchRow = new PlatformPerformanceRow("Twitch");
        var kickRow = new PlatformPerformanceRow("Kick");
        var youTubeRow = new PlatformPerformanceRow("YouTube");
        var tiktokRow = new PlatformPerformanceRow("TikTok");
        var discordRow = new PlatformPerformanceRow("Discord");

        PlatformRows = new ObservableCollection<PlatformPerformanceRow>
        {
            twitchRow, kickRow, youTubeRow, tiktokRow, discordRow,
        };
        _platformRows = new Dictionary<string, PlatformPerformanceRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["Twitch"] = twitchRow,
            ["Kick"] = kickRow,
            ["YouTube"] = youTubeRow,
            ["YouTubeChatService"] = youTubeRow,
            ["TikTok"] = tiktokRow,
            ["Discord"] = discordRow,
            ["DiscordService"] = discordRow,
        };

        ComponentRows = new ObservableCollection<ComponentPerformanceRow>
        {
            _eventBusRow, _workflowRow, _webServerRow,
        };
    }

    /// <summary>Begins the UI refresh loop. Safe to call repeatedly.</summary>
    public void Start()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds) };
            _timer.Tick += (_, _) => RefreshOnce();
        }

        _timer.Start();
        RefreshOnce();
        _logger.LogDebug("Performance view refresh started ({Seconds}s interval)", RefreshIntervalSeconds);
    }

    /// <summary>Stops the UI refresh loop (tab hidden or paused). Counters keep accumulating.</summary>
    public void Stop()
    {
        _timer?.Stop();
        _logger.LogDebug("Performance view refresh stopped");
    }

    [RelayCommand]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused)
        {
            _timer?.Stop();
        }
        else
        {
            _timer?.Start();
            RefreshOnce();
        }
    }

    /// <summary>
    /// One UI refresh: reads the latest telemetry sample + a registry snapshot,
    /// derives rates from the previous tick, and updates all bindable state.
    /// Runs on the UI thread (DispatcherTimer).
    /// </summary>
    public void RefreshOnce()
    {
        try
        {
            var sample = _telemetry.Latest;
            var snapshot = _metrics.GetSnapshot();
            var now = DateTime.UtcNow;
            var seconds = (now - _previousTickUtc).TotalSeconds;
            if (seconds <= 0)
            {
                seconds = 1;
            }

            // Process tiles
            CpuPercent = sample.CpuPercent;
            MemoryMb = sample.WorkingSetMb;
            PrivateMemoryMb = sample.PrivateMemoryMb;
            ThreadCount = sample.ThreadCount;
            HandleCount = sample.HandleCount;
            GcTotalCollections = (long)sample.Gen0Collections + sample.Gen1Collections + sample.Gen2Collections;
            AllocationRateMbPerSec = sample.AllocatedBytesPerSec / 1024.0 / 1024.0;
            ThreadPoolThreads = sample.ThreadPoolThreads;
            ThreadPoolPending = sample.ThreadPoolPending;
            UptimeText = FormatUptime(now - _telemetry.StartedUtc);

            // Rates
            EventsPerSecond = Rate(snapshot, _previousSnapshot, s => s.GetCounterSum("events"), seconds);
            WorkflowsPerSecond = Rate(snapshot, _previousSnapshot, s => s.GetCounter("Workflows", "runs"), seconds);
            RequestsPerSecond = Rate(snapshot, _previousSnapshot, s => s.GetCounter("WebServer", "requests"), seconds);
            EventHandlerErrors = snapshot.GetCounter("EventBus", "handler_errors");
            EventBusPending = Math.Max(0, snapshot.GetCounter("EventBus", "published") - snapshot.GetCounter("EventBus", "processed"));

            var workflowStats = snapshot.Durations.TryGetValue("Workflows", out var wf)
                ? wf
                : new Core.Services.Performance.DurationStats(0, 0);
            WorkflowFailures = snapshot.GetCounter("Workflows", "failed");
            WorkflowAvgDuration = workflowStats.Count > 0 ? $"{workflowStats.AverageMs:F0} ms" : "—";

            UpdateSparklines();
            UpdatePlatformRows(snapshot, _previousSnapshot, seconds);
            UpdateComponentRows(snapshot, _webServerService, _workflowTriggerService);
            UpdateProcessRows();

            _previousSnapshot = snapshot;
            _previousTickUtc = now;
            _tickCount++;

            // DB-backed connection status is only worth polling occasionally.
            if (_tickCount % 5 == 1)
            {
                _ = PollDbPlatformStatusesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Performance refresh failed");
        }
    }

    private void UpdatePlatformRows(MetricsSnapshot snapshot, MetricsSnapshot previous, double seconds)
    {
        // Aggregate counters per platform from every source that maps to it.
        var events = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var messages = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, metrics) in snapshot.Counters)
        {
            var row = ResolvePlatform(source);
            if (row is null)
            {
                continue;
            }

            if (metrics.TryGetValue("events", out var sourceEvents))
            {
                events[row.Platform] = events.GetValueOrDefault(row.Platform) + sourceEvents;
            }
            if (metrics.TryGetValue("events:ChatMessageReceivedEvent", out var sourceMessages))
            {
                messages[row.Platform] = messages.GetValueOrDefault(row.Platform) + sourceMessages;
            }
        }

        foreach (var row in PlatformRows)
        {
            var total = events.GetValueOrDefault(row.Platform);
            var previousTotal = previous.GetCounter(row.Platform, "events");
            row.EventsTotal = total;
            row.EventsPerSecond = Math.Max(0, total - previousTotal) / seconds;

            var msgs = messages.GetValueOrDefault(row.Platform);
            var previousMsgs = previous.GetCounter(row.Platform, "events:ChatMessageReceivedEvent");
            row.MessagesTotal = msgs;
            row.MessagesPerSecond = Math.Max(0, msgs - previousMsgs) / seconds;

            var sent = snapshot.GetCounter("Sends:" + row.Platform, "sent");
            var failed = snapshot.GetCounter("Sends:" + row.Platform, "failed");
            var previousSent = previous.GetCounter("Sends:" + row.Platform, "sent");
            row.SendsTotal = sent;
            row.SendsFailed = failed;
            row.SendsPerSecond = Math.Max(0, sent - previousSent) / seconds;

            row.EventBreakdown = BuildEventBreakdown(snapshot, row.Platform);

            // Live service state wins over the DB row; TikTok has no live
            // service yet, so it shows the DB status directly.
            var live = row.Platform switch
            {
                "Twitch" => _twitchService.IsConnected,
                "Kick" => _kickService.IsConnected,
                "YouTube" => _youTubeService.IsConnected,
                "Discord" => _discordBotManager.Connected.Count > 0 || _discordService.IsConnected,
                _ => (bool?)null,
            };
            // YouTube gets an expressive status: the interesting reasons
            // (not live yet, chat active, re-auth needed) beat a bare word.
            if (row.Platform == "YouTube")
            {
                if (_youTubeService.RequiresReauth)
                {
                    row.Status = "Re-authorization required";
                }
                else if (_youTubeService.IsConnected)
                {
                    row.Status = _youTubeService.LiveChatState switch
                    {
                        "Listening" => "Connected — chat active",
                        "WaitingForBroadcast" => "Connected — waiting for you to go live",
                        _ => "Connected",
                    };
                }
                else if (string.Equals(row.DbStatus, "Connected", StringComparison.OrdinalIgnoreCase)
                    && _youTubeService.LiveChatState == "Connecting")
                {
                    row.Status = "Connecting…";
                }
                else
                {
                    row.Status = row.DbStatus ?? "Disconnected";
                }
            }
            else
            {
            row.Status = live switch
            {
                true => "Connected",
                false when string.Equals(row.DbStatus, "Connected", StringComparison.OrdinalIgnoreCase) => "Connecting…",
                false => row.DbStatus ?? "Disconnected",
                null => row.DbStatus ?? "Disconnected",
            };
            }

            if (row.Platform == "Discord")
            {
                var bots = _discordBotManager.Connected.Count;
                if (bots > 0)
                {
                    row.EventBreakdown = bots + (bots == 1 ? " bot connected" : " bots connected");
                }
            }
        }
    }

    private PlatformPerformanceRow? ResolvePlatform(string source)
    {
        if (_platformRows.TryGetValue(source, out var exact))
        {
            return exact;
        }

        // Service-named sources ("KickLibChatService", "YouTubeChatService", …)
        foreach (var platform in KnownPlatforms)
        {
            if (source.Contains(platform, StringComparison.OrdinalIgnoreCase))
            {
                return _platformRows[platform];
            }
        }

        return null;
    }

    private string BuildEventBreakdown(MetricsSnapshot snapshot, string platform)
    {
        if (!snapshot.Counters.TryGetValue(platform, out var metrics))
        {
            return "—";
        }

        var breakdown = metrics
            .Where(kvp => kvp.Key.StartsWith("events:", StringComparison.Ordinal))
            .Select(kvp => (Type: kvp.Key["events:".Length..], Count: kvp.Value))
            .OrderByDescending(x => x.Count)
            .Take(3)
            .Select(x => $"{ShortenEventTypeName(x.Type)} ×{x.Count}")
            .ToList();

        return breakdown.Count > 0 ? string.Join(", ", breakdown) : "—";
    }

    private static string ShortenEventTypeName(string typeName) =>
        typeName.Replace("Event", string.Empty, StringComparison.Ordinal);

    private void UpdateComponentRows(
        MetricsSnapshot snapshot,
        WebServerService webServerService,
        WorkflowTriggerService workflowTriggerService)
    {
        _eventBusRow.Status = "Running";
        _eventBusRow.Detail = $"Subscribers: {_eventBus.SubscriptionCount}, pending: {EventBusPending}, handler errors: {EventHandlerErrors}";
        _eventBusRow.Total = snapshot.GetCounter("EventBus", "published");
        _eventBusRow.PerSecond = EventsPerSecond;

        _workflowRow.Status = workflowTriggerService.IsRunning ? "Active" : "Idle";
        _workflowRow.Detail = $"Runs: {snapshot.GetCounter("Workflows", "runs")}, avg: {WorkflowAvgDuration}, failed: {WorkflowFailures}";
        _workflowRow.Total = snapshot.GetCounter("Workflows", "runs");
        _workflowRow.PerSecond = WorkflowsPerSecond;

        _webServerRow.Status = webServerService.IsRunning ? $"Running :{webServerService.Port}" : "Stopped";
        _webServerRow.Detail = webServerService.IsRunning
            ? $"{snapshot.GetCounter("WebServer", "requests")} requests ({snapshot.GetCounter("WebServer", "errors")} errors)"
            : "Webhooks/audio server not started";
        _webServerRow.Total = snapshot.GetCounter("WebServer", "requests");
        _webServerRow.PerSecond = RequestsPerSecond;
    }

    private void UpdateProcessRows()
    {
        var children = _telemetry.ChildProcesses;
        var seen = new HashSet<int>();

        ProcessRows.Clear();
        foreach (var child in children)
        {
            seen.Add(child.ProcessId);
            ProcessRows.Add(new ChildProcessRow(child.Name, child.ProcessId)
            {
                MemoryText = $"{child.WorkingSetMb:F1} MB",
                CpuText = child.TotalCpuSeconds >= 1
                    ? $"{child.TotalCpuSeconds:F0}s"
                    : $"{child.TotalCpuSeconds * 1000:F0}ms",
            });
        }

        if (ProcessRows.Count == 0)
        {
            // Keep one placeholder row so the "no child processes" state is data-driven.
            ProcessRows.Add(new ChildProcessRow("None running", 0));
        }
    }

    private void UpdateSparklines()
    {
        var history = _telemetry.History;
        if (history.Count == 0)
        {
            return;
        }

        CpuHistoryPoints = BuildPoints(history.Select(s => s.CpuPercent), scaleToProcessorCount: true);
        MemoryHistoryPoints = BuildPoints(history.Select(s => s.WorkingSetMb), scaleToProcessorCount: false);
        CpuPeakText = $"{history.Max(s => s.CpuPercent):F1}%";
        MemoryPeakText = $"{history.Max(s => s.WorkingSetMb):F0} MB";
    }

    /// <summary>
    /// Maps a value series into a 0-100 × 0-100 point space (newest at the
    /// right edge). <see cref="System.Windows.Media.Polyline"/> stretches this
    /// to the chart bounds.
    /// </summary>
    private static System.Windows.Media.PointCollection BuildPoints(IEnumerable<double> values, bool scaleToProcessorCount)
    {
        var list = values.ToList();
        var points = new System.Windows.Media.PointCollection(list.Count);

        double max = scaleToProcessorCount ? 100.0 : Math.Max(1, list.Max());
        // CPU can exceed 100 per core when summed; clamp into view.
        for (var i = 0; i < list.Count; i++)
        {
            var x = list.Count == 1 ? 100 : i * 100.0 / (list.Count - 1);
            var y = 100.0 - Math.Clamp(list[i] / max, 0, 1) * 100.0;
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
    }

    private static double Rate(MetricsSnapshot current, MetricsSnapshot previous, Func<MetricsSnapshot, long> read, double seconds)
    {
        var delta = read(current) - read(previous);
        return Math.Max(0, delta) / seconds;
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }
        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
        return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
    }

    /// <summary>
    /// Polls the Platform table for authoritative connection status rows
    /// (every few refreshes, fire-and-forget with a re-entrancy guard).
    /// </summary>
    private async Task PollDbPlatformStatusesAsync()
    {
        if (Interlocked.CompareExchange(ref _dbPollRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            foreach (var platformName in KnownPlatforms)
            {
                var platform = await _dataService.GetPlatformByNameAsync(platformName).ConfigureAwait(true);
                if (platform is null)
                {
                    continue;
                }

                if (_platformRows.TryGetValue(platformName, out var row))
                {
                    row.DbStatus = platform.Status;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to poll platform statuses");
        }
        finally
        {
            Interlocked.Exchange(ref _dbPollRunning, 0);
        }
    }
}
