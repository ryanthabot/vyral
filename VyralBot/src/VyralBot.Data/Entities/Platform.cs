namespace VyralBot.Data.Entities;

/// <summary>
/// Represents a connected streaming platform (Twitch, YouTube, etc.).
/// </summary>
public class Platform
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Platform name (Twitch, YouTube, Kick, TikTok).
    /// </summary>
    public required string Name { get; set; }
    
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Encrypted access token.
    /// </summary>
    public string? AccessToken { get; set; }
    
    /// <summary>
    /// Encrypted refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }
    
    /// <summary>
    /// Token expiration time (UTC).
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }
    
    /// <summary>
    /// Connected username.
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Platform user ID.
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// Channel ID (used by Kick for WebSocket subscriptions).
    /// </summary>
    public string? ChannelId { get; set; }
    
    /// <summary>
    /// Chatroom ID (used by Kick for WebSocket chat subscriptions).
    /// </summary>
    public string? ChatroomId { get; set; }
    
    /// <summary>
    /// Client ID for OAuth.
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Encrypted client secret.
    /// </summary>
    public string? ClientSecret { get; set; }
    
    /// <summary>
    /// Connection status (Connected, Disconnected, Error).
    /// </summary>
    public string Status { get; set; } = "Disconnected";
    
    /// <summary>
    /// Last error message (if any).
    /// </summary>
    public string? LastError { get; set; }
    
    public DateTime? ConnectedAt { get; set; }
    
    /// <summary>
    /// Enable chat receiving for Kick (WebView2)
    /// </summary>
    public bool KickChatEnabled { get; set; } = false;
    
    /// <summary>
    /// Auto-start Kick chat on app launch
    /// </summary>
    public bool KickChatAutoStart { get; set; } = false;

    /// <summary>
    /// Enable chat receiving for Twitch
    /// </summary>
    public bool TwitchChatEnabled { get; set; } = false;

    /// <summary>
    /// Auto-start Twitch chat on app launch
    /// </summary>
    public bool TwitchChatAutoStart { get; set; } = false;

    /// <summary>
    /// Enable chat receiving for YouTube
    /// </summary>
    public bool YoutubeChatEnabled { get; set; } = false;

    /// <summary>
    /// Auto-start YouTube chat on app launch
    /// </summary>
    public bool YoutubeChatAutoStart { get; set; } = false;

    /// <summary>
    /// Enable chat receiving for TikTok
    /// </summary>
    public bool TiktokChatEnabled { get; set; } = false;

    /// <summary>
    /// Auto-start TikTok chat on app launch
    /// </summary>
    public bool TiktokChatAutoStart { get; set; } = false;

    /// <summary>
    /// Enable chat receiving for Discord
    /// </summary>
    public bool DiscordChatEnabled { get; set; } = false;

    /// <summary>
    /// Auto-start Discord chat on app launch
    /// </summary>
    public bool DiscordChatAutoStart { get; set; } = false;

    /// <summary>
    /// Enable chat receiving for Facebook
    /// </summary>
    public bool FacebookChatEnabled { get; set; } = false;

    /// <summary>
    /// Auto-start Facebook chat on app launch
    /// </summary>
    public bool FacebookChatAutoStart { get; set; } = false;

    /// <summary>
    /// Enable chat receiving for Rumble
    /// </summary>
    public bool RumbleChatEnabled { get; set; } = false;

    /// <summary>
    /// Auto-start Rumble chat on app launch
    /// </summary>
    public bool RumbleChatAutoStart { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
