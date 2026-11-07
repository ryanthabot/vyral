# Vyral

> Open-source multi-platform chat bot and stream assistant for Twitch, Kick, YouTube, and Discord — one app, one chat feed, every platform at once.

Welcome to the official Vyral documentation. These pages ship with the app and live in the repository (`AppDocs/`), so they work in two places:

- **In the app** — Home → Documentation opens the built-in documentation browser with a navigation tree, search, back/forward history, and clickable cross-links. Links to source files open the actual code in a read-only code viewer.
- **On GitHub / any markdown viewer** — the exact same files render as normal markdown; links between pages and to source files are standard relative links.

Every page covers its feature for **two audiences at once**: a *User Guide* for streamers who just want to use the feature, and *How It Works / Files & Code* sections for developers and AI agents who want to understand or modify the implementation. A *Status & Known Issues* section on each page tells you honestly what works, what is stubbed, and what is broken in the current build — the full ledger lives in [Known Issues](reference/known-issues.md).

## What Vyral does

| Area | What you get |
|---|---|
| **[Unified chat](chat/chat-window.md)** | One merged chat feed from Twitch, Kick, YouTube, and Discord with platform badges, colors, and history. Send to one platform or broadcast to all connected platforms at once. |
| **[Platforms](platforms/overview.md)** | OAuth connection wizards for Twitch, Kick, and YouTube, a bot-token wizard plus a multi-bot manager for Discord (any number of bots, each with its own active channel and command scope), auto-reconnect, and per-platform dashboards (live stream previews for Twitch/Kick, per-bot server/channel managers for Discord). TikTok is planned (UI present, backend not yet built). |
| **[Event reactions](chat/event-reactions.md)** | Follows, subs, raids, cheers, hosts, and more each trigger up to five effects: a chat announcement, a sound, a visual alert, a currency bonus, and a customizable auto-reply. |
| **[Chat features](chat-features/commands.md)** | Chat commands — every command, built-in or custom, edits in a drag-and-drop visual flow editor that adopts the command on save — plus template variables, auto-replies, conditional rules, event history, an event testing console, a community [marketplace](chat-features/marketplace.md) of shareable command flows, and cross-platform [account linking](chat-features/account-linking.md); the full command pipeline (commands, currency, giveaways, polls, watchtime) runs from live chat. The [Automation](chat-features/automation.md) page hosts the same editor for event/timer workflows, including real Discord `/` commands. |
| **[Stream tools](stream-tools/giveaways.md)** | Giveaways, polls, watchtime tracking, statistics, a full currency & rewards economy, sounds, and OBS browser-source audio. |
| **[Web server + ngrok](settings/web-server-ngrok.md)** | An embedded local web server receives Kick webhooks through an ngrok tunnel and serves the OBS audio browser source. |
| **[Profiles](getting-started/profiles.md)** | Fully portable per-profile data — separate database, credentials, and settings per persona — with PIN protection and hot switching. |
| **[Documentation](home/documentation.md)** | This documentation system itself — an in-app browser for these same markdown files, with a built-in code viewer and a contributor guide for extending the docs. |

## Platform support status

| Platform | Chat in | Chat out | Events | Status |
|---|---|---|---|---|
| Twitch | Yes (WebSocket IRC) | Yes | Yes (EventSub WebSocket) | Integrated |
| Kick | Yes (Pusher WebSocket) | Yes (as the broadcaster) | Yes (webhooks via ngrok) | Integrated |
| YouTube | Yes (polling) | Yes (while live) | No (chat messages only) | Integrated |
| TikTok | No | No | No | UI shell — backend not built |
| [Discord](platforms/discord.md) | Yes (Discord.Net gateway, multiple bots) | Yes (per bot, to that bot's selected channel) | Chat messages only | Integrated |
| OBS | — | — | — | Integrated via the sidebar **OBS** page (Full OBS window hosting, embedded libobs Bot Studio, scene Collections); the Platforms-page card is a placeholder |

Twitch EventSub connects once `Platform.UserId` is set — both Twitch auth flows populate it automatically (fixed 2026-08-15); accounts connected before the fix need one re-auth. See [Twitch](platforms/twitch.md) and the [Known Issues ledger](reference/known-issues.md).

## Where to start

- **I'm a streamer setting this up** → [Quick Start](getting-started/quick-start.md) — the zero-to-first-command path in one page — then [Profiles](getting-started/profiles.md) and [Platforms](platforms/overview.md) for the full detail.
- **I want to run giveaways/polls/currency** → [Giveaways](stream-tools/giveaways.md), [Polls](stream-tools/polls.md), [Currency & Rewards](stream-tools/currency.md).
- **I'm a developer (or an AI agent) working on the code** → [Startup & Lifecycle](architecture/startup-lifecycle.md), [Event Bus](architecture/event-bus.md), [Data Layer](architecture/data-layer.md), and the [Known Issues ledger](reference/known-issues.md) before anything else.

## The tech stack in one paragraph

Vyral is a .NET 10 WPF desktop app split into projects: `VyralBot.UI` (WPF views/viewmodels), `VyralBot.Core` (domain services — commands, currency, giveaways, polls, watchtime, sounds, rules), `VyralBot.Data` (SQLite via Dapper), `VyralBot.Api` (embedded Kestrel web server, webhook controller, ngrok service), `VyralBot.Workflows` (the visual workflow engine behind Automation and the flow editor), and `VyralBot.Integrations` (Twitch/Kick/YouTube/Discord clients, embedded OBS via libobs + obs-websocket, FFmpeg restreaming, lights, Stream Deck). Everything talks through one in-process [Event Bus](architecture/event-bus.md); all data lives in [per-profile SQLite databases](architecture/data-layer.md). The full developer map starts at [Startup & Lifecycle](architecture/startup-lifecycle.md).

## Catalogs

- [Every chat command](reference/command-catalog.md) — commands, currency games, giveaway and poll commands.
- [Every template variable](reference/variables-catalog.md) — system, auto-updating, and social variables.
- [Every database table](reference/database-reference.md) — what is stored where.
- [Every window and page](reference/ui-reference.md) — the complete UI inventory, including dead and unreachable views.

---

*These docs describe the current build of the code, including its flaws. If you find a discrepancy between the docs and the code, trust the code and please update the docs — that is the whole point of them.*
