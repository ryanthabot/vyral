using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Quotes;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Round-trip + command tests for the Quotes system (WAVE 6): the Quotes
/// table CRUD behind a temp SQLite db and the !quote command family handled
/// by <see cref="QuoteService"/> (random, by-number, add/del mod gating,
/// search, count).
/// </summary>
public class QuoteServiceTests
{
    private static VyralBot.Data.IDataService CreateData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-quotes-{Guid.NewGuid():N}.db");
        return new VyralBot.Data.DapperDataService($"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
    }

    private static async Task<QuoteService> CreateServiceAsync()
    {
        var data = CreateData();
        await data.EnsureDatabaseCreatedAsync();
        return new QuoteService(NullLogger<QuoteService>.Instance, data);
    }

    [Fact]
    public async Task Add_AssignsSequentialNumbers_AndStoresCallerAndPlatform()
    {
        var svc = await CreateServiceAsync();

        var first = await svc.AddQuoteAsync(new Quote { Text = "hello there", AddedBy = "alice", Platform = "Twitch" });
        var second = await svc.AddQuoteAsync(new Quote { Text = "general kenobi", AddedBy = "bob", Platform = "Kick" });

        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal(2, await svc.GetQuoteCountAsync());

        var fetched = await svc.GetQuoteByNumberAsync(1);
        Assert.NotNull(fetched);
        Assert.Equal("hello there", fetched!.Text);
        Assert.Equal("alice", fetched.AddedBy);
        Assert.Equal("Twitch", fetched.Platform);
    }

    [Fact]
    public async Task Search_MatchesTextAndAuthor_CaseInsensitively()
    {
        var svc = await CreateServiceAsync();
        await svc.AddQuoteAsync(new Quote { Text = "Never gonna give you up", AddedBy = "rick", Platform = "Twitch" });
        await svc.AddQuoteAsync(new Quote { Text = "something unrelated", AddedBy = "GONNA", Platform = "Discord" });

        var byText = await svc.SearchQuotesAsync("GONNA");
        Assert.Equal(2, byText.Count()); // Text hit + AddedBy hit

        var byAuthor = await svc.SearchQuotesAsync("rick");
        Assert.Single(byAuthor);
        Assert.Equal(1, byAuthor.First().Number);
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheTargetNumber()
    {
        var svc = await CreateServiceAsync();
        await svc.AddQuoteAsync(new Quote { Text = "one" });
        var second = await svc.AddQuoteAsync(new Quote { Text = "two" });

        Assert.True(await svc.DeleteQuoteAsync(second.Number));
        Assert.False(await svc.DeleteQuoteAsync(second.Number)); // already gone
        Assert.Null(await svc.GetQuoteByNumberAsync(second.Number));
        Assert.NotNull(await svc.GetQuoteByNumberAsync(1));
        Assert.Equal(1, await svc.GetQuoteCountAsync());
    }

    [Fact]
    public async Task GetRandom_ReturnsNullOnEmpty_AndAQuoteOtherwise()
    {
        var svc = await CreateServiceAsync();
        Assert.Null(await svc.GetRandomQuoteAsync());

        await svc.AddQuoteAsync(new Quote { Text = "only quote" });
        var random = await svc.GetRandomQuoteAsync();
        Assert.NotNull(random);
        Assert.Equal("only quote", random!.Text);
    }

    // ===== !quote command family =====

    [Fact]
    public async Task ProcessCommand_NonQuoteMessages_ReturnNull()
    {
        var svc = await CreateServiceAsync();
        Assert.Null(await svc.ProcessCommandAsync("!points", "alice", "Twitch", false));
        Assert.Null(await svc.ProcessCommandAsync("hello chat", "alice", "Twitch", false));
        Assert.Null(await svc.ProcessCommandAsync("", "alice", "Twitch", true));
    }

    [Fact]
    public async Task ProcessCommand_RandomOnEmptyDatabase_SuggestsAdding()
    {
        var svc = await CreateServiceAsync();
        var reply = await svc.ProcessCommandAsync("!quote", "alice", "Twitch", false);
        Assert.NotNull(reply);
        Assert.Contains("!quote add", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCommand_Add_ByModerator_SucceedsAndNumbersQuote()
    {
        var svc = await CreateServiceAsync();

        var reply = await svc.ProcessCommandAsync("!quote add hello there", "mod1", "Twitch", isModerator: true);

        Assert.NotNull(reply);
        Assert.Contains("#1", reply);
        var stored = await svc.GetQuoteByNumberAsync(1);
        Assert.NotNull(stored);
        Assert.Equal("hello there", stored!.Text);
        Assert.Equal("mod1", stored.AddedBy);
        Assert.Equal("Twitch", stored.Platform);
    }

    [Fact]
    public async Task ProcessCommand_Add_ByViewer_Blocked()
    {
        var svc = await CreateServiceAsync();

        var reply = await svc.ProcessCommandAsync("!quote add nope", "viewer", "Kick", isModerator: false);

        Assert.NotNull(reply);
        Assert.Contains("moderator", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await svc.GetQuoteCountAsync());
    }

    [Fact]
    public async Task ProcessCommand_AddEmptyText_ShowsUsage()
    {
        var svc = await CreateServiceAsync();
        var reply = await svc.ProcessCommandAsync("!quote add", "mod1", "Twitch", isModerator: true);
        Assert.NotNull(reply);
        Assert.StartsWith("Usage:", reply);
    }

    [Fact]
    public async Task ProcessCommand_ByNumber_FindsAndMisses()
    {
        var svc = await CreateServiceAsync();
        await svc.ProcessCommandAsync("!quote add a classic", "mod1", "Twitch", true);

        var hit = await svc.ProcessCommandAsync("!quote 1", "viewer", "Twitch", false);
        Assert.NotNull(hit);
        Assert.Contains("a classic", hit);

        var miss = await svc.ProcessCommandAsync("!quote 42", "viewer", "Twitch", false);
        Assert.NotNull(miss);
        Assert.Contains("not found", miss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCommand_Delete_ModGated()
    {
        var svc = await CreateServiceAsync();
        await svc.ProcessCommandAsync("!quote add temp", "mod1", "Discord", true);

        var viewerReply = await svc.ProcessCommandAsync("!quote del 1", "viewer", "Discord", isModerator: false);
        Assert.NotNull(viewerReply);
        Assert.Contains("moderator", viewerReply, StringComparison.OrdinalIgnoreCase);

        var modReply = await svc.ProcessCommandAsync("!quote del 1", "mod1", "Discord", isModerator: true);
        Assert.NotNull(modReply);
        Assert.Contains("deleted", modReply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await svc.GetQuoteCountAsync());
    }

    [Fact]
    public async Task ProcessCommand_Search_ListsMatchingNumbers()
    {
        var svc = await CreateServiceAsync();
        await svc.ProcessCommandAsync("!quote add the cake is a lie", "mod1", "Twitch", true);
        await svc.ProcessCommandAsync("!quote add totally unrelated", "mod1", "Twitch", true);

        var reply = await svc.ProcessCommandAsync("!quote search cake", "viewer", "Kick", false);

        Assert.NotNull(reply);
        Assert.Contains("#1", reply);
        Assert.DoesNotContain("unrelated", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCommand_QuoteCount_ReportsTotals()
    {
        var svc = await CreateServiceAsync();
        Assert.Contains("0 quotes", await svc.ProcessCommandAsync("!quotecount", "viewer", "Twitch", false));

        await svc.ProcessCommandAsync("!quote add one", "mod1", "Twitch", true);
        Assert.Contains("1 quote", await svc.ProcessCommandAsync("!quotecount", "viewer", "Twitch", false));
    }

    [Fact]
    public async Task ProcessCommand_Help_ListsSubcommands()
    {
        var svc = await CreateServiceAsync();
        var reply = await svc.ProcessCommandAsync("!quote help", "viewer", "Twitch", false);
        Assert.NotNull(reply);
        Assert.Contains("!quote add", reply);
        Assert.Contains("!quotecount", reply);
    }

    [Fact]
    public async Task ProcessCommand_StoresOriginPlatform_ForParityTracking()
    {
        // The platform field is what proves multi-platform parity: the same
        // handler runs for every platform, the quote records its origin.
        var svc = await CreateServiceAsync();
        await svc.ProcessCommandAsync("!quote add from twitch", "mod1", "Twitch", true);
        await svc.ProcessCommandAsync("!quote add from kick", "mod2", "Kick", true);
        await svc.ProcessCommandAsync("!quote add from youtube", "mod3", "YouTube", true);
        await svc.ProcessCommandAsync("!quote add from discord", "mod4", "Discord", true);

        var all = (await svc.GetQuotesAsync()).ToList();
        Assert.Equal(new[] { "Twitch", "Kick", "YouTube", "Discord" }, all.Select(q => q.Platform));
    }
}
