using System.Diagnostics;

namespace VyralBot.Core.Services.ProcessManagement;

/// <summary>
/// Manages and tracks all external processes spawned by VyralBot.
/// Ensures proper cleanup and zero orphaned processes.
/// </summary>
public interface IProcessManager : IAsyncDisposable
{
    /// <summary>
    /// Starts and tracks a new process.
    /// </summary>
    Task<Process> StartProcessAsync(ProcessStartInfo startInfo, CancellationToken ct = default);
    
    /// <summary>
    /// Gets all currently active processes.
    /// </summary>
    IReadOnlyList<ProcessInfo> GetActiveProcesses();
    
    /// <summary>
    /// Kills a specific process by ID.
    /// </summary>
    Task KillProcessAsync(int processId, CancellationToken ct = default);
    
    /// <summary>
    /// Kills all tracked processes (called on shutdown).
    /// </summary>
    Task KillAllProcessesAsync(CancellationToken ct = default);
}

/// <summary>
/// Information about a tracked process.
/// </summary>
public sealed record ProcessInfo(
    int ProcessId, 
    string ProcessName, 
    DateTime StartTime, 
    bool IsRunning);
