using System.IO;
using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-12 coverage for the wave-10 <c>VariableReplacementService</c> fixes:
///
///   1. REPEATED placeholders in one template ("{user} and {user}") must have
///      EVERY occurrence substituted (the wave-10 fix: string.Replace replaces
///      all occurrences at once, so deduping matches would eat the second
///      replacement and log a bogus circular-reference warning);
///   2. unknown placeholders are left untouched;
///   3. the two cache lanes expire independently — static variables live for
///      minutes, auto-updating counts (follower/sub counts, refreshed via
///      UpdateVariableValueAsync, which cannot invalidate the cache) live for
///      seconds.
///
/// Runs against a REAL DapperDataService on a temp SQLite file (the
/// DataLayerNewTablesTests pattern) so seeding, UpdateVariableValueAsync and
/// the read path are the production code paths. The 15s/5min TTLs are too
/// slow to wait out in a unit test, so lane expiry is forced by backdating
/// the lane's timestamp through the private cache entry (wave-8/10
/// reflection pattern); InvalidateCacheAsync covers the explicit flush path.
/// </summary>
public class Wave12VariableReplacementTests
{
    private const string Moved = "VariableReplacementService.{0} moved — update the wave-12 tests";

    private static async Task<DapperDataService> CreateDataServiceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-wave12-vars-{Guid.NewGuid():N}.db");
        var data = new DapperDataService($"Data Source={path}", NullLogger<DapperDataService>.Instance);
        await data.EnsureDatabaseCreatedAsync();
        return data;
    }

    private static VariableReplacementService MakeService(IDataService data)
        => new(NullLogger<VariableReplacementService>.Instance, data);

    private static Variable MakeVariable(string name, string? value, bool isAutoUpdating = false) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        DisplayName = name,
        Category = "Wave12",
        Value = value,
        IsEditable = true,
        IsAutoUpdating = isAutoUpdating
    };

    /// <summary>Backdates a lane expiry on the "all" cache entry so the next
    /// read must reload that lane without waiting out the TTL.</summary>
    private static void BackdateLane(VariableReplacementService service, string laneExpiryPropertyName)
    {
        var cache = (IDictionary)(typeof(VariableReplacementService)
            .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(string.Format(Moved, "_cache")))
            .GetValue(service)!;
        var entry = cache["all"]
            ?? throw new InvalidOperationException("variable cache not warmed — call ReplaceVariablesAsync before backdating");
        var property = entry.GetType().GetProperty(laneExpiryPropertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"CachedVariables.{laneExpiryPropertyName} moved — update the wave-12 tests");
        property.SetValue(entry, DateTime.UtcNow.AddSeconds(-1));
    }

    // ------------------------------------------------------------------
    // The wave-10 fix: repeated placeholders
    // ------------------------------------------------------------------

    [Fact]
    public async Task Replace_RepeatedContextPlaceholder_SubstitutesEveryOccurrence()
    {
        var data = await CreateDataServiceAsync();
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync(
            "Hi {user}! Look {user}, everyone!",
            new Dictionary<string, string> { ["user"] = "Alice" });

        Assert.Equal("Hi Alice! Look Alice, everyone!", result);
    }

    [Fact]
    public async Task Replace_RepeatedDbPlaceholder_SubstitutesEveryOccurrence()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-greeting", "Howdy"));
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync("{wave12-greeting} and {wave12-greeting}");

        Assert.Equal("Howdy and Howdy", result);
    }

    [Fact]
    public async Task Replace_MixedKnownAndRepeated_AllKnownReplaced()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-greeting", "Howdy"));
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync(
            "{wave12-greeting} {user}, {user} again, {wave12-greeting}",
            new Dictionary<string, string> { ["user"] = "Bob" });

        Assert.Equal("Howdy Bob, Bob again, Howdy", result);
    }

    // ------------------------------------------------------------------
    // Unknown / unresolvable placeholders
    // ------------------------------------------------------------------

    [Fact]
    public async Task Replace_UnknownPlaceholder_LeftAsIs()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-known", "yes"));
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync(
            "known={wave12-known} unknown={wave12-this-variable-does-not-exist}");

        Assert.Equal("known=yes unknown={wave12-this-variable-does-not-exist}", result);
    }

    [Fact]
    public async Task Replace_NullValuedDbVariable_LeftAsPlaceholder()
    {
        // EnsureDatabaseCreatedAsync seeds the system "username" variable with
        // a null value (runtime context only) — it must not resolve from the
        // database and must stay visible for the caller to notice.
        var data = await CreateDataServiceAsync();
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync("hello {username}");

        Assert.Equal("hello {username}", result);
    }

    [Fact]
    public async Task Replace_PlaceholderIsCaseInsensitive()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-greeting", "Howdy"));
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync("{WAVE12-GREETING} {Wave12-Greeting}");

        Assert.Equal("Howdy Howdy", result);
    }

    [Fact]
    public async Task Replace_EmptyAndNullTemplates_ReturnedAsIs()
    {
        var service = MakeService(await CreateDataServiceAsync());

        Assert.Equal(string.Empty, await service.ReplaceVariablesAsync(string.Empty));
        Assert.Null(await service.ReplaceVariablesAsync(null!));
    }

    // ------------------------------------------------------------------
    // Nested variables + cycle bound
    // ------------------------------------------------------------------

    [Fact]
    public async Task Replace_NestedDbVariable_ResolvedRecursively()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-greeting", "Howdy"));
        await data.SaveVariableAsync(MakeVariable("wave12-outer", "says {wave12-greeting}"));
        var service = MakeService(data);

        var result = await service.ReplaceVariablesAsync("[{wave12-outer}]");

        Assert.Equal("[says Howdy]", result);
    }

    [Fact]
    public async Task Replace_ReferenceCycle_TerminatesWithoutThrowing()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-a", "{wave12-b}"));
        await data.SaveVariableAsync(MakeVariable("wave12-b", "{wave12-a}"));
        var service = MakeService(data);

        // Must terminate at MaxNestedDepth instead of recursing forever;
        // the cycle is simply left partially substituted.
        var result = await service.ReplaceVariablesAsync("{wave12-a}");

        Assert.Contains("{wave12-", result);
    }

    // ------------------------------------------------------------------
    // Cache lanes (wave-10 fix: independent expiry for count variables)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CacheLanes_AutoUpdatingExpiresIndependentlyOfStaticLane()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-static", "s1", isAutoUpdating: false));
        await data.SaveVariableAsync(MakeVariable("wave12-count", "10", isAutoUpdating: true));
        var service = MakeService(data);
        var template = "static={wave12-static} count={wave12-count}";

        // First read loads + caches both lanes.
        Assert.Equal("static=s1 count=10", await service.ReplaceVariablesAsync(template));

        // The platform integrations push fresh values straight into the DB
        // (they cannot invalidate this cache — that is the whole point of
        // the short TTL on the count lane).
        await data.UpdateVariableValueAsync("wave12-static", "s2");
        await data.UpdateVariableValueAsync("wave12-count", "11");

        // Both lanes are still inside their TTL → stale values served.
        Assert.Equal("static=s1 count=10", await service.ReplaceVariablesAsync(template));

        // Count lane expires (15s TTL forced) → fresh count, but the static
        // lane (5min TTL) must NOT have been refreshed by that load.
        BackdateLane(service, "AutoUpdatingExpiresAt");
        Assert.Equal("static=s1 count=11", await service.ReplaceVariablesAsync(template));

        // Static lane expires separately → both fresh now.
        BackdateLane(service, "ExpiresAt");
        Assert.Equal("static=s2 count=11", await service.ReplaceVariablesAsync(template));
    }

    [Fact]
    public async Task CacheLanes_InvalidateCacheAsync_DropsBothLanes()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-static", "s1"));
        await data.SaveVariableAsync(MakeVariable("wave12-count", "10", isAutoUpdating: true));
        var service = MakeService(data);
        var template = "static={wave12-static} count={wave12-count}";

        Assert.Equal("static=s1 count=10", await service.ReplaceVariablesAsync(template));

        await data.UpdateVariableValueAsync("wave12-static", "s2");
        await data.UpdateVariableValueAsync("wave12-count", "11");
        await service.InvalidateCacheAsync();

        Assert.Equal("static=s2 count=11", await service.ReplaceVariablesAsync(template));
    }

    [Fact]
    public async Task GetAvailableVariables_ReturnsAutoUpdatingAndStaticValues()
    {
        var data = await CreateDataServiceAsync();
        await data.SaveVariableAsync(MakeVariable("wave12-static", "s1"));
        await data.SaveVariableAsync(MakeVariable("wave12-count", "10", isAutoUpdating: true));
        var service = MakeService(data);

        var available = await service.GetAvailableVariablesAsync();

        Assert.Equal("s1", available["wave12-static"]);
        Assert.Equal("10", available["wave12-count"]);
    }
}
