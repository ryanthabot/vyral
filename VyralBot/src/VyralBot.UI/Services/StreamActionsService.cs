using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.YouTube;

namespace VyralBot.UI.Services;

/// <summary>
/// Executes stream-management quick actions against the connected platforms:
/// Twitch via the Helix API, Kick via documented mod chat commands sent as the
/// bot account, and YouTube via the liveBroadcasts API. Every failure path
/// returns a <see cref="StreamActionResult"/> with a helpful message — never throws.
/// </summary>
public class StreamActionsService : IStreamActionsService
{
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IYouTubeService _youTubeService;
    private readonly ILogger<StreamActionsService> _logger;

    public StreamActionsService(
        ITwitchService twitchService,
        IKickService kickService,
        IYouTubeService youTubeService,
        ILogger<StreamActionsService> logger)
    {
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _youTubeService = youTubeService ?? throw new ArgumentNullException(nameof(youTubeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Twitch (Helix) ────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchEditStreamInfoAsync(string? title, string? category) =>
        RunTwitchActionAsync(() => _twitchService.EditChannelInfoAsync(title, category));

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchCreateMarkerAsync(string? description) =>
        RunTwitchActionAsync(() => _twitchService.CreateStreamMarkerAsync(description));

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchCreateClipAsync() =>
        RunTwitchActionAsync(() => _twitchService.CreateClipAsync());

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchRaidAsync(string targetChannel) =>
        RunTwitchActionAsync(() => _twitchService.RaidChannelAsync(targetChannel));

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchStopRaidAsync() =>
        RunTwitchActionAsync(() => _twitchService.StopRaidAsync());

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchClearChatAsync() =>
        RunTwitchActionAsync(() => _twitchService.ClearChatAsync());

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchSetEmoteOnlyAsync(bool enabled) =>
        RunTwitchActionAsync(() => _twitchService.SetChatModeAsync(emoteOnly: enabled, followerOnly: null));

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchSetFollowerOnlyAsync(bool enabled) =>
        RunTwitchActionAsync(() => _twitchService.SetChatModeAsync(emoteOnly: null, followerOnly: enabled));

    /// <inheritdoc />
    public Task<StreamActionResult> TwitchSetShieldModeAsync(bool enabled) =>
        RunTwitchActionAsync(() => _twitchService.SetShieldModeAsync(enabled));

    /// <inheritdoc />
    public async Task<TwitchChatSettingsState?> TwitchGetChatSettingsAsync()
    {
        if (!_twitchService.IsConnected)
        {
            return null;
        }

        try
        {
            var state = await _twitchService.GetChatModeStateAsync();
            return state == null
                ? null
                : new TwitchChatSettingsState(state.EmoteOnly, state.FollowerOnly, state.ShieldMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the Twitch chat settings");
            return null;
        }
    }

    /// <summary>
    /// Guards on the Twitch connection, runs the Helix action, and maps the
    /// outcome (or any unexpected exception) to a StreamActionResult.
    /// </summary>
    private async Task<StreamActionResult> RunTwitchActionAsync(Func<Task<TwitchActionResponse>> action)
    {
        if (!_twitchService.IsConnected)
        {
            return new StreamActionResult(false, "Twitch is not connected — connect Twitch in Platforms first.");
        }

        try
        {
            var result = await action();
            return new StreamActionResult(result.Success, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitch stream action failed");
            return new StreamActionResult(false, $"The Twitch action failed: {ex.Message}");
        }
    }

    // ── Kick (mod chat commands sent as the bot account) ──────────────

    /// <inheritdoc />
    public Task<StreamActionResult> KickClearChatAsync() => SendKickCommandAsync("/clear");

    /// <inheritdoc />
    public Task<StreamActionResult> KickSetEmoteOnlyAsync(bool enabled) =>
        SendKickCommandAsync(enabled ? "/emoteonly" : "/emoteonlyoff");

    /// <inheritdoc />
    public Task<StreamActionResult> KickSetFollowerOnlyAsync(bool enabled) =>
        SendKickCommandAsync(enabled ? "/followerOnly" : "/followerOnlyOff");

    /// <inheritdoc />
    public Task<StreamActionResult> KickSetSlowModeAsync(bool enabled) =>
        SendKickCommandAsync(enabled ? "/slow" : "/slowoff");

    /// <summary>
    /// Sends a documented Kick mod chat command as the bot account. Kick's API
    /// only confirms the message send itself, so the result wording calls out
    /// the bot's moderator permission dependency.
    /// </summary>
    private async Task<StreamActionResult> SendKickCommandAsync(string command)
    {
        if (!_kickService.IsConnected)
        {
            return new StreamActionResult(false, "Kick chat is not connected — connect Kick in Platforms first.");
        }

        try
        {
            await _kickService.SendMessageAsync(command);
            _logger.LogInformation("Sent Kick mod command {Command}", command);
            return new StreamActionResult(true, $"Sent {command} to Kick chat (requires bot to have moderator permission).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send the Kick mod command {Command}", command);
            return new StreamActionResult(false, $"Failed to send {command} to Kick chat: {ex.Message}");
        }
    }

    // ── YouTube (LiveBroadcasts API) ──────────────────────────────────

    /// <inheritdoc />
    public async Task<StreamActionResult> YouTubeUpdateStreamInfoAsync(string? title, string? description)
    {
        if (!_youTubeService.IsConnected)
        {
            return new StreamActionResult(false, "YouTube is not connected — connect YouTube in Platforms first.");
        }

        try
        {
            var result = await _youTubeService.UpdateStreamInfoAsync(title, description);
            return new StreamActionResult(result.Success, result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube stream action failed");
            return new StreamActionResult(false, $"The YouTube action failed: {ex.Message}");
        }
    }
}
