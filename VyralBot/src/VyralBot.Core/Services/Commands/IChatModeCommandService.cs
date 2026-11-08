namespace VyralBot.Core.Services.Commands;

/// <summary>
/// Chat-mode protection commands with AUTO-REVERT (the streamer.bot raid-protection
/// staple): <c>!emoteonly</c>, <c>!subonly</c>, <c>!followers</c>, <c>!slow</c>.
/// Enabling a mode arms a timed auto-revert so the chat can never stay locked
/// because a mod forgot to turn the mode back off.
/// </summary>
/// <remarks>
/// Interface AND implementation (<see cref="ChatModeCommandService"/>) live in
/// Core; the Twitch side is reached through the Core-owned
/// <see cref="ITwitchChatModeApplier"/> seam (implemented by the Integrations
/// <c>TwitchService</c>), because Core cannot reference Integrations directly —
/// the same dependency split as <see cref="IStreamToolsCommandService"/>, with
/// the seam inverted so the command class itself stays in Core next to its
/// siblings. Registered in App.xaml.cs as
/// <c>AddSingleton&lt;IChatModeCommandService, ChatModeCommandService&gt;()</c>.
/// </remarks>
public interface IChatModeCommandService
{
    /// <summary>
    /// Handles a chat-mode command and returns the reply to post in chat.
    /// </summary>
    /// <param name="username">Login of the invoking viewer.</param>
    /// <param name="platform">
    /// Originating platform name ("Twitch", "Kick", "YouTube", "Discord",
    /// "TikTok"), matched case-insensitively. All chat-mode commands are
    /// Twitch-only (Helix chat settings have no counterpart on the other
    /// platforms).</param>
    /// <param name="isModerator">Whether the invoking viewer is a moderator/broadcaster.</param>
    /// <param name="message">Raw chat message (must start with '!').</param>
    /// <returns>
    /// The chat reply, or <c>null</c> when the message is not one of the
    /// chat-mode commands (never throws — failures return short error text).
    /// </returns>
    Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message);
}
