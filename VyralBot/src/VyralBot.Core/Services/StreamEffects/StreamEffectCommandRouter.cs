using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.Moderation;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.StreamEffects;

/// <summary>Context for one Discord chat message that may be a stream-effect command.</summary>
/// <param name="BotId">Id of the bot instance that received the message.</param>
/// <param name="GuildId">Snowflake id of the guild the message was posted in (null for DMs).</param>
/// <param name="ChannelId">Snowflake id of the channel the message was posted in.</param>
/// <param name="SenderUserId">Discord snowflake id of the sender (mod tier lookup key).</param>
/// <param name="SenderUsername">Display name of the sender (replies + audit log).</param>
/// <param name="Message">Raw message text.</param>
public sealed record StreamEffectCommandContext(
    string BotId,
    string? GuildId,
    string ChannelId,
    string SenderUserId,
    string SenderUsername,
    string Message);

/// <summary>
/// Handles stream-effect commands (flashlights, lightson, lightsoff, streamdeck)
/// typed with an optional !/. prefix in any Discord channel. Gated by
/// <see cref="ModTiers"/> via <see cref="IModTierResolver"/>; publishes a
/// <see cref="StreamEffectEvent"/> on the EventBus and audits every run to the
/// ModActions table (same insert shape as <see cref="ModCommandEngine"/>).
/// </summary>
public sealed class StreamEffectCommandRouter
{
    /// <summary>The stream-effect command verbs, matched case-insensitively.</summary>
    public static readonly string[] Verbs = StreamEffectPayloads.Verbs;

    private const string AuditPrefix = "streamfx:";

    private readonly IModTierResolver _tiers;
    private readonly IEventBus _eventBus;
    private readonly IDataService _data;

    public StreamEffectCommandRouter(IModTierResolver tiers, IEventBus eventBus, IDataService data)
    {
        _tiers = tiers ?? throw new ArgumentNullException(nameof(tiers));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Chat reply when the message is a stream-effect command, or null to fall through to other handlers.</summary>
    public async Task<string?> TryHandleAsync(StreamEffectCommandContext ctx)
    {
        var tokens = (ctx.Message ?? "").Trim().TrimStart('!', '.')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var verb = tokens[0].ToLowerInvariant();
        if (!Verbs.Contains(verb))
        {
            return null;
        }

        var tier = await _tiers.ResolveTierAsync(
            ctx.BotId, ctx.GuildId, ctx.SenderUserId, ctx.SenderUsername).ConfigureAwait(false);
        if (!ModTiers.CanRun(tier, verb))
        {
            return $"⛔ {ctx.SenderUsername}, '{verb}' requires {ModTiers.MinimumTier(verb)} tier or higher.";
        }

        var (effectType, payload) = StreamEffectPayloads.Build(verb, tokens);
        var reply = $"✨ '{verb}' triggered by {ctx.SenderUsername}.";

        // Audit BEFORE publishing so an audit failure can never produce a fired
        // effect with no audit row (the exception propagates like any other).
        await AuditAsync(ctx, verb, reply).ConfigureAwait(false);

        await _eventBus.PublishAsync(new StreamEffectEvent
        {
            Source = "Discord",
            EffectType = effectType,
            Payload = payload,
            RequestedBy = ctx.SenderUsername,
            ChannelId = ctx.ChannelId
        }).ConfigureAwait(false);

        return reply;
    }

    private async Task AuditAsync(StreamEffectCommandContext ctx, string verb, string reply)
    {
        // Mirrors ModCommandEngine's audit insert: ModUsername/ModUserId from the
        // caller, the channel as the target argument, Details = the chat reply;
        // Timestamp defaults to UtcNow on the entity.
        await _data.SaveModActionAsync(new ModAction
        {
            ModUsername = ctx.SenderUsername ?? "",
            ModUserId = ctx.SenderUserId ?? "",
            TargetUsername = ctx.ChannelId,
            TargetUserId = "",
            Action = AuditPrefix + verb,
            Details = reply
        }).ConfigureAwait(false);
    }
}
