namespace VyralBot.Workflows.Abstractions;

/// <summary>
/// Outcome of one stream-control action: <see cref="Ok"/> plus a human message
/// that doubles as the node's failure reason / success output. Mirrors the
/// non-throwing <c>TwitchActionResponse</c> contract the platform services use.
/// </summary>
public sealed record StreamControlOutcome(bool Ok, string Message);

/// <summary>
/// Stream-control capability bridge consumed by the OBS / clip workflow nodes
/// (<c>obs_scene</c>, <c>obs_stream</c>, <c>obs_replay</c>, <c>twitch_clip</c>).
/// Same seam as <see cref="IDiscordWorkflowBridge"/>: the engine has no
/// Integration dependencies, so the UI layer registers an implementation that
/// adapts over the OBS remote bridge (obs-websocket client, VyralDeck's
/// <c>ObsRemoteBridge</c> pattern) and the Twitch Helix clip call
/// (<c>IStreamActionsService.TwitchCreateClipAsync</c> /
/// <c>ITwitchService.CreateClipAsync</c>).
/// Nodes resolve it lazily from <see cref="Runtime.ExecutionContext"/>.Services
/// and must treat a missing registration as a failed node result, never a
/// throw. Implementations are best-effort: they return
/// <see cref="StreamControlOutcome"/> with <see cref="Ok"/> false (and a helpful
/// message — OBS not running, remote control disabled, replay buffer not
/// active, Twitch disconnected/offline) instead of throwing, so flows can route
/// the error branch.
/// </summary>
public interface IStreamControlWorkflowBridge
{
    /// <summary>Switches the OBS program scene. Fails with a message when OBS remote control is unavailable or the scene does not exist.</summary>
    Task<StreamControlOutcome> SetSceneAsync(string sceneName);

    /// <summary>Starts the OBS stream output.</summary>
    Task<StreamControlOutcome> StartStreamAsync();

    /// <summary>Stops the OBS stream output.</summary>
    Task<StreamControlOutcome> StopStreamAsync();

    /// <summary>
    /// Saves the current OBS replay buffer. Fails with a helpful message when
    /// the replay buffer output is not running (it must be enabled and active
    /// in OBS first).
    /// </summary>
    Task<StreamControlOutcome> SaveReplayBufferAsync();

    /// <summary>
    /// Creates a clip of the live Twitch stream via Helix. Fails with a message
    /// when Twitch is not connected or the stream is offline.
    /// </summary>
    Task<StreamControlOutcome> CreateTwitchClipAsync();
}
