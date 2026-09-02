using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.Performance;

/// <summary>
/// Samples process-level resource usage once per second into a bounded
/// history ring, and (every <see cref="ChildScanIntervalTicks"/> ticks)
/// enumerates VyralBot-owned child processes (WebView2 runtime scoped to the
/// VyralBot temp user-data folder, ngrok). The Settings → Performance tab
/// reads <see cref="Latest"/>/<see cref="History"/>/<see cref="ChildProcesses"/>
/// on the UI thread; sampling keeps running for the app lifetime so counters
/// are already populated when the tab opens.
/// </summary>
public sealed class PerformanceTelemetryService : BackgroundService
{
    private const int HistoryCapacity = 180;           // 3 minutes at 1s
    private const int ChildScanIntervalTicks = 5;      // child process scan every 5s

    private readonly ILogger<PerformanceTelemetryService> _logger;
    private readonly Process _process;
    private readonly object _sync = new();
    private readonly TelemetrySample[] _history = new TelemetrySample[HistoryCapacity];

    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private long _lastAllocatedBytes;
    private TelemetrySample _latest = TelemetrySample.Empty;
    private int _historyCount;
    private int _tickCount;
    private IReadOnlyList<ChildProcessInfo> _childProcesses = Array.Empty<ChildProcessInfo>();

    /// <summary>When the service (≈ the app) started; used for uptime display.</summary>
    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    /// <summary>Most recent sample.</summary>
    public TelemetrySample Latest
    {
        get { lock (_sync) return _latest; }
    }

    /// <summary>Copy of the history ring, oldest first.</summary>
    public IReadOnlyList<TelemetrySample> History
    {
        get
        {
            lock (_sync)
            {
                var copy = new TelemetrySample[_historyCount];
                // The ring is filled in order and wraps; oldest-first when wrapped.
                for (var i = 0; i < _historyCount; i++)
                {
                    copy[i] = _history[i];
                }
                return copy;
            }
        }
    }

    /// <summary>Child processes from the most recent scan (refreshed every 5s).</summary>
    public IReadOnlyList<ChildProcessInfo> ChildProcesses
    {
        get { lock (_sync) return _childProcesses; }
    }

    public PerformanceTelemetryService(ILogger<PerformanceTelemetryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _process = Process.GetCurrentProcess();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PerformanceTelemetryService started (1s sampling, child scan every {ScanSeconds}s)",
            ChildScanIntervalTicks);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SampleOnce();
                if (_tickCount % ChildScanIntervalTicks == 0)
                {
                    await ScanChildProcessesAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Performance telemetry sample failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PerformanceTelemetryService stopped");
    }

    /// <summary>
    /// Takes one process sample and appends it to the history ring. Public so
    /// tests (and the UI) can drive sampling deterministically.
    /// </summary>
    public TelemetrySample SampleOnce()
    {
        _process.Refresh();

        var now = DateTime.UtcNow;
        var cpuTime = _process.TotalProcessorTime;
        var elapsed = now - _lastSampleUtc;

        double cpuPercent = 0;
        double allocatedPerSec = 0;
        if (elapsed.TotalMilliseconds > 0)
        {
            cpuPercent = (cpuTime - _lastCpuTime).TotalMilliseconds
                         / elapsed.TotalMilliseconds
                         / Environment.ProcessorCount
                         * 100.0;

            var allocated = GC.GetTotalAllocatedBytes(precise: false);
            allocatedPerSec = Math.Max(0, (allocated - _lastAllocatedBytes) / elapsed.TotalSeconds);
            _lastAllocatedBytes = allocated;
        }

        var sample = new TelemetrySample(
            now,
            Math.Clamp(cpuPercent, 0, 100 * Environment.ProcessorCount),
            _process.WorkingSet64 / 1024.0 / 1024.0,
            _process.PrivateMemorySize64 / 1024.0 / 1024.0,
            _process.Threads.Count,
            _process.HandleCount,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetGCMemoryInfo().HeapSizeBytes,
            allocatedPerSec,
            ThreadPool.ThreadCount,
            ThreadPool.PendingWorkItemCount);

        _lastCpuTime = cpuTime;
        _lastSampleUtc = now;
        _tickCount++;

        lock (_sync)
        {
            if (_historyCount < HistoryCapacity)
            {
                _history[_historyCount++] = sample;
            }
            else
            {
                // Shift left by one — 180 reference copies per second is negligible
                // and keeps History a plain oldest-first array.
                Array.Copy(_history, 1, _history, 0, HistoryCapacity - 1);
                _history[HistoryCapacity - 1] = sample;
            }
            _latest = sample;
        }

        return sample;
    }

    /// <summary>
    /// Enumerates VyralBot-owned child processes. WebView2 processes are
    /// attributed via their command line (same KI-23 scoping as App.xaml.cs
    /// cleanup); ngrok is matched by name. Runs on the thread pool every 5s.
    /// </summary>
    private async Task ScanChildProcessesAsync(CancellationToken stoppingToken)
    {
        try
        {
            var found = await Task.Run(() =>
            {
                var processes = new List<ChildProcessInfo>();

                foreach (var webview in FindVyralWebView2Processes())
                {
                    processes.Add(webview);
                }

                foreach (var ngrok in Process.GetProcessesByName("ngrok"))
                {
                    using (ngrok)
                    {
                        double cpuSeconds = 0;
                        try { cpuSeconds = ngrok.TotalProcessorTime.TotalSeconds; } catch { }

                        double workingSetMb = 0;
                        try { workingSetMb = ngrok.WorkingSet64 / 1024.0 / 1024.0; } catch { }

                        processes.Add(new ChildProcessInfo("ngrok", ngrok.Id, workingSetMb, cpuSeconds));
                    }
                }

                return (IReadOnlyList<ChildProcessInfo>)processes;
            }, stoppingToken).ConfigureAwait(false);

            lock (_sync)
            {
                _childProcesses = found;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Child process scan failed");
        }
    }

    /// <summary>
    /// WebView2 processes belonging to VyralBot: roots are msedgewebview2
    /// processes whose parent is this PID (browser/crashpad), and the rest of
    /// the tree (renderers, GPU) hangs below them. Attribution walks parent
    /// PIDs via NtQueryInformationProcess — fast, no WMI; a parent that
    /// already exited just makes that link unverifiable and the process is
    /// skipped.
    /// </summary>
    private List<ChildProcessInfo> FindVyralWebView2Processes()
    {
        var results = new List<ChildProcessInfo>();

        try
        {
            var myPid = Environment.ProcessId;
            var all = Process.GetProcessesByName("msedgewebview2");
            var parentIds = new Dictionary<int, int?>(all.Length); // pid → parent pid (null = unknown)
            var rootIds = new HashSet<int>();

            foreach (var proc in all)
            {
                parentIds[proc.Id] = GetParentProcessId(proc);
            }

            foreach (var (pid, parentId) in parentIds)
            {
                if (parentId == myPid)
                {
                    rootIds.Add(pid);
                }
            }

            if (rootIds.Count > 0)
            {
                foreach (var (pid, parentId) in parentIds)
                {
                    if (rootIds.Contains(pid))
                    {
                        continue;
                    }

                    // Walk up a bounded chain (renderers → browser root).
                    var cursor = parentId;
                    for (var depth = 0; depth < 4 && cursor is int current && !rootIds.Contains(current); depth++)
                    {
                        cursor = parentIds.TryGetValue(current, out var next) ? next : null;
                    }

                    if (cursor is int match && rootIds.Contains(match))
                    {
                        rootIds.Add(pid); // descendant of a Vyral root
                    }
                }
            }

            foreach (var proc in all)
            {
                using (proc)
                {
                    if (!rootIds.Contains(proc.Id))
                    {
                        continue;
                    }

                    double cpuSeconds = 0;
                    try { cpuSeconds = proc.TotalProcessorTime.TotalSeconds; } catch { }

                    double workingSetMb = 0;
                    try { workingSetMb = proc.WorkingSet64 / 1024.0 / 1024.0; } catch { }

                    results.Add(new ChildProcessInfo("WebView2", proc.Id, workingSetMb, cpuSeconds));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WebView2 process scan failed");
        }

        return results;
    }

    /// <summary>Parent PID via NtQueryInformationProcess, or null when unavailable.</summary>
    private static int? GetParentProcessId(Process process)
    {
        try
        {
            var pbi = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(process.Handle, 0, ref pbi,
                Marshal.SizeOf(pbi), out _);
            if (status == 0)
            {
                return (int)pbi.InheritedFromUniqueProcessId;
            }
        }
        catch { /* exited or access denied */ }

        return null;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    public override void Dispose()
    {
        _process.Dispose();
        base.Dispose();
    }
}
