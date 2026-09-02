# OBS Integration Master Plan — VyralBot

> **Goal:** Embed libobs directly into VyralBot to create the first open-source all-in-one streaming + chat + bot application. Users' existing OBS plugins, scenes, and profiles load automatically.

---

## Executive Assessment

### What the OBS-addition.md Got Right
✅ **libobs embedding** — The correct approach. Streamlabs proved this model works.  
✅ **C++/CLI wrapper** — Right bridging technology for .NET ↔ C interop.  
✅ **D3D11 shared texture** — The zero-copy GPU rendering path for WPF preview.  
✅ **Plugin compatibility** — Plugins talk to libobs, not the Qt UI. They will load.  
✅ **Config import** — Reading `%APPDATA%/obs-studio/` is the right user onboarding path.  

### What Needs Reconsideration
⚠️ **C++/CLI is correct but there's an alternative** — A pure P/Invoke wrapper (like [obs-studio-node](https://github.com/nicegram/obs-studio-node) or [obs-websocket](https://github.com/obsproject/obs-websocket) approach) could work too. C++/CLI is more performant but harder to maintain. **Recommendation: Start with P/Invoke for prototyping, graduate to C++/CLI for performance-critical paths.**  
⚠️ **D3DImage → DXGI shared handle path** — The original doc shows `D3DResourceType.IDirect3DSurface9` which is the legacy DX9 interop. Modern WPF + D3D11 uses `D3D11_BIND_SHARED` + ` IDXGIKeyedMutex` or `SharpDX.DXGI` + `D3D11Image`. This is a critical technical detail.  
⚠️ **GPL-2.0 implications** — libobs is GPL-2.0. If you link against it, VyralBot must also be GPL-2.0. You've confirmed this is acceptable. Just ensure the license file is correct.  
⚠️ **Binary distribution** — You need to ship ~200MB of OBS runtime DLLs. This is a packaging/distribution concern that needs planning.  

### Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| C++ interop complexity | High | Start with P/Invoke, prototype incrementally |
| OBS version coupling | Medium | Pin to a specific OBS release, test before upgrading |
| Plugin compatibility edge cases | Medium | Test top 20 plugins, document known issues |
| Binary size bloat | Medium | Offer OBS as optional download component |
| GPU thread safety (libobs ↔ WPF) | High | All libobs calls must be on a dedicated thread |
| .NET 10 + C++/CLI compatibility | Medium | Test early, C++/CLI support may need workarounds |

---

## Phase Overview

```
Phase 0: Foundation & Research ............ (1-2 weeks)
Phase 1: Core libobs Wrapper .............. (2-3 weeks)  
Phase 2: Video Preview Rendering .......... (1-2 weeks)
Phase 3: Scene & Source Management ........ (2-3 weeks)
Phase 4: Streaming & Recording ............ (1-2 weeks)
Phase 5: Audio System ..................... (1-2 weeks)
Phase 6: UI Panels & Property Editors ..... (3-4 weeks)
Phase 7: Config Import & Plugin Discovery . (1-2 weeks)
Phase 8: Polish, Testing & Distribution ... (2-3 weeks)
──────────────────────────────────────────────────────
Total Estimated: 14-23 weeks
```

---

## Phase 0: Foundation & Research (1-2 weeks)

### Objectives
- Set up build infrastructure for native interop
- Prototype libobs initialization from C#
- Validate GPU rendering path works with WPF
- Establish project structure

### Tasks

#### 0.1 Create `VyralBot.OBS` Project
```
src/VyralBot.OBS/
├── VyralBot.OBS.csproj          (net10.0-windows, AllowUnsafeBlocks)
├── Native/
│   ├── ObsNativeMethods.cs      (P/Invoke declarations for obs.h)
│   ├── ObsStructs.cs            (struct mappings from obs.h)
│   └── ObsEnums.cs              (enum mappings from obs.h)
├── Services/
│   ├── IObsService.cs           (primary service interface)
│   ├── ObsService.cs            (implementation, owns libobs lifecycle)
│   └── ObsModuleService.cs      (plugin loading/discovery)
├── Models/
│   ├── ObsSourceInfo.cs
│   ├── ObsSceneInfo.cs
│   └── ObsOutputSettings.cs
└── Interop/
    └── D3D11Interop.cs          (GPU texture sharing helpers)
```

**Key decision:** Use P/Invoke first, add C++/CLI later only if needed.
- P/Invoke is pure C#, works with any .NET version
- Easy to debug, no mixed-mode assembly issues
- Performance is fine for control-plane calls (init, scene management, etc.)
- Only the video preview render loop needs high perf, which can use `unsafe` + pointers

#### 0.2 Define Core P/Invoke Surface
```csharp
// VyralBot.OBS/Native/ObsNativeMethods.cs — Start with these ~30 functions
// (Don't wrap everything — wrap what you need incrementally)

static class ObsNative {
    // Lifecycle
    [DllImport("obs")] static extern bool obs_startup(string locale, string module_config_path, string profiler_path);
    [DllImport("obs")] static extern void obs_shutdown();
    [DllImport("obs")] static extern bool obs_initialized();
    
    // Video
    [DllImport("obs")] static extern int obs_reset_video(ref obs_video_info ovi);
    [DllImport("obs")] static extern int obs_reset_audio(ref obs_audio_info oai);
    
    // Modules/Plugins
    [DllImport("obs")] static extern void obs_add_module_path(string bin_path, string data_path);
    [DllImport("obs")] static extern void obs_load_all_modules();
    [DllImport("DllImport("obs")] static extern void obs_post_load_modules();
    
    // Scenes
    [DllImport("obs")] static extern IntPtr obs_scene_create(string name);
    [DllImport("obs")] static extern IntPtr obs_scene_add(IntPtr scene, IntPtr source);
    [DllImport("obs")] static extern IntPtr obs_scene_get_source(IntPtr scene);
    
    // Sources
    [DllImport("obs")] static extern IntPtr obs_source_create(string id, string name, IntPtr settings, IntPtr hotkeys);
    [DllImport("obs")] static extern void obs_source_release(IntPtr source);
    [DllImport("obs")] static extern IntPtr obs_source_get_settings(IntPtr source);
    
    // Properties (for dynamic UI)
    [DllImport("obs")] static extern IntPtr obs_source_properties(string id);
    [DllImport("obs")] static extern IntPtr obs_properties_first(IntPtr props);
    
    // Output
    [DllImport("obs")] static extern IntPtr obs_output_create(string id, string name, IntPtr settings, IntPtr hotkeys);
    [DllImport("obs")] static extern bool obs_output_start(IntPtr output);
    [DllImport("obs")] static extern void obs_output_stop(IntPtr output);
    
    // Display/Render
    [DllImport("obs")] static extern IntPtr obs_display_create(ref obs_display_info info);
    [DllImport("obs")] static extern void obs_display_set_size(IntPtr display, uint cx, uint cy);
}
```

#### 0.3 Prototype: "Hello libobs"
Create a console test that:
1. Calls `obs_startup()`
2. Sets up video (D3D11, 1920x1080)
3. Sets up audio (48kHz, stereo)
4. Loads modules from OBS install path
5. Creates a test scene + color source
6. Confirms rendering pipeline works
7. Shuts down cleanly

**Acceptance Criteria:** No crashes, module count > 0, at least 1 source type enumerated.

#### 0.4 Bundle OBS Binaries
- Download OBS Studio (portable zip)
- Extract `obs-plugins/64bit/`, `data/`, and core DLLs
- Create a build script that copies them to VyralBot output
- Consider: ship as a separate download? Or embed in installer?

```xml
<!-- VyralBot.OBS.csproj — reference native DLLs -->
<ItemGroup>
  <None Include="..\..\Libs\obs-studio\bin\64bit\*.dll" CopyToOutputDirectory="PreserveNewest" />
  <None Include="..\..\Libs\obs-studio\obs-plugins\64bit\**\*" LinkBase="obs-plugins\64bit" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

#### 0.5 DI Registration Pattern
Follow existing VyralBot patterns from `App.xaml.cs`:
```csharp
// In App.xaml.cs ConfigureServices:
services.AddSingleton<IObsService, ObsService>();
services.AddSingleton<ObsSceneService>();
services.AddSingleton<ObsSourceService>();
services.AddSingleton<ObsOutputService>();
services.AddSingleton<ObsAudioService>();

// ViewModels
services.AddSingleton<ObsStudioViewModel>();
services.AddSingleton<ObsScenesViewModel>();
services.AddSingleton<ObsSourcesViewModel>();
services.AddSingleton<ObsAudioMixerViewModel>();
services.AddSingleton<ObsStreamSettingsViewModel>();
```

---

## Phase 1: Core libobs Wrapper (2-3 weeks)

### Objectives
- Full lifecycle management (init → run → shutdown)
- Thread model: libobs runs on its own dedicated thread
- Scene/source CRUD operations
- Plugin enumeration

### Tasks

#### 1.1 ObsService — Lifecycle Manager
```
IObsService
├── InitializeAsync()           — Start libobs on dedicated thread
├── ShutdownAsync()             — Clean teardown
├── IsInitialized               — Observable bool
├── ModuleCount                 — Number of loaded plugins
├── EnumerateSourceTypes()      — All available source types (from plugins)
├── EnumerateEncoderTypes()     — Available encoders (NVENC, x264, etc.)
├── EnumerateOutputTypes()      — Available outputs (RTMP, file, etc.)
└── EnumerateTransitionTypes()  — Available transitions
```

**Critical: Thread Model**
libobs requires all API calls from the same thread that called `obs_startup()`. Use a dedicated `StaTaskScheduler` or custom message loop:

```csharp
public class ObsService : IObsService, IDisposable
{
    private Thread? _obsThread;
    private readonly BlockingCollection<Action> _queue = new();
    
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> action) =>
        Task.Factory.StartNew(action, CancellationToken.None, 
            TaskCreationOptions.None, _obsScheduler);
    
    public Task InvokeAsync(Action action) =>
        Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.None, _obsScheduler);
}
```

#### 1.2 Scene Management
```
ObsSceneService
├── CreateScene(name) → ObsScene
├── RemoveScene(id)
├── GetScenes() → ObservableCollection<ObsSceneInfo>
├── SetCurrentScene(id)
├── DuplicateScene(sourceId, newName)
└── ReorderScenes(orderedIds)
```

#### 1.3 Source Management  
```
ObsSourceService
├── AddSource(sceneId, sourceTypeId, name, settings) → ObsSource
├── RemoveSource(sourceId)
├── UpdateSourceSettings(sourceId, newSettings)
├── SetSourceVisibility(sourceId, visible)
├── SetSourceTransform(sourceId, transform)
├── GetSourceProperties(sourceTypeId) → ObsPropertyDef[]  // For dynamic UI
├── MoveSourceOrder(sourceId, newOrder)
├── DuplicateSource(sourceId, newName)
└── GetSourcesInScene(sceneId) → List<ObsSourceInfo>
```

#### 1.4 Module/Plugin Enumeration
```
ObsModuleService
├── LoadedPlugins → List<ObsPluginInfo>
├── GetSourceTypes() → List<(string id, string name, ObsSourceType type)>
├── GetFilterTypes() → List<(string id, string name)>
├── GetEncoderTypes() → List<(string id, string name, ObsEncoderCodec codec)>
└── GetOutputTypes() → List<(string id, string name)>
```

#### 1.5 Unit Tests
- Mock libobs P/Invoke calls
- Test ObsService lifecycle (init/shutdown)
- Test scene/source CRUD
- Test thread marshaling

---

## Phase 2: Video Preview Rendering (1-2 weeks)

### Objectives
- Render libobs output in a WPF control
- Zero-copy GPU texture sharing via D3D11
- Support resize, fullscreen preview, and studio mode (dual preview)

### Architecture
```
libobs renders → D3D11 texture → DXGI shared handle → WPF D3D11Image → UI
```

### Tasks

#### 2.1 D3D11 Shared Texture Bridge
```csharp
// VyralBot.OBS/Interop/D3D11Interop.cs
public unsafe class ObsPreviewRenderer : IDisposable
{
    private ID3D11Device* _device;           // Shared D3D11 device
    private ID3D11Texture2D* _sharedTexture; // GPU texture libobs renders into
    private IntPtr _sharedHandle;            // Cross-process/Cross-thread handle
    
    // Called from libobs render callback
    public void OnFrameRendered(IntPtr data);
    
    // Returns the shared handle for WPF consumption
    public IntPtr GetSharedHandle() => _sharedHandle;
}
```

#### 2.2 WPF Preview Control
```csharp
// VyralBot.UI/Controls/ObsPreviewControl.cs
public class ObsPreviewControl : FrameworkElement
{
    // Uses D3D11Image (SharpDX or custom) to render shared texture
    // Supports resize, aspect ratio lock, click-to-focus
    
    public static readonly DependencyProperty SourceProperty = ...;
    public ObsPreviewRenderer Source { get; set; }
    
    protected override void OnRender(DrawingContext drawingContext);
}
```

#### 2.3 NuGet Dependencies
```xml
<PackageReference Include="SharpDX.D3D11" Version="4.2.0" />
<PackageReference Include="SharpDX.DXGI" Version="4.2.0" />
<PackageReference Include="SharpDX.Direct3D11" Version="4.2.0" />
```
Or alternatively, use the newer `Vortice.D3D11` which supports .NET 8+ natively.

#### 2.4 Acceptance Criteria
- [ ] libobs renders to a WPF control with < 1 frame of latency
- [ ] Preview resizes correctly when the window resizes
- [ ] No memory leaks over 1 hour of rendering
- [ ] GPU memory usage stays reasonable (< 500MB for 1080p preview)

---

## Phase 3: Scene & Source Management UI (2-3 weeks)

### Objectives
- Scene list with add/remove/reorder
- Source tree with visibility, lock, drag-to-reorder
- Source transforms (position, scale, rotation, crop)
- Right-click context menus matching OBS behavior

### New Views & ViewModels

#### 3.1 Scene List Panel
```xml
<!-- Left panel in OBS Studio view -->
<ListBox ItemsSource="{Binding Scenes}" SelectedItem="{Binding CurrentScene}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <materialDesign:PackIcon Kind="Filmstrip"/>
                <TextBlock Text="{Binding Name}"/>
                <!-- Active indicator, remove button -->
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

#### 3.2 Source Tree Panel
```xml
<!-- Below scene list -->
<TreeView ItemsSource="{Binding CurrentScene.Sources}">
    <!-- Visibility toggle, lock icon, source name, type icon -->
    <!-- Drag/drop reorder via GongSolutions.WPF.DragDrop or custom -->
</TreeView>
```

#### 3.3 Source Transform Editor
- Position (X, Y)
- Scale (X%, Y%)
- Rotation (degrees)
- Crop (Top, Bottom, Left, Right)
- Reset to defaults
- Visual drag handles on preview (stretch goal)

#### 3.4 Integration with TriggerEngine
Connect OBS scene/source changes to VyralBot's existing event system:
```csharp
// New events in VyralBot.Core
public record ObsSceneChangedEvent : IEvent { string SceneName; }
public record ObsSourceAddedEvent : IEvent { string SourceName; string SourceType; }
public record ObsStreamStartedEvent : IEvent { }
public record ObsStreamStoppedEvent : IEvent { }
```

---

## Phase 4: Streaming & Recording (1-2 weeks)

### Objectives
- Start/stop stream (RTMP)
- Start/stop recording
- Encoder settings UI
- Stream health monitoring

### Tasks

#### 4.1 Output Service
```
ObsOutputService
├── StartStreamingAsync(profile)     — RTMP output
├── StopStreamingAsync()
├── StartRecordingAsync(settings)    — File output (MKV/MP4)
├── StopRecordingAsync()
├── IsStreaming → bool
├── IsRecording → bool
├── StreamTime → TimeSpan
├── DroppedFrames → int
├── Bitrate → int (live)
└── TotalBytes → long
```

#### 4.2 Encoder Settings UI
- Simple mode: Quality preset (Low/Medium/High/Very High)
- Advanced mode: Full encoder property grid
- Support NVENC, QSV, x264, Apple VT (if applicable)

#### 4.3 Stream Key Management
- Store in VyralBot's profile system (existing `ProfileService`)
- Import from OBS profiles on first setup
- Support for multiple stream platforms simultaneously

#### 4.4 Stream Health Dashboard
- Bitrate graph (live)
- Dropped frames counter
- CPU/GPU usage
- Encode latency
- Skipped frames

---

## Phase 5: Audio System (1-2 weeks)

### Objectives
- Audio source enumeration (desktop, mic, aux)
- Per-source volume + mute controls
- VU meter visualization
- Audio monitoring (listen-only)
- Audio filters

### Tasks

#### 5.1 Audio Mixer Service
```
ObsAudioService
├── GetAudioSources() → List<ObsAudioSource>
├── SetVolume(sourceId, float)      // 0.0 - 1.0
├── SetMuted(sourceId, bool)
├── GetVolumeMeter(sourceId) → IObservable<float>  // ~60fps VU data
├── AddAudioFilter(sourceId, filterTypeId)
└── RemoveAudioFilter(sourceId, filterId)
```

#### 5.2 VU Meter Control
Custom WPF control with real-time audio level bars:
- Peak hold indicator
- Green/yellow/red zones
- Per-channel (L/R) display

#### 5.3 Audio Sources Panel
- Desktop Audio
- Mic/Aux
- Custom audio captures
- Per-source volume slider + mute toggle + VU meter

---

## Phase 6: UI Panels & Dynamic Property Editors (3-4 weeks)

### Objectives
- Dynamic property editor that adapts to any OBS source/filter type
- Source properties dialog (matching OBS's properties dialog)
- Filter management UI
- Transitions UI
- Studio mode (dual preview)

### Tasks

#### 6.1 Dynamic Property System
This is the hardest UI challenge. Every OBS source type declares its own properties via `obs_source_properties()`. Your UI must dynamically generate editors:

```csharp
// obs_properties returns a linked list of property definitions
public enum ObsPropertyType { Int, Float, Text, Bool, List, Color, Font, Path, Button, EditableList }

public class ObsPropertyEditorGenerator
{
    public FrameworkElement CreateEditor(ObsPropertyDef property)
    {
        return property.Type switch
        {
            ObsPropertyType.Int => CreateIntEditor(property),
            ObsPropertyType.Float => CreateSliderEditor(property),
            ObsPropertyType.Text => CreateTextBox(property),
            ObsPropertyType.Bool => CreateToggle(property),
            ObsPropertyType.List => CreateComboBox(property),
            ObsPropertyType.Color => CreateColorPicker(property),
            ObsPropertyType.Font => CreateFontPicker(property),
            ObsPropertyType.Path => CreatePathBrowser(property),
            ObsPropertyType.EditableList => CreateEditableList(property),
            _ => new TextBlock { Text = $"Unsupported: {property.Type}" }
        };
    }
}
```

Use Material Design styling for all editors to match VyralBot's RedSlate theme.

#### 6.2 Source Properties Dialog
```
ObsSourcePropertiesDialog
├── Window with property editors generated dynamically
├── OK/Cancel/Apply buttons
├── Live preview (changes reflect immediately)
└── Defer update until Apply (or live, depending on source)
```

#### 6.3 Filter Stack UI
```
ObsFiltersPanel
├── List of applied filters (drag-reorderable)
├── Add filter button → dropdown of available filter types
├── Each filter: name + visibility toggle + edit button
└── Edit → opens property editor for that filter type
```

#### 6.4 Transitions UI
- Transition type dropdown (Cut, Fade, Stinger, etc.)
- Duration slider (ms)
- Per-scene transition override
- Studio mode: Preview ↔ Program swap button

#### 6.5 Studio Mode
- Two preview panels side-by-side
- "Transition" button swaps Preview → Program
- Only available when Studio Mode is enabled

---

## Phase 7: Config Import & Plugin Discovery (1-2 weeks)

### Objectives
- One-click import of user's existing OBS setup
- Automatic plugin detection
- Profile migration
- Scene collection loading

### Tasks

#### 7.1 OBS Config Importer
```csharp
public class ObsConfigImporter
{
    // Auto-detect OBS installation
    public bool IsOBSInstalled { get; }
    public string OBSConfigPath { get; }  // %APPDATA%/obs-studio
    
    // Import everything
    public ObsImportResult ImportAll();
    
    // Granular imports
    public List<ObsProfile> ImportProfiles();
    public List<ObsSceneCollection> ImportSceneCollections();
    public List<string> DiscoverInstalledPlugins();
    
    // First-run wizard
    public ObsSetupWizard CreateWizard();
}
```

#### 7.2 First-Run OBS Setup Wizard
A step-by-step dialog:
1. **Detect** — "We found OBS Studio installed! Import your setup?"
2. **Choose** — Select which profiles/scene collections to import
3. **Review** — Show what was found (scenes, sources, plugins)
4. **Import** — Copy configs, validate, resolve conflicts
5. **Complete** — Show imported items, offer to launch a test stream

#### 7.3 Scene Collection Loading
Parse OBS's scene JSON format:
```json
{
  "current_scene": "Gaming",
  "scenes": [
    {
      "name": "Gaming",
      "sources": [
        { "id": "game_capture", "type": "monitor_capture", "settings": {...} },
        { "id": "webcam", "type": "dshow_input", "settings": {...} }
      ]
    }
  ]
}
```

#### 7.4 Plugin Compatibility Checker
- Enumerate installed OBS plugins
- Cross-reference against tested/known-working list
- Warn about incompatible plugins
- Option to disable problematic plugins

---

## Phase 8: Polish, Testing & Distribution (2-3 weeks)

### Objectives
- Full integration testing
- Performance optimization
- Distribution packaging
- Documentation

### Tasks

#### 8.1 Integration with VyralBot Sidebar
Add "OBS Studio" section to the sidebar navigation (between "Audio & Alerts" and "Monitoring"):

```xml
<!-- In MainWindow.xaml sidebar -->
<materialDesign:PackIcon Kind="Video" Width="20" Height="20"/>
<TextBlock Text="OBS Studio" Margin="12,0,0,0"/>

<!-- Sub-items (collapsible) -->
├── Scenes & Sources
├── Audio Mixer
├── Stream Settings
├── Transitions
└── Plugin Manager
```

#### 8.2 EventBus Integration
Connect OBS events to VyralBot's trigger/action system:
```csharp
// Users can create triggers like:
// "When OBS stream starts → Send chat message: 'We're LIVE!'"
// "When raid received → Switch to 'Raid Welcome' scene"

events.Subscribe<ObsStreamStartedEvent>(e => { /* trigger actions */ });
events.Subscribe<ObsSceneChangedEvent>(e => { /* trigger actions */ });
```

#### 8.3 Performance Targets
- Preview render: ≤ 1 frame latency vs. native OBS
- Scene switch: < 200ms
- Memory overhead vs. OBS standalone: < 100MB additional
- CPU overhead: < 5% additional on modern hardware

#### 8.4 Distribution Packaging
```
VyralBot-Installer/
├── VyralBot.exe + managed DLLs
├── obs-studio/          (optional component)
│   ├── bin/64bit/       (libobs.dll, obs.dll, etc.)
│   ├── obs-plugins/     (plugin DLLs)
│   └── data/            (plugin data files)
├── runtime/             (.NET 10 runtime if self-contained)
└── VCRedist/            (C++ runtime if needed)
```

Options:
1. **Full bundle** — OBS runtime included (~200MB additional)
2. **Lightweight** — Detects existing OBS installation, uses its binaries
3. **Hybrid** — Core libobs bundled, plugins loaded from user's OBS install

**Recommendation: Option 3** — This gives the best user experience while keeping download size reasonable.

#### 8.5 Testing Matrix
| Scenario | Must Test |
|----------|-----------|
| Fresh install (no OBS) | libobs init with bundled binaries |
| Existing OBS installed | Import and dual-use |
| Top 10 OBS plugins | Load and function correctly |
| NVENC encoding | Stream at 1080p60 6000kbps |
| x264 encoding | Stream at 1080p30 4000kbps |
| Browser source | CEF-based browser source renders |
| Scene collections | Load complex 20+ scene setups |
| Audio routing | Desktop + mic capture |
| Studio mode | Dual preview + transitions |
| Long-running stability | 8-hour stream no crash/leak |

---

## Project Structure After Completion

```
src/
├── VyralBot.OBS/                    ← NEW PROJECT
│   ├── VyralBot.OBS.csproj
│   ├── Native/
│   │   ├── ObsNativeMethods.cs      (P/Invoke declarations)
│   │   ├── ObsStructs.cs
│   │   └── ObsEnums.cs
│   ├── Services/
│   │   ├── IObsService.cs
│   │   ├── ObsService.cs            (lifecycle, thread management)
│   │   ├── ObsSceneService.cs
│   │   ├── ObsSourceService.cs
│   │   ├── ObsOutputService.cs
│   │   ├── ObsAudioService.cs
│   │   ├── ObsModuleService.cs
│   │   └── ObsConfigImporter.cs
│   ├── Models/
│   │   ├── ObsSceneInfo.cs
│   │   ├── ObsSourceInfo.cs
│   │   ├── ObsPropertyDef.cs
│   │   ├── ObsAudioSource.cs
│   │   └── ObsStreamStats.cs
│   ├── Interop/
│   │   ├── D3D11Interop.cs
│   │   └── SharedTextureBridge.cs
│   └── Events/
│       ├── ObsStreamStartedEvent.cs
│       ├── ObsStreamStoppedEvent.cs
│       ├── ObsSceneChangedEvent.cs
│       └── ObsSourceEvent.cs
│
├── VyralBot.UI/
│   ├── Controls/
│   │   ├── ObsPreviewControl.cs     (D3D11 render surface)
│   │   ├── ObsVuMeter.cs            (audio VU meter)
│   │   └── ObsPropertyEditor.cs     (dynamic property grid)
│   ├── Views/
│   │   ├── ObsStudioView.xaml       (main OBS view)
│   │   ├── ObsScenesView.xaml
│   │   ├── ObsSourcesView.xaml
│   │   ├── ObsAudioMixerView.xaml
│   │   ├── ObsStreamSettingsView.xaml
│   │   ├── ObsTransitionView.xaml
│   │   ├── ObsPluginManagerView.xaml
│   │   └── ObsSetupWizardView.xaml
│   └── ViewModels/
│       ├── ObsStudioViewModel.cs
│       ├── ObsScenesViewModel.cs
│       ├── ObsSourcesViewModel.cs
│       ├── ObsAudioMixerViewModel.cs
│       ├── ObsStreamSettingsViewModel.cs
│       └── ObsPluginManagerViewModel.cs
│
├── VyralBot.Core/
│   └── (existing — add OBS events to EventBus)
│
└── VyralBot.sln                     (add VyralBot.OBS project)
```

---

## Dependency Graph

```
VyralBot.UI
  ├── VyralBot.OBS (NEW)
  │     └── libobs.dll (native, bundled or system)
  │     └── SharpDX.D3D11 / Vortice.D3D11
  ├── VyralBot.Core (OBS events added)
  ├── VyralBot.Data
  ├── VyralBot.Integrations
  └── VyralBot.Api
```

---

## MVP vs. Full Feature Set

### MVP (Phases 0-4) — "It streams from VyralBot"
- libobs initialized and running
- Scene/source creation and management
- Video preview in WPF
- Start/stop stream to RTMP
- Start/stop recording
- Import existing OBS scene collection

### Full Feature (Phases 5-8) — "It replaces OBS"
- Audio mixer with VU meters
- Dynamic property editors for all source types
- Plugin manager
- Studio mode
- Full config import wizard
- Trigger/action integration
- Stream health dashboard

---

## Open Questions to Resolve Before Starting

1. **Which OBS version to target?** Recommend pinning to OBS 31.x stable.
2. **SharpDX vs. Vortice?** SharpDX is archived but battle-tested. Vortice is active but newer. Recommend **Vortice** since it supports .NET 10 natively.
3. **OBS binary distribution strategy?** Recommend hybrid approach (core bundled, plugins from user's install).
4. **C++/CLI timing?** Recommend starting pure P/Invoke, only adding C++/CLI wrapper if profiling shows overhead.
5. **First platform for testing?** Recommend Windows + NVENC since that's the most common streaming setup.

---

## Next Steps

1. **Install OBS Studio** (if not already) for testing reference
2. **Download OBS Studio source** to study libobs API patterns
3. **Create `VyralBot.OBS` project** in the solution
4. **Write the Phase 0 prototype** — get `obs_startup` working from C#
5. **Iterate from there** following the phase plan
