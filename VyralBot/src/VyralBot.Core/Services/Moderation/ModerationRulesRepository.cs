using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Loads / saves the auto-moderation <see cref="ModerationRuleSet"/> as JSON
/// under the <c>Moderation.AutoModRules</c> config key (per-profile SQLite via
/// <see cref="IDataService.GetConfigValueAsync"/> /
/// <see cref="IDataService.SetConfigValueAsync"/>) and applies every load/save
/// to the engine it wraps (default: <see cref="ChatModerationEngine.Shared"/>),
/// so the chat pipelines see the new rules immediately.
/// </summary>
public sealed class ModerationRulesRepository
{
    public const string ConfigKey = "Moderation.AutoModRules";
    public const string Category = "Moderation";

    private static readonly JsonSerializerOptions SerializeOptions = new(JsonSerializerDefaults.General);

    private readonly IDataService _dataService;
    private readonly IChatModerationEngine _engine;
    private readonly ILogger? _logger;

    public ModerationRulesRepository(
        IDataService dataService,
        IChatModerationEngine? engine = null,
        ILogger? logger = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _engine = engine ?? ChatModerationEngine.Shared;
        _logger = logger;
    }

    /// <summary>
    /// Reads the stored rule set; a missing key or unparseable JSON falls back
    /// to <see cref="ModerationRuleSet.CreateDefault"/> (a corrupt config never
    /// bricks chat) and the result is applied to the engine.
    /// </summary>
    public async Task<ModerationRuleSet> LoadAsync()
    {
        ModerationRuleSet rules;
        try
        {
            var json = await _dataService.GetConfigValueAsync(ConfigKey).ConfigureAwait(false);
            rules = Parse(json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Auto-mod rule load failed — using defaults");
            rules = ModerationRuleSet.CreateDefault();
        }

        _engine.ApplyRules(rules);
        // Normalize before handing the set back so the dashboard never sees a
        // null rule slot (Engine.ApplyRules normalizes its own copy).
        rules.Normalize();
        return rules.Clone();
    }

    /// <summary>Normalizes, persists and applies the rule set.</summary>
    public async Task SaveAsync(ModerationRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var normalized = rules.Clone();
        normalized.Normalize();

        var json = JsonSerializer.Serialize(normalized, SerializeOptions);
        await _dataService.SetConfigValueAsync(ConfigKey, json, Category).ConfigureAwait(false);

        _engine.ApplyRules(normalized);
        _logger?.LogInformation("Auto-mod rules saved and applied");
    }

    private static ModerationRuleSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ModerationRuleSet.CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<ModerationRuleSet>(json, SerializeOptions)
                   ?? ModerationRuleSet.CreateDefault();
        }
        catch (JsonException)
        {
            return ModerationRuleSet.CreateDefault();
        }
    }
}
