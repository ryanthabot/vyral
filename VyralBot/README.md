# Vyral

> **Open-source multi-platform chat bot and stream assistant for Windows** — one app, one chat feed, Twitch + Kick + YouTube + Discord at once.

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green)](AppDocs/reference/known-issues.md)
[![Status](https://img.shields.io/badge/Status-In%20Development-yellow)](AppDocs/reference/known-issues.md)

---

## Documentation

**[AppDocs/](AppDocs/index.md) is the documentation** — 44 pages that ship with the app and describe every feature for two audiences at once: a *User Guide* for streamers and *How It Works* sections for developers. Open it two ways:

- **In the app** — Home → **Documentation** opens the built-in docs browser (navigation tree, search, history, and a read-only source-code viewer).
- **On GitHub** — the same markdown files render normally; start at [AppDocs/index.md](AppDocs/index.md).

Every page has a *Status & Known Issues* section, and the honest app-wide defect inventory lives in the [Known Issues Ledger](AppDocs/reference/known-issues.md). If the docs and the code ever disagree, trust the code and update the docs.

## What works today

| Area | Status |
|---|---|
| **Unified chat** — one merged, scrolling feed from Twitch, Kick, YouTube, and Discord with badges, colors, timestamps, on-demand history, and send-to-one-or-all | Working |
| **Chat commands** — 21 seeded built-ins plus custom commands, permissions, cooldowns, per-platform toggles, template variables; the full command pipeline runs from live chat | Working |
| **Currency & rewards** — a complete viewer economy: earning streams (chat activity, watchtime, event bonuses with the platform × boost × tier × role multiplier stack), ~25 chat commands, games (slots, duels, self-resolving heists, roulette), rewards store with approval queue, bank with audited interest, auctions, boosts, loyalty tiers, cross-platform account linking | Working |
| **Giveaways & polls** — create in the UI, run from chat (`!enter`, `!poll vote 2`), weighted draws, vote changing, min-watchtime eligibility (real watchtime) and follower eligibility on Twitch | Working |
| **Watchtime** — per-viewer tracking feeding the dashboard, currency earning (configurable rate), and eligibility gating; `!watchtime` commands | Working |
| **Event reactions** — follows, subs, resubs, gift subs, raids, hosts, cheers each trigger a chat announcement, a sound, a visual alert, a currency bonus, a rate-limited auto-reply, and a statistics row | Working |
| **Alerts & sounds** — Alert Settings persist and drive real alerts (animation, position, duration, per-event toggles); sounds play locally (`.wav`) and through the OBS browser source with volume honored | Working |
| **Statistics & event history** — every reacted event is recorded; filterable, exportable analytics and history pages | Working |
| **Profiles** — fully portable per-profile data (own SQLite DB, credentials, settings), optional PBKDF2-hashed PIN, hot switching, ZIP export | Working |
| **Web server + ngrok** — embedded Kestrel server receives Kick webhooks through an ngrok tunnel (with automatic resubscribe on URL rotation) and serves the OBS audio browser source | Working |
| **In-app docs browser** — the AppDocs pages render inside the app with search and a code viewer | Working |
| **Twitch** — OAuth wizard, WebSocket IRC chat, EventSub WebSocket events (follows, subs, resubs, gift subs, raids, cheers), auto-reconnect | Working |
| **Kick** — OAuth wizard (with channel-ID discovery), Pusher chat, official webhooks for channel events, messages sent as the broadcaster | Working |
| **YouTube** — Google Cloud OAuth wizard, live-chat polling (while live), chat in/out and history | Working |
| **Discord** — bot-token wizard, multi-bot manager (any number of bots, each with its own active channel), full command pipeline with per-bot command scoping, graceful stop/reconnect | Working |
| **TikTok** — setup-wizard UI shell saves an honest "Coming Soon" status; no backend yet | Planned |
| **OBS integration** (beyond the audio browser source) | Planned |

Honest caveats and remaining limitations — follower eligibility on Kick/YouTube, auto-reply template variables, and the rest — are itemized in the [Known Issues Ledger](AppDocs/reference/known-issues.md).

## Quick start

1. **Build and run** (or download a release when available):
   ```powershell
   dotnet restore src/VyralBot.sln
   dotnet build src/VyralBot.sln --configuration Release
   dotnet run --project src/VyralBot.UI
   ```
2. **Create a profile** on first launch — each profile is a portable, self-contained identity under `appdata/`.
3. **Connect platforms** — Platforms page → Twitch / Kick / YouTube card → four-step OAuth wizard. Kick webhooks additionally need the ngrok setup (Settings → API & Webhooks → Run Setup Wizard).
4. **Chat, commands, and events start flowing immediately.** Configure reactions under Chat Settings, sounds under Sounds Management, alerts under Visual Alerts.

**No installation required. Fully portable.** All data lives in the local `appdata/` folder — no registry, no %APPDATA%. Delete `appdata/` for a fresh start; copy it to move everything to another machine.

## For developers

- **Prerequisites**: [.NET 10 SDK](https://dotnet.microsoft.com/), Visual Studio 2022 / Rider / VS Code, Windows 10/11 64-bit.
- **Tech stack**: C# / .NET 10, WPF + MaterialDesignInXaml, SQLite via Dapper, embedded ASP.NET Core (Kestrel), Serilog, TwitchLib, KickLib.
- **Project layout**:
  ```
  src/
  ├── VyralBot.UI/              # WPF app: views, viewmodels, navigation
  ├── VyralBot.Core/            # domain services: commands, currency, giveaways,
  │                             #   polls, watchtime, sounds, alerts, event bus
  ├── VyralBot.Data/            # SQLite + Dapper persistence (per-profile DBs)
  ├── VyralBot.Api/             # embedded Kestrel server, webhooks, ngrok
  └── VyralBot.Integrations/    # Twitch (IRC + EventSub), Kick (Pusher + OAuth), YouTube (polling)
  appdata/                      # user data (git-ignored): profiles, credentials, DBs
  AppDocs/                      # the documentation (source of truth)
  logs/                         # Serilog rolling logs (git-ignored)
  ```

Everything talks through one in-process [Event Bus](AppDocs/architecture/event-bus.md); all data lives in [per-profile SQLite databases](AppDocs/architecture/data-layer.md). The developer map starts at [Startup & Lifecycle](AppDocs/architecture/startup-lifecycle.md), and the catalogs cover [every command](AppDocs/reference/command-catalog.md), [every variable](AppDocs/reference/variables-catalog.md), [every table](AppDocs/reference/database-reference.md), and [every page](AppDocs/reference/ui-reference.md).

Note: root-level planning documents (`plan.md`, `ROADMAP.md`, `docs/`, etc.) are historical planning artifacts; where they disagree with the code, `AppDocs/` is authoritative.

## Contributing

Contributions are welcome. Read [AppDocs/home/documentation.md](AppDocs/home/documentation.md) for the documentation contributor guide, and the [Known Issues Ledger](AppDocs/reference/known-issues.md) for a curated list of open work — the "Remains" notes on partially-resolved entries and the KI-26+ limitations are good starting points.

## License

Vyral is **MIT Licensed**. You can use, modify, and distribute Vyral freely, including commercially.
