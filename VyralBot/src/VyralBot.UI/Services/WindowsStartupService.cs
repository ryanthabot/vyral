using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace VyralBot.UI.Services;

/// <summary>
/// Manages the "Launch VyralBot when my PC starts" toggle via the per-user
/// HKCU Run key (<c>Software\Microsoft\Windows\CurrentVersion\Run</c>).
/// The registry is the source of truth for whether Windows launches
/// VyralBot at sign-in; this is the only Windows startup mechanism used.
/// </summary>
public class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VyralBot";

    private readonly ILogger<WindowsStartupService> _logger;

    public WindowsStartupService(ILogger<WindowsStartupService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads the HKCU Run key: true when a "VyralBot" value exists.
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the Windows Run key startup entry");
            return false;
        }
    }

    /// <summary>
    /// Writes (or deletes) the "VyralBot" Run-key value holding the quoted
    /// executable path, so Windows launches VyralBot at sign-in.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.LogWarning("Cannot enable launch-on-PC-start: process path is unavailable");
                    return;
                }

                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                key.SetValue(ValueName, $"\"{exePath}\"");
                _logger.LogInformation("Launch VyralBot when my PC starts enabled: {ExePath}", exePath);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Launch VyralBot when my PC starts disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update the Windows Run key startup entry");
        }
    }
}
