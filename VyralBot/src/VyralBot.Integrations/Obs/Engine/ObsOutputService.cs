using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Native;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>The three outputs ObsOutputService drives.</summary>
public enum ObsOutputKind
{
    Stream,
    Record,
    ReplayBuffer,
}

/// <summary>Outcome of one output action — user-facing, never contains secrets (no server URLs or keys).</summary>
public sealed record ObsOutputActionResult(bool Success, string Message);

/// <summary>Immutable snapshot of one output for 1 Hz UI polling.</summary>
public sealed record ObsOutputState(ObsOutputKind Kind, bool Active, bool Reconnecting, bool Starting);

/// <summary>
/// Stream/record/replay-buffer outputs built from the OBS profile the user
/// last selected. Every Start re-reads the profile (the user can switch
/// profiles while the app runs), builds fresh rtmp_custom service + encoder
/// + output objects under the engine gate, and keeps their native references
/// in instance fields until Stop releases them. All native work runs through
/// <see cref="IObsNativeGate.RunNativeAsync{T}"/>; the post-stop activity
/// polling deliberately sleeps OUTSIDE the gate so a stopping output never
/// blocks engine/preview traffic.
///
/// Secrets: the stream server/key are serialized straight into an obs_data
/// object in memory and never logged, and no message ever embeds them.
/// </summary>
public sealed class ObsOutputService
{
    /// <summary>Native references owned by one running output. Released (output first, then encoders/service) on stop.</summary>
    private sealed record NativeRefs(IntPtr Output, IntPtr VideoEncoder, IntPtr AudioEncoder, IntPtr Service);

    private const string ServiceId = "rtmp_custom";
    private const string AudioEncoderId = "ffmpeg_aac";
    private const string StreamOutputId = "rtmp_output";
    private const string RecordOutputId = "ffmpeg_muxer";
    private const string ReplayOutputId = "replay_buffer";

    // Wait bounds for the post-stop drain (outside the gate, 100 ms slices).
    private static readonly TimeSpan GracefulDrain = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForcedDrain = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(100);

    private readonly IObsNativeGate engine;
    private readonly ObsInstallLocator locator;
    private readonly ILogger<ObsOutputService> logger;

    // Written only inside gate-held native work; read the same way.
    private NativeRefs? streamRefs;
    private NativeRefs? recordRefs;
    private NativeRefs? replayRefs;
    private bool streamStarting;
    private bool recordStarting;
    private bool replayStarting;

    public ObsOutputService(ObsEngine engine, ObsInstallLocator locator, ILogger<ObsOutputService>? logger = null)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.logger = logger ?? NullLogger<ObsOutputService>.Instance;
    }

    /// <summary>True once the embedded libobs engine reached Ready — the precondition for every output action.</summary>
    public bool IsConfigured => engine.IsReady;

    public Task<ObsOutputActionResult> StartStreamAsync() => StartOutputAsync(ObsOutputKind.Stream);
    public Task<ObsOutputActionResult> StopStreamAsync() => StopOutputAsync(ObsOutputKind.Stream);
    public Task<ObsOutputActionResult> StartRecordAsync() => StartOutputAsync(ObsOutputKind.Record);
    public Task<ObsOutputActionResult> StopRecordAsync() => StopOutputAsync(ObsOutputKind.Record);
    public Task<ObsOutputActionResult> StartReplayBufferAsync() => StartOutputAsync(ObsOutputKind.ReplayBuffer);
    public Task<ObsOutputActionResult> StopReplayBufferAsync() => StopOutputAsync(ObsOutputKind.ReplayBuffer);

    /// <summary>
    /// App-exit funnel (App.OnExit): gracefully stops every live or starting
    /// output, drains OUTSIDE the engine gate (5 s graceful, 2 s after a force
    /// stop), releases the native references, and never throws — a wedged
    /// output must not block the process from exiting.
    /// </summary>
    public async Task StopAllAsync()
    {
        foreach (ObsOutputKind kind in (ObsOutputKind[])ObsOutputKind.GetValues(typeof(ObsOutputKind)))
        {
            try
            {
                await StopCoreAsync(kind).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Includes the not-ready fault from RunNativeAsync when the
                // engine never booted — nothing to stop in that case.
                logger.LogWarning(ex, "OBS output: {Kind} could not be stopped on exit", kind);
            }
        }
    }

    /// <summary>
    /// Triggers the replay buffer's save proc (obs-ffmpeg-mux.c @ 32.1.1
    /// registers <c>void save()</c> on the output's proc handler; the
    /// "saved" signal is emitted BY the output after muxing, so firing that
    /// signal from the host would not save anything).
    /// </summary>
    public Task<ObsOutputActionResult> SaveReplayAsync()
    {
        if (!engine.IsReady)
        {
            return Task.FromResult(Fail($"{Label(ObsOutputKind.ReplayBuffer)} save unavailable: OBS engine is not initialized."));
        }

        return engine.RunNativeAsync(() =>
        {
            IntPtr output = replayRefs?.Output ?? IntPtr.Zero;
            if (output == IntPtr.Zero || !ObsNative.ObsOutputActive(output))
            {
                return Fail("The replay buffer is not active — start it before saving a replay.");
            }

            // 1 = saved, 2 = output alive but the save proc call failed.
            int outcome = ObsNative.ProcHandlerCall(ObsNative.ObsOutputGetProcHandler(output), "save", IntPtr.Zero) ? 1 : 2;
            return outcome switch
            {
                1 => Pass("Replay saved."),
                _ => Fail("The replay buffer rejected the save request."),
            };
        });
    }

    /// <summary>Snapshot of all three outputs for the UI's 1 Hz poll. Reports all-inactive when the engine is not Ready.</summary>
    public Task<IReadOnlyList<ObsOutputState>> GetStatesAsync()
    {
        if (!engine.IsReady)
        {
            IReadOnlyList<ObsOutputState> inactive =
            [
                new(ObsOutputKind.Stream, false, false, false),
                new(ObsOutputKind.Record, false, false, false),
                new(ObsOutputKind.ReplayBuffer, false, false, false),
            ];
            return Task.FromResult(inactive);
        }

        return engine.RunNativeAsync(() =>
        {
            var states = new List<ObsOutputState>(3);
            foreach (ObsOutputKind kind in (ObsOutputKind[])ObsOutputKind.GetValues(typeof(ObsOutputKind)))
            {
                NativeRefs? refs = RefsOf(kind);
                IntPtr output = refs?.Output ?? IntPtr.Zero;
                bool active = output != IntPtr.Zero && ObsNative.ObsOutputActive(output);
                bool reconnecting = output != IntPtr.Zero && ObsNative.ObsOutputReconnecting(output);
                bool starting = StartingFlagOf(kind);
                if (active)
                {
                    ClearStartingFlag(kind);
                    starting = false;
                }

                states.Add(new ObsOutputState(kind, active, reconnecting, starting && !active));
            }

            return (IReadOnlyList<ObsOutputState>)states;
        });
    }

    // ------------------------------------------------------------------
    // Start
    // ------------------------------------------------------------------

    private async Task<ObsOutputActionResult> StartOutputAsync(ObsOutputKind kind)
    {
        if (!engine.IsReady)
        {
            return Fail($"{Label(kind)} cannot start: OBS engine is not initialized.");
        }

        try
        {
            ObsOutputActionResult result = await engine.RunNativeAsync(() => StartCore(kind)).ConfigureAwait(false);
            // The one log line EVERY start path funnels through — the UI only
            // shows the message, so without this a failed start (encoder,
            // capture, output_start…) leaves the log silent at the stall point.
            if (result.Success)
            {
                logger.LogInformation("OBS output: {Kind} start outcome: {Message}", kind, result.Message);
            }
            else
            {
                logger.LogWarning("OBS output: {Kind} start failed: {Message}", kind, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OBS output: {Kind} start failed", kind);
            return Fail($"{Label(kind)} failed to start: {ex.Message}");
        }
    }

    /// <summary>Gate held via RunNativeAsync. Builds the profile-driven object graph and starts the output.</summary>
    private ObsOutputActionResult StartCore(ObsOutputKind kind)
    {
        if (locator.Locate() is not { } install)
        {
            return Fail($"{Label(kind)} cannot start: the OBS Studio install was not found.");
        }

        ObsProfileResult profile = new ObsProfileReader(install).ReadCurrentProfile();
        if (!profile.Success || profile.Settings is null)
        {
            return Fail($"{Label(kind)} cannot start: {profile.Error}");
        }

        NativeRefs? existing = RefsOf(kind);
        if (existing is { } refs && refs.Output != IntPtr.Zero && ObsNative.ObsOutputActive(refs.Output))
        {
            return Fail($"{Label(kind)} is already active.");
        }

        // Stale references from a failed earlier start — release before rebuilding.
        ReleaseRefs(kind);

        return kind switch
        {
            ObsOutputKind.Stream => StartStreamCore(profile.Settings),
            ObsOutputKind.Record => StartRecordCore(profile.Settings),
            _ => StartReplayCore(profile.Settings),
        };
    }

    private ObsOutputActionResult StartStreamCore(ObsProfileSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Service.Server))
        {
            return Fail("Stream cannot start: the OBS profile has no stream server configured.");
        }

        // The key rides in this JSON only — never logged, never in errors.
        IntPtr serviceData = ObsNative.ObsDataCreateFromJson(JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["server"] = settings.Service.Server,
                ["key"] = settings.Service.StreamKey ?? string.Empty,
            }));
        IntPtr service = ObsNative.ObsServiceCreate(ServiceId, "vyral_service", serviceData, IntPtr.Zero);
        ObsNative.ObsDataRelease(serviceData);
        if (service == IntPtr.Zero)
        {
            return Fail("Stream cannot start: the RTMP service could not be created.");
        }

        IntPtr videoEncoder = CreateVideoEncoder(
            ObsOutputPlanner.VideoEncoderCandidates(settings.Output.StreamEncoderId),
            ObsOutputPlanner.VideoEncoderSettingsJson(settings.Output.StreamVideoBitrateKbps),
            "vyral_stream_enc");
        if (videoEncoder == IntPtr.Zero)
        {
            ObsNative.ObsServiceRelease(service);
            return Fail("Stream cannot start: no usable video encoder was found (tried the profile encoder, jim_nvenc and obs_x264).");
        }

        IntPtr audioEncoder = CreateAudioEncoder(ObsOutputPlanner.AudioEncoderSettingsJson(settings.Output.AudioBitrateKbps), "vyral_stream_aac");
        if (audioEncoder == IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(videoEncoder);
            ObsNative.ObsServiceRelease(service);
            return Fail("Stream cannot start: the AAC audio encoder could not be created.");
        }

        IntPtr output = CreateOutput(StreamOutputId, "vyral_stream_output", "{}");
        if (output == IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(videoEncoder);
            ObsNative.ObsEncoderRelease(audioEncoder);
            ObsNative.ObsServiceRelease(service);
            return Fail("Stream cannot start: the RTMP output could not be created.");
        }

        ObsNative.ObsOutputSetService(output, service);
        ObsNative.ObsOutputSetVideoEncoder(output, videoEncoder);
        ObsNative.ObsOutputSetAudioEncoder(output, audioEncoder, (UIntPtr)0);
        logger.LogInformation("OBS output: service and audio/video encoders attached to the {OutputId} output", StreamOutputId);

        var refs = new NativeRefs(output, videoEncoder, audioEncoder, service);
        ObsOutputActionResult start = BeginDataCapture(output, refs, ObsOutputKind.Stream);
        if (start.Success)
        {
            streamRefs = refs;
            streamStarting = true;
        }

        return start;
    }

    private ObsOutputActionResult StartRecordCore(ObsProfileSettings settings)
    {
        string directory = Environment.ExpandEnvironmentVariables(settings.Output.RecordDirectory);
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return Fail($"Recording cannot start: the record directory '{directory}' could not be created ({ex.Message}).");
        }

        // ffmpeg_muxer takes the complete file path (obs-ffmpeg-mux.c @ 32.1.1);
        // compose the timestamped name here.
        string filePath = ObsOutputPlanner.RecordFilePath(directory, settings.Output.RecordFormat, DateTimeOffset.Now);

        // Separate encoders per output — an encoder pair can only feed one
        // active output, so stream/record/replay must never share instances.
        IntPtr videoEncoder = CreateVideoEncoder(
            ObsOutputPlanner.RecordVideoEncoderCandidates(settings.Output.RecordEncoderId, settings.Output.StreamEncoderId),
            ObsOutputPlanner.VideoEncoderSettingsJson(settings.Output.StreamVideoBitrateKbps),
            "vyral_rec_enc");
        if (videoEncoder == IntPtr.Zero)
        {
            return Fail("Recording cannot start: no usable video encoder was found (tried the profile record encoder, jim_nvenc and obs_x264).");
        }

        IntPtr audioEncoder = CreateAudioEncoder(ObsOutputPlanner.AudioEncoderSettingsJson(settings.Output.AudioBitrateKbps), "vyral_rec_aac");
        if (audioEncoder == IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(videoEncoder);
            return Fail("Recording cannot start: the AAC audio encoder could not be created.");
        }

        IntPtr output = CreateOutput(RecordOutputId, "vyral_record_output", ObsOutputPlanner.RecordSettingsJson(filePath));
        if (output == IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(videoEncoder);
            ObsNative.ObsEncoderRelease(audioEncoder);
            return Fail("Recording cannot start: the file output could not be created.");
        }

        ObsNative.ObsOutputSetVideoEncoder(output, videoEncoder);
        ObsNative.ObsOutputSetAudioEncoder(output, audioEncoder, (UIntPtr)0);
        logger.LogInformation("OBS output: audio/video encoders attached to the {OutputId} output", RecordOutputId);

        var refs = new NativeRefs(output, videoEncoder, audioEncoder, IntPtr.Zero);
        ObsOutputActionResult start = BeginDataCapture(output, refs, ObsOutputKind.Record);
        if (start.Success)
        {
            recordRefs = refs;
            recordStarting = true;
        }

        return start;
    }

    private ObsOutputActionResult StartReplayCore(ObsProfileSettings settings)
    {
        string directory = Environment.ExpandEnvironmentVariables(settings.Output.RecordDirectory);
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return Fail($"The replay buffer cannot start: the record directory '{directory}' could not be created ({ex.Message}).");
        }

        string extension = ObsOutputPlanner.NormalizeContainerExtension(settings.Output.RecordFormat);
        IntPtr videoEncoder = CreateVideoEncoder(
            ObsOutputPlanner.RecordVideoEncoderCandidates(settings.Output.RecordEncoderId, settings.Output.StreamEncoderId),
            ObsOutputPlanner.VideoEncoderSettingsJson(settings.Output.StreamVideoBitrateKbps),
            "vyral_replay_enc");
        if (videoEncoder == IntPtr.Zero)
        {
            return Fail("The replay buffer cannot start: no usable video encoder was found (tried the profile record encoder, jim_nvenc and obs_x264).");
        }

        IntPtr audioEncoder = CreateAudioEncoder(ObsOutputPlanner.AudioEncoderSettingsJson(settings.Output.AudioBitrateKbps), "vyral_replay_aac");
        if (audioEncoder == IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(videoEncoder);
            return Fail("The replay buffer cannot start: the AAC audio encoder could not be created.");
        }

        IntPtr output = CreateOutput(ReplayOutputId, "vyral_replay", ObsOutputPlanner.ReplayBufferSettingsJson(directory, extension));
        if (output == IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(videoEncoder);
            ObsNative.ObsEncoderRelease(audioEncoder);
            return Fail("The replay buffer cannot start: the replay output could not be created.");
        }

        ObsNative.ObsOutputSetVideoEncoder(output, videoEncoder);
        ObsNative.ObsOutputSetAudioEncoder(output, audioEncoder, (UIntPtr)0);
        logger.LogInformation("OBS output: audio/video encoders attached to the {OutputId} output", ReplayOutputId);

        var refs = new NativeRefs(output, videoEncoder, audioEncoder, IntPtr.Zero);
        ObsOutputActionResult start = BeginDataCapture(output, refs, ObsOutputKind.ReplayBuffer);
        if (start.Success)
        {
            replayRefs = refs;
            replayStarting = true;
        }

        return start;
    }

    /// <summary>Gate held. The can-capture → initialize-encoders → start sequence shared by every output. Each step logs its result — the stall point of a dead start must be findable in the log alone.</summary>
    private ObsOutputActionResult BeginDataCapture(IntPtr output, NativeRefs refs, ObsOutputKind kind)
    {
        if (!ObsNative.ObsOutputCanBeginDataCapture(output, ObsNativeConstants.OutputFlagsAv))
        {
            logger.LogWarning("OBS output: {Kind} can_begin_data_capture = false (flags AV) — aborting the start", kind);
            ReleaseRefs(refs);
            return Fail($"{Label(kind)} cannot start: data capture cannot begin (no matching audio/video capture).");
        }

        logger.LogInformation("OBS output: {Kind} can_begin_data_capture = true", kind);

        if (!ObsNative.ObsOutputInitializeEncoders(output, ObsNativeConstants.OutputFlagsAv))
        {
            logger.LogWarning("OBS output: {Kind} initialize_encoders = false — aborting the start", kind);
            ReleaseRefs(refs);
            return Fail($"{Label(kind)} cannot start: the encoders could not be initialized.");
        }

        logger.LogInformation("OBS output: {Kind} initialize_encoders = true", kind);

        if (!ObsNative.ObsOutputStart(output))
        {
            logger.LogWarning("OBS output: {Kind} output_start = false — aborting the start", kind);
            ReleaseRefs(refs);
            return Fail($"{Label(kind)} could not be started.");
        }

        logger.LogInformation("OBS output: {Kind} output_start = true", kind);
        logger.LogInformation("OBS output: {Kind} started", kind);
        return Pass($"{Label(kind)} started.");
    }

    /// <summary>
    /// Gate held. Creates the video encoder from the first candidate id
    /// libobs accepts. The winning id is logged only when
    /// obs_video_encoder_create returned a non-null encoder — the logged id
    /// is always the one libobs actually accepted, never a requested one.
    /// </summary>
    private IntPtr CreateVideoEncoder(IReadOnlyList<string> candidateIds, string settingsJson, string name)
    {
        foreach (string id in candidateIds)
        {
            IntPtr data = ObsNative.ObsDataCreateFromJson(settingsJson);
            IntPtr encoder = ObsNative.ObsVideoEncoderCreate(id, name, data, IntPtr.Zero);
            ObsNative.ObsDataRelease(data);
            if (encoder != IntPtr.Zero)
            {
                logger.LogInformation("OBS output: video encoder created with id {EncoderId} (accepted by libobs)", id);
                return encoder;
            }

            logger.LogInformation("OBS output: video encoder id {EncoderId} rejected by libobs, trying the next candidate", id);
        }

        logger.LogWarning("OBS output: no video encoder candidate was accepted (tried {CandidateIds})", string.Join(", ", candidateIds));
        return IntPtr.Zero;
    }

    /// <summary>Gate held. Single fixed-id audio encoder (ffmpeg_aac, mixer 0).</summary>
    private IntPtr CreateAudioEncoder(string settingsJson, string name)
    {
        IntPtr data = ObsNative.ObsDataCreateFromJson(settingsJson);
        IntPtr encoder = ObsNative.ObsAudioEncoderCreate(AudioEncoderId, name, data, (UIntPtr)0, IntPtr.Zero);
        ObsNative.ObsDataRelease(data);
        if (encoder != IntPtr.Zero)
        {
            logger.LogInformation("OBS output: audio encoder created with id {EncoderId}", AudioEncoderId);
        }
        else
        {
            logger.LogWarning("OBS output: audio encoder id {EncoderId} rejected by libobs", AudioEncoderId);
        }

        return encoder;
    }

    /// <summary>Gate held. obs_output_create + release of the settings object (create takes its own reference). Flags native state as loaded (R3 exit policy).</summary>
    private IntPtr CreateOutput(string id, string name, string settingsJson)
    {
        IntPtr data = ObsNative.ObsDataCreateFromJson(settingsJson);
        IntPtr output = ObsNative.ObsOutputCreate(id, name, data, IntPtr.Zero);
        ObsNative.ObsDataRelease(data);
        if (output != IntPtr.Zero)
        {
            engine.MarkNativeStateLoaded();
            logger.LogInformation("OBS output: output created (id {OutputId}, name {OutputName})", id, name);
        }
        else
        {
            logger.LogWarning("OBS output: output id {OutputId} rejected by libobs", id);
        }

        return output;
    }

    // ------------------------------------------------------------------
    // Stop
    // ------------------------------------------------------------------

    private Task<ObsOutputActionResult> StopOutputAsync(ObsOutputKind kind)
    {
        if (!engine.IsReady)
        {
            return Task.FromResult(Fail($"{Label(kind)} cannot stop: OBS engine is not initialized."));
        }

        return StopCoreAsync(kind);
    }

    /// <summary>
    /// Shared stop path for <see cref="StopOutputAsync"/> (user command) and
    /// <see cref="StopAllAsync"/> (app exit): graceful <c>obs_output_stop</c>
    /// under the gate, bounded drain outside it, <c>obs_output_force_stop</c>
    /// when the output is still active, then release of this kind's native
    /// references. Faults when the engine is not Ready (the caller decides
    /// whether that is a user-facing error or an exit-time swallow).
    /// </summary>
    private async Task<ObsOutputActionResult> StopCoreAsync(ObsOutputKind kind)
    {
        // Gate held only for the stop call itself; the drain loop below sleeps
        // between short gate-held probes so nothing else is blocked.
        IntPtr output = await engine.RunNativeAsync(() =>
        {
            IntPtr ptr = RefsOf(kind)?.Output ?? IntPtr.Zero;
            bool active = ptr != IntPtr.Zero && ObsNative.ObsOutputActive(ptr);
            bool starting = ptr != IntPtr.Zero && StartingFlagOf(kind);
            if (!active && !starting)
            {
                return IntPtr.Zero;
            }

            ClearStartingFlag(kind);
            if (active)
            {
                ObsNative.ObsOutputStop(ptr);
            }

            return ptr;
        }).ConfigureAwait(false);

        if (output == IntPtr.Zero)
        {
            return Fail($"{Label(kind)} is not running.");
        }

        bool forced = false;
        if (!await DrainAsync(output, GracefulDrain).ConfigureAwait(false))
        {
            logger.LogWarning("OBS output: {Kind} still active after {Seconds}s — forcing stop", kind, GracefulDrain.TotalSeconds);
            await engine.RunNativeAsync<object?>(() =>
            {
                ObsNative.ObsOutputForceStop(output);
                return null;
            }).ConfigureAwait(false);
            forced = true;
            await DrainAsync(output, ForcedDrain).ConfigureAwait(false);
        }

        // Release whatever this output's references still are (a concurrent
        // Start may have replaced them — matching refs only).
        await engine.RunNativeAsync<object?>(() =>
        {
            ReleaseIfCurrent(kind, output);
            return null;
        }).ConfigureAwait(false);

        logger.LogInformation("OBS output: {Kind} stopped{Forced}", kind, forced ? " (forced)" : string.Empty);
        return Pass($"{Label(kind)} stopped{(forced ? " (forced after a timeout)" : string.Empty)}.");
    }

    /// <summary>OUTSIDE the gate. Polls obs_output_active in 100 ms steps until inactive or the budget is spent.</summary>
    private async Task<bool> DrainAsync(IntPtr output, TimeSpan budget)
    {
        int steps = Math.Max(1, (int)(budget / PollStep));
        for (int i = 0; i < steps; i++)
        {
            await Task.Delay(PollStep).ConfigureAwait(false);
            if (await engine.RunNativeAsync(() => !ObsNative.ObsOutputActive(output)).ConfigureAwait(false))
            {
                return true;
            }
        }

        return !await engine.RunNativeAsync(() => ObsNative.ObsOutputActive(output)).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Field helpers (call under the gate only)
    // ------------------------------------------------------------------

    private NativeRefs? RefsOf(ObsOutputKind kind) => kind switch
    {
        ObsOutputKind.Stream => streamRefs,
        ObsOutputKind.Record => recordRefs,
        _ => replayRefs,
    };

    private void ClearStartingFlag(ObsOutputKind kind)
    {
        switch (kind)
        {
            case ObsOutputKind.Stream: streamStarting = false; break;
            case ObsOutputKind.Record: recordStarting = false; break;
            default: replayStarting = false; break;
        }
    }

    private bool StartingFlagOf(ObsOutputKind kind) => kind switch
    {
        ObsOutputKind.Stream => streamStarting,
        ObsOutputKind.Record => recordStarting,
        _ => replayStarting,
    };

    private void ReleaseRefs(ObsOutputKind kind)
    {
        switch (kind)
        {
            case ObsOutputKind.Stream:
                ReleaseRefs(streamRefs);
                streamRefs = null;
                streamStarting = false;
                break;
            case ObsOutputKind.Record:
                ReleaseRefs(recordRefs);
                recordRefs = null;
                recordStarting = false;
                break;
            default:
                ReleaseRefs(replayRefs);
                replayRefs = null;
                replayStarting = false;
                break;
        }
    }

    /// <summary>Gate held. Releases this kind's references only if they still point at the stopped output.</summary>
    private void ReleaseIfCurrent(ObsOutputKind kind, IntPtr expectedOutput)
    {
        NativeRefs? refs = RefsOf(kind);
        if (refs is null || refs.Output != expectedOutput)
        {
            return;
        }

        ReleaseRefs(kind);
    }

    /// <summary>Output first (its destroy may touch the encoders), then encoders, then the service.</summary>
    private void ReleaseRefs(NativeRefs? refs)
    {
        if (refs is null)
        {
            return;
        }

        if (refs.Output != IntPtr.Zero)
        {
            ObsNative.ObsOutputRelease(refs.Output);
        }

        if (refs.VideoEncoder != IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(refs.VideoEncoder);
        }

        if (refs.AudioEncoder != IntPtr.Zero)
        {
            ObsNative.ObsEncoderRelease(refs.AudioEncoder);
        }

        if (refs.Service != IntPtr.Zero)
        {
            ObsNative.ObsServiceRelease(refs.Service);
        }
    }

    private static string Label(ObsOutputKind kind) => kind switch
    {
        ObsOutputKind.Stream => "Stream",
        ObsOutputKind.Record => "Recording",
        _ => "Replay buffer",
    };

    private static ObsOutputActionResult Pass(string message) => new(true, message);

    private static ObsOutputActionResult Fail(string message) => new(false, message);
}
