# Variables Catalog

The exhaustive catalog of every template variable in VyralBot. Variables are `{kebab-case}` placeholders resolved at send time in command responses, manual chat sends, and social templates. Feature page: [Variables](../chat-features/variables.md).

Related reference pages: [Command Catalog](command-catalog.md), [Database Reference](database-reference.md).

---

## Syntax and resolution rules

Implemented in [VariableReplacementService.cs](../../src/VyralBot.Core/Services/Variables/VariableReplacementService.cs).

- **Syntax**: `{name}` where `name` must be kebab-case lowercase — regex `^[a-z0-9]+(-[a-z0-9]+)*$` (`VariableReplacementService.cs:16`). The placeholder match itself is case-insensitive, but names are normalized to lowercase. Names with underscores or capitals will not resolve.
- **Merge order**: the service loads all DB variables that have a non-empty `Value` (per-platform filter applied), then merges the caller's context variables over them — **context wins**, case-insensitively (`VariableReplacementService.cs:38-71`).
- **System/context variables have no stored value**. They are seeded with `Value = null`, which means the DB lookup never supplies them; they only resolve when the calling code passes them in the context dictionary.
- **Recursion**: replacement is recursive up to **depth 5** (`MaxNestedDepth = 5`), with per-pass circular-reference detection — a variable whose expansion contains itself is left alone after one pass. This is what lets `full-socials` (whose value contains `{twitter}`, `{discord}`, `{youtube}`) resolve.
- **Cache**: DB variables are cached for **5 minutes** (`CacheExpiryMinutes = 5`) in a per-platform cache. The Variables manager invalidates the cache after every save/delete, so edits apply immediately there; changes made directly in the database take up to 5 minutes to appear.
- **Unknown placeholders are left as-is** — `{notdefined}` passes through unchanged rather than erroring. On any exception the original template is returned.

31 variables are seeded into an empty database at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):3583-3737: 19 system/context, 6 auto-updating, 6 social.

---

## System / context variables (19)

`IsSystem = 1`, `IsEditable = 0`, category "Global", no stored value — each resolves only when the calling code supplies it in the context. The "where it resolves" column lists the code paths that actually pass the variable today.

| Variable | Type | Where it resolves | Description |
|---|---|---|---|
| `{username}` | string | Command responses; auto-reply templates; event reactions | Display name of the user who triggered the event or command. |
| `{message}` | string | Passed by event handlers where relevant | The user's chat message content. |
| `{months}` | number | Sub/resub event auto-replies | Number of months subscribed. |
| `{number}` | number | Event handlers passing generic counts | Generic number (followers, gift subs, etc.). |
| `{platform}` | string | Command responses; manual chat sends; auto-reply templates | Platform name (Kick, Twitch, etc.). |
| `{viewers}` | number | Raid/host event auto-replies | Number of viewers (raids, hosts). |
| `{bits}` | number | Cheer event auto-replies | Twitch bits amount cheered. |
| `{reward}` | string | Reward-redeemed event auto-replies | Channel point reward name. |
| `{tier}` | string | Sub event auto-replies | Sub tier (1000, 2000, 3000). |
| `{gifter}` | string | Gift-sub event auto-replies | Username of the gift-sub sender. |
| `{raider}` | string | Raid event auto-replies | Username of the raider. |
| `{hoster}` | string | Host event auto-replies | Username of the hoster. |
| `{sender}` | string | Donation-type event paths | Username of the sender (donations, etc.). |
| `{amount}` | number | Donation/tip event paths | Monetary amount (donations, tips). |
| `{balance}` | number | Command responses (supplied by `CommandService.ParseVariables`) | User's current currency balance. |
| `{role}` | string | Command responses (supplied by `CommandService.ParseVariables`) | User's role (Broadcaster, Moderator, VIP, Subscriber, Viewer). |
| `{channel}` | string | Command responses; manual chat sends | Name of the channel. |
| `{uptime}` | string | Nowhere (no uptime tracking exists) | How long the stream has been live. |
| `{watchtime}` | string | Nowhere live (the seeded `!watchtime` command template references it, but that built-in returns a placeholder before template use) | How long the user has watched. |

Notes:

- `CommandService.ParseVariables` (`CommandService.cs:325-353`) supplies exactly `username`, `platform`, `channel`, `balance`, `role` plus any `CommandContext.CustomVariables`.
- Manual chat sends (`ChatViewModel` `SendMessageAsync` paths) build a context of `bot-username`, `channel`, `platform` — so a manually typed `{bot-username}` resolves to the **bot's** name. `{username}` is not in that context, so it stays literal in manual sends (system variables have no DB value to fall back on).
- Event-reaction auto-replies pass event-specific keys (see the auto-reply section below).

---

## Auto-updating platform variables (6)

`IsAutoUpdating = 1`, `IsEditable = 0`, `Value` synced from webhook/API data (seeded to `"0"`), category = platform name, platform-scoped via the `Platforms` column. They resolve from the DB value like ordinary variables; the difference is that external data keeps the value current (`LastSyncedAt` tracks the sync).

| Variable | Type | Where it resolves | Description |
|---|---|---|---|
| `{kick-followercount}` | number | Any full variable-replacement path (command responses, manual sends) | Current Kick follower count. |
| `{kick-subscribercount}` | number | Any full variable-replacement path | Current Kick subscriber count. |
| `{kick-viewcount}` | number | Any full variable-replacement path | Current Kick viewer count. |
| `{twitch-followercount}` | number | Any full variable-replacement path | Current Twitch follower count. |
| `{twitch-subscribercount}` | number | Any full variable-replacement path | Current Twitch subscriber count. |
| `{twitch-viewcount}` | number | Any full variable-replacement path | Current Twitch viewer count. |

Caveat: these values are synced from webhook/API payloads. The Twitch event pipeline is currently gated on a `UserId` that is never set ([Known Issues](known-issues.md) KI-2), and Kick channel events depend on webhook/Pusher data — so in practice the values only update for the event sources that are actually delivering data.

---

## Editable social variables (6)

`IsEditable = 1`, category "Social", editable in the Variables manager (Settings → Variables). These have stored values and resolve from the DB in every full replacement path.

| Variable | Type | Where it resolves | Description |
|---|---|---|---|
| `{twitter}` | string | Command responses; manual chat sends; nested inside `{full-socials}` | Your Twitter/X handle or URL (seeded empty). |
| `{discord}` | string | Command responses; manual chat sends; nested inside `{full-socials}` | Your Discord invite link (seeded empty). |
| `{youtube}` | string | Command responses; manual chat sends; nested inside `{full-socials}` | Your YouTube channel URL (seeded empty). |
| `{instagram}` | string | Command responses; manual chat sends | Your Instagram handle or URL (seeded empty). |
| `{tiktok}` | string | Command responses; manual chat sends | Your TikTok handle or URL (seeded empty). |
| `{full-socials}` | string (composite) | Command responses; manual chat sends | Combined social links. Seeded value: `Twitter: {twitter} \| Discord: {discord} \| YouTube: {youtube}` — flagged `ContainsNestedVariables = 1` and resolved through the depth-5 recursion. |

The seeded `!socials` and `!discord` built-in commands reference `{twitter}`, `{discord}`, `{youtube}` — see the [Command Catalog](command-catalog.md).

---

## Where variables resolve (and where they do not)

Three replacement paths exist in the app, and they do **not** behave the same:

1. **Command responses** (full engine): `CommandService.ParseVariables` → `VariableReplacementService.ReplaceVariablesAsync`. DB variables, auto-updating variables, and social variables all resolve, merged under the command context keys. Note this call is sync-over-async (`.GetAwaiter().GetResult()`).
2. **Manual chat sends** (full engine): in `ChatViewModel`, when the typed message contains `{`, a context (`bot-username`, `channel`, `platform`) is built and the full engine runs before sending — to one platform or the fan-out "send to all". See [Sending Messages](../chat/sending-messages.md).
3. **Event auto-reply templates** (simple replace — the big difference): `AutoReplyService.ProcessTemplate` (`AutoReplyService.cs:37-51`) is a plain case-insensitive `string.Replace` over the keys the event handler passes in — typically `{username}`, `{platform}`, and event fields (`{months}`, `{count}`, `{gifter}`, `{raider}`, `{viewers}`, `{bits}`, `{reward}`, `{amount}`, ...).

> **Auto-reply templates do NOT resolve database or social variables.** `{twitter}` or `{followercount}`-style placeholders in an auto-reply are sent literally. Only the context keys above are replaced. Additionally, default auto-reply templates ship as `@{username}` — the simple replace only touches `{username}`, so a stray `@` remains in the sent message. Both quirks are tracked as [Known Issues](known-issues.md) KI-12. Details: [Auto-Replies](../chat-features/auto-replies.md).

Alerts/overlays are fed from event properties directly, not through this engine. The condition/rule engine (`RuleEvaluator`) does not resolve variables at all.

---

## Editing variables

The Variables manager (Settings → Variables, hosting `VariablesViewModel`) supports category filtering, text search, and add/edit/delete for editable (`IsEditable = 1`) variables only. Saving enforces the kebab-case name rule and invalidates the 5-minute cache. System and auto-updating rows are read-only. See [Variables](../chat-features/variables.md).

The **VariablePicker** is attached to every TextBox application-wide (global style in `App.xaml` → `TextBoxVariableBehavior`), offering a context-menu insert of `{name}` at the caret. Its category inference maps `twitch-*`/`kick-*` prefixes to the platform categories and the social set to Social.

---

## See also

- [Command Catalog](command-catalog.md) — which templates use which variables
- [Auto-Replies](../chat-features/auto-replies.md) — the simple-replace path and its limitations
- [Database Reference](database-reference.md) — the `Variables` table schema
- [Known Issues Ledger](known-issues.md) — KI-12 (auto-reply variable limitations)
