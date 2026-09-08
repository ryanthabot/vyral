namespace VyralBot.Integrations.Discord;

/// <summary>
/// A text channel within a Discord guild, identified by its snowflake ID.
/// </summary>
public record DiscordChannelInfo(string Id, string Name);

/// <summary>A guild role for the mod role→tier mapping picker.</summary>
public record DiscordRoleInfo(string Id, string Name, int Position, uint ColorRaw, bool Managed);

/// <summary>
/// Snapshot of a Discord guild (server) the bot is currently connected to.
/// <see cref="MemberCount"/> is approximate (Discord reports it with the
/// guild download) and <see cref="TextChannels"/> contains every text channel
/// visible to the bot at snapshot time.
/// </summary>
public record DiscordGuildInfo(string Id, string Name, int MemberCount, IReadOnlyList<DiscordChannelInfo> TextChannels);
