# Embedded OBS Studio (libobs via P/Invoke) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Top-level sidebar **OBS** tab (like Chat) that boots the real OBS engine from the user's installed OBS Studio (`C:\Program Files\obs-studio`), loads their scene collection natively with all plugins, renders a live preview, and streams/records inside VyralBot. Existing JSON import/browse UI becomes a "Collections" sub-tab.

**Engine source (user-confirmed):** libobs loaded from the **installed OBS** at runtime. Never rebuilt, never redistributed. OBS not installed → graceful degraded state; JSON Collections keep working.

## Global Constraints

- **Zero new NuGet packages.** P/Invoke only; `HwndHost` is WPF; `ClientWebSocket` unused here.
- **Secrets** (stream key, tokens from basic.ini [Twitch]) are read into memory only — never logged, never persisted.
- Pin **`<PlatformTarget>x64</PlatformTarget>`** in `VyralBot.UI.csproj` (obs.dll is 64-bit).
- All native calls serialize through one `SemaphoreSlim(1,1)` in the engine facade; engine init runs off the UI thread (`Task.Run`).
- Every engine feature degrades gracefully when OBS is absent or init fails (state + reason surfaced in UI). Existing app behavior/tests (238 green) must stay that way.
- Conventional commits (`feat(obs): …`), TDD for all pure logic (locator, INI parser, profile reader, loader plan builder, struct sizes).

## CRITICAL: verify native layouts against the installed version FIRST (Task 1 step 0)

Struct/enum values below are from libobs `obs.h` (OBS 30.x — installed build links avcodec-61). The implementer MUST:
1. Read the installed version: PowerShell `(Get-Item 'C:\Program Files\obs-studio\bin\64bit\obs64.exe').VersionInfo` (file version ≈ 30.x.y).
2. WebFetch the matching tag's headers from GitHub to confirm every struct/enum below:
   - `https://raw.githubusercontent.com/obsproject/obs-studio/<tag>/libobs/obs.h` (obs_video_info, obs_display_info, function signatures)
   - `https://raw.githubusercontent.com/obsproject/obs-studio/<tag>/libobs/media-io/video-io.h` (video_format/scale/colorspace/range enums)
   - `https://raw.githubusercontent.com/obsproject/obs-studio/<tag>/libobs/graphics/graphics.h` (gs_color_format, gs_zstencil_format)
   - `https://raw.githubusercontent.com/obsproject/obs-studio/<tag>/libobs/obs-config.h`? (not needed) + `UI/obs-app.cpp` for the load-order reference if needed.
3. Document each verified numeric value in a code comment (`// VIDEO_FORMAT_NV12 = 2, verified obs 30.2 header`).

### Struct: obs_video_info (x64, sequential — expect Marshal.SizeOf == 320)

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ObsVideoInfo
{
    public string GraphicsModule;          // pointer: "libobs-d3d11.dll"
    public uint FpsNum, FpsDen;
    public uint BaseWidth, BaseHeight;
    public uint OutputWidth, OutputHeight;
    public int OutputFormat;               // enum video_format  → VIDEO_FORMAT_NV12 = 2 (verify)
    public byte GpuConversion;             // 1
    // 3 bytes padding (C# sequential handles: declare as uint-aligned — use explicit padding field if SizeOf check fails)
    public int Colorspace;                 // enum video_colorspace → VIDEO_CS_DEFAULT = 0
    public int Range;                      // enum video_range_type → VIDEO_RANGE_DEFAULT = 0
    public int ScaleType;                  // enum video_scale_type → VIDEO_SCALE_DEFAULT = 0
    public int GpuAdapter;                 // 0
    public D3D11Adapter D3D11Adapters0;    // inline array — see below; 16 entries × 16 bytes
    // … D3D11Adapters1..15
    public uint D3D11AdapterCount;         // 0
    // trailing 4 bytes pad to 8
}
public struct D3D11Adapter { public IntPtr Device; public long Luid; } // 16 bytes
```
C# has no inline fixed arrays in sequential structs without unsafe — declare **16 consecutive `D3D11Adapter` fields** (or one 256-byte field via `[MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] D3D11Adapter[]` with initializer — ByValArray in a struct passed by-ref to P/Invoke works with blittable element). Final: assert `Marshal.SizeOf<ObsVideoInfo>() == 320` in a unit test. `obs_audio_info` = `{ uint SampleRate; int Speakers /*SPEAKERS_STEREO = 1*/; }` → 8 bytes.

### Struct: obs_display_info (x64 — expect 32)

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct ObsDisplayInfo
{
    public IntPtr Window;      // HWND
    public uint BaseWidth, BaseHeight;
    public int Format;         // gs_color_format → GS_RGBA (verify; expected 2)
    public int ZsFormat;       // gs_zstencil_format → GS_ZS_NONE = 0
    public uint Adapter;       // 0
    public uint Flags;         // 0
}
```

### P/Invoke subset (ObsNative.cs — static class, `[DllImport("obs", CallingConvention = CallingConvention.Cdecl)]`, entry points exact)

Lifecycle/video: `obs_startup(string locale, string moduleConfigPath, IntPtr profiler)→bool`; `obs_shutdown()`; `bool obs_initialized()`; `IntPtr obs_get_version()→string` (returns const char* → `[return: MarshalAs(UnmanagedType.LPStr)] string`); `int obs_reset_video(ref ObsVideoInfo)`; `int obs_reset_audio(ref ObsAudioInfo)` (0 = success for both; OBS_VIDEO_SUCCESS=0); `void obs_add_module_path(string binPath, string dataPath)`; `void obs_load_all_modules()`; `void obs_post_load_modules()`; `void obs_add_log_handler(IntPtr handler)` (callback: `void(int level, IntPtr format, va_list args, IntPtr p)` — va_list can't be formatted; OBS convention for bridges: use `obs_log` fallback? Instead: skip log handler OR use `obs_log_loaded_modules` post-hoc; simplest v1: no log bridge, rely on return codes + module count).
Sources/data: `IntPtr obs_data_create_from_json(string json)`; `void obs_data_release(IntPtr)`; `IntPtr obs_source_create(string id, string name, IntPtr settings, IntPtr hotkeyData)`; `void obs_source_load(IntPtr)`; `void obs_source_release(IntPtr)`; `[return: LPStr] string obs_source_get_name(IntPtr)`; `[return: LPStr] string obs_source_get_id(IntPtr)`; `void obs_set_output_source(UIntPtr channel, IntPtr source)`; `IntPtr obs_get_source_by_name(string)`; `void obs_source_set_volume(IntPtr, float)`; `float obs_source_get_volume`? (not needed); `void obs_source_set_muted(IntPtr, bool)`; `void obs_source_set_audio_mixers(IntPtr, uint)`; `void obs_source_set_monitoring_type(IntPtr, int)`; `void obs_source_set_flags(IntPtr, uint)`; `void obs_source_filter_add(IntPtr parent, IntPtr filter)`; `void obs_source_set_enabled(IntPtr, bool)`.
Scenes/items: `IntPtr obs_scene_from_source(IntPtr source)`; `IntPtr obs_source_get_source?` no — `IntPtr obs_scene_get_source(IntPtr scene)`; delegate+`bool obs_scene_enum_items(IntPtr scene, ObsEnumItemsCallback cb, IntPtr param)` (`bool cb(void* param, IntPtr sceneItem)` — keep callback alive; collect IntPtr items, return true) ; `IntPtr obs_sceneitem_get_source(IntPtr item)`; `[return: LPStr] string obs_sceneitem_get_name?` (use child source name via obs_source_get_name); `void obs_sceneitem_set_visible(IntPtr, bool)`; `void obs_sceneitem_set_locked(IntPtr, bool)`; `bool obs_sceneitem_get_visible`? for current state: `obs_sceneitem_visible(IntPtr)→bool` (verify name).
Display: `IntPtr obs_display_create(ref ObsDisplayInfo)`; `void obs_display_destroy(IntPtr)`; `void obs_display_resize(IntPtr, uint cx, uint cy)`; delegate `void ObsDrawCallback(IntPtr param, uint cx, uint cy)` + `void obs_display_add_draw_callback(IntPtr, ObsDrawCallback, IntPtr param)`; **in callback: `void obs_render_main_texture()`**; `void obs_display_set_background_color(IntPtr, uint)`; `void obs_display_set_enabled(IntPtr, bool)`.
Outputs/encoders/service: `IntPtr obs_output_create(string id, string name, IntPtr settings, IntPtr hotkeys)`; `bool obs_output_can_begin_data_capture(IntPtr, int audioMixers?, int video?)` — verify exact signature `(IntPtr, bool audio, bool video)` in header → use `(IntPtr, int audio, int video)` per `obs_output_can_begin_data_capture(obs_output_t *output, bool audio, bool video)`; `bool obs_output_initialize_encoders(IntPtr)`; `bool obs_output_start(IntPtr)`; `void obs_output_stop(IntPtr[, bool force])` (verify: `obs_output_stop(output)` + `obs_output_force_stop`); `bool obs_output_active(IntPtr)`; `bool obs_output_reconnecting(IntPtr)`; `void obs_output_set_service(IntPtr, IntPtr)`; `void obs_output_set_video_encoder(IntPtr, IntPtr)`; `void obs_output_set_audio_encoder(IntPtr, IntPtr, UIntPtr idx)`; `IntPtr obs_video_encoder_create(string id, string name, IntPtr settings, IntPtr hotkeys)`; `IntPtr obs_audio_encoder_create(string id, string name, IntPtr settings, UIntPtr mixerIdx, IntPtr hotkeys)`; `IntPtr obs_service_create(string id, string name, IntPtr settings, IntPtr hotkeys)`; `IntPtr obs_output_get_signal_handler(IntPtr)`; `void signal_handler_signal(IntPtr handler, string signal, IntPtr params)` (replay-buffer save uses signal `"saved"` — verify against `UI/simple-output.cpp`/`obs-frontend-api` usage).
Enum sources: delegate+`void obs_enum_sources(bool (*cb)(void*, obs_source_t*), void*)` (verify: `obs_enum_sources(bool (*enum_proc)(void *param, obs_source_t *source), void *param)`).
Memory note: every create returns a NEW reference; the engine keeps created IntPtrs in a list for the app lifetime (prevents premature destruction), releases them at shutdown.

## Init sequence (ObsEngine.InitializeAsync, on Task.Run, holding the engine semaphore)

1. `ObsInstallLocator.Locate()` → null → state NotInstalled.
2. kernel32 `SetDllDirectoryW(binDir)` + prepend binDir to `PATH` env (process env, so dependent DLLs like avcodec resolve) + `AddDllDirectoryW`.
3. `obs_startup("en-US", null, 0)` → false → Failed("obs_startup failed").
4. `obs_add_module_path("<obs>\obs-plugins\64bit", "<obs>\data\obs-plugins")`; if `%APPDATA%\obs-studio\obs-plugins\64bit` exists add that path too with its data dir; `obs_load_all_modules()`; `obs_post_load_modules()`.
5. `obs_reset_video(ref ovi)` with profile [Video] values (BaseCX/BaseCY, OutputCX/OutputCY, FPSType/FPSCommon "60"→60/1, or FPSNum/FPSDen; defaults 1920×1080, 60fps); `GraphicsModule="libobs-d3d11.dll"`, `OutputFormat=NV12`, `GpuConversion=1`, defaults 0 elsewhere; non-zero return → Failed(reason: 0 ok, else mapped message).
6. `obs_reset_audio(ref {48000, SPEAKERS_STEREO})`.
7. State = Ready; `Version = obs_get_version()`.

`--obs-selftest` CLI mode: `App.OnStartup` checks args; runs init → loads largest `%APPDATA%\obs-studio\basic\scenes\*.json` via loader → writes a report (version, module/plugin count via `obs_get_modules`? fallback: source count created, missing list, display skip) → `obs_shutdown()` → `Environment.Exit(0/1)`. Wire BEFORE main window creation; used by the verification wave.

## Scene collection loader (two-pass, mirrors UI/obs-app.cpp LoadData)

Plan builder (PURE, testable — no native): input = collection JSON text; output = `ObsLoadPlan`:
- `Sources: List<ObsSourcePlan>` in file order `{ Name, Id, SettingsJson (raw "settings" object text — re-serialize the JsonElement), HotkeysJson ("hotkeys" obj or null), Volume(float "volume" default 1), Muted("muted" def false), Mixers(uint "mixers" def 0? frontend default 0xFF? use stored value verbatim; missing → skip call), Monitoring("monitoring_type" def 0), Flags("flags"), DeinterlaceMode/FieldOrder, Filters: List<ObsFilterPlan{Id, Name, SettingsJson, Enabled("enabled" def true)}>, IsAudioDevice }` where IsAudioDevice = root keys DesktopAudioDevice1/DesktopAudioDevice2/AuxAudioDevice1..4 (separate top-level single-source entries; channel mapping: desktop1→1, desktop2→2, aux1..4→3..6).
- `CurrentSceneName` ("current_scene"), `SceneOrder` ("scene_order" names).
- Tests against existing fixture + new `obs-scene-collection.filters.json` fixture (a source with 2 filters incl. one disabled).

Native executor (ObsSceneCollectionLoader):
1. Pass 1 create: for each source plan + audio-device plans: settings = `obs_data_create_from_json(SettingsJson ?? "{}")`; hotkeys likewise (or IntPtr.Zero); `ptr = obs_source_create(Id, Name, settings, hotkeys)`; ptr==0 → MissingSources.Add(name); store in `Dictionary<string, IntPtr> byName`. (No releases here — engine holds refs.)
2. Pass 2 per created source: apply volume/muted/mixers/monitoring/flags/deinterlace; for each filter plan: create filter source → `obs_source_set_enabled` if disabled → `obs_source_filter_add(parent, filter)`.
3. Pass 3: `obs_source_load(ptr)` for every created source (scenes resolve their items by name here — item transforms come from the scene source's settings, untouched).
4. `obs_set_output_source(1..6, audioDevicePtrs)`; `obs_set_output_source(0, byName[CurrentSceneName])` (fallback: first scene from SceneOrder; fallback: any "scene"-id source).
5. Return report: created count, missing names, current scene.

## Preview (VyralBot.UI\Controls\ObsPreviewHost.cs)

`ObsPreviewHost : HwndHost`: `BuildWindowCore(HandleRef)` → `CreateWindowExW(WS_CHILD|WS_VISIBLE, "STATIC", ...)`; on Loaded + when engine Ready → engine.AttachPreview(hwnd): `obs_display_create(ref info {Window=hwnd, BaseWidth=actual px, Format=GS_RGBA, ZsFormat=GS_ZS_NONE})` + `obs_display_add_draw_callback(disp, (p,cx,cy) => obs_render_main_texture(), 0)`; `OnRenderSizeChanged` → `obs_display_resize`; `Dispose` → remove callback + `obs_display_destroy`. Callback delegate stored as field (GC pin). Background color dark. Dispatcher marshaling for engine events.

## Outputs (ObsOutputService)

From `ObsProfileSettings` (Task 2): stream = service `rtmp_custom` `{server, key}` + output `rtmp_output`; video encoder = profile Encoder id fallback "obs_x264", settings `{bitrate: [AdvOut].Track1Bitrate or [SimpleOutput].VBitrate default 2500, keyint_sec: 2, use_bufsize? skip}`; audio encoder `ffmpeg_aac` `{bitrate: Track1Bitrate(audio) default 160}` mixer 0. Start: `obs_output_can_begin_data_capture(out, true, true)` → `obs_output_initialize_encoders` → `obs_output_start`. Record output `ffmpeg_muxer` settings `{path: [AdvOut].RecFilePath or [Output]/[SimpleOutput].FilePath fallback %USERPROFILE%\Videos, format: RecFormat2 fallback mkv}` + RecEncoder fallback = stream encoder. State polled 1 Hz by the UI (`obs_output_active/obs_output_reconnecting`). Replay buffer output `replay_buffer` with same record path settings; SaveReplay = `signal_handler_signal(obs_output_get_signal_handler(out), "saved", 0)` (verify name). Secrets: server/key set via obs_data JSON built in memory; `ToString` of profile settings MUST mask key/tokens (tested).

## UI + wiring (Wave 4)

- `Views\ObsStudioView.xaml` + `ViewModels\ObsStudioViewModel.cs`: left = Scenes list + Sources list of current scene (show/hide checkbox via `obs_sceneitem_set_visible`), center = ObsPreviewHost, top/right = Stream ▶/■ + Record + Replay Save buttons (states from polling), status bar (engine state, version, base/output res, fps via `obs_get_average_frame_time_ns`? verify export — fallback: omit fps), missing-sources warning bar after load.
- OBS section sub-tabs **Studio | Collections**: simplest = ObsStudioView hosts a top `ToggleButton` pair; Collections tab embeds the existing `ObsView` content (wrap existing view as `<views:ObsView/>` inside a tab panel — it stays a working UserControl).
- Sidebar: top-level OBS button under Chat (copy Chat button block, `NavigateToObsCommand`, PackIcon `Video`); REMOVE the Stream Tools OBS entry (user asked for "a tab like chat"). DataTemplate stays.
- `NavigateToObsAsync`: on first open per run → kick `InitializeAsync` (fire, UI shows progress), load last-used collection (config `Obs.LastCollectionPath`, category "Obs") via loader automatically when Ready.
- App exit: `ObsEngine.ShutdownAsync()` before other teardown.
- DI: `ObsInstallLocator`, `ObsEngine`, `ObsStudioService`, `ObsOutputService` singletons in Integrations; VM registrations in App.xaml.cs.

## Tasks

1. `ObsNative.cs` + struct-size/enum tests (after header verification step 0) + `VyralBot.UI.csproj` x64 pin.
2. `ObsInstallLocator` + INI parser + `ObsProfileReader` + fixtures + tests (x64 pin lands in Task 1).
3. `ObsEngine` (init sequence, state machine, selftest CLI wiring in App.OnStartup).
4. `ObsSceneCollectionLoader` (plan builder + executor + fixtures/tests).
5. `ObsPreviewHost` + `ObsStudioService` (scenes/sources/items, attach/detach preview).
6. `ObsOutputService` (stream/record/replay from profile).
7. `ObsStudioViewModel`/`ObsStudioView` + sub-tabs + sidebar promotion + nav/DI/exit wiring.
8. Stats/polish + `Obs.LastCollectionPath` + auto-init flag.
9. Verification wave: build + tests; launch app → screenshot Studio tab (preview must show the real collection); `--obs-selftest`; code-review agent; repairs.

Waves: [1,2] parallel → [3,4] parallel → 5 → 6 → [7,8] parallel → 9. (5 and 6 both touch ObsStudioService-adjacent files only where 6 extends the facade — serialize.)

Build/test commands: `dotnet build VyralBot\src\VyralBot.sln`; `dotnet test tests\VyralBot.UI.Tests\VyralBot.UI.Tests.csproj`. Selftest: `VyralBot.UI.exe --obs-selftest`.
