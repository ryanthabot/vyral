using VyralBot.Core.Services;
using VyralBot.Core.Services.Currency;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// W1T4 watchtime + Discord points gating rules:
///
///   1. Twitch/Kick/YouTube watch-time points accrue ONLY while that platform is
///      LIVE. When no live registry is wired (isLive == null) the behavior is
///      back-compat: accrue as before (the registry is an optional dependency).
///   2. Discord viewers earn NO watch-time points ever — Discord has no live
///      concept, so the decision is registry-independent.
///   3. Discord CHAT activity points continue but ONLY in channels the streamer
///      assigned (per-bot AssignedChannelIds, comma-separated, case-insensitive).
///
/// The rules are encoded in two pure static decision points and unit-tested
/// directly:
///   - <see cref="WatchtimeService.ShouldAccrueWatchtime"/> (watchtime accrual,
///     shared by WatchtimeService.IncrementActiveWatchtimeAsync and
///     CurrencyService.AwardWatchTimeAsync)
///   - <see cref="CurrencyService.IsDiscordChannelAllowed"/> (assigned-channel
///     membership for Discord chat activity points)
///
/// Note: the helpers are public (not internal) because VyralBot.Core does not
/// grant InternalsVisibleTo to the test project.
/// </summary>
public class WatchtimeGatingTests
{
    // ------------------------------------------------------------------
    // Rule 1: stream platforms accrue only while live
    // ------------------------------------------------------------------

    [Fact]
    public void Twitch_Accrues_WhileLive()
    {
        Assert.True(WatchtimeService.ShouldAccrueWatchtime("Twitch", isLive: true));
    }

    [Fact]
    public void Twitch_DoesNotAccrue_WhileOffline()
    {
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("Twitch", isLive: false));
    }

    [Fact]
    public void Kick_Accrues_WhileLive_AndStops_WhileOffline()
    {
        Assert.True(WatchtimeService.ShouldAccrueWatchtime("Kick", isLive: true));
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("Kick", isLive: false));
    }

    [Fact]
    public void YouTube_Accrues_WhileLive_AndStops_WhileOffline()
    {
        Assert.True(WatchtimeService.ShouldAccrueWatchtime("YouTube", isLive: true));
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("YouTube", isLive: false));
    }

    [Fact]
    public void StreamPlatforms_Accrue_WhenLiveStatusUnknown_BackCompat()
    {
        // No IStreamLiveRegistry wired (isLive == null) must behave as today so
        // existing constructors / new() callers keep working.
        Assert.True(WatchtimeService.ShouldAccrueWatchtime("Twitch", isLive: null));
        Assert.True(WatchtimeService.ShouldAccrueWatchtime("Kick", isLive: null));
        Assert.True(WatchtimeService.ShouldAccrueWatchtime("YouTube", isLive: null));
    }

    // ------------------------------------------------------------------
    // Rule 2: Discord never accrues watchtime — registry-independent
    // ------------------------------------------------------------------

    [Fact]
    public void Discord_NeverAccrues_EvenWhenLiveStatusUnknown()
    {
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("Discord", isLive: null));
    }

    [Fact]
    public void Discord_NeverAccrues_EvenWhenReportedLive()
    {
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("Discord", isLive: true));
    }

    [Fact]
    public void Discord_NeverAccrues_WhenOffline()
    {
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("Discord", isLive: false));
    }

    [Fact]
    public void Discord_PlatformMatch_IsCaseInsensitive()
    {
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("discord", isLive: true));
        Assert.False(WatchtimeService.ShouldAccrueWatchtime("DISCORD", isLive: null));
    }

    // ------------------------------------------------------------------
    // Rule 3: Discord chat points only in streamer-assigned channels
    // ------------------------------------------------------------------

    [Fact]
    public void DiscordChat_Allows_ChannelExactlyInAssignedList()
    {
        Assert.True(CurrencyService.IsDiscordChannelAllowed("1234567890", "1234567890,9876543210"));
    }

    [Fact]
    public void DiscordChat_ChannelMatch_IsCaseInsensitive()
    {
        Assert.True(CurrencyService.IsDiscordChannelAllowed("abc-channel", "ABC-Channel,other"));
    }

    [Fact]
    public void DiscordChat_AssignedList_ToleratesSpacesAroundIds()
    {
        Assert.True(CurrencyService.IsDiscordChannelAllowed("42", " 42 , 43 ,44"));
    }

    [Fact]
    public void DiscordChat_Denies_ChannelNotInAssignedList()
    {
        Assert.False(CurrencyService.IsDiscordChannelAllowed("9999", "1234567890,9876543210"));
    }

    [Fact]
    public void DiscordChat_Denies_WhenNothingAssigned()
    {
        // Strict per the approved rules: no assigned channels -> no chat points.
        Assert.False(CurrencyService.IsDiscordChannelAllowed("1234567890", null));
        Assert.False(CurrencyService.IsDiscordChannelAllowed("1234567890", ""));
        Assert.False(CurrencyService.IsDiscordChannelAllowed("1234567890", "   "));
    }

    [Fact]
    public void DiscordChat_Denies_NullOrEmptyChannelId()
    {
        // The helper is strict; the back-compat "allow when the caller has no
        // channel info" decision lives in AwardChatActivityAsync, not here.
        Assert.False(CurrencyService.IsDiscordChannelAllowed(null, "1234567890"));
        Assert.False(CurrencyService.IsDiscordChannelAllowed("", "1234567890"));
    }

    [Fact]
    public void DiscordChat_AssignedList_TrailingComma_IsTolerated()
    {
        Assert.True(CurrencyService.IsDiscordChannelAllowed("777", "777,"));
    }
}
