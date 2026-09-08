using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Embedded-OBS Task 3: ObsEngine state machine WITHOUT booting native libobs.
/// Every test points the locator at an empty temp root, so Locate() returns
/// null before any obs.dll P/Invoke happens. The success path needs a real
/// obs.dll boot and is exercised by the `--obs-selftest` CLI (verification wave).
/// </summary>
public class ObsEngineTests
{
    private static string CreateEmptyTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vyral-obs-engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteBestEffort(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task InitializeAsync_WhenObsNotInstalled_FailsWithNotFoundMessage()
    {
        var root = CreateEmptyTempRoot();
        try
        {
            using var engine = new ObsEngine(new ObsInstallLocator(root), NullLogger<ObsEngine>.Instance);

            // Raises arrive on the threadpool — collect them and wait (bounded)
            // for the full Initializing → Failed sequence instead of racing.
            var raises = new List<ObsEngineStatus>();
            engine.StatusChanged += status => { lock (raises) raises.Add(status); };

            var status = await engine.InitializeAsync();

            Assert.Equal(ObsEngineState.Failed, status.State);
            Assert.Contains("not found", status.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Null(status.Version);
            Assert.False(engine.IsReady);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (raises)
                {
                    if (raises.Count >= 2 && raises[^1].State == ObsEngineState.Failed)
                    {
                        break;
                    }
                }

                await Task.Delay(25);
            }

            lock (raises)
            {
                Assert.True(raises.Count >= 2, $"expected 2 raises, saw {raises.Count}");
                Assert.Equal(ObsEngineState.Initializing, raises[0].State);
                Assert.Equal(ObsEngineState.Failed, raises[^1].State);
            }
        }
        finally { DeleteBestEffort(root); }
    }

    [Fact]
    public async Task InitializeAsync_AfterFailedInit_ReAttemptsAndFailsCleanly()
    {
        var root = CreateEmptyTempRoot();
        try
        {
            using var engine = new ObsEngine(new ObsInstallLocator(root), NullLogger<ObsEngine>.Instance);

            var first = await engine.InitializeAsync();
            var second = await engine.InitializeAsync();

            Assert.Equal(ObsEngineState.Failed, first.State);
            Assert.Equal(ObsEngineState.Failed, second.State);
            Assert.Equal(first.Error, second.Error);
            Assert.False(engine.IsReady);
        }
        finally { DeleteBestEffort(root); }
    }

    [Fact]
    public async Task ShutdownAsync_BeforeInitialize_NoThrowAndStateShutdown()
    {
        var root = CreateEmptyTempRoot();
        try
        {
            using var engine = new ObsEngine(new ObsInstallLocator(root), NullLogger<ObsEngine>.Instance);

            Assert.Equal(ObsEngineState.NotInitialized, engine.GetStatus().State);

            await engine.ShutdownAsync();

            Assert.Equal(ObsEngineState.Shutdown, engine.GetStatus().State);
            Assert.False(engine.IsReady);

            // Post-shutdown init must be a no-op snapshot, never a native re-boot
            // (libobs does not support obs_startup after obs_shutdown).
            var status = await engine.InitializeAsync();
            Assert.Equal(ObsEngineState.Shutdown, status.State);
        }
        finally { DeleteBestEffort(root); }
    }
}
