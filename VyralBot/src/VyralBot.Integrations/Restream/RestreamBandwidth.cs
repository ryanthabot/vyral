namespace VyralBot.Integrations.Restream;

/// <summary>
/// Uplink-budget sanity check for multi-destination sessions. Every enabled
/// destination uploads simultaneously, so three 6000 kbps transcodes need an
/// uplink that can carry all of them at once — on a weak connection the
/// encoder outruns the wire and every output drops frames. This is a status
/// hint only: it never blocks a session (the user may know their uplink is
/// fine, e.g. wired fiber).
/// </summary>
public static class RestreamBandwidth
{
    /// <summary>
    /// Conservative default uplink budget (kbps) the hint is measured
    /// against. User-overridable via the <c>BandwidthCapKbps</c> field of the
    /// <c>Restream.Settings</c> config value (shown as "Uplink budget" in the
    /// UI).
    /// </summary>
    public const int DefaultCapKbps = 15_000;

    /// <summary>The hint fires above this fraction of the cap (80% of 15 Mbps = 12 Mbps).</summary>
    public const double WarnFraction = 0.8;

    /// <summary>
    /// Sums the configured output bitrates of the enabled destinations.
    /// Scaled rows contribute their video bitrate (floored like the argument
    /// builder) plus their audio bitrate when re-encoded; "Source"
    /// (pass-through) rows relay whatever OBS sends and contribute 0 here —
    /// <see cref="Evaluate"/> calls that out explicitly instead of guessing.
    /// </summary>
    public static int EstimateTotalKbps(IReadOnlyList<RestreamDestination> destinations) => destinations
        .Where(d => d.Enabled)
        .Sum(d =>
            (d.Resolution == RestreamResolution.Source ? 0 : Math.Max(500, d.VideoBitrateKbps))
            + (d.AudioMode switch
            {
                RestreamAudioMode.A128k => 128,
                RestreamAudioMode.A160k => 160,
                _ => 0,
            }));

    /// <summary>
    /// Returns the warning text when the enabled destinations' combined
    /// bitrate exceeds <see cref="WarnFraction"/> of the cap, else
    /// <c>null</c>. No secrets — destinations only contribute numbers.
    /// </summary>
    public static string? Evaluate(IReadOnlyList<RestreamDestination> destinations, int capKbps)
    {
        if (destinations.Count == 0 || !destinations.Any(d => d.Enabled))
        {
            return null;
        }

        var cap = capKbps > 0 ? capKbps : DefaultCapKbps;
        var threshold = (int)(cap * WarnFraction);
        var total = EstimateTotalKbps(destinations);
        if (total <= threshold)
        {
            return null;
        }

        var passThroughNote = destinations.Any(d => d.Enabled && d.Resolution == RestreamResolution.Source)
            ? " — plus your full OBS bitrate for each pass-through \"Source\" destination"
            : string.Empty;

        return
            $"Total upload ≈{total / 1000.0:0.#} Mbps across destinations, above {threshold / 1000.0:0.#} Mbps "
            + $"(80% of the {cap / 1000.0:0.#} Mbps uplink budget){passThroughNote}. "
            + "A weaker connection will drop frames — lower some bitrates or raise the uplink budget.";
    }
}
