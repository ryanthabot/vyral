# TikTok, Discord & OBS

> These three platforms are placeholders: TikTok and Discord have full-looking setup wizards that mark the platform "Connected" with no backend behind it, and OBS is a "Coming Soon" card — the only real OBS feature in the app is the audio browser source, which does not use the OBS platform at all.

## Overview

The Platforms page shows six cards, and the honest status of the last three is:

- **TikTok** — a UI shell. The wizard looks real, but the OAuth pieces are commented-out TODO stubs. No TikTok service exists anywhere in the codebase.
- **Discord** — the same shell, plus a test-message routine that literally tries to speak IRC to a server Discord does not run. No Discord gateway/REST service exists.
- **OBS** — a "Coming Soon / Not Available" card. Deep OBS integration is planned in design docs at the repo root but unimplemented. The one working OBS touchpoint — adding VyralBot's audio page as an OBS browser source — lives in [Audio](../stream-tools/audio.md), not here.

This page exists so you do not lose time on them, and so developers know exactly what is stub vs. real before touching the code.

## User Guide

### TikTok and Discord: what actually happens

You can open each wizard and click through all four steps — the UI is fully built:

1. **Step 1: Sign In** — the embedded browser opens `https://www.tiktok.com/login` or Discord's login page. Signing in does nothing for VyralBot; nothing is captured.
2. **Step 2: Create Application** — the browser opens TikTok's or Discord's developer portal, and the wizard will accept a pasted Client ID/Secret, but they are never saved anywhere.
3. **Step 3: Authorize Account** — the wizard builds an OAuth URL from a hardcoded placeholder string; the callback handler always returns null. Authorization never completes.
4. **Step 4: Test Connection** — the test-message button cannot succeed (Discord's fails by design — see below).

Despite all that, **finishing either wizard writes a `Platform` row with `Status="Connected"` and empty tokens**. The Platforms card then displays the platform as connected, and Settings shows its (dead) per-platform card. Nothing consumes it — no messages arrive, nothing can be sent. If you did this by accident, just use DISCONNECT on the card to clear it.

**Bottom line: skip TikTok and Discord for now.** They are on the roadmap, not in the product.

### OBS: what you can actually use today

There is no OBS connection to configure. What you can do today is add VyralBot's **sound output** to OBS as a browser source:

1. Go to **Audio Settings** in VyralBot and copy the browser source URL (`http://localhost:5000/audio.html`).
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

### The Discord shell (with Twitch fingerprints)

[DiscordWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs) has the identical stub pattern (`TODO: Implement Discord OAuth service` line 18, no-op credential save/load lines 292-314, placeholder OAuth URL line 317, null callback line 410) — plus copy/paste remnants straight from the Twitch wizard:

- Its Step 1 logs `"Opening Twitch login page in WebView2"` (line 191).
- `SaveCredentialsToEnvironment` writes **`VYRALBOT_TWITCH_CLIENT_ID` / `VYRALBOT_TWITCH_CLIENT_SECRET`** environment variables — machine-level and user-level (lines 662-667).
- The save-failure log reads `"Failed to save Twitch configuration"` (line 486).

And the step-4 test message is Twitch's IRC client logic pointed at Discord's domain (`DiscordWebSetupViewModel.cs:521-531`):

```csharp
using var client = new System.Net.Sockets.TcpClient();
await client.ConnectAsync("discord.com", 6667);   // Discord does not run an IRC server

using var stream = client.GetStream();
using var reader = new System.IO.StreamReader(stream);
using var writer = new System.IO.StreamWriter(stream) { AutoFlush = true };

// Authenticate with OAuth token
await writer.WriteLineAsync($"PASS oauth:{_accessToken}");
await writer.WriteLineAsync($"NICK {_authorizedUsername}");
await writer.WriteLineAsync($"JOIN #{ChannelName.ToLower()}");
```

Discord has no IRC endpoint — the gateway is a WebSocket protocol, and chat messages go over REST. This TCP connection can never authenticate, so the test can only ever fail or hang.

### The fake "Connected" rows

Both stub wizards have a working `FinishSetupAutomaticallyAsync` that saves a real row (e.g. `DiscordWebSetupViewModel.cs:455-468`):

```csharp
var platform = new Platform
{
    Name = "Discord",
    Status = "Connected",     // <-- with empty tokens and no service behind it
    ...
};
platform.AccessToken = /* TODO: Encrypt */ (_accessToken ?? "");
platform.RefreshToken = /* TODO: Encrypt */ (_refreshToken ?? "");
platform.Status = "Connected";
```

`PlatformsViewModel` treats `Status == "Connected"` as truth for card rendering, so the UI faithfully displays a connection that does not exist. No runtime consumer reads the row — but note what *would* react to it: `PlatformsViewModel` connection views, the Settings page platform cards, and startup auto-connect logic all key off `Status == "Connected"`. A stub row can therefore cause the sidebar and Settings pages to render state that no service backs.

### OBS

- The Platforms card is static XAML: "Coming Soon" / "Not Available" (`PlatformsView.xaml:349-372`), and the Settings page has an OBS card with no navigation command at all.
- Repo-root design docs (`OBS_INTEGRATION_PLAN.md`, `OBS-addition.md`) describe plans to embed libobs and script the real OBS app — none of it is implemented.
- The only OBS-adjacent runtime code is the browser-source page: `wwwroot/audio.html` served by [WebServerService](../../src/VyralBot.Api/Services/WebServerService.cs) on port 5000 (URL pinned in `AudioSettingsViewModel.cs:36`), with a `/audio-ws` WebSocket endpoint broadcasting sound triggers to connected OBS browser-source pages. See [Web Server](../architecture/web-server.md).

## Files & Code

| File | Role |
|---|---|
| [TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) | TikTok wizard stub (TODO OAuth; writes fake Connected row) |
| [TikTokWebSetupView.xaml](../../src/VyralBot.UI/Views/TikTokWebSetupView.xaml) | TikTok wizard UI (fully built, cosmetically real) |
| [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) / [TiktokChatSettingsView.xaml](../../src/VyralBot.UI/Views/TiktokChatSettingsView.xaml) | TikTok chat-settings shell (enable toggle persists `TiktokChatEnabled`; nothing consumes it) |
| [DiscordWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs) | Discord wizard stub (TODO OAuth, IRC test message, Twitch env-var remnants) |
| [DiscordWebSetupView.xaml](../../src/VyralBot.UI/Views/DiscordWebSetupView.xaml) | Discord wizard UI |
| [DiscordChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordChatSettingsViewModel.cs) / [DiscordChatSettingsView.xaml](../../src/VyralBot.UI/Views/DiscordChatSettingsView.xaml) | Discord chat-settings shell |
| [PlatformsView.xaml](../../src/VyralBot.UI/Views/PlatformsView.xaml) | Hosts all six cards incl. the OBS "Coming Soon" placeholder (lines 349-372) |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Renders TikTok/Discord as connected purely from the DB row (disconnect ~lines 1548/1680) |
| [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | The real OBS touchpoint: `http://localhost:5000/audio.html` browser-source URL |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Serves `audio.html` + `/audio-ws` to OBS browser sources |

Wizard step destinations, for anyone wiring these up for real: the TikTok wizard navigates to `https://www.tiktok.com/login` (step 1) and `https://developers.tiktok.com/apps/` (step 2); the Discord wizard navigates to `https://discord.com/login` (step 1) and `https://discord.com/developers/applications` (step 2) — the pages and the paste-Client-ID popups all work; only the OAuth and service layers are missing.

## Data & Settings

- **TikTok/Discord `Platform` rows** — created by the stub wizards with `Status="Connected"`, empty AccessToken/RefreshToken (unencrypted), `TokenExpiresAt = Now+4h` on the Discord variant. The `TiktokChatEnabled` / `DiscordChatEnabled` toggles in chat settings persist to these rows and publish `PlatformChatRoutingChangedEvent`, but no platform traffic ever exists to route.
- **OBS** — no entity, no settings row, no service. The audio browser source depends only on the `WebServerSettings` row (port) and the audio settings blob described in [Audio](../stream-tools/audio.md).

## Status & Known Issues

- **TikTok and Discord show fake "Connected" state** — finishing their stub wizards writes `Status="Connected"` rows with empty tokens and no backend service; the Platforms and Settings pages then render them as connected. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Discord test message uses IRC** — `SendTestMessageAsync` opens a TCP connection to `discord.com:6667` with Twitch IRC handshaking (`DiscordWebSetupViewModel.cs:521-531`); Discord has no IRC server, so the test can never succeed. See the [known issues ledger](../reference/known-issues.md).
- **Twitch copy/paste remnants in the Discord wizard** — writes `VYRALBOT_TWITCH_*` environment variables (user-level too), logs "Opening Twitch login page" and "Failed to save Twitch configuration". See the [known issues ledger](../reference/known-issues.md).
- **OBS platform is entirely aspirational** — the card and the repo-root OBS design docs describe an integration that does not exist; only the [audio browser source](../stream-tools/audio.md) is real.
- **No service-layer code for either platform** — implementing TikTok/Discord for real means building OAuth services plus chat/event services under `VyralBot.Integrations` and wiring them in [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) — the wizard UI can then be pointed at them.
