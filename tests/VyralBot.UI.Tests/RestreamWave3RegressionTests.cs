using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VyralBot.Integrations.Restream;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-3 restream regressions, locked on top of
/// RestreamArgumentBuilderTests + RestreamArgumentBuilderRegressionTests:
///
///   1. DESTINATION ROUND-TRIP through the page ViewModel's persistence path
///      (RestreamDestinationItemViewModel.FromModel/ToModel + the exact
///      JsonSerializerOptions RestreamViewModel uses for
///      restream_destinations.json).
///   2. RTMPS DESTINATIONS: the scheme flows through DestinationUrl/Build
///      untouched — no rtmps→rtmp rewrite, no extra TLS flags.
///   3. TRANSCODE-VS-COPY decided PER DESTINATION row, not per session.
///   4. AUDIO-MODE BITRATE FLAG: the "-b:a" value follows AudioMode.
///   5. KEY MASKING: Mask() output never contains a (realistic-length) bare
///      stream key, on any surface, in any casing.
/// </summary>
public class RestreamWave3RegressionTests
{
    // Verbatim mirror of RestreamViewModel.PersistenceJson (private static) —
    // the options the restream page uses for both destinations-file writes and
    // reads. If the VM's options change, this must change with them (and the
    // enum-name assertions below will force exactly that).
    private static readonly JsonSerializerOptions PersistenceJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static RestreamDestination Destination(
        string server,
        string key,
        string id = "d1",
        RestreamResolution resolution = RestreamResolution.Source,
        int bitrateKbps = 4000,
        string preset = "veryfast",
        RestreamAudioMode audio = RestreamAudioMode.Copy,
        bool enabled = true) => new(
        Id: id,
        Name: "Dest " + id,
        Platform: "Twitch",
        ServerUrl: server,
        StreamKey: key,
        Resolution: resolution,
        VideoBitrateKbps: bitrateKbps,
        EncoderPreset: preset,
        AudioMode: audio,
        Enabled: enabled);

    private static RestreamSourceConfig Source() => new(RestreamOperationMode.SinglePC, 1935, "vyral");

    private static int IndexOf(IReadOnlyList<string> args, string option)
        => args.ToList().IndexOf(option);

    private static int LastIndexOf(IReadOnlyList<string> args, string option)
        => args.ToList().LastIndexOf(option);

    // ------------------------------------------------------------------
    // 1. Destination round-trip via the ViewModel persistence path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Destinations_RoundTripThroughItemViewModelAndJson_AreUnchanged()
    {
        var original = new List<RestreamDestination>
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123", id: "a",
                resolution: RestreamResolution.P720, bitrateKbps: 4500,
                preset: "faster", audio: RestreamAudioMode.A160k),
            Destination("rtmp://a.rtmp.youtube.com/live2", "yt_key_9876543210", id: "b",
                resolution: RestreamResolution.Source, bitrateKbps: 6000,
                audio: RestreamAudioMode.Copy, enabled: false),
        };

        // The same file dance SaveDestinationsAsync/LoadDestinationsAsync do:
        // rows → models → JSON on disk → models → rows → models.
        var folder = Path.Combine(Path.GetTempPath(), "vyral-wave3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "restream_destinations.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(original, PersistenceJson));
            var loaded = JsonSerializer.Deserialize<List<RestreamDestination>>(
                await File.ReadAllTextAsync(path), PersistenceJson);

            Assert.NotNull(loaded);
            var back = loaded!.Select(RestreamDestinationItemViewModel.FromModel)
                .Select(item => item.ToModel())
                .ToList();

            Assert.Equal(original, back); // records: full value equality, all 10 fields
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Destinations_File_StoresEnumsAsNames()
    {
        // Old saved files must keep parsing and new ones must stay stable:
        // the enums round-trip as "P720"/"A160k", never as numbers.
        var json = JsonSerializer.Serialize(
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.P720, audio: RestreamAudioMode.A160k),
            PersistenceJson);

        Assert.Contains("\"P720\"", json);
        Assert.Contains("\"A160k\"", json);
        Assert.DoesNotContain("\"Resolution\": 1", json);
    }

    [Fact]
    public void ItemViewModel_ToModel_NormalizesBlankBitrate()
    {
        // ToModel falls back to 6000 when the row's bitrate field is blank.
        var item = RestreamDestinationItemViewModel.FromModel(
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123"));
        item.BitrateText = "   ";

        Assert.Equal(6000, item.ToModel().VideoBitrateKbps);
    }

    // ------------------------------------------------------------------
    // 2. RTMPS destinations
    // ------------------------------------------------------------------

    [Fact]
    public void DestinationUrl_RtmpsServer_SchemeIsPreserved()
    {
        var url = RestreamArgumentBuilder.DestinationUrl(
            Destination("rtmps://a.rtmps.example.com:443/live", "rtmps_key_987654"));

        Assert.Equal("rtmps://a.rtmps.example.com:443/live/rtmps_key_987654", url);
        Assert.StartsWith("rtmps://", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RtmpsDestination_PublishesUnchangedUrlOverFlv()
    {
        var server = "rtmps://a.rtmps.example.com:443/live";
        var key = "rtmps_key_987654";

        var args = RestreamArgumentBuilder.Build(Source(), new[] { Destination(server, key) });

        // Last argument is the publish URL: scheme intact, key appended once.
        Assert.Equal(server + "/" + key, args[^1]);

        // RTMPS is RTMP-over-TLS on the flv muxer — the builder must not
        // rewrite the scheme or add TLS flags of its own.
        Assert.DoesNotContain(server.Replace("rtmps://", "rtmp://", StringComparison.Ordinal), args);
        int format = IndexOf(args, "-f");
        Assert.True(format >= 0, "output must set a format");
        Assert.Equal("flv", args[format + 1]);
    }

    // ------------------------------------------------------------------
    // 3. Transcode-vs-copy decided per destination
    // ------------------------------------------------------------------

    [Fact]
    public void Build_MixedRows_EachOutputGetsItsOwnVideoDecision()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123", id: "copy-row",
                resolution: RestreamResolution.Source),
            Destination("rtmp://a.rtmp.youtube.com/live2", "yt_key_9876543210", id: "scaled-row",
                resolution: RestreamResolution.P540, bitrateKbps: 2500),
        });

        // One video-codec decision per destination, in destination order.
        var videoCodecs = args.Where((a, i) => a == "-c:v").ToList();
        Assert.Equal(2, videoCodecs.Count);
        Assert.Equal("copy", args[IndexOf(args, "-c:v") + 1]);          // first row remuxes
        Assert.Equal("libx264", args[LastIndexOf(args, "-c:v") + 1]);   // second row encodes

        // Only the scaled row gets a scaler; only it gets bitrate triplets.
        int scale = IndexOf(args, "-vf");
        Assert.True(scale > IndexOf(args, "-c:v"), "the -vf belongs to the scaled output");
        Assert.Equal("scale=-2:540:flags=bilinear", args[scale + 1]);
        int videoBitrate = LastIndexOf(args, "-b:v");
        Assert.Equal("2500k", args[videoBitrate + 1]);
        Assert.Equal("2500k", args[LastIndexOf(args, "-maxrate") + 1]);
        Assert.Equal("5000k", args[LastIndexOf(args, "-bufsize") + 1]);

        // Two outputs = two publish URLs / format pairs.
        Assert.Equal(2, args.Count(a => a == "-f"));
    }

    [Fact]
    public void Build_SourceRow_NeverEmitsVideoBitrateOptions()
    {
        // Pass-through ignores the row's bitrate budget entirely — no -b:v,
        // -maxrate, -bufsize, -vf or encoder for a copy row.
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.Source, bitrateKbps: 6000),
        });

        Assert.DoesNotContain("-b:v", args);
        Assert.DoesNotContain("-maxrate", args);
        Assert.DoesNotContain("-bufsize", args);
        Assert.DoesNotContain("-vf", args);
        Assert.DoesNotContain("-preset", args);
    }

    [Fact]
    public void Build_ScaledRow_ClampsBitrateFloorAndDefaultsPreset()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.P720, bitrateKbps: 100, preset: ""),
        });

        Assert.Equal("500k", args[IndexOf(args, "-b:v") + 1]); // floor from Math.Max(500, …)
        Assert.Equal("veryfast", args[IndexOf(args, "-preset") + 1]);
    }

    // ------------------------------------------------------------------
    // 4. Audio-mode bitrate flag
    // ------------------------------------------------------------------

    [Fact]
    public void Build_AudioBitrateModes_FlagFollowsAudioMode()
    {
        var a160 = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.P1080, audio: RestreamAudioMode.A160k),
        });
        Assert.Equal("aac", a160[IndexOf(a160, "-c:a") + 1]);
        Assert.Equal("160k", a160[IndexOf(a160, "-b:a") + 1]); // adjacent pair, exact flag

        var a128 = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.P1080, audio: RestreamAudioMode.A128k),
        });
        Assert.Equal("128k", a128[IndexOf(a128, "-b:a") + 1]);
    }

    [Fact]
    public void Build_AudioCopy_NoBitrateFlag()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.P720, audio: RestreamAudioMode.Copy),
        });

        Assert.Equal("copy", args[IndexOf(args, "-c:a") + 1]);
        Assert.DoesNotContain("-b:a", args);
    }

    [Fact]
    public void Build_SourceRowWithAudioBitrate_ReEncodesAudioOnly()
    {
        var args = RestreamArgumentBuilder.Build(Source(), new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123",
                resolution: RestreamResolution.Source, audio: RestreamAudioMode.A160k),
        });

        Assert.Equal("copy", args[IndexOf(args, "-c:v") + 1]);
        Assert.Equal("aac", args[IndexOf(args, "-c:a") + 1]);
        Assert.Equal("160k", args[IndexOf(args, "-b:a") + 1]);
        Assert.DoesNotContain("libx264", args);
    }

    // ------------------------------------------------------------------
    // 5. Key masking
    // ------------------------------------------------------------------

    [Fact]
    public void Mask_CommandLine_NeverContainsBareKey()
    {
        var destinations = new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123", id: "a"),
            Destination("rtmp://ingest.kick.tv/app", "kick_key_4567890abcdef", id: "b"),
        };

        var commandLine = RestreamArgumentBuilder.BuildCommandLine(Source(), destinations);
        var masked = RestreamArgumentBuilder.Mask("ffmpeg " + commandLine, destinations);

        Assert.DoesNotContain("live_secret_key_123", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kick_key_4567890abcdef", masked, StringComparison.OrdinalIgnoreCase);
        // The publish URLs are masked down to the bare server + "***"...
        Assert.Contains("rtmp://live.twitch.tv/app/***", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rtmp://ingest.kick.tv/app/***", masked, StringComparison.OrdinalIgnoreCase);
        // ...and no full URL with an embedded key survives.
        Assert.DoesNotContain("rtmp://live.twitch.tv/app/live_secret_key_123", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mask_ErrorLineWithFullPublishUrl_MasksKeyButKeepsHost()
    {
        // The classic FFmpeg failure: the error names the full publish URL.
        var destinations = new[]
        {
            Destination("rtmp://live.twitch.tv/app", "live_secret_key_123"),
        };

        var masked = RestreamArgumentBuilder.Mask(
            "[rtmp @ 000001a2b3c4] Connection refused to rtmp://live.twitch.tv/app/live_secret_key_123",
            destinations);

        Assert.DoesNotContain("live_secret_key_123", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rtmp://live.twitch.tv/app/***", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mask_IsCaseInsensitiveOnTheKey()
    {
        // Platforms hand out keys in mixed case; FFmpeg lowercases URLs in
        // some log lines. The replace must catch both casings.
        var destinations = new[]
        {
            Destination("rtmp://live.twitch.tv/app", "Live_Key_ABC123"),
        };

        var masked = RestreamArgumentBuilder.Mask("handshake failed for live_key_abc123", destinations);

        Assert.DoesNotContain("live_key_abc123", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void Mask_ShortKeysAreNotMasked_DocumentedThreshold()
    {
        // Deliberate behavior: keys of 4 characters or fewer are too short to
        // blanket-replace without corrupting unrelated log text, so Mask
        // leaves them. Locked here so the threshold can only move on purpose.
        var destinations = new[]
        {
            Destination("rtmp://live.twitch.tv/app", "abcd"),
        };

        var masked = RestreamArgumentBuilder.Mask("rejected key abcd", destinations);

        Assert.Contains("abcd", masked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("***", masked);
    }
}
