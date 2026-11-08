using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Audio;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-14 coverage for the COMMAND half of <see cref="TextToSpeechService"/>
/// (!tts): parse/gate seams, viewer-allow toggle, disabled → null, empty
/// text → usage, 300-char default truncation, the per-user 30s cooldown and
/// the moderator bypass.
///
/// VOICELESS-ENVIRONMENT SAFETY (deliberate seam choice): the class creates
/// the real <c>SpeechSynthesizer</c> lazily on its worker loop — construction
/// only starts an idle queue consumer, so constructing/disposing is safe. To
/// keep accepted commands from ever reaching SAPI, the tests use
/// <see cref="Wave14TtsDataService"/> (see its doc comment): every
/// command-level DND read passes, every speak-level DND read trips, so the
/// SpeakAsync inside an accepted command returns BEFORE enqueueing — no
/// utterance is ever queued or spoken. Accepted-command tests assert
/// <see cref="Wave14TtsDataService.DndReads"/> reached the expected parity to
/// prove the speak path was entered and gated rather than skipped. On any
/// ordering change the parity fails safe (a tripped command gate returns null).
/// </summary>
public class Wave14TtsCommandTests
{
    private const string Viewer = "chattyviewer";
    private const string OtherViewer = "anotherviewer";
    private const string Mod = "moderator";

    /// <summary>Enables TTS (off by default) via the Tts.* config rows.</summary>
    private static Wave14TtsDataService EnabledData(bool allowViewers = true)
    {
        var data = new Wave14TtsDataService();
        data.ConfigValues["Tts.Enabled"] = "True";
        data.ConfigValues["Tts.AllowViewers"] = allowViewers.ToString();
        return data;
    }

    private static TextToSpeechService Service(Wave14TtsDataService data)
        => new(NullLogger<TextToSpeechService>.Instance, data);

    private static async Task<string?> Run(
        Wave14TtsDataService data,
        string message,
        bool isModerator = false,
        string username = Viewer,
        string platform = "Twitch")
    {
        using var service = Service(data);
        return await service.ProcessCommandAsync(username, platform, isModerator, message);
    }

    // ── constructibility + parse seams (no speech possible) ────────────

    [Fact]
    public async Task Service_IsConstructibleAndDisposable_WithoutTouchingSpeech()
    {
        // The SpeechSynthesizer is created lazily on the worker loop; an empty
        // queue means construction/dispose never reaches SAPI (voiceless-safe).
        // GetVoices() is deliberately NOT called — it hard-creates a synthesizer.
        using (var service = Service(new Wave14TtsDataService()))
        {
            var settings = await service.GetSettingsAsync();
            Assert.NotNull(settings);
        }

        // Defaults with nothing persisted: disabled, viewers allowed, 300-char max.
        using (var service = Service(new Wave14TtsDataService()))
        {
            var defaults = await service.GetSettingsAsync();
            Assert.False(defaults.Enabled);
            Assert.True(defaults.AllowViewers);
            Assert.Equal(300, defaults.MaxLength);
        }
    }

    [Theory]
    [InlineData("!hello")]
    [InlineData("!ttsxyz hello")]
    [InlineData("hey no command here")]
    public async Task NonTtsCommands_FallThrough_Null(string message)
    {
        Assert.Null(await Run(EnabledData(), message));
    }

    [Theory]
    [InlineData("!tts")]
    [InlineData("!tts   ")]
    public async Task EmptyText_GetsUsage_BeforeTheEnabledGate(string message)
    {
        // Nothing persisted → TTS disabled; usage still wins because the
        // empty-text check runs before the settings gate.
        var data = new Wave14TtsDataService();
        Assert.Equal("Usage: !tts <message>", await Run(data, message));
        Assert.Equal(0, data.DndReads);
    }

    // ── toggles ────────────────────────────────────────────────────────

    [Fact]
    public async Task Disabled_ReturnsNull()
    {
        var data = new Wave14TtsDataService(); // Tts.Enabled unset → false
        Assert.Null(await Run(data, "!tts hello there", isModerator: true, username: Mod));
        Assert.Equal(0, data.DndReads);
    }

    [Fact]
    public async Task ViewersDenied_ReturnsNull_ModsUnaffected()
    {
        var data = EnabledData(allowViewers: false);

        Assert.Null(await Run(data, "!tts pick this up", isModerator: false, username: Viewer));
        Assert.Equal(0, data.DndReads); // never got past the viewer gate

        Assert.Equal(
            $"🔊 {Mod} says: pick this up",
            await Run(data, "!tts pick this up", isModerator: true, username: Mod));
    }

    [Fact]
    public async Task ViewersAllowed_AcceptsAndReplies()
    {
        var data = EnabledData();

        var reply = await Run(data, "!tts hello stream");

        Assert.Equal($"🔊 {Viewer} says: hello stream", reply);
        // Two DND reads = command gate passed AND the SpeakAsync inside was
        // DND-gated before enqueueing → nothing was ever queued to speak.
        Assert.Equal(2, data.DndReads);
    }

    // ── truncation (default MaxLength = 300) ───────────────────────────

    [Fact]
    public async Task LongText_IsTruncatedToDefault300Chars()
    {
        var data = EnabledData();
        var longText = new string('x', 320);

        var reply = await Run(data, $"!tts {longText}");

        Assert.Equal($"🔊 {Viewer} says: {new string('x', 300)}", reply);
        Assert.Equal(2, data.DndReads); // speak path entered and DND-gated
    }

    [Fact]
    public async Task ShortText_IsNotTruncated()
    {
        var data = EnabledData();
        Assert.Equal($"🔊 {Viewer} says: hi", await Run(data, "!tts hi"));
    }

    // ── per-user 30s cooldown + mod bypass ─────────────────────────────
    // The cooldown map lives on the service instance, so these tests drive
    // ONE service for both commands (the chat pipeline holds a singleton).

    private static Task<string?> RunOn(
        TextToSpeechService service, string message, bool isModerator, string username)
        => service.ProcessCommandAsync(username, "Twitch", isModerator, message);

    [Fact]
    public async Task Cooldown_SecondUseWithin30Seconds_IsRejectedWithRemaining()
    {
        var data = EnabledData();
        using var service = Service(data);

        var first = await RunOn(service, "!tts one", isModerator: false, username: Viewer);
        var second = await RunOn(service, "!tts two", isModerator: false, username: Viewer);

        Assert.Equal($"🔊 {Viewer} says: one", first);
        Assert.NotNull(second);
        var prefix = $"⏳ {Viewer}, you can use !tts again in";
        Assert.StartsWith(prefix, second);
        var tail = second![prefix.Length..]; // e.g. "30s."
        Assert.EndsWith("s.", tail);

        // The remaining window is the 30s cooldown minus the microseconds
        // between the two awaited calls — i.e. 29 or 30 whole seconds.
        Assert.InRange(int.Parse(tail[..^2]), 29, 30);

        // cmd DND=1(False, passed) + speak DND=2(True, gated) + cmd DND=3
        // (False, passed) — the rejected command never reaches SpeakAsync.
        Assert.Equal(3, data.DndReads);
    }

    [Fact]
    public async Task Cooldown_IsPerUser()
    {
        var data = EnabledData();
        using var service = Service(data);

        Assert.Equal($"🔊 {Viewer} says: mine", await RunOn(service, "!tts mine", isModerator: false, username: Viewer));
        Assert.Equal($"🔊 {OtherViewer} says: also mine", await RunOn(service, "!tts also mine", isModerator: false, username: OtherViewer));
    }

    [Fact]
    public async Task Cooldown_ModeratorsBypass()
    {
        var data = EnabledData();
        using var service = Service(data);

        var first = await RunOn(service, "!tts first", isModerator: true, username: Mod);
        var second = await RunOn(service, "!tts second", isModerator: true, username: Mod);

        Assert.Equal($"🔊 {Mod} says: first", first);
        Assert.Equal($"🔊 {Mod} says: second", second);
    }
}
