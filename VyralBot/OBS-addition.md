## Current State

VyralBot is a mature WPF application targeting .NET 10.0-windows with:

- **50+ XAML views** with full MVVM architecture (CommunityToolkit.MVVM)
- **Material Design theming** (MaterialDesignThemes + custom RedSlate theme)
- **WebView2 integration** for OAuth flows
- Custom controls, converters, behaviors, and extensive data bindings
- 7-project layered architecture where the UI project is deeply integrated


This is 100% opensource so there is no GPL 2.0 issues here. This will be completely public.

I want to have the BEST streaming app so the ability to handle everything and be able to use plugins for OBS on top would be massive, native OBS plugins could work with the app if its built in right?

GitHub Copilot: This is absolutely doable and would be a killer differentiator. Since you're fully open source (GPL-2.0 compatible), embedding libobs directly is the right path. Here's how it all works:

## How OBS Configs + Plugins Would "Just Work"

OBS stores everything in a well-known location:

```
%APPDATA%/obs-studio/
├── basic/
│   ├── profiles/          ← Stream keys, encoder settings, audio config
│   │   └── MyProfile/
│   │       ├── basic.ini
│   │       ├── streamEncoder.json
│   │       └── recordEncoder.json
│   └── scenes/            ← Scenes, sources, scene collections
│       └── MyScenes.json  ← Full scene graph (sources, transforms, filters)
├── plugin_config/         ← Per-plugin settings
│   ├── obs-websocket/
│   ├── StreamFX/
│   └── ...
└── obs-plugins/           ← User-installed plugins (DLLs + data)
    └── 64bit/
```

When you initialize libobs and point it at these paths, **everything loads automatically** — scenes, sources, plugins, the lot.

## Architecture: libobs Inside WPF

```
┌──────────────────────────────────────────────────┐
│                  VyralBot.UI (WPF)               │
│  ┌──────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │ Chat Hub │ │ Commands │ │  OBS Studio View │ │
│  └──────────┘ └──────────┘ │  ┌──────────────┐│ │
│                             │  │ Scene Preview ││ │
│                             │  │ (D3DImage)   ││ │
│                             │  └──────────────┘│ │
│                             │  Scene List       │ │
│                             │  Source Props      │ │
│                             │  Audio Mixer       │ │
│                             │  Stream Controls   │ │
│                             └──────────────────┘ │
├──────────────────────────────────────────────────┤
│           VyralBot.OBS (C++/CLI Wrapper)         │
│  Managed C++ layer that bridges .NET ↔ libobs    │
├──────────────────────────────────────────────────┤
│                    libobs (C)                     │
│  obs_startup() → obs_load_all_modules() →        │
│  obs_get_scene_collection() → rendering pipeline  │
├──────────────────────────────────────────────────┤
│  OBS Plugins (native DLLs)    │  FFmpeg/x264     │
│  StreamFX, Move Transition,   │  NVENC, QSV      │
│  Advanced Scene Switcher, etc │  RTMP, SRT       │
└──────────────────────────────────────────────────┘
```

## The Three Layers You'd Build

### Layer 1: C++/CLI Wrapper (`VyralBot.OBS`)

A managed C++ project that wraps libobs's C API for .NET consumption:

```cpp
// VyralBot.OBS — C++/CLI wrapper (compiles as mixed-mode assembly)
#include <obs.h>
#include <obs-module.h>

namespace VyralBot::OBS {

    public ref class ObsCore {
    public:
        bool Initialize() {
            // Start libobs with DirectX 11 (same GPU pipeline as WPF)
            if (!obs_startup("en-US", nullptr, nullptr))
                return false;

            // Load video/audio subsystems
            obs_reset_video(&videoInfo);  // D3D11 renderer
            obs_reset_audio(&audioInfo);

            // Point at user's existing OBS plugin paths
            obs_add_module_path(
                "C:/Program Files/obs-studio/obs-plugins/64bit",
                "C:/Program Files/obs-studio/data/obs-plugins");

            // User-installed plugins
            obs_add_module_path(
                "%APPDATA%/obs-studio/obs-plugins/64bit",
                "%APPDATA%/obs-studio/obs-plugins");

            // THIS loads every OBS plugin the user has installed
            obs_load_all_modules();
            obs_post_load_modules();

            return true;
        }

        void LoadSceneCollection(String^ path) {
            // Parse the user's existing scenes JSON
            // Recreates their entire OBS setup inside VyralBot
            obs_load_sources();
        }

        IntPtr GetPreviewTexture() {
            // Returns D3D11 shared texture handle for WPF rendering
            return (IntPtr)obs_display_get_texture(display);
        }
    };
}
```

### Layer 2: DirectX Interop for Video Preview

WPF renders via DirectX — and so does libobs. You share a GPU texture:

```csharp
// In your WPF view — renders OBS output directly in the UI
public class ObsPreviewHost : D3DImage {
    // libobs renders scenes → D3D11 shared texture → WPF D3DImage
    // Zero-copy GPU path, no frame copying through CPU

    public void UpdateFrame(IntPtr sharedTextureHandle) {
        Lock();
        SetBackBuffer(D3DResourceType.IDirect3DSurface9, sharedTextureHandle);
        AddDirtyRect(new Int32Rect(0, 0, PixelWidth, PixelHeight));
        Unlock();
    }
}
```

### Layer 3: WPF UI That Replaces the Qt Frontend

You'd build WPF equivalents of OBS's panels:

| OBS Qt Panel | Your WPF Equivalent |
|---|---|
| Scene list | MaterialDesign ListView with drag-reorder |
| Source list | TreeView with visibility toggles, lock, transforms |
| Audio mixer | Custom VU meter controls with sliders |
| Preview/Program | D3DImage-backed render surfaces |
| Source properties | Dynamic property grid (each source type declares its properties via libobs API) |
| Transition controls | ComboBox + duration slider |
| Stream/Record controls | Toggle buttons bound to obs_output |
| Studio mode | Dual preview panels |

## Yes, Native OBS Plugins Would Work

This is the key win. When you call `obs_load_all_modules()`, libobs loads every plugin DLL and they register themselves:

- **Source plugins** (cameras, captures, browser sources, NDI, etc.) — show up as available source types
- **Filter plugins** (StreamFX, color correction, chroma key) — available on any source
- **Output plugins** (custom RTMP, SRT, recording formats) — available as output targets
- **Encoder plugins** (NVENC, QSV, x264 presets) — available in encoding settings
- **Transition plugins** (Move Transition, Stinger, etc.) — available between scenes

The plugins talk to **libobs**, not to the Qt UI. They don't care what the frontend is. Your WPF UI just needs to query libobs for "what plugins are loaded?" and surface their properties.

```csharp
// Enumerate all available source types (including from plugins)
var sourceTypes = obsCore.GetSourceTypes();
// Returns: "wasapi_output_capture", "browser_source", "ndi_source",
//          "streamfx-source-mirror", "window_capture", etc.

// Each source type declares its own properties
var props = obsCore.GetSourceProperties("browser_source");
// Returns: url (string), width (int), height (int), css (string), etc.
// You dynamically build a WPF property editor from this
```

## Importing Existing OBS Setups

To make the "install VyralBot and your OBS stuff is already there" experience:

```csharp
public class ObsConfigImporter {
    private static readonly string ObsConfigPath =
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "obs-studio");

    public ObsUserSetup Import() {
        // Load their profiles (stream keys, encoder settings)
        var profiles = LoadProfiles(Path.Combine(ObsConfigPath, "basic/profiles"));

        // Load their scene collections (scenes, sources, transforms)
        var scenes = LoadScenes(Path.Combine(ObsConfigPath, "basic/scenes"));

        // Discover installed plugins
        var plugins = DiscoverPlugins(Path.Combine(ObsConfigPath, "obs-plugins/64bit"));

        return new ObsUserSetup(profiles, scenes, plugins);
    }
}
```

## What This Actually Takes

| Component | Effort | Notes |
|---|---|---|
| C++/CLI wrapper for libobs | **High** | ~200 key functions to wrap, but can be incremental |
| DirectX shared texture rendering | **Medium** | Well-documented pattern, WPF + D3D11 interop works |
| Scene management UI | **Medium** | ListView + drag/drop, backed by obs_scene API |
| Source properties UI | **High** | Dynamic property system — each source/plugin declares its own props |
| Audio mixer UI | **Medium** | Custom VU meters + volume sliders, backed by obs_volmeter |
| Encoding/output settings | **Medium** | Form UI backed by obs_encoder / obs_output APIs |
| Plugin compatibility testing | **Ongoing** | Most will "just work" but edge cases exist |

## The Competitive Picture

This would put VyralBot in the same category as:
- **Streamlabs** (which started exactly this way — custom UI on top of libobs, though they forked it)
- **OBS itself** (but with your integrated chat, commands, currency, alerts all in one app)

The difference is **you'd be the all-in-one**: streaming engine + multi-platform chat + bot commands + alerts + currency — no need for 5 separate apps.

**Bottom line:** Fully achievable. The C++/CLI wrapper layer is the biggest engineering investment. Start by getting libobs initialized, a test source rendering to a WPF window, and plugin loading working. Once that skeleton is proven, building out the UI is incremental WPF work you're already familiar with.

User: Okay so I think we should make a plan for this then, how do we go about making the most comprehensive plan to accomplish this

GitHub Copilot: Let me do thorough discovery first — I'll research your current architecture patterns, build system, and integration structure in parallel.

VyralBot UI architecture patterns

VyralBot Integrations architecture

VyralBot Core and Data layer

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI)

Searched for regex `class.*ViewModel.*ObservableObject|RelayCommand` (`**/src/VyralBot.UI/**/*.cs`), 20 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/VyralBot.UI.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations)

Searched for text `\.csproj` (`**/*.sln`), 11 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/Directory.Build.props)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Models)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Repositories)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Views)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/VyralBot.Data.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/VyralBot.Core.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#1-1), lines 1 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml#1-1), lines 1 to 100

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/YouTube)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/VyralBot.Integrations.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.sln)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Usings.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/IDataService.cs#1-1), lines 1 to 100

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/DapperDataService.cs#1-1), lines 1 to 100

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/Events)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/Events)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/YouTube/Events)

Searched for text `Project` (`**/src/VyralBot.sln`), 15 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#150-150), lines 150 to 200

Searched for regex `D3D|Direct3D|native|interop|SharpDX|Veldrid|OpenGL|rendering` (`**/src/VyralBot.UI/**/*.{cs,xaml}`), 2 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/MainViewModel.cs#1-1), lines 1 to 80

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/ChatHubViewModel.cs#1-1), lines 1 to 80

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/EventBus)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Models/Profile.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Models/ActionContext.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/IKickService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/YouTube/IYouTubeService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/ITwitchService.cs)

Searched for regex `EventBus|IEventBus` (`**/VyralBot.Core/**`), 20 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Chat)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Webhooks)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Commands)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#200-200), lines 200 to 200

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Services)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/ViewModelBase.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/Events/KickChatMessageEvent.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/TwitchService.cs#1-1), lines 1 to 300

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/KickLibChatService.cs#1-1), lines 1 to 300

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/EventBus/EventBus.cs#1-1), lines 1 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Chat/IChatEventSettingsService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Commands/ICommandService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#200-200), lines 200 to 300

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Controls)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Views/PlatformsView.xaml#1-1), lines 1 to 80

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Views/ChatHubView.xaml#1-1), lines 1 to 80

Searched for text `AddScoped|AddSingleton|AddTransient|ServiceCollection` (`**/src/VyralBot.Integrations/**/*.cs`), no results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src)

Searched for regex `class|namespace` (`**/src/**/*ServiceCollection* OR src/**/*ServiceExtension* OR src/**/*program.cs`), no results

Searched for text `IEvent` (`**/VyralBot.Core/Services/EventBus/**`), 11 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/ActionDispatcher)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/TriggerEngine)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services)

Searched for files matching `**/src/VyralBot.Integrations/**/*.cs`, 29 matches

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#100-100), lines 100 to 300

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Behaviors)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Resources)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/PlatformsViewModel.cs#1-1), lines 1 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Views/PlatformsView.xaml.cs#1-1), lines 1 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services)

Searched for files matching `**/src/VyralBot.UI/**/*.cs`, 30 matches

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/EventBus/IEvent.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/EventBus/IEventBus.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/TriggerEngine/ITriggerEngine.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/ActionDispatcher/IActionDispatcher.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/MainViewModel.cs#80-80), lines 80 to 200

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/MainViewModel.cs#200-200), lines 200 to 300

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Resources/Themes)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Services/AlertService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#1-1), lines 1 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/EventBus)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Chat/IChatMessageStore.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/VyralBot.Integrations.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/ChatMessage.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/Platform.cs#1-1), lines 1 to 100

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml.cs#150-150), lines 150 to 250

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/EventBus/IEventBus.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/EventBus/IEvent.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml#100-100), lines 100 to 300

Searched for regex `RelayCommand|NavigateTo|DataTemplate` (`**/src/VyralBot.UI/ViewModels/MainViewModel.cs`), 50 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml#1-1), lines 1 to 80

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/WebServer)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/Trigger.cs)

Searched for text `TargetFramework` (`**/src/**/*.csproj`), 14 results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/Events/KickSubscribeEvent.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/Events/TwitchSubscribeEvent.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/IKickService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/ITwitchService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/BotAction.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/Converters)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/ViewModels/ChatHubViewModel.cs#80-80), lines 80 to 150

Searched for regex `DataTemplate|ContentPresenter|Binding` (`**/src/VyralBot.UI/Views/ChatHubView.xaml`), 20 results

Searched for files matching `**/src/VyralBot.Integrations/**`, 30 matches

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/ITwitchEventSubService.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/TwitchEventSubService.cs#1-1), lines 1 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/App.xaml#80-80), lines 80 to 150

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Kick/Events)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/Events)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/Config.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/ChatEventSetting.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/VyralBot.Core.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/YouTube/YouTubeChatService.cs#1-1), lines 1 to 200

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/Events/TwitchChatMessageEvent.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/UserCurrency.cs)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Data/Entities/CurrencySetting.cs#1-1), lines 1 to 80

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs#1-1), lines 1 to 100

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Currency)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Rules)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/Variables)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs#1-1), lines 1 to 100

Created memory file [](file:///c%3A/Users/user/AppData/Roaming/Code/User/workspaceStorage/6189104b596c12ef1fdb497f11d910da/GitHub.copilot-chat/memory-tool/memories/MmE3NGVjZDctNmM5NS00MmFiLThjN2EtYzM4ZTdlYmNjNzVk/vyralbot-ui-architecture-exploration.md)

Read [](file:///c%3A/Users/user/AppData/Roaming/Code/User/workspaceStorage/6189104b596c12ef1fdb497f11d910da/GitHub.copilot-chat/chat-session-resources/2a74ecd7-6c95-42ab-8c7a-c38e7ebcc75d/toolu_013FLeAuYvmjTYnMMatxsUAx__vscode-1773566483282/content.txt#1-1), lines 1 to 300

Read [](file:///c%3A/Users/user/AppData/Roaming/Code/User/workspaceStorage/6189104b596c12ef1fdb497f11d910da/GitHub.copilot-chat/chat-session-resources/2a74ecd7-6c95-42ab-8c7a-c38e7ebcc75d/toolu_01MGUBigQkfqxnmcMC45XpA9__vscode-1773566483281/content.txt#1-1), lines 1 to 300

Excellent discovery. Now let me check a few more things before drafting the plan.

VyralBot sidebar navigation and plugin system

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/Update-plan.md#1-1), lines 1 to 100

Searched for files matching `**/MainWindow.xaml`, 1 match

Searched for files matching `**/VyralBot.Plugins/**/*.cs`, no matches

Searched for files matching `**/VyralBot.Integrations.csproj`, 1 match

Searched for files matching `**/IPlugin.cs`, no matches

Searched for files matching `**/src/**/*Plugin*.cs`, no matches

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml#1-1), lines 1 to 200

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/VyralBot.Integrations.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml#200-200), lines 200 to 400

Searched for regex `IPlugin|plugin|Plugin` (`**/src/**/*.cs`), no results

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Plugins)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml#400-400), lines 400 to 600

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Integrations/VyralBot.Integrations.csproj)

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.UI/MainWindow.xaml#600-600), lines 600 to 700

Read [](file:///c%3A/Users/user/OneDrive/Desktop/VyralBot/VyralBot/src/VyralBot.Plugins/VyralBot.Plugins.csproj)

Searched for text `AllowUnsafeBlocks` (`**/src/**/*.csproj`), no results

Searched for files matching `**/src/VyralBot.Plugins/**/*.cs`, no matches

Searched for text `Plugin|plugin` (`**/src/**/*.xaml`), no results

Searched for text `Plugin|plugin` (`**/src/**/*.cs`), no results

Asked yourself 4 questions (Interop approach, OBS binary distribution, Sidebar placement, MVP scope)

