namespace VyralBot.Core.Services.Commands;

/// <summary>
/// Community fun-pack chat commands: <c>!8ball</c>, <c>!dice</c>/<c>!roll</c>,
/// <c>!coinflip</c>/<c>!flip</c>, <c>!lurk</c>, <c>!hug</c>, <c>!rng</c>.
/// Everyone, all platforms, stateless (no DB) — only an in-memory per-user
/// 5-second spam guard.
/// </summary>
/// <remarks>
/// Interface + implementation both live in Core
/// (<c>VyralBot.Core/Services/Commands/FunCommandService.cs</c>) because no
/// platform integration is needed. Registered in App.xaml.cs as
/// <c>AddSingleton&lt;IFunCommandService, FunCommandService&gt;</c> and routed by
/// ChatViewModel after the warning-command block — but AFTER the user-defined
/// command service, so a streamer's custom <c>!dice</c> shadows the built-in.
/// </remarks>
public interface IFunCommandService
{
    /// <summary>
    /// Handles a fun-pack chat command and returns the reply to post in chat.
    /// </summary>
    /// <param name="username">Login of the invoking viewer.</param>
    /// <param name="platform">
    /// Originating platform name ("Twitch", "Kick", "YouTube", "Discord", "TikTok")
    /// — fun commands are platform-agnostic, kept for contract parity.
    /// </param>
    /// <param name="isModerator">
    /// Whether the invoking viewer is a moderator/broadcaster — unused: every
    /// fun command is available to everyone.
    /// </param>
    /// <param name="message">Raw chat message (must start with '!').</param>
    /// <returns>
    /// The chat reply, <c>null</c> when the message is not a fun command, and
    /// <c>null</c> when the same user fired any fun command within the last
    /// 5 seconds (spam guard drops silently). Never throws.
    /// </returns>
    Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message);
}
