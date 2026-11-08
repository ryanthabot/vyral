using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.StreamStatus;
using VyralBot.Integrations.Twitch;

namespace VyralBot.Integrations.StreamTools;

/// <summary>
/// Implementation of <see cref="IStreamToolsCommandService"/> — the classic mod/stream
/// tool chat commands: !so, !clip, !raid, !title, !game, !commercial, !uptime, !followage.
/// Lives in Integrations (not Core, where the interface sits) because it depends on
/// <see cref="ITwitchService"/>; Integrations references Core, never the reverse.
/// </summary>
/// <remarks>
/// <para>API coverage notes (verified against <see cref="ITwitchService"/>):</para>
/// <list type="bullet">
/// <item><c>!so</c> — ITwitchService's channel-info read
/// (<c>GetChannelInfoAsync</c>) only covers the broadcaster's OWN channel; there is no
/// target-capable variant, so the shoutout cannot fetch the target's title/game and
/// degrades to a platform URL + generic hype text (twitch.tv/{user}, kick.com/{user},
/// youtube.com/@{user}, tiktok.com/@{user}; Discord gets plain hype).</item>
/// <item><c>!game</c> set-side resolves the name to a Twitch game id through
/// <c>GetTopGamesAsync(query)</c> ("id:name;..." pairs) and then PATCHes via
/// <c>UpdateChannelInfoAsync(null, gameId)</c>.</item>
/// <item><c>!uptime</c> — Twitch reports the real uptime via
/// <c>GetStreamUptimeAsync</c> (Helix started_at). Other platforms:
/// <see cref="IStreamLiveRegistry"/> tracks only a live bool (no start times), so they
/// report live/offline with "duration unknown" when live. Discord is never tracked and
/// therefore reports offline.</item>
/// <item><c>!followage</c> — <c>GetFollowAgeAsync</c> returns null both for
/// "doesn't follow" and for lookup failures; the reply says "isn't following the
/// channel." for both (documented ambiguity).</item>
/// </list>
/// </remarks>
public class StreamToolsCommandService : IStreamToolsCommandService
{
    private const string NotConnected = "Twitch is not connected.";
    private const string ModOnly = "That's a moderator-only command.";

    /// <summary>Commercial lengths Twitch allows (seconds).</summary>
    private static readonly HashSet<int> CommercialLengths = [30, 60, 90, 120, 150, 180];

    private readonly ITwitchService? _twitch;
    private readonly IStreamLiveRegistry? _liveRegistry;
    private readonly ILogger<StreamToolsCommandService> _logger;

    public StreamToolsCommandService(
        ITwitchService? twitchService,
        IStreamLiveRegistry? liveRegistry,
        ILogger<StreamToolsCommandService> logger)
    {
        _twitch = twitchService;
        _liveRegistry = liveRegistry;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("!"))
            return null;

        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        string? result;
        try
        {
            result = command switch
            {
                "!so" => isModerator ? HandleShoutout(platform, parts) : ModOnly,
                "!clip" => isModerator ? await HandleClipAsync(platform) : ModOnly,
                "!raid" => isModerator ? await HandleRaidAsync(platform, parts) : ModOnly,
                "!title" => isModerator ? await HandleTitleAsync(platform, parts) : ModOnly,
                "!game" => isModerator ? await HandleGameAsync(platform, parts) : ModOnly,
                "!commercial" => isModerator ? await HandleCommercialAsync(platform, parts) : ModOnly,
                "!uptime" => await HandleUptimeAsync(platform),
                "!followage" => await HandleFollowAgeAsync(username, platform, parts),
                _ => null
            };
        }
        catch (Exception ex)
        {
            // A chat command must never throw into the chat pipeline — degrade
            // to a short failure reply (ITwitchService calls normally return
            // values instead of throwing; this guards the rest).
            _logger.LogInformation("StreamTools command {Command} by {Username} on {Platform} failed: {Error}",
                command, username, platform, ex.Message);
            return "That command failed — check the logs.";
        }

        // Unmatched messages fall through (null) so other command services can
        // take them; matched ones are info-logged for the audit trail.
        if (result is not null)
        {
            _logger.LogInformation("StreamTools command {Command} by {Username} on {Platform}",
                command, username, platform);
        }

        return result;
    }

    // ── !so ─────────────────────────────────────────────────────────────
    // No target-capable channel-info read exists on ITwitchService, so this is
    // the degraded form: platform URL + generic hype (see class remarks).
    private string HandleShoutout(string platform, string[] parts)
    {
        var user = SanitizeUser(parts.Length > 1 ? parts[1] : null);
        if (user.Length == 0)
            return "Usage: !so <user>";

        if (platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return $"Check out {user} at twitch.tv/{user.ToLowerInvariant()} — go show them some love!";
        if (platform.Equals("Kick", StringComparison.OrdinalIgnoreCase))
            return $"Check out {user} at kick.com/{user.ToLowerInvariant()} — go show them some love!";
        if (platform.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
            return $"Check out {user} at youtube.com/@{user} — go show them some love!";
        if (platform.Equals("TikTok", StringComparison.OrdinalIgnoreCase))
            return $"Check out {user} at tiktok.com/@{user} — go show them some love!";

        // Discord and unknown platforms have no channel URL to link.
        return $"Go check out {user} — they're awesome!";
    }

    // ── !clip ───────────────────────────────────────────────────────────
    private async Task<string> HandleClipAsync(string platform)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "Clip failed (Twitch only, and the stream must be live)";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        var url = await twitch.CreateClipUrlAsync();
        return url is null
            ? "Clip failed (Twitch only, and the stream must be live)"
            : $"Clip created: {url}";
    }

    // ── !raid ───────────────────────────────────────────────────────────
    private async Task<string> HandleRaidAsync(string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "!raid is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        if (parts.Length > 1 && parts[1].Equals("cancel", StringComparison.OrdinalIgnoreCase))
            return await twitch.CancelRaidAsync() ? "Raid cancelled." : "Raid cancel failed.";

        var target = SanitizeUser(parts.Length > 1 ? parts[1] : null);
        if (target.Length == 0)
            return "Usage: !raid <channel> (or !raid cancel)";

        return await twitch.StartRaidAsync(target) ? $"Raiding {target}!" : "Raid failed — check the channel name.";
    }

    // ── !title ──────────────────────────────────────────────────────────
    private async Task<string> HandleTitleAsync(string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "!title is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        if (parts.Length < 2)
        {
            var info = await twitch.GetChannelInfoAsync();
            return info is null
                ? "Couldn't read the current title."
                : string.IsNullOrWhiteSpace(info.Value.GameName)
                    ? $"Current title: {info.Value.Title}"
                    : $"Current title: {info.Value.Title} (playing {info.Value.GameName})";
        }

        var title = string.Join(' ', parts.Skip(1));
        return await twitch.UpdateChannelInfoAsync(title, null) ? "Title updated." : "Title update failed.";
    }

    // ── !game ───────────────────────────────────────────────────────────
    // Name → id resolution goes through GetTopGamesAsync's "id:name;..." pairs,
    // then UpdateChannelInfoAsync PATCHes the broadcaster's category.
    private async Task<string> HandleGameAsync(string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "!game is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        if (parts.Length < 2)
        {
            var info = await twitch.GetChannelInfoAsync();
            return info is null || string.IsNullOrWhiteSpace(info.Value.GameName)
                ? "Couldn't read the current game."
                : $"Current game: {info.Value.GameName}";
        }

        var gameName = string.Join(' ', parts.Skip(1));
        var games = await twitch.GetTopGamesAsync(gameName);
        var resolved = ParseFirstGameEntry(games);
        if (resolved is null)
            return $"Game \"{gameName}\" not found on Twitch.";

        return await twitch.UpdateChannelInfoAsync(null, resolved.Value.Id)
            ? $"Game updated to {resolved.Value.Name}."
            : "Game update failed.";
    }

    // ── !commercial ─────────────────────────────────────────────────────
    private async Task<string> HandleCommercialAsync(string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "!commercial is Twitch-only.";

        if (parts.Length != 2 || !int.TryParse(parts[1], out var length) || !CommercialLengths.Contains(length))
            return "Usage: !commercial <30|60|90|120|150|180>";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        // Needs the channel:edit:commercial scope on the broadcaster's token.
        return await twitch.StartCommercialAsync(length) ? $"Commercial started ({length}s)." : "Commercial failed.";
    }

    // ── !uptime ─────────────────────────────────────────────────────────
    // Twitch: real uptime from Helix started_at. Other platforms: the live
    // registry only tracks a bool, so they get live/offline + unknown duration.
    private async Task<string> HandleUptimeAsync(string platform)
    {
        if (platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
        {
            var twitch = ConnectedTwitch();
            if (twitch is null)
                return NotConnected;

            var uptime = await twitch.GetStreamUptimeAsync();
            return uptime is null
                ? "Stream is offline."
                : $"Stream uptime: {(int)uptime.Value.TotalHours}h {uptime.Value.Minutes}m";
        }

        if (_liveRegistry is null)
            return "Stream status isn't tracked.";

        return _liveRegistry.IsLive(platform)
            ? "Live now — duration unknown (no start time available)."
            : "Stream is offline.";
    }

    // ── !followage ──────────────────────────────────────────────────────
    private async Task<string> HandleFollowAgeAsync(string username, string platform, string[] parts)
    {
        if (!platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase))
            return "Follow-age lookup is Twitch-only.";

        var twitch = ConnectedTwitch();
        if (twitch is null)
            return NotConnected;

        var target = SanitizeUser(parts.Length > 1 ? parts[1] : null);
        if (target.Length == 0)
            target = string.IsNullOrWhiteSpace(username) ? "you" : username;

        var age = await twitch.GetFollowAgeAsync(target.ToLowerInvariant());
        return age is null
            ? $"{target} isn't following the channel."
            : $"{target} has been following for {HumanizeDuration(age.Value)}.";
    }

    /// <summary>
    /// The connected Twitch service, or null when it is absent/disconnected —
    /// captured into a local so null-state survives the handlers' awaits.
    /// </summary>
    private ITwitchService? ConnectedTwitch() => _twitch is { IsConnected: true } ? _twitch : null;

    /// <summary>
    /// Parses the first "id:name" pair of a GetTopGamesAsync "id:name;id:name;..."
    /// result. The id sits before the first colon, so game names containing
    /// colons stay intact; null when the string is empty/malformed.
    /// </summary>
    private static (string Id, string Name)? ParseFirstGameEntry(string? games)
    {
        if (string.IsNullOrWhiteSpace(games))
            return null;

        var first = games.Split(';', 2)[0];
        var separator = first.IndexOf(':');
        if (separator <= 0)
            return null;

        var id = first[..separator].Trim();
        var name = first[(separator + 1)..].Trim();
        return id.Length == 0 || name.Length == 0 ? null : (id, name);
    }

    /// <summary>Largest two non-zero units ("2 months, 3 days" / "5 minutes").</summary>
    private static string HumanizeDuration(TimeSpan span)
    {
        var years = span.Days / 365;
        var remainingDays = span.Days % 365;
        var months = remainingDays / 30;
        var days = remainingDays % 30;

        // Ordered coarse-to-fine; take the first two non-zero entries.
        (int Value, string Singular)[] units =
        [
            (years, "year"), (months, "month"), (days, "day"),
            (span.Hours, "hour"), (span.Minutes, "minute")
        ];

        var picked = new List<string>();
        foreach (var (value, singular) in units)
        {
            if (value <= 0)
                continue;
            picked.Add(value == 1 ? $"1 {singular}" : $"{value} {singular}s");
            if (picked.Count == 2)
                break;
        }

        return picked.Count > 0 ? string.Join(", ", picked) : "less than a minute";
    }

    /// <summary>Strips '@'/'#' decorations and whitespace from a chat mention.</summary>
    private static string SanitizeUser(string? raw)
    {
        var trimmed = raw?.Trim().TrimStart('@', '#').Trim() ?? string.Empty;
        // Keep only platform-legal username characters so a crafted target
        // can't smuggle path separators or query strings into a profile URL.
        return new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }
}
