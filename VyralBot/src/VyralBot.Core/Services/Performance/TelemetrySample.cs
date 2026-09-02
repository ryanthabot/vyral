namespace VyralBot.Core.Services.Performance;

/// <summary>
/// Immutable per-second snapshot of process-level resource usage.
/// </summary>
/// <param name="TimestampUtc">When the sample was taken.</param>
/// <param name="CpuPercent">Process CPU usage across all cores (0-100 per core, normalized).</param>
/// <param name="WorkingSetMb">Process working set in megabytes.</param>
/// <param name="PrivateMemoryMb">Private bytes in megabytes.</param>
/// <param name="ThreadCount">CLR + native threads owned by the process.</param>
/// <param name="HandleCount">OS handles owned by the process.</param>
/// <param name="Gen0Collections">Total gen-0 GC collections since start.</param>
/// <param name="Gen1Collections">Total gen-1 GC collections since start.</param>
/// <param name="Gen2Collections">Total gen-2 GC collections since start.</param>
/// <param name="HeapBytes">Total GC heap bytes across all generations.</param>
/// <param name="AllocatedBytesPerSec">Managed allocation rate (bytes/s) since the previous sample.</param>
/// <param name="ThreadPoolThreads">Thread-pool worker threads currently alive.</param>
/// <param name="ThreadPoolPending">Work items queued on the thread pool.</param>
public sealed record TelemetrySample(
    DateTime TimestampUtc,
    double CpuPercent,
    double WorkingSetMb,
    double PrivateMemoryMb,
    int ThreadCount,
    int HandleCount,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long HeapBytes,
    double AllocatedBytesPerSec,
    int ThreadPoolThreads,
    long ThreadPoolPending)
{
    /// <summary>Empty sample used before the first tick.</summary>
    public static TelemetrySample Empty { get; } = new(
        DateTime.UtcNow, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// A child process owned by VyralBot (WebView2 runtime, ngrok, ...).
/// </summary>
/// <param name="Name">Friendly display name.</param>
/// <param name="ProcessId">OS process id.</param>
/// <param name="WorkingSetMb">Working set in megabytes.</param>
/// <param name="TotalCpuSeconds">Total processor time used, in seconds.</param>
public sealed record ChildProcessInfo(
    string Name,
    int ProcessId,
    double WorkingSetMb,
    double TotalCpuSeconds);
