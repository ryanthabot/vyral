# Command Marketplace & Sharecodes

## What it is

Community sharing of flow-editor commands. GitHub (`ryanthabot/vyral` by
default) is the read-only store; a BotGhost webhook relay holds the GitHub
write token, so the app never stores repository credentials.

- **Share (export):** flow editor ▸ EXPORT ▸ online code (`VYRAL-XXXXXXXXXX`
  sharecode, copied to clipboard, published via BotGhost), offline code
  (`VYRAL1-…` self-contained gzip+base64 paste) or classic `.flow.json` file.
- **Browse/import:** Commands page ▸ MARKETPLACE overlay (listing grouped by
  category, search, thumbs up/down, IMPORT per row) or IMPORT CODE (paste
  sharecode / offline code / URL, or pick a `.flow.json` file).
- **Import semantics:** an import loads the flow into the editor as a fresh
  working copy — nothing is persisted until the user presses SAVE. "Uninstall"
  is therefore just deleting the resulting workflow on the Commands page;
  there is no separate installed-state tracking.

## Key files

| Piece | Location |
|---|---|
| Service + settings + share docs | `src/VyralBot.UI/Services/MarketplaceService.cs` |
| Portable `.flow.json` (de)serializer | `src/VyralBot.UI/Services/FlowSnapshotService.cs` |
| Listing VM | `src/VyralBot.UI/ViewModels/MarketplaceViewModel.cs` |
| Listing view (overlay in CommandsView) | `src/VyralBot.UI/Views/MarketplaceView.xaml(.cs)` |
| Export chooser / paste box / share form | `Views/ExportCodeDialog`, `Views/ImportCodeDialog`, `Views/ShareCommandDialog` |
| Tests | `tests/VyralBot.UI.Tests/MarketplaceServiceTests.cs` |

## What a share contains

The whole editor snapshot via `FlowSnapshotService.BuildDocument`: trigger
(type, command name/prefix, platform filter `platforms`, mod tier
`minimumTier`), options (cooldown / permission / channel restriction /
DM-only / guild-only / NSFW), typed parameters, all nodes (blockType,
position, full config — the ~35 block parameter sets) and the `connections`
array. Per-install data is intentionally NOT shared: `BotId` scope, usage
counters (`UsageCount`/`LastUsedAt`) and workflow ids are re-created on import.

## Import safety (wave 4 hardening)

All import surfaces (offline code, online sharecode, file) funnel through
`FlowSnapshotService.ParseDocument`, which now sanitizes every document:

- **Node-type whitelist:** only palette blocks (`BlockCatalog`) plus the
  legacy/canvas types `switch`, `loop_after`, `loop_body`, `comparison_case`,
  `comparison_else`, `menu_start`, `button_start` survive; unknown types are
  dropped and their dangling routing refs (next/true/false/error/cases,
  entry/error-handler pins) cleared. An imported flow can never inject a node
  type this build cannot render or execute.
- **Ceilings:** ≤ `MaxImportNodes` (500) nodes or the document is rejected;
  ≤ 100 options / 100 parameters / 64 config keys per node / 100k chars per
  config value (clamped); node ids ≤ 256 chars, duplicates dropped.
- **Offline codes:** input capped at 4 MB text and decompression is bounded
  (gzip bombs fail instead of expanding into memory).
- **Share documents:** `SchemaVersion` > current (1) or a > 4 MB flow payload
  is refused.
- **Errors:** UI messages come from `MarketplaceService.DescribeImportError`
  (offline / timeout / generic) — raw exception text is never surfaced;
  details go to the log only.

## Export fidelity (wave 4)

`BuildDocument` now round-trips every snapshot field: top-level `description`
and `enabled`, trigger `platforms` (wave 2) and `minimumTier`, positions,
full node configs and all routing. Covered by
`FlowSnapshotRoundTripFidelityTests` and `FlowSnapshotServiceTests`.

## Store robustness

- `HttpClient`: static, 30 s timeout, `UserAgent: VyralBot-Marketplace`.
- Listing is cached 10 minutes (`MarketplaceService.ListAsync`) — reopening
  the marketplace overlay does not refetch; REFRESH re-reads.
- `ListAsync` never throws: it returns `MarketplaceListing(Entries, Error)`
  so the UI distinguishes an empty store from offline/rate-limited
  (HTTP 403/429 shows "rate-limited, cached commands shown" + retry hint;
  network failures keep stale cache). The listing view shows a retry banner
  via `MarketplaceViewModel.HasLoadError`.
- Per-entry fetch failures are skipped and logged (warning), never fatal.

## Secrets

The BotGhost API key is stored per profile (`Configs: Marketplace.ApiKey`,
plain config key today) and masked in a `PasswordBox` —
`MarketplaceView.xaml.cs` syncs it manually (PasswordBox is not bindable) and
re-seeds the box whenever the settings object is reloaded. Webhook URLs and
the key are never logged.
