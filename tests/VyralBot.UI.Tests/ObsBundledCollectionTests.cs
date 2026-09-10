using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Hosting;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Decide/Import state machine of the bundled Vyral starter collection. Pure +
/// temp-dir based: the bundled file and the OBS scenes directory are temp
/// paths; the config-stored hash lives in a temp SQLite database.
/// </summary>
public class ObsBundledCollectionTests : IDisposable
{
    private const string BundledJson = "{\"name\":\"Vyral\",\"scene_order\":[],\"scenes\":[],\"sources\":[]}";

    private readonly string _scenesDir;
    private readonly string _bundledPath;
    private readonly VyralBot.Data.IDataService _data;

    public ObsBundledCollectionTests()
    {
        _scenesDir = Path.Combine(Path.GetTempPath(), $"vyral-scenes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scenesDir);
        _bundledPath = Path.Combine(Path.GetTempPath(), $"vyral-bundled-{Guid.NewGuid():N}.json");
        File.WriteAllText(_bundledPath, BundledJson, Encoding.UTF8);
        _data = CreateDataService();
    }

    private static VyralBot.Data.IDataService CreateDataService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-test-{Guid.NewGuid():N}.db");
        var data = new VyralBot.Data.DapperDataService(
            $"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
        data.EnsureDatabaseCreatedAsync().GetAwaiter().GetResult();
        return data;
    }

    private ObsBundledCollectionService CreateService() =>
        new(new ObsInstallLocator(), _data, bundledPathOverride: _bundledPath);

    private string TargetPath => Path.Combine(_scenesDir, "Vyral.json");

    [Fact]
    public async Task FirstRun_NoStoredHash_NoTargetFile_ImportsAsFirstImport()
    {
        var svc = CreateService();

        var state = await svc.DecideAsync(_scenesDir);

        Assert.True(state.ShouldImport);
        Assert.Contains("first import", state.Reason);
        Assert.False(state.ImportedFileMissingFromObs);
    }

    [Fact]
    public async Task Import_MarksVersionImported_SoSecondDecideIsNoOp()
    {
        var svc = CreateService();

        // Import into a scenes directory that does not exist yet — the service
        // creates it (OBS machines always have one; tests must not care).
        string nestedDir = Path.Combine(_scenesDir, "nested");
        await svc.ImportToObsAsync(nestedDir);
        string target = Path.Combine(nestedDir, "Vyral.json");

        Assert.True(File.Exists(target));
        Assert.Equal(await svc.GetStoredHashAsync(), svc.ComputeHash(target));

        var state = await svc.DecideAsync(nestedDir);
        Assert.False(state.ShouldImport);
        Assert.Contains("already imported", state.Reason);
    }

    [Fact]
    public async Task BundledFileChanged_AfterImport_ImportsAsUpdate()
    {
        var svc = CreateService();
        await svc.ImportToObsAsync(_scenesDir);

        // Any byte change is a new bundled version.
        File.WriteAllText(_bundledPath, BundledJson + " ", Encoding.UTF8);

        var state = await svc.DecideAsync(_scenesDir);
        Assert.True(state.ShouldImport);
        Assert.Contains("updated", state.Reason);
    }

    [Fact]
    public async Task ImportedFileDeletedFromObs_DecideFlagsReimport()
    {
        var svc = CreateService();
        await svc.ImportToObsAsync(_scenesDir);
        File.Delete(TargetPath);

        var state = await svc.DecideAsync(_scenesDir);

        Assert.True(state.ShouldImport);
        Assert.True(state.ImportedFileMissingFromObs);
        Assert.Contains("deleted", state.Reason);
    }

    [Fact]
    public async Task NoStoredHash_UserAlreadyHasVyralJson_DoesNotAutoImport()
    {
        var svc = CreateService();
        await File.WriteAllTextAsync(TargetPath, "{\"name\":\"UserOwn\"}", Encoding.UTF8);

        var state = await svc.DecideAsync(_scenesDir);

        Assert.False(state.ShouldImport);
        Assert.Contains("already exists", state.Reason);
    }

    [Fact]
    public async Task ImportToObsAsync_WhenCalledDeliberately_OverwritesExistingVyralJson()
    {
        var svc = CreateService();
        await File.WriteAllTextAsync(TargetPath, "{\"name\":\"UserOwn\"}", Encoding.UTF8);

        await svc.ImportToObsAsync(_scenesDir);

        Assert.Equal(BundledJson, File.ReadAllText(TargetPath));
    }

    [Fact]
    public async Task Import_WritesExactlyOneFile_AndLeavesSiblingsUntouched()
    {
        string sibling = Path.Combine(_scenesDir, "Untitled.json");
        const string siblingContent = "{\"name\":\"Untitled\"}";
        await File.WriteAllTextAsync(sibling, siblingContent, Encoding.UTF8);

        var svc = CreateService();
        await svc.ImportToObsAsync(_scenesDir);

        Assert.Equal(
            new[] { "Untitled.json", "Vyral.json" },
            Directory.GetFiles(_scenesDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(siblingContent, File.ReadAllText(sibling));
        Assert.Equal(BundledJson, File.ReadAllText(TargetPath));
    }

    [Fact]
    public async Task MissingBundledFile_DecideSaysNoImport()
    {
        File.Delete(_bundledPath);
        var svc = CreateService();

        var state = await svc.DecideAsync(_scenesDir);

        Assert.False(state.ShouldImport);
        Assert.Null(svc.GetBundledPath());
    }

    [Fact]
    public void ComputeHash_IsLowercaseHexSha256()
    {
        var svc = CreateService();

        string hash = svc.ComputeHash(_bundledPath);

        using var sha = SHA256.Create();
        string expected = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(_bundledPath))).ToLowerInvariant();
        Assert.Equal(expected, hash);
        Assert.Equal(64, hash.Length);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scenesDir, recursive: true); } catch { /* best-effort */ }
        try { File.Delete(_bundledPath); } catch { /* best-effort */ }
    }
}
