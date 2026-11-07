using System.Net.Http;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Services;

/// <summary>Connection settings for the command marketplace (stored in profile Configs).</summary>
public sealed class MarketplaceSettings
{
    public const string KeyGitHubOwner = "Marketplace.GitHubOwner";
    public const string KeyGitHubRepo = "Marketplace.GitHubRepo";
    public const string KeyBranch = "Marketplace.Branch";
    public const string KeyShareWebhookUrl = "Marketplace.ShareWebhookUrl";
    public const string KeyRatingWebhookUrl = "Marketplace.RatingWebhookUrl";
    public const string KeyApiKey = "Marketplace.ApiKey";
    public const string KeyAuthor = "Marketplace.LastAuthor";

    private const string DefaultOwner = "ryanthabot";
    private const string DefaultRepo = "vyral";
    private const string DefaultBranch = "main";

    public string GitHubOwner { get; set; } = DefaultOwner;
    public string GitHubRepo { get; set; } = DefaultRepo;
    public string Branch { get; set; } = DefaultBranch;
    public string? ShareWebhookUrl { get; set; }
    public string? RatingWebhookUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? LastAuthor { get; set; }

    /// <summary>True when cloud sharing can be attempted (browse works without the BotGhost keys).</summary>
    public bool IsShareConfigured =>
        !string.IsNullOrWhiteSpace(ShareWebhookUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(GitHubOwner) &&
        !string.IsNullOrWhiteSpace(GitHubRepo);

    public bool IsRatingConfigured =>
        !string.IsNullOrWhiteSpace(RatingWebhookUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Loads settings from profile Configs, falling back to the defaults above.</summary>
    public static async Task<MarketplaceSettings> LoadAsync(IDataService dataService)
    {
        string owner = await dataService.GetConfigValueAsync(KeyGitHubOwner);
        string repo = await dataService.GetConfigValueAsync(KeyGitHubRepo);
        string branch = await dataService.GetConfigValueAsync(KeyBranch);

        return new MarketplaceSettings
        {
            GitHubOwner = string.IsNullOrWhiteSpace(owner) ? DefaultOwner : owner.Trim(),
            GitHubRepo = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim(),
            Branch = string.IsNullOrWhiteSpace(branch) ? DefaultBranch : branch.Trim(),
            ShareWebhookUrl = Clean(await dataService.GetConfigValueAsync(KeyShareWebhookUrl)),
            RatingWebhookUrl = Clean(await dataService.GetConfigValueAsync(KeyRatingWebhookUrl)),
            ApiKey = Clean(await dataService.GetConfigValueAsync(KeyApiKey)),
            LastAuthor = Clean(await dataService.GetConfigValueAsync(KeyAuthor))
        };
    }

    public async Task SaveAsync(IDataService dataService)
    {
        await dataService.SetConfigValueAsync(KeyGitHubOwner, GitHubOwner?.Trim() ?? string.Empty);
        await dataService.SetConfigValueAsync(KeyGitHubRepo, GitHubRepo?.Trim() ?? string.Empty);
        await dataService.SetConfigValueAsync(KeyBranch, Branch?.Trim() ?? string.Empty);
        await dataService.SetConfigValueAsync(KeyShareWebhookUrl, ShareWebhookUrl?.Trim() ?? string.Empty);
        await dataService.SetConfigValueAsync(KeyRatingWebhookUrl, RatingWebhookUrl?.Trim() ?? string.Empty);
        await dataService.SetConfigValueAsync(KeyApiKey, ApiKey?.Trim() ?? string.Empty);
        await dataService.SetConfigValueAsync(KeyAuthor, LastAuthor?.Trim() ?? string.Empty);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The portable share document stored as marketplace/commands/&lt;code&gt;.json.</summary>
public sealed class ShareDocument
{
    public string Sharecode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = MarketplaceCategories.Other;
    public string? Author { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Raw JSON of the <c>.flow.json</c> document (FlowSnapshotService shape).</summary>
    public string Flow { get; set; } = string.Empty;
}

/// <summary>A marketplace listing row (command metadata + rating counts).</summary>
public sealed class MarketplaceEntry
{
    public string Sharecode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = MarketplaceCategories.Other;
    public string? Author { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public int Up { get; set; }
    public int Down { get; set; }

    /// <summary>"by PixelQueen · 2026-09-08" line for the listing card (empty pieces are skipped).</summary>
    public string ByLine
    {
        get
        {
            string date = string.Empty;
            if (DateTime.TryParse(CreatedAt, out var parsed))
            {
                date = parsed.ToString("yyyy-MM-dd");
            }

            string author = string.IsNullOrWhiteSpace(Author) ? string.Empty : $"by {Author!.Trim()}";
            return string.Join("  ·  ", new[] { author, date }.Where(part => part.Length > 0));
        }
    }
}

/// <summary>
/// Result of a marketplace listing fetch: the entries plus a friendly error
/// when the store could not be reached (offline / rate-limited). A null error
/// with zero entries means the store is genuinely empty; a non-null error
/// means the shown entries may be stale cache (or none at all).
/// </summary>
public sealed record MarketplaceListing(IReadOnlyList<MarketplaceEntry> Entries, string? Error)
{
    public static MarketplaceListing Empty { get; } = new(Array.Empty<MarketplaceEntry>(), null);
}

/// <summary>Result of resolving a pasted share/offline code into an importable snapshot.</summary>
public sealed record CodeImportResult(FlowEditorSnapshotDto? Snapshot, string Label, string? Error)
{
    public bool Success => Snapshot != null;
}

/// <summary>Fixed marketplace sections.</summary>
public static class MarketplaceCategories
{
    public const string Tickets = "tickets";
    public const string Support = "support";
    public const string Giveaways = "giveaways";
    public const string Games = "games";
    public const string Economy = "economy";
    public const string Other = "other";

    public static readonly string[] All = { Tickets, Support, Giveaways, Games, Economy, Other };

    public static string Normalize(string? category) =>
        All.FirstOrDefault(c => string.Equals(c, category?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Other;
}

/// <summary>
/// Client for the command marketplace: GitHub is the store (read path, unauthenticated)
/// and a BotGhost bot is the write relay holding the GitHub token — the app only POSTs
/// BotGhost webhook events and never holds repository credentials.
/// </summary>
public class MarketplaceService
{
    private const string CommandsFolder = "marketplace/commands";
    private const string RatingsFolder = "marketplace/ratings";
    private static readonly TimeSpan ListingCacheLifetime = TimeSpan.FromMinutes(10);
    private const int MaxParallelFetches = 8;

    private static readonly JsonSerializerOptions WebJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly IDataService _dataService;
    private readonly ILogger<MarketplaceService> _logger;

    private (string CacheKey, DateTime At, List<MarketplaceEntry> Entries)? _listingCache;

    public MarketplaceService(IDataService dataService, ILogger<MarketplaceService> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    // ==================================================================================
    // Settings
    // ==================================================================================

    public Task<MarketplaceSettings> LoadSettingsAsync() => MarketplaceSettings.LoadAsync(_dataService);

    public async Task SaveSettingsAsync(MarketplaceSettings settings)
    {
        await settings.SaveAsync(_dataService);
        _listingCache = null;
    }

    // ==================================================================================
    // Pure helpers (unit-tested) — sharecodes, offline codes, paths, ratings
    // ==================================================================================

    public const string SharecodePrefix = "VYRAL-";
    public const string OfflinePrefix = "VYRAL1-";

    /// <summary>Crockford base32 alphabet (no I/L/O/U so codes stay readable aloud).</summary>
    public const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int CodeBodyLength = 10;

    /// <summary>Generates a new sharecode, e.g. "VYRAL-7K3F9M2Q4D".</summary>
    public static string GenerateShareCode()
    {
        Span<char> chars = stackalloc char[CodeBodyLength];
        for (int i = 0; i < CodeBodyLength; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
        return SharecodePrefix + new string(chars);
    }

    /// <summary>
    /// Normalizes user input into a canonical sharecode: trims, uppercases,
    /// fixes common look-alike characters and restores the prefix. Accepts a
    /// bare code body, a full code, or a URL containing one (collection stops
    /// at the first non-code character, so "…7K3F9M2Q4D.json" stays clean).
    /// </summary>
    public static bool TryNormalizeShareCode(string? input, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate = input!.Trim().ToUpperInvariant();

        // Find the prefix BEFORE look-alike fixes — the prefix's own 'L' must
        // not be rewritten to '1' ("VYRAL-" would become "VYRA1-").
        int prefixIndex = candidate.IndexOf(SharecodePrefix, StringComparison.Ordinal);
        bool hasPrefix = prefixIndex >= 0;
        if (!hasPrefix && candidate.Contains(OfflinePrefix, StringComparison.Ordinal))
        {
            return false; // caller should treat this as an offline code instead
        }

        string remainder = hasPrefix ? candidate[(prefixIndex + SharecodePrefix.Length)..] : candidate;
        remainder = remainder.Replace('O', '0').Replace('I', '1').Replace('L', '1').Replace('U', 'V');

        string body = CollectCodeBody(remainder, 0);
        if (body.Length != CodeBodyLength)
        {
            return false;
        }

        code = SharecodePrefix + body;
        return true;
    }

    /// <summary>Collects code-alphabet characters until the first non-code character or the end.</summary>
    private static string CollectCodeBody(string text, int start)
    {
        var chars = new List<char>(CodeBodyLength);
        for (int i = start; i < text.Length && chars.Count < CodeBodyLength; i++)
        {
            if (!CodeAlphabet.Contains(text[i]))
            {
                break;
            }
            chars.Add(text[i]);
        }
        return new string(chars.ToArray());
    }

    /// <summary>
    /// Hard import ceilings for pasted codes: an offline code (base64 text)
    /// may not exceed ~4 MB and its decompressed flow document ~4 MB — both to
    /// keep a hostile paste (gzip bomb / oversized flow) from draining memory.
    /// </summary>
    public const int MaxOfflineCodeChars = 4_000_000;
    public const int MaxFlowJsonChars = 4_000_000;

    /// <summary>Wraps a raw flow document into a fully self-contained paste-able code.</summary>
    public static string EncodeOfflineCode(string flowJson)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(flowJson);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(plaintext, 0, plaintext.Length);
        }

        return OfflinePrefix + Base64UrlEncode(compressed.ToArray());
    }

    /// <summary>Decodes an offline paste-code back into the raw flow document.</summary>
    public static bool TryDecodeOfflineCode(string? code, out string flowJson)
    {
        flowJson = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string candidate = code.Trim();
        if (candidate.Length > MaxOfflineCodeChars)
        {
            return false;
        }

        int index = candidate.IndexOf(OfflinePrefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        try
        {
            byte[] compressed = Base64UrlDecode(candidate[(index + OfflinePrefix.Length)..]);
            using var source = new MemoryStream(compressed);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();

            // Bounded decompression: a small compressed payload can expand to
            // gigabytes, so stop (and fail) once the flow document ceiling is passed.
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxFlowJsonChars)
                {
                    return false;
                }

                output.Write(buffer, 0, read);
            }

            flowJson = Encoding.UTF8.GetString(output.ToArray());
            return flowJson.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool LooksLikeOfflineCode(string? input) =>
        !string.IsNullOrWhiteSpace(input) && input!.TrimStart().StartsWith(OfflinePrefix, StringComparison.OrdinalIgnoreCase);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        string padded = text.Trim().Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    public static string CommandFilePath(string sharecode) => $"{CommandsFolder}/{sharecode}.json";
    public static string RatingFilePath(string sharecode) => $"{RatingsFolder}/{sharecode}.json";

    public static string RawFileUrl(MarketplaceSettings settings, string path) =>
        $"https://raw.githubusercontent.com/{settings.GitHubOwner}/{settings.GitHubRepo}/{settings.Branch}/{path}";

    public static string ListApiUrl(MarketplaceSettings settings, string folder) =>
        $"https://api.github.com/repos/{settings.GitHubOwner}/{settings.GitHubRepo}/contents/{folder}?ref={Uri.EscapeDataString(settings.Branch)}";

    /// <summary>Applies a vote to current counts (races may drop one vote — acceptable).</summary>
    public static (int Up, int Down) ApplyVote(bool thumbsUp, int currentUp, int currentDown) =>
        thumbsUp ? (currentUp + 1, currentDown) : (currentUp, currentDown + 1);

    /// <summary>Builds the share document JSON for a parsed editor snapshot.</summary>
    public static string BuildShareDocumentJson(
        FlowEditorSnapshotDto snapshot, string sharecode, string name, string? description,
        string category, string? author)
    {
        var document = new ShareDocument
        {
            Sharecode = sharecode,
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled command" : name.Trim(),
            Description = Clean(description),
            Category = MarketplaceCategories.Normalize(category),
            Author = Clean(author),
            CreatedAt = DateTime.UtcNow.ToString("o"),
            SchemaVersion = 1,
            Flow = FlowSnapshotService.BuildDocument(snapshot, author)
        };
        return JsonSerializer.Serialize(document, WebJson);
    }

    public static ShareDocument? ParseShareDocument(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ShareDocument>(json, WebJson);
            if (document == null || string.IsNullOrWhiteSpace(document.Sharecode) || string.IsNullOrWhiteSpace(document.Flow))
            {
                return null;
            }

            // Newer-schema documents (written by a future Vyral build) may carry
            // node types this version cannot sanitize — refuse instead of crashing.
            if (document.SchemaVersion > CurrentSchemaVersion || document.Flow.Length > MaxFlowJsonChars)
            {
                return null;
            }

            document.Category = MarketplaceCategories.Normalize(document.Category);
            return document;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Schema version this build writes and understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Maps an import-time exception to a user-safe message — never surfaces
    /// raw exception text (host names, stack details) in the UI; the full
    /// exception is still logged by the caller.
    /// </summary>
    public static string DescribeImportError(Exception ex) => ex switch
    {
        HttpRequestException => "could not reach the marketplace — check your internet connection and try again.",
        TaskCanceledException => "the request timed out — try again in a moment.",
        IOException or UnauthorizedAccessException => ex.Message, // local file problems (locked, permissions) are safe and useful to show
        _ => "an unexpected error occurred (details are in the log)."
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ==================================================================================
    // Import resolution — one path for online sharecodes and offline codes
    // ==================================================================================

    /// <summary>
    /// Resolves a pasted import input (offline code, online sharecode, or
    /// URL containing one) into an editor snapshot. Offline codes decode
    /// locally; online codes are fetched from the marketplace.
    /// </summary>
    public async Task<CodeImportResult> ResolveImportInputAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new CodeImportResult(null, string.Empty, "Paste a share code first.");
        }

        if (input.Trim().Length > MaxOfflineCodeChars)
        {
            return new CodeImportResult(null, string.Empty, "That code is too large to be a Vyral share code.");
        }

        if (LooksLikeOfflineCode(input))
        {
            if (TryDecodeOfflineCode(input, out string flowJson))
            {
                var offlineSnapshot = FlowSnapshotService.ParseDocument(flowJson);
                if (offlineSnapshot != null)
                {
                    return new CodeImportResult(offlineSnapshot, "offline share code", null);
                }
            }

            return new CodeImportResult(null, string.Empty, "Import failed: that offline code is not a valid Vyral flow.");
        }

        if (!TryNormalizeShareCode(input, out string code))
        {
            return new CodeImportResult(null, string.Empty, "That does not look like a share code — expected VYRAL-XXXXXXXXXX or a VYRAL1- offline code.");
        }

        try
        {
            var document = await FetchCommandAsync(code);
            var snapshot = document == null ? null : FlowSnapshotService.ParseDocument(document.Flow);
            if (snapshot == null)
            {
                return new CodeImportResult(null, string.Empty, $"Import failed: {code} was not found in the marketplace.");
            }

            return new CodeImportResult(snapshot, $"{document!.Name} ({code})", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import by code failed for {Code}", code);
            return new CodeImportResult(null, string.Empty, $"Import failed: {DescribeImportError(ex)}");
        }
    }

    // ==================================================================================
    // Read path — GitHub (unauthenticated)
    // ==================================================================================

    /// <summary>
    /// Lists all shared commands (10-minute cache). Never throws for
    /// store/network problems: the result carries the (possibly cached, possibly
    /// empty) entries plus a friendly error explaining why the store could not
    /// be refreshed — so the UI can distinguish "empty store" from "offline".
    /// </summary>
    public async Task<MarketplaceListing> ListAsync(CancellationToken cancellationToken = default)
    {
        var settings = await MarketplaceSettings.LoadAsync(_dataService);
        if (string.IsNullOrWhiteSpace(settings.GitHubOwner) || string.IsNullOrWhiteSpace(settings.GitHubRepo))
        {
            return MarketplaceListing.Empty;
        }

        string cacheKey = $"{settings.GitHubOwner}/{settings.GitHubRepo}@{settings.Branch}";
        if (_listingCache is { } cache && cache.CacheKey == cacheKey && DateTime.UtcNow - cache.At < ListingCacheLifetime)
        {
            return new MarketplaceListing(cache.Entries, null);
        }

        MarketplaceListing Stale(string error)
        {
            IReadOnlyList<MarketplaceEntry> entries =
                _listingCache?.CacheKey == cacheKey ? _listingCache.Value.Entries : Array.Empty<MarketplaceEntry>();
            return new MarketplaceListing(entries, error);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ListApiUrl(settings, CommandsFolder));
            request.Headers.UserAgent.ParseAdd("VyralBot-Marketplace");
            using var response = await Http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 404)
            {
                // Empty/new marketplace repo — normal, cache as empty.
                _listingCache = (cacheKey, DateTime.UtcNow, new List<MarketplaceEntry>());
                return MarketplaceListing.Empty;
            }

            if ((int)response.StatusCode is 403 or 429)
            {
                // GitHub's unauthenticated API rate limit (60 req/h/IP) — the
                // per-entry raw fetches usually still work, so keep stale data.
                _logger.LogWarning("Marketplace listing rate-limited ({Status}) for {Repo}", (int)response.StatusCode, cacheKey);
                return Stale("GitHub is rate-limiting this connection — showing cached commands. Wait a minute, then press REFRESH.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Marketplace listing returned {Status} for {Repo}", (int)response.StatusCode, cacheKey);
                return Stale($"Could not reach the GitHub store (HTTP {(int)response.StatusCode}) — press REFRESH to retry.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return MarketplaceListing.Empty;
            }

            var codes = json.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object &&
                            e.TryGetProperty("name", out var name) &&
                            name.ValueKind == JsonValueKind.String &&
                            (name.GetString()?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(e => e.GetProperty("name").GetString()![..^5])
                .Where(name => TryNormalizeShareCode(name, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var entries = new ConcurrentQueue<MarketplaceEntry>();
            foreach (var chunk in codes.Chunk(MaxParallelFetches))
            {
                await Task.WhenAll(chunk.Select(code => FetchEntryIntoAsync(settings, code, entries, cancellationToken)));
            }

            var list = entries.OrderBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _listingCache = (cacheKey, DateTime.UtcNow, list);
            return new MarketplaceListing(list, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace listing failed for {Repo}", cacheKey);
            return Stale("Could not reach the GitHub store — check your connection and press REFRESH.");
        }
    }

    private async Task FetchEntryIntoAsync(MarketplaceSettings settings, string code, ConcurrentQueue<MarketplaceEntry> sink, CancellationToken ct)
    {
        try
        {
            string? json = await DownloadStringAsync(RawFileUrl(settings, CommandFilePath(code)), ct);
            if (json == null)
            {
                return;
            }

            var document = ParseShareDocument(json);
            if (document == null)
            {
                return;
            }

            var (up, down) = await FetchRatingsAsync(settings, document.Sharecode, ct);
            sink.Enqueue(new MarketplaceEntry
            {
                Sharecode = document.Sharecode,
                Name = document.Name,
                Description = document.Description,
                Category = document.Category,
                Author = document.Author,
                CreatedAt = document.CreatedAt,
                Up = up,
                Down = down
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipping unreadable marketplace entry {Code}", code);
        }
    }

    /// <summary>Fetches one shared command by code; null when it does not exist.</summary>
    public async Task<ShareDocument?> FetchCommandAsync(string sharecode, CancellationToken cancellationToken = default)
    {
        var settings = await MarketplaceSettings.LoadAsync(_dataService);
        string? json = await DownloadStringAsync(RawFileUrl(settings, CommandFilePath(sharecode)), cancellationToken);
        return json == null ? null : ParseShareDocument(json);
    }

    public async Task<(int Up, int Down)> FetchRatingsAsync(string sharecode, CancellationToken cancellationToken = default)
    {
        var settings = await MarketplaceSettings.LoadAsync(_dataService);
        return await FetchRatingsAsync(settings, sharecode, cancellationToken);
    }

    /// <summary>
    /// Fetches the GitHub blob SHA of a ratings file (null when it does not
    /// exist yet). GitHub's update API requires it; the BotGhost rating event
    /// branches on it (empty → create, otherwise → update).
    /// </summary>
    public async Task<string?> FetchRatingShaAsync(string sharecode, CancellationToken cancellationToken = default)
    {
        var settings = await MarketplaceSettings.LoadAsync(_dataService);
        string url = $"https://api.github.com/repos/{settings.GitHubOwner}/{settings.GitHubRepo}/contents/{RatingFilePath(sharecode)}?ref={Uri.EscapeDataString(settings.Branch)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("VyralBot-Marketplace");
            using var response = await Http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 404)
            {
                return null; // no ratings file yet — BotGhost creates it
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rating SHA fetch returned {Status} for {Code}", (int)response.StatusCode, sharecode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                   json.RootElement.TryGetProperty("sha", out var sha) &&
                   sha.ValueKind == JsonValueKind.String
                ? sha.GetString()
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rating SHA fetch failed for {Code}", sharecode);
            return null;
        }
    }

    private async Task<(int Up, int Down)> FetchRatingsAsync(MarketplaceSettings settings, string sharecode, CancellationToken ct)
    {
        try
        {
            string? json = await DownloadStringAsync(RawFileUrl(settings, RatingFilePath(sharecode)), ct);
            if (json == null)
            {
                return (0, 0);
            }

            using var doc = JsonDocument.Parse(json);
            int up = doc.RootElement.TryGetProperty("up", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : 0;
            int down = doc.RootElement.TryGetProperty("down", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0;
            return (up, down);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    private static async Task<string?> DownloadStringAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("VyralBot-Marketplace");
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    // ==================================================================================
    // Write path — BotGhost webhook events (the bot holds the GitHub token)
    // ==================================================================================

    /// <summary>
    /// Submits a shared command through the BotGhost share event. Fire-and-forget:
    /// BotGhost returns only a status code; call <see cref="WaitUntilPublishedAsync"/>
    /// to confirm the file landed on GitHub.
    /// </summary>
    public Task<(bool Ok, string Error)> SubmitShareAsync(ShareDocument document) =>
        PostToBotGhostAsync(
            settings => settings.ShareWebhookUrl,
            new Dictionary<string, string>
            {
                ["gh_path"] = CommandFilePath(document.Sharecode),
                ["gh_message"] = $"marketplace: add {document.Sharecode} ({document.Name})",
                ["gh_content"] = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, WebJson)))
            });

    /// <summary>
    /// Submits new rating counts for a sharecode through the BotGhost rating
    /// event. Pass the blob SHA from <see cref="FetchRatingShaAsync"/> (null =
    /// the file is new and BotGhost should create it).
    /// </summary>
    public Task<(bool Ok, string Error)> SubmitRatingAsync(string sharecode, int up, int down, string? sha) =>
        PostToBotGhostAsync(
            settings => settings.RatingWebhookUrl,
            new Dictionary<string, string>
            {
                ["gh_path"] = RatingFilePath(sharecode),
                ["gh_message"] = $"marketplace: rate {sharecode} (up {up}, down {down})",
                ["gh_content"] = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { up, down }, WebJson))),
                ["gh_sha"] = sha ?? string.Empty
            });

    private async Task<(bool Ok, string Error)> PostToBotGhostAsync(
        Func<MarketplaceSettings, string?> urlSelector,
        Dictionary<string, string> variables)
    {
        var settings = await MarketplaceSettings.LoadAsync(_dataService);
        string? url = urlSelector(settings);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return (false, "Marketplace is not configured. Open the Marketplace and set the BotGhost webhook URL + API key.");
        }

        try
        {
            var payload = new
            {
                variables = variables.Select(kv => new { name = kv.Key, variable = "{" + kv.Key + "}", value = kv.Value }).ToList()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url.Trim())
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, WebJson), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

            using var response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return (true, string.Empty);
            }

            string body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("BotGhost webhook returned {Status}: {Body}", (int)response.StatusCode, body);
            return (false, $"BotGhost rejected the request (HTTP {(int)response.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BotGhost webhook POST failed");
            return (false, $"Could not reach BotGhost: {ex.Message}");
        }
    }

    /// <summary>
    /// Polls GitHub until the shared file is publicly visible (BotGhost is async).
    /// Returns true once found; false after the timeout — the share may still land later.
    /// </summary>
    public async Task<bool> WaitUntilPublishedAsync(string sharecode, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var settings = await MarketplaceSettings.LoadAsync(_dataService);
        string url = RawFileUrl(settings, CommandFilePath(sharecode));
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
        while (DateTime.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            string? body = await DownloadStringAsync(url, cancellationToken);
            if (body != null)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        return false;
    }
}
