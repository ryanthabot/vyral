# API, Web Server & ngrok

> The API & Webhooks category controls Vyral's embedded Kestrel web server and its ngrok tunnel — the shared infrastructure behind Kick webhooks, the OBS audio browser source, and the public health endpoint.

## Overview

Vyral runs a small ASP.NET Core (Kestrel) web server inside the desktop process, listening on localhost (default port 5000), and optionally exposes it to the internet through an ngrok tunnel. Everything internet-facing flows through this one stack:

- **Kick webhooks** — channel events (follows, subscriptions, moderation, stream metadata) delivered from Kick's servers to `{public-url}/webhooks/kick`. This is the only platform that needs inbound webhooks: Twitch uses an outbound EventSub WebSocket and YouTube polls the API.
- **Browser-source audio** — the server hosts `audio.html` and the `/sounds/*` files plus a `/audio-ws` WebSocket, so OBS (or any browser) can play event sounds on the stream. See [Browser Sources](../stream-tools/browser-sources.md).
- **Health endpoint** — `GET /webhooks/health` returns a simple JSON status payload, used by the TEST HEALTH button and the setup wizard.

The whole stack is managed from **Settings → API & Webhooks** and one prerequisite: the 4-step **Ngrok Setup Wizard**.

## User Guide

### First-time setup: the Ngrok Setup Wizard

Click **RUN SETUP** on the Ngrok Tunnel card. The wizard has four steps:

1. **Install ngrok** — opens the Microsoft Store page for ngrok and verifies installation by running `ngrok version`. ngrok must be on your PATH.
2. **Sign in** — opens https://dashboard.ngrok.com/login so you can create/log into an ngrok account.
3. **Copy your authtoken** — opens https://dashboard.ngrok.com/get-started/your-authtoken in an embedded browser, with a modal to paste the token into.
4. **Verify** — the TEST CONNECTION button checks the token's format, starts the web server and a real tunnel, fetches `{public-url}/webhooks/health` through the tunnel, and tears both back down. Passing this step means the entire pipeline works.

Finishing the wizard saves the token per-profile (a plaintext `ngrok_credentials.json` inside the profile folder) **and** writes it into ngrok's own machine-level config with `ngrok config add-authtoken` — so the token JSON is per-profile, but the ngrok agent config on your machine is shared across all profiles.

### The main screen

Three cards (plus a static "About Webhook Infrastructure" card):

**Ngrok Tunnel.** Shows RUN SETUP when setup is incomplete (ngrok missing or no token), otherwise CONFIGURE, which opens the inline ngrok configuration screen (see below).

**Webhook Server.** START / STOP buttons, a status dot and text, the current public URL, and two helpers:

- **COPY URL** — copies the tunnel URL to the clipboard.
- **TEST HEALTH** — opens `{public-url}/webhooks/health` in your browser to verify the tunnel end-to-end.
- **STOP is blocked while features depend on the server.** If Kick webhooks are enabled, stopping is refused and the card lists the active dependencies. Disable the webhooks first (in the [Kick settings](overview.md)), then stop.

**Server Configuration.**

- **Auto-start on Launch** — persists immediately when toggled. Turning it ON while stopped also starts the whole stack right away. On subsequent launches the stack starts automatically only if the toggle is set *and* a saved ngrok token exists.
- **Local Port** — default 5000; disabled while the server is running. Note the OBS audio browser source uses port 5000 (the page itself is port-agnostic now, but the server port must stay 5000 for the audio source — see [Browser Sources](../stream-tools/browser-sources.md)).

### The ngrok configuration screen

Reached via CONFIGURE on the Ngrok Tunnel card: an Authentication Token text box and a Region combo (us, eu, ap, au, sa, jp, in; default us) with a SAVE SETTINGS button.

Edits to the Authentication Token here **persist** (fixed 2026-08-15): Save calls `NgrokService.SaveAuthTokenAsync`, which writes the per-profile `ngrok_credentials.json` — the same file the setup wizard writes. Region and the other fields save normally too. (The dead `IsEnabled` flag was removed from the ViewModel; the entity column remains but is read by nothing.)

### What you get once it runs

- **Kick webhook events** start arriving. Enable the webhooks toggle in the Kick settings page (Settings → Platforms → Kick); it requires the server to be running, a stored Kick access token, and your Kick user ID. Once enabled, the server cannot be stopped until you disable them again.
- **Browser-source audio** — `http://localhost:5000/audio.html` becomes usable as an OBS browser source for event sounds (volume honored); see [Audio Settings](../stream-tools/audio.md) and [Sounds](../stream-tools/sounds.md).
- **A public health endpoint** — anyone with the tunnel URL can check `GET /webhooks/health` and get a small JSON status payload.
- **Auto-reconnect on launch** — with Auto-start on Launch enabled, each start of Vyral brings the whole stack up automatically during profile load.
- **Automatic Kick webhook healing on URL rotation** — if the free-plan tunnel URL changes, the coordinator refreshes the Kick token if needed and resubscribes the webhooks to the new URL (fixed 2026-08-15); the manual off/on toggle is only the fallback if that fails.

### Troubleshooting

| Symptom | Cause and fix |
|---|---|
| START fails with "port may be in use" | Another process (or a second Vyral remnant) holds the port. Free it or change Local Port while stopped. Remember the audio browser source needs port 5000. |
| "Ngrok is not installed" | ngrok is not on PATH. Install it (wizard step 1 links the Microsoft Store page) and confirm `ngrok version` works in a terminal. |
| Auto-start does nothing at launch | The toggle only fires when a saved ngrok token exists for the *current profile* — the token file is per-profile. Re-run the wizard (or save a token on the configuration screen) for that profile. |
| Kick webhook toggle will not enable | Requires the webhook server running, a Kick access token in the database, and a stored user ID. Start the server first; reconnect Kick if credentials are missing. |
| Public URL changed and Kick events stopped | Usually heals itself — the coordinator resubscribes automatically. If it warned instead, toggle Kick webhooks off and on to resubscribe manually. |
| STOP is refused | A feature (Kick webhooks) depends on the server; the card lists active dependencies. Disable them first. |

## How It Works (Developer)

The full internals — middleware order, signature validation, subscription orchestration, process management — are documented in [Web Server (Architecture)](../architecture/web-server.md). The essentials for this settings page:

- `WebServerSettingsViewModel.InitializeAsync()` loads the `WebServerSettings` DB row plus the ngrok token from the per-profile JSON, and computes `IsSetupComplete = ngrok on PATH && token non-empty` by spawning `ngrok version`.
- START requires setup to be complete, saves settings, then calls `WebhookCoordinatorService.StartAsync()` — Kestrel first, then the tunnel, then persisting `CurrentTunnelUrl` and `LastStarted` back to the DB.
- STOP is gated by `WebhookCoordinatorService.CanDisableWebServer()`, which consults the `WebServerDependencyTracker` ("Kick" registers a dependency while webhooks are active).
- `OnAutoStartOnLaunchChanged` persists immediately and, if turned ON while stopped, starts the stack on the spot (`WebServerSettingsViewModel.cs:482-499`).
- Boot-time auto-start is handled once, in `WebhookCoordinatorService.InitializeAsync()` during profile load — deliberately not repeated elsewhere to avoid a double start/stop cycle.

## Files & Code

| File | Role |
|---|---|
| [WebServerSettingsView.xaml](../../src/VyralBot.UI/Views/WebServerSettingsView.xaml) | Both screens: the three main cards and the inline ngrok token/region editor |
| [WebServerSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs) | Load/save, start/stop with dependency gating, setup-completeness check, auto-start side effect |
| [NgrokSetupWizardViewModel.cs](../../src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs) | The 4-step wizard, including the end-to-end tunnel health test and the token save + `ngrok config add-authtoken` write |
| [WebServerEntities.cs](../../src/VyralBot.Data/Entities/WebServerEntities.cs) | `WebServerSettings` entity (single row, Id=1) and `WebhookSubscription` |
| [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) | Orchestrates Kestrel + ngrok + Kick subscriptions; auto-start; URL-change warning |
| [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) | Spawns the ngrok process, polls its local API for the public URL, monitors for changes, persists the per-profile token |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | The embedded Kestrel host: controllers, validation middleware, static files, `/audio-ws` |

The settings-page save path — token and DB fields together (the token write was added 2026-08-15):

```csharp
// WebServerSettingsViewModel.cs — SaveSettingsAsync
var settings = new WebServerSettings
{
    Id = 1,
    Port = Port,
    NgrokRegion = NgrokRegion,
    AutoStartOnLaunch = AutoStartOnLaunch,
    CurrentTunnelUrl = PublicUrl
};
await _dataService.SaveWebServerSettingsAsync(settings);

// The Authentication Token persists via the ngrok service (same file the wizard writes)
if (!string.IsNullOrWhiteSpace(NgrokAuthToken))
    await _ngrokService.SaveAuthTokenAsync(NgrokAuthToken);
```

The wizard's completion path — the only place the token is persisted, in two places at once:

```csharp
// NgrokSetupWizardViewModel.cs — OnCompleteSetupAsync
await _ngrokService.SaveAuthTokenAsync(_savedNgrokAuth);   // <profile>/ngrok_credentials.json
Process.Start(new ProcessStartInfo
{
    FileName = "ngrok",
    Arguments = $"config add-authtoken {_savedNgrokAuth}", // machine-level ngrok config
    ...
});
```

## Gotchas / Status & Known Issues

- **Token edits in the Ngrok Configuration screen persist** (fixed 2026-08-15) — Save writes `ngrok_credentials.json` through `NgrokService.SaveAuthTokenAsync`, the same file the wizard writes.
- **`WebServerSettings.IsEnabled` is a legacy column** — removed from the ViewModel on 2026-08-15; the column still exists in the table but nothing reads or writes it.
- **URL rotation heals automatically** — the coordinator refreshes the Kick OAuth token if needed and resubscribes the webhooks to the new URL; the manual off/on toggle remains only as the failure fallback (fixed 2026-08-15).
- **The audio browser source needs port 5000** — `audio.html` is port-agnostic now (it derives its URLs from `window.location`), but OBS must point at the port the server actually listens on, and the server-side port is what this page configures; the Audio page pins its display to 5000 ([Browser Sources](../stream-tools/browser-sources.md)).
- **The token file is plaintext** per profile, and the wizard also writes machine-level state (the `ngrok config add-authtoken` write is intentional — the ngrok agent needs it). The legacy `VYRALBOT_NGROK_CLIENT_ID/SECRET` user-environment writes are gone (removed 2026-08-15).
- **`NgrokSetupView.xaml` / `NgrokSetupViewModel.cs` are dead code** — a previous-generation UI never referenced by navigation; the active flow is `NgrokSetupWizardView(ViewModel)`.
