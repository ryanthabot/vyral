namespace VyralBot.Data.Entities;

public class Command
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Response { get; set; }
    public required string Permission { get; set; } = "Everyone";
    public int GlobalCooldown { get; set; } = 0;
    public int UserCooldown { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; } = false;
    public bool EnabledOnKick { get; set; } = true;
    public bool EnabledOnTwitch { get; set; } = true;

    /// <summary>
    /// Discord bot scope: the DiscordBots row id this command is pinned to.
    /// Null means the command runs for EVERY Discord bot (and, as before, on
    /// any platform enabled above).
    /// </summary>
    public string? BotId { get; set; }

    /// <summary>
    /// JSON array of alternate replies (strings), one per line in the builder.
    /// When present and non-empty, execution picks one entry AT RANDOM instead
    /// of <see cref="Response"/> (custom commands only).
    /// </summary>
    public string? ResponsesJson { get; set; }

    /// <summary>
    /// JSON array of ordered action steps ({type, ...config} objects) — the
    /// BotGhost-style ACTION SEQUENCE. When present (custom commands only),
    /// <c>CommandService.ExecuteAsync</c> does NOT run the steps (they need the
    /// live platform senders); it returns the
    /// <see cref="VyralBot.Core.Services.Commands.ICommandService.ActionsMarkerPrefix"/>
    /// marker so the UI layer's CommandActionRunner executes the sequence.
    /// </summary>
    public string? ActionsJson { get; set; }

    public int UsageCount { get; set; } = 0;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
