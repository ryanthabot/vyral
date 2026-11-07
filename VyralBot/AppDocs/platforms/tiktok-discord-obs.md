# TikTok & OBS

> TikTok is a placeholder — a full-looking setup wizard that marks the platform "Coming Soon" with no chat backend behind it. OBS is the opposite now: the **Platforms-page card** still reads "Coming Soon", but the sidebar has a real **OBS** page that hosts the actual obs64.exe window, embeds a libobs-powered Bot Studio, and manages scene collections. (Discord used to live on this page as a third stub; it is a fully implemented platform now — see [Discord](discord.md).)

## Overview

The Platforms page shows six cards. Twitch, Kick, YouTube, and [Discord](discord.md) are real; the honest status of the remaining two is:

- **TikTok** — a UI shell. The wizard looks real, but the OAuth pieces are commented-out TODO stubs. No TikTok chat service exists anywhere in the codebase. Stream/OAuth connect is explicitly gated "Coming Soon".
- **OBS** — the Platforms card is still a static "Coming Soon / Not Available" placeholder, but OBS itself is **fully integrated** through its own top-level **OBS** page in the sidebar: the **Full OBS** tab hosts the real obs64.exe window inside the app (with a silent install offer when OBS is missing and obs-websocket remote control so bot features can drive it), the **Bot Studio** tab embeds a libobs-based studio, and the **Collections** tab imports/browses/exports OBS scene-collection JSON. The older OBS touchpoint — adding Vyral's audio page as an OBS browser source — lives in [Audio](../stream-tools/audio.md).

This page exists so you do not lose time on TikTok, and so you know where OBS actually lives (the sidebar, not the Platforms card).

## User Guide

### TikTok: what actually happens

You can open the wizard and click through all four steps — the UI is fully built:

1. **Step 1: Sign In** — the embedded browser opens `https://www.tiktok.com/login`. Signing in does nothing for Vyral; nothing is captured.
2. **Step 2: Create Application** — the browser opens TikTok's developer portal, and the wizard will accept a pasted Client ID/Secret, but they are never saved anywhere.
3. **Step 3: Authorize Account** — the wizard builds an OAuth URL from a hardcoded placeholder string; the callback handler always returns null. Authorization never completes.
4. **Step 4: Test Connection** — the test-message button cannot succeed.

Finishing the wizard writes a `Platform` row with **`Status="Coming Soon"`** and `TokenExpiresAt = null` (changed on 2026-08-15 — it used to write a fake `"Connected"`). Nothing in the app treats a "Coming Soon" row as connected: no connection view flip, no sidebar entry, no auto-connect. No messages arrive, nothing can be sent. If you did this by accident, use DISCONNECT on the card to clear it.

**Bottom line: skip TikTok for now.** It is on the roadmap, not in the product.

### OBS: what you can actually use today

Ignore the "Coming Soon" OBS card on the Platforms page — open **OBS** in the sidebar instead. That page has three tabs:

1. **Full OBS** — hosts the real obs64.exe window inside the page. If OBS is not installed, the tab offers a silent install. obs-websocket remote control is wired so bot features can drive the running OBS.
2. **Bot Studio** — an embedded libobs studio (the native engine boots only when you first select this tab, never at app start).
3. **Collections** — imports OBS scene-collection JSON verbatim, browses the imported documents as a tree, and exports byte-identical copies.

The older OBS touchpoint also still works — add Vyral's **sound output** to OBS as a browser source:

1. Go to **Audio Settings** in Vyral and copy the browser source URL (`http://localhost:5000/audio.html`).
2. In OBS: Sources → Add → **Browser**, paste the URL, size to taste.
3. Sounds you trigger (event sounds from [Sounds](../stream-tools/sounds.md)) then play through OBS for your stream. Details in [Audio](../stream-tools/audio.md) and [Browser Sources](../stream-tools/browser-sources.md).

That page is served by the app's embedded web server — the same one used for [Kick webhooks](kick.md) — and works over localhost or your LAN.

## How It Works (Developer)

### The TikTok shell

[TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) is a template copy of the Twitch wizard with the working parts removed:

- The OAuth service is a commented-out field: `// private readonly TikTokOAuthService _oAuthService; // TODO: Implement TikTok OAuth service` (line 18).
- Credential save/load are no-ops: `await Task.CompletedTask /* TODO: TikTok credential save */;` (lines 292-314).
- The authorization URL is a hardcoded placeholder string: `"https://www.tiktok.com/v2/auth/authorize/" /* TODO: Build TikTok OAuth URL */` (line 317).
- `HandleOAuthCallbackAsync` always returns `null` (line 410).
- Steps 1-2 navigate the WebView2 to `https://www.tiktok.com/login` and `https://developers.tiktok.com/apps/` — real pages, cosmetic only.
- There is **no TikTok service anywhere** in `VyralBot.Integrations` or `VyralBot.Core`; grep for TikTok across `src` finds only UI files, the Platform entity's flag columns, and converters.

For contrast, the Discord wizard that used to share this stub pattern was rewritten outright on 2026-08-15 into a real bot-token wizard backed by [DiscordService](../../src/VyralBot.Integrations/Discord/DiscordService.cs) — see [Discord](discord.md) for how a non-OAuth platform was wired end to end.

### The "Coming Soon" rows

The stub wizard has a working `FinishSetupAutomaticallyAsync` that saves a real row — but since 2026-08-15 with honest state (e.g. `TikTokWebSetupViewModel.cs:455-468`):

```csharp
var platform = new Platform
{
    Name = "TikTok",
    Status = "Coming Soon",   // <-- not "Connected"; empty tokens, TokenExpiresAt = null
    ...
};
```

`PlatformsViewModel` keys connection views, sidebar entries, and startup auto-connect off `Status == "Connected"`, so a "Coming Soon" row renders none of that — the UI does not display a connection that does not exist.

### OBS

- The Platforms card is still static XAML: "Coming Soon" / "Not Available" (`PlatformsView.xaml:349-372`) — a leftover placeholder; OBS is reached from the sidebar's **OBS** entry (`NavigateToObsCommand` → `ObsViewModel`, `MainViewModel.cs:1993`).
- The OBS page ([ObsView.xaml](../../src/VyralBot.UI/Views/ObsView.xaml) + [ObsViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsViewModel.cs)) hosts three tabs: **Full OBS** ([ObsHostViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsHostViewModel.cs) — real obs64.exe window hosted in-page via the `ObsWindowEmbedHost` control, silent install when OBS is missing, obs-websocket remote control), **Bot Studio** ([ObsStudioViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsStudioViewModel.cs) — embedded libobs studio; the native engine boots only when the tab is first selected), and **Collections** (verbatim scene-collection JSON import/tree/export via [ObsSceneCollectionStore.cs](../../src/VyralBot.Integrations/Obs/ObsSceneCollectionStore.cs)).
- The integration layer lives under `VyralBot.Integrations/Obs/` — `Engine` (libobs P/Invoke), `Hosting` (window hosting, acquisition, bundled collections, obs-websocket config editing), `Native`, and `WebSocket`.
- The browser-source page also remains: `wwwroot/audio.html` served by [WebServerService](../../src/VyralBot.Api/Services/WebServerService.cs) on port 5000 (URL pinned in `AudioSettingsViewModel.cs:36`), with a `/audio-ws` WebSocket endpoint broadcasting sound triggers to connected OBS browser-source pages. See [Web Server](../architecture/web-server.md).

## Files & Code

| File | Role |
|---|---|
| [TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) | TikTok wizard stub (TODO OAuth; writes honest "Coming Soon" row) |
| [TikTokWebSetupView.xaml](../../src/VyralBot.UI/Views/TikTokWebSetupView.xaml) | TikTok wizard UI (fully built, cosmetically real) |
| [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) / [TiktokChatSettingsView.xaml](../../src/VyralBot.UI/Views/TiktokChatSettingsView.xaml) | TikTok chat-settings shell (enable toggle persists `TiktokChatEnabled`; nothing consumes it) |
| [PlatformsView.xaml](../../src/VyralBot.UI/Views/PlatformsView.xaml) | Hosts all six cards incl. the OBS "Coming Soon" placeholder (lines 349-372) — the placeholder; the real OBS page is the sidebar's OBS entry |
| [ObsView.xaml](../../src/VyralBot.UI/Views/ObsView.xaml) / [ObsViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsViewModel.cs) | The real OBS page: Full OBS / Bot Studio / Collections tabs |
| [ObsHostViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsHostViewModel.cs) | Full OBS tab: hosts obs64.exe in-page, silent install, obs-websocket wiring |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Renders TikTok as connected purely from the DB row (disconnect ~line 1548) |
| [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | The real OBS touchpoint: `http://localhost:5000/audio.html` browser-source URL |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Serves `audio.html` + `/audio-ws` to OBS browser sources |

Wizard step destinations, for anyone wiring TikTok up for real: the wizard navigates to `https://www.tiktok.com/login` (step 1) and `https://developers.tiktok.com/apps/` (step 2) — the pages and the paste-Client-ID popups all work; only the OAuth and service layers are missing.

## Data & Settings

- **TikTok `Platform` row** — created by the stub wizard with `Status="Coming Soon"`, empty AccessToken/RefreshToken (unencrypted), `TokenExpiresAt = null`. The `TiktokChatEnabled` toggle in chat settings persists to this row and publishes `PlatformChatRoutingChangedEvent`, but no platform traffic ever exists to route.
- **OBS** — no entity, no settings row, no service. The audio browser source depends only on the `WebServerSettings` row (port) and the audio settings blob described in [Audio](../stream-tools/audio.md).

## Status & Known Issues

- **TikTok saves honest "Coming Soon" state (fixed 2026-08-15)** — finishing the stub wizard writes a `Status="Coming Soon"` row with `TokenExpiresAt = null`; nothing in the app treats it as connected. Previously it wrote a fake `"Connected"` row. Tracked in the [known issues ledger](../reference/known-issues.md) (KI-24 partially resolved).
- **OBS is real but entered from the sidebar, not the Platforms card** — the card still says "Coming Soon" (stale placeholder), while the OBS page (Full OBS hosting, Bot Studio, Collections) is implemented on top of `VyralBot.Integrations/Obs`. The [audio browser source](../stream-tools/audio.md) also still works.
- **No service-layer code for TikTok** — implementing TikTok for real means building an OAuth service plus a chat/event service under `VyralBot.Integrations` and wiring them in [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) — the wizard UI can then be pointed at them. (The Discord rewrite is the template to follow for a non-OAuth platform — see [Discord](discord.md).)
- **Discord is no longer a stub** — its former entries here (fake Connected row, IRC test message, Twitch copy/paste remnants) were all eliminated by the 2026-08-15 rewrite; Discord limitations now live on [Discord](discord.md) and in the [known issues ledger](../reference/known-issues.md) (KI-34).
