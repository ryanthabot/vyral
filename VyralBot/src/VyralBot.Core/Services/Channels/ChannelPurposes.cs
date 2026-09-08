namespace VyralBot.Core.Services.Channels;

/// <summary>Canonical purpose tags stored on ConnectedChannel.Purposes as CSV.</summary>
public static class ChannelPurposes
{
    public const string Moderation = "Moderation";
    public const string Admin = "Admin";
    public const string Support = "Support";
    public const string Logging = "Logging";
    public static readonly string[] All = { Moderation, Admin, Support, Logging };

    public static bool IsValid(string? purpose) =>
        All.Any(p => string.Equals(p, purpose?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses a CSV into canonical names, ignoring case and unknown values, preserving order, de-duplicated.</summary>
    public static List<string> Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(p => All.FirstOrDefault(a => string.Equals(a, p, StringComparison.OrdinalIgnoreCase)))
                 .OfType<string>().Distinct().ToList();

    public static string ToCsv(IEnumerable<string> purposes) => string.Join(",", purposes);

    public static bool HasPurpose(string? csv, string purpose) => Parse(csv).Contains(purpose);
}
