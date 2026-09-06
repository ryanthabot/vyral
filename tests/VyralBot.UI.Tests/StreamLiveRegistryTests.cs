using VyralBot.Integrations.StreamStatus;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Unit tests for the platform live-status registry (W1T2).
/// The state logic is exercised via the parameterless StreamLiveRegistry ctor,
/// which intentionally skips EventBus subscriptions so no bus fake is needed;
/// the subscription wiring (Twitch/Kick/YouTube events) lives in the DI ctor.
/// </summary>
public class StreamLiveRegistryTests
{
    [Fact]
    public void IsLive_BeforeAnyUpdate_PlatformsStartNotLive()
    {
        var registry = new StreamLiveRegistry();

        Assert.False(registry.IsLive("Twitch"));
        Assert.False(registry.IsLive("Kick"));
        Assert.False(registry.IsLive("YouTube"));
    }

    [Fact]
    public void SetLive_ThenIsLive_ReflectsLatestState()
    {
        var registry = new StreamLiveRegistry();

        registry.SetLive("Twitch", true);
        Assert.True(registry.IsLive("Twitch"));

        registry.SetLive("Twitch", false);
        Assert.False(registry.IsLive("Twitch"));
    }

    [Fact]
    public void SetLive_IsLive_CaseInsensitivePlatformMatch()
    {
        var registry = new StreamLiveRegistry();

        registry.SetLive("Twitch", true);
        Assert.True(registry.IsLive("TWITCH"));
        Assert.True(registry.IsLive("twitch"));

        registry.SetLive("kick", true);
        Assert.True(registry.IsLive("Kick"));
    }

    [Fact]
    public void IsLive_UnknownPlatform_ReturnsFalse()
    {
        var registry = new StreamLiveRegistry();
        registry.SetLive("Twitch", true);
        registry.SetLive("Kick", true);

        // Discord (and any other platform) is never tracked here; the watchtime
        // gate excludes Discord separately. Unknown platforms must report not-live.
        Assert.False(registry.IsLive("Discord"));
        Assert.False(registry.IsLive("TikTok"));
    }

    [Fact]
    public void SetLive_PerPlatform_StateIsIndependent()
    {
        var registry = new StreamLiveRegistry();

        registry.SetLive("Twitch", true);
        registry.SetLive("YouTube", true);
        registry.SetLive("Kick", false);

        Assert.True(registry.IsLive("Twitch"));
        Assert.True(registry.IsLive("YouTube"));
        Assert.False(registry.IsLive("Kick"));
    }

    [Fact]
    public void SetLive_OnChange_FiresStreamLiveChangedWithPlatformName()
    {
        var registry = new StreamLiveRegistry();
        string? changedPlatform = null;
        int fireCount = 0;
        registry.StreamLiveChanged += (_, platform) => { changedPlatform = platform; fireCount++; };

        registry.SetLive("Kick", true);

        Assert.Equal(1, fireCount);
        Assert.Equal("Kick", changedPlatform);
    }

    [Fact]
    public void SetLive_SameValue_DoesNotFireStreamLiveChanged()
    {
        var registry = new StreamLiveRegistry();
        int fireCount = 0;
        registry.StreamLiveChanged += (_, _) => fireCount++;

        registry.SetLive("YouTube", false);
        registry.SetLive("YouTube", false);
        registry.SetLive("YouTube", true);
        registry.SetLive("YouTube", true);

        // YouTube emits repeated non-live states ("Connecting", "WaitingForBroadcast",
        // "ReauthRequired"); consumers must only hear about actual transitions.
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SetLive_MultipleSubscribers_AllNotified()
    {
        var registry = new StreamLiveRegistry();
        int first = 0, second = 0;
        registry.StreamLiveChanged += (_, _) => first++;
        registry.StreamLiveChanged += (_, _) => second++;

        registry.SetLive("Twitch", true);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void StreamLiveChanged_Unsubscribe_StopsNotifying()
    {
        var registry = new StreamLiveRegistry();
        int fireCount = 0;
        void Handler(object? sender, string platform) => fireCount++;
        registry.StreamLiveChanged += Handler;

        registry.SetLive("Twitch", true);
        registry.StreamLiveChanged -= Handler;
        registry.SetLive("Kick", true);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SetLive_NullOrEmptyPlatform_IsIgnored()
    {
        var registry = new StreamLiveRegistry();
        int fireCount = 0;
        registry.StreamLiveChanged += (_, _) => fireCount++;

        registry.SetLive("", true);
        registry.SetLive("   ", true);
        registry.SetLive(null!, true);

        Assert.Equal(0, fireCount);
        Assert.False(registry.IsLive(""));
        Assert.False(registry.IsLive("   "));
    }

    [Fact]
    public void IsLive_NullOrEmptyPlatform_ReturnsFalse()
    {
        var registry = new StreamLiveRegistry();

        Assert.False(registry.IsLive(""));
        Assert.False(registry.IsLive(null!));
    }

    [Fact]
    public void UnsubscribeAll_WithoutEventBus_IsSafeNoOp()
    {
        // The parameterless ctor never subscribes, so cleanup must tolerate
        // the absence of a bus (keeps tests and early shutdown simple).
        var registry = new StreamLiveRegistry();
        registry.SetLive("Twitch", true);

        registry.UnsubscribeAll();

        Assert.True(registry.IsLive("Twitch"));
    }
}
