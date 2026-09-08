using VyralBot.Integrations.StreamDeck;
using Xunit;

namespace VyralBot.UI.Tests;

public class StreamDeckServiceTests
{
    private sealed class FakeStore : IStreamDeckStore
    {
        public string StoredJson { get; set; } = """[{"id":"a1","name":"Hype","uri":"streamdeck://plugins/com.elgato.cpu/act?x=1"}]""";
        public int SaveCount;
        public Task<string> LoadAsync() => Task.FromResult(StoredJson);
        public Task SaveAsync(string json) { StoredJson = json; SaveCount++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SaveAction_RejectsNonStreamDeckUri()
    {
        var result = await new StreamDeckService(new FakeStore()).SaveActionAsync("Bad", "https://evil.example");
        Assert.False(result.Success);
        Assert.Contains("streamdeck://", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAction_NormalizesSchemeAndUpsertsByName()
    {
        var store = new FakeStore();
        var ok = await new StreamDeckService(store).SaveActionAsync("hype", "STREAMDECK://plugins/com.elgato.cpu/act");
        Assert.True(ok.Success);
        var one = Assert.Single(await new StreamDeckService(store).GetActionsAsync());
        Assert.Equal("hype", one.Name);
        Assert.Equal("streamdeck://plugins/com.elgato.cpu/act", one.Uri);
        Assert.NotEqual("a1", one.Id);
    }

    [Fact]
    public async Task DeleteAction_RemovesById()
    {
        var store = new FakeStore();
        var svc = new StreamDeckService(store);
        await svc.DeleteActionAsync("a1");
        Assert.Empty(await svc.GetActionsAsync());
    }

    [Fact]
    public async Task Trigger_ByActionName_CaseInsensitive_LaunchesUri()
    {
        string? launched = null;
        var svc = new StreamDeckService(new FakeStore(), uri => launched = uri);
        var result = await svc.TriggerAsync("HYPE");
        Assert.Contains("Hype", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("streamdeck://plugins/com.elgato.cpu/act?x=1", launched);
    }

    [Fact]
    public async Task Trigger_UnknownAction_FriendlyMessage()
    {
        var svc = new StreamDeckService(new FakeStore(), _ => { });
        Assert.Contains("not found", await svc.TriggerAsync("nope"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Trigger_LaunchFailure_ReturnsErrorNotThrow()
    {
        var svc = new StreamDeckService(new FakeStore(), _ => throw new InvalidOperationException("boom"));
        var result = await svc.TriggerAsync("hype");
        Assert.StartsWith("❌", result);
        Assert.Contains("boom", result);
    }
}
