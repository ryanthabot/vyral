using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Commands;
using VyralBot.Data.Entities;

namespace VyralBot.UI.Tests;

// ======================================================================
// WAVE-15 coverage for the Nightbot / StreamElements command import
// (Commands page CUSTOM tab IMPORT button):
//   - Nightbot CSV parser: happy path, header row + quoted fields, skip
//     paths (Nightbot built-ins, Vyral built-ins, blank responses, in-file
//     duplicates)
//   - StreamElements JSON parser: happy path, name normalization, wrapper
//     object root, invalid JSON
//   - Plain-text fallback: happy path + malformed lines
//   - Format auto-detection matrix
//   - Variable translation matrix ($(user)/${user} → {username} etc.,
//     unmapped tokens left verbatim and flagged)
//   - ApplyAsync: created row shape (enabled, all platforms, usage 0) and
//     the duplicate-name skip
// Fake ICommandService follows the wave-8/14 double style: real behavior
// for the members the importer touches, everything else throws.
// ======================================================================

public class Wave15CommandImportTests
{
    private static CommandImportService Service(Wave15CommandService? commands = null)
        => new(commands ?? new Wave15CommandService(), NullLogger<CommandImportService>.Instance);

    // ── Nightbot CSV ───────────────────────────────────────────────────

    [Fact]
    public async Task NightbotCsv_HappyPath_MapsPermissionCooldownVariables()
    {
        var preview = await Service().ParseAsync(
            "!hug,everyone,10,$(user) hugs $(touser)!\n!clearchat,moderator,60,Cleared $(count) times",
            "nightbot");

        Assert.Equal(2, preview.ImportableCount);
        Assert.Equal(0, preview.SkippedCount);

        var hug = preview.Rows[0];
        Assert.Equal("!hug", hug.Name);
        Assert.False(hug.ModOnly);
        Assert.Equal(10, hug.CooldownSec);
        Assert.Equal("{username} hugs {username}!", hug.Response);
        Assert.Null(hug.Notes);

        var clear = preview.Rows[1];
        Assert.Equal("!clearchat", clear.Name);
        Assert.True(clear.ModOnly);
        Assert.Equal(60, clear.CooldownSec);
        Assert.Equal("Cleared {count} times", clear.Response);
    }

    [Fact]
    public async Task NightbotCsv_HeaderRow_AndQuotedFields_AreSupported()
    {
        var preview = await Service().ParseAsync(
            "command,userlevel,cooldown,count,response,text\n" +
            "!slap,everyone,15,12,\"$(user) slaps $(touser) with a, large trout\",\"\"",
            "nightbot");

        var row = Assert.Single(preview.Rows);
        Assert.Equal("!slap", row.Name);
        Assert.Equal(15, row.CooldownSec);
        Assert.Equal("{username} slaps {username} with a, large trout", row.Response);
        Assert.False(row.Skipped);
    }

    [Fact]
    public async Task NightbotCsv_SkipsBuiltIns_BlankResponses_AndInFileDuplicates()
    {
        var preview = await Service().ParseAsync(
            "!commands,everyone,5,the list\n" +          // Nightbot built-in
            "!regulars,everyone,5,regulars\n" +          // Nightbot built-in
            "!uptime,everyone,5,Up $(uptime)\n" +        // Vyral built-in
            "!points,everyone,5,points\n" +              // Vyral built-in
            "!empty,everyone,5,\n" +                     // no response
            "!slap,everyone,5,first\n" +                 // ok
            "!slap,moderator,5,second\n",                // duplicate in file
            "nightbot");

        Assert.Equal(1, preview.ImportableCount);
        Assert.Equal(6, preview.SkippedCount);

        Assert.Equal("Nightbot built-in", preview.Rows[0].SkipReason);
        Assert.Equal("Vyral already has a built-in !uptime", preview.Rows[2].SkipReason);
        Assert.Equal("Vyral already has a built-in !points", preview.Rows[3].SkipReason);
        Assert.Equal("No response text", preview.Rows[4].SkipReason);
        Assert.Equal("Duplicate of an earlier row in this file", preview.Rows[6].SkipReason);

        var slap = preview.Rows[5];
        Assert.False(slap.Skipped);
        Assert.Equal("first", slap.Response); // the FIRST row wins, the dup is skipped
    }

    // ── StreamElements JSON ────────────────────────────────────────────

    [Fact]
    public async Task StreamElementsJson_HappyPath_MapsBothCooldownsAndPermission()
    {
        var preview = await Service().ParseAsync(
            """
            [
              {"command":"!fly","reply":"$(user) can fly ${user}","enabled":true,"cooldownAll":5,"cooldownUser":30,"permission":"moderator"},
              {"command":"lurk","reply":"Thanks for lurking $(count)","enabled":false,"cooldownUser":0,"permission":"everyone"}
            ]
            """,
            "streamelements");

        Assert.Equal(2, preview.ImportableCount);

        var fly = preview.Rows[0];
        Assert.Equal("!fly", fly.Name);
        Assert.True(fly.ModOnly);
        Assert.Equal(30, fly.CooldownSec);      // cooldownUser → user cooldown
        Assert.Equal(5, fly.GlobalCooldownSec); // cooldownAll → global cooldown
        Assert.Equal("{username} can fly {username}", fly.Response);
        Assert.Equal("StreamElements", fly.Source);

        // A command without the leading '!' is normalized, and "enabled":
        // false still imports (rows are always created enabled).
        var lurk = preview.Rows[1];
        Assert.Equal("!lurk", lurk.Name);
        Assert.False(lurk.ModOnly);
        Assert.False(lurk.Skipped);
        Assert.Equal("Thanks for lurking {count}", lurk.Response);
    }

    [Fact]
    public async Task StreamElementsJson_WrapperObjectRoot_IsUnwrapped()
    {
        var preview = await Service().ParseAsync(
            """{"commands":[{"command":"!fly","reply":"Up!","enabled":true}]}""",
            "streamelements");

        Assert.Equal(1, preview.ImportableCount);
        Assert.Equal("!fly", preview.Rows[0].Name);
    }

    [Fact]
    public async Task StreamElementsJson_InvalidJson_YieldsEmptyPreview()
    {
        var preview = await Service().ParseAsync("this is ] not json [", "streamelements");

        Assert.Empty(preview.Rows);
        Assert.Equal(0, preview.ImportableCount);
    }

    [Fact]
    public async Task StreamElementsJson_BlankReply_IsSkipped()
    {
        var preview = await Service().ParseAsync(
            """[{"command":"!fly","reply":""}]""",
            "streamelements");

        var row = Assert.Single(preview.Rows);
        Assert.True(row.Skipped);
        Assert.Equal("No response text", row.SkipReason);
    }

    // ── Plain text ─────────────────────────────────────────────────────

    [Fact]
    public async Task PlainText_HappyPath_AndMalformedLines()
    {
        var preview = await Service().ParseAsync(
            "!hello=Hello $(user)!\n" +
            "bye=Goodbye $(touser)\n" +        // no '!' prefix is fine
            "# a comment line\n" +             // ignored silently
            "this is not a command\n" +        // no '=' → skipped
            "\n",
            "plain");

        Assert.Equal(2, preview.ImportableCount);
        Assert.Equal(1, preview.SkippedCount);

        Assert.Equal("!hello", preview.Rows[0].Name);
        Assert.Equal("Hello {username}!", preview.Rows[0].Response);
        Assert.Equal("!bye", preview.Rows[1].Name);
        Assert.Equal("Not a !name=response line", preview.Rows[2].SkipReason);
    }

    // ── Format auto-detection + validation ─────────────────────────────

    [Theory]
    [InlineData("nightbot")]
    [InlineData("auto")]
    [InlineData("")]
    public async Task Parse_AutoDetects_NightbotCsv(string format)
    {
        var preview = await Service().ParseAsync("!hug,everyone,10,hi there", format);

        Assert.Equal("nightbot", preview.Format);
        Assert.Equal(1, preview.ImportableCount);
        Assert.Equal("!hug", preview.Rows[0].Name);
    }

    [Theory]
    [InlineData("auto", "streamelements")]
    [InlineData("auto", "plain")]
    [InlineData("nightbot", "nightbot")]
    public async Task Parse_FormatDetection_Matrix(string requested, string expected)
    {
        var content = expected switch
        {
            "streamelements" => """[{"command":"!fly","reply":"up"}]""",
            "nightbot" => "!hug,everyone,10,hi",
            _ => "!hello=hi"
        };

        var preview = await Service().ParseAsync(content, requested);
        Assert.Equal(expected, preview.Format);
    }

    [Fact]
    public void DetectFormat_PlainLineWithCommas_IsNotCsv()
    {
        // A plain !hello=hi, welcome, enjoy line must not smell like CSV.
        Assert.Equal("plain", Service().DetectFormat("!hello=hi, welcome, enjoy"));
    }

    [Fact]
    public async Task Parse_UnknownFormat_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().ParseAsync("!hello=hi", "discorddotchat"));
    }

    [Fact]
    public async Task Parse_EmptyContent_EmptyPreview()
    {
        var preview = await Service().ParseAsync("", "auto");
        Assert.Empty(preview.Rows);
    }

    // ── Variable translation matrix ────────────────────────────────────

    [Theory]
    [InlineData("$(user)", "{username}")]
    [InlineData("$(touser)", "{username}")]
    [InlineData("${user}", "{username}")]
    [InlineData("${touser}", "{username}")]
    [InlineData("$(count)", "{count}")]
    [InlineData("$(time)", "{time}")]
    [InlineData("$(uptime)", "{uptime}")]
    [InlineData("$(uptimeapi)", "{uptime}")]
    [InlineData("$(watchtime)", "{watchtime}")]
    [InlineData("$(channel)", "{channel}")]
    [InlineData("hey $(user), welcome to $(channel)!", "hey {username}, welcome to {channel}!")]
    public void TranslateVariables_MapsKnownTokens(string source, string expected)
    {
        var result = CommandImportService.TranslateVariables(source, out var untranslated);

        Assert.Equal(expected, result);
        Assert.Empty(untranslated);
    }

    [Theory]
    [InlineData("$(query)")]
    [InlineData("$(followage)")]
    [InlineData("$(eval 1+1)")]
    [InlineData("$(random.pick a, b)")]
    [InlineData("$(urlfetch https://example.com)")]
    [InlineData("$(time America/New_York)")]
    [InlineData("${random.pick 'a', 'b'}")]
    public void TranslateVariables_LeavesUnknownTokens_Verbatim(string source)
    {
        var result = CommandImportService.TranslateVariables(source, out var untranslated);

        Assert.Equal(source, result);
    }

    [Fact]
    public void TranslateVariables_FlagsDetectableUnknownTokens()
    {
        var result = CommandImportService.TranslateVariables(
            "$(user) has $(query) and $(followage)", out var untranslated);

        Assert.Equal("{username} has $(query) and $(followage)", result);
        Assert.Equal(new[] { "$(query)", "$(followage)" }, untranslated);
    }

    [Fact]
    public async Task Preview_NotesUntranslatedTokens_OnTheRow()
    {
        var preview = await Service().ParseAsync("!follow=$(followage) years", "plain");

        var row = Assert.Single(preview.Rows);
        Assert.False(row.Skipped);
        Assert.Contains("$(followage)", row.Notes);
    }

    // ── Apply ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_CreatesEnabledCommands_OnAllPlatforms_WithZeroUsage()
    {
        var commands = new Wave15CommandService();
        var preview = await Service(commands).ParseAsync(
            "!hug,everyone,10,hug!\n!clearchat,moderator,60,cleared", "nightbot");

        var result = await Service(commands).ApplyAsync(preview);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(2, commands.Registered.Count);

        var hug = commands.Registered[0];
        Assert.Equal("!hug", hug.Name);
        Assert.Equal("Everyone", hug.Permission);
        Assert.Equal(10, hug.UserCooldown);
        Assert.Equal(0, hug.GlobalCooldown);
        Assert.True(hug.IsEnabled);
        Assert.False(hug.IsBuiltIn);
        Assert.True(hug.EnabledOnKick);
        Assert.True(hug.EnabledOnTwitch);
        Assert.Equal(0, hug.UsageCount);
        Assert.Null(hug.BotId);

        Assert.Equal("Moderator", commands.Registered[1].Permission);
    }

    [Fact]
    public async Task Apply_SkipsExistingNames_WithoutOverwriting()
    {
        var commands = new Wave15CommandService();
        commands.Store(new Command
        {
            Id = "existing",
            Name = "!lurk",
            Response = "the original lurk",
            Permission = "Everyone"
        });

        var preview = await Service(commands).ParseAsync(
            "!lurk,everyone,5,imported lurk\n!hug,everyone,10,hug!", "nightbot");

        var result = await Service(commands).ApplyAsync(preview);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(new[] { "!lurk" }, result.SkippedNames);

        // The existing custom row is untouched — imports never overwrite.
        Assert.Equal("the original lurk", commands.GetByName("!lurk")!.Response);
        Assert.Single(commands.Registered, c => c.Name == "!hug");
    }

    [Fact]
    public async Task Apply_SkipsBuiltInShadow_EvenWithoutLiveCheck()
    {
        var commands = new Wave15CommandService();
        commands.Store(new Command
        {
            Id = "builtin",
            Name = "!hug",
            Response = "built-in hug",
            Permission = "Everyone",
            IsBuiltIn = true
        });

        var preview = await Service(commands).ParseAsync("!hug,everyone,5,my hug", "nightbot");
        var result = await Service(commands).ApplyAsync(preview);

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.True(preview.Rows[0].Skipped);
        Assert.Equal("Already exists", preview.Rows[0].SkipReason);
    }
}

/// <summary>
/// Configurable ICommandService for the command importer: real in-memory
/// behavior for RegisterCommandAsync / GetCommandByNameAsync /
/// GetAllCommandsAsync (the members CommandImportService touches); every
/// other member throws so an accidental coupling fails loudly.
/// </summary>
public sealed class Wave15CommandService : ICommandService
{
    private const string NotImplemented = "Not needed for wave-15 import tests.";

    private readonly Dictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Commands handed to RegisterCommandAsync, in call order.</summary>
    public List<Command> Registered { get; } = new();

    public Command? GetByName(string name)
        => _commands.TryGetValue(name, out var command) ? command : null;

    public void Store(Command command) => _commands[command.Name] = command;

    public Task<bool> RegisterCommandAsync(Command command)
    {
        if (_commands.TryGetValue(command.Name, out var existing) && existing.IsBuiltIn)
        {
            return Task.FromResult(false);
        }

        // Same save semantics as the real service: the row is stored by name
        // (an unchecked duplicate would overwrite — ApplyAsync must prevent
        // that by checking GetCommandByNameAsync first, which the
        // never-overwrite test asserts).
        _commands[command.Name] = command;
        Registered.Add(command);
        return Task.FromResult(true);
    }

    public Task<Command?> GetCommandByNameAsync(string name)
        => Task.FromResult(GetByName(name));

    public Task<IEnumerable<Command>> GetAllCommandsAsync(bool? enabledOnly = null)
        => Task.FromResult<IEnumerable<Command>>(
            _commands.Values.Where(c => enabledOnly == null || c.IsEnabled == enabledOnly).ToList());

    public Task InitializeAsync() => throw new NotImplementedException(NotImplemented);
    public Task<string?> ExecuteAsync(string message, CommandContext context, string? discordBotId = null) => throw new NotImplementedException(NotImplemented);
    public bool IsCommand(string message) => throw new NotImplementedException(NotImplemented);
    public Task<bool> UpdateCommandAsync(Command command) => throw new NotImplementedException(NotImplemented);
    public Task<bool> DeleteCommandAsync(string id) => throw new NotImplementedException(NotImplemented);
    public bool HasPermission(CommandContext context, string permission) => throw new NotImplementedException(NotImplemented);
    public Task<(bool OnCooldown, int SecondsRemaining)> CheckCooldownAsync(Command command, CommandContext context) => throw new NotImplementedException(NotImplemented);
    public string ParseVariables(string text, CommandContext context) => throw new NotImplementedException(NotImplemented);
}
