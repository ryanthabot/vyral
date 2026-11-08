using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Variables;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-7 coverage for PER-PLATFORM COMMAND RESPONSES: one command, a
/// different reply text per platform. Covers the full round trip —
/// <see cref="Command.BuildPlatformResponsesJson"/> →
/// <see cref="Command.PlatformResponsesJson"/> (DapperDataService insert +
/// update, additive ALTER-column migration on a fresh temp db) →
/// <see cref="Command.ParsePlatformResponses"/> / <see cref="Command.ResolvePlatformResponse"/>
/// → the live resolution point (<see cref="CommandService.ExecuteAsync"/>'s
/// reply pick for the ORIGINATING platform, custom commands and the stored
/// built-in template path alike). Blank/missing entries keep the legacy
/// single-Response (and random ResponsesJson) behavior — backward compatible.
/// </summary>
public class PlatformResponsesRoundTripTests
{
    // ------------------------------------------------------------------
    // Entity helpers: build → parse → resolve
    // ------------------------------------------------------------------

    [Fact]
    public void Build_DropsBlankEntries_AndReturnsNull_WhenAllBlank()
    {
        // Blank boxes must not bloat the row — the column stays NULL until a
        // real override exists (untouched rows keep legacy behavior).
        Assert.Null(Command.BuildPlatformResponsesJson(null));
        Assert.Null(Command.BuildPlatformResponsesJson(new Dictionary<string, string?>()));
        Assert.Null(Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
        {
            ["Twitch"] = "   ",
            ["Kick"] = "",
            ["YouTube"] = null,
            ["Discord"] = "\t"
        }));
    }

    [Fact]
    public void Build_Parse_RoundTrips_Overrides()
    {
        var json = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
        {
            ["Twitch"] = " Follow on Twitch! ",
            ["Kick"] = "Follow on Kick!",
            ["YouTube"] = "",   // blank box → dropped
            ["Discord"] = "Join the Discord!"
        });

        var map = Command.ParsePlatformResponses(json);

        Assert.Equal(3, map.Count);
        Assert.Equal("Follow on Twitch!", map["Twitch"]); // trimmed
        Assert.Equal("Follow on Kick!", map["Kick"]);
        Assert.Equal("Join the Discord!", map["Discord"]);
        Assert.False(map.ContainsKey("YouTube"));
    }

    [Fact]
    public void Parse_Tolerates_NullBlank_Malformed_AndNonObject_Json()
    {
        Assert.Empty(Command.ParsePlatformResponses(null));
        Assert.Empty(Command.ParsePlatformResponses("   "));
        Assert.Empty(Command.ParsePlatformResponses("not json {"));
        Assert.Empty(Command.ParsePlatformResponses("[\"Twitch\"]")); // array, not object
        Assert.Empty(Command.ParsePlatformResponses("null"));
    }

    [Fact]
    public void Resolve_PrefersPlatformEntry_CaseInsensitive_BlankFallsBack()
    {
        var command = new Command
        {
            Id = "c1",
            Name = "!hello",
            Response = "default reply",
            Permission = "Everyone",
            PlatformResponsesJson = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
            {
                ["Twitch"] = "twitch reply",
                ["Kick"] = "   " // blank box → stored as absent → default
            })
        };

        Assert.Equal("twitch reply", command.ResolvePlatformResponse("Twitch"));
        Assert.Equal("twitch reply", command.ResolvePlatformResponse("twitch")); // case-insensitive
        Assert.Null(command.ResolvePlatformResponse("Kick"));      // blank entry → legacy pick
        Assert.Null(command.ResolvePlatformResponse("Discord"));   // never authored
        Assert.Null(command.ResolvePlatformResponse(null));        // unknown origin
        Assert.Null(command.ResolvePlatformResponse("  "));
    }

    // ------------------------------------------------------------------
    // Data layer: PlatformResponsesJson survives save/load (insert + update)
    // on a fresh temp db, which also exercises the additive column migration.
    // ------------------------------------------------------------------

    private static VyralBot.Data.IDataService CreateService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-wave7-{Guid.NewGuid():N}.db");
        return new VyralBot.Data.DapperDataService($"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
    }

    [Fact]
    public async Task DapperCommandRow_PlatformResponsesJson_RoundTrips_InsertAndUpdate()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();

        var command = new Command
        {
            Id = "cmd-wave7",
            Name = "!wave7links",
            Response = "default links",
            Permission = "Everyone",
            PlatformResponsesJson = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
            {
                ["Twitch"] = "twitch links",
                ["Kick"] = "kick links"
            })
        };

        await svc.SaveCommandAsync(command);

        var inserted = await svc.GetCommandByNameAsync("!wave7links");
        Assert.NotNull(inserted);
        Assert.Equal(command.PlatformResponsesJson, inserted!.PlatformResponsesJson);
        var insertedMap = Command.ParsePlatformResponses(inserted.PlatformResponsesJson);
        Assert.Equal("twitch links", insertedMap["Twitch"]);
        Assert.Equal("kick links", insertedMap["Kick"]);

        // Editor save path: changed boxes rewrite the JSON (update branch).
        inserted.PlatformResponsesJson = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
        {
            ["Twitch"] = "new twitch links",
            ["Kick"] = "" // cleared box → dropped, Kick falls back to default
        });
        await svc.SaveCommandAsync(inserted);

        var updated = await svc.GetCommandAsync("cmd-wave7");
        Assert.NotNull(updated);
        var updatedMap = Command.ParsePlatformResponses(updated!.PlatformResponsesJson);
        var onlyEntry = Assert.Single(updatedMap);
        Assert.Equal("Twitch", onlyEntry.Key);
        Assert.Equal("new twitch links", onlyEntry.Value);
    }

    [Fact]
    public async Task DapperCommandRow_NullPlatformResponses_StaysNull()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();

        await svc.SaveCommandAsync(new Command { Id = "cmd-legacy", Name = "!wave7plain", Response = "invite", Permission = "Everyone" });

        var row = await svc.GetCommandByNameAsync("!wave7plain");
        Assert.NotNull(row);
        Assert.Null(row!.PlatformResponsesJson); // untouched rows keep NULL → legacy behavior
    }

    // ------------------------------------------------------------------
    // Live resolution: CommandService.ExecuteAsync picks the reply text for
    // the ORIGINATING platform (the point where the response reaches the
    // send path).
    // ------------------------------------------------------------------

    /// <summary>Passthrough variable service — reply templates return verbatim.</summary>
    private sealed class PassthroughVariables : IVariableReplacementService
    {
        public Task<string> ReplaceVariablesAsync(
            string template, Dictionary<string, string>? contextVariables = null, string? platform = null)
            => Task.FromResult(template ?? string.Empty);

        public Task<Dictionary<string, string>> GetAvailableVariablesAsync(string? platform = null)
            => Task.FromResult(new Dictionary<string, string>(0));

        public Task<string?> GetVariableValueAsync(string variableName)
            => Task.FromResult<string?>(null);

        public bool ValidateVariableName(string name, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }

    private static async Task<CommandService> CreateCommandServiceAsync(Command seed)
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        await svc.SaveCommandAsync(seed);
        return new CommandService(svc, new PassthroughVariables(), NullLogger<CommandService>.Instance);
    }

    private static CommandContext ContextOn(string platform) => new()
    {
        Username = "viewer",
        Platform = platform,
        Channel = "streamer"
    };

    [Fact]
    public async Task ExecuteAsync_CustomCommand_UsesOverrideForOriginatingPlatform()
    {
        var service = await CreateCommandServiceAsync(new Command
        {
            Id = "c-override",
            Name = "!follow",
            Response = "Follow everywhere!",
            Permission = "Everyone",
            PlatformResponsesJson = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
            {
                ["Twitch"] = "Follow on Twitch!",
                ["Kick"] = "Follow on Kick!"
            })
        });

        Assert.Equal("Follow on Twitch!", await service.ExecuteAsync("!follow", ContextOn("Twitch")));
        Assert.Equal("Follow on Kick!", await service.ExecuteAsync("!follow", ContextOn("Kick")));
        // No YouTube entry → the single default Response applies there.
        Assert.Equal("Follow everywhere!", await service.ExecuteAsync("!follow", ContextOn("YouTube")));
    }

    [Fact]
    public async Task ExecuteAsync_PlatformOverride_BeatsRandomReplies()
    {
        var service = await CreateCommandServiceAsync(new Command
        {
            Id = "c-random",
            Name = "!hype",
            Response = "default",
            Permission = "Everyone",
            ResponsesJson = "[\"reply A\",\"reply B\"]",
            PlatformResponsesJson = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
            {
                ["Twitch"] = "twitch hype"
            })
        });

        // Twitch: the override wins deterministically.
        Assert.Equal("twitch hype", await service.ExecuteAsync("!hype", ContextOn("Twitch")));
        // Kick (no override): the legacy random-reply pool still serves it.
        var kickReply = await service.ExecuteAsync("!hype", ContextOn("Kick"));
        Assert.Contains(kickReply, new[] { "reply A", "reply B" });
    }

    [Fact]
    public async Task ExecuteAsync_BuiltInStoredTemplate_HonorsOverride()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();

        // Fresh DBs seed the built-in "!socials" row behind the unique name
        // index — EDIT it (the update branch), zeroing its seeded cooldowns so
        // both platforms answer. Built-ins like !socials/!discord reply from
        // the stored template in Response; the override must apply there too.
        var seeded = await svc.GetCommandByNameAsync("!socials");
        Assert.NotNull(seeded);
        seeded!.Response = "all my links";
        seeded.GlobalCooldown = 0;
        seeded.UserCooldown = 0;
        seeded.PlatformResponsesJson = Command.BuildPlatformResponsesJson(new Dictionary<string, string?>
        {
            ["Discord"] = "discord links"
        });
        await svc.SaveCommandAsync(seeded);

        var service = new CommandService(svc, new PassthroughVariables(), NullLogger<CommandService>.Instance);

        Assert.Equal("discord links", await service.ExecuteAsync("!socials", ContextOn("Discord")));
        Assert.Equal("all my links", await service.ExecuteAsync("!socials", ContextOn("Twitch")));
    }

    [Fact]
    public async Task ExecuteAsync_NoOverrides_KeepsLegacyBehavior()
    {
        var service = await CreateCommandServiceAsync(new Command
        {
            Id = "c-legacy",
            Name = "!hello",
            Response = "the one reply",
            Permission = "Everyone"
        });

        Assert.Equal("the one reply", await service.ExecuteAsync("!hello", ContextOn("Twitch")));
        Assert.Equal("the one reply", await service.ExecuteAsync("!hello", ContextOn("Kick")));
        Assert.Equal("the one reply", await service.ExecuteAsync("!hello", ContextOn("Discord")));
    }
}
