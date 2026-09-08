using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Native;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Embedded-OBS Task 6: the pure output-planning parts (container extension
/// normalization, record path composition, encoder fallback chains, settings
/// JSON) plus the ObsOutputService error surface when the engine is not
/// Ready. Native start/stop paths need a booted libobs and are exercised by
/// the manual verification wave — no test here touches obs.dll.
/// </summary>
public class ObsOutputServiceTests
{
    // ---------------------------------------------------------------------
    // Container extension normalization (profile RecFormat2 → ffmpeg ext)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("hybrid_mp4", "mp4")]
    [InlineData("mp4", "mp4")]
    [InlineData("mkv", "mkv")]
    [InlineData("mov", "mov")]
    [InlineData("MKV", "mkv")]
    [InlineData("flv", "flv")]
    [InlineData("ts", "ts")]
    public void NormalizeContainerExtension_KnownFormats_MapToContainerExt(string format, string expected)
    {
        Assert.Equal(expected, ObsOutputPlanner.NormalizeContainerExtension(format));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hybrid_weird")]
    [InlineData("fragmented_mp4")]
    public void NormalizeContainerExtension_UnknownFormat_FallsBackToMp4(string? format)
    {
        Assert.Equal("mp4", ObsOutputPlanner.NormalizeContainerExtension(format));
    }

    // ---------------------------------------------------------------------
    // Record file path composition (ffmpeg_muxer "path" = full file path)
    // ---------------------------------------------------------------------

    [Fact]
    public void RecordFilePath_ComposesDirectoryTimestampAndNormalizedExt()
    {
        var when = new DateTimeOffset(2026, 9, 7, 18, 4, 9, TimeSpan.Zero);

        var path = ObsOutputPlanner.RecordFilePath(@"C:\Users\user\Videos", "hybrid_mp4", when);

        Assert.Equal(@"C:\Users\user\Videos\rec_20260907_180409.mp4", path);
    }

    [Fact]
    public void RecordFilePath_MkvKeepsItsExt()
    {
        var when = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var path = ObsOutputPlanner.RecordFilePath(@"D:\Clips", "mkv", when);

        Assert.Equal(@"D:\Clips\rec_20260102_030405.mkv", path);
    }

    // ---------------------------------------------------------------------
    // Encoder fallback chains (unknown ids make obs_video_encoder_create
    // return null → the service walks the candidate list)
    // ---------------------------------------------------------------------

    [Fact]
    public void VideoEncoderCandidates_NullProfileId_FallsBackToX264Only()
    {
        Assert.Equal(new[] { "obs_x264" }, ObsOutputPlanner.VideoEncoderCandidates(null));
        Assert.Equal(new[] { "obs_x264" }, ObsOutputPlanner.VideoEncoderCandidates("  "));
    }

    [Fact]
    public void VideoEncoderCandidates_SimpleModeNvencAlias_KeptFirstThenKnownChain()
    {
        // "nvenc" is what the real profile on this machine stores (Simple mode
        // UI alias) — libobs has no such id, so it must stay first (harmless
        // null) and fall through to the real ids.
        Assert.Equal(
            new[] { "nvenc", "jim_nvenc", "obs_x264" },
            ObsOutputPlanner.VideoEncoderCandidates("nvenc"));
    }

    [Fact]
    public void VideoEncoderCandidates_ProfileIdDeduped_PreservingFirstOccurrence()
    {
        Assert.Equal(
            new[] { "jim_nvenc", "obs_x264" },
            ObsOutputPlanner.VideoEncoderCandidates("jim_nvenc"));
        Assert.Equal(
            new[] { "obs_x264", "jim_nvenc" },
            ObsOutputPlanner.VideoEncoderCandidates("obs_x264"));
    }

    [Fact]
    public void RecordVideoEncoderCandidates_RecordIdPreferred_StreamAsFallback()
    {
        Assert.Equal(
            new[] { "obs_x264", "jim_nvenc" },
            ObsOutputPlanner.RecordVideoEncoderCandidates("obs_x264", "nvenc"));
        Assert.Equal(
            new[] { "nvenc", "jim_nvenc", "obs_x264" },
            ObsOutputPlanner.RecordVideoEncoderCandidates(null, "nvenc"));
        Assert.Equal(
            new[] { "obs_x264" },
            ObsOutputPlanner.RecordVideoEncoderCandidates(null, null));
    }

    // ---------------------------------------------------------------------
    // Settings JSON (ffmpeg_muxer / replay_buffer minimal viable settings)
    // ---------------------------------------------------------------------

    [Fact]
    public void RecordSettingsJson_CarriesFullPath()
    {
        var data = JsonDocument.Parse(ObsOutputPlanner.RecordSettingsJson(@"C:\V\rec_1.mp4")).RootElement;

        Assert.Equal(@"C:\V\rec_1.mp4", data.GetProperty("path").GetString());
    }

    [Fact]
    public void ReplayBufferSettingsJson_CarriesDirectoryFormatExtensionAndMaxTime()
    {
        var data = JsonDocument.Parse(ObsOutputPlanner.ReplayBufferSettingsJson(@"C:\Users\user\Videos", "mp4")).RootElement;

        Assert.Equal(@"C:\Users\user\Videos", data.GetProperty("directory").GetString());
        Assert.Equal("replay_%CCYY-%MM-%DD_%hh-%mm-%ss", data.GetProperty("format").GetString());
        Assert.Equal("mp4", data.GetProperty("extension").GetString());
        Assert.Equal(15, data.GetProperty("max_time_sec").GetInt32());
    }

    // ---------------------------------------------------------------------
    // Native constants pin (libobs/obs-output.h @ 32.1.1 defines)
    // ---------------------------------------------------------------------

    [Fact]
    public void OutputFlags_MatchObsOutputHeaderDefines()
    {
        Assert.Equal(1u, ObsNativeConstants.OutputFlagVideo); // (1 << 0)
        Assert.Equal(2u, ObsNativeConstants.OutputFlagAudio); // (1 << 1)
        Assert.Equal(3u, ObsNativeConstants.OutputFlagsAv);   // VIDEO | AUDIO
    }

    // ---------------------------------------------------------------------
    // Error surface: engine never reached Ready → friendly results, and the
    // whole path runs without any native call (empty locator root ⇒ boot
    // fails before any obs.dll P/Invoke, matching ObsEngineTests).
    // ---------------------------------------------------------------------

    private static string CreateEmptyTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vyral-obs-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<ObsEngine> CreateFailedEngineAsync(string root)
    {
        var engine = new ObsEngine(new ObsInstallLocator(root), NullLogger<ObsEngine>.Instance);
        await engine.InitializeAsync(); // empty root → Failed before any P/Invoke
        return engine;
    }

    [Fact]
    public async Task StartStopSave_WhenEngineNotReady_ReturnFriendlyErrorResults()
    {
        var root = CreateEmptyTempRoot();
        try
        {
            using var engine = await CreateFailedEngineAsync(root);
            var service = new ObsOutputService(engine, new ObsInstallLocator(root));

            Assert.False(service.IsConfigured);

            Assert.False((await service.StartStreamAsync()).Success);
            Assert.False((await service.StopStreamAsync()).Success);
            Assert.False((await service.StartRecordAsync()).Success);
            Assert.False((await service.StopRecordAsync()).Success);
            Assert.False((await service.StartReplayBufferAsync()).Success);
            Assert.False((await service.StopReplayBufferAsync()).Success);
            Assert.False((await service.SaveReplayAsync()).Success);

            Assert.Contains("not initialized", (await service.StartStreamAsync()).Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not initialized", (await service.SaveReplayAsync()).Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ } }
    }

    [Fact]
    public async Task GetStates_WhenEngineNotReady_AllInactive()
    {
        var root = CreateEmptyTempRoot();
        try
        {
            using var engine = await CreateFailedEngineAsync(root);
            var service = new ObsOutputService(engine, new ObsInstallLocator(root));

            var states = await service.GetStatesAsync();

            Assert.Equal(3, states.Count);
            Assert.All(states, s => Assert.False(s.Active || s.Reconnecting || s.Starting));
            Assert.Equal(
                new[] { ObsOutputKind.Stream, ObsOutputKind.Record, ObsOutputKind.ReplayBuffer },
                states.Select(s => s.Kind));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ } }
    }
}
