# Fun Command Service

## Overview
The community fun pack (`!8ball`, `!dice`/`!roll`, `!coinflip`/`!flip`,
`!lurk`, `!hug`, `!rng`). One service, registered `IFunCommandService` →
`FunCommandService` (both in
`src/VyralBot.Core/Services/Commands/FunCommandService.cs`).
ChatViewModel calls `ProcessCommandAsync` after the warning-command block; a
`null` return means "not mine" (or "spam-dropped") so other handlers can take
the message. Everyone, all platforms, stateless — no database.

## Contract
```csharp
Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
```
Returns the chat reply, or `null` when the message is not a fun command or the
user is inside the spam window. Never throws — failures are caught,
info-logged and answered with short error text. `isModerator` is unused
(everyone-commands); `platform` is ignored (all platforms).

## Constructor
`ILogger<FunCommandService>`, optional `Random` (seedable — tests pin
sequences), optional `Func<DateTime>` clock (tests advance the spam window
instead of sleeping; the `ChatModeCommandService` pattern).

## Commands
| Command | Reply shape |
|---|---|
| `!8ball <question>` | `🔮 ` + one of the 20 classic answers |
| `!dice [1-10]` / `!roll [1-10]` | `🎲 5` or `🎲 4, 2, 6 = 12` (d6 each) |
| `!coinflip` / `!flip` | `🪙 Heads!` / `🪙 Tails!` |
| `!lurk` | `{user} slips into the shadows… 👋 (welcome back anytime)` |
| `!hug <user>` | `{user} gives {target} a big hug! 🤗` |
| `!rng <min> <max>` | `🎯 {n}` — inclusive both ends |

Validation: missing question/target → usage text; dice count outside 1–10 →
usage; `!rng` needs two ints, min ≤ max, span under a billion (keeps
`max + 1` representable); `!hug` targets are sanitized exactly like `!so`
(strip `@`/`#`, keep `[A-Za-z0-9_.-]`).

## Spam guard (the only state)
One 5-second slot per username; ANY matched fun command refreshes it, a
second inside the window returns `null` silently so the bot never amplifies
spam. Unmatched messages (`!hello`) do not consume a slot; usage errors do.
The registry is a lock-guarded dictionary capped at 256 entries — inserts
past the cap prune expired timestamps first, then evict the oldest
(bounded by construction; entries live 5s anyway). In-memory only: it dies
with the process.

## Routing (ChatViewModel)
Field `_funCommandService` → optional ctor param resolved by DI → routing
block after the warnings block (four-branch platform reply Twitch/Kick/
YouTube/Discord, `evt.IsModerator || evt.IsBroadcaster` flag passed as
`isModerator`). Runs AFTER the user-defined command service, so a streamer's
custom `!dice` shadows the built-in.

## Tests
`tests/VyralBot.UI.Tests/Wave17FunCommandTests.cs` — seeded-Random matrix,
`!rng N N` span-of-one determinism, spam-window clock control, sanitizer
parity with `!so`, capacity pruning.
