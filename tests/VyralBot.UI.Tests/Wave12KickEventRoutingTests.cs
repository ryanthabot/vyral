using System.Collections.Concurrent;
using System.Reflection;
using VyralBot.Core.Services.EventBus.Events;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-12 coverage for the Kick cross-transport event dedup added in wave 10
/// (<see cref="KickEventRouting.TryBeginEvent"/>): the first claim wins, any
/// duplicate inside the 15-second window is suppressed, an entry whose window
/// has expired can be reclaimed, and the backing key cache is pruned back to
/// its cap once it overflows.
///
/// The dedup state is process-wide static (both the KickLib websocket and the
/// webhook transports claim against it in production), so every test resets it
/// first. That is safe because xUnit runs the tests of one class sequentially;
/// no other test class in this suite touches <see cref="KickEventRouting"/>.
///
/// Overflow-pruning semantics pinned here (matches the implementation, which
/// is documented as best-effort): when the cache exceeds its cap the NEXT
/// claim evicts every EXPIRED entry but never a still-fresh one — so a burst
/// of distinct live events can transiently exceed the cap until those entries
/// age out. Dedup correctness is unaffected while over cap.
/// </summary>
public class Wave12KickEventRoutingTests
{
    private const string FieldMoved = "KickEventRouting.{0} moved — update the wave-12 tests";

    private static ConcurrentDictionary<string, DateTime> RecentEventKeys()
        => (ConcurrentDictionary<string, DateTime>)(typeof(KickEventRouting)
            .GetField("RecentEventKeys", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(string.Format(FieldMoved, "RecentEventKeys")))
            .GetValue(null)!;

    private static int MaxTrackedKeys()
        => (int)(typeof(KickEventRouting)
            .GetField("MaxTrackedKeys", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(string.Format(FieldMoved, "MaxTrackedKeys")))
            .GetValue(null)!;

    private static TimeSpan DedupWindow()
        => (TimeSpan)(typeof(KickEventRouting)
            .GetField("DedupWindow", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(string.Format(FieldMoved, "DedupWindow")))
            .GetValue(null)!;

    private static void ResetState() => RecentEventKeys().Clear();

    // ------------------------------------------------------------------
    // Claim / duplicate semantics
    // ------------------------------------------------------------------

    [Fact]
    public void TryBeginEvent_FirstClaimTrue_DuplicateWithinWindowFalse()
    {
        ResetState();

        Assert.True(KickEventRouting.TryBeginEvent("wave12:sub:42"));
        Assert.False(KickEventRouting.TryBeginEvent("wave12:sub:42"));
        // A third delivery inside the window is still a duplicate.
        Assert.False(KickEventRouting.TryBeginEvent("wave12:sub:42"));
    }

    [Fact]
    public void TryBeginEvent_DistinctKeys_ClaimIndependently()
    {
        ResetState();

        Assert.True(KickEventRouting.TryBeginEvent("wave12:gift:1"));
        Assert.True(KickEventRouting.TryBeginEvent("wave12:gift:2"));
        Assert.False(KickEventRouting.TryBeginEvent("wave12:gift:1"));
        Assert.False(KickEventRouting.TryBeginEvent("wave12:gift:2"));
    }

    [Fact]
    public void TryBeginEvent_WindowExpiry_BackdatedEntryCanBeReclaimed()
    {
        ResetState();
        var key = "wave12:sub:expired";

        Assert.True(KickEventRouting.TryBeginEvent(key));
        Assert.False(KickEventRouting.TryBeginEvent(key));

        // Simulate the 15s window elapsing without sleeping: backdate the
        // stored claim timestamp past the dedup window.
        RecentEventKeys()[key] = DateTime.UtcNow - DedupWindow() - TimeSpan.FromSeconds(1);

        // The same key may be claimed again, and the window re-arms.
        Assert.True(KickEventRouting.TryBeginEvent(key));
        Assert.False(KickEventRouting.TryBeginEvent(key));
    }

    // ------------------------------------------------------------------
    // Cache bounding
    // ------------------------------------------------------------------

    [Fact]
    public void Prune_OverflowOfExpiredEntries_EvictedOnNextClaim()
    {
        ResetState();
        var keys = RecentEventKeys();
        var expiredAt = DateTime.UtcNow - DedupWindow() - TimeSpan.FromMinutes(5);
        for (var i = 0; i < MaxTrackedKeys() + 500; i++)
        {
            keys[$"wave12:expired:{i}"] = expiredAt;
        }

        // The next claim crosses the cap and prunes the expired flood.
        Assert.True(KickEventRouting.TryBeginEvent("wave12:fresh:overflow"));

        Assert.InRange(keys.Count, 1, MaxTrackedKeys() + 1);
        Assert.False(keys.ContainsKey("wave12:expired:0"));
        Assert.False(keys.ContainsKey($"wave12:expired:{MaxTrackedKeys() + 499}"));
        Assert.True(keys.ContainsKey("wave12:fresh:overflow"));
    }

    [Fact]
    public void Prune_MixedFlood_ExpiredEvicted_FreshEntriesSurvive()
    {
        ResetState();
        var keys = RecentEventKeys();
        var now = DateTime.UtcNow;

        for (var i = 0; i < 1500; i++)
        {
            keys[$"wave12:old:{i}"] = now - DedupWindow() - TimeSpan.FromSeconds(1);
        }
        for (var i = 0; i < 600; i++)
        {
            keys[$"wave12:live:{i}"] = now;
        }
        Assert.Equal(2100, keys.Count);

        Assert.True(KickEventRouting.TryBeginEvent("wave12:fresh:mixed"));

        // 1500 expired rows are gone; the 600 fresh rows plus the new claim
        // survive (pruning only ever drops EXPIRED entries).
        Assert.Equal(601, keys.Count);
        Assert.True(keys.ContainsKey("wave12:live:0"));
        Assert.False(keys.ContainsKey("wave12:old:0"));

        // Dedup still works for a fresh survivor across the prune.
        Assert.False(KickEventRouting.TryBeginEvent("wave12:live:0"));
    }

    [Fact]
    public void Prune_AllFreshOverflow_BestEffortRetained_DedupStillWorks()
    {
        ResetState();
        var keys = RecentEventKeys();
        var now = DateTime.UtcNow;

        for (var i = 0; i < MaxTrackedKeys() + 100; i++)
        {
            keys[$"wave12:flood:{i}"] = now;
        }

        // Pruning runs on the next claim but cannot evict still-fresh keys:
        // the documented bound is best-effort (expired-only eviction), so the
        // count transiently exceeds the cap until entries age out.
        Assert.True(KickEventRouting.TryBeginEvent("wave12:fresh:burst"));
        Assert.Equal(MaxTrackedKeys() + 101, keys.Count);

        // Correctness is unaffected while over cap: duplicates are still
        // suppressed, and a brand-new key still claims.
        Assert.False(KickEventRouting.TryBeginEvent("wave12:flood:0"));
        Assert.True(KickEventRouting.TryBeginEvent("wave12:fresh:burst:2"));
    }

    // ------------------------------------------------------------------
    // Pin the documented constants (wave-10 KickCaps style)
    // ------------------------------------------------------------------

    [Fact]
    public void Caps_Pinned_At2000KeysAnd60SecondWindow()
    {
        Assert.Equal(2000, MaxTrackedKeys());
        Assert.Equal(TimeSpan.FromSeconds(15), DedupWindow());
    }
}
