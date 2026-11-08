using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Variables;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-10 coverage for the per-platform response PRECEDENCE MATRIX behind
/// <c>CommandService.PickResponse</c> (the wave-7 feature's pick point,
/// driven here through the PUBLIC <c>ExecuteAsync</c> on the shared wave-8
/// in-memory <see cref="IntegrationWave8DataService"/> instead of a temp
/// SQLite db). Matrix under test, in order:
///   1. a non-blank per-platform override authored for the ORIGINATING
///      platform wins over everything (case-insensitive both ways);
///   2. else a random entry from ResponsesJson;
///   3. else the single default Response;
///   and a BLANK override entry is treated as UNSET (a hand-authored row
///   with whitespace falls through to 2/3 — the builder never stores one).
/// Sibling of PlatformResponsesRoundTripTests (wave 7, real Dapper layer);
/// this file pins the precedence via the fake data service.
/// </summary>
public class Wave10CommandPickResponseTests
{
    private static (CommandService Service, IntegrationWave8DataService Data) CreateService(params Command[] seeds)
    {
        var data = new IntegrationWave8DataService();
        foreach (var seed in seeds)
        {
            data.CommandsByName[seed.Name] = seed;
        }

        return (new CommandService(data, new PassthroughVariables(), NullLogger<CommandService>.Instance), data);
    }

    private static CommandContext ContextOn(string platform) => new()
    {
        Username = "viewer",
        Platform = platform,
        Channel = "streamer"
    };

    private static Command Command(
        string name, string response,
        string? responsesJson = null,
        string? platformResponsesJson = null) => new()
    {
        Id = $"cmd-{name}",
        Name = name,
        Response = response,
        Permission = "Everyone",
        ResponsesJson = responsesJson,
        PlatformResponsesJson = platformResponsesJson
    };

    // ==================================================================
    // 1. Platform hit beats everything else (for the ORIGINATING platform)
    // ==================================================================

    [Fact]
    public async Task PlatformHit_ReturnsOverride_OtherPlatformsGetDefault()
    {
        var (service, _) = CreateService(Command("!follow", "default follow",
            platformResponsesJson: """{"Twitch":"follow on Twitch","Kick":"follow on Kick"}"""));

        Assert.Equal("follow on Twitch", await service.ExecuteAsync("!follow", ContextOn("Twitch")));
        Assert.Equal("follow on Kick", await service.ExecuteAsync("!follow", ContextOn("Kick")));
        // No YouTube entry was authored -> the default single response.
        Assert.Equal("default follow", await service.ExecuteAsync("!follow", ContextOn("YouTube")));
    }

    [Fact]
    public async Task PlatformHit_BeatsRandomReplies()
    {
        var (service, _) = CreateService(Command("!hype", "default hype",
            responsesJson: """["reply A","reply B"]""",
            platformResponsesJson: """{"Twitch":"twitch hype"}"""));

        // Twitch: deterministic override, never the random pool.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("twitch hype", await service.ExecuteAsync("!hype", ContextOn("Twitch")));
        }

        // Kick (no override): the random pool still serves it.
        for (var i = 0; i < 5; i++)
        {
            Assert.Contains(await service.ExecuteAsync("!hype", ContextOn("Kick")), new[] { "reply A", "reply B" });
        }
    }

    [Fact]
    public async Task PlatformLookup_IsCaseInsensitive_BothKeyAndOrigin()
    {
        // An editor-authored UPPERCASE key hits, and so do lowercase/odd-case
        // originating platforms (the map and lookup both ignore case).
        var (service, _) = CreateService(Command("!promo", "default promo",
            platformResponsesJson: """{"TWITCH":"upper hit"}"""));

        Assert.Equal("upper hit", await service.ExecuteAsync("!promo", ContextOn("Twitch")));
        Assert.Equal("upper hit", await service.ExecuteAsync("!promo", ContextOn("twitch")));
        Assert.Equal("default promo", await service.ExecuteAsync("!promo", ContextOn("Discord")));
    }

    // ==================================================================
    // Blank override entries are treated as UNSET (fall through)
    // ==================================================================

    [Fact]
    public async Task BlankPlatformEntry_FallsThroughToRandomReplies()
    {
        // Hand-authored row (the builder drops blanks — this proves the
        // RUNTIME treats a stored blank as unset too): Kick's entry is
        // whitespace, so Kick must fall to the replies pool, while the
        // non-blank Twitch entry keeps winning.
        var (service, _) = CreateService(Command("!mixed", "default mixed",
            responsesJson: """["kick fallback"]""",
            platformResponsesJson: """{"Kick":"   ","Twitch":"twitch hit"}"""));

        Assert.Equal("twitch hit", await service.ExecuteAsync("!mixed", ContextOn("Twitch")));
        Assert.Equal("kick fallback", await service.ExecuteAsync("!mixed", ContextOn("Kick")));
    }

    [Fact]
    public async Task AllBlankPlatformEntries_BehaveLikeNoOverrides()
    {
        var (service, _) = CreateService(Command("!blanks", "the one reply",
            platformResponsesJson: """{"Kick":"  ","Twitch":"\t","Discord":""}"""));

        Assert.Equal("the one reply", await service.ExecuteAsync("!blanks", ContextOn("Twitch")));
        Assert.Equal("the one reply", await service.ExecuteAsync("!blanks", ContextOn("Kick")));
        Assert.Equal("the one reply", await service.ExecuteAsync("!blanks", ContextOn("Discord")));
    }

    // ==================================================================
    // 2. Random replies (ResponsesJson) when no override applies
    // ==================================================================

    [Fact]
    public async Task RepliesOnly_NoOverride_ServesFromThePool()
    {
        // Single-entry pool keeps the pick deterministic.
        var (service, _) = CreateService(Command("!pool", "default pool",
            responsesJson: """["only reply"]"""));

        Assert.Equal("only reply", await service.ExecuteAsync("!pool", ContextOn("Twitch")));
        Assert.Equal("only reply", await service.ExecuteAsync("!pool", ContextOn("Kick")));
    }

    [Fact]
    public async Task MultiReplyPool_AlwaysServesAMember()
    {
        var (service, _) = CreateService(Command("!roll", "default roll",
            responsesJson: """["a","b","c","d"]"""));

        for (var i = 0; i < 10; i++)
        {
            Assert.Contains(await service.ExecuteAsync("!roll", ContextOn("Twitch")), new[] { "a", "b", "c", "d" });
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[" ", ""]""")]          // blank entries are not replies
    [InlineData("not json {")]
    public async Task EmptyOrJunkRepliesJson_FallsBackToDefault(string responsesJson)
    {
        var (service, _) = CreateService(Command("!junk", "the fallback", responsesJson: responsesJson));

        Assert.Equal("the fallback", await service.ExecuteAsync("!junk", ContextOn("Twitch")));
    }

    // ==================================================================
    // 3. Default single response (and the missing-everything cases)
    // ==================================================================

    [Fact]
    public async Task NothingButDefaultResponse_ServesItOnEveryPlatform()
    {
        var (service, _) = CreateService(Command("!hello", "the one reply"));

        Assert.Equal("the one reply", await service.ExecuteAsync("!hello", ContextOn("Twitch")));
        Assert.Equal("the one reply", await service.ExecuteAsync("!hello", ContextOn("Kick")));
        Assert.Equal("the one reply", await service.ExecuteAsync("!hello", ContextOn("Discord")));
    }

    [Fact]
    public async Task MalformedPlatformResponsesJson_BehavesLikeNoOverrides()
    {
        var (service, _) = CreateService(Command("!broken", "safe default",
            responsesJson: """["pool reply"]""",
            platformResponsesJson: "this is { not json"));

        Assert.Equal("pool reply", await service.ExecuteAsync("!broken", ContextOn("Twitch")));
    }

    // ==================================================================
    // Public helper: the replies pool parser itself
    // ==================================================================

    [Fact]
    public void ParseResponses_FiltersBlanks_AndToleratesJunk()
    {
        Assert.Empty(CommandService.ParseResponses(null));
        Assert.Empty(CommandService.ParseResponses("   "));
        Assert.Empty(CommandService.ParseResponses("not json"));
        Assert.Empty(CommandService.ParseResponses("""["", "  ", null]"""));
        Assert.Equal(new[] { "keep me", "me too" },
            CommandService.ParseResponses("""["keep me", " ", "", "me too"]"""));
    }

    /// <summary>Passthrough variable service — reply templates return verbatim.</summary>
    private sealed class PassthroughVariables : IVariableReplacementService
    {
        public Task<string> ReplaceVariablesAsync(
            string template, Dictionary<string, string>? contextVariables = null, string? platform = null)
            => Task.FromResult(template ?? string.Empty);

        public Task<Dictionary<string, string>> GetAvailableVariablesAsync(string? platform = null)
            => Task.FromResult(new Dictionary<string, string>(0));

        public Task<string?> GetVariableValueAsync(string variableName) => Task.FromResult<string?>(null);

        public bool ValidateVariableName(string name, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }
}
