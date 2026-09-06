using Microsoft.Extensions.DependencyInjection;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Sends a chat message through the platform send hook the UI layer installs on
/// the WorkflowRuntime (WorkflowRuntime.MessageSender). Config: "text" (variable-
/// substituted), optional "platform" override (e.g. send a Twitch-triggered
/// workflow's message to Discord).
/// </summary>
public class SendMessageNode : INode
{
    public string Type => "send_message";
    public string Category => "Messaging";
    public string Title => "Send Message";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var text = ctx.Substitute(NodeConfig.Get(node, "text"));
        if (string.IsNullOrWhiteSpace(text))
            return NodeResult.Fail("No message text configured");

        // Interactive components (Buttons/Menu config) are Discord-only: route
        // through the Discord bridge's component-aware send so clicks dispatch
        // back into the workflow via the vyralwf custom ids.
        var buttonsJson = NodeConfig.Get(node, "Buttons");
        var menuJson = NodeConfig.Get(node, "Menu");
        if (!string.IsNullOrWhiteSpace(buttonsJson) || !string.IsNullOrWhiteSpace(menuJson))
        {
            var bridge = ctx.Services.GetService<VyralBot.Workflows.Abstractions.IDiscordWorkflowBridge>();
            if (bridge is null)
                return NodeResult.Fail("Discord bridge unavailable (buttons/menus require Discord)");

            // "ChannelOverride" (mirrors SendEmbedNode) — a channel name, NOT the
            // "platform" key: reading "platform" here sent component messages to
            // a channel literally named "Discord"/"Twitch" when a step saved a
            // platform override alongside Buttons/Menu config.
            var channelOverride = NodeConfig.Get(node, "ChannelOverride").Trim();
            var deliveredComponents = await bridge.SendToChannelWithComponentsAsync(
                ctx.BotId, ctx.GuildName, channelOverride, text, null,
                buttonsJson, menuJson, $"vyralwf:{ctx.WorkflowId}:{node.Id}").ConfigureAwait(false);
            return deliveredComponents
                ? NodeResult.Success(text)
                : NodeResult.Fail("Component message send reported failure");
        }

        var runtime = ctx.Services.GetService<WorkflowRuntime>();
        var sender = runtime?.MessageSender;
        if (sender is null)
            return NodeResult.Fail("No message sender installed (the UI layer must install WorkflowRuntime.MessageSender at startup)");

        // Optional platform override: send to a different platform than the trigger's.
        var platformOverride = NodeConfig.Get(node, "platform").Trim();
        var originalPlatform = ctx.Platform;
        var swapped = false;
        if (!string.IsNullOrWhiteSpace(platformOverride) &&
            !string.Equals(platformOverride, originalPlatform, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Platform = platformOverride;
            swapped = true;
        }

        try
        {
            var delivered = await sender(ctx, text).ConfigureAwait(false);
            return delivered
                ? NodeResult.Success(text)
                : NodeResult.Fail($"Message sender reported failure for platform '{ctx.Platform}'");
        }
        finally
        {
            if (swapped)
                ctx.Platform = originalPlatform;
        }
    }
}
