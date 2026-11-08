# Restream (multi-platform FFmpeg fan-out)

One OBS output fanned out to N platform destinations (Twitch / YouTube /
Kick / custom RTMP), each at its own resolution and bitrate. Works single-PC
(OBS publishes to a loopback RTMP ingest) and two-PC (a gaming PC publishes
over the LAN). Built across marathon waves 1–8; wave 8 (2026-11-07) pinned
the FFmpeg acquisition, added the uplink-budget hint and surfaced dropped
frames.

## Components

| Piece | Where |
|---|---|
| `RestreamEngine` (singleton supervision: listen-mode ingest, crash backoff, auto-disable of failing destinations) | `VyralBot.Integrations/Restream/RestreamEngine.cs` |
| `RestreamArgumentBuilder` (FFmpeg argv, Windows escaping, stream-key masking) | `VyralBot.Integrations/Restream/RestreamArgumentBuilder.cs` |
| `RestreamValidation` (shared row/start gate: rtmp/rtmps only, key-embedded URLs) | `VyralBot.Integrations/Restream/RestreamValidation.cs` |
| `RestreamBandwidth` (combined-bitrate uplink hint) | `VyralBot.Integrations/Restream/RestreamBandwidth.cs` |
| `RestreamFfmpegLocator` (override → bundled → PATH resolution + `-version` probe) | `VyralBot.Integrations/Restream/RestreamFfmpegLocator.cs` |
| `RestreamFfmpegAcquisitionService` (verified FFmpeg download/extract) | `VyralBot.Integrations/Restream/RestreamFfmpegAcquisitionService.cs` |
| `RestreamViewModel` + `RestreamView` | `VyralBot.UI/ViewModels` / `Views` |

All registered as singletons in `App.xaml.cs` `ConfigureServices`; the page
VM subscribes to the engine with named handlers and unsubscribes on `Dispose`
(navigating away never stops a live session — only Stop/app exit does).

## FFmpeg acquisition (Download button)

- **Source**: gyan.dev — the official Windows FFmpeg distribution linked from
  ffmpeg.org (GPL build including x264).
- **Pinned artifact**: the URL is the *immutable versioned* package
  `ffmpeg-9.0.2-essentials_build.zip` under `/builds/packages/` (~110 MB),
  NOT the rolling `ffmpeg-release-essentials.zip` (its bytes change with
  every release, so it cannot be hash-pinned).
- **Integrity**: after download the zip's SHA-256 must equal the pinned
  `RestreamFfmpegAcquisitionService.ExpectedSha256` constant (computed from
  the artifact and cross-checked against the publisher's `.zip.sha256`
  sidecar on 2026-11-07). Mismatch = terminal failure; the zip is deleted
  and nothing is extracted.
- **Retry**: dropped/truncated downloads are retried up to 3 times (full
  restart — no HTTP range resume); a checksum mismatch is NOT retried.
- **Disk space**: preflight requires ~110 MB free on the temp drive and
  ~100 MB on the appdata drive (pinned byte sizes + 64 MB margin); failures
  name the drive and amounts.
- **Atomic install**: ffmpeg.exe extracts to `appdata/bin/ffmpeg/ffmpeg.exe.tmp`
  and is moved over the final name only when complete — no half-extracted
  binary is ever visible to the locator. `BundledFfmpegPath` is the
  locator's first candidate; `appdata` follows
  `Core/Services/AppDataRoot.GetRoot()` (exe-adjacent when writable, else
  `%LOCALAPPDATA%\VyralBot`).
- **Bumping the pinned version**: update `DownloadUrl`, `ExpectedSha256`,
  `ExpectedZipBytes`, `ExpectedFfmpegBytes` together.

## Bandwidth guard (uplink-budget hint)

`RestreamBandwidth.Evaluate` sums the enabled destinations' configured
output bitrates (scaled rows: video bitrate with the builder's 500 kbps
floor + 128/160 kbps audio re-encodes; "Source" pass-through rows relay the
publisher's unknown OBS bitrate and count as 0 — the hint text calls that
out). Above **80% of the cap** the Destinations card shows a warning; it is
a status hint only and never blocks a session.

- Default cap: **15 Mbps** (`RestreamBandwidth.DefaultCapKbps = 15000`).
- User override: "Uplink budget (Mbps)" field on the Restream page,
  persisted as `BandwidthCapKbps` in the `Restream.Settings` config value
  (`IDataService`, category "Restream"); invalid/empty falls back to the
  default.
- Refreshed on: destination add/remove, any row bitrate/resolution/audio/
  enabled edit (`ConfigurationChanged` named events, unsubscribed on
  remove/dispose), cap edits, and immediately before going live.

## Stream health

- **Aggregate stats**: FFmpeg runs with `-progress pipe:1 -stats_period 1`;
  the engine's `ProgressParser` turns the key=value blocks into
  `RestreamStats` (fps, combined bitrate kbps, speed ×, `drop_frames`), and
  the page shows UPTIME / BITRATE / FPS / SPEED / DROPPED in the live-status
  card (DROPPED added in wave 8).
- **Per-destination stats**: not separable — one FFmpeg process publishes
  all outputs and its progress stream is aggregate. Per-destination health
  IS tracked (Active / Error / AutoDisabled rows, with masked FFmpeg error
  detail), including auto-disable after two attributed session kills.

## UI states → actions (every state has one)

| State | Action |
|---|---|
| FFmpeg not found | Download (~110 MB, checksum-verified) or Browse… to a local ffmpeg.exe |
| No destinations | "+ Twitch / YouTube / Kick / Custom" buttons |
| Row invalid | Inline validation message (never echoes the stream key) |
| Running | Stop Restream (Start button becomes Restart) |
| Destination auto-disabled | "Restart & Re-enable" hint button |
| Over uplink budget | Warning hint + budget field / per-row bitrate edits |
| Faulted (port busy, all destinations failed…) | Engine message names the cause + remediation |

## Persistence & secrets

- Non-secret settings (mode, ingest port, ffmpeg override, bandwidth cap):
  `Restream.Settings` config value (category "Restream"). **Going live
  persists the exact session configuration** before the engine starts.
- Destinations (including stream keys): per-profile
  `restream_destinations.json` in the profile credentials folder
  (`IProfileService.GetProfileCredentialsPath`), never logged; keys are
  masked in every log surface via `RestreamArgumentBuilder.Mask`.

## Tests

`tests/VyralBot.UI.Tests/`: `RestreamArgumentBuilder{,Regression}Tests`,
`RestreamWave3RegressionTests`, `RestreamWave4Tests` (validation/builder
agreement, persistence round-trip, key masking),
`RestreamWave8Tests` (bandwidth hint boundaries + acquisition pinning).
