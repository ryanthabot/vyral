# Loyalty Commands (currency + watchtime chat surface)

The daily-driver chat economy commands. Two Core services own them, both DI
singletons invoked **directly from `ChatViewModel`** for every `!` message —
not through the DB-backed `CommandService` cooldown pipeline.

| Service | File |
|---|---|
| `CurrencyCommandService` | `src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs` |
| `WatchtimeCommandService` | `src/VyralBot.Core/Services/WatchtimeCommandService.cs` |

## Command inventory

| Command (aliases) | Permission | Availability |
|---|---|---|
| `!points` / `!balance` / `!currency` (optional `@user`) | everyone | all platforms |
| `!leaderboard` / `!top` (optional count, max 10) | everyone | all platforms (currency) |
| `!watchtime` (optional `@user` / `top` / `leaderboard` / `lb`) | everyone | all platforms (watchtime) |
| `!give @user amount`, `!take @user amount` | moderator | all platforms |
| `!gamble` / `!bet`, `!slots`, `!duel`, `!accept`, `!heist`, `!roulette` | everyone | all platforms |
| `!daily`, `!rank` / `!tier` | everyone | all platforms |
| `!store` / `!shop`, `!rewards`, `!redeem` | everyone | all platforms |
| `!bank`, `!deposit`, `!withdraw` | everyone | all platforms |
| `!link`, `!linkcode`, `!linked`, `!delink` | everyone | all platforms |
| `!goals`, `!mystatus`, `!mp` / `!multiplatform` | everyone | all platforms |
| `!boost`, `!auction` (start/end/cancel/info) | moderator | all platforms |
| `!bid` | everyone | all platforms |

There is **no `!gift`** command — gifting to viewers is the moderator-only
`!give`. Replies contain no hardcoded platform names (the `!link` flow mentions
the Discord bot only in its non-Discord variant, which is correct there).

## Rate limiting (added W10)

The ChatViewModel direct-invocation path has no DB cooldowns, so both services
carry an **in-memory per-user cooldown (10 s, silent skip)**:

- `WatchtimeCommandService`: all `!watchtime` forms, keyed `platform|username`.
- `CurrencyCommandService`: read-only informational commands only
  (`!points`/`!balance`/`!currency`, `!leaderboard`/`!top`, `!rewards`,
  `!store`/`!shop`, `!rank`/`!tier`, `!bank`, `!linked`, `!goals`,
  `!mystatus`), keyed `command|platform|username`. State-changing commands
  (games, bank transfers, redeem, link codes, mod tools) are deliberately not
  throttled here — they rely on their own protections (per-game DB cooldowns
  via `CurrencyGameService.CanPlayAsync`, escrowed stakes, daily stamps), and
  silently swallowing them could hide a real action.

Cooldown maps are bounded (cleared at 10k entries). Games register cooldowns
through `CurrencyGameService` (`MarkPlayed`); `!gamble` registers its own when
a `Gamble` game-settings row exists, else falls back to the legacy min-bet-10
path with no cooldown.

## Watchtime behavior

- Accrual is per `(username, platform)` row; Discord **never** accrues
  (`WatchtimeService.ShouldAccrueWatchtime`), stream platforms accrue only
  while live (`IStreamLiveRegistry`).
- `!watchtime` on Discord replies with an explicit "does not accrue on
  Discord" message instead of the generic "keep watching" text.
- `!watchtime top` uses `GetTopWatchtimeUsersAsync`: `ORDER BY TotalMinutes
  DESC, Username COLLATE NOCASE ASC, Platform ASC LIMIT @Limit`, **combined
  across platforms** (one row per platform-account; a linked viewer can appear
  once per platform). Tie-breakers match the currency leaderboard
  (`Username COLLATE NOCASE`, then `Platform`) so ties are deterministic; the
  all-viewers and active-viewers queries use the same tie-breakers.
- Leaderboard replies truncate display names at 20 chars so a max-size board
  fits platform message limits (YouTube clips ~200 chars).

## Known gaps (W10 audit, not fixed here — outside file ownership)

- `!top` currency leaderboard is combined per platform-account row, not a
  unified-wallet sum: a linked viewer with balances on two platforms shows as
  two entries rather than one combined total.
