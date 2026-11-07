using VyralBot.Integrations.Restream;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-1 restream fixes, regression-locked on top of
/// RestreamArgumentBuilderTests:
///
///   1. DESTINATION URL KEY DE-DUPLICATION: Kick and custom dashboards hand
///      out ONE url with the stream key already appended. Appending the key
///      again would publish to "server/key/key" (rejected by every platform),
///      so when the server already embeds the key it IS the publish url.
///   2. AUDIO MODE ON COPY ROWS: a destination with AudioMode Copy must
///      remux the audio track ("-c:a copy") — video pass-through AND scaled
///      outputs alike; only A128k/A160k rows re-encode to aac.
/// </summary>
public class RestreamArgumentBuilderRegressionTests
{
    private static RestreamDestination Destination(
        string server,
        string key,
        RestreamResolution resolution = RestreamResolution.Source,
        RestreamAudioMode audio = RestreamAudioMode.Copy) => new(
        Id: "d1",
        Name: "Dest",
        Platform: "Twitch",
        ServerUrl: server,
        StreamKey: key,
        Resolution: resolution,
        VideoBitrateKbps: 4000,
        EncoderPreset: "veryfast",
        AudioMode: audio,
        Enabled: true);

    private static RestreamSourceConfig Source() => new(RestreamOperationMode.SinglePC, 1935, "vyral");

    private static int IndexOf(IReadOnlyList<string> args, string option)
        => args.ToList().IndexOf(option);

    // ------------------------------------------------------------------
    // DestinationUrl: full-URL-with-key vs bare-key destinations
    // ------------------------------------------------------------------

    [Fact]
    public void DestinationUrl_ServerAlreadyContainsKey_UsedAsIs_NoDuplicate()
    {
        var key = "live_7f3d9a2b1c4e"; // 15 chars, the minimum detection length is 8
        var fullUrl = "https://contribute.example.net/app/" + key;

        var url = RestreamArgumentBuilder.DestinationUrl(Destination(fullUrl, key));

        Assert.Equal(fullUrl, url);
        Assert.DoesNotContain(key + "/" + key, url);
    }

    [Fact]
    public void DestinationUrl_BareKeyDestination_AppendsKey()
    {
        var url = RestreamArgumentBuilder.DestinationUrl(
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123"));

        Assert.Equal("rtmp://live.twitch.tv/app/live_secret_key_123", url);
    }

    [Fact]
    public void DestinationUrl_TrailingSlashOnServer_IsNotDoubled()
    {
        var url = RestreamArgumentBuilder.DestinationUrl(
            Destination("rtmp://live.twitch.tv/app/", "live_secret_key_123"));

        Assert.Equal("rtmp://live.twitch.tv/app/live_secret_key_123", url);
    }

    [Fact]
    public void Build_FullUrlDestination_LastArgumentHasNoDuplicatedKey()
    {
        var key = "live_9c8b7a6d5e4f";
        var server = "rtmp://ingest.kick.tv/app";

        var args = RestreamArgumentBuilder.Build(Source(), new[] { Destination(server, key) });

        Assert.Equal(server + "/" + key, args[^1]);

        // And the embedded-key form end-to-end: the server field carries the key.
        var embedded = server + "/" + key;
        var embeddedArgs = RestreamArgumentBuilder.Build(Source(), new[] { Destination(embedded, key) });
        Assert.Equal(embedded, embeddedArgs[^1]);
        Assert.DoesNotContain(embedded + "/" + key, embeddedArgs);
    }

    // ------------------------------------------------------------------
    // Audio mode on copy rows
    // ------------------------------------------------------------------

    [Fact]
    public void Build_SourceRow_AudioCopy_IncludesCopyAudioCodec()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.Source, audio: RestreamAudioMode.Copy)
        });

        // Video remux...
        Assert.Contains("copy", args);
        Assert.DoesNotContain("libx264", args);
        // ...and audio remux too: the adjacent pair "-c:a copy".
        int audioCodec = IndexOf(args, "-c:a");
        Assert.True(audioCodec >= 0, "pass-through row must set an audio codec");
        Assert.Equal("copy", args[audioCodec + 1]);
        Assert.DoesNotContain("aac", args);
    }

    [Fact]
    public void Build_SourceRow_AudioBitrateMode_ReEncodesAudioOnly()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.Source, audio: RestreamAudioMode.A128k)
        });

        // Video stays pass-through; only the audio track re-encodes.
        Assert.DoesNotContain("libx264", args);
        int audioCodec = IndexOf(args, "-c:a");
        Assert.Equal("aac", args[audioCodec + 1]);
        Assert.Contains("128k", args);
    }

    [Fact]
    public void Build_ScaledRow_AudioCopy_KeepsAudioCopy()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.P720, audio: RestreamAudioMode.Copy)
        });

        // The scaled output re-encodes video...
        Assert.Contains("libx264", args);
        // ...but a Copy audio mode must NOT be re-encoded to aac.
        int audioCodec = IndexOf(args, "-c:a");
        Assert.Equal("copy", args[audioCodec + 1]);
        Assert.DoesNotContain("aac", args);
        Assert.DoesNotContain("128k", args);
        Assert.DoesNotContain("160k", args);
    }
}
