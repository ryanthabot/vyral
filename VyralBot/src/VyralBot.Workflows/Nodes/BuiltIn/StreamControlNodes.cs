using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Switches the OBS program scene via the stream-control bridge (obs-websocket,
/// same seam the VyralDeck OBS buttons use). Config: "SceneName"
/// (variable-substituted, e.g. "Gameplay"). A missing name, OBS being offline or
/// an unknown scene fails the node so the error branch can route around it.
/// </summary>
public class ObsSceneNode : INode
{
    public string Type => "obs_scene";
    public string Category => "Streaming";
    public string Title => "Switch OBS Scene";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var scene = ctx.Substitute(NodeConfig.Get(node, "SceneName")).Trim();
        if (string.IsNullOrWhiteSpace(scene))
            return NodeResult.Fail("No scene name configured");

        return await StreamControlNodeHelper.RunAsync(ctx, "obs_scene",
            bridge => bridge.SetSceneAsync(scene)).ConfigureAwait(false);
    }
}

/// <summary>
/// Starts or stops the OBS stream output via the stream-control bridge.
/// Config: "Action" — "Start" (default) or "Stop", variable-substituted so
/// <c>{args}</c>-driven flows work. Failure (OBS offline, remote control
/// disabled, already live/stopped) takes the error branch.
/// </summary>
public class ObsStreamNode : INode
{
    public string Type => "obs_stream";
    public string Category => "Streaming";
    public string Title => "OBS Stream Start/Stop";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var action = ctx.Substitute(NodeConfig.Get(node, "Action", "Start")).Trim();

        return action.ToLowerInvariant() switch
        {
            "start" => await StreamControlNodeHelper.RunAsync(ctx, "obs_stream:start",
                bridge => bridge.StartStreamAsync()).ConfigureAwait(false),
            "stop" => await StreamControlNodeHelper.RunAsync(ctx, "obs_stream:stop",
                bridge => bridge.StopStreamAsync()).ConfigureAwait(false),
            _ => NodeResult.Fail($"Unknown OBS stream action '{action}' (use Start or Stop)")
        };
    }
}

/// <summary>
/// Saves the OBS replay buffer highlight via the stream-control bridge.
/// No config. Fails with a helpful message when the replay buffer output is not
/// running (it must be enabled and active in OBS first), so the error branch
/// can route around it.
/// </summary>
public class ObsReplayNode : INode
{
    public string Type => "obs_replay";
    public string Category => "Streaming";
    public string Title => "Save OBS Replay Buffer";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        StreamControlNodeHelper.RunAsync(ctx, "obs_replay",
            bridge => bridge.SaveReplayBufferAsync());
}

/// <summary>
/// Creates a clip of the live Twitch stream (Helix) via the stream-control
/// bridge. No config. Fails with a message when Twitch is not connected or the
/// stream is offline, so the error branch can route around it.
/// </summary>
public class TwitchClipNode : INode
{
    public string Type => "twitch_clip";
    public string Category => "Streaming";
    public string Title => "Create Twitch Clip";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        StreamControlNodeHelper.RunAsync(ctx, "twitch_clip",
            bridge => bridge.CreateTwitchClipAsync());
}

/// <summary>
/// Shared runner for the stream-control nodes: resolves the bridge, executes one
/// action and maps the outcome onto <see cref="NodeResult"/> without ever
/// throwing. Run-level cancellation is rethrown so the runtime records
/// "Cancelled" (see <see cref="WaitNode"/>'s cancellation handling).
/// </summary>
internal static class StreamControlNodeHelper
{
    public static async Task<NodeResult> RunAsync(
        ExecutionContext ctx,
        string what,
        Func<IStreamControlWorkflowBridge, Task<StreamControlOutcome>> action)
    {
        var bridge = ctx.Services.GetService<IStreamControlWorkflowBridge>();
        if (bridge is null)
            return NodeResult.Fail($"{what}: stream control is not available (IStreamControlWorkflowBridge is not registered)");

        try
        {
            var outcome = await action(bridge).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                ctx.Logger.LogInformation("[workflow] {What} failed: {Message}", what, outcome.Message);
                return NodeResult.Fail(outcome.Message);
            }

            ctx.Logger.LogInformation("[workflow] {What}: {Message}", what, outcome.Message);
            return NodeResult.Success(outcome.Message);
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            // Run-level cancellation: rethrow so the runtime records "Cancelled"
            // instead of routing the error branch / error handler on a dying run.
            throw;
        }
        catch (Exception ex)
        {
            ctx.Logger.LogInformation("[workflow] {What} failed: {Error}", what, ex.Message);
            return NodeResult.Fail($"{what} failed: {ex.Message}");
        }
    }
}
