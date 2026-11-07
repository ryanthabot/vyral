# Marketplace & Sharecodes

> A community store of ready-made commands: browse shared flows on GitHub from the **MARKETPLACE** overlay on the Commands page, import one as an editable working copy, review it on the canvas, and SAVE to make it yours — then share your own back out as a code anyone can paste. GitHub is the read-only store; a BotGhost webhook relay holds the write credentials, so the app never stores repository tokens.

## Overview

The marketplace exists because every command — built-in or custom — is now a [flow](automation.md#visual-flow-editor) ([Commands](commands.md)), and a flow serializes cleanly. A **sharecode** is that serialization: an online code (`VYRAL-XXXXXXXXXX`, a short id resolved against the GitHub store) or an **offline code** (`VYRAL1-…`, the whole flow gzip-compressed and base64-pasted — no network needed). There is deliberately **no "installed" state**: importing loads the flow into the editor for review, SAVE persists it as an ordinary workflow, and "uninstalling" is deleting that workflow like any other. Nothing tracks where a command came from.

Three surfaces, one import pipeline:

- **Commands page → MARKETPLACE** — the browsing overlay (listing, search, voting, one-click import).
- **Flow editor → EXPORT / IMPORT** — the share-out path and the paste-a-code path ([Automation](automation.md)).
- **File** — classic portable `.flow.json` documents, in and out.

## User Guide

### Browsing and importing from the Commands page

1. Open **Chat Features → Commands** and click **MARKETPLACE** in the page header. The listing loads over the page as an overlay.
2. Entries are grouped into the fixed sections — **Tickets, Support, Giveaways, Games, Economy, Other** — each row showing the command's name, description, author, sharecode, and 👍/👎 vote counts. The **search** box filters name/description/author/code live.
3. Click **IMPORT** on a row. The overlay closes and the flow opens **in the visual editor as a working copy** — nothing has been saved yet.
4. **Review it on the canvas**: the trigger (type, command name, platform filter, mod tier), Options (cooldown, permission, channel restrictions), typed parameters, and every node with its config are all there, editable like a flow you built yourself. Check the Send Message blocks for what it will actually say.
5. Press **SAVE**. The flow persists and the command answers from that moment. Closing without saving discards the import entirely.

If the listing fails to load (offline, or GitHub rate-limiting an anonymous client), the overlay keeps showing the cached commands with a retry hint instead of an error wall — **REFRESH** re-reads the store.

### Importing a code someone gave you

**IMPORT CODE** (on the same overlay, and the flow editor's IMPORT button) opens a paste box that accepts any of:

- an **online sharecode** (`VYRAL-XXXXXXXXXX`) — resolved against the store,
- an **offline code** (`VYRAL1-…`) — self-contained, works with no network,
- a **store URL** pasted whole,
- or a picked **`.flow.json` file** (size-checked before reading).

Every path lands in the same place: the editor with a fresh working copy, ready for review and SAVE.

### "Uninstalling"

There is nothing to uninstall — an imported command you saved is just a workflow. Remove it the normal way: delete it on the Commands page (a flow-adopted command row deletes both), or delete the workflow row on the Automation page. Deleting cleans up everything, including trigger registrations.

### Sharing your own command out

In the [flow editor](automation.md#visual-flow-editor), open any saved flow and click **EXPORT** — a chooser with three formats:

| Choice | What you get | Best for |
|---|---|---|
| **Online code** | A `VYRAL-…` sharecode copied to your clipboard — and optionally **published to the store** (a share dialog asks for a name, description, and category; publishing goes through the BotGhost relay) | Sharing publicly |
| **Offline code** | A `VYRAL1-…` self-contained paste (gzip + base64 of the whole flow) | DMs, forums, no-network handoff |
| **File** | A classic `.flow.json` document saved to disk | Backups, attachments |

A share contains the **whole editor snapshot** — trigger, options, typed parameters, all nodes and routing — but never your per-install data: the Discord **bot scope** (`BotId`), usage counters, and workflow ids are dropped and re-created on import, so an imported command is unscoped and starts at zero usage.

### Voting and settings

- **👍 / 👎** on a listing row submits a vote through the BotGhost rating webhook. Voting needs the webhook configured (⚙ in the overlay) — without it the buttons explain themselves instead of failing silently.
- **⚙ Settings** (in the overlay) holds the BotGhost **webhook URL** and **API key** (masked entry) plus an optional store repo override. Sharing and voting need them; browsing and importing do not.

## How It Works (Developer)

- [MarketplaceService.cs](../../src/VyralBot.UI/Services/MarketplaceService.cs) is the client: GitHub raw-URL reads against `ryanthabot/vyral` by default (owner/repo overridable in settings), a **10-minute listing cache**, `FetchCommandAsync` for sharecodes, `ResolveImportInputAsync` for pasted input (code/URL), `SubmitShareAsync`/`SubmitRatingAsync` through BotGhost, and SHA-contract rating writes. `ListAsync` **never throws** — it returns entries plus an error string so the UI can show the cached list with a retry hint. One static `HttpClient`, 30 s timeout, `UserAgent: VyralBot-Marketplace`.
- [FlowSnapshotService.cs](../../src/VyralBot.UI/Services/FlowSnapshotService.cs) is the portable-document (de)serializer — `BuildDocument` (export) and `ParseDocument` (import) — and the **import sanitizer** every surface funnels through:
  - **Node-type whitelist** — only palette blocks (`BlockCatalog`) plus the legacy canvas types (`switch`, `loop_after`, `loop_body`, `comparison_case`, `comparison_else`, `menu_start`, `button_start`) survive; unknown types are dropped *and* their dangling routing references (next/true/false/error/cases, entry/error pins) are cleared, so a dropped node can never leave a wire pointing at nothing.
  - **Ceilings** — at most 500 nodes (reject above), 100 options, 100 parameters, 64 config keys per node, 100k chars per config value (clamped), node ids ≤ 256 chars, duplicate ids dropped.
  - **Bombs** — offline-code input capped at 4 MB with bounded decompression (gzip bombs fail, not expand); share documents with `SchemaVersion` above the current one or a >4 MB payload are refused.
- **Import semantics** ([FlowEditorViewModel.cs](../../src/VyralBot.UI/ViewModels/FlowEditorViewModel.cs), `ImportSharedSnapshot` :5956): the snapshot becomes the working copy — a previously open draft is replaced, `_sourceCommandId` is nulled (an import is never a command adoption), and nothing touches the database until SAVE.
- **Hosting** ([CommandsView.xaml.cs](../../src/VyralBot.UI/Views/CommandsView.xaml.cs)): the MARKETPLACE header button lazily resolves the singleton `MarketplaceViewModel`, hosts `MarketplaceView` in the page overlay, and refreshes on open; `ImportRequested` hands the parsed snapshot to the page's flow-editor overlay directly.
- **Error wording** comes from `MarketplaceService.DescribeImportError` (offline / timeout / generic) — raw exception text never reaches the UI; details go to the log.

## Files & Code

| File | Role |
|---|---|
| [MarketplaceService.cs](../../src/VyralBot.UI/Services/MarketplaceService.cs) | Store client (listing/fetch/resolve/share/rate), settings, categories, import-error wording |
| [FlowSnapshotService.cs](../../src/VyralBot.UI/Services/FlowSnapshotService.cs) | Portable `.flow.json` build/parse + the import sanitizer (whitelist, ceilings, bomb guards) |
| [MarketplaceViewModel.cs](../../src/VyralBot.UI/ViewModels/MarketplaceViewModel.cs) | Overlay state: grouped listing, search, voting, settings, import commands |
| [MarketplaceView.xaml](../../src/VyralBot.UI/Views/MarketplaceView.xaml) / `.cs` | The overlay UI (hosted inside CommandsView); code-behind syncs the masked API-key PasswordBox |
| [ExportCodeDialog.xaml](../../src/VyralBot.UI/Views/ExportCodeDialog.xaml) / [ImportCodeDialog.xaml](../../src/VyralBot.UI/Views/ImportCodeDialog.xaml) / [ShareCommandDialog.xaml](../../src/VyralBot.UI/Views/ShareCommandDialog.xaml) | Format chooser, paste/pick box, publish form |
| [FlowEditorViewModel.cs](../../src/VyralBot.UI/ViewModels/FlowEditorViewModel.cs) | EXPORT (`ExportCurrentFlowAsync` :5833), IMPORT (`ImportSharedSnapshot` :5956), publish (`ShareCurrentFlowAsync` :5978) |
| [CommandsView.xaml.cs](../../src/VyralBot.UI/Views/CommandsView.xaml.cs) | MARKETPLACE button → overlay hosting + the ImportRequested hand-off (:41-103) |

## Data & Settings

| Key | Store | Meaning |
|---|---|---|
| `Marketplace.*` (webhook URL, API key, owner/repo/branch overrides) | Configs (per-profile SQLite) | Connection settings edited in the overlay's ⚙ panel; the API key is masked in the UI |
| `.flow.json` documents | Files on disk | Portable command documents (export/import) |

Imported/saved commands are ordinary `Workflows` rows — no marketplace-specific tables, no installed-state tracking.

## Status & Known Issues

- **Import review is the safety model** — nothing from the store (or a pasted code, or a file) is persisted until you review it in the editor and press SAVE; the sanitizer additionally guarantees an import cannot inject a node type this build cannot render or execute.
- **Sharing and voting need the BotGhost relay** — GitHub is read-only for the app by design; without the webhook settings, browse/import still work, share/vote explain what is missing.
- **Anonymous GitHub reads can be rate-limited** — the overlay degrades to the 10-minute cache with a retry hint rather than failing (HTTP 403/429 detected explicitly).
- **The API key sits in a plain Configs value** — masked in the UI, never logged, but not DPAPI-wrapped (unlike bot tokens). See [Settings → Security](../settings/overview.md#security-category).
