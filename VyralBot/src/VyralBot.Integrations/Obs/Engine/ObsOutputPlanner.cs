using System.Globalization;
using System.Text.Json;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>
/// Pure output planning for <see cref="ObsOutputService"/>: container
/// extension normalization, record path composition, encoder fallback
/// chains and the settings JSON for the libobs outputs. No native calls —
/// every rule here mirrors a 32.1.1 header/plugin fact cited inline so a
/// libobs behavior change fails a test instead of a stream.
/// </summary>
public static class ObsOutputPlanner
{
    /// <summary>
    /// Replay-buffer "format" value. The plugin composes file names via
    /// os_generate_formatted_filename (libobs/util/platform.c @ 32.1.1),
    /// which expands %CCYY %MM %DD %hh %mm %ss (there is no %TS specifier).
    /// </summary>
    public const string ReplayBufferFormat = "replay_%CCYY-%MM-%DD_%hh-%mm-%ss";

    /// <summary>Replay buffer keeps the last 15 s — matches the OBS frontend default.</summary>
    public const int ReplayBufferMaxTimeSec = 15;

    private static readonly HashSet<string> KnownContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "mov", "flv", "ts",
    };

    /// <summary>
    /// Maps the profile's RecFormat2 value to a concrete container extension
    /// for ffmpeg_muxer file paths. OBS 32 stores "hybrid_mp4" (remux-on-the-
    /// fly hybrid container) — ffmpeg_muxer itself writes plain mp4. Anything
    /// unrecognized falls back to mp4.
    /// </summary>
    public static string NormalizeContainerExtension(string? recordFormat)
    {
        if (string.IsNullOrWhiteSpace(recordFormat))
        {
            return "mp4";
        }

        // Strip the "hybrid_" prefix, then lowercase ("hybrid_MKV" etc.).
        string candidate = recordFormat.Trim();
        const string hybridPrefix = "hybrid_";
        if (candidate.StartsWith(hybridPrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[hybridPrefix.Length..];
        }

        return KnownContainers.Contains(candidate) ? candidate.ToLowerInvariant() : "mp4";
    }

    /// <summary>
    /// Full file path for a recording. ffmpeg_muxer takes a single "path"
    /// settings key with the complete file name (obs-ffmpeg-mux.c @ 32.1.1:
    /// obs_data_get_string(settings, "path")); OBS's own directory+format
    /// composition only applies to the replay buffer, so we timestamp here.
    /// </summary>
    public static string RecordFilePath(string recordDirectory, string recordFormat, DateTimeOffset timestamp)
    {
        string stamp = timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string extension = NormalizeContainerExtension(recordFormat);
        return Path.Combine(recordDirectory, $"rec_{stamp}.{extension}");
    }

    /// <summary>
    /// Encoder id candidates for a video encoder, in try order. The profile
    /// value may be a UI alias libobs rejects (obs_video_encoder_create
    /// returns null for unknown ids — the real machine's Simple profile
    /// stores "nvenc"): keep it first, then the NVENC compat id (registered
    /// as "jim_nvenc" by obs-nvenc's nvenc-compat.c even at 32.1.1, where it
    /// reroutes to the modern implementations), then software x264.
    /// Deduped preserving the first occurrence.
    /// </summary>
    public static IReadOnlyList<string> VideoEncoderCandidates(string? profileEncoderId)
    {
        if (string.IsNullOrWhiteSpace(profileEncoderId))
        {
            return ["obs_x264"];
        }

        return DedupePreservingOrder([profileEncoderId, "jim_nvenc", "obs_x264"]);
    }

    /// <summary>
    /// Record-output encoder candidates: the record encoder id when set,
    /// else the stream encoder id, then the same fallback chain.
    /// </summary>
    public static IReadOnlyList<string> RecordVideoEncoderCandidates(string? recordEncoderId, string? streamEncoderId)
    {
        return VideoEncoderCandidates(
            FirstNonEmpty(recordEncoderId, streamEncoderId));
    }

    /// <summary>ffmpeg_muxer settings: {"path": full file path}.</summary>
    public static string RecordSettingsJson(string filePath)
    {
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["path"] = filePath,
        });
    }

    /// <summary>
    /// replay_buffer settings (obs-ffmpeg-mux.c @ 32.1.1): the plugin wraps
    /// ffmpeg_muxer and composes its file from "directory" + "format" +
    /// "extension"; "max_time_sec" bounds the kept footage. Minimal viable
    /// set — "max_size_mb"/"allow_spaces" keep their plugin defaults.
    /// </summary>
    public static string ReplayBufferSettingsJson(string recordDirectory, string containerExtension)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["directory"] = recordDirectory,
            ["format"] = ReplayBufferFormat,
            ["extension"] = containerExtension,
            ["max_time_sec"] = ReplayBufferMaxTimeSec,
        });
    }

    /// <summary>Video encoder settings: bitrate in kbps + 2 s keyframe interval.</summary>
    public static string VideoEncoderSettingsJson(int videoBitrateKbps)
    {
        return JsonSerializer.Serialize(new Dictionary<string, int>
        {
            ["bitrate"] = videoBitrateKbps,
            ["keyint_sec"] = 2,
        });
    }

    /// <summary>Audio encoder settings: bitrate in kbps (ffmpeg_aac).</summary>
    public static string AudioEncoderSettingsJson(int audioBitrateKbps)
    {
        return JsonSerializer.Serialize(new Dictionary<string, int>
        {
            ["bitrate"] = audioBitrateKbps,
        });
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;

    private static IReadOnlyList<string> DedupePreservingOrder(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (string value in values)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
