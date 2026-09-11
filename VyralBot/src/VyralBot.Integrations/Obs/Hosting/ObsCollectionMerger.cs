using System.Text.Json;
using System.Text.Json.Nodes;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>Outcome of <see cref="ObsCollectionMerger.Merge"/>. <see cref="MergedJson"/>
/// is empty whenever <see cref="Success"/> is false — callers must never write it.</summary>
public sealed record ObsMergeResult(
    bool Success,
    string Message,
    string MergedJson,
    int AddedScenes,
    int AddedSources,
    int AddedSceneItems,
    int KeptUserSources);

/// <summary>
/// NON-DESTRUCTIVE union merge of two OBS scene-collection documents. The
/// USER's file is the base: every existing scene/source/scene-item keeps the
/// USER's version (device connections, positions, settings, uuids). Upstream
/// contributes only NEW entries. Nothing is ever removed. Works on
/// <see cref="JsonNode"/> so unknown fields survive verbatim; matching is by
/// exact name (OBS source names are case-sensitive).
/// </summary>
public static class ObsCollectionMerger
{
    public static ObsMergeResult Merge(string userJson, string upstreamJson)
    {
        JsonObject? userRoot;
        JsonObject? upstreamRoot;
        try
        {
            userRoot = JsonNode.Parse(userJson) as JsonObject;
            upstreamRoot = JsonNode.Parse(upstreamJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            return new ObsMergeResult(
                false, $"Merge failed — the collection is not valid JSON: {ex.Message}", "", 0, 0, 0, 0);
        }

        if (userRoot is null)
        {
            return new ObsMergeResult(
                false, "Merge failed — the current collection is not a JSON object.", "", 0, 0, 0, 0);
        }

        if (upstreamRoot is null)
        {
            return new ObsMergeResult(
                false, "Merge failed — the upstream collection is not a JSON object.", "", 0, 0, 0, 0);
        }

        // The merged document STARTS as the user's document — everything we do
        // not explicitly touch (resolution, modules, transitions, canvases,
        // version, unknown fields) keeps the user's version automatically.
        JsonObject merged = userRoot.DeepClone().AsObject();
        JsonArray mergedSources = EnsureArray(merged, "sources");

        // name → uuid over the USER's entries (sources + groups); the lookup
        // set drives the union, the uuid drives the "connected correctly"
        // rewrite of appended scene/group items.
        var userUuidByName = new Dictionary<string, string?>(StringComparer.Ordinal);
        CollectUuids(userRoot["sources"], userUuidByName);
        CollectUuids(userRoot["groups"], userUuidByName);

        int addedScenes = 0;
        int addedSources = 0;
        int keptUserSources = 0;
        var upstreamNames = new HashSet<string>(StringComparer.Ordinal);
        var appendedNames = new HashSet<string>(StringComparer.Ordinal);
        var countedNames = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1 — union of both arrays by name: the user's entries already
        // live in `merged`; only upstream-only names are appended (verbatim
        // except the source_uuid rewrite of their scene items).
        UnionArray(upstreamRoot["sources"], mergedSources);
        UnionArray(upstreamRoot["groups"], EnsureArray(merged, "groups"));

        // Pass 2 — items upstream added to scenes the USER already has: the
        // scene object stays the user's; upstream-only items are appended and
        // id_counter bumped so OBS never reuses an item id.
        int addedItems = MergeSceneItems();

        // Pass 3 — scene_order: upstream's ordering first, user-only scenes
        // appended in their existing relative order.
        merged["scene_order"] = BuildSceneOrder();

        // Pass 4 — current_scene/current_program_scene: the user's value wins
        // while it still points at an existing scene; otherwise upstream's.
        HashSet<string> mergedSceneNames = SceneNames(mergedSources);
        AdoptUpstreamCurrentScene("current_scene");
        AdoptUpstreamCurrentScene("current_program_scene");

        string message = addedScenes == 0 && addedSources == 0 && addedItems == 0
            ? "Already up to date — the local collection has everything from GitHub."
            : $"Updated — added {Describe(addedScenes, "scene", "scenes")}"
                + $", {Describe(addedSources, "source", "sources")}"
                + $", {Describe(addedItems, "scene item", "scene items")}"
                + "; all your existing settings preserved.";

        return new ObsMergeResult(
            true, message, merged.ToJsonString(), addedScenes, addedSources, addedItems, keptUserSources);

        // ----- local helpers (share the state above) -----

        void UnionArray(JsonNode? upstreamArray, JsonArray target)
        {
            foreach (JsonObject node in (upstreamArray as JsonArray ?? []).OfType<JsonObject>())
            {
                string? name = Str(node["name"]);
                if (name is null)
                {
                    continue; // unnamed entry — nothing to union on
                }

                upstreamNames.Add(name);

                if (userUuidByName.ContainsKey(name))
                {
                    if (countedNames.Add(name))
                    {
                        keptUserSources++; // the USER's version wins — counted, never replaced
                    }

                    continue;
                }

                if (!appendedNames.Add(name))
                {
                    continue; // duplicate within upstream — the first one was already appended
                }

                JsonObject clone = node.DeepClone().AsObject();
                RewriteItemUuids(clone, userUuidByName);
                target.Add(clone);
                if (IsScene(clone))
                {
                    addedScenes++;
                }
                else
                {
                    addedSources++;
                }
            }
        }

        int MergeSceneItems()
        {
            int appendedTotal = 0;
            foreach (JsonObject upScene in (upstreamRoot["sources"] as JsonArray ?? []).OfType<JsonObject>().Where(IsScene))
            {
                string? sceneName = Str(upScene["name"]);
                if (sceneName is null || !userUuidByName.ContainsKey(sceneName))
                {
                    continue; // new scene — Pass 1 appended it whole; user-only — nothing to merge
                }

                JsonObject? userScene = mergedSources.OfType<JsonObject>()
                    .FirstOrDefault(s => IsScene(s) && string.Equals(Str(s["name"]), sceneName, StringComparison.Ordinal));
                if (userScene?["settings"] is not JsonObject settings)
                {
                    continue;
                }

                JsonArray? userItems = settings["items"] as JsonArray;
                var existingNames = new HashSet<string>(
                    (userItems ?? []).OfType<JsonObject>()
                        .Select(i => Str(i["name"])).Where(n => n is not null),
                    StringComparer.Ordinal);

                int appendedToScene = 0;
                foreach (JsonObject upItem in ((upScene["settings"] as JsonObject)?["items"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    string? itemName = Str(upItem["name"]);
                    if (itemName is null || existingNames.Contains(itemName))
                    {
                        continue; // the user's item wins — never replaced
                    }

                    JsonObject itemClone = upItem.DeepClone().AsObject();
                    RewriteItemUuids(itemClone, userUuidByName);
                    (userItems ??= []).Add(itemClone);
                    settings["items"] = userItems;
                    existingNames.Add(itemName);
                    appendedToScene++;
                }

                if (appendedToScene > 0)
                {
                    BumpIdCounter(settings, userItems, appendedToScene);
                    appendedTotal += appendedToScene;
                }
            }

            return appendedTotal;
        }

        JsonArray BuildSceneOrder()
        {
            HashSet<string> sceneNames = SceneNames(mergedSources);
            var ordered = new List<string>(sceneNames.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddIfScene(string? name)
            {
                if (name is not null && sceneNames.Contains(name) && seen.Add(name))
                {
                    ordered.Add(name);
                }
            }

            // Upstream's intended order for every name upstream knows…
            foreach (JsonObject entry in (upstreamRoot["scene_order"] as JsonArray ?? []).OfType<JsonObject>())
            {
                AddIfScene(Str(entry["name"]));
            }

            foreach (JsonObject node in mergedSources.OfType<JsonObject>())
            {
                if (IsScene(node) && Str(node["name"]) is { } name && upstreamNames.Contains(name))
                {
                    AddIfScene(name); // upstream scene missing from upstream's scene_order
                }
            }

            // …then user-only scenes in their existing relative order.
            foreach (JsonObject entry in (userRoot["scene_order"] as JsonArray ?? []).OfType<JsonObject>())
            {
                AddIfScene(Str(entry["name"]));
            }

            foreach (JsonObject node in mergedSources.OfType<JsonObject>())
            {
                if (IsScene(node))
                {
                    AddIfScene(Str(node["name"])); // never listed — hand-edited collection
                }
            }

            var array = new JsonArray();
            foreach (string name in ordered)
            {
                array.Add(new JsonObject { ["name"] = name });
            }

            return array;
        }

        void AdoptUpstreamCurrentScene(string key)
        {
            string? userValue = Str(merged[key]);
            if (userValue is not null && mergedSceneNames.Contains(userValue))
            {
                return; // still valid — the user's choice is kept
            }

            if (upstreamRoot[key] is JsonValue value && value.TryGetValue<string>(out string? upstreamValue))
            {
                merged[key] = upstreamValue;
            }
        }
    }

    private static void CollectUuids(JsonNode? array, Dictionary<string, string?> into)
    {
        foreach (JsonObject node in (array as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Str(node["name"]) is { } name)
            {
                into.TryAdd(name, Str(node["uuid"]));
            }
        }
    }

    /// <summary>
    /// An appended scene/group whose items reference a source the USER already
    /// has must point at the USER's uuid — otherwise OBS shows a broken
    /// ("missing") source. Items referencing upstream-only sources keep the
    /// upstream uuid: that source is appended verbatim right next to them.
    /// </summary>
    private static void RewriteItemUuids(JsonObject entry, Dictionary<string, string?> userUuidByName)
    {
        if (entry["settings"] is not JsonObject settings || settings["items"] is not JsonArray items)
        {
            return;
        }

        foreach (JsonObject item in items.OfType<JsonObject>())
        {
            if (Str(item["name"]) is { } name
                && userUuidByName.TryGetValue(name, out string? userUuid)
                && userUuid is not null)
            {
                item["source_uuid"] = userUuid;
            }
        }
    }

    private static void BumpIdCounter(JsonObject settings, JsonArray? items, int appendedCount)
    {
        int max = 0;
        if (settings["id_counter"] is JsonValue counter && counter.TryGetValue<int>(out int current))
        {
            max = current;
        }

        foreach (JsonObject item in (items ?? []).OfType<JsonObject>())
        {
            if (item["id"] is JsonValue id && id.TryGetValue<int>(out int itemId))
            {
                max = Math.Max(max, itemId);
            }
        }

        settings["id_counter"] = max + appendedCount;
    }

    private static HashSet<string> SceneNames(JsonArray sources) =>
        sources.OfType<JsonObject>().Where(IsScene)
            .Select(s => Str(s["name"])).Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

    private static JsonArray EnsureArray(JsonObject root, string key)
    {
        if (root[key] is JsonArray array)
        {
            return array;
        }

        var created = new JsonArray();
        root[key] = created;
        return created;
    }

    private static bool IsScene(JsonObject node) => string.Equals(Str(node["id"]), "scene", StringComparison.Ordinal);

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;

    private static string Describe(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";
}
