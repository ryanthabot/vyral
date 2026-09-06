# Account Linking

> Cross-platform viewer identities: a viewer proves they own accounts on two or more platforms with a 6-digit code, and every linked platform counts toward the multi-platform earnings bonus, giveaway odds boosts, and Vyral Goals.

## Overview

By default Vyral's economy treats every (username, platform) pair as a separate viewer — `SameName` on Twitch and `SameName` on Kick are two wallets. Account linking collapses those into one viewer identity: a single internal viewer ID shared by `LinkedAccount` rows, one per platform the viewer has proven. A link group holds **at most one account per platform** — redeeming a code that would add a second account on a platform the group already covers is rejected (see [The one-account-per-platform rule](#the-one-account-per-platform-rule)).

The flow is deliberately simple:

1. On one platform the viewer asks for a **link code** — by typing `!link` in chat, or by DMing `link` to your [linking bot](#choosing-your-linking-bot).
2. On **another** platform they redeem that code — `!link <code>` in chat, `!linkcode <code>`, or by DMing the bare code to your linking bot.
3. Both accounts now resolve to the same viewer identity, and both show up in `!linked` and `!mystatus`.

Linking is enabled by default and gated by the `Linking.Enabled` config (an explicit `false` turns every `!link`-family command into a "🔗 Linking is currently disabled." reply).

Seven platform identities are recognized end to end: **Twitch, Kick, YouTube, Discord, TikTok, Facebook, and Rumble**. See the [platform support matrix](#platform-support-matrix) for what "recognized" means for the platforms without a live chat integration yet.

## User Guide

### Choosing your linking bot

Discord is the only platform with the private DM flow, and **one specific bot** does the answering: the bot holding the **Account linking bot** role.

- **Only this bot answers link and delink DMs** (`link`, `!link`, `connect`, and bare codes) — DMs sent to any other Vyral bot are not picked up by the link flow. The reply is always sent by the **same bot that received the DM**: the account the viewer messaged is the account that answers, including when that bot is the wizard-connected setup bot.
- Assign the role on that bot's page in the [Discord section](../platforms/discord.md#bot-roles), or with the **Account linking Discord bot** selector in **Settings → Viewer Hub → Account Linking** — both set the same thing.
- The role is **single-select**: assigning it to one bot removes it from every other bot.
- When no bot is assigned, the DM flow falls back to the **Primary** bot — a single-bot setup needs zero configuration.

### Generating a code

| Where | What the viewer does | Where the code lands |
|---|---|---|
| Any connected chat (Twitch, Kick, YouTube, Discord channel) | Type `!link` | Chat reply: `🔗 Your link code: 123456 (expires in 5 min). Send it from another platform: type '!link 123456' in that platform's chat, or DM it to the Discord bot.` |
| Discord DM to your [linking bot](#choosing-your-linking-bot) | Send `link`, `!link`, or `connect` | **In the DM** — the reply never goes to a channel, and it comes from the same bot that received yours |

Codes are 6 digits, **single-use**, and expire after **5 minutes** (`Linking.CodeExpiryMinutes`). Requesting a fresh code invalidates the viewer's previous outstanding code.

> **Chat visibility:** a code requested via `!link` in chat is visible to everyone in that chat. That is acceptable because the code is worthless once used or expired — but viewers who want privacy should use the Discord DM flow, where the code goes to their DMs only.

### Redeeming a code (on a *different* platform)

Any one of these completes the link:

- `!link 123456` — in the other platform's chat,
- `!linkcode 123456` — same thing, more explicit,
- DM `123456` (the bare code) to your [linking bot](#choosing-your-linking-bot) — answered in-DM, by the bot that received it.

An account that is already inside the code's link group (including the code's creator re-typing their own code) gets a friendly no-op — `You're already part of this link group.` — nothing merges and nothing changes. A different account on the same platform can redeem the code, which is the whole point: the code moves between accounts, not just between platforms. On a fresh redeem the viewer gets a confirmation reply and all linked platforms immediately count toward [Vyral Goals](../stream-tools/vyral-goals.md), the [multi-platform earnings bonus](../stream-tools/currency.md#multi-platform-bonus), and giveaway [odds boosts](../stream-tools/giveaways.md#odds-boosts).

Expired, already-used, or unknown codes get an explanatory error reply. The redeem is also refused when it would break the one-account-per-platform rule — see [below](#the-one-account-per-platform-rule).

### The one-account-per-platform rule

A link group holds **one account per platform, no more**. Two guards enforce this at redeem time:

- **Target-group guard** — if the group the code belongs to already has an account on the redeemer's platform, the redeem is rejected with `Your link group already has a {platform} account ({name}). Remove it first with !delink.` — the `{platform}` and `{name}` slots filled with the conflicting account. The fix is exactly what the message says: run the [`!delink` flow](#removing-an-account-the-delink-flow) on that platform first, then redeem again.
- **Merge-conflict guard** — if the redeemer has their *own* identity holding an account on a platform the target group already covers (a platform other than the one being redeemed), the redeem is rejected with `Both link groups have a {platform} account — remove one with !delink first.` Merging two groups is only allowed while no platform would end up doubled.

This keeps the "one wallet per platform" model clean: every bonus ladder, goal check, and giveaway odds boost reads the linked-platform *count*, and a doubled platform would silently distort all of it.

### Removing an account (the `!delink` flow)

Removal is deliberately two-sided, so nobody can yank someone else's account out of a group with a single command:

1. **Authorize** — from any linked account that is **not** the one being removed (DMing your [linking bot](#choosing-your-linking-bot) keeps it private — the delink-code reply comes from the same bot you messaged): type `!delink <platform>` (e.g. `!delink twitch`). The reply is a one-time **delink code** plus `Now type '!delink {code}' on {platform} within {n} minutes.` Like link codes, delink codes are 6 digits, single-use, and expire after 5 minutes (`Linking.CodeExpiryMinutes`).
2. **Confirm** — **on the platform being removed**, type `!delink <code>`. The account is removed from the group and the replier sees either `✅ {platform} account removed from your link group. {N} platform(s) remain: {names}` or — when nothing is linked anymore — `Your accounts are no longer linked together.` Every connected platform sees the refreshed list (the same event that drives `!linked` is republished).

Typing `!delink` with no arguments prints the usage help: `From one linked account: !delink <platform> — then on the account to remove: !delink <code>`.

Two guards protect the authorizer:

- You **cannot authorize removal of the account you are typing from** — `!delink twitch` typed *on* Twitch answers `You can't authorize removal of the account you're typing from`. This is what makes the flow two-sided.
- An identity with only a single linked account cannot start a removal at all — it gets `This is your only linked account — ask the streamer to remove it`. If a viewer wants out of linking entirely and has no second account to authorize from, the streamer does it for them (see below).

Valid platform names for `!delink`: `twitch`, `kick`, `youtube`, `discord`, `tiktok`, `facebook`, `rumble` (case-insensitive; normalized internally to `"Twitch"`, `"Kick"`, etc.). Requesting a delink for a platform the group does not cover answers `No {platform} account is linked to your group`.

### Banned users (the streamer override)

The `!delink` flow assumes a cooperative viewer — a banned user will not run it. For that case the streamer removes accounts directly in the app:

- **Settings → Viewer Hub → Linked Viewers** — every linked account row carries its own **Unlink** button (the whole group is listed account-by-account, not just per platform group).
- **Click the username in chat** — chat usernames are clickable and open the full [viewer profile window](viewer-profiles.md), which lists the same per-account **Unlink** buttons.

Streamer-side unlink takes effect immediately and does not require a delink code.

### Commands

All commands below run through the [command pipeline](commands.md) and respect the `Linking.Enabled` master gate:

| Command | Who | Effect |
|---|---|---|
| `!link` | Everyone | Generates a 6-digit code (5-minute expiry, single-use) and posts it with instructions. |
| `!link <code>` | Everyone | Redeems a code generated on another platform and links the accounts. |
| `!linkcode <code>` | Everyone | Alias for `!link <code>`. |
| `!linked` | Everyone | Lists linked platforms and the count — `🔗 Linked platforms: Kick, Twitch (2)` — or "No linked accounts yet — type !link to start". |
| `!delink` | Everyone | Usage help: `From one linked account: !delink <platform> — then on the account to remove: !delink <code>` |
| `!delink <platform>` | Everyone | From an account that stays: authorizes removal of that platform's account and posts a one-time delink code (5-minute expiry). |
| `!delink <code>` | Everyone | On the account being removed: redeems the delink code and leaves the link group. |
| `!unlink` | Everyone | Retired — replies only with a pointer: `⚠️ !unlink was replaced by !delink. From a linked account: !delink <platform>, then on the account to remove: !delink <code>.` |

Valid platform names for `!delink <platform>`: `twitch`, `kick`, `youtube`, `discord`, `tiktok`, `facebook`, `rumble` (case-insensitive; normalized internally to `"Twitch"`, `"Kick"`, etc.) — and only ones the caller's group actually covers (see [the `!delink` flow](#removing-an-account-the-delink-flow)). Banned or uncooperative users are handled by the streamer via [Viewer Hub](viewer-profiles.md#unlinking-a-banned-user) / the viewer profile window instead.

### Platform support matrix

"Supported" has two senses, and it pays to keep them straight:

| Platform | Live chat/events | Link identity | DM link flow | Notes |
|---|---|---|---|---|
| Twitch | Yes | Yes | n/a (no DM intake) | Fully usable for linking today. |
| Kick | Yes | Yes | n/a | Fully usable today. |
| YouTube | Yes | Yes | n/a | Fully usable today. |
| Discord | Yes | Yes | **Yes** | The only platform with the in-DM flow — codes stay private, the DM intake is answered by your [linking bot](#choosing-your-linking-bot), and the reply always comes from the bot that received the DM. |
| TikTok | Not yet ([stub wizard](../platforms/tiktok.md)) | **Yes** | n/a | Counts once linked from other platforms; see [TikTok](../platforms/tiktok.md). |
| Facebook | Not yet | **Yes** | n/a | Recognized identity — see [Facebook](../platforms/facebook.md). |
| Rumble | Not yet | **Yes** | n/a | Recognized identity — see [Rumble](../platforms/rumble.md). |

The practical reading: **linking happens on the platforms you can actually chat on**, and the TikTok/Facebook/Rumble identities become reachable as soon as their chat integrations land — codes and links referencing them are already first-class everywhere in the data model (`!delink rumble` validates against the known platform list, `AllowedPlatforms: "Twitch,Rumble"` in a giveaway validates, the bonus ladder counts Rumble).

## How It Works (Developer)

### LinkCodeService

`VyralBot.Core/Services/Linking/LinkCodeService.cs` is the engine:

- `GenerateCodeAsync(username, platform)` resolves/creates the viewer's `ViewerId`, invalidates any outstanding code for that viewer, then generates a random 6-digit code (100000–999999) unique in the `LinkCodes` table. Expiry comes from the `Linking.CodeExpiryMinutes` config (default **5**).
- `RedeemCodeAsync(code, username, platform)` fails on unknown codes, already-redeemed codes (`RedeemedAt != null`), and expired codes, and rejects `Purpose="Delink"` codes outright (a removal code must go back through `!delink`). When the redeemer's account already sits inside the code's identity it answers an early no-op (`You're already part of this link group.`) without touching data; otherwise the one-per-platform guards run against **every** account of the target group, no exemptions (target-group check and the merge-conflict check — see [the rule](#the-one-account-per-platform-rule)) before the merge writes the new `LinkedAccount` row under the code's `ViewerId` and returns a ready-to-send chat message.
- `GenerateDelinkCodeAsync(username, platform, targetPlatform)` runs the authorizer guards (can't remove the account you're typing from; group must cover `targetPlatform`; single-account identities must ask the streamer) and writes a `LinkCodes` row with `Purpose="Delink"` and `TargetPlatform=targetPlatform`. Any prior unredeemed delink code for the same viewer+target is invalidated first.
- `RedeemDelinkCodeAsync(code, username, platform)` validates the code is a `Purpose="Delink"` code, that the redeemer's `ViewerId` matches the code's, and that they are typing from `code.TargetPlatform` — then deletes that `LinkedAccount` row, detaches the matching `ViewerPlatformStatus` row (its `ViewerId` is nulled so the account drops out of aggregates), marks the code redeemed, and republishes `AccountsLinkedEvent` with the remaining platform names so UI and `!linked` refresh.
- Per-platform kill switches: `Linking.Platforms.{platform}` config keys (checked by `LinkCodeService`); generating from a disabled platform throws `InvalidOperationException`, which the command layer surfaces as the error reply.

### Command routing

The `!link`, `!linkcode`, `!linked`, and `!delink` handlers live in `CurrencyCommandService` so linking commands sit alongside the rest of the economy commands. Every handler starts with the shared `IsLinkingEnabledAsync` gate (`Linking.Enabled`, default true); `!delink` routes its three argument shapes (none / platform name / 6-digit code) exactly as described in [the removal flow](#removing-an-account-the-delink-flow).

### The Discord DM intake

`ChatViewModel` watches [Discord chat events](../architecture/event-bus.md) for DMs and runs a dedicated link-intake path: `IsDiscordLinkIntakeMessage` matches the bare words `link`, `!link`, `connect`, **or a bare 6-digit code**, and `HandleDiscordDmLinkIntakeAsync` generates or redeems and replies **in the DM channel** (never to a server channel) — and the reply is sent through the same bot instance that received the DM, including the wizard-connected setup bot, so the answering account always matches the account the viewer messaged. The intake itself is role-scoped: only DMs received by the bot holding the **Account linking bot** role enter this path (the **Primary** bot when the role is unset — see [Choosing your linking bot](#choosing-your-linking-bot)), so DMs to other Vyral bots get no link reply. Anything else — `!points`, conversation — flows through the normal Discord command pipelines untouched.

### Why the ViewerId matters

Every cross-platform feature keys off the viewer ID, not the username pair: `ViewerStatusService` aggregates follow/sub/tier state across linked rows, `MultiPlatformBonusService` resolves the earnings ladder from the linked-platform count, `GiveawayService` looks up best-tier/follower/linked-count at entry time for odds boosts, and `SocialGoalService` evaluates [Vyral Goals](../stream-tools/vyral-goals.md) against the aggregates. Link two platforms and all of those light up at once.

## Files & Code

| File | Role |
|---|---|
| [LinkCodeService.cs](../../src/VyralBot.Core/Services/Linking/LinkCodeService.cs) | Code generation/redeem/unlink (link + delink codes); expiry, per-platform enable checks, and the one-per-platform guards. |
| [ILinkCodeService.cs](../../src/VyralBot.Core/Services/Linking/ILinkCodeService.cs) | Interface, including the `LinkRedeemResult` with its ready-to-send message. |
| [LinkedAccountService.cs](../../src/VyralBot.Core/Services/Currency/LinkedAccountService.cs) | The older `LinkedAccount`/`ViewerId` service the link flow is built on. |
| [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) | `!link` family handlers and the `Linking.Enabled` gate. |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Discord DM link intake (matcher + in-DM handler). |
| [LinkedAccount.cs](../../src/VyralBot.Data/Entities/LinkedAccount.cs) / [LinkCode.cs](../../src/VyralBot.Data/Entities/LinkCode.cs) | The identity rows and the outstanding-code rows. |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `LinkedAccounts` and `LinkCodes` tables (`DapperDataService.cs:3183` / `:3199`). |

## Data & Settings

Config keys (via `GetConfigValueAsync` — see [Data Layer](../architecture/data-layer.md)):

| Key | Default | Meaning |
|---|---|---|
| `Linking.Enabled` | `true` | Master gate for every `!link`-family command and the DM intake. |
| `Linking.CodeExpiryMinutes` | `5` | Link-code lifetime in minutes. |
| `Linking.Platforms.{platform}` | (enabled) | Per-platform enable switch for code generation. |

Tables: **LinkedAccounts** (one row per linked platform under a `ViewerId`) and **LinkCodes** (outstanding codes with `ExpiresAt`/`RedeemedAt`; delink codes add `Purpose="Delink"` and `TargetPlatform`).

## Status & Known Issues

- **Codes generated in chat are public** — the `!link` reply necessarily lands in the channel. Fine in practice (single-use, 5-minute expiry); privacy-conscious viewers should use the Discord DM flow.
- **The initiation quirk is gone** — the current `LinkCodeService` flow resolves both accounts through the code itself; the old same-username assumption documented in [Currency & Rewards](currency.md) no longer applies to the `!link`-family flow.
- **`!unlink` is retired, replaced by `!delink`** — typing it no longer unlinks anything; it answers only a pointer to the two-sided `!delink` flow (authorize from one account, confirm from the account being removed) so nobody can unilaterally strip another account out of a group. The one-shot streamer path is the Viewer Hub / viewer-profile unlink buttons ([Viewer Profiles](viewer-profiles.md)).
- **TikTok/Facebook/Rumble cannot generate or redeem codes yet** — there is no chat connection to type into. Their identities are otherwise fully supported (delink, platform lists, bonus ladder, goals).
- Linking state is per-profile — codes and links live in the profile's SQLite database and do not follow a profile switch.
