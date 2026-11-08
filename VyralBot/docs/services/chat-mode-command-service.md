# Chat Mode Command Service

## Overview
Twitch chat-mode protection commands with AUTO-REVERT (the streamer.bot
raid-protection staple): `!emoteonly`, `!subonly`, `!followers`, `!slow`.
Enabling a mode arms a timed auto-revert (default 10 minutes) so a
raid-shielded chat always opens up again even if the mod forgets the off
command; `!<mode> off` disables immediately and cancels the pending revert.
ChatViewModel calls `ProcessCommandAsync` in its command fallback chain; a
`null` return means "not mine" so other services can take the message.

## Source Files
- **Interface:** `src/VyralBot.Core/Services/Commands/IChatModeCommandService.cs`
- **Interface + enum (Twitch seam):**
  `src/VyralBot.Core/Services/Commands/ITwitchChatModeApplier.cs`
- **Implementation:** `src/VyralBot.Core/Services/Commands/ChatModeCommandService.cs`
- **Twitch side:** `TwitchService` implements `ITwitchChatModeApplier`
  (`src/VyralBot.Integrations/Twitch/TwitchService.cs`, PUT /helix/chat/settings)

Interface AND implementation live in Core; Twitch is reached through the
Core-owned `ITwitchChatModeApplier` seam because Core cannot reference
Integrations (Integrations → Core only) — the same dependency split as
`IStreamToolsCommandService` (interface in Core, class in Integrations), with
the seam inverted so the command class itself stays in Core.

## Contract
```csharp
Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
```
Returns the chat reply, or `null` when the message is not a chat-mode command.
Never throws — failures return short error text (unexpected exceptions are
caught, info-logged and answered with "That command failed — check the logs.").

## Constructor
`ITwitchChatModeApplier?` (nullable — absent/disconnected Twitch yields
"Twitch is not connected."), `ILogger<ChatModeCommandService>`,
`Func<DateTime>? utcNow = null` (clock seam for the Wave-16 tests; defaults to
`DateTime.UtcNow`).

## Commands (all moderator-only, Twitch-only)
| Command | Reply shape |
|---|---|
| `!emoteonly [minutes]` | "Emote-only enabled for {n} minutes." (default 10) |
| `!emoteonly off` | "Emote-only disabled." + cancels the pending revert |
| `!subonly [minutes]` / `!subonly off` | "Subscribers-only enabled/disabled …" |
| `!followers [follow min\|hours, e.g. 30 or 2h] [revert minutes]` | "Followers-only enabled (must have followed for {2 largest units}) for {n} minutes." — follow age defaults to 0 (any follower) |
| `!followers off` | "Followers-only disabled." |
| `!slow <3-120 seconds> [minutes]` / `!slow off` | "Slow mode enabled ({s}s) for {n} minutes." / "Slow mode disabled." |

Non-mods get "That's a moderator-only command."; other platforms get
"!<command> is Twitch-only.". Bad arguments get a usage line and touch nothing
(no Twitch call, no revert armed). The explicit revert window is clamped to
1–120 minutes; a rejected Twitch call yields "{label} update failed." and arms
nothing. The enable reply carries the window because the service cannot send
chat lines itself.

## Auto-revert design
- **Bounded registry:** mode → due UTC in a `Dictionary<TwitchChatMode,
  DateTime>` — at most 4 entries (the closed enum), one pending revert per
  mode; re-issuing a command re-arms (replaces) the entry.
- **Timer:** a single one-shot `System.Threading.Timer` (the
  scheduled-messages pattern from `DiscordBotManager`) checks due reverts every
  30 s; created disarmed, only ticks while a revert is pending, re-armed in a
  `finally` with an `Interlocked` overlap guard and an `ObjectDisposedException`
  catch (dispose-safe). A due revert fires within one tick (~30 s late at most).
- **On due:** single attempt — `ApplyChatModeAsync(mode, false)`; success
  info-logs "Auto-reverted Twitch {Mode} chat mode", a failed/disconnected
  attempt drops the entry with an info log (a mod can re-issue `!<mode> off`);
  no chat line is sent.
- **Lifetime:** pending reverts are in-memory only — they die with the process
  and the modes stay as-is on Twitch after a shutdown. Singleton service; DI
  disposes the timer on host shutdown.
- **Test seam:** `ProcessDueRevertsAsync()` (returns the revert count) and
  `PendingRevertCount` — public on the concrete class only (Core grants no
  InternalsVisibleTo to the tests) — let the Wave-16 tests drive the registry
  with a controllable clock instead of waiting on the timer.

## Registration
`App.xaml.cs` `ConfigureServices` (chat-modes block):
```csharp
services.AddSingleton<VyralBot.Core.Services.Commands.ITwitchChatModeApplier>(sp =>
    (VyralBot.Core.Services.Commands.ITwitchChatModeApplier)sp.GetRequiredService<VyralBot.Integrations.Twitch.ITwitchService>());
services.AddSingleton<VyralBot.Core.Services.Commands.IChatModeCommandService, VyralBot.Core.Services.Commands.ChatModeCommandService>();
```
The cast registration maps the seam onto the TwitchService singleton (same
instance, one Helix client).

## Tests
`tests/VyralBot.UI.Tests/Wave16ChatModeCommandTests.cs` — routing/mod/platform/
connection gates, the full parse matrix (windows, follow-age suffixes "2h"/"30m",
slow-second bounds 3–120, usage replies), off-cancellation, and the revert
registry: due-time firing, re-arm on re-issue (single entry), one pending per
mode, single-attempt failure, disconnect drop, dispose safety.

## Logging
Info-level only: one line per handled command (`Chat-mode command {Command} by
{Username} on {Platform}`), auto-revert outcomes, and tick failures. No tokens
or credentials are involved or logged.
