using Microsoft.Extensions.DependencyInjection;
using VyralBot.Core.Services.Audio;
using VyralBot.UI.Services;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using VyralBot.Workflows.Persistence;
using Xunit;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave 10 coverage for the <c>play_sound</c> workflow block: the node resolves
/// <see cref="ISoundService"/> from the execution context and plays a named
/// sound file at a clamped 0-100 volume, the <see cref="BlockCatalog"/> entry
/// carries SoundName/Volume params, and the Sub Appreciation template still
/// parses (it now plays subscribe.wav instead of the wait+second-message hack).
/// </summary>
public class WorkflowPlaySoundWave10Tests
{
    private static WorkflowNode Node(string type, Dictionary<string, string> config) =>
        new() { Id = "n1", Type = type, Config = config };

    private static (ServiceProvider Provider, FakeSoundService Sounds) Harness()
    {
        var sounds = new FakeSoundService();
        var services = new ServiceCollection();
        services.AddSingleton<ISoundService>(sounds);
        return (services.BuildServiceProvider(), sounds);
    }

    [Fact]
    public async Task Wave10_PlaySoundNode_PlaysNamedSound_AtConfiguredVolume()
    {
        var (provider, sounds) = Harness();
        await using var lease = provider;
        sounds.Existing.Add("subscribe.wav");
        var ctx = new ExecutionContext(provider);

        var result = await new PlaySoundNode().ExecuteAsync(
            Node("play_sound", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoundName"] = "subscribe.wav",
                ["Volume"] = "80"
            }), ctx);

        Assert.True(result.Succeeded, result.Error);
        var call = Assert.Single(sounds.Plays);
        Assert.Equal("subscribe.wav", call.Name);
        Assert.Equal(0.8, (double)call.Volume, 3); // 80 (0-100) → 0.8 (0..1)
    }

    [Fact]
    public async Task Wave10_PlaySoundNode_ClampsVolume_AndDefaultsToFull()
    {
        var (provider, sounds) = Harness();
        await using var lease = provider;
        sounds.Existing.Add("airhorn.mp3");
        var node = new PlaySoundNode();
        var ctx = new ExecutionContext(provider);

        var clamped = await node.ExecuteAsync(
            Node("play_sound", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoundName"] = "airhorn.mp3",
                ["Volume"] = "150" // above 100 clamps to 100
            }), ctx);
        Assert.True(clamped.Succeeded, clamped.Error);
        Assert.Equal(1.0, (double)Assert.Single(sounds.Plays).Volume, 3);

        sounds.Plays.Clear();
        var defaulted = await node.ExecuteAsync(
            Node("play_sound", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoundName"] = "airhorn.mp3" // no Volume → default 100
            }), ctx);
        Assert.True(defaulted.Succeeded, defaulted.Error);
        Assert.Equal(1.0, (double)Assert.Single(sounds.Plays).Volume, 3);
    }

    [Fact]
    public async Task Wave10_PlaySoundNode_Fails_WhenSoundNotInLibrary()
    {
        var (provider, sounds) = Harness();
        await using var lease = provider;
        var ctx = new ExecutionContext(provider);

        var result = await new PlaySoundNode().ExecuteAsync(
            Node("play_sound", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoundName"] = "missing.wav"
            }), ctx);

        Assert.False(result.Succeeded);
        Assert.Contains("missing.wav", result.Error, StringComparison.Ordinal);
        Assert.Empty(sounds.Plays); // never handed to the service
    }

    [Fact]
    public async Task Wave10_PlaySoundNode_Fails_WithoutService_OrSoundName()
    {
        // No ISoundService registered → failed result, never a throw.
        var empty = new ServiceCollection().BuildServiceProvider();
        await using var emptyLease = empty;

        var noService = await new PlaySoundNode().ExecuteAsync(
            Node("play_sound", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SoundName"] = "subscribe.wav"
            }), new ExecutionContext(empty));
        Assert.False(noService.Succeeded);

        var (provider, sounds) = Harness();
        await using var lease = provider;
        sounds.Existing.Add("subscribe.wav");

        var noName = await new PlaySoundNode().ExecuteAsync(
            Node("play_sound", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new ExecutionContext(provider));
        Assert.False(noName.Succeeded);
        Assert.Empty(sounds.Plays);
    }

    [Fact]
    public void Wave10_BlockCatalog_AndNodeRegistry_ExposePlaySound()
    {
        var block = BlockCatalog.Find("play_sound");
        Assert.NotNull(block);
        Assert.Equal(BlockGroup.Action, block!.Group);
        Assert.False(block.IsConditional); // plain action: a single grey Next port
        Assert.Contains(BlockCatalog.GetOutputs("play_sound"), p => p.Key == BlockCatalog.PortNext);

        var soundName = Assert.Single(block.Params, p => p.Key == "SoundName");
        Assert.Equal(ParamKind.Text, soundName.Kind); // free text + hint (no dynamic sound dropdown kind exists)
        Assert.NotNull(soundName.Hint);
        var volume = Assert.Single(block.Params, p => p.Key == "Volume");
        Assert.Equal(ParamKind.Number, volume.Kind);
        Assert.Equal("100", volume.DefaultValue);

        var registry = new NodeRegistry();
        BuiltInNodes.RegisterAll(registry);
        Assert.NotNull(registry.Get("play_sound"));
    }

    [Fact]
    public void Wave10_SubAppreciation_Template_Parses_AndImportsDisabled()
    {
        var template = WorkflowTemplates.Templates.First(t => t.Key == "sub-appreciation");

        var graph = WorkflowJson.FromGraphData(template.GraphJson); // parse test: JSON stays valid
        Assert.Contains(graph.Nodes, n => n.Type == "play_sound");

        var workflow = WorkflowTemplates.CreateWorkflow(template); // import path
        Assert.NotNull(workflow);
        Assert.False(workflow!.Enabled);
        var soundNode = Assert.Single(workflow.Nodes, n => n.Type == "play_sound");
        Assert.Equal("subscribe.wav", soundNode.Config["SoundName"]);
    }

    /// <summary>In-memory ISoundService: records plays, membership-driven file existence.</summary>
    private sealed class FakeSoundService : ISoundService
    {
        public List<(string Name, float Volume)> Plays { get; } = new();
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<List<string>> GetSoundFilesAsync() =>
            Task.FromResult(Existing.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList());

        public Task<string> UploadSoundAsync(string sourceFilePath, string? fileName = null) =>
            throw new NotSupportedException("not used by play_sound tests");

        public Task<bool> DeleteSoundAsync(string fileName) =>
            throw new NotSupportedException("not used by play_sound tests");

        public Task PlayEventSoundAsync(string eventType, string platform) =>
            throw new NotSupportedException("not used by play_sound tests");

        public Task PlaySoundFileAsync(string soundFile, float volume = 1.0f)
        {
            Plays.Add((soundFile, volume));
            return Task.CompletedTask;
        }

        public bool SoundFileExists(string fileName) => Existing.Contains(fileName);

        public string GetSoundsDirectory() => "fixtures/sounds";

        public Task InitializeDefaultSoundsAsync() => Task.CompletedTask;
    }
}
