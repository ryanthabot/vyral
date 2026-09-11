using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Hosting;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// ObsCollectionUpdateService against a temp scenes directory (scenesDirOverride
/// — the ConfigDir is derived as its grandparent, so global.ini detection is
/// tested with a real ini file) and a stubbed HttpClient. Enforces the safety
/// contract: the user's active collection file is only ever touched AFTER a
/// successful merge.
/// </summary>
public class ObsCollectionUpdateServiceTests : IDisposable
{
    private const string UpstreamJson =
        """{"name": "Vyral", "scene_order": [{"name": "New Scene"}], "sources": [{"name": "New Scene", "id": "scene", "uuid": "up-ns", "settings": {"items": []}}]}""";

    private readonly string _configDir;
    private readonly string _scenesDir;
    private readonly string _activePath;
    private readonly VyralBot.Data.IDataService _data;
    private readonly ObsBundledCollectionService _bundled;

    public ObsCollectionUpdateServiceTests()
    {
        // scenesDirOverride is <configDir>\basic\scenes — OBS's real layout.
        _configDir = Path.Combine(Path.GetTempPath(), $"vyral-cfg-{Guid.NewGuid():N}");
        _scenesDir = Path.Combine(_configDir, "basic", "scenes");
        Directory.CreateDirectory(_scenesDir);
        _activePath = Path.Combine(_scenesDir, "Vyral.json");
        _data = CreateDataService();
        _bundled = new ObsBundledCollectionService(
            new ObsInstallLocator(), _data,
            bundledPathOverride: Path.Combine(_configDir, "bundled.json"));
    }

    private static VyralBot.Data.IDataService CreateDataService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-test-{Guid.NewGuid():N}.db");
        var data = new VyralBot.Data.DapperDataService(
            $"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
        data.EnsureDatabaseCreatedAsync().GetAwaiter().GetResult();
        return data;
    }

    private ObsCollectionUpdateService CreateService(HttpResponseMessage response) =>
        new(_bundled, new ObsInstallLocator(), _data,
            http: new HttpClient(new StubHandler(response)),
            scenesDirOverride: _scenesDir);

    private void WriteActiveCollection(string json) =>
        File.WriteAllText(_activePath, json, Encoding.UTF8);

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("VyralBot", request.Headers.UserAgent.ToString());
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ActiveFileMissing_UpstreamWrittenVerbatim_AsFreshSetup()
    {
        ObsCollectionUpdateService svc = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UpstreamJson) });

        ObsUpdateResult result = await svc.UpdateFromGitHubAsync();

        Assert.True(result.Success);
        Assert.Equal(1, result.AddedScenes);
        Assert.Equal(UpstreamJson, File.ReadAllText(_activePath));
        // Fresh install counts as applied — the auto-import must stay quiet.
        Assert.Equal(_bundled.ComputeContentHash(UpstreamJson), await _bundled.GetStoredHashAsync());
    }

    [Fact]
    public async Task Update_MergesIntoActiveFile_AndStoresFetchedHash()
    {
        WriteActiveCollection("""{"name": "Vyral", "scene_order": [], "sources": [{"name": "User Scene", "id": "scene", "uuid": "user-s", "settings": {"items": []}}]}""");
        ObsCollectionUpdateService svc = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UpstreamJson) });

        ObsUpdateResult result = await svc.UpdateFromGitHubAsync();

        Assert.True(result.Success);
        Assert.Equal(1, result.AddedScenes);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_activePath));
        var names = doc.RootElement.GetProperty("sources").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Equal(["User Scene", "New Scene"], names); // user first, upstream appended — same FILE
        Assert.Equal(_bundled.ComputeContentHash(UpstreamJson), await _bundled.GetStoredHashAsync());
    }

    [Fact]
    public async Task MalformedUpstream_ActiveFileNeverTouched_AndHashNotChanged()
    {
        const string original = """{"name": "Vyral", "scene_order": [], "sources": []}""";
        WriteActiveCollection(original);
        ObsCollectionUpdateService svc = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ broken") });

        ObsUpdateResult result = await svc.UpdateFromGitHubAsync();

        Assert.False(result.Success);
        Assert.Contains("JSON", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(_activePath)); // byte-identical — never touched
        Assert.Null(await _bundled.GetStoredHashAsync());
    }

    [Fact]
    public async Task ActiveCollectionName_ReadFromGlobalIniSceneCollectionFile()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_configDir, "global.ini"),
            "[Basic]\nSceneCollection=My Show\nSceneCollectionFile=MyShow\n",
            Encoding.UTF8);
        ObsCollectionUpdateService svc = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UpstreamJson) });

        await svc.UpdateFromGitHubAsync();

        // The ACTIVE collection file gets the update — not Vyral.json.
        Assert.True(File.Exists(Path.Combine(_scenesDir, "MyShow.json")));
        Assert.False(File.Exists(_activePath));
    }

    [Fact]
    public async Task ActiveCollectionName_FallsBackToVyral_WhenIniHasNoBasicSection()
    {
        await File.WriteAllTextAsync(Path.Combine(_configDir, "global.ini"), "[General]\nMaxLogs=10\n", Encoding.UTF8);
        ObsCollectionUpdateService svc = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UpstreamJson) });

        await svc.UpdateFromGitHubAsync();

        Assert.True(File.Exists(_activePath));
    }

    [Fact]
    public async Task UpstreamHttpError_FailsWithMessage()
    {
        ObsCollectionUpdateService svc = CreateService(
            new HttpResponseMessage(HttpStatusCode.NotFound));

        ObsUpdateResult result = await svc.UpdateFromGitHubAsync();

        Assert.False(result.Success);
        Assert.False(File.Exists(_activePath));
    }

    [Fact]
    public void IsObsRunning_NeverThrows()
    {
        // The VALUE is environment-dependent (true whenever obs64.exe runs),
        // so the contract under test is only "never throws".
        _ = ObsCollectionUpdateService.IsObsRunning();
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { /* best-effort */ }
    }
}
