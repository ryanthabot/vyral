// src/VyralBot.Integrations/Kick/KickLibOAuthService.cs
using KickLib;
using KickLib.Auth;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services;
using VyralBot.Core.Services.Profiles;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Integrations.Kick;

/// <summary>
/// Wrapper around KickLib's OAuth generator to maintain compatibility with existing UI.
/// Replaces the custom KickOAuthService (530 lines) with KickLib's built-in OAuth support.
/// </summary>
public class KickLibOAuthService
{
    private readonly ILogger<KickLibOAuthService> _logger;
    private readonly IDataService _dataService;
    private readonly IProfileService _profileService;
    private readonly KickOAuthGenerator _oauthGenerator;
    
    private string? _codeVerifier;

    public KickLibOAuthService(
        ILogger<KickLibOAuthService> logger,
        IDataService dataService,
        IProfileService profileService)
    {
        _logger = logger;
        _dataService = dataService;
        _profileService = profileService;
        _oauthGenerator = new KickOAuthGenerator();
    }

    /// <summary>
    /// Generate OAuth authorization URL with PKCE.
    /// </summary>
    public string GenerateAuthorizationUrl(string clientId, string redirectUri, out string verifier)
    {
        var scopes = new List<string>
        {
            KickScopes.UserRead,
            KickScopes.ChannelRead,
            KickScopes.ChatWrite,
            KickScopes.EventsSubscribe,
            KickScopes.ModerationBan,
            KickScopes.KicksRead
        };

        var url = _oauthGenerator.GetAuthorizationUri(
            redirectUri,
            clientId,
            scopes,
            out verifier).ToString();

        _codeVerifier = verifier;
        _logger.LogInformation("Generated OAuth URL for Kick authorization");
        
        return url;
    }

    /// <summary>
    /// Exchange authorization code for tokens using KickLib.
    /// </summary>
    public async Task<bool> ExchangeCodeForTokenAsync(
        string code,
        string clientId,
        string clientSecret,
        string redirectUri,
        string state,
        string verifier)
    {
        try
        {
            var result = await _oauthGenerator.ExchangeCodeForTokenAsync(
                code,
                clientId,
                clientSecret,
                redirectUri,
                state,
                verifier);

            if (!result.IsSuccess)
            {
                _logger.LogError("Token exchange failed: {Error}", result.Errors.FirstOrDefault()?.Message);
                return false;
            }

            var tokenData = result.Value;

            // Fetch user info using the new access token
            var api = KickApi.Create();
            var userResult = await api.Users.GetMeAsync(tokenData.AccessToken);
            if (!userResult.IsSuccess)
            {
                _logger.LogError("Failed to fetch user info: {Error}", userResult.Errors.FirstOrDefault()?.Message);
                return false;
            }
            var userData = userResult.Value;

            _logger.LogInformation("Fetched user info - Username: {Username}, ID: {UserId}", userData.Name, userData.UserId);

            // Store in database (compatible with existing Platform structure)
            var platform = await _dataService.GetPlatformByNameAsync("Kick") ?? new Platform
            {
                Id = Guid.NewGuid(),
                Name = "Kick",
                CreatedAt = DateTime.UtcNow
            };

            platform.AccessToken = tokenData.AccessToken;
            platform.RefreshToken = tokenData.RefreshToken;
            platform.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn);
            platform.Username = userData.Name;
            platform.UserId = userData.UserId.ToString();
            platform.ClientId = clientId;
            platform.ClientSecret = clientSecret;
            platform.Status = "Connected";
            platform.UpdatedAt = DateTime.UtcNow;

            await _dataService.SavePlatformAsync(platform);

            _logger.LogInformation("Successfully saved Kick OAuth tokens");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token exchange");
            return false;
        }
    }

    /// <summary>
    /// Refresh access token using refresh token.
    /// </summary>
    public async Task<bool> RefreshTokenAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform?.RefreshToken == null)
            {
                _logger.LogWarning("No refresh token found for Kick");
                return false;
            }

            var result = await _oauthGenerator.RefreshAccessTokenAsync(
                platform.RefreshToken,
                platform.ClientId ?? string.Empty,
                platform.ClientSecret ?? string.Empty);

            if (!result.IsSuccess)
            {
                _logger.LogError("Token refresh failed: {Error}", result.Errors.FirstOrDefault()?.Message);
                return false;
            }

            var tokenData = result.Value;

            // Update platform with new tokens
            platform.AccessToken = tokenData.AccessToken;
            platform.RefreshToken = tokenData.RefreshToken;
            platform.TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn);
            platform.UpdatedAt = DateTime.UtcNow;

            await _dataService.SavePlatformAsync(platform);

            _logger.LogInformation("Successfully refreshed Kick access token");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during token refresh");
            return false;
        }
    }
}