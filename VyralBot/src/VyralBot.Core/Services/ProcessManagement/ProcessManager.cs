using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.ProcessManagement;

/// <summary>
/// Tracks and manages all external processes to prevent orphans.
/// Implements zero-child-process policy from PROCESS_MANAGEMENT.md.
/// </summary>
public sealed class ProcessManager : IProcessManager
{
    private readonly ILogger<ProcessManager> _logger;
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
    private bool _disposed;

    public ProcessManager(ILogger<ProcessManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts a new process and registers it for tracking.
    /// </summary>
    public Task<Process> StartProcessAsync(ProcessStartInfo startInfo, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProcessManager));
        
        var process = new Process { StartInfo = startInfo };
        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;
        
        if (process.Start())
        {
            _activeProcesses.TryAdd(process.Id, process);
            // Arguments are deliberately NOT logged: they can embed secrets
            // (the restream command line carries every platform stream key).
            _logger.LogInformation("Process started: {Name} (PID: {Pid})",
                process.ProcessName, process.Id);
            return Task.FromResult(process);
        }
        
        throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
    }

    /// <summary>
    /// Gets snapshot of all active processes.
    /// </summary>
    public IReadOnlyList<ProcessInfo> GetActiveProcesses()
    {
        return _activeProcesses.Values
            .Where(p => !p.HasExited)
            .Select(p => new ProcessInfo(p.Id, p.ProcessName, p.StartTime, !p.HasExited))
            .ToList();
    }

    /// <summary>
    /// Kills a specific process.
    /// </summary>
    public Task KillProcessAsync(int processId, CancellationToken ct = default)
    {
        if (_activeProcesses.TryRemove(processId, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    _logger.LogInformation("Process killed: {Name} (PID: {Pid})", 
                        process.ProcessName, processId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill process {Pid}", processId);
            }
            finally
            {
                process.Dispose();
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kills all tracked processes (shutdown cleanup).
    /// </summary>
    public async Task KillAllProcessesAsync(CancellationToken ct = default)
    {
        var processCount = _activeProcesses.Count;
        if (processCount == 0)
        {
            _logger.LogInformation("No active processes to kill");
            return;
        }
        
        _logger.LogInformation("Killing all {Count} active processes", processCount);
        
        var killTasks = _activeProcesses.Keys.Select(pid => KillProcessAsync(pid, ct));
        await Task.WhenAll(killTasks);
        
        _logger.LogInformation("All processes terminated");
    }

    /// <summary>
    /// Event handler for process exit.
    /// </summary>
    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            _activeProcesses.TryRemove(process.Id, out _);
            _logger.LogInformation("Process exited: {Name} (PID: {Pid}, ExitCode: {ExitCode})", 
                process.ProcessName, process.Id, process.ExitCode);
            process.Dispose();
        }
    }

    /// <summary>
    /// Cleanup all processes on disposal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _logger.LogInformation("ProcessManager disposing - cleaning up processes");
        await KillAllProcessesAsync();
        _disposed = true;
    }
}
