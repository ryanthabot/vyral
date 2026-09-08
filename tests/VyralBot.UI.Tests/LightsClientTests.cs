using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using VyralBot.Integrations.Lights;
using Xunit;

namespace VyralBot.UI.Tests;

public sealed class RecordingHandler : HttpMessageHandler
{
    public List<(string Url, string? Method, string? Body)> Requests { get; } = new();
    public List<string?> AuthorizationHeaders { get; } = new();
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content == null ? null : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        Requests.Add((request.RequestUri!.ToString(), request.Method.ToString(), body));
        AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
        return Task.FromResult(_respond(request));
    }
    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("[]") };
}

public class LightsClientTests
{
    private static Task<(string? BridgeIp, string? AppKey, IReadOnlyList<string> LightIds)> HueConfig(
        string? ip = "192.168.1.2", string? key = "appkey", IReadOnlyList<string>? ids = null) =>
        Task.FromResult((ip, key, (IReadOnlyList<string>)(ids ?? [])));

    [Fact]
    public async Task Hue_GetLights_ParsesBridgeResponse()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Json(
            """{"1":{"name":"Desk","state":{"on":true,"reachable":true},"productname":"White"},"2":{"name":"Ambient","state":{"on":false,"reachable":false}}}"""));
        var client = new HueLightsClient(() => HueConfig(), handler);

        var lights = await client.GetLightsAsync();

        Assert.Equal(2, lights.Count);
        var desk = lights.Single(l => l.Id == "1");
        Assert.Equal("Desk", desk.Name);
        Assert.True(desk.On);
        Assert.True(desk.Reachable);
        Assert.Equal("White", desk.ProductName);
        var ambient = lights.Single(l => l.Id == "2");
        Assert.Equal("Ambient", ambient.Name);
        Assert.False(ambient.On);
        Assert.False(ambient.Reachable);
        Assert.Null(ambient.ProductName);
    }

    [Fact]
    public async Task Hue_Flash_SendsAlertPerLightPerCycle_ThenRestores()
    {
        var handler = new RecordingHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/lights", StringComparison.Ordinal))
                return RecordingHandler.Json("""{"1":{"name":"Desk","state":{"on":true,"reachable":true}}}""");
            if (req.Method == HttpMethod.Get)
                return RecordingHandler.Json("""{"state":{"on":true,"xy":[0.3,0.3],"bri":200}}""");
            return RecordingHandler.Ok();
        });
        var client = new HueLightsClient(() => HueConfig(ids: ["1", "2"]), handler);

        var result = await client.FlashAsync("#FF00AA", times: 3, intervalMs: 50);

        var puts = handler.Requests.Where(r => r.Method == "PUT").ToList();
        Assert.Equal(3 * 2 + 2, puts.Count);
        Assert.All(handler.Requests, r => Assert.StartsWith("http://192.168.1.2/api/appkey/", r.Url));
        Assert.All(puts, r => Assert.Contains("/state", r.Url));
        Assert.All(puts.Take(6), r => Assert.Contains("\"alert\":\"select\"", r.Body));
        // Last PUT per light restores the original look.
        Assert.EndsWith("/lights/1/state", puts[^2].Url);
        Assert.EndsWith("/lights/2/state", puts[^1].Url);
        Assert.Contains("\"alert\":\"none\"", puts[^2].Body);
        Assert.Contains("\"alert\":\"none\"", puts[^1].Body);
        Assert.Contains("2 light", result, StringComparison.Ordinal);
        Assert.Contains("3x", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#FF0000", 0.70, 0.30)]
    [InlineData("#00FF00", 0.17, 0.75)]
    public void Hue_RgbToXy_KnownColors(string hex, double x, double y)
    {
        var xy = HueLightsClient.RgbToXy(hex);
        Assert.Equal(x, xy[0], 2);
        Assert.Equal(y, xy[1], 2);
    }

    [Fact]
    public async Task Hue_Flash_Unconfigured_ReturnsFriendlyMessage()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Ok());
        var client = new HueLightsClient(() => HueConfig(ip: null, key: null), handler);

        var result = await client.FlashAsync("#FF0000", times: 2, intervalMs: 50);

        Assert.Contains("not configured", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Hue_Flash_InvalidColor_ReturnsFriendlyMessage()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Ok());
        var client = new HueLightsClient(() => HueConfig(ids: ["1"]), handler);

        var result = await client.FlashAsync("#ZZZ", times: 2, intervalMs: 50);

        Assert.Contains("❌", result, StringComparison.Ordinal);
        Assert.Contains("color", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Webhook_Flash_PostsJsonPayloadAndBearer()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Ok());
        var client = new WebhookLightsClient(
            () => Task.FromResult(("http://example.com/lights", (string?)"tok123")), handler);

        var result = await client.FlashAsync("#00FFAA", times: 3, intervalMs: 150);

        Assert.Contains("flash triggered", result, StringComparison.OrdinalIgnoreCase);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("http://example.com/lights", req.Url);
        Assert.Equal("POST", req.Method);
        Assert.Contains("\"action\":\"flash\"", req.Body, StringComparison.Ordinal);
        Assert.Contains("\"color\":\"#00FFAA\"", req.Body, StringComparison.Ordinal);
        Assert.Equal("Bearer tok123", handler.AuthorizationHeaders.Single());
    }

    [Fact]
    public async Task LightsManager_NoneProvider_ReturnsWarning()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Ok());
        var manager = new LightsManager(() => Task.FromResult(new LightsConfig("None")), handler);

        var flash = await manager.FlashAsync(null, times: 1, intervalMs: 50);
        var power = await manager.SetPowerAsync(true);

        Assert.Contains("not configured", flash, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not configured", power, StringComparison.OrdinalIgnoreCase);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetLightsAsync());
        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }
}
