namespace VyralBot.Core.Services.Commands;

/// <summary>
/// Classic mod/stream-tool chat commands: <c>!so</c>, <c>!clip</c>, <c>!raid</c>,
/// <c>!title</c>, <c>!game</c>, <c>!commercial</c>, <c>!uptime</c>, <c>!followage</c>.
/// </summary>
/// <remarks>
/// The IMPLEMENTATION lives in <c>VyralBot.Integrations.StreamTools</c>
/// (<c>StreamToolsCommandService</c>), mirroring the <see cref="StreamStatus.IStreamLiveRegistry"/>
/// pattern: Core owns the interface so ChatViewModel/DI can reference it, while the
/// class sits next to the platform integrations it needs (<c>ITwitchService</c> lives in
/// VyralBot.Integrations, which references Core — the reverse reference is impossible).
/// Registered in App.xaml.cs as
/// <c>AddSingleton&lt;IStreamToolsCommandService, VyralBot.Integrations.StreamTools.StreamToolsCommandService&gt;</c>.
/// </remarks>
public interface IStreamToolsCommandService
{
    /// <summary>
    /// Handles a stream-tools chat command and returns the reply to post in chat.
    /// </summary>
    /// <param name="username">Login of the invoking viewer.</param>
    /// <param name="platform">
    /// Originating platform name ("Twitch", "Kick", "YouTube", "Discord", "TikTok"),
    /// matched case-insensitively.
    /// </param>
    /// <param name="isModerator">Whether the invoking viewer is a moderator/broadcaster.</param>
    /// <param name="message">Raw chat message (must start with '!').</param>
    /// <returns>
    /// The chat reply, or <c>null</c> when the message is not one of the
    /// stream-tools commands (never throws — failures return short error text).
    /// </returns>
    Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message);
}
