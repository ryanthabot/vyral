using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace VyralBot.UI.Services;

/// <summary>
/// Singleton manager for CoreWebView2Environment.
/// Reuses a single environment across all WebView2 instances to:
/// - Reduce browser process spawning (saves 50-100MB per instance)
/// - Share user data folder for faster startup
/// - Disable unnecessary browser features for performance
/// 
/// Per updated_performance_upgrade.md: TIER 2 critical optimization.
/// </summary>
public sealed class WebView2EnvironmentManager : IDisposable
{
    private static readonly Lazy<WebView2EnvironmentManager> _instance = 
        new Lazy<WebView2EnvironmentManager>(() => new WebView2EnvironmentManager());
    
    private CoreWebView2Environment? _environment;
    private readonly object _lock = new();
    private bool _isInitializing;
    private TaskCompletionSource<CoreWebView2Environment>? _initTcs;
    private ILogger? _logger;
    
    /// <summary>
    /// Singleton instance
    /// </summary>
    public static WebView2EnvironmentManager Instance => _instance.Value;
    
    /// <summary>
    /// User data folder path for WebView2
    /// </summary>
    public static string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VyralBot", "WebView2Data"
    );
    
    private WebView2EnvironmentManager() { }
    
    /// <summary>
    /// Set logger for the manager (call once during app startup)
    /// </summary>
    public void SetLogger(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Get or create the shared CoreWebView2Environment.
    /// Thread-safe and returns cached instance on subsequent calls.
    /// </summary>
    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        // Fast path: already initialized
        if (_environment != null)
            return _environment;
        
        lock (_lock)
        {
            // Double-check after acquiring lock
            if (_environment != null)
                return _environment;
            
            // Wait for existing initialization
            if (_isInitializing && _initTcs != null)
            {
                return _initTcs.Task.GetAwaiter().GetResult();
            }
            
            // Start initialization
            _isInitializing = true;
            _initTcs = new TaskCompletionSource<CoreWebView2Environment>();
        }
        
        try
        {
            _logger?.LogInformation("Creating shared CoreWebView2Environment...");
            
            // Ensure user data folder exists
            Directory.CreateDirectory(UserDataFolder);
            
            // Configure environment options for performance
            // Per updated_performance_upgrade.md: disable unnecessary features
            var options = new CoreWebView2EnvironmentOptions(
                // Disable background networking to reduce CPU/network
                "--disable-background-networking " +
                // No browser extensions needed
                "--disable-extensions " +
                // Disable component updates (we manage updates)
                "--disable-component-update " +
                // Disable crash reporting
                "--disable-breakpad " +
                // Disable sync features
                "--disable-sync " +
                // Disable translation
                "--disable-translate " +
                // Minimal metrics
                "--metrics-recording-only " +
                // Skip first-run experience
                "--no-first-run " +
                // Disable safe browsing updates (security handled at app level)
                "--safebrowsing-disable-auto-update " +
                // Reduce GPU process overhead if not needed
                "--disable-gpu-compositing"
            );
            
            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,  // Use installed Edge
                userDataFolder: UserDataFolder,
                options: options
            );
            
            _logger?.LogInformation(
                "✅ Shared CoreWebView2Environment created (UserData: {UserDataFolder})", 
                UserDataFolder
            );
            
            _initTcs?.TrySetResult(_environment);
            return _environment;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create CoreWebView2Environment");
            _initTcs?.TrySetException(ex);
            throw;
        }
        finally
        {
            lock (_lock)
            {
                _isInitializing = false;
            }
        }
    }
    
    /// <summary>
    /// Pre-warm the environment on a background thread during app startup.
    /// Call this early to avoid delay when first WebView2 is needed.
    /// </summary>
    public void PreWarmAsync()
    {
        Task.Run(async () =>
        {
            try
            {
                await GetEnvironmentAsync();
                _logger?.LogInformation("WebView2Environment pre-warmed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to pre-warm WebView2Environment (will retry on first use)");
            }
        });
    }
    
    /// <summary>
    /// Check if the environment is ready (initialized)
    /// </summary>
    public bool IsReady => _environment != null;
    
    /// <summary>
    /// Dispose the shared environment (call only on app shutdown)
    /// </summary>
    public void Dispose()
    {
        // CoreWebView2Environment doesn't implement IDisposable,
        // but we clear our reference to allow GC
        _environment = null;
        _logger?.LogInformation("WebView2EnvironmentManager disposed");
    }
}
