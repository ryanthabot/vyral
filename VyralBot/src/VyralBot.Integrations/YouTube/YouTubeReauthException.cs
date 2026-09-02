namespace VyralBot.Integrations.YouTube;

/// <summary>
/// Thrown when YouTube OAuth tokens cannot be refreshed (e.g. Google returned
/// <c>invalid_grant</c> because the refresh token expired or was revoked).
/// The stored credentials are kept — the user must re-authorize via the
/// YouTube setup flow to obtain a fresh token.
/// </summary>
public class YouTubeReauthException : InvalidOperationException
{
    /// <summary>Google's error reason when available (e.g. "invalid_grant").</summary>
    public string? Reason { get; }

    public YouTubeReauthException(string message, string? reason = null) : base(message)
    {
        Reason = reason;
    }
}
