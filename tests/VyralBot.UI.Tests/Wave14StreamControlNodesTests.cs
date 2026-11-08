using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-14 coverage for the stream-control workflow nodes
/// (<see cref="ObsSceneNode"/>, <see cref="ObsStreamNode"/>,
/// <see cref="ObsReplayNode"/>, <see cref="TwitchClipNode"/>) and their
/// plumbing: the BlockCatalog palette entries (obs_scene / obs_stream /
/// obs_replay / twitch_clip), the BuiltInNodes.RegisterAll registry mapping,
/// the fake-bridge success/failure outcomes (Ok=true → node success with the
/// bridge message; Ok=false → NodeResult.Fail carrying the bridge message),
/// the missing-bridge fail-not-throw contract, and the per-node config
/// validation (empty scene name, unknown stream action) plus {variable}
/// substitution into the scene name.
/// </summary>
public class Wave14StreamControlNodesTests
{
    private static readonly string[] StreamControlTypes =
        ["obs_scene", "obs_stream", "obs_replay", "twitch_clip"];

    private static WorkflowNode Node(string type, params (string Key, string Value)[] config)
    {
        var node = new WorkflowNode { Type = type };
        foreach (var (key, value) in config)
        {
            node.Config[key] = value;
        }
        return node;
    }

    /// <summary>An ExecutionContext whose DI provider contains only the bridge (or nothing).</summary>
    private static ExecutionContext Context(IStreamControlWorkflowBridge? bridge)
    {
        var services = new ServiceCollection();
        if (bridge is not null)
        {
            services.AddSingleton(bridge);
        }
        return new ExecutionContext(services.BuildServiceProvider(), NullLogger.Instance);
    }

    // ── catalog + registry plumbing ────────────────────────────────────

    [Theory]
    [InlineData("obs_scene", "Switch OBS Scene")]
    [InlineData("obs_stream", "OBS Stream Start/Stop")]
    [InlineData("obs_replay", "Save OBS Replay Buffer")]
    [InlineData("twitch_clip", "Create Twitch Clip")]
    public void Catalog_ContainsStreamControlBlocks_InStreamingSection(string type, string title)
    {
        var block = BlockCatalog.Find(type);

        Assert.NotNull(block);
        Assert.Equal(title, block!.Title);
        Assert.Equal(BlockGroup.Action, block.Group);
        Assert.Equal(BlockSections.Streaming, block.Section);
    }

    [Fact]
    public void Catalog_SceneAndStreamBlocks_ExposeTheirConfigParams()
    {
        var scene = BlockCatalog.Find("obs_scene")!;
        var stream = BlockCatalog.Find("obs_stream")!;

        Assert.Contains(scene.Params, p => p.Key == "SceneName" && p.Kind == ParamKind.Text);
        var action = Assert.Single(stream.Params, p => p.Key == "Action");
        Assert.Equal(ParamKind.Select, action.Kind);
        Assert.Equal(new[] { "Start", "Stop" }, action.Choices);
        Assert.Equal("Start", action.DefaultValue);
    }

    [Fact]
    public void Registry_RegisterAll_MapsAllFourStreamControlTypes()
    {
        var registry = new NodeRegistry();
        BuiltInNodes.RegisterAll(registry);

        Assert.IsType<ObsSceneNode>(registry.Get("obs_scene"));
        Assert.IsType<ObsStreamNode>(registry.Get("obs_stream"));
        Assert.IsType<ObsReplayNode>(registry.Get("obs_replay"));
        Assert.IsType<TwitchClipNode>(registry.Get("twitch_clip"));

        foreach (var type in StreamControlTypes)
        {
            var node = registry.Get(type);
            Assert.NotNull(node);
            Assert.Equal("Streaming", node!.Category);
            Assert.False(string.IsNullOrWhiteSpace(node.Title));
        }
    }

    // ── bridge Ok=true → node success with the bridge message ─────────

    [Fact]
    public async Task ObsScene_WithBridgeOk_SucceedsAndSwitchesScene()
    {
        var bridge = new Wave14StreamControlBridge { Outcome = new(true, "Switched to Gameplay") };

        var result = await new ObsSceneNode().ExecuteAsync(
            Node("obs_scene", ("SceneName", "Gameplay")), Context(bridge));

        Assert.True(result.Succeeded);
        Assert.Equal("Switched to Gameplay", result.OutputValue);
        Assert.Equal(new[] { "scene:Gameplay" }, bridge.Calls);
    }

    [Fact]
    public async Task ObsStream_DefaultAction_StartsTheStream()
    {
        var bridge = new Wave14StreamControlBridge();

        var result = await new ObsStreamNode().ExecuteAsync(Node("obs_stream"), Context(bridge));

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "stream:start" }, bridge.Calls);
    }

    [Theory]
    [InlineData("Start", "stream:start")]
    [InlineData("start", "stream:start")]
    [InlineData("Stop", "stream:stop")]
    public async Task ObsStream_ActionSelectsStartOrStop(string action, string expectedCall)
    {
        var bridge = new Wave14StreamControlBridge();

        var result = await new ObsStreamNode().ExecuteAsync(
            Node("obs_stream", ("Action", action)), Context(bridge));

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { expectedCall }, bridge.Calls);
    }

    [Fact]
    public async Task ObsReplay_WithBridgeOk_Succeeds()
    {
        var bridge = new Wave14StreamControlBridge { Outcome = new(true, "Replay saved") };

        var result = await new ObsReplayNode().ExecuteAsync(Node("obs_replay"), Context(bridge));

        Assert.True(result.Succeeded);
        Assert.Equal("Replay saved", result.OutputValue);
        Assert.Equal(new[] { "replay" }, bridge.Calls);
    }

    [Fact]
    public async Task TwitchClip_WithBridgeOk_Succeeds()
    {
        var bridge = new Wave14StreamControlBridge { Outcome = new(true, "Clip created") };

        var result = await new TwitchClipNode().ExecuteAsync(Node("twitch_clip"), Context(bridge));

        Assert.True(result.Succeeded);
        Assert.Equal("Clip created", result.OutputValue);
        Assert.Equal(new[] { "clip" }, bridge.Calls);
    }

    // ── bridge Ok=false → NodeResult.Fail with the bridge message ─────

    [Fact]
    public async Task BridgeNotOk_FailsTheNode_WithTheBridgeMessage()
    {
        var bridge = new Wave14StreamControlBridge
        {
            Outcome = new(false, "OBS is not running")
        };

        var scene = await new ObsSceneNode().ExecuteAsync(
            Node("obs_scene", ("SceneName", "Gameplay")), Context(bridge));
        var stream = await new ObsStreamNode().ExecuteAsync(Node("obs_stream"), Context(bridge));
        var replay = await new ObsReplayNode().ExecuteAsync(Node("obs_replay"), Context(bridge));
        var clip = await new TwitchClipNode().ExecuteAsync(Node("twitch_clip"), Context(bridge));

        foreach (var result in new[] { scene, stream, replay, clip })
        {
            Assert.False(result.Succeeded);
            Assert.Equal("OBS is not running", result.Error);
        }
        Assert.Equal(new[] { "scene:Gameplay", "stream:start", "replay", "clip" }, bridge.Calls);
    }

    // ── missing bridge → fail, never throw ────────────────────────────

    [Fact]
    public async Task MissingBridge_FailsWithoutThrowing_AllFourNodes()
    {
        // No IStreamControlWorkflowBridge in the provider.
        var ctx = Context(null);

        var scene = await new ObsSceneNode().ExecuteAsync(
            Node("obs_scene", ("SceneName", "Gameplay")), ctx);
        var stream = await new ObsStreamNode().ExecuteAsync(Node("obs_stream"), ctx);
        var replay = await new ObsReplayNode().ExecuteAsync(Node("obs_replay"), ctx);
        var clip = await new TwitchClipNode().ExecuteAsync(Node("twitch_clip"), ctx);

        foreach (var result in new[] { scene, stream, replay, clip })
        {
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Error);
            Assert.Contains("IStreamControlWorkflowBridge is not registered", result.Error);
        }
    }

    // ── config validation ──────────────────────────────────────────────

    [Fact]
    public async Task ObsScene_EmptySceneName_FailsWithoutTouchingTheBridge()
    {
        var bridge = new Wave14StreamControlBridge();

        var result = await new ObsSceneNode().ExecuteAsync(Node("obs_scene"), Context(bridge));

        Assert.False(result.Succeeded);
        Assert.Equal("No scene name configured", result.Error);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task ObsStream_UnknownAction_FailsWithoutTouchingTheBridge()
    {
        var bridge = new Wave14StreamControlBridge();

        var result = await new ObsStreamNode().ExecuteAsync(
            Node("obs_stream", ("Action", "Pause")), Context(bridge));

        Assert.False(result.Succeeded);
        Assert.Equal("Unknown OBS stream action 'Pause' (use Start or Stop)", result.Error);
        Assert.Empty(bridge.Calls);
    }

    // ── variable substitution ──────────────────────────────────────────

    [Fact]
    public async Task ObsScene_SceneNameSupportsVariables()
    {
        var bridge = new Wave14StreamControlBridge();
        var ctx = Context(bridge);
        ctx.Channel = "vyralhq";

        await new ObsSceneNode().ExecuteAsync(
            Node("obs_scene", ("SceneName", "{channel} - Gameplay")), ctx);

        Assert.Equal(new[] { "scene:vyralhq - Gameplay" }, bridge.Calls);
    }
}
