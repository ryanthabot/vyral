namespace VyralBot.Integrations.Restream;

/// <summary>
/// Single home for "is this destination publishable?" checks, shared by the
/// page ViewModel (live row hints + the start gate) and the tests. Aligned with
/// what the engine can actually deliver: every output is muxed FLV
/// (<c>-f flv</c>, RestreamArgumentBuilder), so only <c>rtmp://</c> and
/// <c>rtmps://</c> publish URLs are valid — SRT/UDP endpoints need an MPEG-TS
/// muxer the builder does not build, and http(s) URLs are dashboard pages, not
/// ingests. A destination with an empty key is still valid when the server URL
/// already embeds it (Kick hands out one URL with the key appended);
/// <see cref="RestreamArgumentBuilder.DestinationUrl"/> publishes those
/// verbatim instead of appending the key twice.
/// </summary>
public static class RestreamValidation
{
    /// <summary>Output schemes the FLV-muxing command line can publish to.</summary>
    public static readonly IReadOnlyList<string> SupportedSchemes = new[] { "rtmp", "rtmps" };

    /// <summary>
    /// Validates one destination. Returns <c>null</c> when it can be part of a
    /// session; otherwise a short user-facing reason (no secrets — never
    /// includes the stream key).
    /// </summary>
    public static string? Validate(RestreamDestination destination)
    {
        var server = destination.ServerUrl.Trim();

        if (server.Length == 0)
        {
            return "enter the RTMP server URL (rtmp://…)";
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Scheme))
        {
            return "the server URL must start with rtmp:// or rtmps://";
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is "http" or "https")
        {
            return "that looks like a web page URL — copy the RTMP ingest URL from the platform's stream dashboard instead";
        }

        if (scheme != "rtmp" && scheme != "rtmps")
        {
            return $"unsupported scheme '{scheme}://' — restream publishes over rtmp:// or rtmps:// only";
        }

        if (string.IsNullOrWhiteSpace(destination.StreamKey) && !ServerEmbedsKey(server))
        {
            return "enter the stream key — or paste the full URL with the key appended (Kick style) and leave the key empty";
        }

        return null;
    }

    /// <summary>
    /// True when the server URL's path is long enough to BE the publish URL
    /// (base ingests have a single app segment, e.g. <c>rtmp://host/app</c>;
    /// a key-appended URL has app + key, e.g. <c>rtmp://host/app/live_key…</c>).
    /// Mirrors the ≥8-char embedding check in
    /// <see cref="RestreamArgumentBuilder.DestinationUrl"/> as closely as a
    /// key-less URL can be judged.
    /// </summary>
    public static bool ServerEmbedsKey(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.Segments.Count(s => s.Trim('/').Length > 0);
        return segments >= 2;
    }
}
