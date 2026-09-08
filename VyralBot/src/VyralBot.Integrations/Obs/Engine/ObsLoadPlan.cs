using System.IO;
using System.Text.Json;

namespace VyralBot.Integrations.Obs.Engine;

public sealed record ObsFilterPlan(string Id, string Name, string SettingsJson, bool Enabled);

/// <summary>
/// One <c>sources[]</c> entry (scene or regular — creation is identical, the
/// scene id resolves items during <c>obs_source_load</c>). The raw settings /
/// hotkeys JSON is carried verbatim so native creation needs no reparsing on
/// the managed side.
/// </summary>
public sealed record ObsSourcePlan(
    string Id,
    string Name,
    string SettingsJson,
    string? HotkeysJson,
    float? Volume,
    bool? Muted,
    uint? Mixers,
    int? MonitoringType,
    uint? Flags,
    string? DeinterlaceMode,
    string? DeinterlaceFieldOrder,
    IReadOnlyList<ObsFilterPlan> Filters);

/// <summary>ChannelKey: "Desktop1", "Desktop2", "Aux1".."Aux4".</summary>
public sealed record ObsAudioDevicePlan(string ChannelKey, ObsSourcePlan Source);

public sealed record ObsLoadPlan(
    IReadOnlyList<ObsSourcePlan> Sources,
    IReadOnlyList<ObsAudioDevicePlan> AudioDevices,
    string? CurrentSceneName,
    IReadOnlyList<string> SceneOrder);

/// <summary>
/// Pure projection of a scene-collection JSON onto the native creation plan.
/// Works on the raw document (unlike <see cref="Obs.ObsSceneCollectionDocument"/>,
/// which is display-only) because source creation needs the verbatim
/// settings/hotkeys/filters objects.
/// </summary>
public static class ObsLoadPlanBuilder
{
    // OBS always names the global audio devices with these top-level keys
    // (same set and order as ObsSceneCollectionDocument).
    private static readonly (string RootKey, string ChannelKey)[] AudioDeviceKeys =
    [
        ("DesktopAudioDevice1", "Desktop1"),
        ("AuxAudioDevice1", "Aux1"),
        ("DesktopAudioDevice2", "Desktop2"),
        ("AuxAudioDevice2", "Aux2"),
        ("AuxAudioDevice3", "Aux3"),
        ("AuxAudioDevice4", "Aux4"),
    ];

    public static ObsLoadPlan Build(string collectionJson)
    {
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(collectionJson);
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

            List<ObsSourcePlan> sourcePlans = [];
            foreach (JsonElement source in sources.EnumerateArray())
            {
                if (source.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                sourcePlans.Add(ReadSourcePlan(source));
            }

            List<ObsAudioDevicePlan> audioDevices = [];
            foreach ((string rootKey, string channelKey) in AudioDeviceKeys)
            {
                if (root.TryGetProperty(rootKey, out JsonElement device)
                    && device.ValueKind == JsonValueKind.Object)
                {
                    audioDevices.Add(new ObsAudioDevicePlan(channelKey, ReadSourcePlan(device)));
                }
            }

            List<string> sceneOrder = [];
            if (root.TryGetProperty("scene_order", out JsonElement order)
                && order.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in order.EnumerateArray())
                {
                    if (Str(entry, "name") is { } name)
                    {
                        sceneOrder.Add(name);
                    }
                }
            }

            return new ObsLoadPlan(
                sourcePlans,
                audioDevices,
                Str(root, "current_scene"),
                sceneOrder);
        }
    }

    private static ObsSourcePlan ReadSourcePlan(JsonElement source)
    {
        List<ObsFilterPlan> filters = [];
        if (source.TryGetProperty("filters", out JsonElement filtersElement)
            && filtersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement filter in filtersElement.EnumerateArray())
            {
                if (filter.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                filters.Add(new ObsFilterPlan(
                    Str(filter, "id") ?? "",
                    Str(filter, "name") ?? "",
                    RawJson(filter, "settings") ?? "{}",
                    Bool(filter, "enabled", defaultValue: true)));
            }
        }

        return new ObsSourcePlan(
            Str(source, "id") ?? "",
            Str(source, "name") ?? "",
            RawJson(source, "settings") ?? "{}",
            RawJson(source, "hotkeys"),
            NullableNumber(source, "volume", static v => (float)v),
            NullableBool(source, "muted"),
            NullableNumber(source, "mixers", static v => (uint)v),
            NullableNumber(source, "monitoring_type", static v => (int)v),
            NullableNumber(source, "flags", static v => (uint)v),
            Str(source, "deinterlace_mode"),
            Str(source, "deinterlace_field_order"),
            filters);
    }

    // JsonElement serialization writes the raw document slice, so the stored
    // number/wording of the original file survives the round trip untouched.
    private static string? RawJson(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            ? JsonSerializer.Serialize(value)
            : null;
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

    private static bool? NullableBool(JsonElement parent, string propertyName)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static T? NullableNumber<T>(JsonElement parent, string propertyName, Func<double, T> convert)
        where T : struct
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            ? convert(value.GetDouble())
            : null;
    }
}
