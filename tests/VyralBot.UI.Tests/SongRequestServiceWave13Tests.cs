using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Songs;
using VyralBot.Data.Entities;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Song request queue (WAVE 13): the SongRequests table behind a temp SQLite
/// db, the !sr command family handled by <see cref="SongRequestService"/>
/// (URL/id parsing, per-user + total caps, mod gating, queue order, status
/// transitions, title resolution seam).
/// </summary>
public class SongRequestServiceWave13Tests
{
    private static VyralBot.Data.IDataService CreateData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-songs-{Guid.NewGuid():N}.db");
        return new VyralBot.Data.DapperDataService($"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
    }

    private static async Task<SongRequestService> CreateServiceAsync(ITitleResolver? resolver = null)
    {
        var data = CreateData();
        await data.EnsureDatabaseCreatedAsync();
        return new SongRequestService(NullLogger<SongRequestService>.Instance, data, resolver);
    }

    private const string VideoId = "dQw4w9WgXcQ";

    private sealed class FakeResolver : ITitleResolver
    {
        private readonly string _title;
        private readonly int? _duration;

        public FakeResolver(string title, int? duration = null)
        {
            _title = title;
            _duration = duration;
        }

        public Task<SongTitleInfo?> ResolveAsync(string youtubeId) =>
            Task.FromResult<SongTitleInfo?>(new SongTitleInfo(_title, _duration));
    }

    // ===== URL parsing variants =====

    [Theory]
    [InlineData("dQw4w9WgXcQ")]                                                    // bare 11-char id
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]                    // classic watch URL
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]              // watch URL with timestamp
    [InlineData("https://www.youtube.com/watch?app=desktop&v=dQw4w9WgXcQ")]        // watch URL, v not first param
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc123")]                         // short link
    [InlineData("youtu.be/dQw4w9WgXcQ")]                                          // scheme-less short link
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]                      // mobile
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]                  // YouTube Music
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]                       // live permalink
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]                     // shorts
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]                      // embed
    public async Task Enqueue_AcceptsAllYoutubeUrlShapes(string input)
    {
        var svc = await CreateServiceAsync();

        var result = await svc.EnqueueAsync(input, "alice", "Twitch");

        Assert.True(result.Success, $"expected success for '{input}', got: {result.Error}");
        Assert.Equal(VideoId, result.Request!.YoutubeId);
    }

    [Theory]
    [InlineData("never gonna give you up")]      // search text is NOT supported
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")] // not a YouTube host
    [InlineData("dQw4w9WgX")]                    // 10 chars — too short
    [InlineData("dQw4w9WgXcQ!")]                 // 12 chars / bad char
    [InlineData("")]
    public async Task Enqueue_RejectsSearchTextAndBadIds(string input)
    {
        var svc = await CreateServiceAsync();

        var result = await svc.EnqueueAsync(input, "alice", "Twitch");

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await svc.GetQueueAsync());
    }

    [Fact]
    public async Task Enqueue_DefaultTitleIsVideoId_WhenResolverHasNoData()
    {
        var svc = await CreateServiceAsync();

        var result = await svc.EnqueueAsync($"https://youtu.be/{VideoId}", "alice", "Twitch");

        Assert.True(result.Success);
        Assert.Equal($"Video {VideoId}", result.Request!.Title);
        Assert.Null(result.Request.DurationSeconds);
    }

    [Fact]
    public async Task Enqueue_ResolverSeam_SuppliesTitleAndDuration()
    {
        var svc = await CreateServiceAsync(new FakeResolver("Never Gonna Give You Up", 213));

        var result = await svc.EnqueueAsync(VideoId, "alice", "Twitch");

        Assert.True(result.Success);
        Assert.Equal("Never Gonna Give You Up", result.Request!.Title);
        Assert.Equal(213, result.Request.DurationSeconds);
    }

    // ===== Caps (config keys Songs.MaxPerUser / Songs.MaxQueue) =====

    [Fact]
    public async Task Enqueue_PerUserCap_DefaultTwo_BlocksThirdRequest()
    {
        var svc = await CreateServiceAsync();
        await svc.EnqueueAsync("aaaaaaaaaaa", "alice", "Twitch");
        await svc.EnqueueAsync("bbbbbbbbbbb", "alice", "Twitch");

        var third = await svc.EnqueueAsync("ccccccccccc", "alice", "Twitch");

        Assert.False(third.Success);
        Assert.Contains("max 2", third.Error);
        Assert.Equal(2, (await svc.GetQueueAsync()).Count());

        // Other viewers are unaffected
        Assert.True((await svc.EnqueueAsync("ccccccccccc", "bob", "Twitch")).Success);
    }

    [Fact]
    public async Task Enqueue_TotalCap_ReadsConfigKey_AndBlocksAtLimit()
    {
        var data = CreateData();
        await data.EnsureDatabaseCreatedAsync();
        await data.SetConfigValueAsync("Songs.MaxQueue", "3");
        var svc = new SongRequestService(NullLogger<SongRequestService>.Instance, data);

        Assert.True((await svc.EnqueueAsync("aaaaaaaaaaa", "u1", "Twitch")).Success);
        Assert.True((await svc.EnqueueAsync("bbbbbbbbbbb", "u2", "Twitch")).Success);
        Assert.True((await svc.EnqueueAsync("ccccccccccc", "u3", "Twitch")).Success);

        var fourth = await svc.EnqueueAsync("ddddddddddd", "u4", "Twitch");

        Assert.False(fourth.Success);
        Assert.Contains("full (3/3)", fourth.Error);
    }

    [Fact]
    public async Task Enqueue_PerUserCap_ReadsConfigKey()
    {
        var data = CreateData();
        await data.EnsureDatabaseCreatedAsync();
        await data.SetConfigValueAsync("Songs.MaxPerUser", "1");
        var svc = new SongRequestService(NullLogger<SongRequestService>.Instance, data);

        Assert.True((await svc.EnqueueAsync("aaaaaaaaaaa", "alice", "Kick")).Success);
        var second = await svc.EnqueueAsync("bbbbbbbbbbb", "alice", "Kick");

        Assert.False(second.Success);
        Assert.Contains("max 1", second.Error);
    }

    // ===== !sr command family =====

    [Fact]
    public async Task ProcessCommand_NonSongMessages_ReturnNull()
    {
        var svc = await CreateServiceAsync();
        Assert.Null(await svc.ProcessCommandAsync("!points", "alice", "Twitch", false));
        Assert.Null(await svc.ProcessCommandAsync("!srtotallydifferent", "alice", "Twitch", false));
        Assert.Null(await svc.ProcessCommandAsync("hello chat", "alice", "Twitch", false));
        Assert.Null(await svc.ProcessCommandAsync("", "alice", "Twitch", true));
    }

    [Fact]
    public async Task ProcessCommand_SrWithoutArgument_ShowsUsage()
    {
        var svc = await CreateServiceAsync();
        var reply = await svc.ProcessCommandAsync("!sr", "alice", "Twitch", false);
        Assert.NotNull(reply);
        Assert.StartsWith("Usage:", reply);
    }

    [Fact]
    public async Task ProcessCommand_SrWithUrl_EnqueuesAndReportsPosition()
    {
        var svc = await CreateServiceAsync();
        await svc.ProcessCommandAsync("!sr aaaaaaaaaaa", "alice", "Twitch", false);
        var reply = await svc.ProcessCommandAsync("!sr https://youtu.be/dQw4w9WgXcQ", "bob", "Kick", false);

        Assert.NotNull(reply);
        Assert.Contains("position #2", reply);

        var queue = (await svc.GetQueueAsync()).ToList();
        Assert.Equal(2, queue.Count);
        Assert.Equal("alice", queue[0].RequestedBy);
        Assert.Equal(VideoId, queue[1].YoutubeId);
        Assert.Equal("Kick", queue[1].Platform);
    }

    [Fact]
    public async Task ProcessCommand_SrQueue_ShowsNextThree()
    {
        var svc = await CreateServiceAsync();
        await svc.EnqueueAsync("aaaaaaaaaaa", "alice", "Twitch");
        await svc.EnqueueAsync("bbbbbbbbbbb", "bob", "Twitch");
        await svc.EnqueueAsync("ccccccccccc", "carol", "Twitch");
        await svc.EnqueueAsync("ddddddddddd", "dave", "Twitch");

        var reply = await svc.ProcessCommandAsync("!srqueue", "viewer", "Twitch", false);

        Assert.NotNull(reply);
        Assert.Contains("(4)", reply);
        Assert.Contains("(alice)", reply);
        Assert.Contains("(bob)", reply);
        Assert.Contains("(carol)", reply);
        Assert.DoesNotContain("(dave)", reply);
    }

    [Fact]
    public async Task ProcessCommand_SrQueue_EmptyQueue()
    {
        var svc = await CreateServiceAsync();
        var reply = await svc.ProcessCommandAsync("!srqueue", "viewer", "Twitch", false);
        Assert.NotNull(reply);
        Assert.Contains("empty", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessCommand_SkipAndClear_ModGated()
    {
        var svc = await CreateServiceAsync();
        await svc.EnqueueAsync("aaaaaaaaaaa", "alice", "Twitch");
        await svc.MarkPlayingAsync();

        var skipDenied = await svc.ProcessCommandAsync("!srskip", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(skipDenied);
        Assert.Contains("moderator", skipDenied, StringComparison.OrdinalIgnoreCase);

        var clearDenied = await svc.ProcessCommandAsync("!srclear", "viewer", "Twitch", isModerator: false);
        Assert.NotNull(clearDenied);
        Assert.Contains("moderator", clearDenied, StringComparison.OrdinalIgnoreCase);

        var skipOk = await svc.ProcessCommandAsync("!srskip", "mod1", "Twitch", isModerator: true);
        Assert.NotNull(skipOk);
        Assert.Contains("Skipped", skipOk);

        var clearOk = await svc.ProcessCommandAsync("!srclear", "mod1", "Twitch", isModerator: true);
        Assert.NotNull(clearOk);
        Assert.Contains("cleared", clearOk, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await svc.GetQueueAsync());
    }

    [Fact]
    public async Task ProcessCommand_Skip_NothingPlaying()
    {
        var svc = await CreateServiceAsync();
        var reply = await svc.ProcessCommandAsync("!srskip", "mod1", "Twitch", isModerator: true);
        Assert.NotNull(reply);
        Assert.Contains("Nothing is currently playing", reply);
    }

    // ===== Queue order + status transitions =====

    [Fact]
    public async Task QueueOrder_IsInsertionOrder_AndSurvivesLifecycle()
    {
        var svc = await CreateServiceAsync();
        await svc.EnqueueAsync("aaaaaaaaaaa", "alice", "Twitch");
        await svc.EnqueueAsync("bbbbbbbbbbb", "bob", "Twitch");
        await svc.EnqueueAsync("ccccccccccc", "carol", "Twitch");

        // First request starts playing
        var playing = await svc.MarkPlayingAsync();
        Assert.NotNull(playing);
        Assert.Equal("aaaaaaaaaaa", playing!.YoutubeId);
        Assert.Equal(SongRequestStatuses.Playing, playing.Status);
        Assert.Equal(2, (await svc.GetQueueAsync()).Count());

        // Song finishes -> next one can play
        var played = await svc.MarkPlayedAsync();
        Assert.NotNull(played);
        Assert.Equal(SongRequestStatuses.Played, played!.Status);
        Assert.Null(await svc.GetCurrentSongRequestAsync());

        var second = await svc.MarkPlayingAsync();
        Assert.NotNull(second);
        Assert.Equal("bbbbbbbbbbb", second!.YoutubeId);

        // Mod skip ends it early
        var skipped = await svc.SkipCurrentAsync();
        Assert.NotNull(skipped);
        Assert.Equal(SongRequestStatuses.Skipped, skipped!.Status);
        Assert.Null(await svc.GetCurrentSongRequestAsync());

        // Third request is still queued and plays next
        var third = await svc.MarkPlayingAsync();
        Assert.NotNull(third);
        Assert.Equal("ccccccccccc", third!.YoutubeId);

        // Empty queue promotes nothing
        await svc.MarkPlayedAsync();
        Assert.Null(await svc.MarkPlayingAsync());
        Assert.Null(await svc.MarkPlayedAsync());
        Assert.Null(await svc.SkipCurrentAsync());
    }

    // ===== Title resolution seam (player callback) =====

    [Fact]
    public async Task UpdateTitle_SetsTitleAndDuration_OnMatchingRows()
    {
        var svc = await CreateServiceAsync();
        var first = await svc.EnqueueAsync("aaaaaaaaaaa", "alice", "Twitch");
        var second = await svc.EnqueueAsync("aaaaaaaaaaa", "bob", "Kick");
        await svc.EnqueueAsync("bbbbbbbbbbb", "carol", "Twitch");

        var updated = await svc.UpdateTitleAsync("aaaaaaaaaaa", "Song A", 180);

        Assert.Equal(2, updated);
        var queue = (await svc.GetQueueAsync()).ToList();
        Assert.All(queue.Where(r => r.YoutubeId == "aaaaaaaaaaa"), r => Assert.Equal("Song A", r.Title));
        Assert.Equal(180, queue.First(r => r.YoutubeId == "aaaaaaaaaaa").DurationSeconds);
        Assert.Equal($"Video bbbbbbbbbbb", queue.First(r => r.YoutubeId == "bbbbbbbbbbb").Title);
        Assert.Equal(first.Request!.Id + 1, second.Request!.Id); // sequential ids like Quotes

        // The queue reply now shows the resolved title
        var reply = await svc.ProcessCommandAsync("!srqueue", "viewer", "Twitch", false);
        Assert.NotNull(reply);
        Assert.Contains("Song A", reply);
    }
}
