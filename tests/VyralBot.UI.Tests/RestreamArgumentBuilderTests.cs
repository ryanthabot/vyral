using VyralBot.Integrations.Restream;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Restream page: pure FFmpeg argument construction — ingest URL per mode,
/// pass-through vs scaled output shape, Windows escaping, and stream-key
/// masking. Process supervision is live-verified; these lock the command the
/// engine spawns.
/// </summary>
public class RestreamArgumentBuilderTests
{
    private static RestreamDestination Destination(
        RestreamResolution resolution = RestreamResolution.Source,
        string key = "live_secret_key_123",
        string server = "rtmp://live.twitch.tv/app",
        RestreamAudioMode audio = RestreamAudioMode.Copy) => new(
        Id: "d1",
        Name: "Twitch",
        Platform: "Twitch",
        ServerUrl: server,
        StreamKey: key,
        Resolution: resolution,
        VideoBitrateKbps: 4000,
        EncoderPreset: "veryfast",
        AudioMode: audio,
        Enabled: true);

    [Fact]
    public void IngestUrl_BindsLoopbackForSinglePc_AndAllInterfacesForTwoPc()
    {
        var single = new RestreamSourceConfig(RestreamOperationMode.SinglePC, 1935, "vyral");
        var dual = new RestreamSourceConfig(RestreamOperationMode.TwoPC, 1936, "mykey");

        Assert.Equal("rtmp://127.0.0.1:1935/live/vyral", RestreamArgumentBuilder.IngestUrl(single));
        Assert.Equal("rtmp://0.0.0.0:1936/live/mykey", RestreamArgumentBuilder.IngestUrl(dual));
    }

    [Fact]
    public void Build_SourceResolution_IsPassThroughCopy()
    {
        var source = new RestreamSourceConfig(RestreamOperationMode.SinglePC, 1935, "vyral");
        var args = RestreamArgumentBuilder.Build(source, new[] { Destination() });

        Assert.Contains("copy", args);
        Assert.DoesNotContain("libx264", args);
        // Destination URL is the last argument.
        Assert.Equal("rtmp://live.twitch.tv/app/live_secret_key_123", args[^1]);
        // Listen-mode ingest input.
        int input = args.ToList().IndexOf("-i");
        Assert.Equal("-listen", args[input - 2]);
        Assert.Equal("1", args[input - 1]);
        // Machine-readable progress on stdout.
        Assert.Contains("pipe:1", args);
    }

    [Fact]
    public void Build_ScaledResolution_AddsOneEncodeChainPerDestination()
    {
        var source = new RestreamSourceConfig(RestreamOperationMode.SinglePC, 1935, "vyral");
        var destinations = new[]
        {
            Destination(),
            Destination(resolution: RestreamResolution.P720, audio: RestreamAudioMode.A128k),
        };

        var args = RestreamArgumentBuilder.Build(source, destinations);

        // One libx264 chain for the scaled destination only.
        Assert.Equal(1, args.Count(a => a == "libx264"));
        Assert.Contains("scale=-2:720:flags=bilinear", args);
        Assert.Contains("4000k", args);
        Assert.Contains("8000k", args); // bufsize = 2x bitrate
        Assert.Contains("expr:gte(t,n_forced*2)", args);
        // Re-encoded audio for the scaled destination.
        Assert.Contains("128k", args);
        // Both destinations produce an flv output.
        Assert.Equal(2, args.Count(a => a == "flv"));
        // Output order matches destination order: the pass-through output URL
        // comes before the scaled encode chain, which ends with the scaled URL.
        var list = args.ToList();
        int firstUrl = list.IndexOf("rtmp://live.twitch.tv/app/live_secret_key_123");
        int encode = list.IndexOf("libx264");
        int lastUrl = list.LastIndexOf("rtmp://live.twitch.tv/app/live_secret_key_123");
        Assert.True(firstUrl < encode && encode < lastUrl);
    }

    [Fact]
    public void Mask_HidesStreamKeys_EverywhereTheyAppear()
    {
        var destinations = new[] { Destination() };
        var errorLine = "[rtmp @ 0x1] Connection to rtmp://live.twitch.tv/app/live_secret_key_123 failed: Connection refused";

        var masked = RestreamArgumentBuilder.Mask(errorLine, destinations);

        Assert.DoesNotContain("live_secret_key_123", masked);
        Assert.Contains("***", masked);
        // Bare key occurrences are masked too.
        var bare = RestreamArgumentBuilder.Mask("key was live_secret_key_123", destinations);
        Assert.DoesNotContain("live_secret_key_123", bare);
    }

    [Fact]
    public void Escape_QuotesWhitespaceAndBackslashQuotes()
    {
        Assert.Equal("plain", RestreamArgumentBuilder.Escape("plain"));
        Assert.Equal("\"\"", RestreamArgumentBuilder.Escape(""));
        Assert.Equal("\"a b\"", RestreamArgumentBuilder.Escape("a b"));
        Assert.Equal("\"say \\\"hi\\\"\"", RestreamArgumentBuilder.Escape("say \"hi\""));
    }
}
