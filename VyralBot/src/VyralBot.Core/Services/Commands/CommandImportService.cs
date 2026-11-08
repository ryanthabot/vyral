using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Commands;

/// <summary>
/// <see cref="ICommandImportService"/> implementation. Three pure parsers
/// (Nightbot CSV, StreamElements JSON, plain <c>!name=response</c> text)
/// build an <see cref="CommandImportPreview"/> without touching the
/// database; <see cref="ApplyAsync"/> then registers the importable rows as
/// enabled custom commands on every platform. Nightbot/StreamElements
/// variables are translated through a static map (see
/// <see cref="TranslateVariables"/>) so <c>$(user)</c> style tokens become
/// Vyral's <c>{username}</c> style; unmapped tokens stay verbatim and are
/// flagged on the row.
/// </summary>
public partial class CommandImportService : ICommandImportService
{
    public const string FormatAuto = "auto";
    public const string FormatNightbot = "nightbot";
    public const string FormatStreamElements = "streamelements";
    public const string FormatPlain = "plain";

    /// <summary>
    /// Nightbot's own default commands — importing them would shadow real
    /// Nightbot features Vyral does not have, so they are always skipped.
    /// </summary>
    private static readonly HashSet<string> NightbotBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "commands", "command", "regulars", "regular", "nightbot",
        "songrequest", "sr", "songs", "skip", "next", "volume",
        "playlist", "queue", "q", "promote", "filter", "betamode",
        "title", "game"
    };

    /// <summary>
    /// VyralBot's built-in command families (seeded rows, the reserved link
    /// family, the currency aliases from the HELP tab and the StreamTools
    /// commands). Names that match one of these are skipped at parse time —
    /// ApplyAsync re-checks the live database for everything else.
    /// </summary>
    private static readonly HashSet<string> AppBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        // seeded built-in rows
        "commands", "help", "uptime", "socials", "discord", "watchtime",
        "points", "leaderboard", "gamble", "give", "take", "daily", "rank",
        "slots", "duel", "accept", "heist", "roulette", "store", "rewards",
        "redeem",
        // reserved link family + currency/quote aliases (HELP tab)
        "link", "linkcode", "linked", "unlink", "delink",
        "balance", "currency", "tier", "top", "bet", "shop",
        "quote", "quotecount", "goals", "mystatus", "mp", "multiplatform",
        "poll", "giveaway", "auction", "boost",
        // StreamTools / TTS live commands
        "so", "raid", "title", "game", "commercial", "followage", "tts"
    };

    /// <summary>
    /// Source variable → Vyral token. Covers the tokens with a direct Vyral
    /// equivalent; Nightbot/SE specials ($(eval), $(random.pick a, b),
    /// $(urlfetch ...), $(query) ...) are deliberately absent so they stay
    /// verbatim and get flagged in the row notes.
    /// </summary>
    private static readonly Dictionary<string, string> VariableTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "username",       // $(user) / $(touser) / ${user} → {username}
        ["touser"] = "username",
        ["count"] = "count",         // command counter → {count}
        ["time"] = "time",           // plain $(time) (no timezone arg) → {time}
        ["uptime"] = "uptime",       // $(uptime) / $(uptimeapi) → {uptime}
        ["uptimeapi"] = "uptime",
        ["watchtime"] = "watchtime", // $(watchtime) → {watchtime}
        ["channel"] = "channel"      // $(channel) → {channel}
    };

    /// <summary>$(name) and ${name} tokens WITHOUT arguments (those with args stay verbatim).</summary>
    [GeneratedRegex(@"\$[({]([A-Za-z0-9_]+)[)}]", RegexOptions.Compiled)]
    private static partial Regex SourceVariableRegex();

    private readonly ICommandService _commandService;
    private readonly ILogger<CommandImportService> _logger;

    public CommandImportService(ICommandService commandService, ILogger<CommandImportService> logger)
    {
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------

    public Task<CommandImportPreview> ParseAsync(string content, string format)
    {
        if (content == null)
        {
            content = "";
        }

        var resolvedFormat = NormalizeFormat(string.IsNullOrWhiteSpace(format) ? FormatAuto : format);
        if (resolvedFormat == FormatAuto)
        {
            resolvedFormat = DetectFormat(content);
        }

        var preview = new CommandImportPreview { Format = resolvedFormat };
        switch (resolvedFormat)
        {
            case FormatNightbot:
                ParseNightbotCsv(content, preview);
                break;
            case FormatStreamElements:
                ParseStreamElementsJson(content, preview);
                break;
            default:
                ParsePlainText(content, preview);
                break;
        }

        _logger.LogInformation("Parsed {Importable} importable and {Skipped} skipped command(s) from a {Format} export",
            preview.ImportableCount, preview.SkippedCount, resolvedFormat);
        return Task.FromResult(preview);
    }

    public string DetectFormat(string content)
    {
        var trimmed = (content ?? "").TrimStart();
        if (trimmed.StartsWith('[') || trimmed.StartsWith('{'))
        {
            return FormatStreamElements;
        }

        // Nightbot CSV sniff: the first record's first field is the header
        // word "command" or a !name — and never contains '=' (a plain-text
        // "!hello=hi, welcome, enjoy" line would otherwise look like CSV).
        foreach (var line in SplitLines(trimmed))
        {
            var fields = ParseCsvRecord(line);
            if (fields.Count >= 3)
            {
                var first = fields[0].Trim();
                if (!first.Contains('=', StringComparison.Ordinal)
                    && (first.Equals("command", StringComparison.OrdinalIgnoreCase) || first.StartsWith('!')))
                {
                    return FormatNightbot;
                }
            }

            break; // only the first non-blank line decides
        }

        return FormatPlain;
    }

    private static string NormalizeFormat(string format)
    {
        var f = format.Trim();
        if (f.Equals(FormatAuto, StringComparison.OrdinalIgnoreCase))
        {
            return FormatAuto;
        }

        if (f.Equals(FormatNightbot, StringComparison.OrdinalIgnoreCase)
            || f.Equals("nightbot-csv", StringComparison.OrdinalIgnoreCase))
        {
            return FormatNightbot;
        }

        if (f.Equals(FormatStreamElements, StringComparison.OrdinalIgnoreCase)
            || f.Equals("se", StringComparison.OrdinalIgnoreCase))
        {
            return FormatStreamElements;
        }

        if (f.Equals(FormatPlain, StringComparison.OrdinalIgnoreCase)
            || f.Equals("text", StringComparison.OrdinalIgnoreCase)
            || f.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            return FormatPlain;
        }

        throw new ArgumentException($"Unknown import format '{format}'. Use nightbot, streamelements, plain or auto.", nameof(format));
    }

    // ------------------------------------------------------------------
    // Nightbot CSV
    // ------------------------------------------------------------------

    /// <summary>
    /// Nightbot CSV export: <c>command,userlevel,cooldown,response,text</c>
    /// (header row optional — with the real export's count column it is
    /// required, and columns are then mapped by name). userlevel
    /// everyone/moderator maps to Everyone/Moderator permission, cooldown
    /// seconds become the per-user cooldown, and Nightbot built-ins plus
    /// Vyral built-ins are skipped with a reason.
    /// </summary>
    private void ParseNightbotCsv(string content, CommandImportPreview preview)
    {
        var records = ParseCsv(content);
        if (records.Count == 0)
        {
            return;
        }

        // Header mapping when the first record is a header row.
        Dictionary<string, int>? columns = null;
        var first = records[0];
        if (first.Count > 0 && first[0].Trim().Equals("command", StringComparison.OrdinalIgnoreCase))
        {
            columns = new Dictionary<string, int>(first.Count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < first.Count; i++)
            {
                columns[first[i].Trim()] = i; // accepts the "coolown" header typo too
            }

            records.RemoveAt(0);
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record.Count == 0 || string.IsNullOrWhiteSpace(record[0]))
            {
                continue; // stray blank line
            }

            string name, userlevel, cooldownText, response, text;
            if (columns != null)
            {
                name = Field(record, columns, "command");
                userlevel = Field(record, columns, "userlevel");
                cooldownText = Field(record, columns, "cooldown", "coolown");
                response = Field(record, columns, "response");
                text = Field(record, columns, "text");
            }
            else
            {
                // Positional: command,userlevel,cooldown,response,text
                name = Field(record, 0);
                userlevel = Field(record, 1);
                cooldownText = Field(record, 2);
                response = Field(record, 3);
                text = Field(record, 4);
            }

            var row = BuildRow(NormalizeName(name), "Nightbot", response, text,
                userlevel, cooldownText, globalCooldownText: null, seenNames);
            preview.Rows.Add(row);
        }
    }

    private static string Field(IReadOnlyList<string> record, Dictionary<string, int> columns, params string[] names)
    {
        foreach (var name in names)
        {
            if (columns.TryGetValue(name, out var index) && index < record.Count)
            {
                return record[index];
            }
        }

        return "";
    }

    private static string Field(IReadOnlyList<string> record, int index)
        => index >= 0 && index < record.Count ? record[index] : "";

    // ------------------------------------------------------------------
    // StreamElements JSON
    // ------------------------------------------------------------------

    /// <summary>
    /// StreamElements JSON export: an array of
    /// <c>{command, reply, enabled, cooldownAll, cooldownUser, permission}</c>
    /// objects (a root object with an array property, e.g. {"commands": [...]},
    /// is accepted too). Same permission/cooldown/skip mapping as Nightbot;
    /// cooldownAll additionally becomes the global cooldown.
    /// </summary>
    private void ParseStreamElementsJson(string content, CommandImportPreview preview)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "StreamElements import: content is not valid JSON");
            return; // empty preview — nothing importable
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                // {"commands":[...]} style wrappers: take the first array property.
                var found = false;
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        root = property.Value;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return;
                }
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = JsonString(element, "command");
                var reply = JsonString(element, "reply");
                var response = JsonString(element, "response");
                var permission = JsonString(element, "permission");
                var cooldownUser = JsonString(element, "cooldownUser");
                var cooldownAll = JsonString(element, "cooldownAll");

                var row = BuildRow(NormalizeName(name), "StreamElements", reply, response,
                    permission, cooldownUser, cooldownAll, seenNames);
                preview.Rows.Add(row);
            }
        }
    }

    private static string JsonString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            && (value.ValueKind == JsonValueKind.String || value.ValueKind == JsonValueKind.Number)
                ? value.ToString()
                : "";

    // ------------------------------------------------------------------
    // Plain text
    // ------------------------------------------------------------------

    /// <summary>Plain-text fallback: one <c>!name=response</c> per line.</summary>
    private void ParsePlainText(string content, CommandImportPreview preview)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(content))
        {
            if (line.Length > 0 && line[0] == '#')
            {
                continue; // comment line
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                preview.Rows.Add(new CommandImportRow
                {
                    Name = "",
                    Response = line,
                    Source = "Text",
                    Skipped = true,
                    SkipReason = "Not a !name=response line"
                });
                continue;
            }

            var row = BuildRow(NormalizeName(line[..separator]), "Text",
                line[(separator + 1)..], "", "", "", "", seenNames);
            preview.Rows.Add(row);
        }
    }

    // ------------------------------------------------------------------
    // Shared row construction
    // ------------------------------------------------------------------

    /// <summary>
    /// Shared mapping for every parser: name checks (invalid / Nightbot
    /// built-in / Vyral built-in / duplicate in file), response fallback,
    /// permission + cooldown mapping, and variable translation.
    /// </summary>
    private static CommandImportRow BuildRow(
        string? name,
        string source,
        string response,
        string fallbackResponse,
        string userlevel,
        string cooldownText,
        string? globalCooldownText,
        HashSet<string> seenNames)
    {
        var row = new CommandImportRow
        {
            Source = source,
            CooldownSec = ParseCooldownSeconds(cooldownText)
        };

        if (globalCooldownText != null)
        {
            row.GlobalCooldownSec = ParseCooldownSeconds(globalCooldownText);
        }

        if (name == null)
        {
            row.Skipped = true;
            row.SkipReason = "Invalid or missing command name";
            return row;
        }

        row.Name = name;

        var responseText = string.IsNullOrWhiteSpace(response) ? fallbackResponse : response;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            row.Skipped = true;
            row.SkipReason = "No response text";
            return row;
        }

        var bareName = name[1..]; // without the normalized '!' prefix
        if (NightbotBuiltIns.Contains(bareName))
        {
            row.Skipped = true;
            row.SkipReason = "Nightbot built-in";
            return row;
        }

        if (AppBuiltIns.Contains(bareName))
        {
            row.Skipped = true;
            row.SkipReason = $"Vyral already has a built-in {name}";
            return row;
        }

        if (!seenNames.Add(name))
        {
            row.Skipped = true;
            row.SkipReason = "Duplicate of an earlier row in this file";
            return row;
        }

        row.ModOnly = IsModLevel(userlevel);
        row.Response = TranslateVariables(responseText.Trim(), out var untranslated);
        if (untranslated.Count > 0)
        {
            row.Notes = $"Untranslated tokens left as-is: {string.Join(", ", untranslated)}";
        }

        return row;
    }

    /// <summary>
    /// Nightbot userlevels / StreamElements permissions at moderator level
    /// and above (moderator, master/owner/streamer) become ModOnly;
    /// everything else (everyone, subscriber, vip, regular) imports as
    /// Everyone — the closest Vyral default.
    /// </summary>
    private static bool IsModLevel(string userlevel)
        => userlevel.Trim().ToLowerInvariant() is "moderator" or "mod" or "master" or "owner" or "streamer" or "broadcaster";

    private static int ParseCooldownSeconds(string text)
    {
        if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return Math.Clamp(seconds, 0, 86400);
        }

        return 0;
    }

    /// <summary>
    /// "!", "!Name", "name", "!!name" → "!name" (lowercased invariant); null
    /// when nothing usable is left.
    /// </summary>
    private static string? NormalizeName(string? raw)
    {
        var name = (raw ?? "").Trim().TrimStart('!');
        return name.Length == 0 ? null : "!" + name.ToLowerInvariant();
    }

    /// <summary>
    /// Translates Nightbot/StreamElements variables ($(user), ${user}, ...)
    /// to Vyral tokens ({username}, {count}, {time}, {uptime}, {watchtime},
    /// {channel}). Tokens with arguments ($(time TZ), $(random.pick a, b))
    /// and tokens without a Vyral equivalent ($(eval), $(query), ...) are
    /// left verbatim and returned in <paramref name="untranslated"/> so the
    /// preview row can flag them.
    /// </summary>
    public static string TranslateVariables(string response, out List<string> untranslated)
    {
        var localUntranslated = new List<string>(2); // out params can't be captured in lambdas
        string result;
        if (string.IsNullOrEmpty(response))
        {
            result = response;
        }
        else
        {
            result = SourceVariableRegex().Replace(response, match =>
            {
                var token = match.Groups[1].Value;
                if (VariableTranslations.TryGetValue(token, out var vyralToken))
                {
                    return "{" + vyralToken + "}";
                }

                localUntranslated.Add(match.Value);

                return match.Value; // keep verbatim
            });
        }

        untranslated = localUntranslated;
        return result;
    }

    // ------------------------------------------------------------------
    // Apply
    // ------------------------------------------------------------------

    public async Task<CommandImportResult> ApplyAsync(CommandImportPreview preview)
    {
        if (preview == null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        var result = new CommandImportResult();
        foreach (var row in preview.Rows)
        {
            if (row.Skipped)
            {
                continue;
            }

            // Never overwrite: a row that exists (built-in OR custom) is
            // skipped and reported. RegisterCommandAsync would happily
            // replace an existing custom row, so the check must come first.
            if (await _commandService.GetCommandByNameAsync(row.Name) != null)
            {
                row.Skipped = true;
                row.SkipReason = "Already exists";
                result.SkippedCount++;
                result.SkippedNames.Add(row.Name);
                continue;
            }

            var command = new Command
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = row.Name,
                Response = row.Response,
                Permission = row.ModOnly ? "Moderator" : "Everyone",
                GlobalCooldown = row.GlobalCooldownSec,
                UserCooldown = row.CooldownSec,
                IsEnabled = true,
                IsBuiltIn = false,
                EnabledOnKick = true,
                EnabledOnTwitch = true,
                BotId = null,
                UsageCount = 0,
                LastUsedAt = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (!await _commandService.RegisterCommandAsync(command))
            {
                // RegisterCommandAsync refuses names shadowing a built-in —
                // normally impossible after the check above, but a custom
                // row saved between parse and apply lands here.
                row.Skipped = true;
                row.SkipReason = "Already exists";
                result.SkippedCount++;
                result.SkippedNames.Add(row.Name);
                continue;
            }

            result.ImportedCount++;
            result.ImportedCommands.Add(command);
        }

        _logger.LogInformation("Command import applied: {Imported} imported, {Skipped} skipped ({SkippedNames})",
            result.ImportedCount, result.SkippedCount, string.Join(", ", result.SkippedNames));
        return result;
    }

    // ------------------------------------------------------------------
    // CSV / line helpers
    // ------------------------------------------------------------------

    private static IEnumerable<string> SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            yield break;
        }

        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>
    /// Minimal RFC 4180 CSV parser: quoted fields, embedded commas, doubled
    /// quotes and embedded newlines, CR/LF and LF line endings.
    /// </summary>
    private static List<List<string>> ParseCsv(string content)
    {
        var records = new List<List<string>>();
        if (string.IsNullOrEmpty(content))
        {
            return records;
        }

        var field = new StringBuilder();
        var record = new List<string>(8);
        var inQuotes = false;
        var fieldStarted = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();
            if (record.Count > 1 || record[0].Length > 0)
            {
                records.Add(record);
            }

            record = new List<string>(8);
        }

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && !fieldStarted)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == ',')
            {
                EndField();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        // Trailing field/record without a newline.
        if (field.Length > 0 || record.Count > 0)
        {
            EndRecord();
        }

        return records;
    }

    /// <summary>Single-record CSV parse used by the format sniffer.</summary>
    private static List<string> ParseCsvRecord(string line)
    {
        var records = ParseCsv(line);
        return records.Count > 0 ? records[0] : new List<string>();
    }
}
