using VyralBot.Integrations.Obs.Native;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>
/// Minimal surface the loader needs from the engine gate. ObsEngine (parallel
/// task, not yet in the tree when this was written) exposes exactly
/// <c>bool IsReady</c> + <c>Task&lt;T&gt; RunNativeAsync&lt;T&gt;(Func&lt;T&gt;)</c>;
/// Task 5 reconciles by having ObsEngine implement this interface so the
/// loader can take the engine directly.
/// </summary>
internal interface IObsNativeGate
{
    bool IsReady { get; }

    /// <summary>Runs native work on a background thread under the engine gate.</summary>
    Task<T> RunNativeAsync<T>(Func<T> nativeWork);
}

public sealed record ObsLoadResult(
    bool Success,
    string Message,
    int CreatedCount,
    IReadOnlyList<string> MissingSources,
    string? CurrentScene);

/// <summary>
/// Recreates a scene collection inside a running libobs instance from its
/// JSON document: creates sources, applies properties + filters, loads scenes
/// (which resolve their items by name), then wires audio channels and the
/// program scene. Kept source/filter references are intentionally never
/// released — the engine holds them for the app lifetime and
/// <c>obs_shutdown</c> frees everything on teardown.
/// </summary>
public sealed class ObsSceneCollectionLoader
{
    private readonly IObsNativeGate engine;

    // Internal while the gate is internal; Task 5 swaps this for a public
    // ObsEngine-taking constructor once the engine type exists.
    internal ObsSceneCollectionLoader(IObsNativeGate engine)
    {
        this.engine = engine;
    }

    public Task<ObsLoadResult> LoadAsync(string collectionJson)
    {
        // Pure JSON work outside the gate; malformed collections throw
        // InvalidDataException before any native call is queued.
        var plan = ObsLoadPlanBuilder.Build(collectionJson);
        if (!engine.IsReady)
        {
            return Task.FromResult(new ObsLoadResult(
                false, "OBS engine is not ready.", 0, [], null));
        }

        return engine.RunNativeAsync(() => Execute(plan));
    }

    private static ObsLoadResult Execute(ObsLoadPlan plan)
    {
        List<string> missing = [];
        // Creation order is the load order; byName is only for lookups
        // (scene items, audio channels) where the last entry wins.
        List<(ObsSourcePlan Plan, IntPtr Source)> created = [];
        Dictionary<string, IntPtr> byName = new(StringComparer.Ordinal);
        List<ObsSourcePlan> allPlans = [.. plan.Sources, .. plan.AudioDevices.Select(d => d.Source)];

        // Pass 1 — create every source (scenes included; settings untouched).
        foreach (ObsSourcePlan source in allPlans)
        {
            IntPtr settings = DataFromJson(source.SettingsJson);
            IntPtr hotkeys = DataFromJson(source.HotkeysJson);
            IntPtr ptr = ObsNative.ObsSourceCreate(source.Id, source.Name, settings, hotkeys);
            if (settings != IntPtr.Zero)
            {
                ObsNative.ObsDataRelease(settings);
            }

            if (hotkeys != IntPtr.Zero)
            {
                ObsNative.ObsDataRelease(hotkeys);
            }

            if (ptr == IntPtr.Zero)
            {
                missing.Add(source.Name);
                continue;
            }

            created.Add((source, ptr));
            byName[source.Name] = ptr;
        }

        // Pass 2 — properties and filters on the created sources.
        List<IntPtr> filterRefs = [];
        foreach ((ObsSourcePlan source, IntPtr ptr) in created)
        {
            ApplyProperties(ptr, source, filterRefs);
        }

        // Pass 3 — load: scenes resolve their items by name here, so every
        // referenced source must already exist (pass 1 guarantees that).
        foreach ((_, IntPtr ptr) in created)
        {
            ObsNative.ObsSourceLoad(ptr);
        }

        // Audio devices → global mix channels 1-6 (0 is the program channel).
        foreach (ObsAudioDevicePlan device in plan.AudioDevices)
        {
            if (AudioChannel(device.ChannelKey) is { } channel
                && byName.TryGetValue(device.Source.Name, out IntPtr ptr))
            {
                ObsNative.ObsSetOutputSource(channel, ptr);
            }
        }

        // Program scene: current_scene, else the first created scene source.
        string? currentScene = null;
        if (plan.CurrentSceneName is not null
            && byName.TryGetValue(plan.CurrentSceneName, out IntPtr scenePtr))
        {
            ObsNative.ObsSetOutputSource(0, scenePtr);
            currentScene = plan.CurrentSceneName;
        }
        else
        {
            var fallback = created.FirstOrDefault(entry => entry.Plan.Id == "scene");
            if (fallback.Plan is not null)
            {
                ObsNative.ObsSetOutputSource(0, fallback.Source);
                currentScene = fallback.Plan.Name;
            }
        }

        List<string> notes = [$"Loaded {created.Count} source(s)."];
        if (missing.Count > 0)
        {
            notes.Add($"Missing: {string.Join(", ", missing)}.");
        }

        notes.Add(currentScene is not null
            ? $"Current scene '{currentScene}'."
            : "No current scene set.");
        if (plan.CurrentSceneName is not null && currentScene != plan.CurrentSceneName)
        {
            notes.Add($"Scene '{plan.CurrentSceneName}' not found; fell back to '{currentScene}'.");
        }

        return new ObsLoadResult(
            Success: missing.Count == 0,
            Message: string.Join(" ", notes),
            CreatedCount: created.Count,
            missing,
            currentScene);
    }

    private static void ApplyProperties(IntPtr source, ObsSourcePlan plan, List<IntPtr> filterRefs)
    {
        if (plan.Volume is { } volume)
        {
            ObsNative.ObsSourceSetVolume(source, volume);
        }

        if (plan.Muted is { } muted)
        {
            ObsNative.ObsSourceSetMuted(source, muted);
        }

        if (plan.Mixers is { } mixers)
        {
            ObsNative.ObsSourceSetAudioMixers(source, mixers);
        }

        if (plan.MonitoringType is { } monitoringType)
        {
            ObsNative.ObsSourceSetMonitoringType(source, monitoringType);
        }

        if (plan.Flags is { } flags)
        {
            ObsNative.ObsSourceSetFlags(source, flags);
        }

        // DeinterlaceMode/DeinterlaceFieldOrder are carried on the plan but
        // skipped here: ObsNative (32.1.1 layer) exposes no deinterlace setters.

        foreach (ObsFilterPlan filter in plan.Filters)
        {
            IntPtr settings = DataFromJson(filter.SettingsJson);
            IntPtr filterPtr = ObsNative.ObsSourceCreate(filter.Id, filter.Name, settings, IntPtr.Zero);
            if (settings != IntPtr.Zero)
            {
                ObsNative.ObsDataRelease(settings);
            }

            if (filterPtr == IntPtr.Zero)
            {
                continue;
            }

            if (!filter.Enabled)
            {
                ObsNative.ObsSourceSetEnabled(filterPtr, false);
            }

            ObsNative.ObsSourceFilterAdd(source, filterPtr);
            filterRefs.Add(filterPtr);
        }
    }

    // obs_source_create treats null settings exactly like an empty obs_data,
    // so "{}" (the universal empty in collections) skips the alloc/release.
    private static IntPtr DataFromJson(string? json)
    {
        return string.IsNullOrEmpty(json) || json == "{}"
            ? IntPtr.Zero
            : ObsNative.ObsDataCreateFromJson(json);
    }

    private static uint? AudioChannel(string channelKey)
    {
        return channelKey switch
        {
            "Desktop1" => 1,
            "Desktop2" => 2,
            "Aux1" => 3,
            "Aux2" => 4,
            "Aux3" => 5,
            "Aux4" => 6,
            _ => null,
        };
    }
}
