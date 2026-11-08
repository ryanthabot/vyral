# Song Requests

## Overview
Chat-driven song request queue (the last major streamer.bot staple): viewers
enqueue YouTube videos with `!sr <link>`, mods skip/clear, and a song player
drains the queue through the status lifecycle Queued → Playing →
Played/Skipped. Requests are per-profile (each profile's SQLite database has
its own `SongRequests` table, mirroring `Quotes`) and the command pipeline is
platform-agnostic — the same handler answers on Twitch, Kick, YouTube and
Discord, and the reply is sent on the ORIGINATING platform by the
ChatViewModel routing block.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Songs/ISongRequestService.cs` | Interface (`ProcessCommandAsync` + queue surface), `ITitleResolver` seam, `NullTitleResolver`, `SongRequestResult` |
| `src/VyralBot.Core/Services/Songs/SongRequestService.cs` | `!sr` command family, URL/id parsing, caps, lifecycle |
| `src/VyralBot.Data/Entities/SongRequest.cs` | Entity (Id, RequestedBy, Platform, YoutubeId, Title, RequestedAt, Status, DurationSeconds) + `SongRequestStatuses` |
| `src/VyralBot.Data/DapperDataService.cs` | `SongRequests` table DDL + CRUD |
| `src/VyralBot.Data/IDataService.cs` | Song request data methods (forwarded by `ProfileAwareDataService`) |
| `src/VyralBot.UI/ViewModels/ChatViewModel.cs` | `!sr` routing block (after the quote block) |
| `src/VyralBot.Api/Controllers/SongsController.cs` | Loopback `/songs` JSON API for the player page (see below) |
| `src/VyralBot.Api/wwwroot/songplayer.html` | OBS browser-source player (YouTube IFrame API) |
| `src/VyralBot.UI/ViewModels/SoundsViewModel.cs` + `Views/SoundsView.xaml` | "Song Requests" card on the Sounds page |
| `tests/VyralBot.UI.Tests/SongRequestServiceWave13Tests.cs` | URL parse variants, caps, mod gating, queue order, status transitions |

## Table shape
`SongRequests(Id INTEGER PRIMARY KEY AUTOINCREMENT, RequestedBy TEXT,
Platform TEXT, YoutubeId TEXT NOT NULL, Title TEXT, RequestedAt TEXT,
Status TEXT NOT NULL DEFAULT 'Queued', DurationSeconds INTEGER)` — queue order
is `Id ASC`; indexes on Status, RequestedBy and YoutubeId. Per-profile
scoping is identical to Quotes (row lives in the profile's own SQLite
database, no ProfileId column). Whitelisted in the Settings → Data Management
inventory as "Song requests".

Status lifecycle (`SongRequestStatuses`): `Queued` → `MarkPlayingAsync` →
`Playing` → `MarkPlayedAsync`/`SkipCurrentAsync` → `Played`/`Skipped`.

## Commands (handled by `SongRequestService.ProcessCommandAsync`)
| Command | Who | Behavior |
|---------|-----|----------|
| `!sr <url\|id>` | Everyone | Enqueue a YouTube video; replies with title + queue position |
| `!srqueue` | Everyone | Next 3 queued songs + total count |
| `!srskip` | Moderator+ | Skips the currently playing song |
| `!srclear` | Moderator+ | Clears every song request row |

Input must be a YouTube URL (watch?v=, youtu.be/, live/, shorts/, embed/,
music.youtube.com — scheme optional) or a bare 11-character video id.
Search text is NOT supported: anything else gets a clear error reply.

## Caps (config keys)
| Key | Default | Meaning |
|-----|---------|---------|
| `Songs.MaxPerUser` | 2 | Max queued requests per viewer |
| `Songs.MaxQueue` | 30 | Max total queued requests |
| `Songs.PlayerVolume` | 50 | Browser-source volume percent baked into the player URL |

All three are editable from the Sounds page card (see below); `Songs.MaxPerUser`
and `Songs.MaxQueue` are the keys the `!sr` family enforces at enqueue time.

## Title resolution (no API key)
`ITitleResolver.ResolveAsync(youtubeId)` returns `SongTitleInfo?` — the
default `NullTitleResolver` returns null, so requests are stored as
`Video <id>`. Once the song player page knows the real title it calls
`ISongRequestService.UpdateTitleAsync(youtubeId, title, durationSeconds)`,
which updates Title/DurationSeconds on every matching row. A future keyed
resolver can be registered in DI without touching the command pipeline.

## Pipeline wiring
`ChatViewModel.OnChatMessageReceivedAsync` gates on
`evt.Message.StartsWith("!sr")` (the block sits right after the quote block)
and calls `_songRequestService.ProcessCommandAsync(...)`. The returned string
is routed to the originating platform: Twitch → `ITwitchService`, Kick →
`IKickService`, YouTube → `IYouTubeService`, Discord → `SendDiscordReplyAsync`
(per-bot/DM aware). Returning `null` (non-song messages) lets the rest of the
pipeline continue.

DI: `App.xaml.cs` registers `services.AddSingleton<ITitleResolver,
NullTitleResolver>()` and `services.AddSingleton<ISongRequestService,
SongRequestService>()` (after the quotes block). `ProfileAwareDataService`
forwards every song request method to the current profile's
`DapperDataService`.

## Player browser source (`/songs` API)

`wwwroot/songplayer.html` is the OBS Browser source that actually plays the
queue (dependency-light: the official YouTube IFrame API is the only external
script). URL: `http://localhost:<port>/songplayer.html?volume=50` (copy button
on the Sounds page builds it from the web-server port). Behaviors:

- Polls `GET /songs/queue` every 5s (`{ current, upcoming }`); a changed
  `current.videoId` loads + plays that video, `current: null` stops playback.
  The GET lazily promotes the first Queued row to Playing — playback only
  starts once a player page is open.
- Volume comes only from `?volume=N` (browser sources get no keyboard input).
  The default player is small but visible (200x112, bottom-right) per YouTube
  TOS; `?video=hidden` shrinks it to a 2x2 stub for audio-only layouts.
- When a video ends (or errors — removed/embedding-disabled videos), it POSTs
  `/songs/played {videoId}` and syncs immediately instead of waiting for the
  next poll. Stale reports from a second browser source are no-ops (the route
  compares against the current row's `YoutubeId`).
- Connection badge mirrors `audio.html` conventions (Connecting/Connected/
  Disconnected, only Connected auto-hides; fetch failures show Disconnected
  and the 5s poll keeps retrying). Plain browsers that block unmuted autoplay
  get a "Click to start audio" button (OBS sources autoplay fine).
- Title feedback loop: rows are stored as `"Video <id>"` until resolved. The
  player reports the current video's title/duration from the IFrame player
  metadata, and resolves *upcoming* videos with a hidden 2x2 probe player that
  cues each video (metadata without playback), sending
  `POST /songs/title {id, title, duration}` per video. The route forwards to
  `UpdateTitleAsync`, so the Sounds page card shows real titles.

The `/songs` routes are NOT part of the token-gated `/api` surface — the server
binds loopback only and the page is same-origin. Hardening (wave-2 style):
loopback Origin enforcement on POSTs (mirrors `/audio-ws`), 4 KB body cap
(413), YouTube-id charset + title/duration bounds validation, and a semaphore
so concurrent player polls cannot double-promote rows. When the services are
absent (self-host without the song registrations) routes answer 503
`songs_disabled`. `WebServerService` forwards `ISongRequestService` +
`IDataService` from the WPF host into the child container.

## Sounds page card

The Sounds page has a collapsible **Song Requests** card (collapsed by
default; the rest of the page is unchanged). Left: the queue list — title
(the bare id while unresolved), requester, platform, duration when known, and
a NOW PLAYING highlight on the Playing row (fed by the per-profile
`SongRequests` table via `GetCurrentSongRequestAsync` + `GetQueueAsync`;
refreshed by a 3s `DispatcherTimer` while the page is open, so player-side
auto-advances appear without event plumbing). Right: the browser-source URL
with Copy button, a volume box feeding `?volume=N`, max-per-user and
max-queue-total boxes (persisting `Songs.MaxPerUser`/`Songs.MaxQueue` — the
same keys `!sr` enforces), plus Skip (`SkipCurrentAsync`) and Clear
(`ClearAsync`, confirmed) buttons. A future global skip-hotkey was deferred:
`GlobalHotkeyService` maps gestures to sound files specifically, so an action
hotkey needs a new concept there.

See also `docs/ui/sounds-view.md` and `docs/services/embedded-web-server.md`.
