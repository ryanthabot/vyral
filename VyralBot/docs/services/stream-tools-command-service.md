# Stream Tools Command Service

## Overview
Classic mod/stream-tool chat commands (`!so`, `!clip`, `!raid`, `!title`, `!game`,
`!commercial`, `!uptime`, `!followage`) answered from one service. ChatViewModel calls
`ProcessCommandAsync` in its command fallback chain; a `null` return means "not mine"
so other services (custom commands, giveaway, quotes…) can take the message.

## Source Files
- **Interface:** `src/VyralBot.Core/Services/Commands/IStreamToolsCommandService.cs`
  (namespace `VyralBot.Core.Services.Commands`)
- **Implementation:** `src/VyralBot.Integrations/StreamTools/StreamToolsCommandService.cs`
  (namespace `VyralBot.Integrations.StreamTools`)

The split mirrors `IStreamLiveRegistry`: Core owns the interface (the type ChatViewModel
injects), while the class lives in Integrations beside `ITwitchService`, because Core
cannot reference Integrations (Integrations → Core only).

## Contract
```csharp
Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
```
Returns the chat reply, or `null` when the message is not a stream-tools command.
Never throws — failures return short error text (unexpected exceptions are caught,
info-logged and answered with "That command failed — check the logs.").

## Constructor
`ITwitchService?` (nullable — absent/disconnected Twitch yields "Twitch is not connected."),
`IStreamLiveRegistry?` (nullable — non-Twitch `!uptime` yields "Stream status isn't tracked."),
`ILogger<StreamToolsCommandService>`.

## Commands
| Command | Gating | Platforms | Reply shape |
|---|---|---|---|
| `!so <user>` | mod | all | "Check out {user} at {url} — go show them some love!" |
| `!clip` | mod | Twitch | "Clip created: {url}" / "Clip failed (Twitch only, and the stream must be live)" |
| `!raid <channel>` / `!raid cancel` | mod | Twitch | "Raiding {channel}!" / "Raid cancelled." |
| `!title [new title...]` | mod | Twitch | no args: "Current title: {t} (playing {g})"; set: "Title updated." |
| `!game [game name...]` | mod | Twitch | no args: "Current game: {g}"; set: "Game updated to {g}." / "Game \"x\" not found on Twitch." |
| `!commercial <30\|60\|90\|120\|150\|180>` | mod | Twitch | "Commercial started ({n}s)." / "Commercial failed." |
| `!uptime` | everyone | all | Twitch: "Stream uptime: {h}h {m}m" (Helix started_at); others: live/offline from `IStreamLiveRegistry` |
| `!followage [user]` | everyone | Twitch | "{user} has been following for {2 largest units}." / "isn't following the channel." |

Non-mods get "That's a moderator-only command." Shoutout URLs: `twitch.tv/{user}`,
`kick.com/{user}`, `youtube.com/@{user}`, `tiktok.com/@{user}`; Discord gets plain hype
(no URL). Usernames are stripped of `@`/`#` before use. Twitch calls go through the
engine-facing `ITwitchService` members (`CreateClipUrlAsync`, `StartRaidAsync`/
`CancelRaidAsync`, `GetChannelInfoAsync`/`UpdateChannelInfoAsync`, `GetTopGamesAsync`,
`StartCommercialAsync`, `GetStreamUptimeAsync`, `GetFollowAgeAsync`).

## Degradation notes
- **`!so`** — `GetChannelInfoAsync` only reads the broadcaster's OWN channel; no
  target-capable variant exists on `ITwitchService`, so the target's title/game cannot
  be fetched and the shoutout degrades to URL + generic hype text.
- **`!game` set** — names resolve via `GetTopGamesAsync(query)` (exact `GET /games?name=`
  match first, `/search/categories` partial fallback, inherent Helix limitation on
  misspellings), parsed from the "id:name;…" pairs; then `UpdateChannelInfoAsync(null, id)`.
- **`!uptime` non-Twitch** — `IStreamLiveRegistry` tracks only a live bool per platform
  (no start times) and only for Twitch/Kick/YouTube, so those platforms reply
  "Live now — duration unknown (no start time available)." or "Stream is offline."
  (Discord is never tracked → reports offline. `CommandService`'s built-in `!uptime`
  reads the Twitch-only `Stream.StartedAtUtc` config key; the ChatViewModel call order
  decides which one answers.)
- **`!followage`** — `GetFollowAgeAsync` returns null both for "doesn't follow" and for
  lookup failures; both cases reply "{user} isn't following the channel."

## Registration
`App.xaml.cs` `ConfigureServices` (quotes block, next to `IQuoteService`):
`services.AddSingleton<VyralBot.Core.Services.Commands.IStreamToolsCommandService, VyralBot.Integrations.StreamTools.StreamToolsCommandService>();`

## Logging
Info-level only: one line per handled command (`StreamTools command {Command} by
{Username} on {Platform}`), plus failures. No tokens or credentials are involved or logged.
