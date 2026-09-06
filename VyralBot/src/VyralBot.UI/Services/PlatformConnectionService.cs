using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;

namespace VyralBot.UI.Services;

/// <summary>
/// Shared platform connect/disconnect/token-refresh flows, extracted from MainViewModel's
/// startup auto-connect paths so Settings → Platforms and the Chat Hub can drive the same
/// operations on demand. "Connect" here means live-service connect with stored credentials —
/// first-time OAuth sign-in still lives on the Platforms (Connections) page.
/// </summary>
public class PlatformConnectionService
{
    private readonly ILogger<PlatformConnectionService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDataService _dataService;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IYouTubeService _youtubeService;
    private readonly IDiscordService _discordService;
    private readonly IDiscordBotManager _discordBotManager;
    private readonly IEventBus _eventBus;

    public PlatformConnectionService(
        ILogger<PlatformConnectionService> logger,
        IServiceProvider serviceProvider,
        IDataService dataService,
        ITwitchService twitchService,
        IKickService kickService,
        IYouTubeService youtubeService,
        IDiscordService discordService,
        IDiscordBotManager discordBotManager,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _youtubeService = youtubeService ?? throw new ArgumentNullException(nameof(youtubeService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public bool IsPlatformConnected(string platformName) => platformName switch
    {
        "Twitch" => _twitchService.IsConnected,
        "Kick" => _kickService.IsConnected,
        "YouTube" => _youtubeService.IsConnected,
        "Discord" => _discordService.IsConnected || _discordBotManager.Connected.Count > 0,
        _ => false,
    };

    /// <summary>True when the platform row exists with credentials stored (i.e. it can be re-connected without sign-in).</summary>
    public async Task<bool> IsPlatformConfiguredAsync(string platformName)
    {
        var platform = await _dataService.GetPlatformByNameAsync(platformName);
        return platform != null && !string.IsNullOrEmpty(platform.AccessToken);
    }

    /// <summary>Connects a platform with its stored credentials. Returns a status message for the caller to display.</summary>
    public Task<(bool Success, string Message)> ConnectAsync(string platformName, bool refreshTokensFirst = true) => platformName switch
    {
        "Twitch" => ConnectTwitchAsync(refreshTokensFirst),
        "Kick" => ConnectKickAsync(refreshTokensFirst),
        "YouTube" => ConnectYouTubeAsync(),
        "Discord" => ConnectDiscordAsync(),
        _ => Task.FromResult((false, $"{platformName} is not supported yet.")),
    };

    /// <summary>Disconnects a platform's live connection (credentials are kept).</summary>
    public Task<(bool Success, string Message)> DisconnectAsync(string platformName) => platformName switch
    {
        "Twitch" => DisconnectTwitchAsync(),
        "Kick" => DisconnectKickAsync(),
        "YouTube" => DisconnectYouTubeAsync(),
        "Discord" => DisconnectDiscordAsync(),
        _ => Task.FromResult((false, $"{platformName} is not supported yet.")),
    };

    /// <summary>Manually refreshes stored OAuth tokens (Twitch/Kick only; YouTube refreshes on connect and Discord bot tokens don't expire).</summary>
    public Task<(bool Success, string Message)> RefreshTokensAsync(string platformName) => platformName switch
    {
        "Twitch" => RefreshTwitchTokensAsync(),
        "Kick" => RefreshKickTokensAsync(),
        _ => Task.FromResult((false, $"{platformName} tokens cannot be refreshed manually.")),
    };

    public async Task<(bool Success, string Message)> ConnectTwitchAsync(bool refreshTokensFirst)
    {
        try
        {
            if (_twitchService.IsConnected)
                return (true, "Twitch is already connected.");

            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken))
                return (false, "Twitch is not set up yet — sign in on the Connections page first.");

            if (refreshTokensFirst && !string.IsNullOrEmpty(platform.RefreshToken))
            {
                await RefreshTwitchTokensAsync();
                // Re-read the row so the connect below uses the rotated tokens
                platform = await _dataService.GetPlatformByNameAsync("Twitch") ?? platform;
            }

            var accessToken = TwitchOAuthService.Decrypt(platform.AccessToken);
            var refreshToken = !string.IsNullOrEmpty(platform.RefreshToken)
                ? TwitchOAuthService.Decrypt(platform.RefreshToken)
                : null;
            var botUsername = platform.Username?.ToLower() ?? "";

            await _twitchService.ConnectAsync(botUsername, accessToken, refreshToken, platform.Username ?? "");

            await MarkConnectedAsync(platform, p => p.TwitchChatEnabled = p.TwitchChatEnabled == false ? true : p.TwitchChatEnabled);
            return (true, $"Connected to Twitch as {platform.Username}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Twitch");
            return (false, $"Twitch connect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DisconnectTwitchAsync()
    {
        try
        {
            if (_twitchService.IsConnected)
                await _twitchService.DisconnectAsync();

            await MarkDisconnectedAsync("Twitch");
            return (true, "Twitch disconnected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Twitch");
            return (false, $"Twitch disconnect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> RefreshTwitchTokensAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");
            if (platform == null || string.IsNullOrEmpty(platform.RefreshToken))
                return (false, "No Twitch refresh token stored.");

            var oAuthService = _serviceProvider.GetRequiredService<TwitchOAuthService>();
            await oAuthService.LoadCredentialsAsync();

            var refreshToken = TwitchOAuthService.Decrypt(platform.RefreshToken);
            var refreshed = await oAuthService.RefreshAccessTokenAsync(refreshToken);
            if (refreshed == null)
                return (false, "Twitch token refresh failed — the existing token is still used.");

            await oAuthService.SaveTokensAsync(platform.Username ?? "", refreshed.Value.AccessToken, refreshed.Value.RefreshToken);
            return (true, "Twitch tokens refreshed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Twitch tokens");
            return (false, $"Twitch token refresh failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ConnectKickAsync(bool refreshTokensFirst)
    {
        try
        {
            if (_kickService.IsConnected)
                return (true, "Kick is already connected.");

            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken) || string.IsNullOrEmpty(platform.UserId))
                return (false, "Kick is not set up yet — sign in on the Connections page first.");

            if (refreshTokensFirst && !string.IsNullOrEmpty(platform.RefreshToken))
            {
                await RefreshKickTokensAsync();
                platform = await _dataService.GetPlatformByNameAsync("Kick") ?? platform;
            }

            var broadcasterUserId = int.Parse(platform.UserId);
            await _kickService.ConnectAsync(
                platform.Username ?? "",
                platform.AccessToken ?? "",
                platform.RefreshToken,
                platform.Username ?? "",
                broadcasterUserId);

            await MarkConnectedAsync(platform, p => p.KickChatEnabled = p.KickChatEnabled == false ? true : p.KickChatEnabled);
            return (true, $"Connected to Kick as {platform.Username}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Kick");
            return (false, $"Kick connect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DisconnectKickAsync()
    {
        try
        {
            if (_kickService.IsConnected)
                await _kickService.DisconnectAsync();

            await MarkDisconnectedAsync("Kick");
            return (true, "Kick disconnected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Kick");
            return (false, $"Kick disconnect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> RefreshKickTokensAsync()
    {
        try
        {
            var kickOAuthService = _serviceProvider.GetRequiredService<KickLibOAuthService>();
            var refreshed = await kickOAuthService.RefreshTokenAsync();
            return refreshed
                ? (true, "Kick tokens refreshed.")
                : (false, "Kick token refresh failed — the existing token is still used.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Kick tokens");
            return (false, $"Kick token refresh failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ConnectYouTubeAsync()
    {
        try
        {
            if (_youtubeService.IsConnected)
                return (true, "YouTube is already connected.");

            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken) || string.IsNullOrEmpty(platform.RefreshToken))
                return (false, "YouTube is not set up yet — sign in on the Connections page first.");

            await _youtubeService.ConnectAsync(
                platform.Username ?? "",
                platform.AccessToken,
                platform.RefreshToken,
                platform.Username ?? "");

            await MarkConnectedAsync(platform, p => p.YoutubeChatEnabled = p.YoutubeChatEnabled == false ? true : p.YoutubeChatEnabled);
            return (true, $"Connected to YouTube as {platform.Username}.");
        }
        catch (YouTubeReauthException ex)
        {
            // Google refused the refresh — mark the row honestly, like the startup path does
            _logger.LogWarning("YouTube re-authorization required ({Reason})", ex.Reason ?? ex.Message);
            await MarkDisconnectedAsync("YouTube");
            return (false, "YouTube re-authorization required — sign in again on the Connections page.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to YouTube");
            return (false, $"YouTube connect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DisconnectYouTubeAsync()
    {
        try
        {
            if (_youtubeService.IsConnected)
                await _youtubeService.DisconnectAsync();

            await MarkDisconnectedAsync("YouTube");
            return (true, "YouTube disconnected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from YouTube");
            return (false, $"YouTube disconnect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ConnectDiscordAsync()
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord");
            if (platform == null || string.IsNullOrEmpty(platform.AccessToken))
                return (false, "Discord is not set up yet — add a bot on the Discord Bots page first.");

            // Single source of truth: the Platform row stays the config store
            // for the Home connection, but the RUNNING session is owned by the
            // bot manager. Adoption finds/creates the DiscordBots row for this
            // token, connects/keeps the legacy singleton on it and claims that
            // session as a managed bot — exactly one gateway per token. It is
            // also idempotent, so it safely replaces the old "already
            // connected" early return (which skipped adoption when the legacy
            // session was live but not yet claimed — the split-brain case).
            var adoption = await _discordBotManager.AdoptPlatformTokenAsync(platform.AccessToken, platform.Username);
            if (!adoption.Success)
                return (false, adoption.Error ?? "Discord bot failed to connect — check the bot token.");

            // Keep the Platforms → Discord section honest: reflect the bot that
            // actually connected (a stale Username from a previous bot would
            // otherwise linger when the wizard token changed).
            if (!string.IsNullOrWhiteSpace(adoption.BotUsername))
                platform.Username = adoption.BotUsername;

            await MarkConnectedAsync(platform, null);
            return (true, $"Connected to Discord as {adoption.BotUsername ?? platform.Username}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Discord");
            return (false, $"Discord connect failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DisconnectDiscordAsync()
    {
        try
        {
            // The Home session runs as an adopted managed bot — stop exactly
            // that bot through the manager so the running world, the Discord
            // Bots page and the Platforms section agree. Unrelated manager
            // bots are never touched.
            var homeBotId = _discordBotManager.PlatformAdoptedBotId;

            if (_discordService.IsConnected)
                await _discordService.DisconnectAsync();

            if (homeBotId != null)
                await _discordBotManager.StopBotAsync(homeBotId);

            await MarkDisconnectedAsync("Discord");
            return (true, "Discord disconnected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from Discord");
            return (false, $"Discord disconnect failed: {ex.Message}");
        }
    }

    private async Task MarkConnectedAsync(Platform platform, Action<Platform>? enableChatFlag)
    {
        try
        {
            platform.Status = "Connected";
            platform.ConnectedAt = DateTime.UtcNow;
            platform.LastError = null;
            platform.UpdatedAt = DateTime.UtcNow;
            enableChatFlag?.Invoke(platform);
            await _dataService.SavePlatformAsync(platform);

            await _eventBus.PublishAsync(new PlatformConnectionChangedEvent
            {
                Source = platform.Name,
                Platform = platform.Name,
                Connected = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking {Platform} connected", platform.Name);
        }
    }

    private async Task MarkDisconnectedAsync(string platformName)
    {
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(platformName);
            if (platform != null)
            {
                platform.Status = "Disconnected";
                platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            await _eventBus.PublishAsync(new PlatformConnectionChangedEvent
            {
                Source = platformName,
                Platform = platformName,
                Connected = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking {Platform} disconnected", platformName);
        }
    }
}
