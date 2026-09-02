namespace VyralBot.Integrations.Discord;

/// <summary>
/// Parses and dispatches chat commands for a single Discord bot instance.
/// Implementations never throw: every failure is returned as a chat-ready
/// <see cref="DiscordCommandResult.Response"/>.
/// </summary>
public interface IDiscordCommandHandler
{
    /// <summary>
    /// Try to interpret <paramref name="commandLine"/> as a moderation command and
    /// run it against <paramref name="bot"/> (the bot whose gateway received the
    /// message).
    /// </summary>
    /// <param name="bot">The originating bot's service — commands run in its guilds.</param>
    /// <param name="commandLine">
    /// Raw message text. A leading '!' or '.' prefix is accepted and stripped, so
    /// both "kick @user" and "!kick @user" work.
    /// </param>
    /// <param name="callerIsModerator">Sender holds the ManageMessages guild permission.</param>
    /// <param name="callerIsOwner">Sender is the guild owner or an administrator.</param>
    /// <param name="channelId">
    /// Snowflake ID of the channel the message was posted in, when known. Used as
    /// the target context for slowmode/purge and to resolve the guild for
    /// member commands.
    /// </param>
    /// <returns>
    /// <see cref="DiscordCommandResult.Handled"/> is true when the first word matched
    /// a known command name (the response may still be an error or a permission
    /// denial); false when the text is not a command for this handler at all.
    /// </returns>
    Task<DiscordCommandResult> HandleAsync(
        IDiscordService bot,
        string commandLine,
        bool callerIsModerator,
        bool callerIsOwner,
        string? channelId = null);
}
