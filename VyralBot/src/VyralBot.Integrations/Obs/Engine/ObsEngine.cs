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
public sealed class ObsEngine : IDisposable
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

    public ObsEngine(ObsInstallLocator locator, ILogger<ObsEngine>? logger = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.logger = logger ?? NullLogger<ObsEngine>.Instance;
    }

    /// <summary>Lock-free readiness probe (safe from any thread, never blocks on the gate).</summary>
    public bool IsReady => state == ObsEngineState.Ready;

    /// <summary>Raised on the threadpool after every state change; subscribers marshal to their own context.</summary>
    public event Action<ObsEngineStatus>? StatusChanged;

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
            // raises dispatch from this one pool thread so subscribers observe
            // Initializing strictly before the terminal status.
            status = await Task.Run(() =>
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
                // Native teardown joins libobs worker threads — off the caller thread.
                await Task.Run(ObsNative.ObsShutdown).ConfigureAwait(false);
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
        if (!ObsNative.ObsStartup("en-US", null, IntPtr.Zero))
        {
            return Fail("obs_startup failed");
        }

        logger.LogInformation("OBS engine: registering module paths ({PluginsDir})", install.PluginsBinDir);
        ObsNative.ObsAddModulePath(install.PluginsBinDir, install.PluginsDataDir);

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

        ObsNative.ObsLoadAllModules();
        ObsNative.ObsPostLoadModules();
        logger.LogInformation("OBS engine: modules loaded");

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

        // Sanity guard: startup and resets succeeded, so libobs must report live.
        if (!ObsNative.ObsInitialized())
        {
            return Fail("obs_initialized returned false after a successful startup");
        }

        version = ObsNative.ObsGetVersionString();
        state = ObsEngineState.Ready;
        error = null;
        logger.LogInformation("OBS engine: ready (libobs {Version})", version);
        return Snapshot();
    }

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
