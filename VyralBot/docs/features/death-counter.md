# Death Counter

## Overview
The streamer.bot death-counter staple: one shared per-profile count that chat
mods bump mid-run (`!death` family), the Tools page card edits (+/−/set/reset),
the OBS browser-source widget displays, and the VyralDeck state payload
broadcasts as `deathCount`. All four surfaces read/write the same two config
rows, so a chat `!death+` shows up on the overlay within one 3s poll and on a
paired phone within one 20s state pump.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Counters/IDeathCounterService.cs` | Interface: `ProcessCommandAsync`, `GetCountAsync`, mutations, history, `CountChanged` |
| `src/VyralBot.Core/Services/Counters/DeathCounterService.cs` | `!death` command family + count/history persistence |
| `src/VyralBot.Api/Controllers/CountersController.cs` | Loopback `GET /counters/death` (widget poll) |
| `src/VyralBot.Api/wwwroot/deathcounter.html` | OBS browser-source widget (dependency-free) |
| `src/VyralBot.UI/ViewModels/ToolsViewModel.cs` | Tools page death-counter card state |
| `src/VyralBot.UI/Views/ToolsView.xaml` | Card UI (count, buttons, widget URL + COPY, history) |
| `src/VyralBot.UI/ViewModels/ChatViewModel.cs` | Pipeline wiring (`ProcessCommandAsync` fall-through) |
| `src/VyralBot.UI/Services/VyralDeck/DeckServer.cs` | `deathCount` key in the live-state payload |

## Persistence
No new table — two per-profile config rows (category `DeathCounter`):
- `DeathCounter.Count` — plain int (0 when unset; hard ceiling 999,999)
- `DeathCounter.History` — JSON array, newest first, capped at 50 events of
  `{ at, delta, by }` (`at` UTC, `delta` signed — set/reset included, `by`
  chat username or "Tools page")
- `DeathCounter.Label` — the card's editable widget label (default "Deaths",
  debounced persist)

Mutations are serialized through a `SemaphoreSlim` gate (two concurrent chat
messages cannot lose an update); each successful mutation raises `CountChanged`.

## Commands (`DeathCounterService.ProcessCommandAsync`)
| Command | Who | Behavior |
|---------|-----|----------|
| `!death` | Everyone | "💀 Deaths: N" |
| `!death+` / `!death add` | Moderator+ | +1 (add clamps 1..10000 per call) |
| `!death add <n>` | Moderator+ | +n |
| `!death-` / `!death- <n>` | Moderator+ | −1 / −n, floored at 0 |
| `!death set <n>` | Moderator+ | Set (0..999999) |
| `!death reset` | Moderator+ | Back to 0 — confirm-less, but logged at info |
| `!death help` | Everyone | Usage summary |

Non-`!death` messages return null so the ChatViewModel pipeline continues
(same contract as QuoteService); the reply is sent on the originating platform.

## OBS widget (`deathcounter.html`)
Browser source polling `GET /counters/death` every 3s (loopback, same origin,
no-store). URL params: `?label=Deaths&size=120&color=%23ffffff` — label is
plain-text only, size clamped 20..480px, color must match a strict hex pattern
or falls back to white (defaults are sane on dark overlays; text has a dark
glow for bright scenes). Count changes pop-scale the number. Status badge uses
the audio.html/songplayer.html conventions (connected auto-hides after 5s,
disconnected stays visible; the poll loop is the reconnect). Dependency-free.

## Tools page card
Big count + skull, `+1` / `−1` / SET (typed value) / RESET buttons, the widget
URL with COPY (label param baked in from the editable label box, port from
`GetWebServerSettingsAsync` with 5000 fallback), and the last 10 history events
(newest first). The VM self-hydrates from its constructor (the Tools navigation
does not call `InitializeAsync`).

## VyralDeck
`DeckServer.LiveStateAsync` adds `deathCount` (string) alongside `nowPlaying` /
`uptimeText`; omitted when the service is not registered. The mobile chip
ticks immediately via `IDeathCounterService.CountChanged` (hooked on first
lazy resolution), with the deduped 20s state pump as the fallback. Older
mobile clients ignore unknown keys (PROTOCOL.md).

## DI
`App.xaml.cs`: `services.AddSingleton<IDeathCounterService, DeathCounterService>();`
(stream-tools block). `WebServerService` forwards it into the loopback host
(`FromParent<IDeathCounterService>`) so the controller can serve the widget.
