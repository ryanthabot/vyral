# OBS Integration (Deep Guide)

> Three ways Vyral owns OBS: run the **real obs64.exe window inside the app** (Full OBS), drive a **libobs-powered studio embedded in the Vyral process** (Bot Studio), or **import scene-collection JSON** and browse/export it byte-identical (Collections) — plus a silent installer when OBS is missing and obs-websocket remote control so bot features can steer a running OBS.

## Overview

The sidebar **OBS** page hosts three tabs — **Full OBS** (the default), **Bot Studio**, and **Collections** (ObsViewModel.cs:12-17; ObsView.xaml:65-101). They share one install probe: `ObsInstallLocator` finds OBS Studio (an explicit override root is authoritative with no fallback; otherwise default install locations), and when nothing is found the Full OBS tab offers a one-click silent install (ObsInstallLocator.cs:17-43; ObsAcquisitionService.cs:117-135).

| Tab | What it is | Process |
|---|---|---|
| **Full OBS** | The actual obs64.exe window hosted in-page — settings, mixer, scenes, everything | Separate process, owned (not parented) by Vyral's window |
| **Bot Studio** | A studio built on libobs (`obs.dll`) loaded *into* Vyral: scenes/sources list, program preview, stream/record/replay outputs | In-process native engine |
| **Collections** | Import OBS scene-collection JSON verbatim, browse it as a tree, export byte-identical copies | Pure data, no OBS required |

For the Platforms-page OBS card (a placeholder) and where TikTok stands, see [TikTok & OBS](../platforms/tiktok-discord-obs.md).

## User Guide

### Full OBS: the real window, embedded

Navigate to OBS → the app **launches or adopts obs64**, finds its main Qt window and embeds it exactly over the tab's area (ObsHostViewModel.cs:106-164). You get the genuine OBS UI — the page says it best: "Everything here is the real OBS — settings, mixer, scenes. Changes apply to OBS everywhere" (ObsHostViewModel.cs:97-98).

- **Open in own window** detaches the OBS window to the desktop (status: "OBS detached"); **Put back into Vyral** re-embeds it (ObsHostViewModel.cs:211-239).
- **Relaunch** re-runs the launch path when OBS was closed or its window never appeared (e.g. a dialog blocked it) (ObsHostViewModel.cs:185-209).
- Leaving the tab (or the app) **parks** the window — minimized to the taskbar with ownership cleared — so it never floats above other pages. OBS keeps running; Vyral exiting minimizes it, never kills it (ObsHostViewModel.cs:487-517; ObsWindowHostService.cs:43-56).
- If OBS runs **as administrator** and Vyral doesn't, Windows UIPI silently blocks the embed; the page detects this and offers a one-click **restart Vyral as administrator** (ObsWindowHostService.cs:84-86; ObsHostViewModel.cs:449-474).

**Remote control**: the tab enables OBS's obs-websocket server when it's off (never while obs64 is running — the flag is only read at OBS startup), then connects a client so bot features can drive OBS. Status line shows "Remote control: connected — scene X • stream live" (ObsHostViewModel.cs:301-404). This is the same channel the [StreamDeck](vyraldeck.md) OBS actions use.

**Remote control auto-reconnects** (remote mode only): if the OBS connection drops for any reason other than you disconnecting — OBS restarted, the websocket server toggled, the machine slept — the client retries **indefinitely with exponential backoff, 1 s doubling to a 30 s ceiling**, and each reconnect re-reads the obs-websocket password straight from OBS's own config (so a password changed in OBS's WebSocket Server Settings is picked up by the next retry without re-entering the page), re-subscribes events, and refreshes scene/stream state (ObsWebSocketClient.cs:39-41, 346-391; ObsHostViewModel.cs:371). The status line narrates every attempt ("The OBS connection on port 4455 was lost — reconnecting in 2 s…"), and a manual STOP truly stops — no zombie loop.

**When OBS isn't installed**: the acquisition panel downloads the latest official OBS Studio Windows installer (GitHub releases feed) with byte progress and runs it silently (`/S`) (ObsAcquisitionService.cs:45-47, 140-200, 313).

### Bot Studio: embedded libobs

Selecting the Bot Studio tab **boots the native engine lazily** — never on app start, never from the default tab (ObsViewModel.cs:30-32, 118-122). The engine loads `obs.dll` from your OBS install, deliberately blocklisting UI-hostile plugin modules (CEF browser, VST, frontend glue, obs-websocket…) that access-violate a non-OBS host (ObsEngine.cs:62-82).

Once Ready (`Ready — libobs <version>`), you get:

- **Scenes list** with the current program scene highlighted; clicking a scene switches it live (ObsStudioViewModel.cs:329-395).
- **Sources panel** for the selected scene with per-item visibility and lock checkboxes (ObsStudioViewModel.cs:397-475).
- **Outputs**: Stream, Record and **Replay Buffer** toggles plus **Save Replay** while the buffer runs — stream settings re-read from your OBS profile on every start (rtmp_custom service + encoders built fresh), and the 1 Hz strip shows fps and uptime (ObsStudioViewModel.cs:517-636; ObsOutputService.cs:16-47).
- **Load collection from file**: load any OBS scene-collection JSON into the studio; the last-loaded path is remembered and auto-loaded next time the engine boots. On first boot the bundled **Vyral starter collection** imports once (in-memory; nothing is written to your OBS config from this tab) (ObsStudioViewModel.cs:251-299, 481-511).
- **Missing-plugins notice**: sources the collection references that need plugins you don't have are listed by id (ObsStudioViewModel.cs:301-323).

If OBS Studio isn't installed, the tab says so and stops — the embedded engine needs a real OBS install to load native modules from (ObsEngine.cs:9-11).

### Collections: verbatim import/export

**Import** picks an OBS scene-collection JSON from disk and stores it **verbatim — the raw bytes are the canonical store**, unknown/future fields survive (ObsSceneCollectionStore.cs:8-11). The list shows each collection's scene/source counts and import date; selecting one loads a **document tree** — Scenes (with per-item visibility/rotation/scale/position/lock), non-scene Sources (filters, muted, volume), and audio devices (ObsViewModel.cs:296-343). **Export** writes those exact bytes back — the status line even appends "(byte-identical)". **Delete** removes the imported copy only; the original OBS file is never touched (ObsViewModel.cs:230-294).

## How It Works (Developer)

### Window hosting without reparenting

The host never calls `SetParent` — cross-process reparenting breaks the foreign window's DWM paint. Instead it makes Vyral's top-level window the OBS window's **owner** (`GWL_HWNDPARENT`), positions it over the embed area, and runs a 1 Hz health check that heals drift beyond a 2 px tolerance; when OBS's Qt enforces a minimum size and refuses to shrink, the window region is **clipped** to the owner intersection so nothing renders outside Vyral (ObsWindowHostService.cs:43-56, 112-124). Window discovery polls by PID every 250 ms (largest `Qt`-class window ≥ 400×300 wins) with a 30 s timeout; process exit polls at 1 s (ObsWindowHostService.cs:59-68). A named mutex (`Local\VyralBot.ObsWindowHost`) stops a second Vyral instance from stealing the hosted window (ObsWindowHostService.cs:79-82).

### The libobs engine, safely

`ObsEngine` serializes every native call through one gate (libobs is single-init, not thread-safe); boot runs on the threadpool, never the UI thread; and every service call in the ViewModels is wrapped so a native fault degrades to a status message instead of taking the app down (ObsEngine.cs:32-40; ObsStudioViewModel.cs:18-20). After `obs_shutdown` the engine cannot boot again in the process (`Shutdown` is terminal — ObsEngine.cs:24-27).

### Outputs and secrets

`ObsOutputService` builds stream/record/replay outputs from the OBS profile the user last selected; the stream server/key are serialized into an in-memory `obs_data` and never logged — no action message ever embeds them (ObsOutputService.cs:16-47). The post-stop drain (≤ 7 s) runs *outside* the gate so a stopping output never blocks engine traffic (ObsOutputService.cs:29-34).

### Collection update path (non-destructive)

Separate from the Collections tab, `ObsCollectionUpdateService` can merge the upstream **Vyral starter collection** from the project's GitHub repo into your *active* OBS collection: a union merge where the user's file is the base, upstream contributes only **new** scenes/sources/items, nothing is removed, and your file is only written after a successful merge (ObsCollectionUpdateService.cs:8-17; ObsCollectionMerger.cs:8-16). The strictly-additive local import (`Vyral.json` in the OBS scenes dir, tracked by content hash) is `ObsBundledCollectionService` (ObsBundledCollectionService.cs:8-30).

## Files & Code

| File | Role |
|---|---|
| [ObsViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsViewModel.cs) | Page host: the three tabs, lazy studio boot, park-on-tab-leave, Collections CRUD |
| [ObsHostViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsHostViewModel.cs) | Full OBS tab: launch/adopt, embed status, relaunch/detach/re-embed, silent install, remote control |
| [ObsStudioViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsStudioViewModel.cs) | Bot Studio: engine lifecycle, scenes/sources, stream/record/replay, 1 Hz stats |
| [ObsWindowHostService.cs](../../src/VyralBot.Integrations/Obs/Hosting/ObsWindowHostService.cs) | obs64 process + window ownership, park/restore, health check, UIPI detection, host mutex |
| [ObsEngine.cs](../../src/VyralBot.Integrations/Obs/Engine/ObsEngine.cs) | libobs lifecycle: gated single-init boot, module blocklist, shutdown |
| [ObsStudioService.cs](../../src/VyralBot.Integrations/Obs/Engine/ObsStudioService.cs) / [ObsSceneCollectionLoader.cs](../../src/VyralBot.Integrations/Obs/Engine/ObsSceneCollectionLoader.cs) | Scene/source operations; loading collection JSON into the running engine |
| [ObsOutputService.cs](../../src/VyralBot.Integrations/Obs/Engine/ObsOutputService.cs) | Stream/Record/ReplayBuffer outputs from the OBS profile |
| [ObsAcquisitionService.cs](../../src/VyralBot.Integrations/Obs/Hosting/ObsAcquisitionService.cs) | GitHub-release resolve → download → silent `/S` install, with progress |
| [ObsSceneCollectionStore.cs](../../src/VyralBot.Integrations/Obs/ObsSceneCollectionStore.cs) | Verbatim import / byte-identical export of collection JSON |
| [ObsWebSocketClient.cs](../../src/VyralBot.Integrations/Obs/WebSocket/ObsWebSocketClient.cs) / [ObsWebSocketConfigEditor.cs](../../src/VyralBot.Integrations/Obs/Hosting/ObsWebSocketConfigEditor.cs) | obs-websocket client (optional `autoReconnect`: backoff ladder 1 s→30 s, unlimited attempts, password re-read per attempt) + config read/enable |

## Data & Settings

| Key | Store | Meaning |
|---|---|---|
| `Obs.AutoInit` | Configs (category "Obs") | Whether the Bot Studio engine auto-initializes on first tab select (default true) (ObsStudioViewModel.cs:23, 145-152) |
| `Obs.LastCollectionPath` | Configs | Last collection loaded into the studio — auto-loaded on next Ready (ObsStudioViewModel.cs:24, 283-292) |
| `Obs.BundledCollectionHash` | Configs | SHA256 of the bundled Vyral collection at last import (import-once gate) (ObsBundledCollectionService.cs:28) |
| `ObsSceneCollections` table | Per-profile SQLite | Imported collections: raw JSON bytes + derived scene/source counts (ObsSceneCollectionStore.cs:28-40) |

OBS's own config (profiles, scene collections, websocket settings) lives in OBS's user dir and is only read — or written by the strictly-additive bundled import.

## Status & Known Issues

1. **Full OBS and Bot Studio are separate engines** — hosting obs64 does not boot libobs and vice versa; each tab owns its own state (ObsViewModel.cs:94-123).
2. **The embed needs matching elevation** — OBS as admin + Vyral normal = UIPI failure with a guided elevated restart; not a crash (ObsWindowHostService.cs:84-86).
3. **Vyral never kills OBS** — Dispose parks/minimizes it; even a Vyral crash leaves a running, recoverable OBS on the taskbar (ObsWindowHostService.cs:49-56).
4. **Bot Studio engine is single-shot per process** — after shutdown (or a failed boot→shutdown cycle) it cannot re-init without restarting the app (ObsEngine.cs:24-27).
5. **Remote-control enable needs OBS closed** — the websocket flag is only read at OBS startup, so Vyral never writes it under a running obs64; it tells you to close OBS or flip the setting yourself (ObsHostViewModel.cs:118-136).

### Verified against (2026-11-07)

`ObsViewModel.cs` (whole file), `ObsHostViewModel.cs` (whole file — init :106-179, detach/re-embed :211-239, remote control :301-404, admin restart :449-474, park :487-517), `ObsStudioViewModel.cs` (whole file — auto-init :140-153, auto-load :251-299, outputs :517-656), `ObsWindowHostService.cs` (:9-124 state machine, owner-not-parent + park semantics, mutex, UIPI), `ObsEngine.cs` (:8-90 states, gate, blocklist), `ObsOutputService.cs` (:11-47 outputs, secrets, drain), `ObsAcquisitionService.cs` (:117-200 + installer runner :313), `ObsSceneCollectionStore.cs` (:8-40), `ObsCollectionUpdateService.cs` (:8-30), `ObsCollectionMerger.cs` (:8-16), `ObsBundledCollectionService.cs` (:8-60), `ObsInstallLocator.cs` (:17-50), `ObsView.xaml` (tab pills :65-101).
