# Command Marketplace & Sharecodes — Design

Date: 2026-09-08
Status: Approved (user approved via plan mode; BotGhost+GitHub hybrid chosen over BotGhost-only,
Cloudflare Worker, and offline-only alternatives).
Supersedes: none.

## Problem statement

Users build commands in the visual flow editor (Workflows). Today a command lives only inside one
profile's SQLite database. We want:

1. **Sharecodes** — after building a command, a user gets a short code (e.g. `VYRAL-7K3F9M2Q4D`)
   that any other PC can use to load the command's contents into their flow editor.
2. **Marketplace** — an in-app browser of all shared commands, grouped into sections
   (tickets / support / giveaways / games / economy / other), with 👍👎 ratings and optional author
   credit, importable in one click into the visual builder.
3. **Offline paste-codes** — a fully self-contained code (`VYRAL1-…`) that carries the whole
   command with zero backend, exchangeable anywhere.

## Non-goals (explicit)

- No user accounts / auth. Author credit is a free-text field, cosmetic only.
- No moderation tooling in v1 (git history on the marketplace repo is the cleanup path).
- No ngrok round-trip confirmation of shares (fire-and-forget + verify-by-refetch in v1; the
  BotGhost → app ngrok push-back is a v2 nicety).
- No editing/deleting of already-shared commands by their authors in v1 (republishing produces a
  new code).
- BotGhost remains a **write relay only** — the marketplace list is never enumerated from
  BotGhost variables (collection iteration is unverified and the read path belongs to GitHub).

## Architecture decisions

1. **GitHub is the database (read path).** Repo `ryanthabot/vyral` (public), branch `main`:

   ```
   marketplace/
     commands/
       VYRAL-7K3F9M2Q4D.json      # full share document
     ratings/
       VYRAL-7K3F9M2Q4D.json      # {"up": 12, "down": 1}
   ```

   Flat `commands/` folder; category lives inside the JSON and the UI groups by it. Every command
   is fetchable by code alone: `raw.githubusercontent.com/ryanthabot/vyral/main/marketplace/commands/<CODE>.json`.
   Listing uses one unauthenticated Contents API call per browse
   (`GET /repos/ryanthabot/vyral/contents/marketplace/commands`), cached 10 minutes per profile
   (GitHub allows 60 unauthenticated requests/hour per IP — the cache keeps normal use far under).

2. **BotGhost is the write relay and token vault.** The app never ships a GitHub write token
   (anyone could extract it from a distributed desktop app). Instead the app POSTs payloads to two
   BotGhost **webhook custom events**; the bot's "Send an API Request" action performs the GitHub
   Contents API PUT with `Authorization: Bearer {token}`, where the fine-grained token (contents:
   write on `ryanthabot/vyral` only) is stored as a BotGhost data-storage variable.
   - `share-submit`: body carries `commit_message`, `path`, `content_b64` (app pre-base64s the
     document so BotGhost only forwards it) → PUT new file (no SHA needed for new files).
   - `rating-submit`: body carries `path`, `content_b64`, and the action sequence is
     GET file → save `{sha}` from the response → condition → PUT with `sha` (updates require the
     existing file's SHA; new ratings files PUT without one).
   - Verified BotGhost capabilities this relies on: webhook custom events accept external POSTs
     (Authorization: API key), the request builder supports method/URL/headers/raw-JSON-body with
     variable interpolation in all of them ("Replace variables in the URL" toggle), and responses
     are saved to variables usable in later actions/conditions. **Fallback if URL interpolation
     fails in practice:** ship a locked-down fine-grained token embedded in the app and commit
     directly (worse secret posture, acceptable; decided at setup time with the user).

3. **Share document = existing flow export + metadata.** The flow editor already serializes a
   portable document (`FlowEditorViewModel.BuildExportDocument()` → id/name/trigger/options/
   parameters/nodes/connections/metadata, DTOs `FlowEditorSnapshotDto` etc.). The share document
   wraps it:

   ```json
   {
     "sharecode": "VYRAL-7K3F9M2Q4D",
     "name": "Giveaway Roll",
     "description": "Rolls a winner from viewers",
     "category": "games",
     "author": "PixelQueen",
     "createdAt": "2026-09-08T12:00:00Z",
     "schemaVersion": 1,
     "flow": { …BuildExportDocument() output… }
   }
   ```

   Import unwraps `flow` and feeds the snapshot through the existing `ImportJson()` path, so a
   shared command opens in the flow editor as a fresh editable working copy — identical to file
   import. Unknown fields are already tolerated by the workflow JSON loader (forward compatible).

4. **Sharecode format.** `VYRAL-` + 10 chars from Crockford base32 (no 0/O/1/I), generated with a
   cryptographic RNG app-side (≈10^15 space; collision-check against the cached listing, retry on
   hit). Offline codes: `VYRAL1-` + base64url(gzip(flow JSON)) — prefix distinguishes them from
   cloud codes on import.

5. **Categories.** Fixed set: `tickets, support, giveaways, games, economy, other`. The share
   dialog is a dropdown; anything unmapped parses as `other`.

6. **Ratings.** Thumbs up/down per code, one JSON file per code
   (`{"up": n, "down": n}`). App reads the current file from GitHub, increments locally, sends the
   new counts to BotGhost for commit. Racing votes can lose a count — harmless at this scale.

7. **App settings.** BotGhost webhook base + API key (+ repo owner/name/branch for flexibility)
   stored in the existing per-profile `Configs` key-value storage, dotted keys `Marketplace.*`.
   Cloud share/browse degrade gracefully when unset (share dialog shows setup guidance; marketplace
   shows offline import only).

## App changes

- **`FlowSnapshotService`** (new, `src/VyralBot.UI/Services`): extracts `BuildExportDocument()` /
  snapshot-DTO serialization from `FlowEditorViewModel` so the share path and the editor share one
  implementation. Existing Export/Import behavior unchanged.
- **`MarketplaceService`** (new): sharecode generation, share-document build/unwrap, offline
  encode/decode, GitHub GETs + cache, BotGhost webhook POSTs, rating increment.
- **UI:** SHARE + COPY OFFLINE CODE buttons in the flow editor toolbar (next to EXPORT/IMPORT);
  Share dialog (name/description/category/author); Marketplace overlay launched from the Commands
  page (category sections, search, 👍👎, import); Import-paste dialog that auto-detects offline
  blob vs cloud code.
- **Tests** (`tests/VyralBot.UI.Tests`): sharecode generation (alphabet/length/uniqueness), share
  document round-trip, offline encode/decode round-trip, rating increment, GitHub path/URL helpers.

## Setup checklist (user actions before cloud features work)

1. Create (or make public) `ryanthabot/vyral`, default branch `main`.
2. GitHub fine-grained PAT: **Contents read/write** on that repository only.
3. BotGhost dashboard: create the two webhook custom events (`share-submit`, `rating-submit`) with
   the API-request actions described above; store the PAT as a data-storage text variable; note the
   webhook URL + API key.
4. VyralBot → Settings → Marketplace: paste webhook URL + API key (+ repo/branch if non-default).

## Risks

- **BotGhost URL interpolation** unverified in a live dashboard — test once at setup; fallback
  documented above.
- **Spam:** the BotGhost key embedded in the app allows submissions by extractors. Git history
  makes cleanup trivial; real mitigation (moderation queue / Worker relay) deferred to v2.
- **Rate limits:** BotGhost webhook 429s on share bursts (shares are infrequent; surfaced in UI);
  GitHub 60 req/h unauth listing (10-min cache; optional read-only token later if needed).
- **Repo availability:** public repo required for unauthenticated reads.
