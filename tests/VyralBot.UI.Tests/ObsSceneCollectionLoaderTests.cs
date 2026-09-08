using System.IO;
using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Embedded-OBS scene collection loader without a native libobs boot: the
/// fake gate answers IsReady=false (or records gate traffic) so every path
/// here stays pure — no obs.dll P/Invoke can run. The real load path is
/// exercised by the live verification wave.
/// </summary>
public class ObsSceneCollectionLoaderTests
{
    /// <summary>Gate fake that records MarkNativeStateLoaded flags (R3) and runs queued work synchronously.</summary>
    private sealed class FakeGate : IObsNativeGate
    {
        public bool IsReady { get; init; }

        public int MarkNativeStateLoadedCalls { get; private set; }

        public void MarkNativeStateLoaded() => MarkNativeStateLoadedCalls++;

        public Task<T> RunNativeAsync<T>(Func<T> nativeWork) => Task.FromResult(nativeWork());
    }

    [Fact]
    public async Task LoadAsync_WhenEngineNotReady_ReturnsNotReadyResult()
    {
        var loader = new ObsSceneCollectionLoader(new FakeGate { IsReady = false });

        var result = await loader.LoadAsync("""{"sources":[]}""");

        Assert.False(result.Success);
        Assert.Contains("not ready", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.CreatedCount);
    }

    [Fact]
    public async Task LoadAsync_WhenEngineNotReady_DoesNotFlagNativeState()
    {
        var gate = new FakeGate { IsReady = false };
        var loader = new ObsSceneCollectionLoader(gate);

        await loader.LoadAsync("""{"sources":[]}""");

        Assert.Equal(0, gate.MarkNativeStateLoadedCalls);
    }

    [Fact]
    public async Task LoadAsync_MalformedJson_ThrowsInvalidDataBeforeAnyNativeCall()
    {
        var gate = new FakeGate { IsReady = true };
        var loader = new ObsSceneCollectionLoader(gate);

        await Assert.ThrowsAsync<InvalidDataException>(() => loader.LoadAsync("{ not json"));

        // The plan build is pure JSON work — it must run (and fail) before any
        // gate work or native-state flagging.
        Assert.Equal(0, gate.MarkNativeStateLoadedCalls);
    }
}
