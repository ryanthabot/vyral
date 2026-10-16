# AGENTS.md — VyralBot

Instructions for AI coding agents (ZCode and others) working in this repository.
Read this before your first change; it encodes decisions, toolchain facts and
conventions gathered from the project's memory, past sessions and docs.

## What this repo is

**VyralBot** is a Windows streaming-automation desktop app: a multi-platform
(Twitch / Kick / YouTube / Discord / TikTok) chat bot, overlay/alert system,
embedded OBS (libobs + obs-websocket), FFmpeg restreaming, currency/moderation/
workflow engines, and a StreamDeck button grid — all in one process.

Two codebases live here:

| Piece | Stack | Location |
|---|---|---|
| Desktop app (Vyral.exe) | C# / .NET 10 / WPF, MaterialDesign, CommunityToolkit.Mvvm | `VyralBot/src/` (8-project solution) |
| VyralDeck mobile companion | Flutter / Dart (iOS + Android), LAN-only | `VyralBot/mobile/vyral_deck/` |

**Stack decision (2026-10-07, standing):** the desktop app stays C#/WPF — do not
propose a Flutter/Electron rewrite; its value is native Windows integration
(embedded libobs P/Invoke, win-capture, embedded Kestrel, WebView2 OAuth).
Flutter is for the mobile companion only.

## Repo map

```
publish-exe.ps1            Desktop release script (self-contained Vyral.exe)
releases/                  Release artifacts (Vyral.exe + VyralDeck APK) — NEVER commit
VyralBot/src/              The .NET solution (VyralBot.sln)
  VyralBot.UI/             WPF app: Views/, ViewModels/ (74+), Services/, App.xaml.cs (DI root)
  VyralBot.Core/           Domain services (chat, currency, events, audio, moderation…)
  VyralBot.Api/            Embedded Kestrel web server (webhooks, audio-ws)
  VyralBot.Data/           Dapper data access (IDataService), SQLite per profile
  VyralBot.Integrations/   Platform + OBS + lights + StreamDeck integrations
  VyralBot.Workflows/      Visual workflow engine (WorkflowRuntime, WorkflowJson)
VyralBot/mobile/vyral_deck/  VyralDeck Flutter app (lib/, android/, PROTOCOL.md)
VyralBot/CONTEXT_MASTER.md   Project's own "required reading" index — respect it
VyralBot/docs/             Feature/service/architecture docs (docs-sync rules apply)
docs/superpowers/specs/    Design specs (YYYY-MM-DD-<topic>-design.md)
tests/VyralBot.UI.Tests/   Test project
plugins/vyralbot/          ZCode plugin source (agents + skills for this repo)
.zcode/plans/              Archived session plans from past features
```

## Golden rules

1. **Never commit release binaries** (`releases/Vyral.exe`, APKs). Put new
   artifacts in `Desktop\Vyral\` (the user's launch folder) AND `releases\`,
   leave them uncommitted, and ask before tracking them.
2. **Docs-sync protocol** (`VyralBot/docs/copilot-instructions.md`): every new
   ViewModel/Service/View gets a matching doc in `VyralBot/docs/{ui,services,features}/`,
   and edits to existing code update the docs that reference it.
3. **Performance + zero-orphans** (`VyralBot/CONTEXT_MASTER.md`): no direct
   `Process.Start` — use `ProcessManager`; kill tracked processes on shutdown;
   no `.Result`/`.Wait()`; bounded channels with DropOldest; EventBus limit 4
   concurrent handlers; info-level logging only; HttpClient over WebView2.
4. **Secrets** (stream keys, tokens, OAuth): store per-profile via
   `IProfileService.GetProfileCredentialsPath` JSON or DPAPI; never log them;
   mask in UI and log output.
5. **Fragile file:** `ViewModels/ChatViewModel.cs` is huge — make surgical
   edits, never wholesale rewrites. Past recovery (proven): re-decompile from
   the last good DLL if an edit shreds it.
6. **Data safety:** `%LOCALAPPDATA%\VyralBot` and `Desktop\Vyral\profiles` hold
   user data. Publishing must go through `publish-exe.ps1` (stages first, backs
   up profiles, never deletes). Vyral.exe must be closed to publish; obs64
   running is fine.
7. Grep hits under `src/**/bin/`, `src/**/VerifyBin/`, `obj/` are build noise —
   exclude those dirs when searching.

## Toolchain (this machine)

| Tool | Where | Notes |
|---|---|---|
| .NET SDK | 10.0.100-rc on PATH | all projects `net10.0-windows`, x64 |
| Flutter 3.47.5 | `%LOCALAPPDATA%\flutter` (NOT on PATH) | invoke `"$LOCALAPPDATA/flutter/bin/flutter.bat"` |
| JDK 21 | `C:\Program Files\Java\jdk-21` | set `JAVA_HOME` for Gradle |
| Android SDK | `%LOCALAPPDATA%\Android\Sdk` | licenses accepted; set `ANDROID_HOME` |

## Commands

Desktop (from `VyralBot/src/`):
```bash
dotnet build VyralBot.sln -c Debug --nologo -v q          # full build
dotnet build VyralBot.UI/VyralBot.UI.csproj -c Debug ...  # UI-only iteration
dotnet test                                                # keep the suite green
```
Fast verify variant that avoids clobbering bin outputs (from past sessions):
`-p:BaseOutputPath=VerifyBin/`.

Desktop release (repo root, Vyral closed):
```powershell
powershell -ExecutionPolicy Bypass -File publish-exe.ps1
# → Desktop\Vyral\Vyral.exe (self-contained single-file), profiles backed up
```

Mobile (from `VyralBot/mobile/vyral_deck/`, export `JAVA_HOME` + `ANDROID_HOME`):
```bash
"$LOCALAPPDATA/flutter/bin/flutter.bat" analyze   # must be clean
"$LOCALAPPDATA/flutter/bin/flutter.bat" test
"$LOCALAPPDATA/flutter/bin/flutter.bat" build apk --release
# → build/app/outputs/flutter-apk/app-release.apk
```

## Architecture quick reference

- **DI root:** `VyralBot.UI/App.xaml.cs` `ConfigureServices` (manual
  `AddSingleton` registrations; host built with `Host.CreateDefaultBuilder`).
- **Navigation:** sidebar buttons are hard-coded XAML in `MainWindow.xaml`;
  sub-sections use the collapsible-group pattern (Restream group: Restream +
  StreamDeck). Pages map ViewModel→View via `DataTemplate` at the bottom of
  `MainWindow.xaml`; `MainViewModel` owns per-page scope fields +
  `NavigateToXxxAsync` + disposal bookkeeping.
- **New page recipe:** create `Views/XxxView.xaml` (copy `RestreamView.xaml`
  conventions: MaterialDesign cards, `BoolToVis` resources, dark theme brushes
  `BackgroundPrimaryBrush`/`CardBackgroundBrush`/`PrimaryBrush`/`TextPrimaryBrush`)
  + `ViewModels/XxxViewModel.cs` (`[ObservableProperty]`/`[RelayCommand]`,
  `InitializeAsync` per navigation) → register in `App.xaml.cs` → scope fields +
  navigate command + disposal in `MainViewModel` → DataTemplate in
  `MainWindow.xaml` → nav button.
- **Persistence:** per-feature settings = `IDataService.Get/SetConfigValueAsync`
  (JSON string, category = feature name). Secrets/files = profile credentials
  folder. UI prefs = `ui-settings.json` via `UiPreferencesService`.
- **Event bus:** everything flows through `IEventBus` (`PublishAsync<T>` /
  `SubscribeAsync<T>`); live status via `IStreamLiveRegistry`.
- **Long-running services are singletons** that outlive their page VM (Restream
  engine, DeckServer, embedded OBS host) — the VM subscribes/unsubscribes with
  named handlers so `Dispose` actually removes them.
- **LAN servers:** the webhook server (`VyralBot.Api/Services/WebServerService`)
  binds loopback only; LAN-exposed services (VyralDeck `DeckServer`, port 5199
  TCP + 5198 UDP discovery) are separate Kestrel hosts, **off by default**,
  PIN-paired with per-device tokens. Protocol: `VyralBot/mobile/vyral_deck/PROTOCOL.md`.

## Subsystem map (where past work lives)

| Subsystem | Entry points |
|---|---|
| Restream (FFmpeg fan-out) | `Integrations/Restream/RestreamEngine.cs`, `RestreamViewModel` |
| StreamDeck / VyralDeck | `UI/Services/VyralDeck/` (models, executor, server, pairing), `StreamDeckViewModel`, mobile `lib/` |
| OBS (embedded + remote) | `Integrations/Obs/` (Engine, Hosting, WebSocket), `ObsHostViewModel` |
| Workflows / Flow editor | `VyralBot.Workflows/`, `FlowEditorViewModel`, `WorkflowTriggerService` |
| Moderation | `Core/Services/Moderation/`, `ModeratorViewModel`, mod tiers |
| Currency / points | `Core/Services/Currency/`, per-platform `PlatformEarnings` ledger |
| Marketplace / sharecodes | `UI/Services/MarketplaceService`, GitHub `ryanthabot/vyral` store |
| Chat events / webhooks | `Api/Controllers/WebhooksController`, `Core/Services/Chat/` |

## Git conventions

- Conventional commits: `feat(scope): …`, `fix(scope): …`, `build(scope): …`
  (see `git log`). Commit source freely on `main`; **never** commit binaries.
- Don't push unless asked.
- Design work goes through a spec in `docs/superpowers/specs/` committed before
  implementation.

## Skills & agents for this repo

A ZCode plugin lives at `plugins/vyralbot/` (marketplace: `plugins/marketplace.json`).
Install it via **Plugin Marketplace → Add → Add Plugin Marketplace** pointing at
the `plugins/` folder, then install "VyralBot Toolkit". It provides:

- Agents: `vyral-desktop-dev`, `vyral-mobile-dev`, `vyral-release-engineer`.
- Skills: `vyralbot-build`, `vyralbot-release`, `vyraldeck-mobile`,
  `vyralbot-feature` (the recipes above, as step-by-step procedures).
