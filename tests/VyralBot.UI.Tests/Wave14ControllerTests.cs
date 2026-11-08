using Xunit;
using VyralBot.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Api.Controllers;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-14 coverage for the two loopback overlay controllers:
/// <see cref="LeaderboardController"/> (count clamped to 1..25 — a size cap,
/// not an error — and case-insensitive platform validation against the known
/// platform set) and <see cref="CountersController"/> (503 when the backing
/// service is not registered, otherwise the { count } snapshot the OBS widget
/// polls). The action methods are invoked directly with a DefaultHttpContext
/// (the controllers only touch Response.Headers, which a bare context
/// satisfies) against the Wave-14 fakes — no Kestrel host needed.
/// </summary>
public class Wave14ControllerTests
{
    private static object? Prop(object source, string name)
        => source.GetType().GetProperty(name)?.GetValue(source);

    // ── LeaderboardController ──────────────────────────────────────────

    private static LeaderboardController LeaderboardController(Wave14CurrencyService? currency)
        => new(NullLogger<LeaderboardController>.Instance, currency)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public async Task Leaderboard_WithoutCurrencyService_Returns503()
    {
        var result = await LeaderboardController(null).Data();

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
        Assert.Equal(503, status.StatusCode);
    }

    [Theory]
    [InlineData(0, 1)]     // clamps up — a stale overlay URL keeps working
    [InlineData(-7, 1)]
    [InlineData(3, 3)]     // in-range passes through
    [InlineData(25, 25)]   // exactly the cap
    [InlineData(999, 25)]  // clamps down (unbounded dump guard)
    public async Task Leaderboard_CountIsSizeCapped_NotAnError(int requested, int effective)
    {
        // Enough fake rows that the entry count always reflects the clamp.
        var currency = new Wave14CurrencyService { Count = 25 };
        var controller = LeaderboardController(currency);

        var result = await controller.Data(count: requested);

        var payload = Assert.IsType<OkObjectResult>(result).Value;
        Assert.NotNull(payload);
        Assert.Equal(effective, Prop(payload!, "count"));
        var call = Assert.Single(currency.LeaderboardCalls);
        Assert.Equal(effective, call.Limit);
        // Entries carry 1-based ranks in query order.
        Assert.Equal(effective, ((System.Collections.ICollection)Prop(payload!, "entries")!).Count);
    }

    [Theory]
    [InlineData("twitch", "Twitch")]
    [InlineData("KICK", "Kick")]
    [InlineData("youtube", "YouTube")]
    public async Task Leaderboard_PlatformIsCanonicalized_CaseInsensitively(string input, string canonical)
    {
        var currency = new Wave14CurrencyService();
        var controller = LeaderboardController(currency);

        var result = await controller.Data(platform: input);

        var payload = Assert.IsType<OkObjectResult>(result).Value;
        Assert.NotNull(payload);
        Assert.Equal(canonical, Prop(payload!, "platform"));
        Assert.Equal(canonical, Assert.Single(currency.LeaderboardCalls).Platform);
    }

    [Theory]
    [InlineData("myspace")]
    [InlineData("trovo")]
    public async Task Leaderboard_UnknownPlatform_ReturnsBadRequest(string platform)
    {
        var currency = new Wave14CurrencyService();
        var controller = LeaderboardController(currency);

        var result = await controller.Data(platform: platform);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("invalid_platform", Prop(bad.Value!, "error"));
        Assert.Empty(currency.LeaderboardCalls); // rejected before the query
    }

    [Fact]
    public async Task Leaderboard_NoPlatform_PassesNullThrough()
    {
        var currency = new Wave14CurrencyService();
        var controller = LeaderboardController(currency);

        await controller.Data();

        Assert.Null(Assert.Single(currency.LeaderboardCalls).Platform);
    }

    // ── CountersController ─────────────────────────────────────────────

    private static CountersController CountersController(Wave14CountersService? counters)
        => new(NullLogger<CountersController>.Instance, counters)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public async Task Counters_WithoutCounterService_Returns503()
    {
        var result = await CountersController(null).Death();

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
        Assert.Equal(503, status.StatusCode);
    }

    [Fact]
    public async Task Counters_Death_ReturnsCountSnapshot_WithNoStoreHeader()
    {
        var counters = new Wave14CountersService { Count = 42 };
        var controller = CountersController(counters);

        var result = await controller.Death();

        var payload = Assert.IsType<OkObjectResult>(result).Value;
        Assert.NotNull(payload);
        Assert.Equal(42, Prop(payload!, "count"));
        Assert.Equal(1, counters.GetCountCalls);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }
}
