using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.Core.Services.Audio;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Plays a sound from the Sounds library via the Core <see cref="ISoundService"/>.
/// Config: "SoundName" (file name as listed on the Audio &amp; Sounds page,
/// variable-substituted, e.g. "subscribe.wav"), optional "Volume" (0-100,
/// default 100 — mapped onto the service's 0..1 float).
/// Playback honors the streamer/stream sound settings; a missing sound fails
/// the node so the error branch can route around it (see WorkflowRuntime).
/// </summary>
public class PlaySoundNode : INode
{
    public string Type => "play_sound";
    public string Category => "Utility";
    public string Title => "Play Sound";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var sounds = ctx.Services.GetService<ISoundService>();
        if (sounds is null)
            return NodeResult.Fail("ISoundService is not available");

        var name = ctx.Substitute(NodeConfig.Get(node, "SoundName")).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return NodeResult.Fail("No sound name configured");

        // Volume is configured as 0-100 (block default 100); PlaySoundFileAsync
        // takes a 0..1 float. Out-of-range or unparsable values clamp to the
        // nearest end / fall back to full volume rather than failing the flow.
        var volumeRaw = ctx.Substitute(NodeConfig.Get(node, "Volume", "100"));
        var volumePercent = int.TryParse(volumeRaw, out var parsed)
            ? Math.Clamp(parsed, 0, 100)
            : 100;

        // By-name playback contract: PlaySoundFileAsync logs-and-returns when
        // the file is missing (it cannot report failure), so existence is
        // checked here to give flows a real, routable failure.
        if (!sounds.SoundFileExists(name))
        {
            ctx.Logger.LogInformation(
                "[workflow] play_sound: '{SoundName}' is not in the sounds library ({SoundsDirectory})",
                name, sounds.GetSoundsDirectory());
            return NodeResult.Fail($"Sound '{name}' not found in the sounds library");
        }

        try
        {
            await sounds.PlaySoundFileAsync(name, volumePercent / 100f).ConfigureAwait(false);
            return NodeResult.Success(name);
        }
        catch (Exception ex)
        {
            ctx.Logger.LogInformation("[workflow] play_sound: playing '{SoundName}' failed: {Error}", name, ex.Message);
            return NodeResult.Fail($"Playing sound '{name}' failed: {ex.Message}");
        }
    }
}
