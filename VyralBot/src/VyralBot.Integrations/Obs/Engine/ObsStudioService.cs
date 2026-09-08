using VyralBot.Integrations.Obs.Native;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>One scene of the running libobs instance, in enumeration order.</summary>
public sealed record ObsSceneInfo(string Name);

/// <summary>One scene item: the child source plus its per-item state. <paramref name="Item"/> is the native obs_sceneitem_t* (valid until libobs state changes).</summary>
public sealed record ObsSceneItemInfo(IntPtr Item, string SourceName, string SourceId, bool Visible, bool Locked);

/// <summary>
/// Studio facade over the embedded engine: scene-collection loading plus the
/// scenes/items operations the studio UI needs. Every native call runs through
/// <see cref="IObsNativeGate.RunNativeAsync{T}"/> (background thread, engine
/// gate held); scene-item pointers are cached keyed by (scene, source) and
/// refreshed by any subsequent enumeration of the same scene.
/// </summary>
public sealed class ObsStudioService
{
    private readonly ObsEngine engine;
    private readonly ObsSceneCollectionLoader loader;
    private readonly object cacheLock = new();
    private readonly Dictionary<(string Scene, string Source), IntPtr> itemCache = new();

    public ObsStudioService(ObsEngine engine, ObsSceneCollectionLoader loader)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    /// <summary>
    /// Recreates a scene collection inside the running libobs instance. On
    /// success the (scene, source) → item cache is dropped explicitly: the
    /// native scene graph was rebuilt, so cached pointers belong to the
    /// previous load (the next enumeration repopulates the cache).
    /// </summary>
    public async Task<ObsLoadResult> LoadCollectionAsync(string collectionJson)
    {
        ObsLoadResult result = await loader.LoadAsync(collectionJson).ConfigureAwait(false);
        if (result.Success)
        {
            lock (cacheLock)
            {
                itemCache.Clear();
            }
        }

        return result;
    }

    /// <summary>All scenes, in libobs enumeration order (creation order for a fresh load).</summary>
    public Task<IReadOnlyList<ObsSceneInfo>> GetScenesAsync() => engine.RunNativeAsync(() =>
    {
        List<ObsSceneInfo> scenes = [];
        ObsNative.ObsEnumScenes((_, source) =>
        {
            scenes.Add(new ObsSceneInfo(ObsNative.SourceGetName(source)));
            return true;
        }, IntPtr.Zero);
        return (IReadOnlyList<ObsSceneInfo>)scenes;
    });

    /// <summary>Puts the named scene on the program output (channel 0). Throws when it does not exist.</summary>
    public Task SetCurrentSceneAsync(string sceneName) => engine.RunNativeAsync(() =>
    {
        IntPtr source = RequireSource(sceneName);
        try
        {
            ObsNative.ObsSetOutputSource(0, source);
        }
        finally
        {
            // obs_get_source_by_name returns a new reference; obs_set_output_source
            // adds its own — drop ours (matches the OBS frontend's set-then-release).
            ObsNative.ObsSourceRelease(source);
        }
    });

    /// <summary>Items of the named scene; refreshes the (scene, source) → item cache.</summary>
    public Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemsAsync(string sceneName) =>
        engine.RunNativeAsync(() => (IReadOnlyList<ObsSceneItemInfo>)EnumerateItems(sceneName));

    public Task<bool> SetItemVisibleAsync(string sceneName, string sourceName, bool visible) =>
        SetItemStateAsync(sceneName, sourceName, item => ObsNative.ObsSceneItemSetVisible(item, visible));

    public Task<bool> SetItemLockedAsync(string sceneName, string sourceName, bool locked) =>
        SetItemStateAsync(sceneName, sourceName, item => ObsNative.ObsSceneItemSetLocked(item, locked));

    /// <summary>Gate held via RunNativeAsync. Applies the state change; false when the item no longer exists.</summary>
    private Task<bool> SetItemStateAsync(string sceneName, string sourceName, Action<IntPtr> apply) =>
        engine.RunNativeAsync(() =>
        {
            IntPtr item = ResolveItem(sceneName, sourceName);
            if (item == IntPtr.Zero)
            {
                return false;
            }

            apply(item);
            return true;
        });

    /// <summary>Gate already held. Cache lookup with a same-gate re-enumeration on miss (native state may have drifted).</summary>
    private IntPtr ResolveItem(string sceneName, string sourceName)
    {
        lock (cacheLock)
        {
            if (itemCache.TryGetValue((sceneName, sourceName), out IntPtr cached))
            {
                return cached;
            }
        }

        EnumerateItems(sceneName);
        lock (cacheLock)
        {
            return itemCache.TryGetValue((sceneName, sourceName), out IntPtr item) ? item : IntPtr.Zero;
        }
    }

    /// <summary>Gate already held. Enumerates the scene's items and refreshes the cache. Caller owns no references (enum callbacks yield borrowed pointers).</summary>
    private List<ObsSceneItemInfo> EnumerateItems(string sceneName)
    {
        IntPtr source = RequireSource(sceneName);
        try
        {
            IntPtr scene = ObsNative.ObsSceneFromSource(source);
            if (scene == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Source '{sceneName}' is not a scene.");
            }

            var items = new List<ObsSceneItemInfo>();
            ObsNative.ObsSceneEnumItems(scene, (_, item, _) =>
            {
                IntPtr child = ObsNative.ObsSceneItemGetSource(item);
                items.Add(new ObsSceneItemInfo(
                    item,
                    ObsNative.SourceGetName(child),
                    ObsNative.SourceGetId(child),
                    ObsNative.ObsSceneItemVisible(item),
                    ObsNative.ObsSceneItemLocked(item)));
                return true;
            }, IntPtr.Zero);

            lock (cacheLock)
            {
                itemCache.Clear();
                foreach (ObsSceneItemInfo info in items)
                {
                    itemCache[(sceneName, info.SourceName)] = info.Item;
                }
            }

            return items;
        }
        finally
        {
            ObsNative.ObsSourceRelease(source);
        }
    }

    /// <summary>Gate already held. Returns a borrowed-plus-one reference (caller must release); throws when absent.</summary>
    private IntPtr RequireSource(string name)
    {
        IntPtr source = ObsNative.ObsGetSourceByName(name);
        if (source == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Source '{name}' was not found in the running OBS instance.");
        }

        return source;
    }
}
