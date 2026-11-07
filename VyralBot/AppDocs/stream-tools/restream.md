# Restream (Multi-Platform Fan-Out)

> One OBS output, every platform: a local FFmpeg ingest listens for your OBS stream and fans it out to Twitch / YouTube / Kick / custom RTMP — each destination with its own resolution, bitrate and audio budget — while the engine babysits the process so one bad stream key can't take the whole stream down.

## Overview

The Restream page (sidebar, grouped under the Restream expander next to StreamDeck) runs a single FFmpeg process in **RTMP listen mode**: FFmpeg *is* the ingest server, OBS publishes to it once, and FFmpeg writes one output per enabled destination (RestreamEngine.cs:9-18). Because it is one input decoded at most once, a "Source (no re-encode)" destination costs essentially nothing — the bits are remuxed straight through (`-c:v copy`, RestreamArgumentBuilder.cs:87-105) — while scaled destinations each get their own x264 encode.

Two ways to run it (RestreamOperationMode, RestreamModels.cs:4-11):

- **Single PC** — OBS on this machine publishes to `rtmp://127.0.0.1:<port>/live/<key>` (loopback bind).
- **Two PC** — a gaming PC publishes over the LAN; FFmpeg binds `0.0.0.0` so remote machines can reach the ingest (RestreamArgumentBuilder.cs:36-41). The page lists your LAN IPv4 addresses as ready-to-copy ingest URLs (RestreamViewModel.cs:573-596).

Destination types are labels with an ingest autofill: **Twitch** (`rtmp://live.twitch.tv/app`) and **YouTube** (`rtmp://a.rtmp.youtube.com/live2`) autofill their well-known servers; **Kick** and **Custom** expect the per-account URL you paste from the platform dashboard (RestreamViewModel.cs:74-79, 122-131). If the server field already embeds the key (Kick hands out one URL with the key appended), the builder uses it verbatim instead of appending the key twice (RestreamArgumentBuilder.cs:50-61).

The engine keeps running when you navigate away — only the uptime ticker stops; the engine is a singleton that outlives the page, and its `Dispose` (user stop / app exit) is the only thing that ends a session (RestreamEngine.cs:16-17, RestreamViewModel.cs:717-726).

## User Guide

### Before you start: FFmpeg

The page probes for FFmpeg on open and shows the verdict ("FFmpeg ready — 7.1 (bundled download)"). Resolution order: an explicit override path you set, the copy Vyral downloaded into its appdata tree, then system PATH (RestreamFfmpegLocator.cs:55-77). If nothing is found, **DOWNLOAD FFMPEG** streams the official gyan.dev `release-essentials` build (~90 MB, GPL, ships x264), extracts just `ffmpeg.exe` into `appdata/bin/ffmpeg/`, and shows byte-level progress (RestreamFfmpegAcquisitionService.cs:40, 57-211). You can also paste a custom path to an existing ffmpeg.exe.

### Setting up the ingest

1. Pick the mode: **Single PC** or **Two PC**.
2. Set the ingest port (default **1935**) and the ingest stream key (default **vyral**) — the key OBS must publish with; FFmpeg's listen mode matches app/stream key exactly (RestreamModels.cs:35).
3. Copy the shown ingest URL into OBS: **Custom** service, server `rtmp://…/live/<key>`, stream key `<key>`. Single-PC shows one loopback URL; Two-PC shows one URL per LAN address. Buttons copy the URL and the key (RestreamViewModel.cs:536-548).

Both the ingest key and every destination's stream key are entered in password boxes (never plain text), and every log surface the engine emits runs through a masker that replaces each key — and any full URL embedding one — with `***` before the line is shown or logged (RestreamArgumentBuilder.cs:217-236; RestreamEngine.cs:179, 284).

### Destinations

Each row: Name, Platform, Server URL, **Stream key** (masked entry), Resolution, and for scaled rows Video bitrate + x264 preset; audio mode (Copy / AAC 128k / AAC 160k) applies to every row. Resolution choices: **Source (no re-encode)**, 1080p, 936p, 720p, 540p, 360p; picking a rung autofills its default bitrate (6000/5000/4000/2500/1200 kbps, RestreamViewModel.cs:47-64; RestreamArgumentBuilder.cs:14-22). Rows can be Enabled/Disabled — disabled rows are excluded from the session entirely. **ADD DESTINATION** per platform, **Delete** per row, **Save Settings** persists everything.

Scaled outputs use `libx264`, `veryfast` by default (ultrafast…faster selectable), `high` profile, yuv420p, ABR with `maxrate = b:v` and 2× bufsize, bilinear `scale=-2:<height>` (aspect ratio kept, even width enforced), and a forced keyframe every 2 s — platforms require short GOPs and the args are built before the publisher's fps is known (RestreamArgumentBuilder.cs:107-144).

### Going live

Press **Start Restream**. The engine validates FFmpeg, requires at least one enabled destination with both server and key set, then — **going live persists the exact session configuration** so destinations edited without pressing Save survive an app restart (a persistence failure is logged but never blocks the stream, RestreamViewModel.cs:468-478). FFmpeg starts listening; the status reads **Waiting for OBS** until your OBS connects ("Input #" in FFmpeg's log flips it to **Live**, RestreamEngine.cs:257-266). The header shows uptime, bitrate, fps and speed from FFmpeg's machine-readable `-progress pipe:1` output (one snapshot per second, RestreamEngine.cs:560-591), and each destination row shows **streaming** / **error: …** / **auto-disabled after repeated failures**.

**Stop Restream** kills FFmpeg's process tree and returns to Idle (RestreamEngine.cs:84-96).

### What happens when things break

- **OBS disconnects** (clean exit after a real session): FFmpeg exits with code 0 and the engine goes straight back to **waiting for it to reconnect** — no backoff, because OBS auto-reconnects and republishes on its own (RestreamEngine.cs:361-375).
- **A destination keeps killing the session**: FFmpeg error lines are matched against each destination's full publish URL (and server host as fallback); when the *same* destination is implicated **twice in a row**, it is **auto-disabled for the session** — excluded from the command line so the healthy outputs keep streaming. When *every* destination has been excluded, the session stops with an honest "every destination failed repeatedly" fault (RestreamEngine.cs:386-401, 164-176).
- **Crash-looping** (bad args, unreachable ingest, publisher that never handshakes): quick failures (< 10 s) climb a backoff ladder of 2 / 5 / 10 / 30 s between restarts; three consecutive *unattributed* failures end the session with the last five masked FFmpeg log lines in the status (RestreamEngine.cs:22, 25, 404-424).
- **Port in use**: FFmpeg's "Address already in use" is detected and reported as a terminal fault telling you to change the ingest port or stop the other app (RestreamEngine.cs:278-282, 345-354).

The masked FFmpeg console (last 300 lines) is on the page for diagnosis (RestreamViewModel.cs:641-649).

## How It Works (Developer)

- One supervision loop per session: builds the command line from the *active* (enabled, non-excluded) destinations, starts FFmpeg through the shared `ProcessManager` (no direct `Process.Start`), subscribes `Exited` before probing `HasExited` to dodge the disposal race, drains **both** stdout and stderr to EOF so the child never blocks on a full pipe, and races exit against cancellation so a user stop kills the tree instead of waiting (RestreamEngine.cs:181-338).
- Attribution vs. backoff are deliberately separate: the quick-failure streak only drives the backoff ladder; attribution (which destination an error line names) drives auto-disable and works for slow failures too — platforms that accept the connection and then reject the key kill the session well past the 10 s window (RestreamEngine.cs:377-403).
- Stats come from a tiny `ProgressParser` that folds FFmpeg's `key=value` progress blocks into fps/bitrate/speed/dropped-frames (RestreamEngine.cs:561-622); state is an immutable `RestreamEngineStatus` record raised on every change, and the ViewModel marshals it to the dispatcher and maps per-destination health onto rows by stable GUID id (RestreamViewModel.cs:638-707).
- Windows argument escaping is `CommandLineToArgvW`-canonical (quotes on whitespace, backslash-doubling before quotes — RestreamArgumentBuilder.cs:165-210).

## Files & Code

| File | Role |
|---|---|
| [RestreamEngine.cs](../../src/VyralBot.Integrations/Restream/RestreamEngine.cs) | The singleton supervisor: start/stop, crash/backoff policy, attribution + auto-disable, progress parsing |
| [RestreamArgumentBuilder.cs](../../src/VyralBot.Integrations/Restream/RestreamArgumentBuilder.cs) | The single FFmpeg command: listen-mode input, per-destination outputs, escaping, stream-key masking |
| [RestreamModels.cs](../../src/VyralBot.Integrations/Restream/RestreamModels.cs) | Source/destination/session records, states (`Idle`/`WaitingForSource`/`Streaming`/`Faulted`), per-destination health |
| [RestreamFfmpegLocator.cs](../../src/VyralBot.Integrations/Restream/RestreamFfmpegLocator.cs) | Override → bundled-download → PATH resolution, `-version` probe with 5 s hang guard |
| [RestreamFfmpegAcquisitionService.cs](../../src/VyralBot.Integrations/Restream/RestreamFfmpegAcquisitionService.cs) | gyan.dev release-essentials download + ffmpeg.exe-only extraction, progress records, never throws |
| [RestreamViewModel.cs](../../src/VyralBot.UI/ViewModels/RestreamViewModel.cs) | Page state: ingest settings, destination grid, FFmpeg status/acquire, start/stop, log view, persistence |
| [RestreamView.xaml](../../src/VyralBot.UI/Views/RestreamView.xaml) | Layout — including the PasswordBoxes for ingest key (:214) and per-row stream keys (:341) |

## Data & Settings

| Key | Store | Meaning |
|---|---|---|
| `Restream.Settings` | Configs (category "Restream") | Non-secret source settings JSON: mode, ingest port, ingest key, FFmpeg path override (RestreamViewModel.cs:177, 379-392) |
| `restream_destinations.json` | Profile credentials folder | The destination list **including stream keys** — kept in the credentials folder, not the DB (RestreamViewModel.cs:178, 324-376) |

Stream keys therefore never appear in log output (masked) and live only in the per-profile credentials directory.

## Status & Known Issues

1. **Going live persists your edits** — start persists settings + destinations even if you never pressed Save, so the config you streamed with is the config that comes back (RestreamViewModel.cs:468-478).
2. **Auto-disable is per session** — an excluded destination is only excluded until you stop/start; re-check its key/URL before restarting (RestreamEngine.cs:390-401).
3. **Audio on Source rows** — a Source (pass-through) row with an audio bitrate re-encodes just the audio track; video stays copy (RestreamArgumentBuilder.cs:87-105).
4. **Two-PC binds all interfaces** — anyone on the LAN can attempt to publish to your ingest; the ingest key is the only gate. Keep it non-default on untrusted networks.

### Verified against (2026-11-07)

`RestreamEngine.cs` (whole file — supervision, backoff ladder :22-27, attribution :386-429, port-in-use :278-354, progress parser :560-622), `RestreamArgumentBuilder.cs` (Build :64-154, DestinationUrl :50-61, Mask :217-236, Escape :165-210), `RestreamModels.cs` (modes, ladder, states), `RestreamFfmpegLocator.cs` (resolution order :55-77), `RestreamFfmpegAcquisitionService.cs` (gyan.dev URL :40, extract-only-ffmpeg.exe :145-168), `RestreamViewModel.cs` (persistence :379-392, start validation + persist :440-487, ingest URLs :573-607, health mapping :651-707, Dispose :717-726), `RestreamView.xaml` (PasswordBoxes :214, :341).
