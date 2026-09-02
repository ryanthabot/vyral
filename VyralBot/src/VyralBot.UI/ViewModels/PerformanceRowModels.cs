using CommunityToolkit.Mvvm.ComponentModel;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One platform row in the Settings → Performance tab: activity counters for
/// Twitch / Kick / YouTube / TikTok / Discord, refreshed live.
/// </summary>
public partial class PlatformPerformanceRow : ObservableObject
{
    public PlatformPerformanceRow(string platform)
    {
        Platform = platform;
    }

    public string Platform { get; }

    [ObservableProperty]
    private string _status = "Disconnected";

    /// <summary>Breakdown of the most frequent event types, e.g. "ChatMessage ×12, Follow ×3".</summary>
    [ObservableProperty]
    private string _eventBreakdown = "—";

    [ObservableProperty]
    private long _eventsTotal;

    [ObservableProperty]
    private double _eventsPerSecond;

    [ObservableProperty]
    private long _messagesTotal;

    [ObservableProperty]
    private double _messagesPerSecond;

    [ObservableProperty]
    private long _sendsTotal;

    [ObservableProperty]
    private double _sendsPerSecond;

    [ObservableProperty]
    private long _sendsFailed;

    /// <summary>Status string from the Platform DB row (polled every few seconds).</summary>
    public string? DbStatus { get; set; }
}

/// <summary>
/// One app component row (EventBus, workflow engine, web server, ...).
/// </summary>
public partial class ComponentPerformanceRow : ObservableObject
{
    public ComponentPerformanceRow(string component)
    {
        Component = component;
    }

    public string Component { get; }

    [ObservableProperty]
    private string _detail = "—";

    [ObservableProperty]
    private string _status = "—";

    [ObservableProperty]
    private long _total;

    [ObservableProperty]
    private double _perSecond;
}

/// <summary>
/// One VyralBot-owned child process (WebView2 runtime, ngrok).
/// </summary>
public partial class ChildProcessRow : ObservableObject
{
    public ChildProcessRow(string name, int processId)
    {
        Name = name;
        ProcessId = processId;
    }

    public string Name { get; }
    public int ProcessId { get; }

    [ObservableProperty]
    private string _memoryText = "—";

    [ObservableProperty]
    private string _cpuText = "—";
}
