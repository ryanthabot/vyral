using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Embedded-OBS Task 5: module blocklist matching and the native-work gate
/// guard, both WITHOUT booting native libobs (the real module load is exercised
/// by the `--obs-selftest` CLI verification wave).
/// </summary>
public class ObsEngineModuleTests
{
    [Theory]
    [InlineData("frontend-tools", true)]
    [InlineData("OBS-Browser", true)] // stem match is case-insensitive
    [InlineData("win-dshow", false)]
    [InlineData("aja-output", false)] // exact stem only — no substring matches
    public void IsBlocklisted_MatchesStemCaseInsensitively(string stem, bool expected)
    {
        Assert.Equal(expected, ObsEngine.IsBlocklisted(stem, ObsEngine.DefaultBlocklistedModules));
    }

    [Fact]
    public void DefaultBlocklist_ContainsModulesKnownToCrashNonObsHosts()
    {
        // Every entry here AV'd (or is module-class hostile) in a non-Qt,
        // non-OBS host per the 2026-09-07 embedded-OBS selftest run.
        string[] knownHostile =
        [
            "frontend-tools",
            "scene-notes-dock",
            "decklink-output-ui",
            "decklink-captions",
            "aja-output-ui",
            "aja",
            "obs-websocket",
            "obs-browser",
            "obs-vst",
            "aitum-stream-suite",
            "obs-scripting",
        ];

        foreach (string stem in knownHostile)
        {
            Assert.Contains(stem, ObsEngine.DefaultBlocklistedModules);
        }
    }

    [Fact]
    public async Task RunNativeAsync_WhenEngineNotReady_FaultsWithInvalidOperationException()
    {
        // Engine pointed at an empty root: IsReady is false before any boot.
        var root = Path.Combine(Path.GetTempPath(), $"vyral-obs-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var engine = new ObsEngine(new ObsInstallLocator(root), NullLogger<ObsEngine>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => engine.RunNativeAsync(() => 42));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => engine.RunNativeAsync(() => { }));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
