namespace VyralBot.Core.Services.Counters;

/// <summary>
/// One shared death counter (the streamer.bot staple): a per-profile count the
/// chat `!death` command family mutates, the Tools page card edits, the
/// /counters/death loopback endpoint serves to the OBS widget, and the
/// VyralDeck state payload broadcasts. Mirrors the QuoteService chat-pipeline
/// contract — <see cref="ProcessCommandAsync"/> returns the ready-to-send
/// reply, or null when the message is not a death command (ChatViewModel falls
/// through to the next handler).
/// </summary>
public interface IDeathCounterService
{
    /// <summary>
    /// Processes a death-counter command (!death family) and returns the chat
    /// reply, or null when the message is not a death command.
    /// </summary>
    /// <param name="username">Sender's username (recorded in the history).</param>
    /// <param name="platform">Originating platform (Twitch/Kick/YouTube/Discord).</param>
    /// <param name="isModerator">Sender's moderator status — gates add/set/reset.</param>
    /// <param name="message">Raw chat message text.</param>
    Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message);

    /// <summary>Current count (0 when the profile has no stored value).</summary>
    Task<int> GetCountAsync();

    // ===== Management surface (Tools page death-counter card) =====

    /// <summary>Adds <paramref name="delta"/> (clamped 1..10000) and returns the new count.</summary>
    /// <param name="by">Who made the change ("Tools page" from the dashboard, username from chat).</param>
    Task<int> AddAsync(int delta, string by);

    /// <summary>Subtracts <paramref name="delta"/> (floors at 0) and returns the new count.</summary>
    Task<int> SubtractAsync(int delta, string by);

    /// <summary>Sets the counter (clamped 0..999999) and returns the new count.</summary>
    Task<int> SetAsync(int value, string by);

    /// <summary>Resets the counter to 0 (no confirmation — the reset is logged).</summary>
    Task<int> ResetAsync(string by);

    /// <summary>Most recent history events, newest first.</summary>
    /// <param name="limit">Max events to return (default 10, hard cap 50).</param>
    Task<IReadOnlyList<DeathCounterEvent>> GetHistoryAsync(int limit = 10);

    /// <summary>Raised with the new count after every successful mutation (chat, Tools page).</summary>
    event EventHandler<int>? CountChanged;
}

/// <summary>One recorded counter change (persisted as the DeathCounter.History JSON).</summary>
/// <param name="At">UTC timestamp of the change.</param>
/// <param name="Delta">Signed change applied to the count (set/reset included).</param>
/// <param name="By">Who made the change (chat username or "Tools page").</param>
public sealed record DeathCounterEvent(DateTimeOffset At, int Delta, string By);
