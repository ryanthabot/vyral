using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.ProcessManagement;
using VyralBot.Integrations.Restream;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-4 restream regressions, on top of the wave-3 suite:
///
///   1. DESTINATION VALIDATION (RestreamValidation): scheme gate aligned with
///      what the builder can actually publish (rtmp/rtmps only — every output
///      is muxed FLV), key-less rows allowed when the URL embeds the key, and
///      error messages never echo the stream key.
///   2. BUILDER/VAlidATOR AGREEMENT: a key-less embedded URL publishes
///      verbatim — no trailing-slash "server/" publish URL.
///   3. ROW LIVE VALIDATION wiring (RestreamDestinationItemViewModel).
///   4. RESOLUTION LADDER: the combo labels and autofilled bitrates match
///      RestreamArgumentBuilder's heights/defaults exactly.
///   5. ENGINE AUTO-DISABLE + RE-ENABLE: a destination that kills the session
///      twice is excluded, the exclusion resets on StartAsync (the wave-4 UI
///      restart path) — proven against the real RestreamEngine with a
///      script-based fake FFmpeg, not a mock.
/// </summary>
public class RestreamWave4Tests
{
    private static RestreamDestination Destination(
        string server,
        string key,
        string id = "d1",
        RestreamResolution resolution = RestreamResolution.Source,
        bool enabled = true) => new(
        Id: id,
        Name: "Dest " + id,
        Platform: "Twitch",
        ServerUrl: server,
        StreamKey: key,
        Resolution: resolution,
        VideoBitrateKbps: 4000,
        EncoderPreset: "veryfast",
        AudioMode: RestreamAudioMode.Copy,
        Enabled: enabled);

    private static RestreamSourceConfig Source() => new(RestreamOperationMode.SinglePC, 1935, "vyral");

    // ==================================================================
    // 1. Validation rules (scheme gate, embedded keys, secret-free messages)
    // ==================================================================

    [Theory]
    [InlineData("", "key_12345678", "server URL")]                                  // no URL at all
    [InlineData("   ", "key_12345678", "server URL")]
    [InlineData("live.twitch.tv/app", "key_12345678", "rtmp://")]                   // no scheme
    [InlineData("rtmp://live.twitch.tv/app", "", "stream key")]                     // URL without key
    [InlineData("https://dashboard.twitch.tv/settings/stream", "key_12345678", "web page")] // dashboard page
    [InlineData("http://live.twitch.tv/app", "key_12345678", "web page")]
    [InlineData("srt://ingest.example.com:9000?streamid=key", "key_12345678", "unsupported scheme")]
    [InlineData("udp://239.0.0.1:1234", "key_12345678", "unsupported scheme")]
    [InlineData("rtsp://server.example.com/live", "key_12345678", "unsupported scheme")]
    public void Validate_RejectsWithClearReason(string server, string key, string expectedFragment)
    {
        var problem = RestreamValidation.Validate(Destination(server, key));

        Assert.NotNull(problem);
        Assert.Contains(expectedFragment, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rtmp://live.twitch.tv/app", "live_secret_key_123")]                 // classic split URL + key
    [InlineData("rtmps://a.rtmp.youtube.com/live2", "yt_key_9876543210")]            // RTMPS
    [InlineData("rtmps://ingest.example.com:443/stream/live_abc123def456", "")]      // key embedded in URL
    [InlineData("rtmp://fa7a.ingest.kick.tv/app/live_kick_key_999888", "")]          // Kick-style full URL, key field empty
    [InlineData("rtmp://live.twitch.tv/app/live_secret_key_123", "live_secret_key_123")] // key in both places (deduped later)
    public void Validate_Accepts(string server, string key)
    {
        Assert.Null(RestreamValidation.Validate(Destination(server, key)));
    }

    [Fact]
    public void Validate_MessagesNeverEchoTheStreamKey()
    {
        var key = "super_secret_key_9876543210";

        var problem = RestreamValidation.Validate(Destination("https://twitch.tv/dashboard", key));

        Assert.NotNull(problem);
        Assert.DoesNotContain(key, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DisabledRow_IsStillValidated()
    {
        // The live row hint applies regardless of the Enabled toggle — the
        // user needs to know the row is incomplete before they flip it on.
        Assert.NotNull(RestreamValidation.Validate(Destination("rtmp://live.twitch.tv/app", "", enabled: false)));
    }

    // ==================================================================
    // 2. Builder/validator agreement on key-less embedded URLs
    // ==================================================================

    [Fact]
    public void DestinationUrl_EmptyKey_ReturnsServerVerbatim_NoTrailingSlash()
    {
        // Regression: appending an empty key segment used to publish to
        // "rtmp://host/app/" — rejected by every platform.
        var url = RestreamArgumentBuilder.DestinationUrl(
            Destination("rtmp://fa7a.ingest.kick.tv/app/live_kick_key_999888/", ""));

        Assert.Equal("rtmp://fa7a.ingest.kick.tv/app/live_kick_key_999888", url);
        Assert.False(url.EndsWith("/"), $"url should not end with a trailing slash: {url}");
    }

    [Fact]
    public void Build_KeylessEmbeddedUrl_PublishesServerVerbatim()
    {
        var server = "rtmp://fa7a.ingest.kick.tv/app/live_kick_key_999888";

        var args = RestreamArgumentBuilder.Build(Source(), new[] { Destination(server, "") });

        Assert.Equal(server, args[^1]); // the publish URL is the last argument
    }

    [Theory]
    [InlineData("rtmp://live.twitch.tv/app", false)]                       // base ingest: single app segment
    [InlineData("rtmp://a.rtmp.youtube.com/live2", false)]
    [InlineData("rtmps://ingest.example.com:443/stream", false)]
    [InlineData("rtmp://host/app/live_abc123def456", true)]                // app + key = complete URL
    [InlineData("rtmps://host/live/key123456789", true)]
    public void ServerEmbedsKey_PathDepthDecides(string server, bool expected)
    {
        Assert.Equal(expected, RestreamValidation.ServerEmbedsKey(server));
    }

    [Fact]
    public void Mask_KeylessEmbeddedUrl_HidesTheKeyInsideTheServer()
    {
        // FFmpeg error lines name the full publish URL; for a key-less row
        // the secret lives INSIDE the server URL, so masking must reduce it
        // to scheme://host/*** — not leave the embedded key visible.
        var server = "rtmp://fa7a.ingest.kick.tv/app/live_kick_key_999888";

        var masked = RestreamArgumentBuilder.Mask(
            $"[rtmp @ fake] Connection failed to {server}",
            new[] { Destination(server, "") });

        Assert.DoesNotContain("live_kick_key_999888", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rtmp://fa7a.ingest.kick.tv/***", masked, StringComparison.OrdinalIgnoreCase);
    }

    // ==================================================================
    // 3. Row live-validation wiring
    // ==================================================================

    [Fact]
    public void Row_ValidationMessage_TracksServerAndKeyEdits()
    {
        var item = RestreamDestinationItemViewModel.FromModel(
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123"));

        Assert.Null(item.ValidationMessage); // complete row: no hint

        item.StreamKey = "";
        Assert.NotNull(item.ValidationMessage);
        Assert.Contains("stream key", item.ValidationMessage, StringComparison.Ordinal);

        item.ServerUrl = "https://twitch.tv/dashboard";
        Assert.Contains("web page", item.ValidationMessage, StringComparison.Ordinal);

        // Kick-style rescue: pasting the full URL (key appended) clears it.
        item.ServerUrl = "rtmp://fa7a.ingest.kick.tv/app/live_kick_key_999888";
        Assert.Null(item.ValidationMessage);

        // ...and breaking the scheme brings the hint back.
        item.ServerUrl = "srt://ingest.example.com:9000";
        Assert.NotNull(item.ValidationMessage);
    }

    // ==================================================================
    // 4. Resolution ladder: labels + autofilled bitrates match the builder
    // ==================================================================

    [Fact]
    public void ResolutionChoices_LabelsMatchBuilderHeights()
    {
        var choices = new RestreamDestinationItemViewModel().ResolutionChoices;

        Assert.Equal(6, choices.Count);
        Assert.Equal("Source (no re-encode)", choices[0].Label);
        Assert.Null(RestreamArgumentBuilder.Height(choices[0].Value));

        foreach (var choice in choices.Skip(1))
        {
            var height = RestreamArgumentBuilder.Height(choice.Value);
            Assert.NotNull(height);
            Assert.Equal($"{height}p", choice.Label); // "1080p"… "360p", never a drifted label
        }
    }

    [Fact]
    public void ResolutionChange_AutofillsBuilderDefaultBitrate()
    {
        var item = RestreamDestinationItemViewModel.FromModel(
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123"));
        var choices = item.ResolutionChoices;

        foreach (var choice in choices.Skip(1))
        {
            item.Resolution = choice;
            Assert.Equal(RestreamArgumentBuilder.DefaultBitrateKbps(choice.Value).ToString(), item.BitrateText);
            Assert.True(item.IsScaled); // scaled rows expose bitrate + preset fields
        }

        item.Resolution = choices[0]; // back to Source
        Assert.False(item.IsScaled);
    }

    // ==================================================================
    // 5. Engine auto-disable + re-enable (real RestreamEngine + fake ffmpeg)
    // ==================================================================

    /// <summary>
    /// Test-double ProcessManager: mirrors the real one's lifecycle exactly
    /// (EnableRaisingEvents before start, tracking by pid, dispose on exit,
    /// tree-kill) so the engine's disposal-race handling is exercised for real.
    /// </summary>
    private sealed class TrackingProcessManager : IProcessManager
    {
        private readonly ConcurrentDictionary<int, Process> _processes = new();

        public int StartCount;

        public Task<Process> StartProcessAsync(ProcessStartInfo startInfo, CancellationToken ct = default)
        {
            var process = new Process { StartInfo = startInfo };
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) =>
            {
                if (sender is Process exited)
                {
                    _processes.TryRemove(exited.Id, out _);
                    exited.Dispose();
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"fake manager could not start {startInfo.FileName}");
            }

            Interlocked.Increment(ref StartCount);
            _processes[process.Id] = process;
            return Task.FromResult(process);
        }

        public IReadOnlyList<ProcessInfo> GetActiveProcesses() =>
            _processes.Values.Select(p => new ProcessInfo(p.Id, p.ProcessName, p.StartTime, true)).ToList();

        public Task KillProcessAsync(int processId, CancellationToken ct = default)
        {
            if (_processes.TryRemove(processId, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception)
                {
                    // Already gone.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return Task.CompletedTask;
        }

        public async Task KillAllProcessesAsync(CancellationToken ct = default)
        {
            foreach (var pid in _processes.Keys.ToList())
            {
                await KillProcessAsync(pid, ct);
            }
        }

        public async ValueTask DisposeAsync() => await KillAllProcessesAsync();
    }

    private static string WriteFakeFfmpeg(string name, string script)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vyral-wave4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, script);
        return path;
    }

    /// <summary>Always fails, naming the bad destination's full publish URL on stderr.</summary>
    private const string BadKeyScript = """
        @echo off
        echo [rtmp @ fake] Connection failed to rtmp://live.twitch.tv/app/badkey_12345678 1>&2
        exit /b 1
        """;

    /// <summary>Fails while the bad destination is on the command line; once it is excluded, acts as a healthy parked publisher.</summary>
    private const string ConditionalScript = """
        @echo off
        echo %* | findstr /C:"badkey_12345678" >nul && goto :fail
        echo Input #0, rtmp from 127.0.0.1 1>&2
        ping -n 120 127.0.0.1 >nul
        exit /b 0
        :fail
        echo [rtmp @ fake] Connection failed to rtmp://live.twitch.tv/app/badkey_12345678 1>&2
        exit /b 1
        """;

    /// <summary>
    /// Healthy publisher that stops cleanly after ~11s (the OBS-disconnect
    /// path). Longer than the engine's 10s quick-failure window on purpose:
    /// quick post-publisher exits are classified as crashes by design.
    /// </summary>
    private const string CleanExitScript = """
        @echo off
        echo Input #0, rtmp from 127.0.0.1 1>&2
        ping -n 12 127.0.0.1 >nul
        exit /b 0
        """;

    /// <summary>Healthy publisher that parks until killed.</summary>
    private const string ParkScript = """
        @echo off
        echo Input #0, rtmp from 127.0.0.1 1>&2
        ping -n 120 127.0.0.1 >nul
        """;

    private static RestreamSessionConfig Config(string ffmpegPath, params RestreamDestination[] destinations) =>
        new(ffmpegPath, Source(), destinations);

    private static RestreamDestination BadDestination() =>
        Destination("rtmp://live.twitch.tv/app", "badkey_12345678", id: "bad");

    private static RestreamDestination GoodDestination() =>
        Destination("rtmp://good.example.com/app", "goodkey_87654321", id: "good");

    private static async Task<RestreamEngineStatus> WaitForAsync(
        RestreamEngine engine, Func<RestreamEngineStatus, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = engine.Status;
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(100);
        }

        return engine.Status; // let the caller's assertions fail on the final snapshot
    }

    [Fact]
    public async Task Engine_AutoDisabledDestination_IsReenabledByRestart()
    {
        var manager = new TrackingProcessManager();
        using var engine = new RestreamEngine(manager, NullLogger<RestreamEngine>.Instance);
        var config = Config(WriteFakeFfmpeg("bad-ffmpeg.cmd", BadKeyScript), BadDestination());

        await engine.StartAsync(config);

        // Two attributed failures -> destination excluded -> "every destination
        // failed repeatedly" fault, with the row visibly AutoDisabled.
        var faulted = await WaitForAsync(engine,
            s => s.State == RestreamSessionState.Faulted && s.Message.Contains("every destination failed", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30));
        Assert.Equal(RestreamSessionState.Faulted, faulted.State);
        var row = Assert.Single(faulted.Destinations);
        Assert.Equal(RestreamDestinationHealth.AutoDisabled, row.Health);
        Assert.Equal(2, manager.StartCount); // exactly the two failing runs

        // The wave-4 re-enable path: StartAsync again (same config, same app
        // instance — what the Restart button does) resets the exclusion. The
        // row may already show Error/Pending from the new session's first
        // failing run — what matters is that it is IN the session again.
        await engine.StartAsync(config);
        var fresh = engine.Status;
        Assert.Equal(RestreamSessionState.WaitingForSource, fresh.State);
        row = Assert.Single(fresh.Destinations);
        Assert.NotEqual(RestreamDestinationHealth.AutoDisabled, row.Health); // re-included and retried

        await engine.StopAsync();
        Assert.Equal(RestreamSessionState.Idle, engine.Status.State);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task Engine_BadDestinationExcluded_GoodDestinationKeepsStreaming()
    {
        var manager = new TrackingProcessManager();
        using var engine = new RestreamEngine(manager, NullLogger<RestreamEngine>.Instance);
        var config = Config(
            WriteFakeFfmpeg("conditional-ffmpeg.cmd", ConditionalScript),
            BadDestination(),
            GoodDestination());

        await engine.StartAsync(config);

        var streaming = await WaitForAsync(engine,
            s => s.State == RestreamSessionState.Streaming
                && s.Destinations.Any(d => d.Id == "good" && d.Health == RestreamDestinationHealth.Active),
            TimeSpan.FromSeconds(30));

        Assert.Equal(RestreamSessionState.Streaming, streaming.State);
        var bad = streaming.Destinations.Single(d => d.Id == "bad");
        var good = streaming.Destinations.Single(d => d.Id == "good");
        Assert.Equal(RestreamDestinationHealth.AutoDisabled, bad.Health); // dropped, session survived
        Assert.Equal(RestreamDestinationHealth.Active, good.Health);
        Assert.True(manager.StartCount >= 3); // 2 failing runs + the healthy re-listen

        await engine.StopAsync();
        Assert.Equal(RestreamSessionState.Idle, engine.Status.State);
    }

    [Fact]
    public async Task Engine_PublisherCleanExit_GoesBackToListeningForReconnect()
    {
        var manager = new TrackingProcessManager();
        using var engine = new RestreamEngine(manager, NullLogger<RestreamEngine>.Instance);

        // The "waiting for reconnect" window is transient (the engine
        // immediately re-listens and the fake publisher reconnects), so record
        // every raised status instead of polling a snapshot.
        var observed = new ConcurrentQueue<RestreamEngineStatus>();
        engine.StatusChanged += (_, status) => observed.Enqueue(status);

        await engine.StartAsync(Config(WriteFakeFfmpeg("clean-ffmpeg.cmd", CleanExitScript), GoodDestination()));

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
        while (DateTimeOffset.UtcNow < deadline
               && !observed.Any(s => s.State == RestreamSessionState.WaitingForSource
                   && s.Message.Contains("reconnect", StringComparison.Ordinal)))
        {
            await Task.Delay(100);
        }

        // Clean publisher stop (exit 0 after a real session, even when the
        // exit code read races the ProcessManager's disposal): re-listen, no
        // fault, no "FFmpeg keeps exiting".
        Assert.Contains(observed, s =>
            s.State == RestreamSessionState.WaitingForSource
            && s.Message.Contains("reconnect", StringComparison.Ordinal));
        Assert.True(engine.IsRunning); // still supervising: it re-listened instead of faulting

        await engine.StopAsync();
        Assert.Equal(RestreamSessionState.Idle, engine.Status.State);
    }

    [Fact]
    public async Task Engine_StartTwiceFast_SerializesAndLeavesOneRunningSession()
    {
        var manager = new TrackingProcessManager();
        using var engine = new RestreamEngine(manager, NullLogger<RestreamEngine>.Instance);
        var config = Config(WriteFakeFfmpeg("park-ffmpeg.cmd", ParkScript), GoodDestination());

        // Two starts fired together — the engine mutex must serialize them; the
        // second replaces the first, leaving exactly one supervision running.
        await Task.WhenAll(engine.StartAsync(config), engine.StartAsync(config));

        Assert.True(engine.IsRunning);
        await WaitForAsync(engine, s => s.State == RestreamSessionState.Streaming, TimeSpan.FromSeconds(30));

        await engine.StopAsync();
        Assert.Equal(RestreamSessionState.Idle, engine.Status.State);
        Assert.Equal(0, manager.GetActiveProcesses().Count); // no orphaned fake ffmpeg
    }
}
