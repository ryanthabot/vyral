using VyralBot.Integrations.Restream;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-8 restream polish regressions:
///
///   1. BANDWIDTH HINT (RestreamBandwidth): fires above 80% of the
///      user-overridable uplink cap, stays silent at/below it, excludes
///      disabled rows, counts audio re-encodes + the builder's 500 kbps
///      floor, flags pass-through "Source" rows as extra load, and never
///      echoes secrets.
///   2. ACQUISITION PINNING: the FFmpeg download URL is the immutable
///      versioned gyan.dev package (never the rolling "release-essentials"
///      URL, whose bytes change with every release and cannot be hash-pinned)
///      and the pinned SHA-256/sizes are well-formed.
/// </summary>
public class RestreamWave8Tests
{
    private static RestreamDestination Dest(
        int bitrate = 6000,
        RestreamResolution resolution = RestreamResolution.P720,
        RestreamAudioMode audio = RestreamAudioMode.Copy,
        bool enabled = true) => new(
        "id", "Twitch", "Twitch", "rtmp://live.twitch.tv/app", "key1234567890",
        resolution, bitrate, "veryfast", audio, enabled);

    [Fact]
    public void BandwidthHint_IsNull_AtOrBelowEightyPercentOfCap()
    {
        // 2x 6000 = 12000 = exactly 80% of 15000 → not above → silent.
        var destinations = new[] { Dest(), Dest() };
        Assert.Null(RestreamBandwidth.Evaluate(destinations, RestreamBandwidth.DefaultCapKbps));
    }

    [Fact]
    public void BandwidthHint_Fires_AboveEightyPercentOfCap()
    {
        var destinations = new[] { Dest(), Dest(), Dest() }; // 18000 kbps
        var hint = RestreamBandwidth.Evaluate(destinations, RestreamBandwidth.DefaultCapKbps);
        Assert.NotNull(hint);
        Assert.Contains("18 Mbps", hint); // ≈18 Mbps combined
        Assert.DoesNotContain("key1234567890", hint); // no secrets in hints
    }

    [Fact]
    public void BandwidthHint_RespectsUserCap()
    {
        var destinations = new[] { Dest() }; // 6000 kbps
        Assert.Null(RestreamBandwidth.Evaluate(destinations, 10_000)); // threshold 8000 → quiet
        Assert.NotNull(RestreamBandwidth.Evaluate(destinations, 7_000)); // threshold 5600 → warn
    }

    [Fact]
    public void BandwidthHint_IgnoresDisabledRows_AndCountsAudioReencodes()
    {
        Assert.Null(RestreamBandwidth.Evaluate(new[] { Dest(enabled: false) }, 15_000));

        // 6000 video + 160 audio = 6160; cap 7000 → threshold 5600 → fires.
        Assert.NotNull(RestreamBandwidth.Evaluate(new[] { Dest(audio: RestreamAudioMode.A160k) }, 7_000));
    }

    [Fact]
    public void BandwidthHint_NotesPassThroughRows_ButDoesNotGuessTheirBitrate()
    {
        // Source row contributes 0 (OBS's publisher bitrate is unknown here);
        // the scaled row's 6000 alone must exceed the 5600 threshold.
        var hint = RestreamBandwidth.Evaluate(
            new[] { Dest(resolution: RestreamResolution.Source), Dest() }, 7_000);
        Assert.NotNull(hint);
        Assert.Contains("Source", hint); // calls the pass-through cost out explicitly
        Assert.Contains("6 Mbps", hint); // ≈6 Mbps counted
    }

    [Fact]
    public void Estimate_AppliesBuilderBitrateFloor_ForScaledRows()
    {
        var low = new[] { Dest(bitrate: 100) }; // floored to 500 like the argument builder
        Assert.Equal(500, RestreamBandwidth.EstimateTotalKbps(low));
    }

    [Fact]
    public void Acquisition_Url_IsPinnedVersionedPackage_NotRolling()
    {
        Assert.StartsWith(
            "https://www.gyan.dev/ffmpeg/builds/packages/",
            RestreamFfmpegAcquisitionService.DownloadUrl);
        Assert.DoesNotContain("release-essentials", RestreamFfmpegAcquisitionService.DownloadUrl);
    }

    [Fact]
    public void Acquisition_PinnedHashAndSizes_AreWellFormed()
    {
        Assert.Matches("^[0-9a-f]{64}$", RestreamFfmpegAcquisitionService.ExpectedSha256);
        Assert.True(RestreamFfmpegAcquisitionService.ExpectedZipBytes > 50_000_000, "zip ~110 MB");
        Assert.True(RestreamFfmpegAcquisitionService.ExpectedFfmpegBytes > 50_000_000, "exe ~100 MB");
        Assert.True(RestreamFfmpegAcquisitionService.ExpectedFfmpegBytes < RestreamFfmpegAcquisitionService.ExpectedZipBytes + 50_000_000);
    }
}
