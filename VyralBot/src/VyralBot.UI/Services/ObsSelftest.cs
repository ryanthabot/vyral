using System.IO;
using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Obs;
using VyralBot.Integrations.Obs.Engine;

namespace VyralBot.UI.Services;

/// <summary>
/// `--obs-selftest` CLI mode: headless boot of the embedded OBS engine —
/// locate install, initialize libobs (startup → modules → reset video/audio),
/// parse the largest scene collection managed-side (the native loader is
/// Task 4; this validates engine boot only), then obs_shutdown.
///
/// The app is a WinExe, so Console output has nowhere to go when launched
/// without a terminal — the durable output is the report file, by default
/// `obs-selftest-report.txt` next to the exe, overridable with
/// `--obs-selftest-report=&lt;path&gt;` (Task 9 reads that file).
/// Exit codes: 0 = ok, 1 = OBS not found, 2 = selftest failure.
/// </summary>
internal static class ObsSelftest
{
    public const string Flag = "--obs-selftest";

    private const string ReportArgPrefix = "--obs-selftest-report=";

    public static int Run(IReadOnlyList<string> args)
    {
        var reportPath = ResolveReportPath(args);

        // Crash-safe: the report is rewritten line-by-line as the selftest runs,
        // so a native access violation (which bypasses every finally) still
        // leaves behind the exact step it died on.
        using var writer = new StreamWriter(reportPath, append: false);

        void Log(string line)
        {
            Console.WriteLine(line);
            writer.WriteLine(line);
            writer.Flush();
        }

        int exitCode;
        try
        {
            exitCode = RunCore(Log);
        }
        catch (Exception ex)
        {
            Log($"SELFTEST FAILED: {ex.GetType().Name}: {ex.Message}");
            exitCode = 2;
        }

        Console.WriteLine($"Report written to {reportPath}");
        return exitCode;
    }

    private static int RunCore(Action<string> log)
    {
        // 1. Locate the install (no obs.dll touched yet).
        var locator = new ObsInstallLocator();
        ObsInstall? install = locator.Locate();
        if (install is null)
        {
            log("OBS NOT FOUND");
            return 1;
        }

        log($"OBS install root: {install.Root}");
        log($"obs.dll: {install.ObsDllPath}");

        // 2. Engine boot. The forwarding logger mirrors every engine step
        // (startup, module load, reset_video/audio results) into the report.
        using var engine = new ObsEngine(locator, new ReportLogger(log));
        ObsEngineStatus status = engine.InitializeAsync().GetAwaiter().GetResult();
        log($"Engine state: {status.State}");
        log($"OBS version: {status.Version ?? "(unavailable)"}");
        log($"Engine install root: {status.InstallRoot ?? "(unavailable)"}");
        if (status.State != ObsEngineState.Ready)
        {
            log($"SELFTEST FAILED: engine did not become Ready: {status.Error}");
            return 2;
        }

        // 3. Largest scene collection, parsed managed-side only — never loaded
        // natively here.
        DescribeLargestSceneCollection(install, log);

        // 4. Native teardown.
        engine.ShutdownAsync().GetAwaiter().GetResult();
        log($"Engine state after shutdown: {engine.GetStatus().State}");
        log("SELFTEST OK");
        return 0;
    }

    private static void DescribeLargestSceneCollection(ObsInstall install, Action<string> log)
    {
        if (!Directory.Exists(install.SceneCollectionsDir))
        {
            log($"Scene collections directory not found: {install.SceneCollectionsDir}");
            return;
        }

        var largest = new DirectoryInfo(install.SceneCollectionsDir)
            .EnumerateFiles("*.json")
            .OrderByDescending(static f => f.Length)
            .FirstOrDefault();
        if (largest is null)
        {
            log($"No scene collections (*.json) found in {install.SceneCollectionsDir}");
            return;
        }

        log($"Largest scene collection: {largest.FullName} ({largest.Length:N0} bytes)");
        ObsSceneCollectionDocument document = ObsSceneCollectionDocument.Parse(File.ReadAllText(largest.FullName));
        log($"  scenes: {document.Scenes.Count}");
        log($"  non-scene sources: {document.NonSceneSources.Count}");
        log($"  total source entries: {document.SourceCount}");
        log($"  audio devices: {document.AudioDevices.Count}");
        if (document.CurrentScene is { } currentScene)
        {
            log($"  current scene: {currentScene}");
        }
    }

    private static string ResolveReportPath(IReadOnlyList<string> args)
    {
        var reportArg = args.FirstOrDefault(a => a.StartsWith(ReportArgPrefix, StringComparison.OrdinalIgnoreCase));
        if (reportArg is not null)
        {
            var value = reportArg[ReportArgPrefix.Length..].Trim().Trim('"');
            if (value.Length > 0)
            {
                return value;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "obs-selftest-report.txt");
    }

    /// <summary>Forwards engine step logs into the report so reset_video/audio results are visible.</summary>
    private sealed class ReportLogger(Action<string> log) : ILogger<ObsEngine>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = formatter(state, exception);
            if (exception is not null)
            {
                line += $" :: {exception.GetType().Name}: {exception.Message}";
            }

            log($"  [engine] {line}");
        }
    }
}
