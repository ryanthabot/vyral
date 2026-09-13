using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace VyralBot.UI.Services;

/// <summary>
/// First-launch dependency check. The published Vyral.exe is self-contained
/// (ships the .NET runtime), so the one genuine machine dependency is the
/// Microsoft WebView2 runtime — chat panels, platform logins and browser
/// sources all need it, and a fresh Windows install may not have it. When it
/// is missing, the check offers to fetch and silently run Microsoft's official
/// Evergreen bootstrapper. Everything is logged; failures never block the app.
/// </summary>
public static class RuntimeDependencyCheck
{
    /// <summary>Microsoft's permanent Evergreen bootstrapper link (x64).</summary>
    public const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static bool IsWebView2RuntimeInstalled()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the startup check: detect, ask, download, silently install, verify.
    /// Call from a background task; the confirmation prompt is marshalled to
    /// the UI thread.
    /// </summary>
    public static async Task RunStartupCheckAsync(ILogger logger)
    {
        if (IsWebView2RuntimeInstalled())
        {
            logger.LogInformation("WebView2 runtime present — no dependencies to install");
            return;
        }

        logger.LogWarning("WebView2 runtime is MISSING — offering first-launch installation");

        var install = false;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            install = System.Windows.MessageBox.Show(
                "Vyral needs the Microsoft WebView2 runtime, which is not installed on this PC.\n\n" +
                "It is required for chat panels, platform logins and browser sources.\n\n" +
                "Download and install it now? (~2 MB installer from Microsoft)",
                "Install required dependency",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
        });

        if (!install)
        {
            logger.LogInformation("WebView2 runtime installation declined by user");
            return;
        }

        await InstallWebView2RuntimeAsync(logger);
    }

    /// <summary>Downloads the bootstrapper, runs it silently, verifies the result.</summary>
    public static async Task InstallWebView2RuntimeAsync(ILogger logger)
    {
        var installerPath = Path.Combine(
            Path.GetTempPath(), $"MicrosoftEdgeWebView2RuntimeInstallerX64-{Guid.NewGuid():N}.exe");

        try
        {
            logger.LogInformation("Downloading WebView2 bootstrapper from {Url}", WebView2BootstrapperUrl);
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("VyralBot");
                await using var source = await http.GetStreamAsync(WebView2BootstrapperUrl);
                await using var target = File.Create(installerPath);
                await source.CopyToAsync(target);
            }

            logger.LogInformation("Running WebView2 bootstrapper silently ({Path})", installerPath);
            using (var process = Process.Start(new ProcessStartInfo(installerPath, "/silent /install")
            {
                UseShellExecute = true, // lets the installer self-elevate via UAC
            }))
            {
                if (process is null)
                {
                    logger.LogError("WebView2 bootstrapper failed to launch");
                    NotifyFailure(logger, "The installer could not be started.");
                    return;
                }

                // Elevation and installation can legitimately take minutes.
                await process.WaitForExitAsync();
                logger.LogInformation(
                    "WebView2 bootstrapper exited with code {ExitCode}", process.ExitCode);
            }

            if (IsWebView2RuntimeInstalled())
            {
                logger.LogInformation("WebView2 runtime installed successfully");
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                dispatcher?.Invoke(() => System.Windows.MessageBox.Show(
                    "The WebView2 runtime was installed successfully. Vyral is ready.",
                    "Dependency installed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information));
            }
            else
            {
                logger.LogError("WebView2 runtime still missing after the installer ran");
                NotifyFailure(logger,
                    "The installer finished but the runtime is still not detected. " +
                    "You can install it manually from https://developer.microsoft.com/microsoft-edge/webview2/ and restart Vyral.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WebView2 runtime installation failed");
            NotifyFailure(logger, $"The installation failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
        }
    }

    private static void NotifyFailure(ILogger logger, string detail)
    {
        logger.LogWarning("WebView2 runtime not available: {Detail}", detail);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.Invoke(() => System.Windows.MessageBox.Show(
            detail + "\n\nVyral will keep running, but chat panels and browser sources will not work until it is installed.",
            "Dependency not installed",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning));
    }
}
