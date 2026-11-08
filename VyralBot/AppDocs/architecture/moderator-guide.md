# Moderator Guide

> The complete guide to VyralBot's moderation team system: a private, auto-created `#vyral-mods` Discord channel where your mods run tier-gated `!mod` commands — kicks, timeouts, currency edits, giveaway and poll control — with every action audited and visible in the app, plus an in-app command builder with favorites so you never have to type the syntax at all.

## Overview

The mod system turns Discord into your moderation office. You connect a Discord bot, click one button, and Vyral creates (or reuses) a **private `#vyral-mods` text channel** that only your mod role and the bot can see. Inside that channel, assigned mods type `!mod <action>` commands — and only there, so nothing ever spills into public chat. Each mod holds one of **four tiers** (TrialMod, Mod, SeniorMod, Admin), and the tier decides which actions the bot will run: a TrialMod can kick and purge, an Admin can reassign mod tiers and set balances.

Three principles shape the whole system:

- **Tier-gated** — commands are checked against the caller's stored tier; a lower tier asking for a higher command gets `❌ '<action>' requires <tier> or higher.`
- **Channel-scoped** — `!mod` commands only work in the mod channel; the same text anywhere else is just a normal message.
- **Audited** — every executed action is written to a persistent action log, shown on the Moderator page and in the affected viewer's [profile window](../chat-features/viewer-profiles.md).

The surfaces:

| Surface | What it is | Where |
|---|---|---|
| **Moderator page** | The dashboard: mod channel setup, tier assignments, action log, command builder | Home → Moderator (window title "Vyral - Moderator") |
| **Moderator shortcut chip** | One-click jump to the dashboard | Chat hub header |
| **Settings → Moderator** | Mod channel ID, mod role name, auto-create toggle | Settings |
| **The mod channel** | Private `#vyral-mods` in your Discord server — where `!mod` commands are typed | Discord |
| **Action log** | Last 100 executed actions | Moderator page + viewer profiles |

## User Guide

### Setup, step by step

**1. Connect a Discord bot.** Follow the [Discord setup wizard](../platforms/discord.md) (create the application, save the bot token with the MESSAGE CONTENT intent, invite the bot, connect). The mod system runs on your **primary running bot** — that is the bot that creates the channel, posts into it, and answers commands. A fresh single-bot setup needs no other configuration.

**2. Create the mod channel.** Open **Home → Moderator**. In the **Mod Channel** card:

- **Mod role name** — the Discord role that should be able to see the channel (default `Vyral Mod`). The role must already exist in the server (create it in Discord first, or use any existing role).
- Click **Create Mod Channel**. Vyral asks Discord for (or creates) a text channel named **`#vyral-mods`** and locks it down: `@everyone` is denied view, and **only the mod role plus the bot** can view, send, and read history. An existing `#vyral-mods` is reused rather than duplicated.
- The channel ID is saved automatically (both to the app's config and to the bot's own row), and the status line flips to "Mod channel: {id}".

Requirements and failure modes: the bot must be connected and in the server, the role name must match exactly, and the bot needs the **Manage Channels** permission (the wizard's invite grants Administrator, so a standard install already has it). If creation fails, the error is shown on the page — nothing is left half-configured.

**3. Assign mod tiers.** In the **Mod Roles** card on the same page, enter the mod's **Discord user ID** (right-click a user → Copy ID, with Developer Mode enabled), an optional display name, pick the tier, and click assign. Assignments are stored per Discord user and Vyral best-effort grants the matching Discord role of the same name. Admins can also assign tiers from Discord itself with `!mod assignmod` (Admin-only — see the command table).

**4. Optional: Settings → Moderator.** The same channel ID and role name live here, plus an **Auto-create when missing** toggle (default on): when a mod action needs the channel and no channel is configured, Vyral can create it on the spot. Leave the channel ID empty to let the Moderator page own creation.

That's it — your mods open `#vyral-mods` in Discord and type `!mod` to see the usage line.

### The tier ladder

Tiers are cumulative: each tier can run everything the tiers below it can. Tier names are matched case-insensitively.

| Capability | TrialMod | Mod | SeniorMod | Admin |
|---|:-:|:-:|:-:|:-:|
| Kick, timeout, purge | ✅ | ✅ | ✅ | ✅ |
| Unban, mute | — | ✅ | ✅ | ✅ |
| Add / remove currency | — | ✅ | ✅ | ✅ |
| Ban | — | — | ✅ | ✅ |
| Start / cancel giveaway, draw winners | — | — | ✅ | ✅ |
| Start / end poll | — | — | ✅ | ✅ |
| Set balance | — | — | — | ✅ |
| Assign mod tiers (`assignmod`) | — | — | — | ✅ |

Anyone without an assigned tier who types a `!mod` command in the mod channel gets `❌ You are not a moderator.` Assignments persist in the per-profile database (`ModRoleAssignments`); removing an assignment on the Moderator page takes the powers away immediately.

### Command reference

All commands are typed in the mod channel, prefixed with `!mod`. Placeholders: `<user>` = a username or `@mention`, `<id>` = a Discord user ID, `<amount>` = a positive whole number.

| Command | Minimum tier | What it does |
|---|---|---|
| `!mod` | (any mod) | Prints the usage line listing every action. |
| `!mod kick <user> [reason]` | TrialMod | Kicks the user from the server. |
| `!mod timeout <user> <minutes> [reason]` | TrialMod | Times the user out (Discord's native timeout). |
| `!mod purge <1-100>` | TrialMod | Bulk-deletes that many recent messages in the mod channel. |
| `!mod unban <id>` | Mod | Lifts a ban for the user ID. |
| `!mod mute <user> <minutes>` | Mod | Mutes the user for the given minutes. |
| `!mod addcurrency <user> <amount>` | Mod | Adds that many points to the user's balance (on the Discord platform ledger). |
| `!mod removecurrency <user> <amount>` | Mod | Removes points (fails politely on an insufficient balance). |
| `!mod ban <user> [reason]` | SeniorMod | Bans the user from the server. |
| `!mod startgiveaway <name> <winnerCount> <keyword>` | SeniorMod | Creates **and starts** a giveaway; viewers enter with `!<keyword>`. Fails if another giveaway is already active. |
| `!mod cancelgiveaway` | SeniorMod | Cancels the active giveaway. |
| `!mod draw` | SeniorMod | Draws winners for the active giveaway and completes it. |
| `!mod startpoll <question> \| <option 1> \| <option 2> [...]` | SeniorMod | Creates and starts a poll with two or more pipe-separated options. |
| `!mod endpoll` | SeniorMod | Ends the active poll. |
| `!mod setbalance <user> <amount>` | Admin | Sets the user's balance to an exact number (delta applied as an add or remove). |
| `!mod assignmod <user> <TrialMod\|Mod\|SeniorMod\|Admin>` | Admin | Assigns a mod tier to a Discord user. |

Behavior worth knowing:

- **Replies stay in the mod channel**, sent by the same bot that received the command — successes (`✅ …`, including new balances), failures (`❌ …`), and tier rejections alike.
- **Unknown actions** are called out explicitly with the usage line; a bare `!mod` prints usage too. Words that merely *start* with "mod" (`!moderator`, `!modded`) are not mod commands.
- **Every executed action is audited** — mod name, target, action, and the result text land in the `ModActions` log (last 100 on the page, and in the target viewer's profile). Failed lookups that never executed anything are not audited.
- **Economy actions route through the currency engine**, so the amounts show up in the viewer's transaction history and in the per-platform ledger (recorded under Discord).
- Delegated moderation (kick/ban/mute/timeout/purge) reuses the bot's built-in Discord moderation handlers, so limits match them: mute/timeout in minutes, purge up to 100 messages.

### The command builder (in-app)

You never have to type `!mod` syntax from the app. On the Moderator page, the **Command Builder** card composes commands from templates:

1. **Pick a template** — Add currency, Remove currency, Set balance, Start giveaway, Cancel giveaway, Draw winners, Start poll, End poll, Timeout, Kick, Ban, Purge.
2. **Fill the fields that appear** — each template reveals only what it needs: a target user, an amount (relabeled per template: points, minutes, winner count, message count), and a reason where one applies.
3. **Read the live preview** — a read-only line showing the exact command that will be sent, e.g. `!mod addcurrency SameName 500`.
4. **Send** — posts the composed command into the configured mod channel through the primary bot. From the engine's point of view it is identical to a mod typing it, so the same tier gating and auditing apply.
5. **Save as favorite** — stores the composed command for one-click **Run** later; favorites persist per profile and can be deleted from the favorites list. Perfect for the `!mod addcurrency …` rewards you hand out every stream.

The builder needs a connected bot and a configured mod channel — it tells you which is missing if not.

### Where actions show up afterwards

- **Moderator page → Action Log** — the last 100 executed actions (who ran what, on whom, with the result), with a Refresh button.
- **Viewer profiles** — open a viewer's [profile window](../chat-features/viewer-profiles.md) (click their username in chat) and its moderation section lists the actions recorded against them, so "why was this person muted?" is answerable without leaving the app.
- **Discord itself** — the mod channel keeps the full scroll-back of commands and replies, private to your mod role.

### The auto-mod rules (Moderator page)

Below the mod-system cards, the Moderator page also configures the **cross-platform auto-moderator** — the rule engine that watches every chat message (`ChatModerationEngine`). The **Auto-Mod Rules** card is a grid of rule rows (Banned Words, Links, Caps, Emojis, Repeated Message, ASCII Art — each with threshold, action, timeout, and an Enabled switch), uniform exemptions (**Moderators / VIPs / Subscribers** checkboxes; the broadcaster is always exempt), a **Platforms** filter (comma-separated, empty = all), an escalation ladder (strikes → timeout → ban), a **Save auto-mod rules** button, and a **Test** box that runs a message against the live rules without saving anything.

Four side-by-side text areas configure the word-level lists (one entry per line):

| Text area | What it does |
|---|---|
| **Link allowlist** (one domain per line) | Domains that never count as links for the Links rule. |
| **Banned words / phrases** (one per line) | Single words match as **whole words** (case-insensitive, separator-aware boundaries); multi-word entries match as **substrings** anywhere in the message. |
| **Word allowlist** (one per line — never banned) | **Word-level exceptions to the banned-words rule**: a word on this list is never a violation, even when the identical word also appears in the banned list. The engine checks it at the match site — a banned-word hit is dropped when the word is allowlisted (`ChatModerationEngine.cs:263`). It applies to **single whole-word matches only**: it does not rescue a message containing a banned multi-word *phrase*, and it has no effect on the Links rule (domains use the Link allowlist above). |
| **Exempt users** (one per line) | Users the entire rule engine skips. |

The lists persist with the rule set (per profile) and normalize on save (case-insensitive, blanks dropped). Enforcement of a verdict (delete/timeout/ban/warn) then depends on the platform — see the parity table below.

### Manual warnings (`!warn` family)

Alongside the auto-mod rules, mods can add strikes by hand from any connected chat (1.1.0 wave 16):

| Command | Who | Effect |
|---|---|---|
| `!warn <user> [reason]` | Moderator+ | Adds a strike to the viewer **on the same escalation ladder as auto-mod rule violations** — the same decay window and the same timeout/ban rungs. A warn that itself tips the viewer onto the Timeout/Ban rung fires that action immediately through the normal action pipeline (executed, degraded and audited exactly like a rule violation). |
| `!warnings <user>` | Moderator+ | Shows the viewer's live strike count, the next rung, and the ladder config. |
| `!clearwarn <user>` | Moderator+ | Resets the viewer's strikes to zero (audited). |

Manual warns work even while auto-mod is master-switched off — a warn is a moderator decision, not a detection — and every warn/clear is audited to the same `ModActions` log the page shows. **Restarting the app clears all strike counts** (in-memory state; the audit rows survive) — [KI-64](../reference/known-issues.md). Full syntax in the [Command Catalog → Warning commands](../reference/command-catalog.md#warning-commands).

## How It Works (Developer)

- **`ModTiers`** (`VyralBot.Core/Services/Moderation/ModTiers.cs`) is the static, single source of truth for the ladder: four cumulative arrays (TrialMod → Mod → SeniorMod → Admin) compiled into a command→minimum-rank map. `CanRun(tier, command)`, `MinimumTier(command)`, `Rank`, and `NormalizeTier` are the only gate logic; tier strings are compared case-insensitively.
- **`ModCommandEngine`** (`ModCommandEngine.cs`) subscribes to `DiscordChatMessageEvent` through a Core-side seam (`DiscordChatSubscriber`) — Core cannot reference the Discord integration, so the DI composition in `App.xaml.cs` wires the subscription, the bot resolver, and the moderation invoker to the real Discord types. The engine filters to the configured mod channel (`Mod.ChannelId` config, falling back to the primary bot's `ModChannelId` column), parses `!mod <action>`, resolves the caller's tier via `IModRoleService`, enforces the ladder, executes, and audits through `IDataService.SaveModActionAsync`. Economy actions call `ICurrencyService` directly; giveaway/poll actions call `GiveawayService`/`PollService`; the eight Discord moderation verbs delegate to the bot's built-in moderation command handler (`timeout` is an alias for its `mute`).
- **`ModRoleService`** persists one `ModRoleAssignment` per Discord user (unique user ID, canonical tier) and best-effort grants a Discord role named after the tier — a Discord-side failure is logged and swallowed so the stored assignment always lands.
- **Channel creation** lives on the Discord service (`CreateModChannelAsync(modRoleName)`): resolve the role first (so a typo never leaves a bot-only invisible channel behind), reuse an existing `#vyral-mods`, otherwise create it with permission overwrites denying `@everyone` and allowing only the mod role and the bot. The resulting snowflake is persisted to `Mod.ChannelId` and the bot's `DiscordBots.ModChannelId` column.
- **The Moderator page** (`ModeratorViewModel` + `ModeratorView`) is navigation-scoped: MainViewModel builds it fresh on each visit and disposes it on leave. Its builder composes the preview string from `ModCommandTemplate` records and persists favorites as JSON in the `Mod.FavoriteCommands` config key.

### Auto-mod platform parity

The **auto-moderator** (`ModerationActionService`, `VyralBot.Core/Services/Moderation/`) executes rule-engine verdicts (from the rules card above) against per-platform adapters with graceful degradation: a platform without a needed API falls back through timeout → delete → warn, and a platform with no adapter at all is still logged. Since the Twitch Helix work (2026-11-07) and the Kick/YouTube work (Wave 9, same day), enforcement is native on four of the five platforms. The Moderator dashboard renders this parity card live from `ModerationPlatformParity.Snapshot` (audit table merged with actual adapter registration), so what you see is what the build can do:

| Platform | Enforcement level | What actually happens |
|---|---|---|
| **Twitch** | **Full** | `TwitchModerationAdapter` enforces ban / timeout / delete natively via Helix (`POST /moderation/bans`, `DELETE /moderation/chat`). The chat pipeline does not parse IRC message ids yet, so a Delete without a message id degrades to a **1-second timeout** — a timeout purges the sender's messages, which still removes the offending line. |
| **Discord** | **Full** | `DiscordModerationAdapter` enforces **ban + timeout** by username (timeouts are minute-granular, rounded up, minimum 1). No delete-by-id/by-author API is exposed, so Delete verdicts degrade to a warning notice in the selected channel. |
| **Kick** | **Full** | `KickModerationAdapter` enforces **ban / timeout / delete** natively via Kick's public API v1 (`POST /v1/moderation/bans`, `DELETE /v1/channels/chat/messages/{id}`), id-based through the broadcaster's token. Ban/timeout need the `moderation:ban` scope (requested since launch); message delete needs `moderation:chat_message:manage` — if your Kick connection predates that scope, re-run the Kick setup once and deletes work. The moderation webhooks ([Web Server & ngrok](../settings/web-server-ngrok.md)) remain inbound notifications. |
| **YouTube** | **Full (delete)** | `YouTubeModerationAdapter` **deletes offending messages** natively via `liveChatMessages.delete` (covered by the `youtube.force-ssl` scope the setup already requests). The public YouTube Data API has **no chat ban/timeout endpoint** — `liveChatModerators.insert` appoints moderators, it does not ban — so Ban/Timeout verdicts degrade to a delete, then to a warning notice. |
| **TikTok** | **None** | No chat service integration exists (web setup only) — auto-mod verdicts are logged only. |

This table is the code's own audit-truth (`ModerationPlatformParity.Table` in ModerationActionService.cs); the dashboard card colors the level badge green (Full) / amber (Notice-only) / red (None). The degradation edges are tracked in the [known issues ledger](../reference/known-issues.md) — Twitch's delete-to-purge-timeout fallback is KI-51, Discord's delete-to-notice fallback is KI-53; Kick/YouTube were upgraded from KI-53's notice-only set in Wave 9 (Kick delete's one-time re-auth for pre-Wave-9 tokens remains a live edge).

## Data & Settings

Per-profile config keys (see [Data Layer](data-layer.md)):

| Key | Default | Meaning |
|---|---|---|
| `Mod.ChannelId` | (empty) | Snowflake ID of the mod channel; empty means "not created yet". |
| `Mod.ModRoleName` | `Vyral Mod` | Discord role the mod channel is shared with. |
| `Mod.AutoCreateChannel` | `true` | Create the channel automatically when a mod action needs it and none is configured. |
| `Mod.FavoriteCommands` | (empty) | JSON list of the builder's saved favorite commands. |

Per-profile tables:

| Table | Holds |
|---|---|
| `ModRoleAssignments` | One row per assigned mod: Discord user ID, username, tier, assigned-by, date. |
| `ModActions` | The audit log: mod username/ID, target username, action, result details, timestamp. |

## Status & Known Issues

- **Works**: channel creation/reuse with the private permission overwrites, tier assignment (dashboard and `!mod assignmod`), the full tier ladder with case-insensitive matching, all economy/giveaway/poll/delegated actions, in-channel replies through the receiving bot, full auditing (page + viewer profiles), the command builder with favorites, and the Chat Hub shortcut chip.
- **Single server** — the mod channel is created in the bot's first server; multi-guild mod channels are future work.
- **Purge is scoped to the mod channel** — the delegated purge bulk-deletes in the channel the command was issued in, which is always the mod channel.
- **The builder's "Draw winners" template** composes a `drawwinners` verb; the chat engine's draw action is `draw` — until the two spellings meet, draw winners from chat (`!mod draw`) or the giveaway page.
- **Economy actions record under Discord** — `addcurrency`/`removecurrency`/`setbalance` applied from the mod channel write to the target's Discord-platform balance; for a [linked](../chat-features/account-linking.md) viewer the unified pool makes that the shared spendable balance.
- **Assignments are per Discord user**, not per platform account — a mod's tier follows their Discord ID even if they rename.
