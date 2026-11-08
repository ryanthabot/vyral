namespace VyralBot.Integrations.Kick;

public interface IKickService
{
    bool IsConnected { get; }
    string? Channel { get; }
    string? BotUsername { get; }
    
    Task ConnectAsync(string username, string accessToken, string channel, int broadcasterUserId);
    Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel, int broadcasterUserId);
    Task DisconnectAsync();
    Task SendMessageAsync(string message);

    /// <summary>
    /// Times a viewer out of the broadcaster's Kick chat room via the Kick
    /// public API (scope <c>moderation:ban</c> — already requested by the
    /// setup wizard). Kick timeouts are minute-granular (1 min to 7 days), so
    /// sub-minute durations round up to one minute. Best-effort: returns false
    /// (never throws) when the token lacks the scope, the user id isn't
    /// numeric or Kick rejects the call.
    /// </summary>
    /// <param name="userId">Numeric Kick user id (chat pipeline provides it).</param>
    /// <param name="duration">Requested timeout duration.</param>
    /// <param name="reason">Moderation reason (truncated to Kick's 100-char limit).</param>
    Task<bool> TimeoutUserAsync(string userId, TimeSpan duration, string reason);

    /// <summary>
    /// Permanently bans a viewer from the broadcaster's Kick chat room via the
    /// Kick public API (scope <c>moderation:ban</c>). Best-effort, never throws.
    /// </summary>
    /// <param name="userId">Numeric Kick user id.</param>
    /// <param name="reason">Moderation reason (truncated to 100 chars).</param>
    Task<bool> BanUserAsync(string userId, string reason);

    /// <summary>
    /// Deletes a single chat message via the Kick public API
    /// (<c>DELETE /v1/channels/chat/messages/&#123;id&#125;</c>, scope
    /// <c>moderation:chat_message:manage</c> — added to the wizard's scope
    /// list; tokens granted before that scope was requested get a 403 until
    /// the owner re-runs Kick setup once). Best-effort, never throws.
    /// </summary>
    /// <param name="messageId">Kick chat message id (UUID from Pusher).</param>
    Task<bool> DeleteMessageAsync(string messageId);
}
