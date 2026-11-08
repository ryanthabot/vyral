using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Quotes;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// WAVE-8 INTEGRATION: the !quote command family through the real
/// <see cref="QuoteService"/> over the in-memory wave-8 IDataService (fake
/// Quotes table with the Dapper layer's sequential-numbering contract).
/// Covers the mod/non-mod gate E2E and the add → fetch loop the chat
/// pipeline performs, complementing the SQLite-backed QuoteServiceTests
/// (wave 6) with the fake-data-layer seam the marathon waves share.
/// </summary>
public class IntegrationWave8QuotesTests
{
    [Fact]
    public async Task Add_Is_ModGated_NonMod_Denied_Mod_Stores_Sequentially()
    {
        var data = new IntegrationWave8DataService();
        var svc = new QuoteService(NullLogger<QuoteService>.Instance, data);

        // Non-mod is denied and nothing is stored.
        Assert.Equal(
            "You must be a moderator to add quotes",
            await svc.ProcessCommandAsync("!quote add stay hydrated", "viewer", "Twitch", isModerator: false));
        Assert.Empty(data.Quotes);

        // Mod adds two quotes; numbers are assigned sequentially.
        Assert.Equal(
            "Quote #1 added by modrita.",
            await svc.ProcessCommandAsync("!quote add stay hydrated", "modrita", "Twitch", isModerator: true));
        Assert.Equal(
            "Quote #2 added by modrita.",
            await svc.ProcessCommandAsync("!quote add touch grass", "modrita", "Kick", isModerator: true));

        // Mod without text gets usage, not a row.
        Assert.Equal(
            "Usage: !quote add <text>",
            await svc.ProcessCommandAsync("!quote add", "modrita", "Twitch", isModerator: true));
        Assert.Equal(2, data.Quotes.Count);
    }

    [Fact]
    public async Task Random_And_ByNumber_Return_Stored_Quote_CrossPlatform()
    {
        var data = new IntegrationWave8DataService();
        var svc = new QuoteService(NullLogger<QuoteService>.Instance, data);
        await svc.ProcessCommandAsync("!quote add stay hydrated", "modrita", "Twitch", isModerator: true);

        // !quote on ANOTHER platform returns the stored row (single-quote pool).
        var random = await svc.ProcessCommandAsync("!quote", "viewer", "Kick", isModerator: false);
        Assert.Equal("Quote #1: \"stay hydrated\" — added by modrita", random);

        // By number, plus the count command.
        Assert.Contains("stay hydrated", await svc.ProcessCommandAsync("!quote 1", "viewer", "Kick", isModerator: false));
        Assert.Equal(
            "There is 1 quote in the database.",
            await svc.ProcessCommandAsync("!quotecount", "viewer", "Twitch", isModerator: false));

        // Non-quote commands fall through (null = other handlers see it).
        Assert.Null(await svc.ProcessCommandAsync("!points", "viewer", "Twitch", isModerator: false));
        Assert.Null(await svc.ProcessCommandAsync("just chatting", "viewer", "Twitch", isModerator: false));
    }
}
