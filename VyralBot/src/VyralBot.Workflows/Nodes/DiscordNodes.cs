using Microsoft.Extensions.DependencyInjection;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Shared plumbing for the Discord node family: bridge resolution, username/scope
/// resolution and the exception-to-FailedResult wrapper. All nodes pass
/// <see cref="ExecutionContext.BotId"/>/<see cref="ExecutionContext.GuildName"/>
/// through so the bridge implementation routes to the right bot/guild.
/// </summary>
internal static partial class DiscordNodeHelper
{
    /// <summary>Scope value of the message that started the run.</summary>
    public const string ScopeTrigger = "trigger";

    /// <summary>Scope value of the bot's most recent sent message in the origin channel.</summary>
    public const string ScopeLastBot = "last_bot";

    /// <summary>Resolves the Discord bridge (null = the node must fail with "Discord bridge unavailable").</summary>
    public static IDiscordWorkflowBridge? ResolveBridge(ExecutionContext ctx) =>
        ctx.Services.GetService<IDiscordWorkflowBridge>();

    /// <summary>Resolves the target username: node config (substituted), falling back to the triggering user.</summary>
    public static string ResolveUsername(WorkflowNode node, ExecutionContext ctx)
    {
        var username = ctx.Substitute(NodeConfigLookup.Get(node, "Username")).Trim();
        if (username.Length == 0)
            username = ctx.Username.Trim();
        return username;
    }

    /// <summary>Resolves a message scope from config, falling back to <paramref name="fallback"/> when unset.</summary>
    public static string ResolveScope(WorkflowNode node, string fallback)
    {
        var scope = NodeConfigLookup.Get(node, "Scope").Trim();
        return scope.Length > 0 ? scope : fallback;
    }

    /// <summary>
    /// Invokes one bridge call, converting a false return or an unexpected exception
    /// into a failed node result (bridge failures are logged by the implementation).
    /// </summary>
    public static async Task<NodeResult> InvokeAsync(string operation, Func<Task<bool>> call, string? output = null)
    {
        try
        {
            var ok = await call().ConfigureAwait(false);
            return ok ? NodeResult.Success(output) : NodeResult.Fail($"{operation} failed (see bot logs)");
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"{operation} failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Sends a rich embed (and optional text) to the origin channel or a channel
/// override. Config: "Embed" (embed JSON from the embed builder), optional
/// "Message" (text content), optional "ChannelOverride" (empty = origin channel).
/// </summary>
public class SendEmbedNode : INode
{
    public string Type => "send_embed";
    public string Category => "Discord";
    public string Title => "Send Embed";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var embedJson = ctx.Substitute(NodeConfigLookup.Get(node, "Embed"));
        var content = ctx.Substitute(NodeConfigLookup.Get(node, "Message"));
        var channelOverride = ctx.Substitute(NodeConfigLookup.Get(node, "ChannelOverride")).Trim();

        if (string.IsNullOrWhiteSpace(embedJson) && string.IsNullOrWhiteSpace(content))
            return Task.FromResult(NodeResult.Fail("No embed JSON or message content configured"));

        var buttonsJson = NodeConfigLookup.Get(node, "Buttons");
        var menuJson = NodeConfigLookup.Get(node, "Menu");
        var hasComponents = !string.IsNullOrWhiteSpace(buttonsJson) || !string.IsNullOrWhiteSpace(menuJson);

        return DiscordNodeHelper.InvokeAsync(
            $"Send embed to '{(channelOverride.Length > 0 ? channelOverride : ctx.Channel)}'",
            () => hasComponents
                ? bridge.SendToChannelWithComponentsAsync(
                    ctx.BotId, ctx.GuildName, channelOverride, content, embedJson,
                    buttonsJson, menuJson, $"vyralwf:{ctx.WorkflowId}:{node.Id}")
                : bridge.SendToChannelAsync(ctx.BotId, ctx.GuildName, channelOverride, content, embedJson),
            content);
    }
}

/// <summary>
/// Direct-messages a user. Config: "Content" ({variable}-substituted), optional
/// "Username" (defaults to the triggering user).
/// </summary>
public class SendDmNode : INode
{
    public string Type => "send_dm";
    public string Category => "Discord";
    public string Title => "Send DM";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var username = DiscordNodeHelper.ResolveUsername(node, ctx);
        if (username.Length == 0)
            return Task.FromResult(NodeResult.Fail("No username (node config or triggering user) to DM"));

        var content = ctx.Substitute(NodeConfigLookup.Get(node, "Content"));
        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(NodeResult.Fail("No message content configured"));

        return DiscordNodeHelper.InvokeAsync(
            $"Send DM to '{username}'",
            () => bridge.SendDmAsync(ctx.BotId, username, content, null),
            content);
    }
}

/// <summary>
/// Grants a role to a user. Config: "Role", optional "Username" (defaults to the
/// triggering user), optional "Reason".
/// </summary>
public class AddRoleNode : INode
{
    public string Type => "add_role";
    public string Category => "Discord";
    public string Title => "Add Role";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        return DiscordNodeHelper.MutateRoleAsync(node, ctx, add: true);
    }
}

/// <summary>
/// Removes a role from a user (see <see cref="AddRoleNode"/> for config).
/// </summary>
public class RemoveRoleNode : INode
{
    public string Type => "remove_role";
    public string Category => "Discord";
    public string Title => "Remove Role";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        return DiscordNodeHelper.MutateRoleAsync(node, ctx, add: false);
    }
}

/// <summary>Shared implementation of <see cref="AddRoleNode"/>/<see cref="RemoveRoleNode"/>.</summary>
internal static partial class DiscordNodeHelper
{
    public static Task<NodeResult> MutateRoleAsync(WorkflowNode node, ExecutionContext ctx, bool add)
    {
        var bridge = ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var role = ctx.Substitute(NodeConfigLookup.Get(node, "Role")).Trim();
        if (role.Length == 0)
            return Task.FromResult(NodeResult.Fail("No role configured"));

        var username = ResolveUsername(node, ctx);
        if (username.Length == 0)
            return Task.FromResult(NodeResult.Fail($"No username (node config or triggering user) to {(add ? "add" : "remove")} the role for"));

        var reason = ctx.Substitute(NodeConfigLookup.Get(node, "Reason"));
        if (string.IsNullOrWhiteSpace(reason))
            reason = $"Workflow: {(add ? "add" : "remove")}_role node";

        return InvokeAsync(
            $"{(add ? "Add" : "Remove")} role '{role}' for '{username}'",
            () => add
                ? bridge.AddRoleAsync(ctx.BotId, username, role, reason)
                : bridge.RemoveRoleAsync(ctx.BotId, username, role, reason),
            role);
    }
}

/// <summary>
/// Creates a channel in the origin guild. Config: "Name", "Type"
/// ("Text"/"Voice"/"Forum", default "Text"), optional "Category".
/// </summary>
public class CreateChannelNode : INode
{
    public string Type => "create_channel";
    public string Category => "Discord";
    public string Title => "Create Channel";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var name = ctx.Substitute(NodeConfigLookup.Get(node, "Name")).Trim();
        if (name.Length == 0)
            return Task.FromResult(NodeResult.Fail("No channel name configured"));

        var channelType = ctx.Substitute(NodeConfigLookup.Get(node, "Type")).Trim();
        if (channelType.Length == 0)
            channelType = "Text";

        var category = ctx.Substitute(NodeConfigLookup.Get(node, "Category")).Trim();

        return DiscordNodeHelper.InvokeAsync(
            $"Create {channelType} channel '{name}'",
            () => bridge.CreateChannelAsync(ctx.BotId, name, channelType, category.Length > 0 ? category : null),
            name);
    }
}

/// <summary>
/// Edits the bot's most recent sent message in the origin channel.
/// Config: "NewContent" ({variable}-substituted).
/// </summary>
public class EditMessageNode : INode
{
    public string Type => "edit_message";
    public string Category => "Discord";
    public string Title => "Edit Bot Message";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var newContent = ctx.Substitute(NodeConfigLookup.Get(node, "NewContent"));
        if (string.IsNullOrWhiteSpace(newContent))
            return Task.FromResult(NodeResult.Fail("No new message content configured"));

        return DiscordNodeHelper.InvokeAsync(
            "Edit the bot's last message",
            () => bridge.EditLastBotMessageAsync(ctx.BotId, newContent, null),
            newContent);
    }
}

/// <summary>
/// Deletes the triggering message or the bot's last sent message.
/// Config: "Scope" ("trigger" or "last_bot", default "last_bot").
/// </summary>
public class DeleteMessageNode : INode
{
    public string Type => "delete_message";
    public string Category => "Discord";
    public string Title => "Delete Message";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var scope = DiscordNodeHelper.ResolveScope(node, DiscordNodeHelper.ScopeLastBot);

        return DiscordNodeHelper.InvokeAsync(
            $"Delete message (scope '{scope}')",
            () => bridge.DeleteMessageAsync(ctx.BotId, scope),
            scope);
    }
}

/// <summary>
/// Adds an emoji reaction to the triggering message or the bot's last sent
/// message. Config: "Emoji", optional "Scope" (default "trigger").
/// </summary>
public class ReactNode : INode
{
    public string Type => "react";
    public string Category => "Discord";
    public string Title => "React to Message";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var emoji = ctx.Substitute(NodeConfigLookup.Get(node, "Emoji")).Trim();
        if (emoji.Length == 0)
            return Task.FromResult(NodeResult.Fail("No emoji configured"));

        var scope = DiscordNodeHelper.ResolveScope(node, DiscordNodeHelper.ScopeTrigger);

        return DiscordNodeHelper.InvokeAsync(
            $"React with {emoji} (scope '{scope}')",
            () => bridge.ReactAsync(ctx.BotId, emoji, scope),
            emoji);
    }
}

/// <summary>
/// Creates a thread from the bot's most recent message in the origin channel.
/// Config: "Name", optional "AutoArchiveMinutes" (default 60).
/// </summary>
public class CreateThreadNode : INode
{
    public const int DefaultAutoArchiveMinutes = 60;

    public string Type => "create_thread";
    public string Category => "Discord";
    public string Title => "Create Thread";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var name = ctx.Substitute(NodeConfigLookup.Get(node, "Name")).Trim();
        if (name.Length == 0)
            return Task.FromResult(NodeResult.Fail("No thread name configured"));

        var minutesRaw = ctx.Substitute(NodeConfigLookup.GetOrDefault(
            node, DefaultAutoArchiveMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), "AutoArchiveMinutes"));
        if (!int.TryParse(minutesRaw, out var autoArchiveMinutes) || autoArchiveMinutes <= 0)
            return Task.FromResult(NodeResult.Fail($"Cannot parse 'AutoArchiveMinutes' value '{minutesRaw}' as a positive integer"));

        return DiscordNodeHelper.InvokeAsync(
            $"Create thread '{name}'",
            () => bridge.CreateThreadAsync(ctx.BotId, name, autoArchiveMinutes),
            name);
    }
}

/// <summary>
/// Locks (denies @everyone SendMessages) or unlocks the origin channel.
/// Config: "Mode" ("Lock"/"Unlock", default "Lock"), optional "Reason".
/// </summary>
public class LockChannelNode : INode
{
    public string Type => "lock_channel";
    public string Category => "Discord";
    public string Title => "Lock / Unlock Channel";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var mode = ctx.Substitute(NodeConfigLookup.Get(node, "Mode")).Trim();
        if (mode.Length == 0)
            mode = "Lock";

        var locked = !mode.Equals("Unlock", StringComparison.OrdinalIgnoreCase);

        var reason = ctx.Substitute(NodeConfigLookup.Get(node, "Reason"));
        if (string.IsNullOrWhiteSpace(reason))
            reason = locked ? "Workflow: lock_channel node" : "Workflow: lock_channel node (unlock)";

        return DiscordNodeHelper.InvokeAsync(
            $"{(locked ? "Lock" : "Unlock")} channel '{ctx.Channel}'",
            () => bridge.SetChannelLockAsync(ctx.BotId, locked, reason),
            mode);
    }
}
