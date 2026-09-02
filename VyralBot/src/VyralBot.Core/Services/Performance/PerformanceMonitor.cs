using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VyralBot.Core.Services.Performance;

/// <summary>
/// Monitors application performance metrics every 30 seconds.
/// Tracks memory usage, GC collections, and thread count.
/// </summary>
public sealed class PerformanceMonitor : BackgroundService
{
    private readonly ILogger<PerformanceMonitor> _logger;
    private readonly Process _currentProcess;
    private int _lastGen0Collections;
    private int _lastGen1Collections;
    private int _lastGen2Collections;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCheckTime;

    public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = TimeSpan.Zero;
        _lastCheckTime = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PerformanceMonitor started - will report every 15 seconds");
        
        // Wait 5 seconds before first report (let app stabilize)
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                LogPerformanceMetrics();
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Performance monitoring error");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
        
        _logger.LogInformation("PerformanceMonitor stopped");
    }

    private void LogPerformanceMetrics()
    {
        try
        {
            _currentProcess.Refresh();
            
            // Memory metrics
            var memoryMB = _currentProcess.WorkingSet64 / 1024 / 1024;
            var privateMemoryMB = _currentProcess.PrivateMemorySize64 / 1024 / 1024;
            
            // GC metrics
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var gen0Delta = gen0 - _lastGen0Collections;
            var gen1Delta = gen1 - _lastGen1Collections;
            var gen2Delta = gen2 - _lastGen2Collections;
            
            _lastGen0Collections = gen0;
            _lastGen1Collections = gen1;
            _lastGen2Collections = gen2;
            
            // Thread metrics
            var threadCount = _currentProcess.Threads.Count;
            
            // CPU percentage (based on time elapsed since last check)
            var currentTime = DateTime.UtcNow;
            var currentCpuTime = _currentProcess.TotalProcessorTime;
            var cpuUsed = currentCpuTime - _lastCpuTime;
            var timeElapsed = currentTime - _lastCheckTime;
            var cpuPercent = timeElapsed.TotalMilliseconds > 0 
                ? (cpuUsed.TotalMilliseconds / timeElapsed.TotalMilliseconds / Environment.ProcessorCount) * 100.0
                : 0.0;
            
            _lastCpuTime = currentCpuTime;
            _lastCheckTime = currentTime;
            
            // Count WebView2 child processes
            int webView2ProcessCount = 0;
            try
            {
                var webView2Processes = Process.GetProcessesByName("msedgewebview2");
                webView2ProcessCount = webView2Processes.Length;
                foreach (var p in webView2Processes)
                {
                    p.Dispose();
                }
            }
            catch { /* Ignore errors counting WebView2 processes */ }
            
            _logger.LogInformation(
                "Performance: Memory={Memory}MB (Private={Private}MB), " +
                "GC/15s=Gen0:{Gen0}/Gen1:{Gen1}/Gen2:{Gen2}, " +
                "Threads={Threads}, CPU={CpuPercent:F1}%, WebView2={WebView2Count}",
                memoryMB, privateMemoryMB,
                gen0Delta, gen1Delta, gen2Delta,
                threadCount, cpuPercent, webView2ProcessCount);
            
            // Warnings (adjusted for WPF + WebView2 stack)
            // Per updated_performance_upgrade.md: Target 40-60MB idle, 60-90MB with WebView2
            if (memoryMB > 150 && webView2ProcessCount == 0)
            {
                _logger.LogWarning("High memory usage detected: {Memory}MB without WebView2 (target: <150MB)", memoryMB);
            }
            else if (memoryMB > 300)
            {
                _logger.LogWarning("High memory usage detected: {Memory}MB (target: <300MB with WebView2)", memoryMB);
            }
            
            if (gen0Delta > 15)
            {
                _logger.LogWarning("High GC Gen0 pressure: {Count} collections in 15s", gen0Delta);
            }
            
            if (threadCount > 50)
            {
                _logger.LogWarning("High thread count: {Count} threads (target: 25-40)", threadCount);
            }
            
            if (cpuPercent > 10.0)
            {
                _logger.LogWarning("High CPU usage: {CpuPercent:F1}% (target: <5%)", cpuPercent);
            }
            
            if (webView2ProcessCount > 6)
            {
                _logger.LogWarning("High WebView2 process count: {Count} (expected: 1-5)", webView2ProcessCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect performance metrics");
        }
    }

    public override void Dispose()
    {
        _currentProcess?.Dispose();
        base.Dispose();
    }
}
