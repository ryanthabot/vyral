using System.Text.Json;

namespace VyralBot.Integrations.Obs;

public sealed record ObsSourceSummary(string Name, string Id, string? Uuid, double Volume, bool Muted, int FilterCount);
public sealed record ObsSceneItemSummary(string SourceName, string? SourceUuid, bool Visible, bool Locked, double Rotation, double PosX, double PosY, double ScaleX, double ScaleY, int BoundsType);
public sealed record ObsSceneSummary(string Name, string? Uuid, IReadOnlyList<ObsSceneItemSummary> Items);
public sealed record ObsAudioDeviceSummary(string Name, string? SourceId);

/// <summary>
/// Read-only projection over a verbatim OBS scene-collection JSON document.
/// The raw string is never modified — this type is display data only, so an
/// import → export round trip stays byte-identical.
/// </summary>
public sealed class ObsSceneCollectionDocument
{
    // OBS always names the global audio devices with these top-level keys.
    private static readonly string[] AudioDeviceKeys =
    [
        "DesktopAudioDevice1",
        "AuxAudioDevice1",
        "DesktopAudioDevice2",
        "AuxAudioDevice2",
        "AuxAudioDevice3",
        "AuxAudioDevice4",
    ];

    public required string RawJson { get; init; }
    public string? CurrentScene { get; init; }
    public string? CurrentProgramScene { get; init; }
    public string? CurrentTransition { get; init; }
    public string? Resolution { get; init; }
    public required IReadOnlyList<ObsSceneSummary> Scenes { get; init; } = [];
    public required IReadOnlyList<ObsSourceSummary> NonSceneSources { get; init; } = [];
    public IReadOnlyList<ObsAudioDeviceSummary> AudioDevices { get; init; } = [];
    public required int SourceCount { get; init; }

    public static ObsSceneCollectionDocument Parse(string rawJson)
    {
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The file is not a valid OBS scene collection (malformed JSON).", ex);
        }

        using (json)
        {
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("sources", out JsonElement sources)
                || sources.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The file is JSON but not an OBS scene collection (missing \"sources\" array).");
            }

            List<ObsSceneSummary> scenesInDocOrder = [];
            Dictionary<string, ObsSceneSummary> sceneByName = new(StringComparer.Ordinal);
            List<ObsSourceSummary> nonSceneSources = [];

            foreach (JsonElement source in sources.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string name = Str(source, "name") ?? "";
                string? id = Str(source, "id");
                if (id == "scene")
                {
                    ObsSceneSummary scene = new(name, Str(source, "uuid"), ReadSceneItems(source));
                    scenesInDocOrder.Add(scene);
                    sceneByName[name] = scene;
                }
                else
                {
                    nonSceneSources.Add(new ObsSourceSummary(
                        name,
                        id ?? "",
                        Str(source, "uuid"),
                        Dbl(source, "volume", defaultValue: 1.0),
                        Bool(source, "muted", defaultValue: false),
                        FilterCount(source)));
                }
            }

            return new ObsSceneCollectionDocument
            {
                RawJson = rawJson,
                CurrentScene = Str(root, "current_scene"),
                CurrentProgramScene = Str(root, "current_program_scene"),
                CurrentTransition = Str(root, "current_transition"),
                Resolution = ReadResolution(root),
                Scenes = OrderScenes(root, scenesInDocOrder, sceneByName),
                NonSceneSources = nonSceneSources,
                AudioDevices = ReadAudioDevices(root),
                SourceCount = sources.GetArrayLength(),
            };
        }
    }

    private static List<ObsSceneItemSummary> ReadSceneItems(JsonElement sceneSource)
    {
        List<ObsSceneItemSummary> items = [];
        if (Obj(sceneSource, "settings") is not { } settings
            || Arr(settings, "items") is not { } itemsArray)
        {
            return items;
        }

        foreach (JsonElement item in itemsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            double posX = 0.0, posY = 0.0;
            if (Obj(item, "pos") is { } posValue)
            {
                posX = Dbl(posValue, "x", defaultValue: 0.0);
                posY = Dbl(posValue, "y", defaultValue: 0.0);
            }

            double scaleX = 1.0, scaleY = 1.0;
            if (Obj(item, "scale") is { } scaleValue)
            {
                scaleX = Dbl(scaleValue, "x", defaultValue: 1.0);
                scaleY = Dbl(scaleValue, "y", defaultValue: 1.0);
            }

            items.Add(new ObsSceneItemSummary(
                Str(item, "name") ?? "",
                Str(item, "source_uuid"),
                Bool(item, "visible", defaultValue: true),
                Bool(item, "locked", defaultValue: false),
                Dbl(item, "rot", defaultValue: 0.0),
                posX,
                posY,
                scaleX,
                scaleY,
                (int)Dbl(item, "bounds_type", defaultValue: 0.0)));
        }

        return items;
    }

    private static List<ObsSceneSummary> OrderScenes(
        JsonElement root,
        List<ObsSceneSummary> scenesInDocOrder,
        Dictionary<string, ObsSceneSummary> sceneByName)
    {
        List<ObsSceneSummary> ordered = [];
        HashSet<string> emitted = new(StringComparer.Ordinal);

        if (Arr(root, "scene_order") is { } sceneOrder)
        {
            foreach (JsonElement entry in sceneOrder.EnumerateArray())
            {
                string? name = Str(entry, "name");
                if (name is not null
                    && sceneByName.TryGetValue(name, out ObsSceneSummary? scene)
                    && emitted.Add(name))
                {
                    ordered.Add(scene);
                }
            }
        }

        // Scenes missing from scene_order (e.g. hand-edited collections) keep
        // their document order after the ordered ones.
        foreach (ObsSceneSummary scene in scenesInDocOrder)
        {
            if (emitted.Add(scene.Name))
            {
                ordered.Add(scene);
            }
        }

        return ordered;
    }

    private static List<ObsAudioDeviceSummary> ReadAudioDevices(JsonElement root)
    {
        List<ObsAudioDeviceSummary> devices = [];
        foreach (string key in AudioDeviceKeys)
        {
            if (Obj(root, key) is { } device)
            {
                devices.Add(new ObsAudioDeviceSummary(Str(device, "name") ?? "", Str(device, "id")));
            }
        }

        return devices;
    }

    private static string? ReadResolution(JsonElement root)
    {
        if (Obj(root, "resolution") is not { } resolution)
        {
            return null;
        }

        double x = Dbl(resolution, "x", defaultValue: 0.0);
        double y = Dbl(resolution, "y", defaultValue: 0.0);
        return $"{x:0}x{y:0}";
    }

    private static int FilterCount(JsonElement source)
    {
        return Arr(source, "filters") is { } filters ? filters.GetArrayLength() : 0;
    }

    private static string? Str(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool Bool(JsonElement parent, string propertyName, bool defaultValue)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }

    private static double Dbl(JsonElement parent, string propertyName, double defaultValue)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : defaultValue;
    }

    private static JsonElement? Obj(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    private static JsonElement? Arr(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array
            ? value
            : null;
    }
}
