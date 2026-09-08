using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Native;

namespace VyralBot.Integrations.Obs.Engine;

/// <summary>Lifecycle states of the embedded OBS engine.</summary>
public enum ObsEngineState
{
    /// <summary>OBS Studio was probed and is not present on this machine.</summary>
    NotInstalled,

    /// <summary>Initial state — no init attempt has run yet.</summary>
    NotInitialized,

    /// <summary>Native boot in progress (startup → modules → video/audio reset).</summary>
    Initializing,

    /// <summary>libobs booted; sources/scenes/preview can be created.</summary>
    Ready,

    /// <summary>The last init attempt failed; <see cref="ObsEngineStatus.Error"/> holds the reason.</summary>
    Failed,

    /// <summary>obs_shutdown ran (or the engine was disposed); libobs cannot boot again in this process.</summary>
    Shutdown,
}

/// <summary>Immutable snapshot of the engine state for UI binding and diagnostics.</summary>
public sealed record ObsEngineStatus(ObsEngineState State, string? Error, string? Version, string? InstallRoot);

/// <summary>
/// Owns the libobs (obs.dll) lifecycle for the process. Every public call
/// serializes through one gate (native libobs is single-init and not
/// thread-safe), and the whole boot runs on the threadpool — never the UI
/// thread. The engine holds no unmanaged references of its own: sources and
/// displays created after init are the loaders'/preview's responsibility and
/// must be released before <see cref="ShutdownAsync"/> runs.
/// </summary>
public sealed class ObsEngine : IDisposable, IObsNativeGate
{
    private readonly ObsInstallLocator locator;
    private readonly ILogger<ObsEngine> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object raiseLock = new();

    // Sequential dispatch chain for StatusChanged (see RaiseStatus).
    private Task raiseChain = Task.CompletedTask;

    // volatile: IsReady is read lock-free from UI/polling code.
    private volatile ObsEngineState state = ObsEngineState.NotInitialized;
    private string? error;
    private string? version;
    private string? installRoot;

    /// <summary>
    /// Plugin DLL stems that are never opened into this process. The installed
    /// OBS ships UI-hostile modules (CEF, VST, the OBS frontend glue, Qt dock
    /// plugins) which access-violate a non-OBS host inside their
    /// obs_module_load — the 2026-09-07 selftest AV'd inside
    /// obs_load_all_modules for exactly these. Matched as the exact file stem,
    /// case-insensitive; mutable so bespoke installs can tune it at startup.
    /// </summary>
    public static readonly List<string> DefaultBlocklistedModules = new()
    {
        "frontend-tools",
        "scene-notes-dock",
        "decklink-output-ui",
        "decklink-captions",
        "aja-output-ui",
        "aja",
        "obs-websocket",
        "obs-browser",
        "obs-vst",
        "aitum-stream-suite",
        "obs-scripting",
    };

    // Exact stem match — "aja" must not drag in an unrelated "ajax-foo" plugin.
    internal static bool IsBlocklisted(string moduleStem, IEnumerable<string> blocklist) =>
        blocklist.Contains(moduleStem, StringComparer.OrdinalIgnoreCase);

    public ObsEngine(ObsInstallLocator locator, ILogger<ObsEngine>? logger = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.logger = logger ?? NullLogger<ObsEngine>.Instance;
    }

    /// <summary>Lock-free readiness probe (safe from any thread, never blocks on the gate).</summary>
    public bool IsReady => state == ObsEngineState.Ready;

    /// <summary>Raised on the threadpool after every state change; subscribers marshal to their own context.</summary>
    public event Action<ObsEngineStatus>? StatusChanged;

    /// <summary>
    /// Average frame time in nanoseconds as libobs measured it over the last
    /// second (obs_get_average_frame_time_ns), sampled under the engine gate.
    /// 0 when the engine is not Ready or libobs has not rendered yet.
    /// </summary>
    public Task<ulong> GetAverageFrameTimeNsAsync() =>
        IsReady ? RunNativeAsync(ObsNative.ObsGetAverageFrameTimeNs) : Task.FromResult(0UL);

    /// <summary>Consistent snapshot of the current state. Blocks while init/shutdown is in flight.</summary>
    public ObsEngineStatus GetStatus()
    {
        gate.Wait();
        try
        {
            return Snapshot();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Runs native libobs work on a background thread while the gate is held.
    /// Faults with <see cref="InvalidOperationException"/> when the engine is
    /// not Ready (checked lock-free and again under the gate — the engine may
    /// reach Shutdown while a call is queued). The work must not call back into
    /// engine methods: the gate is already held and re-entering it deadlocks.
    /// </summary>
    public Task<T> RunNativeAsync<T>(Func<T> nativeWork)
    {
        ArgumentNullException.ThrowIfNull(nativeWork);
        if (!IsReady)
        {
            return Task.FromException<T>(NotReady());
        }

        return Task.Run(async () =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (state != ObsEngineState.Ready)
                {
                    throw NotReady();
                }

                return nativeWork();
            }
            finally
            {
                gate.Release();
            }
        });
    }

    /// <summary>Statement-shaped overload of <see cref="RunNativeAsync{T}"/>.</summary>
    public Task RunNativeAsync(Action nativeWork)
    {
        ArgumentNullException.ThrowIfNull(nativeWork);
        return RunNativeAsync<object?>(() =>
        {
            nativeWork();
            return null;
        });
    }

    private static InvalidOperationException NotReady() => new(
        "OBS engine is not ready. Await InitializeAsync reaching ObsEngineState.Ready first.");

    /// <summary>
    /// Runs work on a dedicated background STA thread. Deliberately NOT
    /// Task.Run: threadpool threads come pre-initialized as COM MTA, which makes
    /// libobs's own initialize_com() fail with RPC_E_CHANGED_MODE (logged as
    /// "CoInitializeEx failed: 0x80010106") and the boot corrupts the native
    /// heap (selftest 2026-09-07, 0xC0000374 — reproduced with ZERO modules
    /// loaded). OBS Studio itself boots libobs from its STA main thread
    /// (frontend OBSApp/OBSBasic @ 32.1.1), so mirror that apartment.
    /// </summary>
    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Boots libobs from the located OBS Studio install. A Failed attempt can be
    /// re-attempted; a Ready/Initializing/Shutdown engine just returns its
    /// current snapshot (libobs does not support obs_startup after obs_shutdown).
    /// </summary>
    public async Task<ObsEngineStatus> InitializeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        ObsEngineStatus status;
        try
        {
            if (state is ObsEngineState.Ready or ObsEngineState.Initializing or ObsEngineState.Shutdown)
            {
                return Snapshot();
            }

            // The whole native boot off the caller thread — obs.dll load, module
            // scan and the D3D11 device creation all block for seconds. Both
            // raises dispatch from this one boot thread so subscribers observe
            // Initializing strictly before the terminal status.
            status = await RunOnStaThread(() =>
            {
                state = ObsEngineState.Initializing;
                error = null;
                RaiseStatus(Snapshot());

                var result = InitializeCore();
                RaiseStatus(result);
                return result;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A native fault here can take the process down — accepted risk
            // (plan, Engine lifecycle). Whatever survives is recorded as a clean
            // Failed so the UI can degrade gracefully.
            logger.LogError(ex, "OBS engine: initialization threw");
            state = ObsEngineState.Failed;
            error = ex.Message;
            status = Snapshot();
            RaiseStatus(status);
        }
        finally
        {
            gate.Release();
        }

        return status;
    }

    /// <summary>Tears libobs down (no-op when it never reached Ready) and parks the engine in Shutdown.</summary>
    public async Task ShutdownAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        ObsEngineStatus status;
        try
        {
            if (state == ObsEngineState.Shutdown)
            {
                return;
            }

            if (state == ObsEngineState.Ready)
            {
                // Native teardown joins libobs worker threads — off the caller
                // thread, on the same apartment as the boot (see RunOnStaThread).
                await RunOnStaThread<object?>(() =>
                {
                    ObsNative.ObsShutdown();
                    return null;
                }).ConfigureAwait(false);
                logger.LogInformation("OBS engine: obs_shutdown completed");
            }

            state = ObsEngineState.Shutdown;
            status = Snapshot();
        }
        finally
        {
            gate.Release();
        }

        RaiseStatus(status);
    }

    public void Dispose()
    {
        // Best-effort synchronous shutdown — the DI container disposes singletons
        // during host teardown on app exit.
        try
        {
            ShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OBS engine: disposal shutdown failed");
        }
    }

    /// <summary>Runs on the threadpool with the gate held. Returns the post-boot snapshot.</summary>
    private ObsEngineStatus InitializeCore()
    {
        logger.LogInformation("OBS engine: locating the OBS Studio install");
        ObsInstall? install = locator.Locate();
        if (install is null)
        {
            return Fail("OBS Studio was not found. Install OBS to enable the embedded studio.");
        }

        installRoot = install.Root;
        logger.LogInformation("OBS engine: OBS Studio found at {InstallRoot} ({ObsDll})", install.Root, install.ObsDllPath);

        // The DLL search path MUST be set before any obs P/Invoke resolves
        // obs.dll — its dependencies (avcodec, libobs-d3d11, …) sit in the same
        // bin\64bit directory. Prepending to PATH covers anything that probes
        // dependencies by name later (e.g. plugin-loaded DLLs).
        NativeDllPaths.SetDllDirectory(install.BinDir);
        NativeDllPaths.AddDllDirectory(install.BinDir);
        string oldPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("PATH", install.BinDir + Path.PathSeparator + oldPath);
        logger.LogInformation("OBS engine: DLL search path set to {BinDir}", install.BinDir);

        logger.LogInformation("OBS engine: loading {ObsDll}", install.ObsDllPath);

        // Load obs.dll explicitly by full path (deps resolve from its directory)
        // so the P/Invokes below bind to the located install rather than relying
        // on the default probe order. The module stays loaded for the process
        // lifetime — the engine owns the process-wide native state.
        IntPtr obsModule = NativeDllPaths.LoadLibraryEx(
            install.ObsDllPath, IntPtr.Zero, NativeDllPaths.LoadWithAlteredSearchPath);
        if (obsModule == IntPtr.Zero)
        {
            int win32Error = Marshal.GetLastWin32Error();
            return Fail($"obs.dll could not be loaded from {install.ObsDllPath} (Win32 error {win32Error}).");
        }

        logger.LogInformation("OBS engine: obs_startup(en-US)");
        // Per-module config root (obs_startup's module_config_path): without it
        // modules resolve config relative to the process CWD — rtmp-services
        // then drops its services cache into the app's working directory. Same
        // role as the frontend's %APPDATA%\obs-studio\plugin_config.
        string moduleConfigPath = ToLibobsPath(Path.Combine(install.ConfigDir, "plugin_config")) + "/";
        Directory.CreateDirectory(install.ConfigDir + Path.DirectorySeparatorChar + "plugin_config");
        if (!ObsNative.ObsStartup("en-US", moduleConfigPath, IntPtr.Zero))
        {
            return Fail("obs_startup failed");
        }

        logger.LogInformation("OBS engine: registering module paths ({PluginsDir})", install.PluginsBinDir);
        ObsNative.ObsAddModulePath(install.PluginsBinDir, install.PluginsDataDir);

        // libobs resolves its OWN effect files (default.effect et al) via a
        // CWD-relative "../../data/libobs/" probe (obs-windows.c
        // find_libobs_data_file) — wrong for a host launched from anywhere but
        // the OBS bin dir. Register the absolute dir instead; obs_find_data_file
        // falls through to added paths. Trailing slash required (check_path
        // concatenates the file name onto the path verbatim).
        string libobsDataDir = ToLibobsPath(Path.Combine(install.Root, "data", "libobs")) + "/";
        ObsNative.ObsAddDataPath(libobsDataDir);
        logger.LogInformation("OBS engine: libobs data path registered ({LibobsDataDir})", libobsDataDir);

        // A user plugin bin dir without its data dir fails to load every module
        // in it — only register the pair when both halves exist (skip otherwise).
        if (install.UserPluginsBinDir is { } userBin &&
            install.UserPluginsDataDir is { } userData &&
            Directory.Exists(userBin) &&
            Directory.Exists(userData))
        {
            logger.LogInformation("OBS engine: registering user module paths ({UserPluginsDir})", userBin);
            ObsNative.ObsAddModulePath(userBin, userData);
        }

        // Profile video settings; missing/unreadable profile → 1080p60 defaults.
        ObsProfileResult profile = new ObsProfileReader(install).ReadCurrentProfile();
        ObsVideoSettings? video = profile.Settings?.Video;
        uint fpsNum = video?.FpsNum ?? 60;
        uint fpsDen = video?.FpsDen ?? 1;
        uint baseWidth = video?.BaseWidth ?? 1920;
        uint baseHeight = video?.BaseHeight ?? 1080;
        uint outputWidth = video?.OutputWidth ?? 1920;
        uint outputHeight = video?.OutputHeight ?? 1080;
        logger.LogInformation(
            "OBS engine: profile video {BaseWidth}x{BaseHeight} → {OutputWidth}x{OutputHeight} @ {FpsNum}/{FpsDen} fps{FallbackNote}",
            baseWidth, baseHeight, outputWidth, outputHeight, fpsNum, fpsDen,
            profile.Success ? string.Empty : $" (defaults — {profile.Error})");

        var videoInfo = new ObsVideoInfo
        {
            GraphicsModule = "libobs-d3d11.dll",
            FpsNum = fpsNum,
            FpsDen = fpsDen,
            BaseWidth = baseWidth,
            BaseHeight = baseHeight,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,
            OutputFormat = ObsNativeConstants.VideoFormatNv12,
            Adapter = 0,
            GpuConversion = 1,
            Colorspace = ObsNativeConstants.ColorspaceDefault,
            Range = ObsNativeConstants.RangeDefault,
            ScaleType = ObsNativeConstants.ScaleTypeDefault,
        };

        logger.LogInformation("OBS engine: obs_reset_video");
        int videoResult = ObsNative.ObsResetVideo(ref videoInfo);
        if (videoResult != 0)
        {
            return Fail($"obs_reset_video failed: {DescribeVideoResult(videoResult)}");
        }

        logger.LogInformation("OBS engine: obs_reset_audio");
        var audioInfo = new ObsAudioInfo { SamplesPerSec = 48000, Speakers = ObsNativeConstants.SpeakersStereo };
        if (!ObsNative.ObsResetAudio(ref audioInfo))
        {
            return Fail("obs_reset_audio failed");
        }

        // Modules load AFTER the graphics context exists — the OBS frontend's
        // own order (OBSBasic::OBSInit @ 32.1.1: ResetAudio → ResetVideo →
        // loadAppModules). Several plugins query graphics state during their
        // obs_module_load ("gs_get_device_type: called while not in a graphics
        // context" in the pre-reset ordering); loading them without a context
        // corrupted the native heap (selftest 0xC0000374).
        LoadModules(install);

        // Sanity guard: startup and resets succeeded, so libobs must report live.
        if (!ObsNative.ObsInitialized())
        {
            return Fail("obs_initialized returned false after a successful startup");
        }

        version = ObsNative.GetVersionString();
        state = ObsEngineState.Ready;
        error = null;
        logger.LogInformation("OBS engine: ready (libobs {Version})", version);
        return Snapshot();
    }

    /// <summary>
    /// Gate held. Per-module open + init replaces obs_load_all_modules (which
    /// loads every DLL unconditionally and died inside the first hostile
    /// obs_module_load). Each module logs BEFORE the native call so the
    /// crash-safe selftest report names the culprit if one still AVs.
    /// </summary>
    private void LoadModules(ObsInstall install)
    {
        int loaded = 0, failed = 0, skipped = 0;
        // The user plugin dir often mirrors the program dir — first stem wins.
        var openedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string binDir, string dataDir) in ModuleDirectories(install))
        {
            foreach (string dllPath in Directory.EnumerateFiles(binDir, "*.dll")
                         .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                string stem = Path.GetFileNameWithoutExtension(dllPath);
                if (IsBlocklisted(stem, DefaultBlocklistedModules))
                {
                    skipped++;
                    logger.LogInformation("module {Module}: skipped (blocklist)", stem);
                    continue;
                }

                if (!openedStems.Add(stem))
                {
                    continue;
                }

                // A data directory is a requirement, not a nicety: libobs's own
                // scan (obs-module.c process_found_module) only opens modules
                // whose data/<stem> dir exists, which also keeps non-plugin
                // support DLLs that share the bin dir (chrome_elf, libcef,
                // libEGL, …) from ever being LoadLibrary'd — their DllMain
                // corrupts a non-Chromium host's heap (selftest 0xC0000374).
                string moduleDataDir = Path.Combine(dataDir, stem);
                if (!Directory.Exists(moduleDataDir))
                {
                    skipped++;
                    logger.LogInformation("module {Module}: skipped (no data dir — not an obs plugin)", stem);
                    continue;
                }

                logger.LogInformation("module {Module}: opening", stem);
                // Forward slashes: libobs derives the module name from the text
                // after the last '/' in the path (obs-module.c get_module_name).
                int open = ObsNative.ObsOpenModule(out IntPtr module, ToLibobsPath(dllPath), ToLibobsPath(moduleDataDir));
                if (open != ObsNativeConstants.ModuleSuccess)
                {
                    failed++;
                    logger.LogWarning("module {Module}: failed to open ({Reason})", stem, DescribeModuleResult(open));
                    continue;
                }

                if (!ObsNative.ObsInitModule(module))
                {
                    failed++;
                    logger.LogWarning("module {Module}: failed (obs_init_module)", stem);
                    continue;
                }

                loaded++;
                logger.LogInformation("module {Module}: ok", stem);
            }
        }

        ObsNative.ObsPostLoadModules();
        logger.LogInformation(
            "OBS engine: per-module load done ({Loaded} ok, {Failed} failed, {Skipped} skipped)",
            loaded, failed, skipped);
    }

    private static IEnumerable<(string BinDir, string DataDir)> ModuleDirectories(ObsInstall install)
    {
        if (Directory.Exists(install.PluginsBinDir))
        {
            yield return (install.PluginsBinDir, install.PluginsDataDir);
        }

        if (install.UserPluginsBinDir is { } userBin
            && install.UserPluginsDataDir is { } userData
            && Directory.Exists(userBin))
        {
            yield return (userBin, userData);
        }
    }

    private static string ToLibobsPath(string path) => path.Replace('\\', '/');

    /// <summary>obs-defs.h @ 32.1.1 MODULE_* codes for the obs_open_module result.</summary>
    private static string DescribeModuleResult(int result) => result switch
    {
        ObsNativeConstants.ModuleError => "MODULE_ERROR",
        ObsNativeConstants.ModuleFailedToOpen => "MODULE_FAILED_TO_OPEN",
        ObsNativeConstants.ModuleMissingExports => "MODULE_MISSING_EXPORTS",
        ObsNativeConstants.ModuleIncompatibleVersion => "MODULE_INCOMPATIBLE_VER",
        ObsNativeConstants.ModuleHardcodedSkip => "MODULE_HARDCODED_SKIP",
        _ => $"unknown code {result}",
    };

    /// <summary>Gate held. Terminal failure of the boot attempt — surfaced via the next StatusChanged.</summary>
    private ObsEngineStatus Fail(string reason)
    {
        logger.LogError("OBS engine: {Reason}", reason);
        state = ObsEngineState.Failed;
        error = reason;
        return Snapshot();
    }

    private ObsEngineStatus Snapshot() => new(state, error, version, installRoot);

    /// <summary>
    /// obs-defs.h @ 32.1.1: OBS_VIDEO_SUCCESS=0, FAIL=-1, NOT_SUPPORTED=-2,
    /// INVALID_PARAM=-3, CURRENTLY_ACTIVE=-4, MODULE_NOT_FOUND=-5.
    /// </summary>
    private static string DescribeVideoResult(int result) => result switch
    {
        -1 => "OBS_VIDEO_FAIL",
        -2 => "OBS_VIDEO_NOT_SUPPORTED",
        -3 => "OBS_VIDEO_INVALID_PARAM",
        -4 => "OBS_VIDEO_CURRENTLY_ACTIVE",
        -5 => "OBS_VIDEO_MODULE_NOT_FOUND",
        _ => $"unknown code {result}",
    };

    // Handlers run on the threadpool, off the engine's critical path (a handler
    // may call GetStatus, which takes the gate) — chained continuations keep
    // them sequential and strictly in raise order, since independent Task.Run
    // dispatches may reorder on the pool's LIFO local queue. The UI marshals
    // onto its own dispatcher.
    private void RaiseStatus(ObsEngineStatus status)
    {
        Action<ObsEngineStatus>? handlers = StatusChanged;
        if (handlers is null)
        {
            return;
        }

        lock (raiseLock)
        {
            raiseChain = raiseChain.ContinueWith(_ =>
            {
                try
                {
                    handlers(status);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "OBS engine: StatusChanged handler threw");
                }
            }, TaskScheduler.Default);
        }
    }
}
