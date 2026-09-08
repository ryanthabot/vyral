using System.Runtime.InteropServices;

namespace VyralBot.Integrations.Obs.Native;

/// <summary>
/// P/Invoke layer over libobs (obs.dll). Every struct layout, enum value and
/// signature here was verified against the raw libobs headers at GitHub tag
/// <c>32.1.1</c> — the exact version installed on this machine
/// (C:\Program Files\obs-studio\bin\64bit\obs64.exe, FileVersion 32.1.1).
///
/// Deviations from the 30.x-era integration plan, all confirmed in the
/// 32.1.1 headers:
/// - <c>obs_video_info</c> no longer carries <c>d3d11_adapters[16]</c> /
///   <c>d3d11_adapter_count</c> (removed pre-28.0); the struct is 56 bytes,
///   not the plan's 320.
/// - <c>obs_display_create(const gs_init_data*, uint32_t backround_color)</c>
///   — the old <c>obs_display_info</c> struct no longer exists, so this layer
///   models <c>gs_init_data</c> (graphics.h) as <see cref="ObsInitData"/>.
/// - <c>obs_reset_audio</c> returns bool (not int).
/// - <c>obs_output_can_begin_data_capture(output, uint32_t flags)</c> and
///   <c>obs_output_initialize_encoders(output, uint32_t flags)</c> take flags,
///   not per-stream booleans.
/// - <c>obs_output_destroy</c> does not exist; outputs are refcounted via
///   <see cref="ObsOutputRelease"/>.
/// - <c>obs_scene_enum_items</c> callback is
///   <c>bool(obs_scene_t*, obs_sceneitem_t*, void*)</c> — scene is the first arg.
/// - <c>obs_enum_sources</c> returns void; its callback returns bool.
///
/// All char* parameters, returns and struct fields marshal as UTF-8
/// (LPUTF8Str): libobs uses UTF-8 everywhere and the default Ansi marshaling
/// would corrupt non-ASCII source names and settings.
/// </summary>
internal static class ObsNative
{
    private const string Lib = "obs";
    private const CallingConvention Cdecl = System.Runtime.InteropServices.CallingConvention.Cdecl;

    // ----- lifecycle / video (obs.h @ 32.1.1, lines 366-446) -----

    [DllImport(Lib, EntryPoint = "obs_startup", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsStartup(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? locale,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? moduleConfigPath,
        IntPtr profilerNameStore);

    [DllImport(Lib, EntryPoint = "obs_shutdown", CallingConvention = Cdecl)]
    internal static extern void ObsShutdown();

    [DllImport(Lib, EntryPoint = "obs_initialized", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsInitialized();

    /// <summary>Packed version (major&lt;&lt;24 | minor&lt;&lt;16 | patch). For text use <see cref="ObsGetVersionString"/>.</summary>
    [DllImport(Lib, EntryPoint = "obs_get_version", CallingConvention = Cdecl)]
    internal static extern uint ObsGetVersion();

    // Returns a borrowed pointer to a static string (obs.c: `return OBS_VERSION;`
    // — a compile-time literal in .rdata). Marshaling it as a LPUTF8Str return
    // makes the interop CoTaskMemFree memory libobs does NOT own → heap
    // corruption (0xC0000374). Copy manually, never free.
    [DllImport(Lib, EntryPoint = "obs_get_version_string", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsGetVersionString();

    /// <summary><see cref="ObsGetVersionString"/> copied to a managed string.</summary>
    internal static string GetVersionString()
    {
        IntPtr native = ObsGetVersionString();
        return native == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(native);
    }

    /// <summary>Returns an OBS_VIDEO_* code (obs-defs.h: SUCCESS=0, FAIL=-1, NOT_SUPPORTED=-2, INVALID_PARAM=-3, CURRENTLY_ACTIVE=-4, MODULE_NOT_FOUND=-5).</summary>
    [DllImport(Lib, EntryPoint = "obs_reset_video", CallingConvention = Cdecl)]
    internal static extern int ObsResetVideo(ref ObsVideoInfo videoInfo);

    // Verified: `EXPORT bool obs_reset_audio(const struct obs_audio_info *oai)` — bool, not int.
    [DllImport(Lib, EntryPoint = "obs_reset_audio", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsResetAudio(ref ObsAudioInfo audioInfo);

    [DllImport(Lib, EntryPoint = "obs_add_module_path", CallingConvention = Cdecl)]
    internal static extern void ObsAddModulePath(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string binPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dataPath);

    // Verified: `EXPORT void obs_add_data_path(const char *path);` — appends a
    // search path for obs_find_data_file (libobs's own effect/config files).
    // Checked only AFTER the CWD-relative "../../data/libobs/" probe
    // (obs-windows.c find_libobs_data_file) misses, so this is how a host whose
    // CWD is not the OBS bin dir gets its data dir found. Path must end with a
    // separator: check_path concatenates the file name onto it verbatim.
    [DllImport(Lib, EntryPoint = "obs_add_data_path", CallingConvention = Cdecl)]
    internal static extern void ObsAddDataPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    // NOTE: obs_load_all_modules is deliberately NOT imported. It loads every
    // DLL in the registered paths — including UI/CEF/Qt-hostile modules that
    // access-violate a non-OBS host (2026-09-07 selftest) — with no chance to
    // skip or log per module. The engine uses the open/init pair below instead.

    // Verified: `EXPORT int obs_open_module(obs_module_t **module,
    // const char *path, const char *data_path);` — returns a MODULE_* code
    // (obs-defs.h, see <see cref="ObsNativeConstants"/> Module* constants), NOT
    // bool. `path` is the module library path — the module NAME derives from
    // the file stem — and `data_path` is copied verbatim (a missing data dir
    // only matters to modules that fetch their locale/assets during load).
    // A DLL without obs_module_load fails here with MODULE_MISSING_EXPORTS.
    [DllImport(Lib, EntryPoint = "obs_open_module", CallingConvention = Cdecl)]
    internal static extern int ObsOpenModule(
        out IntPtr module,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dataPath);

    // Verified: `EXPORT bool obs_init_module(obs_module_t *module);` — invokes
    // the module's obs_module_load (this is where plugin entry code runs).
    [DllImport(Lib, EntryPoint = "obs_init_module", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsInitModule(IntPtr module);

    // Verified: `EXPORT obs_module_t *obs_get_module(const char *name);` —
    // name is mod_name (the file stem).
    [DllImport(Lib, EntryPoint = "obs_get_module", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsGetModule([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // Verified: `EXPORT void obs_enum_modules(obs_enum_module_callback_t callback, void *param);`
    [DllImport(Lib, EntryPoint = "obs_enum_modules", CallingConvention = Cdecl)]
    internal static extern void ObsEnumModules(ObsEnumModuleCallback callback, IntPtr param);

    [DllImport(Lib, EntryPoint = "obs_post_load_modules", CallingConvention = Cdecl)]
    internal static extern void ObsPostLoadModules();

    [DllImport(Lib, EntryPoint = "obs_get_average_frame_time_ns", CallingConvention = Cdecl)]
    internal static extern ulong ObsGetAverageFrameTimeNs();

    // ----- obs_data (obs-data.h @ 32.1.1, lines 61-65) -----

    [DllImport(Lib, EntryPoint = "obs_data_create_from_json", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsDataCreateFromJson([MarshalAs(UnmanagedType.LPUTF8Str)] string json);

    [DllImport(Lib, EntryPoint = "obs_data_release", CallingConvention = Cdecl)]
    internal static extern void ObsDataRelease(IntPtr data);

    // ----- sources (obs.h @ 32.1.1) -----

    [DllImport(Lib, EntryPoint = "obs_source_create", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSourceCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr settings,
        IntPtr hotkeyData);

    [DllImport(Lib, EntryPoint = "obs_source_load", CallingConvention = Cdecl)]
    internal static extern void ObsSourceLoad(IntPtr source);

    [DllImport(Lib, EntryPoint = "obs_source_release", CallingConvention = Cdecl)]
    internal static extern void ObsSourceRelease(IntPtr source);

    // Both getters below return BORROWED pointers (source->name / source->id —
    // owned by the source). Same rule as <see cref="ObsGetVersionString"/>:
    // copy via the helpers, never let the marshaler free them.
    [DllImport(Lib, EntryPoint = "obs_source_get_name", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSourceGetName(IntPtr source);

    internal static string SourceGetName(IntPtr source)
    {
        IntPtr native = ObsSourceGetName(source);
        return native == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(native);
    }

    [DllImport(Lib, EntryPoint = "obs_source_get_id", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSourceGetId(IntPtr source);

    internal static string SourceGetId(IntPtr source)
    {
        IntPtr native = ObsSourceGetId(source);
        return native == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(native);
    }

    // Verified: `EXPORT void obs_set_output_source(uint32_t channel, obs_source_t *source)` — uint32, not size_t.
    [DllImport(Lib, EntryPoint = "obs_set_output_source", CallingConvention = Cdecl)]
    internal static extern void ObsSetOutputSource(uint channel, IntPtr source);

    [DllImport(Lib, EntryPoint = "obs_get_source_by_name", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsGetSourceByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, EntryPoint = "obs_source_set_volume", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetVolume(IntPtr source, float volume);

    [DllImport(Lib, EntryPoint = "obs_source_set_muted", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetMuted(IntPtr source, [MarshalAs(UnmanagedType.I1)] bool muted);

    [DllImport(Lib, EntryPoint = "obs_source_set_audio_mixers", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetAudioMixers(IntPtr source, uint mixers);

    // enum obs_monitoring_type: NONE=0, MONITOR_ONLY=1, MONITOR_AND_OUTPUT=2 (obs.h 1360-1364).
    [DllImport(Lib, EntryPoint = "obs_source_set_monitoring_type", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetMonitoringType(IntPtr source, int monitoringType);

    [DllImport(Lib, EntryPoint = "obs_source_set_flags", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetFlags(IntPtr source, uint flags);

    [DllImport(Lib, EntryPoint = "obs_source_filter_add", CallingConvention = Cdecl)]
    internal static extern void ObsSourceFilterAdd(IntPtr source, IntPtr filter);

    [DllImport(Lib, EntryPoint = "obs_source_set_enabled", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetEnabled(IntPtr source, [MarshalAs(UnmanagedType.I1)] bool enabled);

    // ----- scenes / scene items (obs.h @ 32.1.1) -----

    [DllImport(Lib, EntryPoint = "obs_scene_from_source", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSceneFromSource(IntPtr source);

    [DllImport(Lib, EntryPoint = "obs_scene_get_source", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSceneGetSource(IntPtr scene);

    // Verified: callback is `bool(obs_scene_t *scene, obs_sceneitem_t *item, void *param)` — scene first.
    [DllImport(Lib, EntryPoint = "obs_scene_enum_items", CallingConvention = Cdecl)]
    internal static extern void ObsSceneEnumItems(IntPtr scene, ObsEnumSceneItemsCallback callback, IntPtr param);

    // Verified: `EXPORT obs_sceneitem_t *obs_scene_add(obs_scene_t *scene, obs_source_t *source);`
    [DllImport(Lib, EntryPoint = "obs_scene_add", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSceneAdd(IntPtr scene, IntPtr source);

    // Verified: `EXPORT obs_sceneitem_t *obs_scene_find_source(obs_scene_t *scene, const char *name);`
    [DllImport(Lib, EntryPoint = "obs_scene_find_source", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSceneFindSource(IntPtr scene, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, EntryPoint = "obs_sceneitem_get_source", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsSceneItemGetSource(IntPtr sceneItem);

    [DllImport(Lib, EntryPoint = "obs_sceneitem_set_locked", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsSceneItemSetLocked(IntPtr sceneItem, [MarshalAs(UnmanagedType.I1)] bool locked);

    // Verified: `EXPORT bool obs_sceneitem_locked(const obs_sceneitem_t *item);`
    [DllImport(Lib, EntryPoint = "obs_sceneitem_locked", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsSceneItemLocked(IntPtr sceneItem);

    [DllImport(Lib, EntryPoint = "obs_sceneitem_set_visible", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsSceneItemSetVisible(IntPtr sceneItem, [MarshalAs(UnmanagedType.I1)] bool visible);

    [DllImport(Lib, EntryPoint = "obs_sceneitem_visible", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsSceneItemVisible(IntPtr sceneItem);

    // Verified: `EXPORT void obs_enum_sources(bool (*enum_proc)(void *, obs_source_t *), void *param)` — void return.
    [DllImport(Lib, EntryPoint = "obs_enum_sources", CallingConvention = Cdecl)]
    internal static extern void ObsEnumSources(ObsEnumSourcesCallback enumProc, IntPtr param);

    // Verified: `EXPORT void obs_enum_scenes(bool (*enum_proc)(void *, obs_source_t *), void *param);` —
    // void return, same callback shape as obs_enum_sources, scenes only.
    [DllImport(Lib, EntryPoint = "obs_enum_scenes", CallingConvention = Cdecl)]
    internal static extern void ObsEnumScenes(ObsEnumSourcesCallback enumProc, IntPtr param);

    // Verified: `EXPORT void obs_source_set_deinterlace_mode(obs_source_t *source, enum obs_deinterlace_mode mode);`
    [DllImport(Lib, EntryPoint = "obs_source_set_deinterlace_mode", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetDeinterlaceMode(IntPtr source, int mode);

    // Verified: `EXPORT void obs_source_set_deinterlace_field_order(obs_source_t *source, enum obs_deinterlace_field_order field_order);`
    [DllImport(Lib, EntryPoint = "obs_source_set_deinterlace_field_order", CallingConvention = Cdecl)]
    internal static extern void ObsSourceSetDeinterlaceFieldOrder(IntPtr source, int fieldOrder);

    // ----- display (obs.h @ 32.1.1, lines 995-1024 + 814) -----

    // Verified: takes `const struct gs_init_data *` + backround_color — obs_display_info is gone at 32.1.1.
    [DllImport(Lib, EntryPoint = "obs_display_create", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsDisplayCreate(ref ObsInitData graphicsData, uint backgroundColor);

    [DllImport(Lib, EntryPoint = "obs_display_destroy", CallingConvention = Cdecl)]
    internal static extern void ObsDisplayDestroy(IntPtr display);

    [DllImport(Lib, EntryPoint = "obs_display_resize", CallingConvention = Cdecl)]
    internal static extern void ObsDisplayResize(IntPtr display, uint cx, uint cy);

    [DllImport(Lib, EntryPoint = "obs_display_add_draw_callback", CallingConvention = Cdecl)]
    internal static extern void ObsDisplayAddDrawCallback(IntPtr display, ObsDrawCallback draw, IntPtr param);

    // Verified: remove takes the same (display, draw, param) triple as add.
    [DllImport(Lib, EntryPoint = "obs_display_remove_draw_callback", CallingConvention = Cdecl)]
    internal static extern void ObsDisplayRemoveDrawCallback(IntPtr display, ObsDrawCallback draw, IntPtr param);

    [DllImport(Lib, EntryPoint = "obs_display_set_enabled", CallingConvention = Cdecl)]
    internal static extern void ObsDisplaySetEnabled(IntPtr display, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Lib, EntryPoint = "obs_display_set_background_color", CallingConvention = Cdecl)]
    internal static extern void ObsDisplaySetBackgroundColor(IntPtr display, uint color);

    /// <summary>Renders the main composed texture; call from inside a display draw callback only.</summary>
    [DllImport(Lib, EntryPoint = "obs_render_main_texture", CallingConvention = Cdecl)]
    internal static extern void ObsRenderMainTexture();

    // ----- outputs (obs.h @ 32.1.1) -----

    [DllImport(Lib, EntryPoint = "obs_output_create", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsOutputCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr settings,
        IntPtr hotkeyData);

    // Verified: obs_output_destroy does not exist at 32.1.1; release drops the reference.
    [DllImport(Lib, EntryPoint = "obs_output_release", CallingConvention = Cdecl)]
    internal static extern void ObsOutputRelease(IntPtr output);

    // Verified: `(const obs_output_t *output, uint32_t flags)` — flags, not (audio, video) booleans.
    [DllImport(Lib, EntryPoint = "obs_output_can_begin_data_capture", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsOutputCanBeginDataCapture(IntPtr output, uint flags);

    // Verified: `(obs_output_t *output, uint32_t flags)`.
    [DllImport(Lib, EntryPoint = "obs_output_initialize_encoders", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsOutputInitializeEncoders(IntPtr output, uint flags);

    [DllImport(Lib, EntryPoint = "obs_output_start", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsOutputStart(IntPtr output);

    [DllImport(Lib, EntryPoint = "obs_output_stop", CallingConvention = Cdecl)]
    internal static extern void ObsOutputStop(IntPtr output);

    [DllImport(Lib, EntryPoint = "obs_output_force_stop", CallingConvention = Cdecl)]
    internal static extern void ObsOutputForceStop(IntPtr output);

    [DllImport(Lib, EntryPoint = "obs_output_active", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsOutputActive(IntPtr output);

    [DllImport(Lib, EntryPoint = "obs_output_reconnecting", CallingConvention = Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ObsOutputReconnecting(IntPtr output);

    [DllImport(Lib, EntryPoint = "obs_output_set_service", CallingConvention = Cdecl)]
    internal static extern void ObsOutputSetService(IntPtr output, IntPtr service);

    [DllImport(Lib, EntryPoint = "obs_output_set_video_encoder", CallingConvention = Cdecl)]
    internal static extern void ObsOutputSetVideoEncoder(IntPtr output, IntPtr encoder);

    // Verified: idx is size_t → UIntPtr.
    [DllImport(Lib, EntryPoint = "obs_output_set_audio_encoder", CallingConvention = Cdecl)]
    internal static extern void ObsOutputSetAudioEncoder(IntPtr output, IntPtr encoder, UIntPtr idx);

    [DllImport(Lib, EntryPoint = "obs_output_get_signal_handler", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsOutputGetSignalHandler(IntPtr output);

    // ----- encoders / service (obs.h + obs-data.h @ 32.1.1) -----

    [DllImport(Lib, EntryPoint = "obs_video_encoder_create", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsVideoEncoderCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr settings,
        IntPtr hotkeyData);

    // Verified: mixer_idx is size_t → UIntPtr.
    [DllImport(Lib, EntryPoint = "obs_audio_encoder_create", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsAudioEncoderCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr settings,
        UIntPtr mixerIdx,
        IntPtr hotkeyData);

    [DllImport(Lib, EntryPoint = "obs_encoder_release", CallingConvention = Cdecl)]
    internal static extern void ObsEncoderRelease(IntPtr encoder);

    [DllImport(Lib, EntryPoint = "obs_service_create", CallingConvention = Cdecl)]
    internal static extern IntPtr ObsServiceCreate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr settings,
        IntPtr hotkeyData);

    [DllImport(Lib, EntryPoint = "obs_service_release", CallingConvention = Cdecl)]
    internal static extern void ObsServiceRelease(IntPtr service);

    // ----- signals (callback/signal.h @ 32.1.1, line 69) -----

    [DllImport(Lib, EntryPoint = "signal_handler_signal", CallingConvention = Cdecl)]
    internal static extern void SignalHandlerSignal(
        IntPtr handler,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
        IntPtr parameters);
}

/// <summary>
/// Enum/enum-like values used by the interop. Every number is copied from the
/// libobs headers at tag 32.1.1 and pinned by <c>ObsNativeLayoutTests</c> so a
/// transcription typo fails a test instead of surfacing as a native glitch.
/// </summary>
internal static class ObsNativeConstants
{
    /// <summary>video-io.h line 39 — enum video_format: NONE=0, I420=1, NV12=2.</summary>
    public const int VideoFormatNv12 = 2;

    /// <summary>video-io.h line 107 — enum video_colorspace: VIDEO_CS_DEFAULT = 0.</summary>
    public const int ColorspaceDefault = 0;

    /// <summary>video-io.h line 116 — enum video_range_type: VIDEO_RANGE_DEFAULT = 0.</summary>
    public const int RangeDefault = 0;

    /// <summary>video-io.h line 271 — enum video_scale_type: VIDEO_SCALE_DEFAULT = 0.</summary>
    public const int ScaleTypeDefault = 0;

    /// <summary>audio-io.h line 69 — enum speaker_layout: UNKNOWN=0, MONO=1, STEREO=2.</summary>
    public const int SpeakersStereo = 2;

    /// <summary>graphics.h line 56 — enum gs_color_format: UNKNOWN=0, A8=1, R8=2, RGBA=3, BGRX=4, BGRA=5.</summary>
    public const int ColorFormatRgba = 3;

    /// <summary>Same enum — GS_BGRA is what the OBS frontend passes to obs_display_create (OBSQTDisplay::CreateDisplay).</summary>
    public const int ColorFormatBgra = 5;

    /// <summary>graphics.h line 90 — enum gs_zstencil_format: GS_ZS_NONE = 0.</summary>
    public const int ZStencilNone = 0;

    /// <summary>obs-defs.h line 41 — obs_open_module result codes (0 on success, negative failures).</summary>
    public const int ModuleSuccess = 0;
    public const int ModuleError = -1;
    public const int ModuleFailedToOpen = -2;
    public const int ModuleMissingExports = -3;
    public const int ModuleIncompatibleVersion = -4;
    public const int ModuleHardcodedSkip = -5;
}

/// <summary>
/// struct obs_video_info (obs.h @ 32.1.1, lines 191-219). x64: 56 bytes —
/// the pre-28.0 d3d11_adapters[16] tail does not exist at this version.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ObsVideoInfo
{
    /// <summary>const char *graphics_module — e.g. "libobs-d3d11.dll".</summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)]
    public string GraphicsModule;

    public uint FpsNum;        // offset 8
    public uint FpsDen;        // offset 12
    public uint BaseWidth;     // offset 16
    public uint BaseHeight;    // offset 20
    public uint OutputWidth;   // offset 24
    public uint OutputHeight;  // offset 28
    public int OutputFormat;   // offset 32 — enum video_format (NV12 = 2)
    public uint Adapter;       // offset 36 — uint32_t adapter
    public byte GpuConversion; // offset 40 — C99 bool (1 byte)
    // The C compiler pads 3 bytes here so the next enum lands on a 4-byte
    // boundary; sequential marshaling reproduces that naturally (offset 44),
    // pinned by ObsNativeLayoutTests — do not "fix" it with explicit padding.
    public int Colorspace;     // offset 44 — enum video_colorspace (DEFAULT = 0)
    public int Range;          // offset 48 — enum video_range_type (DEFAULT = 0)
    public int ScaleType;      // offset 52 — enum obs_scale_type (DEFAULT = 0)
}
// sizeof = 56; max alignment 8 already satisfied → no trailing pad.

/// <summary>struct obs_audio_info (obs.h @ 32.1.1, lines 224-227). x64: 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ObsAudioInfo
{
    public uint SamplesPerSec; // offset 0 — 48000
    public int Speakers;       // offset 4 — enum speaker_layout (STEREO = 2)
}

/// <summary>
/// struct gs_init_data (graphics.h @ 32.1.1, lines 492-499) — the argument of
/// obs_display_create at this version. x64: 32 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct ObsInitData
{
    public IntPtr Window;      // offset 0 — struct gs_window { void *hwnd; }
    public uint Cx;            // offset 8
    public uint Cy;            // offset 12
    public uint NumBackbuffers;// offset 16
    public int Format;         // offset 20 — enum gs_color_format (RGBA = 3)
    public int ZsFormat;       // offset 24 — enum gs_zstencil_format (ZS_NONE = 0)
    public uint Adapter;       // offset 28
}

/// <summary>
/// obs_display_add_draw_callback draw: `void (*)(void *param, uint32_t cx, uint32_t cy)`.
/// Public because <see cref="ObsDisplayInterop"/> exposes draw-callback wiring;
/// instances MUST be kept alive by the caller (a collected delegate frees the
/// native thunk while libobs still calls it).
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void ObsDrawCallback(IntPtr param, uint cx, uint cy);

/// <summary>obs_enum_sources callback: `bool (*)(void *param, obs_source_t *source)`. Return true to keep enumerating.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool ObsEnumSourcesCallback(IntPtr param, IntPtr source);

/// <summary>obs_enum_modules callback: `void (*)(void *param, obs_module_t *module)`.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ObsEnumModuleCallback(IntPtr param, IntPtr module);

/// <summary>obs_scene_enum_items callback: `bool (*)(obs_scene_t *scene, obs_sceneitem_t *item, void *param)`. Return true to keep enumerating.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.I1)]
internal delegate bool ObsEnumSceneItemsCallback(IntPtr scene, IntPtr sceneItem, IntPtr param);
