# TikTok & OBS

> These two platforms are placeholders: TikTok has a full-looking setup wizard that marks the platform "Coming Soon" with no backend behind it, and OBS is a "Coming Soon" card — the only real OBS feature in the app is the audio browser source, which does not use the OBS platform at all. (Discord used to live on this page as a third stub; it is a fully implemented platform now — see [Discord](discord.md).)

## Overview

The Platforms page shows six cards. Twitch, Kick, YouTube, and [Discord](discord.md) are real; the honest status of the remaining two is:

- **TikTok** — a UI shell. The wizard looks real, but the OAuth pieces are commented-out TODO stubs. No TikTok service exists anywhere in the codebase.
- **OBS** — a "Coming Soon / Not Available" card. Deep OBS integration is planned in design docs at the repo root but unimplemented. The one working OBS touchpoint — adding Vyral's audio page as an OBS browser source — lives in [Audio](../stream-tools/audio.md), not here.

This page exists so you do not lose time on them, and so developers know exactly what is stub vs. real before touching the code.

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

There is no OBS connection to configure. What you can do today is add Vyral's **sound output** to OBS as a browser source:

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

- The Platforms card is static XAML: "Coming Soon" / "Not Available" (`PlatformsView.xaml:349-372`), and the Settings page has an OBS card with no navigation command at all.
- Repo-root design docs (`OBS_INTEGRATION_PLAN.md`, `OBS-addition.md`) describe plans to embed libobs and script the real OBS app — none of it is implemented.
- The only OBS-adjacent runtime code is the browser-source page: `wwwroot/audio.html` served by [WebServerService](../../src/VyralBot.Api/Services/WebServerService.cs) on port 5000 (URL pinned in `AudioSettingsViewModel.cs:36`), with a `/audio-ws` WebSocket endpoint broadcasting sound triggers to connected OBS browser-source pages. See [Web Server](../architecture/web-server.md).

## Files & Code

| File | Role |
|---|---|
| [TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) | TikTok wizard stub (TODO OAuth; writes honest "Coming Soon" row) |
| [TikTokWebSetupView.xaml](../../src/VyralBot.UI/Views/TikTokWebSetupView.xaml) | TikTok wizard UI (fully built, cosmetically real) |
| [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) / [TiktokChatSettingsView.xaml](../../src/VyralBot.UI/Views/TiktokChatSettingsView.xaml) | TikTok chat-settings shell (enable toggle persists `TiktokChatEnabled`; nothing consumes it) |
| [PlatformsView.xaml](../../src/VyralBot.UI/Views/PlatformsView.xaml) | Hosts all six cards incl. the OBS "Coming Soon" placeholder (lines 349-372) |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Renders TikTok as connected purely from the DB row (disconnect ~line 1548) |
| [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | The real OBS touchpoint: `http://localhost:5000/audio.html` browser-source URL |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Serves `audio.html` + `/audio-ws` to OBS browser sources |

Wizard step destinations, for anyone wiring TikTok up for real: the wizard navigates to `https://www.tiktok.com/login` (step 1) and `https://developers.tiktok.com/apps/` (step 2) — the pages and the paste-Client-ID popups all work; only the OAuth and service layers are missing.

## Data & Settings

- **TikTok `Platform` row** — created by the stub wizard with `Status="Coming Soon"`, empty AccessToken/RefreshToken (unencrypted), `TokenExpiresAt = null`. The `TiktokChatEnabled` toggle in chat settings persists to this row and publishes `PlatformChatRoutingChangedEvent`, but no platform traffic ever exists to route.
- **OBS** — no entity, no settings row, no service. The audio browser source depends only on the `WebServerSettings` row (port) and the audio settings blob described in [Audio](../stream-tools/audio.md).

## Status & Known Issues

- **TikTok saves honest "Coming Soon" state (fixed 2026-08-15)** — finishing the stub wizard writes a `Status="Coming Soon"` row with `TokenExpiresAt = null`; nothing in the app treats it as connected. Previously it wrote a fake `"Connected"` row. Tracked in the [known issues ledger](../reference/known-issues.md) (KI-24 partially resolved).
- **OBS platform is entirely aspirational** — the card and the repo-root OBS design docs describe an integration that does not exist; only the [audio browser source](../stream-tools/audio.md) is real.
- **No service-layer code for TikTok** — implementing TikTok for real means building an OAuth service plus a chat/event service under `VyralBot.Integrations` and wiring them in [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) — the wizard UI can then be pointed at them. (The Discord rewrite is the template to follow for a non-OAuth platform — see [Discord](discord.md).)
- **Discord is no longer a stub** — its former entries here (fake Connected row, IRC test message, Twitch copy/paste remnants) were all eliminated by the 2026-08-15 rewrite; Discord limitations now live on [Discord](discord.md) and in the [known issues ledger](../reference/known-issues.md) (KI-34).
