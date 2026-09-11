using System.IO;
using System.Text.Json;
using VyralBot.Integrations.Obs.Hosting;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// NON-DESTRUCTIVE union merge of ObsCollectionMerger. The USER's file is the
/// base: every existing scene/source/scene-item keeps the user's version;
/// upstream contributes only NEW entries. Fixtures are minimal inline JSONs
/// that carry exactly the fields the rules touch (name, id, uuid,
/// settings.items, settings.id_counter) plus unknown fields that must survive.
/// </summary>
public class ObsCollectionMergerTests
{
    // The user has a customized "Screen 1" scene (their display-capture device,
    // their item ids), their own extra scene, and a tweaked source. Their doc
    // also carries unknown root fields a real OBS collection would have.
    private const string UserJson = """
        {
          "name": "Vyral",
          "current_scene": "Screen 1",
          "current_program_scene": "Screen 1",
          "scene_order": [{"name": "Screen 1"}, {"name": "My Own Scene"}],
          "resolution": {"x": 1920, "y": 1080},
          "custom_root": "user-value",
          "sources": [
            {"name": "Screen 1", "id": "scene", "uuid": "user-s1-uuid",
             "settings": {"id_counter": 2, "items": [
               {"name": "User Display Capture", "source_uuid": "user-dc-uuid", "id": 1, "visible": true},
               {"name": "User Webcam", "source_uuid": "user-cam-uuid", "id": 2}]}},
            {"name": "Display Capture", "id": "dshow_input", "uuid": "user-dc-uuid",
             "settings": {"device": "User's Monitor"}},
            {"name": "My Own Scene", "id": "scene", "uuid": "user-own-uuid",
             "settings": {"id_counter": 0, "items": []}}
          ]
        }
        """;

    // Upstream renames the device inside its "Screen 1" variant, adds a new
    // item to "Screen 1", adds a whole new scene "Screen 4" plus a plain new
    // source, and reorders scene_order. Its unknown root field must NOT leak.
    private const string UpstreamJson = """
        {
          "name": "Vyral",
          "current_scene": "Screen 4",
          "current_program_scene": "Screen 4",
          "scene_order": [{"name": "Screen 4"}, {"name": "Screen 1"}],
          "resolution": {"x": 2560, "y": 1440},
          "upstream_only_root": "upstream-value",
          "sources": [
            {"name": "Screen 4", "id": "scene", "uuid": "up-s4-uuid",
             "settings": {"id_counter": 1, "items": [
               {"name": "Display Capture 4", "source_uuid": "up-dc4-uuid", "id": 1}]}},
            {"name": "Screen 1", "id": "scene", "uuid": "up-s1-uuid",
             "settings": {"id_counter": 9, "items": [
               {"name": "User Display Capture", "source_uuid": "up-dc-uuid", "id": 1},
               {"name": "Upstream Alert", "source_uuid": "up-dc-uuid", "id": 2}]}},
            {"name": "Display Capture", "id": "dshow_input", "uuid": "up-dc-uuid",
             "settings": {"device": "Upstream Monitor"}},
            {"name": "New Browser Source", "id": "browser_source", "uuid": "up-browser-uuid",
             "settings": {"url": "https://example.com"}}
          ]
        }
        """;

    private static JsonElement Source(JsonDocument doc, string name)
    {
        foreach (JsonElement source in doc.RootElement.GetProperty("sources").EnumerateArray())
        {
            if (source.GetProperty("name").GetString() == name)
            {
                return source;
            }
        }

        throw new KeyNotFoundException($"No source '{name}' in the merged document.");
    }

    private static List<string> ItemNames(JsonDocument doc, string sceneName)
    {
        JsonElement items = Source(doc, sceneName).GetProperty("settings").GetProperty("items");
        return items.EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();
    }

    private static List<string> SceneOrderNames(JsonDocument doc) =>
        doc.RootElement.GetProperty("scene_order").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToList();

    [Fact]
    public void ExistingScene_KeepsUserVersion_UpstreamVariantDiscarded()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.MergedJson);
        JsonElement scene = Source(doc, "Screen 1");

        // The USER's scene object survives whole: their uuid, their items with
        // their source_uuid links — upstream's "Screen 1" variant is discarded.
        Assert.Equal("user-s1-uuid", scene.GetProperty("uuid").GetString());
        Assert.Equal("user-dc-uuid", scene.GetProperty("settings").GetProperty("items")[0]
            .GetProperty("source_uuid").GetString());
        Assert.DoesNotContain("up-s1-uuid", result.MergedJson);
    }

    [Fact]
    public void ExistingSource_KeepsUserSettings()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        using var doc = JsonDocument.Parse(result.MergedJson);
        JsonElement source = Source(doc, "Display Capture");

        Assert.Equal("user-dc-uuid", source.GetProperty("uuid").GetString());
        Assert.Equal("User's Monitor", source.GetProperty("settings").GetProperty("device").GetString());
        Assert.DoesNotContain("Upstream Monitor", result.MergedJson);
    }

    [Fact]
    public void NewUpstreamSceneAndSource_AreAdded()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        Assert.True(result.Success);
        Assert.Equal(1, result.AddedScenes);
        Assert.Equal(1, result.AddedSources);
        using var doc = JsonDocument.Parse(result.MergedJson);

        JsonElement scene4 = Source(doc, "Screen 4");
        Assert.Equal("up-s4-uuid", scene4.GetProperty("uuid").GetString());
        Assert.Equal(["Display Capture 4"], ItemNames(doc, "Screen 4"));
        Assert.Equal("https://example.com", Source(doc, "New Browser Source")
            .GetProperty("settings").GetProperty("url").GetString());
    }

    [Fact]
    public void OverlapCount_KeptUserSources_CountsUpstreamEntriesTheUserAlreadyHas()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        // Upstream has 4 entries; user has "Screen 1" and "Display Capture".
        Assert.Equal(2, result.KeptUserSources);
    }

    [Fact]
    public void NewSceneItemInExistingScene_IsAppended_AndIdCounterBumped()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        Assert.Equal(1, result.AddedSceneItems);
        using var doc = JsonDocument.Parse(result.MergedJson);

        // User items first and untouched, the upstream-only item appended after.
        Assert.Equal(
            ["User Display Capture", "User Webcam", "Upstream Alert"],
            ItemNames(doc, "Screen 1"));

        // id_counter: max(existing id_counter 2, max item id 2) + 1 appended = 3.
        Assert.Equal(3, Source(doc, "Screen 1").GetProperty("settings")
            .GetProperty("id_counter").GetInt32());
    }

    [Fact]
    public void SceneOrder_UpstreamNamesFirst_ThenUserOnlyNamesInRelativeOrder()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        using var doc = JsonDocument.Parse(result.MergedJson);
        Assert.Equal(
            ["Screen 4", "Screen 1", "My Own Scene"],
            SceneOrderNames(doc));
    }

    [Fact]
    public void UserOnlySceneAndSource_Survive()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        using var doc = JsonDocument.Parse(result.MergedJson);
        Assert.Equal("user-own-uuid", Source(doc, "My Own Scene").GetProperty("uuid").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public void CurrentScene_KeptFromUser_WhenStillValid()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        using var doc = JsonDocument.Parse(result.MergedJson);
        Assert.Equal("Screen 1", doc.RootElement.GetProperty("current_scene").GetString());
        Assert.Equal("Screen 1", doc.RootElement.GetProperty("current_program_scene").GetString());
    }

    [Fact]
    public void CurrentScene_FallsBackToUpstream_WhenUserValueDangling()
    {
        const string user = """{"current_scene": "Ghost Scene", "current_program_scene": "Ghost Scene", "scene_order": [], "sources": [{"name": "Real Scene", "id": "scene", "uuid": "u1", "settings": {"items": []}}]}""";

        ObsMergeResult result = ObsCollectionMerger.Merge(user, UpstreamJson);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.MergedJson);
        Assert.Equal("Screen 4", doc.RootElement.GetProperty("current_scene").GetString());
        Assert.Equal("Screen 4", doc.RootElement.GetProperty("current_program_scene").GetString());
    }

    [Fact]
    public void MalformedUpstream_FailsCleanly_WithoutMergedOutput()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, "{ this is not json");

        Assert.False(result.Success);
        Assert.Contains("JSON", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.MergedJson);
        Assert.Equal(0, result.AddedScenes);
        Assert.Equal(0, result.AddedSources);
        Assert.Equal(0, result.AddedSceneItems);
    }

    [Fact]
    public void MalformedUser_FailsCleanly()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge("[1, 2", UpstreamJson);

        Assert.False(result.Success);
        Assert.Empty(result.MergedJson);
    }

    [Fact]
    public void RootFields_UserVersionWins_UnknownUserFieldsSurvive_UpstreamExtrasDoNotLeak()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UpstreamJson);

        using var doc = JsonDocument.Parse(result.MergedJson);
        Assert.Equal(1920, doc.RootElement.GetProperty("resolution").GetProperty("x").GetInt32());
        Assert.Equal("user-value", doc.RootElement.GetProperty("custom_root").GetString());
        Assert.False(doc.RootElement.TryGetProperty("upstream_only_root", out _));
    }

    [Fact]
    public void AlreadyUpToDate_AllCountsZero_AndStillSucceeds()
    {
        ObsMergeResult result = ObsCollectionMerger.Merge(UserJson, UserJson);

        Assert.True(result.Success);
        Assert.Equal(0, result.AddedScenes);
        Assert.Equal(0, result.AddedSources);
        Assert.Equal(0, result.AddedSceneItems);
        Assert.Equal(3, result.KeptUserSources);
        Assert.Contains("up to date", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingArrays_Tolerated_UpstreamWithoutSourcesIsNoOp()
    {
        const string user = """{"scene_order": [], "sources": [{"name": "A", "id": "scene", "uuid": "u1", "settings": {"items": []}}]}""";

        ObsMergeResult result = ObsCollectionMerger.Merge(user, """{"name": "Vyral"}""");

        Assert.True(result.Success);
        Assert.Equal(0, result.AddedScenes);
        Assert.Equal(0, result.AddedSources);
        Assert.Equal("u1", Source(JsonDocument.Parse(result.MergedJson), "A").GetProperty("uuid").GetString());
    }

    // ----- groups -----

    [Fact]
    public void RealDocumentFixture_RoundTripsThroughMerge_WithUnknownFieldsIntact()
    {
        // A real-world-shaped OBS collection (audio devices, modules,
        // transitions at the root): merged with a mutated copy of itself it
        // must come back valid, with every root field intact and exactly one
        // new entry from the mutation.
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "obs-scene-collection.sample.json");
        string real = File.ReadAllText(fixturePath);
        const string mutated = """{"name": "Mutated", "scene_order": [{"name": "Brand New Scene"}], "sources": [{"name": "Brand New Scene", "id": "scene", "uuid": "new-scene-uuid", "settings": {"items": []}}]}""";

        ObsMergeResult result = ObsCollectionMerger.Merge(real, mutated);

        Assert.True(result.Success);
        Assert.Equal(1, result.AddedScenes);
        using var doc = JsonDocument.Parse(result.MergedJson);
        // The user's root survives verbatim, including fields the merger never touches.
        Assert.True(doc.RootElement.TryGetProperty("DesktopAudioDevice1", out _));
        Assert.True(doc.RootElement.TryGetProperty("modules", out _));
        Assert.Equal(
            doc.RootElement.GetProperty("current_transition").GetString(),
            JsonDocument.Parse(real).RootElement.GetProperty("current_transition").GetString());
        // The upstream scene slots into its intended position, user scenes follow.
        Assert.Equal(3, SceneOrderNames(doc).Count);
        Assert.Equal("Brand New Scene", SceneOrderNames(doc)[0]);
    }

    [Fact]
    public void GroupItem_SourceUuidRewrittenToUserSource_WhenUserHasThatSource()
    {
        // The group itself is upstream-only (appended), but the "Camera" it
        // references exists in the USER's doc with a different uuid — the
        // appended group item must point at the user's camera ("connected
        // correctly"), and at the upstream uuid for upstream-only sources.
        const string user = """{"scene_order": [], "sources": [{"name": "Camera", "id": "dshow_input", "uuid": "user-cam-uuid", "settings": {}}]}""";
        const string upstream = """{"scene_order": [], "sources": [{"name": "New Scene", "id": "scene", "uuid": "up-ns-uuid", "settings": {"items": []}}], "groups": [{"name": "Main", "id": "group", "uuid": "up-g-uuid", "settings": {"id_counter": 2, "items": [{"name": "Camera", "source_uuid": "up-cam-uuid", "id": 1}, {"name": "New Scene", "source_uuid": "up-ns-uuid", "id": 2}]}}]}""";

        ObsMergeResult result = ObsCollectionMerger.Merge(user, upstream);

        Assert.True(result.Success);
        Assert.Equal(1, result.AddedSources); // the group counts as a new source
        using var doc = JsonDocument.Parse(result.MergedJson);
        JsonElement items = doc.RootElement.GetProperty("groups")[0].GetProperty("settings").GetProperty("items");

        Assert.Equal("user-cam-uuid", items[0].GetProperty("source_uuid").GetString());
        Assert.Equal("up-ns-uuid", items[1].GetProperty("source_uuid").GetString());
    }

    [Fact]
    public void ExistingGroup_KeepsUserVersionEntirely()
    {
        const string user = """{"scene_order": [], "sources": [], "groups": [{"name": "Main", "id": "group", "uuid": "user-g-uuid", "settings": {"items": [{"name": "User Item", "source_uuid": "user-x-uuid", "id": 1}]}}]}""";
        const string upstream = """{"scene_order": [], "sources": [], "groups": [{"name": "Main", "id": "group", "uuid": "up-g-uuid", "settings": {"items": [{"name": "Upstream Item", "source_uuid": "up-x-uuid", "id": 1}]}}]}""";

        ObsMergeResult result = ObsCollectionMerger.Merge(user, upstream);

        Assert.True(result.Success);
        Assert.Equal(1, result.KeptUserSources);
        using var doc = JsonDocument.Parse(result.MergedJson);
        JsonElement group = doc.RootElement.GetProperty("groups")[0];
        Assert.Equal("user-g-uuid", group.GetProperty("uuid").GetString());
        Assert.Equal(["User Item"], group.GetProperty("settings").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ToList());
        Assert.DoesNotContain("Upstream Item", result.MergedJson);
    }
}
