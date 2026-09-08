using VyralBot.Integrations.Obs.Native;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>
/// Minimal surface consumers need from the engine gate. Implemented by
/// <see cref="ObsEngine"/>: native work queued through it runs on a background
/// thread while the engine's lifecycle gate is held, and faults when the engine
/// is not Ready.
/// </summary>
public interface IObsNativeGate
{
    bool IsReady { get; }

    /// <summary>Runs native work on a background thread under the engine gate.</summary>
    Task<T> RunNativeAsync<T>(Func<T> nativeWork);

    /// <summary>
    /// Flags that native state now exists in this process (a collection load
    /// created sources, an output was created). Called from inside gate-held
    /// work; App exit consults the engine's <c>HasLoadedNativeState</c> to
    /// decide whether <c>obs_shutdown</c> is safe to run.
    /// </summary>
    void MarkNativeStateLoaded();
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
/// program scene. Each load keeps its created source/filter references only
/// until the NEXT load, which first clears output channels 0–6 and releases
/// them — so repeated loads neither leak nor leave stale scenes/audio behind.
/// </summary>
public sealed class ObsSceneCollectionLoader
{
    private readonly IObsNativeGate engine;

    // References created by the LAST load (written and read only inside
    // gate-held work — the engine gate serializes all access). The running
    // instance needs the current collection's sources alive; releasing them
    // at the start of the next load replaces kept-forever semantics that
    // leaked every historical load and surfaced stale scenes in
    // obs_enum_scenes / duplicate-named sources.
    private List<IntPtr> keptSourceRefs = [];
    private List<IntPtr> keptFilterRefs = [];

    public ObsSceneCollectionLoader(IObsNativeGate engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
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

        return engine.RunNativeAsync(() =>
        {
            // The load attempt creates native state from here on — flag it
            // under the gate so App exit knows obs_shutdown is unsafe.
            engine.MarkNativeStateLoaded();
            return Execute(plan);
        });
    }

    /// <summary>Gate held. Runs one load: resets the previous load's native footprint first, then rebuilds.</summary>
    private ObsLoadResult Execute(ObsLoadPlan plan)
    {
        ResetPreviousLoad();

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

        // This load's references are kept until the next load resets them.
        keptSourceRefs.AddRange(created.Select(entry => entry.Source));
        keptFilterRefs.AddRange(filterRefs);

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

    /// <summary>
    /// Gate held. Wipes the previous load's native footprint before the new
    /// collection rebuilds on top of it: the output channels first (a stale
    /// program scene or audio source would keep the released sources alive and
    /// keep playing audio), then the previous load's kept source and filter
    /// references.
    /// </summary>
    private void ResetPreviousLoad()
    {
        for (uint channel = 0; channel <= 6; channel++)
        {
            ObsNative.ObsSetOutputSource(channel, IntPtr.Zero);
        }

        List<IntPtr> sources = keptSourceRefs;
        List<IntPtr> filters = keptFilterRefs;
        keptSourceRefs = [];
        keptFilterRefs = [];

        foreach (IntPtr source in sources)
        {
            ObsNative.ObsSourceRelease(source);
        }

        foreach (IntPtr filter in filters)
        {
            ObsNative.ObsSourceRelease(filter);
        }
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

        // Deinterlace settings: the collection strings ("yadif_2x", "top")
        // map onto the 32.1.1 int enums; unknown values are skipped rather
        // than passed through, so a newer collection can't inject an enum
        // value libobs would index arrays with.
        if (ObsDeinterlace.ParseMode(plan.DeinterlaceMode) is { } deinterlaceMode)
        {
            ObsNative.ObsSourceSetDeinterlaceMode(source, deinterlaceMode);
        }

        if (ObsDeinterlace.ParseFieldOrder(plan.DeinterlaceFieldOrder) is { } fieldOrder)
        {
            ObsNative.ObsSourceSetDeinterlaceFieldOrder(source, fieldOrder);
        }

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
