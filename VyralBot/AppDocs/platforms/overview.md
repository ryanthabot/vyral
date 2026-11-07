# Platforms

> The Platforms page is where you connect Twitch, Kick, YouTube, and Discord to Vyral (TikTok's wizard is a "Coming Soon" shell) — each through a built-in setup wizard that walks you from sign-in to a working connection. OBS is not connected here anymore: its card is a placeholder, and the real OBS integration lives on the sidebar's **OBS** page (see [TikTok & OBS](tiktok-discord-obs.md)).

## Overview

Vyral talks to each streaming platform differently, but from your point of view the experience is the same everywhere: open the **Platforms** page, click a platform card, and follow a four-step wizard inside the app. For Twitch, Kick, and YouTube the wizard embeds a real browser (WebView2), so you sign in to Twitch/Kick/Google exactly as you would on their websites, and Vyral captures the authorization it needs without you copying long tokens by hand. Discord takes a different route — a **bot-token wizard**: you create a Discord application, enable one intent, copy the bot token, invite the bot to your server, and paste the token ([Discord](discord.md) documents every step).

Once connected, a platform feeds two things into the app: **chat messages** (shown in the unified [Chat Window](../chat/chat-window.md)) and **events** (follows, subs, raids, cheers, and so on — see [Event Reactions](../chat/event-reactions.md)). Connected platforms also get their own dashboard page with a live stream preview where available.

Current connection status at a glance:

| Platform | Chat in | Chat out | Events | State |
|---|---|---|---|---|
| [Twitch](twitch.md) | Yes (WebSocket IRC) | Yes | Follow/sub/resub/gift/raid/cheer/**channel-points redemption** via EventSub WebSocket; full Helix mod enforcement (timeout/ban/delete) | Fully implemented |
| [Kick](kick.md) | Yes (Pusher WebSocket) | Yes (sent as the broadcaster) | Hybrid: Pusher + webhooks via ngrok | Fully implemented |
| [YouTube](youtube.md) | Yes (polling, only while live) | Yes (only while live) | Chat messages + memberships + structured Super Chat/Super Sticker events; command timers (while live) | Implemented, chat + special events |
| [Discord](discord.md) | Yes (Discord.Net gateway, every guild channel + DMs, multiple bots) | Yes (per bot, to that bot's selected channel) | Chat messages; member-join/boost events wired but **intent-gated off** (see [Discord](discord.md#member-joins--boosts-intent-gated)) | Implemented, chat + multi-bot manager |
| [TikTok](tiktok-discord-obs.md) | No | No | No | UI shell only — backend not built (wizard saves "Coming Soon") |
| OBS | — | — | — | Placeholder card on this page — OBS itself is integrated via the sidebar **OBS** page (Full OBS hosting, Bot Studio, Collections; see [TikTok & OBS](tiktok-discord-obs.md)) |

Twitch EventSub connects once `Platform.UserId` is set — both Twitch auth flows populate it automatically now (fixed 2026-08-15); older connections need one re-auth. See [Twitch](twitch.md) and the [known issues ledger](../reference/known-issues.md).

## User Guide

### Opening the Platforms page

Click **Platforms** in the left sidebar (it is grouped under the Platforms expander). The page header reads **Platform Connections** with the subtitle **Select a platform to connect**, and shows a grid of six cards: Twitch, Kick, YouTube, TikTok, Discord, and OBS. Each card shows the platform logo, a description ("Live streaming platform" for the streaming services, "Communication platform" for Discord), and the card lights up when the platform is connected.

### Connecting a platform (the wizard)

All working wizards follow the same four-step shape. Steps for each specific platform are documented on their own pages: [Twitch](twitch.md), [Kick](kick.md), [YouTube](youtube.md), and [Discord](discord.md) — the Discord wizard uses a bot-token flow instead of OAuth (no Client ID/Secret paste, no redirect URI), so its four steps differ in content. For the OAuth platforms in general:

1. Click the platform card. A setup wizard opens inside the page.
2. **Step 1 — Sign in.** The wizard opens an embedded browser on the platform's login page. Log in as normal, then continue.
3. **Step 2 — Create an application.** The wizard opens the platform's developer console in the same embedded browser. You create an app there and paste its **Client ID** and **Client Secret** into a popup when the wizard asks. The wizard also shows you the exact **redirect URI** (`http://localhost:3000`) to register in the app settings, with a copy button.
4. **Step 3 — Authorize.** The wizard opens the platform's OAuth consent page. When you approve, the browser briefly tries to navigate to `http://localhost:3000` — the wizard intercepts that navigation behind the scenes, so you never see an error page. If interception fails for any reason, a manual fallback lets you paste the callback URL yourself.
5. **Step 4 — Test.** Send a test message to confirm the connection, then finish.

When the wizard completes, the card flips to a **connection view** showing your account name, masked Client ID/Secret, an "OAuth Permissions" checklist, and **RECONNECT** / **DISCONNECT** buttons.

A note on the "OAuth Permissions" checklist on the connection views: it is decorative text baked into the page layout, not a live list of what was actually granted. The Twitch checklist, for example, always shows the same ~25 permission categories whether or not the OAuth flow succeeded.

### What happens after connecting

- The platform's chat appears in the [Chat Window](../chat/chat-window.md), and messages you send from the chat box are delivered to that platform ([Sending Messages](../chat/sending-messages.md)).
- Follows, subs, raids, and similar events start firing [event reactions](../chat/event-reactions.md) — sounds, alerts, auto-replies, currency bonuses.
- Twitch and Kick get a **dashboard** entry in the sidebar with your connection status and (on a capable machine) an embedded live stream preview of your own channel. Discord gets a permanent sidebar entry opening the [Discord Bots page](discord.md#multiple-bots) — a multi-bot manager with per-bot server/channel dashboards rather than a stream preview ([Platform Dashboards](dashboards.md)).
- On the next app launch, platforms whose status is "Connected" are **auto-reconnected** — you only run the wizard once. Discord bots saved as connected on the bots page auto-start too (tokens already served by the setup bot are skipped so nothing runs twice).

### Reconnecting and disconnecting

From the connection view:

- **RECONNECT** — for Twitch this re-opens the setup wizard at the OAuth step so you can re-authorize; for Discord it re-opens the wizard at the invite/connect step using the stored token; for other platforms it re-runs the stored-token connection path.
- **DISCONNECT** — signs the platform out. Be aware the behavior differs by platform (see the lifecycle table below): disconnecting **Twitch wipes its entire saved configuration** (you must run the wizard again to reconnect), while Kick/YouTube/TikTok/Discord keep their row in the database with credentials cleared.

### Per-platform dashboards

Connected Twitch and Kick entries in the sidebar open a dashboard page: connection status ("Connected as {username}"), plus an embedded stream preview pointing at the public player for your channel (`player.twitch.tv` / `player.kick.com` with `parent=localhost`). The Kick dashboard status distinguishes "Disconnected (reconnect on app restart)" for the case where chat dropped but the saved configuration survives. Discord's sidebar entry (always visible) opens the [Discord Bots page](discord.md#multiple-bots) — the multi-bot manager; each bot's card opens a per-bot dashboard with the servers list and active-channel picker described in [Discord](discord.md) — no stream preview (Discord has none to embed). All are covered in [Platform Dashboards](dashboards.md).

## How It Works (Developer)

### The wizard framework

Two abstract base classes in `VyralBot.UI` implement the wizard machinery:

- [BaseWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs) — the generic four-step shell: `CurrentStep`, success screen, modal-input helpers (`OpenModal` / `SaveModal`), WebView2 lifecycle (`WebView2InitializeRequested` / `WebView2DisposeRequested` events that the view handles by creating/destroying the control), and the `SetupCompleted` event consumed by `PlatformsViewModel`.
- [BaseOAuthSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs) — the OAuth pattern on top: abstract `SaveCredentialsAsync`, `LoadCredentialsAsync`, `BuildAuthorizationUrl`, `HandleOAuthCallbackAsync`, `SendTestMessageAsync` members, the embedded OAuth step, and a manual "paste callback URL" fallback (`SubmitCallbackUrlAsync`).

**Redirect interception instead of a listener.** Every platform's redirect URI is fixed to `http://localhost:3000` (Twitch exact, Kick `…/callback`, YouTube `…/oauth2callback`). The wizard does not run a web server there; instead it subscribes to the WebView2 `NavigationStarting` event, and when the OAuth page tries to navigate to `localhost:3000`, it cancels the navigation client-side and parses the authorization code out of the URL (`BaseOAuthSetupViewModel.cs:371-391`). The one exception is Twitch's alternate system-browser flow, which does run a real `HttpListener` on port 3000 with a 5-minute timeout (see [Twitch](twitch.md)).

**An honest caveat about inheritance:** the per-platform wizards (Twitch, Kick, YouTube, TikTok, Discord) are standalone `ObservableObject`s that each carry their own copy of this logic rather than inheriting the base classes — the class doc on `BaseWebSetupViewModel` itself says "carbon copy of the Twitch/Kick setup wizard pattern". Only `NgrokSetupWizardViewModel` actually extends `BaseOAuthSetupViewModel`. The Discord wizard diverges on purpose (bot tokens, not OAuth — see [Discord](discord.md)), but still duplicates the step/modal/WebView2 plumbing. Expect duplicated step/OAuth plumbing across the five wizard ViewModels.

### Connect, disconnect, and their inconsistencies

`PlatformsViewModel` is the state machine for the page. Per platform it tracks status text, `Is*Connected`, username, wizard visibility (`Show*Setup`), and connection-view visibility (`Show*Connection`), mirroring the `Platform` database row via `IDataService.GetPlatformByNameAsync(...)` — "connected" means `Status == "Connected"`. It also probes for credential files on disk (`twitch_credentials.json`, `kick_credentials.json` under the profile credentials folder). When a wizard fires `SetupCompleted`, the VM reloads the row, auto-connects the platform service, and notifies `MainViewModel` (sidebar/chat refresh) via callbacks.

The lifecycle differs per platform in ways that matter:

| Platform | Connect path | Auto-connect on launch | DISCONNECT does |
|---|---|---|---|
| Twitch | `ConnectTwitchAsync` (`PlatformsViewModel.cs:638`) — OAuth, tokens stored **DPAPI-encrypted** | `LoadTwitchPlatformAsync` (`PlatformsViewModel.cs:564`) decrypts and connects; on failure status shows "Connected (Chat offline)" | **Deletes the Platform row entirely** and disposes the wizard's WebView2 (`PlatformsViewModel.cs:828`) |
| Kick | Wizard + `KickLibOAuthService`; tokens stored **unencrypted** | Attempts a token refresh first, then connects | Keeps the row, nulls AccessToken/RefreshToken/UserId/ChannelId/ChatroomId/ClientId/ClientSecret (`PlatformsViewModel.cs:1232`) |
| YouTube | Wizard saves tokens; auto-connect with stored tokens | Yes (chat only) | Keeps the row, nulls credentials |
| Discord | Wizard validates the bot token, then `DiscordService.ConnectAsync` (setup-bot token stored on the row, plaintext; every managed bot's `DiscordBots.Token` is DPAPI-wrapped); additional bots via Quick Add on the [Discord Bots page](discord.md#multiple-bots) (`DiscordBots` rows) | Yes — `LoadAndConnectDiscordAsync` (`MainViewModel.cs:579`) connects the setup bot from the stored token, then `StartAllSavedAsync` (`MainViewModel.cs:298`) starts every saved bot (duplicate tokens skipped) | Keeps the row, nulls token/identity fields (the selected channel survives). Bots page **REMOVE** deletes the bot's `DiscordBots` row (after stopping it); **STOP** keeps the row with its token |
| TikTok | Wizard stub writes `Status="Coming Soon"` with empty tokens | No real service exists | Keeps the row, nulls credentials |

`ReconnectTwitchAsync` (`PlatformsViewModel.cs:775`) deliberately re-opens the wizard at step 3 (the OAuth step) instead of silently refreshing.

On startup, `MainViewModel.CheckPlatformConnectionsAsync` auto-connects every platform whose DB status is "Connected", forcing `<Platform>ChatEnabled = true` as a side effect of the first successful auto-connect — **intended behavior** (a connected platform shows in the unified feed by default; see KI-14 in the [known issues ledger](../reference/known-issues.md)). Details in [Startup & Lifecycle](../architecture/startup-lifecycle.md).

### How platform events fan into the app

All platform services and the webhook controller publish typed events onto a single in-process [Event Bus](../architecture/event-bus.md) (`EventBus.cs` — bounded channel of 500 with `DropOldest`, max 4 concurrent handlers, exact-type routing). Consumers include `ChatViewModel` (chat UI, which subscribes to every platform event type), the trigger engine behind [commands](../chat-features/commands.md) and [auto-replies](../chat-features/auto-replies.md), [sounds](../stream-tools/sounds.md), [statistics](../stream-tools/statistics.md), and variable updates. Discord publishes its typed `DiscordChatMessageEvent` plus the platform-agnostic `ChatMessageReceivedEvent`, so the whole command pipeline runs there too.

Two side-effect writes happen inside the integration services themselves: Kick's follower count is written to the `kick-followercount` DB variable, and Twitch EventSub increments `twitch-followercount` / `twitch-subscribercount` (only when those variables already exist). `PlatformConnectionChangedEvent` is published by the Kick, YouTube, and Discord services on connect/disconnect so the UI can react.

Per-platform transports, event types, and credentials are documented on each platform's page.

## Files & Code

Shared Platforms-page and wizard-framework files (per-platform files are listed on their own pages):

| File | Role |
|---|---|
| [PlatformsView.xaml](../../src/VyralBot.UI/Views/PlatformsView.xaml) | Platforms page layout: card grid, wizard containers, connection views with RECONNECT/DISCONNECT |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Page state machine: platform status, wizard/connection visibility, connect/disconnect/reconnect commands |
| [BaseWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs) | Generic 4-step wizard shell (steps, modals, WebView2 lifecycle, `SetupCompleted`) |
| [BaseOAuthSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs) | OAuth wizard pattern: abstract credential/callback hooks, `localhost:3000` interception, manual callback fallback |
| [TwitchDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs) / [TwitchDashboardView.xaml](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml) | Twitch dashboard with embedded stream preview |
| [KickDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs) / [KickDashboardView.xaml](../../src/VyralBot.UI/Views/KickDashboardView.xaml) | Kick dashboard with embedded stream preview |
| [Platform.cs](../../src/VyralBot.Data/Entities/Platform.cs) | Entity for the per-profile `Platforms` table (one row per platform) |
| [EventBus.cs](../../src/VyralBot.Core/Services/EventBus/EventBus.cs) | The bus all platform events are published to |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Startup auto-connect and profile-switch disconnect logic |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | DI registrations for all platform services and wizard ViewModels |

The redirect interception that makes the wizards work without a local web server (`BaseOAuthSetupViewModel.cs:371-391`):

```csharp
private async void OnOAuthNavigationStarting(object? sender,
    Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
{
    if (e.Uri.StartsWith("http://localhost:3000"))
    {
        _logger.LogInformation("OAuth callback detected: {Uri}", e.Uri);

        e.Cancel = true;   // never actually navigate to localhost:3000

        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
        }

        ShowBrowser = false;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await HandleCallbackAsync(e.Uri);   // parse ?code=... and finish
        });
    }
}
```

And the disconnect inconsistency in one contrast — Twitch deletes the row (`PlatformsViewModel.cs:828`):

```csharp
// Remove from database
var platform = await _dataService.GetPlatformByNameAsync("Twitch");
if (platform != null)
{
    await _dataService.DeletePlatformAsync(platform.Id);
}
```

…while Kick keeps the row and clears fields (`PlatformsViewModel.cs:1232`):

```csharp
platform.AccessToken = null;
platform.RefreshToken = null;
platform.UserId = null;
platform.ChannelId = null;
platform.ChatroomId = null;
platform.ClientId = null;
platform.ClientSecret = null;
```

## Data & Settings

Each platform is one row in the per-profile `Platforms` table (see [Data Layer](../architecture/data-layer.md) and the [database reference](../reference/database-reference.md)). Key fields on [Platform.cs](../../src/VyralBot.Data/Entities/Platform.cs): `Name`, `Status` ("Connected"/"Disconnected"/"Error"), `AccessToken`/`RefreshToken` (DPAPI-encrypted for Twitch, plaintext for Kick/YouTube/Discord), `TokenExpiresAt`, `Username`, `UserId`, `ChannelId` (the selected channel for Discord), `ChatroomId` (Kick only), `ClientId`/`ClientSecret` (the **ClientSecret column is DPAPI-wrapped at rest** for every platform via `SecretProtector` — see [Settings → Security](../settings/overview.md#security-category)), plus per-platform `*ChatEnabled` / `*ChatAutoStart` flags.

Credential files on disk, under the current [profile](../getting-started/profiles.md) folder: `twitch_credentials.json` (Client ID/Secret, plaintext JSON) and `kick_credentials.json` (probed by `PlatformsViewModel`; the active Kick wizard saves credentials to the DB row). Per-platform chat display settings live in [Chat Settings](../chat/chat-settings.md); the webhook/ngrok side of Kick lives in [Web Server & ngrok](../settings/web-server-ngrok.md).

## Status & Known Issues

- **Inconsistent disconnect semantics** — DISCONNECT deletes the row for Twitch but only clears credentials for Kick/YouTube/TikTok/Discord; reconnecting Twitch always requires the full wizard. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Twitch EventSub connects (fixed 2026-08-15)** — both auth flows now persist `Platform.UserId` (Helix `GET /users`), so Twitch follow/sub/raid/cheer events arrive after (re)authorization. Details on [Twitch](twitch.md); see the [known issues ledger](../reference/known-issues.md) (KI-2 resolved).
- **TikTok saves honest "Coming Soon" state (fixed 2026-08-15)** — the stub wizard saves a `Status="Coming Soon"` row with empty tokens and no backend service; nothing treats it as connected. **Discord is real now** — rewritten the same day from a stub into a full bot-token integration (see [Discord](discord.md)). See [TikTok & OBS](tiktok-discord-obs.md) and the [known issues ledger](../reference/known-issues.md).
- **"OAuth Permissions" checklist is hardcoded XAML** — not derived from actually granted scopes.
- **Wizard logic is copy/pasted** across the five platform ViewModels rather than shared through the base classes — drift risk for any future change.
- **Kick webhooks resubscribe automatically when the ngrok URL rotates** (fixed 2026-08-15) — see [Kick](kick.md) and [Web Server & ngrok](../settings/web-server-ngrok.md).
