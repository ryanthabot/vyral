# Stream FX / Smart Lights

## Overview
Smart-lights subsystem ("Stream FX"): flashes and on/off control of configured
lights, triggered from Discord mod commands, VyralDeck buttons and the Settings
page test button. Two providers behind one facade — Philips Hue (local bridge
v1 HTTP API, no SDK) and a generic webhook (Home Assistant / SmartThings /
LIFX cloud style).

## Source Files
- `src/VyralBot.Integrations/Lights/ILightsClient.cs` — provider contract; every method returns a user-facing result string and never throws
- `src/VyralBot.Integrations/Lights/HueLightsClient.cs` — Hue bridge v1 API client (flash with sRGB→CIE xy color conversion, snapshot + best-effort restore, power)
- `src/VyralBot.Integrations/Lights/WebhookLightsClient.cs` — POSTs `{action, color, times, intervalMs}` JSON with optional Bearer token
- `src/VyralBot.Integrations/Lights/LightsManager.cs` — facade routing to the configured provider ("Hue" | "Webhook" | "None"); config re-read per call so Settings changes apply without restart; holds the flash debounce lock (one flash loop at a time — concurrent triggers are dropped, not queued)
- `src/VyralBot.UI/Services/StreamEffectsConfigService.cs` — config KV facade + `StreamEffectsSnapshot`
- `src/VyralBot.UI/Services/StreamEffectSubscriber.cs` — EventBus subscriber: `StreamEffectEvent` → `ILightsClient` / `IStreamDeckService`; started once at app startup, never throws
- `src/VyralBot.Core/Services/StreamEffects/` — `StreamEffectEvent`, `StreamEffectPayloads`, `StreamEffectCommandRouter` (tier-gated `!flashlights` / `!lightson` / `!lightsoff` / `!streamdeck`, audited to ModActions)

## How it works
1. **Config** (per-profile config KV, category `StreamEffects`):
   `Lights.Provider`, `Lights.Hue.BridgeIp`, `Lights.Hue.AppKey`,
   `Lights.Hue.LightIds` (CSV), `Lights.Webhook.Url`, `Lights.Webhook.Token`,
   `Lights.Flash.Times`, `Lights.Flash.IntervalMs`, `Lights.Flash.Color`,
   `StreamDeck.Actions`. Load/save round-trip via
   `StreamEffectsConfigService.GetSnapshotAsync` / `SaveSnapshotCoreSettingsAsync`;
   the Hue app key and webhook token are DPAPI-wrapped at rest by that facade
   (see Security).
2. **Hue discovery/pairing** (Settings page): Discover calls
   `https://discovery.meethue.com` (manual IP entry as fallback — no local
   SSDP/mDNS scan); Pair POSTs `{"devicetype":"vyralbot#streamfx"}` to
   `http://<ip>/api` and stores the returned username (link-button flow; Hue
   error 101 → "press the link button" message).
3. **Triggers** (all funnel into the single `ILightsClient` singleton):
   - VyralDeck buttons `LightsFlash` / `LightsPower` → `DeckActionExecutor`
   - Discord mod commands (both `ModCommandEngine` and
     `StreamEffectCommandRouter` entry points) → `StreamEffectEvent` on the
     EventBus → `StreamEffectSubscriber`
   - Settings → Stream FX → Test Flash
4. **Error handling**: provider offline → friendly `⚠️/❌` result string (never
   an exception); HTTP timeout is 5s so a dead bridge fails fast; a failed or
   cancelled flash still attempts to restore the pre-flash light state.

## Not implemented (honest gaps)
- **No stream-event hooks**: follow/sub/raid events do NOT trigger light FX.
  There is no event→light mapping config and no pipeline from the alert/event
  system into `ILightsClient`. The only triggers are the three above.
- **No workflow node**: the visual workflow engine has no lights action; a
  workflow can only reach lights indirectly.
- **No scenes/groups**: individual light ids only (CSV), no Hue groups/scenes,
  no brightness/saturation control.
- **Discovery is cloud-only** (`discovery.meethue.com`); no local SSDP fallback.

## Security
- The Hue app key and webhook bearer token are stored per-profile in the
  config KV (SQLite) **DPAPI-wrapped** via `SecretProtector` (the `v1:`
  CurrentUser-scope envelope used for platform/bot secrets). The wrap lives at
  the `StreamEffectsConfigService` read/write seam: reads unwrap, writes wrap,
  so every caller (Settings VM, `LightsManager`, `StreamActionsService`) only
  ever sees plaintext in memory.
- **Lazy re-encrypt:** rows saved by older builds (plaintext, no `v1:` prefix)
  still read unchanged and are re-encrypted the next time their key is saved —
  no schema change, no destructive migration. An unusable blob (different
  Windows user, corrupted row) is passed through as-is, never destroyed.
- Neither value is ever logged (verified: `Lights/**` has no log statements;
  the config facade has no logger; Settings logs only bridge IP and counts).
- Both secrets are masked in Settings → Stream FX via PasswordBoxes
  (code-behind feeds `SettingsViewModel.HueAppKey` / `WebhookToken`, same
  pattern as the PIN boxes and Discord bot tokens).

## Tests
`tests/VyralBot.UI.Tests/LightsClientTests.cs` — Hue parse/flash/restore
request shapes, color conversion, unconfigured + invalid-color messages,
webhook payload + Bearer header, manager "None" routing.
