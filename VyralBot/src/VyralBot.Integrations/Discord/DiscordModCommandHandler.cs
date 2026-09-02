namespace VyralBot.Integrations.Discord;

/// <summary>
/// Built-in Discord moderation command engine. Parses plain, '!'- or '.'-prefixed
/// chat messages (kick, ban, unban, mute, unmute, slowmode, purge/clear, help) and
/// dispatches them to the <see cref="IDiscordService"/> of the bot whose gateway
/// received the message.
/// </summary>
/// <remarks>
/// <para>
/// Commands (all names case-insensitive, prefix optional):
/// kick &lt;@user|userId&gt; [reason] · ban &lt;@user|userId&gt; [reason] ·
/// unban &lt;userId&gt; · mute &lt;@user|userId&gt; &lt;minutes&gt; [reason] ·
/// unmute &lt;@user|userId&gt; · slowmode &lt;seconds&gt; (or slowmode &lt;#channel|channelId&gt; &lt;seconds&gt;) ·
/// purge &lt;1-100&gt; (alias: clear) · help.
/// </para>
/// <para>
/// "help" is available to everyone; every other command requires the caller to be
/// a moderator or the owner. User mentions in the forms &lt;@123&gt;, &lt;@!123&gt; and raw
/// IDs are accepted; channel mentions as &lt;#123&gt; or raw IDs. This handler never
/// throws — every outcome is a chat-ready string.
/// </para>
/// <para>
/// Stateless and dependency-free so it can be instantiated anywhere (no DI
/// registration required).
/// </para>
/// </remarks>
public class DiscordModCommandHandler : IDiscordCommandHandler
{
    private const int MaxTimeoutMinutes = 28 * 24 * 60; // Discord's 28-day timeout cap
    private const int MaxSlowmodeSeconds = 21600;       // 6 hours, Discord's cap
    private const int MaxPurgeCount = 100;              // Discord bulk-delete page cap

    /// <inheritdoc />
    public async Task<DiscordCommandResult> HandleAsync(
        IDiscordService bot,
        string commandLine,
        bool callerIsModerator,
        bool callerIsOwner,
        string? channelId = null)
    {
        if (bot == null || string.IsNullOrWhiteSpace(commandLine))
        {
            return DiscordCommandResult.NotHandled;
        }

        var tokens = Tokenize(commandLine);
        if (tokens.Count == 0)
        {
            return DiscordCommandResult.NotHandled;
        }

        var command = tokens[0].ToLowerInvariant();
        var args = tokens.Skip(1).ToList();

        // Only the exact command names below are claimed; anything else falls
        // through as a regular chat message.
        switch (command)
        {
            case "help":
                return DiscordCommandResult.Reply(BuildHelpText());
            case "kick":
            case "ban":
            case "unban":
            case "mute":
            case "unmute":
            case "slowmode":
            case "purge":
            case "clear":
                break;
            default:
                return DiscordCommandResult.NotHandled;
        }

        if (!callerIsModerator && !callerIsOwner)
        {
            return DiscordCommandResult.Reply("You don't have permission to use moderation commands.");
        }

        try
        {
            return command switch
            {
                "kick" => await HandleKickAsync(bot, args, channelId).ConfigureAwait(false),
                "ban" => await HandleBanAsync(bot, args, channelId).ConfigureAwait(false),
                "unban" => await HandleUnbanAsync(bot, args, channelId).ConfigureAwait(false),
                "mute" => await HandleMuteAsync(bot, args, channelId).ConfigureAwait(false),
                "unmute" => await HandleUnmuteAsync(bot, args, channelId).ConfigureAwait(false),
                "slowmode" => await HandleSlowmodeAsync(bot, args, channelId).ConfigureAwait(false),
                "purge" or "clear" => await HandlePurgeAsync(bot, args, channelId).ConfigureAwait(false),
                _ => DiscordCommandResult.NotHandled
            };
        }
        catch (Exception)
        {
            return DiscordCommandResult.Reply("❌ Something went wrong while running that command.");
        }
    }

    private static async Task<DiscordCommandResult> HandleKickAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        if (args.Count < 1)
        {
            return DiscordCommandResult.Reply("Usage: kick <@user|userId> [reason]");
        }

        var userId = ParseUserToken(args[0]);
        if (userId == null)
        {
            return DiscordCommandResult.Reply("Please mention a user or pass their ID — kick <@user|userId> [reason]");
        }

        var guildId = ResolveGuildId(bot, channelId);
        if (guildId == null)
        {
            return DiscordCommandResult.Reply(GuildUnknownMessage(bot));
        }

        var reason = args.Count > 1 ? string.Join(' ', args.Skip(1)) : null;
        return DiscordCommandResult.Reply(await bot.KickMemberAsync(guildId, userId, reason).ConfigureAwait(false));
    }

    private static async Task<DiscordCommandResult> HandleBanAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        if (args.Count < 1)
        {
            return DiscordCommandResult.Reply("Usage: ban <@user|userId> [reason]");
        }

        var userId = ParseUserToken(args[0]);
        if (userId == null)
        {
            return DiscordCommandResult.Reply("Please mention a user or pass their ID — ban <@user|userId> [reason]");
        }

        var guildId = ResolveGuildId(bot, channelId);
        if (guildId == null)
        {
            return DiscordCommandResult.Reply(GuildUnknownMessage(bot));
        }

        var reason = args.Count > 1 ? string.Join(' ', args.Skip(1)) : null;
        return DiscordCommandResult.Reply(await bot.BanMemberAsync(guildId, userId, reason).ConfigureAwait(false));
    }

    private static async Task<DiscordCommandResult> HandleUnbanAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        if (args.Count < 1)
        {
            return DiscordCommandResult.Reply("Usage: unban <userId>");
        }

        var userId = ParseUserToken(args[0]);
        if (userId == null)
        {
            return DiscordCommandResult.Reply("Please pass the banned user's ID — unban <userId>");
        }

        var guildId = ResolveGuildId(bot, channelId);
        if (guildId == null)
        {
            return DiscordCommandResult.Reply(GuildUnknownMessage(bot));
        }

        return DiscordCommandResult.Reply(await bot.UnbanMemberAsync(guildId, userId).ConfigureAwait(false));
    }

    private static async Task<DiscordCommandResult> HandleMuteAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        if (args.Count < 2)
        {
            return DiscordCommandResult.Reply("Usage: mute <@user|userId> <minutes> [reason]");
        }

        var userId = ParseUserToken(args[0]);
        if (userId == null)
        {
            return DiscordCommandResult.Reply("Please mention a user or pass their ID — mute <@user|userId> <minutes> [reason]");
        }

        if (!int.TryParse(args[1], out var minutes) || minutes < 1 || minutes > MaxTimeoutMinutes)
        {
            return DiscordCommandResult.Reply($"Mute length must be a number of minutes between 1 and {MaxTimeoutMinutes} (28 days).");
        }

        var guildId = ResolveGuildId(bot, channelId);
        if (guildId == null)
        {
            return DiscordCommandResult.Reply(GuildUnknownMessage(bot));
        }

        var reason = args.Count > 2 ? string.Join(' ', args.Skip(2)) : null;
        return DiscordCommandResult.Reply(await bot.TimeoutMemberAsync(guildId, userId, minutes, reason).ConfigureAwait(false));
    }

    private static async Task<DiscordCommandResult> HandleUnmuteAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        if (args.Count < 1)
        {
            return DiscordCommandResult.Reply("Usage: unmute <@user|userId>");
        }

        var userId = ParseUserToken(args[0]);
        if (userId == null)
        {
            return DiscordCommandResult.Reply("Please mention a user or pass their ID — unmute <@user|userId>");
        }

        var guildId = ResolveGuildId(bot, channelId);
        if (guildId == null)
        {
            return DiscordCommandResult.Reply(GuildUnknownMessage(bot));
        }

        return DiscordCommandResult.Reply(await bot.RemoveTimeoutAsync(guildId, userId).ConfigureAwait(false));
    }

    private static async Task<DiscordCommandResult> HandleSlowmodeAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        // In-channel form: slowmode <seconds>  (channelId comes from the message)
        // Channel-arg form: slowmode <#channel|channelId> <seconds>
        if (args.Count == 1 && !string.IsNullOrWhiteSpace(channelId))
        {
            if (!TryParseSeconds(args[0], out var seconds))
            {
                return DiscordCommandResult.Reply(BuildSlowmodeUsage());
            }

            return DiscordCommandResult.Reply(await bot.SetSlowmodeAsync(channelId, seconds).ConfigureAwait(false));
        }

        if (args.Count == 2)
        {
            var targetChannelId = ParseChannelToken(args[0]);
            if (targetChannelId == null || !TryParseSeconds(args[1], out var seconds))
            {
                return DiscordCommandResult.Reply(BuildSlowmodeUsage());
            }

            return DiscordCommandResult.Reply(await bot.SetSlowmodeAsync(targetChannelId, seconds).ConfigureAwait(false));
        }

        return DiscordCommandResult.Reply(BuildSlowmodeUsage());
    }

    private static async Task<DiscordCommandResult> HandlePurgeAsync(
        IDiscordService bot, IReadOnlyList<string> args, string? channelId)
    {
        if (args.Count < 1)
        {
            return DiscordCommandResult.Reply("Usage: purge <1-100>");
        }

        if (!int.TryParse(args[0], out var count) || count < 1 || count > MaxPurgeCount)
        {
            return DiscordCommandResult.Reply($"Purge count must be a number between 1 and {MaxPurgeCount}.");
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            return DiscordCommandResult.Reply("Run 'purge <count>' in the channel you want to clean.");
        }

        return DiscordCommandResult.Reply(await bot.PurgeMessagesAsync(channelId, count).ConfigureAwait(false));
    }

    /// <summary>
    /// Resolves the guild a command should run in: the guild owning the channel
    /// the message came from, falling back to the bot's only guild.
    /// </summary>
    private static string? ResolveGuildId(IDiscordService bot, string? channelId)
    {
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var guild = bot.ConnectedGuilds.FirstOrDefault(g =>
                g.TextChannels.Any(c => string.Equals(c.Id, channelId, StringComparison.Ordinal)));
            if (guild != null)
            {
                return guild.Id;
            }
        }

        return bot.ConnectedGuilds.Count == 1 ? bot.ConnectedGuilds[0].Id : null;
    }

    private static string GuildUnknownMessage(IDiscordService bot)
    {
        return bot.ConnectedGuilds.Count == 0
            ? "❌ I'm not in any server, so I can't run that command."
            : "❌ I can't tell which server that command was for — run it inside a server channel.";
    }

    /// <summary>
    /// Strips an optional '!' or '.' prefix and splits on whitespace.
    /// </summary>
    private static List<string> Tokenize(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.StartsWith('!') || text.StartsWith('.'))
        {
            text = text[1..].TrimStart();
        }

        if (text.Length == 0)
        {
            return new List<string>();
        }

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>
    /// Accepts "&lt;@123&gt;", "&lt;@!123&gt;" and a raw snowflake; returns the numeric ID
    /// string or null when the token isn't a user reference.
    /// </summary>
    private static string? ParseUserToken(string token)
    {
        token = token.Trim();
        if (token.StartsWith("<@", StringComparison.Ordinal) && token.EndsWith('>') && token.Length > 3)
        {
            var inner = token[2..^1];
            if (inner.StartsWith('!'))
            {
                inner = inner[1..];
            }

            return IsValidSnowflake(inner) ? inner : null;
        }

        return IsValidSnowflake(token) ? token : null;
    }

    /// <summary>
    /// Accepts "&lt;#123&gt;" and a raw snowflake; returns the numeric ID string or
    /// null when the token isn't a channel reference.
    /// </summary>
    private static string? ParseChannelToken(string token)
    {
        token = token.Trim();
        if (token.StartsWith("<#", StringComparison.Ordinal) && token.EndsWith('>') && token.Length > 3)
        {
            var inner = token[2..^1];
            return IsValidSnowflake(inner) ? inner : null;
        }

        return IsValidSnowflake(token) ? token : null;
    }

    private static bool IsValidSnowflake(string value)
    {
        return value.Length > 0 && value.All(char.IsDigit) && ulong.TryParse(value, out var id) && id > 0;
    }

    private static bool TryParseSeconds(string token, out int seconds)
    {
        return int.TryParse(token, out seconds) && seconds is >= 0 and <= MaxSlowmodeSeconds;
    }

    private static string BuildSlowmodeUsage()
    {
        return $"Usage: slowmode <seconds 0-{MaxSlowmodeSeconds}> (in a channel), or slowmode <#channel|channelId> <seconds>";
    }

    private static string BuildHelpText()
    {
        return "Moderation commands (prefix ! or . optional; moderators/owner only unless noted):"
             + "\n• kick <@user|userId> [reason] — kick a member"
             + "\n• ban <@user|userId> [reason] — ban a user"
             + "\n• unban <userId> — lift a ban"
             + "\n• mute <@user|userId> <minutes> [reason] — timeout (max 40320 min = 28 days)"
             + "\n• unmute <@user|userId> — remove a timeout"
             + "\n• slowmode <seconds 0-21600> — set slowmode in this channel (or slowmode <#channel|channelId> <seconds>)"
             + "\n• purge <1-100> — bulk delete recent messages (alias: clear)"
             + "\n• help — show this list (everyone)";
    }
}
