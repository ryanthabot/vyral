namespace VyralBot.Integrations.Discord;

/// <summary>
/// Outcome of running a chat command through an <see cref="IDiscordCommandHandler"/>.
/// <see cref="Handled"/> is true when the input matched a known command (including
/// permission denials and usage errors) and <see cref="Response"/> should be sent
/// back to chat; false means the text was not a command for this handler and the
/// caller should keep processing it as a regular chat message.
/// </summary>
public record DiscordCommandResult(bool Handled, string Response)
{
    /// <summary>Shared "not one of my commands" result (empty response).</summary>
    public static DiscordCommandResult NotHandled { get; } = new(false, string.Empty);

    /// <summary>Creates a handled result carrying a chat reply.</summary>
    public static DiscordCommandResult Reply(string response) => new(true, response);
}
