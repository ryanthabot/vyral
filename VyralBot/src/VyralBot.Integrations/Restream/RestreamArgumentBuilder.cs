namespace VyralBot.Integrations.Restream;

/// <summary>
/// Builds the single FFmpeg command behind a restream session: RTMP listen-mode
/// input (one publisher), then one output per destination — pass-through outputs
/// (<c>-c:v copy</c>, no video decode/encode) and scaled outputs (shared decode,
/// one x264 encode each). Output order matches the destination list so FFmpeg's
/// per-output log lines map back onto rows. Also the single home for Windows
/// argument escaping and stream-key masking.
/// </summary>
public static class RestreamArgumentBuilder
{
    /// <summary>Default video bitrate (kbps) per rung of the resolution ladder.</summary>
    public static int DefaultBitrateKbps(RestreamResolution resolution) => resolution switch
    {
        RestreamResolution.P1080 => 6000,
        RestreamResolution.P936 => 5000,
        RestreamResolution.P720 => 4000,
        RestreamResolution.P540 => 2500,
        RestreamResolution.P360 => 1200,
        _ => 6000,
    };

    /// <summary>Pixel height for each rung; <c>null</c> for pass-through.</summary>
    public static int? Height(RestreamResolution resolution) => resolution switch
    {
        RestreamResolution.P1080 => 1080,
        RestreamResolution.P936 => 936,
        RestreamResolution.P720 => 720,
        RestreamResolution.P540 => 540,
        RestreamResolution.P360 => 360,
        _ => null,
    };

    /// <summary>The RTMP URL OBS must be pointed at.</summary>
    public static string IngestUrl(RestreamSourceConfig source)
    {
        var bind = source.Mode == RestreamOperationMode.TwoPC ? "0.0.0.0" : "127.0.0.1";
        var key = string.IsNullOrWhiteSpace(source.IngestStreamKey) ? "vyral" : source.IngestStreamKey.Trim();
        return $"rtmp://{bind}:{source.IngestPort}/live/{key}";
    }

    /// <summary>
    /// Full publish URL (server + key) for a destination. If the server field
    /// already embeds the key (Kick and custom dashboards hand out ONE url
    /// with the key appended), appending the key again would publish to
    /// "<c>server/key/key</c>" — rejected by every platform — so the server is
    /// used as the complete publish URL instead.
    /// </summary>
    public static string DestinationUrl(RestreamDestination destination)
    {
        var server = destination.ServerUrl.Trim().TrimEnd('/');
        var key = destination.StreamKey.Trim();

        if (key.Length >= 8 && server.Contains(key, StringComparison.OrdinalIgnoreCase))
        {
            return server;
        }

        return $"{server}/{key}";
    }

    /// <summary>FFmpeg argv (unescaped, one element per option/value).</summary>
    public static IReadOnlyList<string> Build(RestreamSourceConfig source, IReadOnlyList<RestreamDestination> destinations)
    {
        var args = new List<string>
        {
            // Machine-readable progress once per second on stdout ("-nostats"
            // suppresses the human stderr banner duplication of the same data).
            "-hide_banner", "-nostdin", "-loglevel", "info",
            "-stats_period", "1", "-progress", "pipe:1", "-nostats",
            // RTMP server mode: accept exactly one publisher connection.
            "-listen", "1",
            "-i", IngestUrl(source),
        };

        foreach (var destination in destinations)
        {
            // Video plus first audio track (audio optional so a video-only
            // ingest still works); data/metadata tracks are dropped — platforms
            // reject them and they cannot be re-encoded anyway.
            args.Add("-map");
            args.Add("0:v:0");
            args.Add("-map");
            args.Add("0:a:0?");

            if (destination.Resolution == RestreamResolution.Source)
            {
                // Pass-through: remux only. No decode, no video encode —
                // effectively free. Audio is still honored: a Source row with
                // an audio bitrate re-encodes just the audio track.
                args.Add("-c:v");
                args.Add("copy");
                if (destination.AudioMode == RestreamAudioMode.Copy)
                {
                    args.Add("-c:a");
                    args.Add("copy");
                }
                else
                {
                    args.Add("-c:a");
                    args.Add("aac");
                    args.Add("-b:a");
                    args.Add(destination.AudioMode == RestreamAudioMode.A128k ? "128k" : "160k");
                }
            }
            else
            {
                var height = Height(destination.Resolution)!.Value;
                // -2 keeps the aspect ratio and forces an even width (yuv420p
                // requirement); bilinear is the cheapest scaler worth using.
                args.Add("-vf");
                args.Add($"scale=-2:{height}:flags=bilinear");

                args.Add("-c:v");
                args.Add("libx264");
                args.Add("-preset");
                args.Add(string.IsNullOrWhiteSpace(destination.EncoderPreset) ? "veryfast" : destination.EncoderPreset);
                args.Add("-profile:v");
                args.Add("high");
                args.Add("-pix_fmt");
                args.Add("yuv420p");

                var kbps = Math.Max(500, destination.VideoBitrateKbps);
                args.Add("-b:v");
                args.Add($"{kbps}k");
                args.Add("-maxrate");
                args.Add($"{kbps}k");
                args.Add("-bufsize");
                args.Add($"{kbps * 2}k");
                // Keyframe every 2s of output time regardless of input fps —
                // platforms require short GOPs and we build args before the
                // publisher's fps is known.
                args.Add("-force_key_frames");
                args.Add("expr:gte(t,n_forced*2)");

                args.Add("-c:a");
                args.Add(destination.AudioMode == RestreamAudioMode.Copy ? "copy" : "aac");
                if (destination.AudioMode != RestreamAudioMode.Copy)
                {
                    args.Add("-b:a");
                    args.Add(destination.AudioMode == RestreamAudioMode.A128k ? "128k" : "160k");
                }
            }

            args.Add("-flvflags");
            args.Add("no_duration_filesize");
            args.Add("-f");
            args.Add("flv");
            args.Add(DestinationUrl(destination));
        }

        return args;
    }

    /// <summary>Single command-line string with CommandLineToArgvW-compatible quoting.</summary>
    public static string BuildCommandLine(RestreamSourceConfig source, IReadOnlyList<RestreamDestination> destinations) =>
        string.Join(" ", Build(source, destinations).Select(Escape));

    /// <summary>
    /// Canonical Windows escaping (CommandLineToArgvW rules): quote when the
    /// argument is empty or contains whitespace, doubling backslashes that
    /// precede a quote. FFmpeg re-parses argv itself, so nothing else is needed.
    /// </summary>
    public static string Escape(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = argument.Any(char.IsWhiteSpace);
        if (!needsQuotes)
        {
            return argument;
        }

        var builder = new System.Text.StringBuilder(argument.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                builder.Append('\\', backslashes + 1);
                backslashes = 0;
                builder.Append('"');
            }
            else
            {
                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(c);
            }
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes);
        }
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Replaces every stream key (and any full URL embedding one) with a masked
    /// form. Applied to everything the engine logs or surfaces — FFmpeg error
    /// lines contain full RTMP URLs including the key.
    /// </summary>
    public static string Mask(string text, IReadOnlyList<RestreamDestination> destinations)
    {
        foreach (var destination in destinations)
        {
            var key = destination.StreamKey?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var full = DestinationUrl(destination);
            text = text.Replace(full, destination.ServerUrl.Trim().TrimEnd('/') + "/***", StringComparison.OrdinalIgnoreCase);
            if (key.Length > 4)
            {
                text = text.Replace(key, "***", StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }
}
