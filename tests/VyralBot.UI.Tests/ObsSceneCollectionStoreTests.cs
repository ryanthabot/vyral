using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Import/export round trips for ObsSceneCollectionStore against a temp SQLite db
/// (real DapperDataService) and the bundled OBS fixture. Verbatim bytes are the
/// contract: import stores the file bytes untouched, export writes them back identical.
/// </summary>
public class ObsSceneCollectionStoreTests
{
    private static VyralBot.Data.IDataService CreateService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vyral-test-{Guid.NewGuid():N}.db");
        return new VyralBot.Data.DapperDataService($"Data Source={path}", NullLogger<VyralBot.Data.DapperDataService>.Instance);
    }

    private static string CopyFixtureToTempFile()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "obs-scene-collection.sample.json");
        var tempPath = Path.Combine(Path.GetTempPath(), $"vyral-obs-{Guid.NewGuid():N}.json");
        File.Copy(fixturePath, tempPath);
        return tempPath;
    }

    private static string TempJsonPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"vyral-obs-{prefix}-{Guid.NewGuid():N}.json");
    }

    private static void DeleteBestEffort(params string[] paths)
    {
        foreach (var path in paths)
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task Import_StoresBytesExactly_AndCounts()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var store = new ObsSceneCollectionStore(svc);
        var tempPath = CopyFixtureToTempFile();
        try
        {
            var originalBytes = await File.ReadAllBytesAsync(tempPath);

            var result = await store.ImportFromFileAsync(tempPath);

            Assert.True(result.Success);
            Assert.Contains("2 scene", result.Message);
            Assert.Contains("5 source", result.Message);
            Assert.NotNull(result.Collection);
            Assert.Equal(originalBytes, result.Collection!.RawJsonBytes);
            Assert.Equal(2, result.Collection.SceneCount);
            Assert.Equal(5, result.Collection.SourceCount);
            Assert.NotNull(result.Document);
            Assert.Equal(2, result.Document!.Scenes.Count);
        }
        finally
        {
            DeleteBestEffort(tempPath);
        }
    }

    [Fact]
    public async Task Export_RoundTripsBytesIdentically()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var store = new ObsSceneCollectionStore(svc);
        var importPath = CopyFixtureToTempFile();
        var exportPath = TempJsonPath("export");
        try
        {
            var import = await store.ImportFromFileAsync(importPath);
            Assert.True(import.Success);

            var (success, _) = await store.ExportToFileAsync(import.Collection!.Id, exportPath);

            Assert.True(success);
            Assert.Equal(await File.ReadAllBytesAsync(importPath), await File.ReadAllBytesAsync(exportPath));
        }
        finally
        {
            DeleteBestEffort(importPath, exportPath);
        }
    }

    [Fact]
    public async Task Import_NonCollectionJson_FailsFriendly()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var store = new ObsSceneCollectionStore(svc);
        var tempPath = TempJsonPath("bad");
        await File.WriteAllBytesAsync(tempPath, Encoding.UTF8.GetBytes("{\"name\":\"nope\"}"));
        try
        {
            var result = await store.ImportFromFileAsync(tempPath);

            Assert.False(result.Success);
            Assert.Contains("sources", result.Message);
            Assert.Null(result.Collection);
            Assert.Null(result.Document);
        }
        finally
        {
            DeleteBestEffort(tempPath);
        }
    }

    [Fact]
    public async Task Import_MissingFile_FailsFriendly()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var store = new ObsSceneCollectionStore(svc);

        var result = await store.ImportFromFileAsync(TempJsonPath("missing"));

        Assert.False(result.Success);
        Assert.Contains("File not found", result.Message);
        Assert.Null(result.Collection);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task LoadDocument_ReturnsProjection()
    {
        var svc = CreateService();
        await svc.EnsureDatabaseCreatedAsync();
        var store = new ObsSceneCollectionStore(svc);
        var tempPath = CopyFixtureToTempFile();
        try
        {
            var import = await store.ImportFromFileAsync(tempPath);
            Assert.True(import.Success);

            var (document, error) = await store.LoadDocumentAsync(import.Collection!.Id);

            Assert.Null(error);
            Assert.NotNull(document);
            Assert.Equal("Starting Soon", document!.CurrentScene);
        }
        finally
        {
            DeleteBestEffort(tempPath);
        }
    }
}
