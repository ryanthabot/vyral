using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Commands;

/// <summary>
/// Imports custom commands from other bots (Nightbot CSV export,
/// StreamElements JSON export) or a plain <c>!name=response</c> text file —
/// the #1 friction for switchers moving to VyralBot. Parsers are pure:
/// <see cref="ParseAsync"/> never touches the database, so the preview grid
/// can be built and tested without a profile. <see cref="ApplyAsync"/> then
/// creates the Command rows, skipping names that already exist.
/// </summary>
public interface ICommandImportService
{
    /// <summary>
    /// Parses an export into a preview. <paramref name="format"/> is one of
    /// "nightbot", "streamelements", "plain" (alias "text") or "auto"
    /// (case-insensitive; null/empty = auto). Rows are NOT created — the
    /// preview lists what would import and what is skipped with a reason
    /// (Nightbot built-ins, Vyral built-ins, blank responses, duplicates in
    /// the file). Nightbot/StreamElements variables (<c>$(user)</c>,
    /// <c>${user}</c>, ...) are translated to Vyral's <c>{username}</c> style;
    /// tokens without an equivalent stay untouched and are flagged in the
    /// row's <see cref="CommandImportRow.Notes"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown format name.</exception>
    Task<CommandImportPreview> ParseAsync(string content, string format);

    /// <summary>
    /// Creates Command rows for every importable preview row: enabled, all
    /// platforms, usage counter 0, not built-in. Names that already exist in
    /// the database (built-in OR custom) are skipped and reported — imports
    /// never overwrite. Rows skipped here are marked in the preview so the
    /// caller can refresh the grid.
    /// </summary>
    Task<CommandImportResult> ApplyAsync(CommandImportPreview preview);

    /// <summary>
    /// Sniffs the export format from the content itself: a JSON array/object
    /// root is StreamElements, a CSV whose first field is "command" or starts
    /// with "!" is Nightbot, anything else is plain text.
    /// </summary>
    string DetectFormat(string content);
}

/// <summary>One line of an import: what would be created, or why it is skipped.</summary>
public class CommandImportRow
{
    /// <summary>Normalized command name — "!" + lowercase word (e.g. "!discord").</summary>
    public string Name { get; set; } = "";

    /// <summary>Response text with Nightbot/SE variables translated to Vyral tokens.</summary>
    public string Response { get; set; } = "";

    /// <summary>Per-user cooldown in seconds (Nightbot "cooldown" / SE "cooldownUser").</summary>
    public int CooldownSec { get; set; }

    /// <summary>Global (all-chatters) cooldown in seconds (SE "cooldownAll"; Nightbot has none).</summary>
    public int GlobalCooldownSec { get; set; }

    /// <summary>True when the source restricted the command to mods and up.</summary>
    public bool ModOnly { get; set; }

    /// <summary>True when this row will NOT be imported.</summary>
    public bool Skipped { get; set; }

    /// <summary>Human-readable skip reason (null while the row is importable).</summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// Import note — currently "Untranslated tokens left as-is: $(eval), ..."
    /// when the response still carries Nightbot/SE variables with no Vyral
    /// equivalent (they stay verbatim for the streamer to hand-fix).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>Origin label for the preview header: "Nightbot", "StreamElements" or "Text".</summary>
    public string Source { get; set; } = "";
}

/// <summary>Result of <see cref="ICommandImportService.ParseAsync"/> — the preview grid model.</summary>
public class CommandImportPreview
{
    public List<CommandImportRow> Rows { get; } = new();

    /// <summary>Detected/explicit format label shown in the preview header.</summary>
    public string Format { get; set; } = "";

    public int ImportableCount => Rows.Count(r => !r.Skipped);

    public int SkippedCount => Rows.Count(r => r.Skipped);
}

/// <summary>Result of <see cref="ICommandImportService.ApplyAsync"/>.</summary>
public class CommandImportResult
{
    public int ImportedCount { get; set; }

    /// <summary>Rows skipped at apply time (name already exists in the database).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Names skipped at apply time, in row order (for the status message).</summary>
    public List<string> SkippedNames { get; } = new();

    /// <summary>The Command rows handed to ICommandService.RegisterCommandAsync.</summary>
    public List<Command> ImportedCommands { get; } = new();
}
