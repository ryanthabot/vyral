namespace VyralBot.Integrations.Restream;

/// <summary>Where the restream ingest listens: loopback only, or all interfaces for a two-PC setup.</summary>
public enum RestreamOperationMode
{
    /// <summary>OBS runs on this PC and publishes to rtmp://127.0.0.1 (loopback bind).</summary>
    SinglePC = 0,

    /// <summary>A gaming PC publishes over the LAN (bind 0.0.0.0 so remote machines can reach the ingest).</summary>
    TwoPC = 1,
}

/// <summary>Output resolution ladder. <see cref="Source"/> relays without re-encoding.</summary>
public enum RestreamResolution
{
    Source = 0,
    P1080 = 1,
    P936 = 2,
    P720 = 3,
    P540 = 4,
    P360 = 5,
}

/// <summary>Audio handling for a destination. Copy is free; re-encoding trades CPU for a lower audio bitrate.</summary>
public enum RestreamAudioMode
{
    Copy = 0,
    A128k = 1,
    A160k = 2,
}

/// <summary>Immutable source-side configuration: where the local RTMP ingest listens.</summary>
/// <param name="Mode">Loopback vs all-interfaces bind.</param>
/// <param name="IngestPort">Local RTMP port FFmpeg listens on (default 1935).</param>
/// <param name="IngestStreamKey">Stream key OBS must publish with (FFmpeg's listen mode matches app/key exactly).</param>
public sealed record RestreamSourceConfig(
    RestreamOperationMode Mode,
    int IngestPort,
    string IngestStreamKey);

/// <summary>One platform destination with its own resolution/bitrate budget.</summary>
/// <param name="Id">Stable id (GUID string) used to map runtime health back onto the row.</param>
/// <param name="Name">User-facing label, e.g. "Twitch 720p".</param>
/// <param name="Platform">Twitch / YouTube / Kick / Custom (label only; the server URL is what matters).</param>
/// <param name="ServerUrl">RTMP base URL WITHOUT the stream key.</param>
/// <param name="StreamKey">Platform stream key — secret; masked in every log surface.</param>
/// <param name="Resolution">Target resolution; Source = pass-through (-c copy, no re-encode).</param>
/// <param name="VideoBitrateKbps">x264 average/max bitrate for scaled outputs.</param>
/// <param name="EncoderPreset">x264 preset: ultrafast … faster.</param>
/// <param name="AudioMode">Copy the source AAC or re-encode at a fixed bitrate.</param>
/// <param name="Enabled">Disabled rows are excluded from the session entirely.</param>
public sealed record RestreamDestination(
    string Id,
    string Name,
    string Platform,
    string ServerUrl,
    string StreamKey,
    RestreamResolution Resolution,
    int VideoBitrateKbps,
    string EncoderPreset,
    RestreamAudioMode AudioMode,
    bool Enabled);

/// <summary>Everything the engine needs to run one session.</summary>
public sealed record RestreamSessionConfig(
    string FfmpegPath,
    RestreamSourceConfig Source,
    IReadOnlyList<RestreamDestination> Destinations);

public enum RestreamSessionState
{
    /// <summary>No session.</summary>
    Idle = 0,

    /// <summary>FFmpeg is listening; waiting for OBS to connect and start publishing.</summary>
    WaitingForSource = 1,

    /// <summary>A publisher is connected and the outputs are being fed.</summary>
    Streaming = 2,

    /// <summary>Terminal error (FFmpeg missing, port in use, no destinations) — the session ended.</summary>
    Faulted = 3,
}

/// <summary>Per-destination health inside a running session.</summary>
public enum RestreamDestinationHealth
{
    /// <summary>Session live but this output has not been confirmed yet.</summary>
    Pending = 0,

    /// <summary>Publisher connected; output is being written.</summary>
    Active = 1,

    /// <summary>FFmpeg reported an error naming this destination's URL/key.</summary>
    Error = 2,

    /// <summary>Auto-disabled after repeated failures so the other outputs can keep streaming.</summary>
    AutoDisabled = 3,
}

/// <summary>Aggregate live stats parsed from FFmpeg's machine-readable progress output.</summary>
public sealed record RestreamStats(double Fps, double BitrateKbps, double SpeedX, long DropFrames);

/// <summary>Runtime health of one destination inside a session.</summary>
public sealed record RestreamDestinationRuntime(string Id, RestreamDestinationHealth Health, string? Detail);

/// <summary>Immutable engine status snapshot (raised through StatusChanged).</summary>
public sealed record RestreamEngineStatus(
    RestreamSessionState State,
    string Message,
    RestreamStats? Stats,
    IReadOnlyList<RestreamDestinationRuntime> Destinations,
    DateTimeOffset? StreamingSince)
{
    public static readonly RestreamEngineStatus Idle =
        new(RestreamSessionState.Idle, "stopped", null, Array.Empty<RestreamDestinationRuntime>(), null);
}
