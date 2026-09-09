# Marketplace Setup Checklist

One-time setup that turns on cloud sharing, the marketplace, and voting.
Offline share codes work without any of this.

## 1. GitHub — the marketplace repo

1. Create (or make public) the repository **`ryanthabot/vyral`**, default branch **`main`**.
   - Public is required: the app lists and fetches commands with unauthenticated GitHub requests.
2. You do **not** need to create the `marketplace/` folders — the first shared command creates them.

## 2. GitHub — the write token (stored ONLY in BotGhost)

1. GitHub → Settings → Developer settings → **Fine-grained tokens** → Generate new token.
2. Repository access: **Only select repositories** → `ryanthabot/vyral`.
3. Permissions: **Contents → Read and write** (nothing else).
4. Copy the token — you will paste it into BotGhost in step 3.

Never ship this token inside the app: VyralBot never holds it.

## 3. BotGhost — the write relay (two webhook events)

In the BotGhost dashboard → **Custom Commands & Events → Custom Events**, create two
**Webhook** events (set the base server ID when asked). Each event gives you a webhook URL
like `https://api.botghost.com/webhook/<bot_id>/<event_id>` — you'll paste both URLs plus your
BotGhost API key into the app in step 4.

Store the GitHub token first: in BotGhost's Data Storage module create a global **text**
variable (e.g. reference `{BGVAR_github_token}`) with the PAT from step 2 as its value.

The app POSTs a `variables` array; each entry becomes an event variable usable in actions.

### Event 1 — share-submit

Receives: `{gh_path}` (file path), `{gh_message}` (commit message), `{gh_content}` (base64 file body).

Add one **Send an API Request** action:

| Field | Value |
| --- | --- |
| Name | `gh_commit` |
| Request type | `PUT` |
| URL | `https://api.github.com/repos/ryanthabot/vyral/contents/{gh_path}` |
| URL options | Enable **Replace variables in the URL** |
| Headers | `Authorization: Bearer {BGVAR_github_token}` · `Accept: application/vnd.github+json` · `User-Agent: VyralBot` |
| Body | Raw JSON: `{"message":"{gh_message}","content":"{gh_content}","branch":"main"}` |
| Body options | Enable **Replace variables in the body** |

### Event 2 — rating-submit

Receives: `{gh_path}`, `{gh_message}`, `{gh_content}`, and `{gh_sha}`
(the file's blob SHA, **empty when the file doesn't exist yet**).

Add actions in this order:

1. **Send an API Request**, name `sha_check` — `GET`
   `https://api.github.com/repos/ryanthabot/vyral/contents/{gh_path}` with the same three
   headers and URL-variable replacement (this would 404 for a first vote; that's fine).
2. **Condition**: `{sha_check.status}` **equals** `200`
   - **true branch** → **Send an API Request**: `PUT` (same URL + headers as event 1) with raw
     body `{"message":"{gh_message}","content":"{gh_content}","branch":"main","sha":"{sha_check.response.sha}"}`
   - **false branch** → same `PUT` but body
     `{"message":"{gh_message}","content":"{gh_content}","branch":"main"}` (no `sha` → GitHub
     treats it as a create).

> **If it doesn't work:** the most likely failure is BotGhost not substituting `{gh_path}` into
> the request URL (check the event's logs / test with a manual POST). Tell ZCode — the fallback
> is embedding a restricted fine-grained token in the app instead.

## 4. VyralBot — app settings

Commands page → **MARKETPLACE** → **⚙** (top right):

- **Share webhook URL** (event 1 URL) and **Rating webhook URL** (event 2 URL)
- **BotGhost API key** (dashboard → your bot's API key)
- GitHub owner / repo / branch (defaults: `ryanthabot` / `vyral` / `main`)

## 5. Verify

1. Open the flow editor on any command → **SHARE** → pick a category → SHARE.
   The app copies the code and waits until the file shows up on GitHub
   (`marketplace/commands/<CODE>.json`) — its status bar confirms when it's live.
2. Commands → **MARKETPLACE** → your command appears under its category → **IMPORT** loads it
   into the flow editor as a fresh working copy.
3. **👍** on the listing → `marketplace/ratings/<CODE>.json` appears with `{"up":1,"down":0}`.
4. **OFFLINE CODE** in the editor copies a `VYRAL1-…` blob; **IMPORT CODE** in the marketplace
   pastes it back (works with no internet and no setup).

## Sharing codes with users

A user only needs the code (`VYRAL-XXXXXXXXXX`) — they open the marketplace, press
**IMPORT CODE**, paste it, and the command opens in their builder. Offline codes
(`VYRAL1-…`) can be exchanged anywhere (Discord DM, pastebin) and import the same way.
