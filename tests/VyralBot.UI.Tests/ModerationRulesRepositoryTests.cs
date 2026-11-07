using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Moderation;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-1 fix: <see cref="ModerationRulesRepository"/> persistence
/// hardening — a corrupt or partial auto-mod config must NEVER brick chat:
///
///   1. unparseable JSON under Moderation.AutoModRules → safe defaults
///      (the curated CreateDefault set), no throw;
///   2. a data-layer failure mid-load → safe defaults too;
///   3. a partial payload with null rule slots → Normalize backfills every
///      slot before the dashboard or engine sees it;
///   4. Save → Load round-trips the rule set (including the Platforms
///      filter) and applies it to the wrapped engine immediately.
///
/// Each test drives a DEDICATED ChatModerationEngine instance (never
/// ChatModerationEngine.Shared, which the app and other tests share).
/// </summary>
public class ModerationRulesRepositoryTests
{
    private static ModerationRulesRepository MakeRepository(
        Wave2TestDataService data, ChatModerationEngine engine)
        => new(data, engine, NullLogger<ModerationRulesRepository>.Instance);

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsSafeDefaults_WithoutThrowing()
    {
        var data = new Wave2TestDataService();
        data.ConfigValues[ModerationRulesRepository.ConfigKey] = "{ this is not json";
        var engine = new ChatModerationEngine();

        var rules = await MakeRepository(data, engine).LoadAsync();

        // CreateDefault signature: master OFF (opt-in), curated rules.
        Assert.False(rules.MasterEnabled);
        Assert.True(rules.Links.Enabled);
        Assert.Empty(rules.BannedWordList);
        Assert.NotNull(rules.BannedWords);
        Assert.NotNull(rules.Escalation);
        // The fallback is applied to the engine too, not just returned.
        Assert.False(engine.Rules.MasterEnabled);
    }

    [Fact]
    public async Task LoadAsync_DataLayerFailure_ReturnsSafeDefaults()
    {
        var data = new ThrowingConfigDataService();
        var engine = new ChatModerationEngine();

        var rules = await MakeRepository(data, engine).LoadAsync();

        Assert.False(rules.MasterEnabled); // defaults, not an exception
        Assert.True(engine.Rules.Links.Enabled);
    }

    [Fact]
    public async Task LoadAsync_MissingKey_ReturnsSafeDefaults()
    {
        var data = new Wave2TestDataService();
        var engine = new ChatModerationEngine();

        var rules = await MakeRepository(data, engine).LoadAsync();

        Assert.False(rules.MasterEnabled);
        Assert.True(rules.Links.Enabled);
    }

    [Fact]
    public async Task LoadAsync_PartialJson_NullSlotsAreBackfilled()
    {
        // Valid JSON, but rule slots the payload omits/nulls must come back
        // non-null and usable (Normalize is the single backfill point). Keys
        // are PascalCase because the repository serializes with
        // JsonSerializerDefaults.General (case-sensitive matching).
        var data = new Wave2TestDataService();
        data.ConfigValues[ModerationRulesRepository.ConfigKey] =
            """{ "MasterEnabled": true, "Platforms": null, "BannedWords": null, "Escalation": null }""";
        var engine = new ChatModerationEngine();

        var rules = await MakeRepository(data, engine).LoadAsync();

        Assert.True(rules.MasterEnabled);
        Assert.NotNull(rules.BannedWords);
        Assert.NotNull(rules.Emojis);
        Assert.NotNull(rules.Escalation);
        Assert.NotNull(rules.Platforms);
        Assert.Empty(rules.Platforms);
        // Applied to the engine as well.
        Assert.True(engine.Rules.MasterEnabled);
    }

    [Fact]
    public async Task SaveAsync_PersistsUnderConfigKey_AndAppliesToEngine()
    {
        var data = new Wave2TestDataService();
        var engine = new ChatModerationEngine();
        var repository = MakeRepository(data, engine);

        var rules = ModerationRuleSet.CreateDefault();
        rules.MasterEnabled = true;
        rules.Platforms.AddRange(new[] { "Twitch", "Kick" });
        rules.BannedWordList.Add("badword");
        await repository.SaveAsync(rules);

        // Persisted under the documented key + category.
        var write = Assert.Single(data.ConfigWrites);
        Assert.Equal(ModerationRulesRepository.ConfigKey, write.Key);
        Assert.Equal(ModerationRulesRepository.Category, write.Category);
        Assert.Contains("badword", write.Value);
        // Applied to the wrapped engine immediately.
        Assert.True(engine.Rules.MasterEnabled);
        Assert.Contains("badword", engine.Rules.BannedWordList);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips_TheRuleSetIncludingPlatforms()
    {
        var data = new Wave2TestDataService();
        var engine = new ChatModerationEngine();
        var repository = MakeRepository(data, engine);

        var rules = ModerationRuleSet.CreateDefault();
        rules.MasterEnabled = true;
        rules.Platforms.AddRange(new[] { "Twitch", "Kick" });
        rules.BannedWordList.Add("badword");
        rules.Links.Threshold = 2;
        await repository.SaveAsync(rules);

        // Fresh repository + engine = simulates an app restart reading the row.
        var reloaded = await MakeRepository(data, new ChatModerationEngine()).LoadAsync();

        Assert.True(reloaded.MasterEnabled);
        Assert.Equal(new[] { "Twitch", "Kick" }, reloaded.Platforms);
        Assert.Contains("badword", reloaded.BannedWordList);
        Assert.Equal(2, reloaded.Links.Threshold);
    }

    /// <summary>An IDataService whose config read always blows up (corrupt DB simulation).</summary>
    private sealed class ThrowingConfigDataService : Wave2TestDataService
    {
        public override Task<string?> GetConfigValueAsync(string key)
            => throw new InvalidOperationException("database is locked");
    }
}
