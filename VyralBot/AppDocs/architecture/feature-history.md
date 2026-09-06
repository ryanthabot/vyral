# Feature History

> A plain-language tour of everything VyralBot has shipped, in the order the work happened — what each feature does for you, why it was built that way, and where to read more. This page condenses the project's internal design documents into one user-facing story; developers can still find the full specs and plans in the repository under `docs/superpowers/`.

## Overview

VyralBot grew from a single-platform chat bot into a cross-platform viewer economy with unified viewer identities, a multi-platform points system, live giveaways, a built-in DM office, and a full moderation team system. This page summarizes each wave of that work: what problem it solved, what you get, and which page documents it in depth.

The waves, in order:

1. **Foundations** — theming, live performance monitoring, and per-section Settings.
2. **Cross-platform viewer identity** — account linking, multi-platform rewards, giveaway odds, Vyral Goals, and the giveaway rework.
3. **Linking hardening + viewer profiles** — exploit-proof link removal and the click-a-username profile window.
4. **Discord bot roles** — which bot does which job, with auto-detected identity.
5. **Watch-time and chat-activity gating** — points that match where the stream actually is.
6. **Unified balances** — one spendable pool with a per-platform ledger.
7. **The DM inbox** — persistent Discord direct messages managed from the app.
8. **The mod system** — a private mod channel, four mod tiers, and tier-gated `!mod` commands.

## Foundations: themes, performance, and Settings

Three early waves rounded out the app itself rather than any single platform:

- **Theming** — Light and Dark base modes, a set of built-in color themes, a Custom theme builder with a full color picker, per-theme logo color variants, and brightness sliders. Everything recolors live, no restart; the choice is stored app-level (not per-profile). See [Settings Overview](../settings/overview.md).
- **Live Performance Monitor** — Settings → Performance shows CPU, memory, GC, and thread-pool numbers sampled every second, plus per-platform and per-component activity (event bus, workflows, web server) and child processes (WebView2, ngrok), with sparkline history. It is display-only and costs almost nothing when the tab is closed.
- **Per-section Settings** — every Settings category got its real content: startup auto-connect toggles per platform, storage locations and cleanup tools under Data Management, notification master toggles, command prefixes, timer and automation gates, security info, and cache/compact tools under Advanced. No "Coming Soon" panels remain.

## Cross-platform viewer identity

The biggest arc: viewers watch from more than one platform, but the bot used to treat each (username, platform) pair as a stranger. The identity wave fixed that end to end:

- **Account linking by one-time code.** A viewer types `!link` in chat (or DMs `link` to your Discord bot) and receives a 6-digit code valid 5 minutes. Typing `!link <code>` on another platform merges both accounts into one viewer identity. Codes are persistent database rows, single-use, and collision-checked. See [Account Linking](../chat-features/account-linking.md).
- **Multi-platform point bonuses.** Viewers linked on 2+ platforms earn bonus points on a configurable ladder (1.25x at 2 platforms up to 2.5x at 5+), in three modes: Off (default), OptIn (`!mp on`), or Auto. See [Currency & Rewards](../stream-tools/currency.md#multi-platform-bonus).
- **Giveaway odds boosts.** Per-giveaway extra entries from a viewer's best sub tier across linked platforms (tier 1/2/3), follower-anywhere status, and linked-platform count — with a human-readable breakdown stored on each entry for auditing. See [Giveaways → Odds boosts](../stream-tools/giveaways.md#odds-boosts).
- **Vyral Goals.** A promotion system: viewers complete multi-platform goals (link N platforms, follow on N platforms, sub tier N) and earn points, multipliers, or badges, with chat announcements and a `!goals` progress command. Four goals are seeded out of the box. See [Vyral Goals](../stream-tools/vyral-goals.md).
- **Giveaway rework.** Old giveaways became deletable (with a confirmation; Active ones must be canceled first), the page became live-updating (event-driven reloads, no manual refresh), and entry intake became adaptive: under flood traffic it switches from Live to Timed batched processing and back, announcing the switch in chat. See [Giveaways](../stream-tools/giveaways.md).
- **More platform cards.** TikTok, Facebook Gaming, and Rumble joined the Platforms page and Viewer Hub as recognized identities (their live chat integrations are future work), with per-platform linking switches in Settings.

## Linking hardening + viewer profiles

The first linking pass had two exploitable rough edges — a code visible in public chat could be stolen, and removal was one-sided. The hardening wave closed both:

- **One account per platform per identity, no exceptions.** A redeem that would give a link group two accounts on the same platform is rejected with instructions; merging two groups is only allowed while no platform would end up doubled.
- **Two-sided removal (`!delink`).** Removing an account requires proof from another account in the group plus confirmation from the account being removed: `!delink <platform>` issues a one-time delink code, and `!delink <code>` typed on the platform being removed completes it. A single-account identity cannot remove itself — the streamer does that. The old one-shot `!unlink` now only points at the new flow.
- **Streamer override.** Every linked account row in the Viewer Hub and in the viewer profile window carries its own Unlink button, for banned or uncooperative viewers.
- **Viewer profile window.** Click any username in chat to open a full profile: unified identity with every linked account (drill down into each one, including its platform user ID), live presence indicator, economy totals with a per-platform breakdown, watchtime and streaks, completed goals, giveaway entries, perks, DM history, and — since the mod system — the moderation actions taken against that viewer. See [Viewer Profiles](../chat-features/viewer-profiles.md).

## Discord bot roles + auto-detected identity

With several Discord bots connected, "every bot answers everything" was chaos. This wave assigned jobs and removed manual typing:

- **Bot roles.** Two single-select roles — **Account linking bot** (the only bot that answers link/delink DMs) and **Giveaway announcements bot** (posts Discord giveaway announcements). Unset roles fall back to the Primary bot, so a single-bot setup needs zero configuration. Assign roles on each bot's page or via the Viewer Hub selector. See [Discord → Bot roles](../platforms/discord.md#bot-roles).
- **Auto-detected identity.** Application ID, bot username, and avatar are read from Discord at connect time and shown read-only — the token is the only thing you ever type.
- **The DM inbox.** Discord direct messages moved out of the main feed into their own inbox: one row per viewer with unread badges, threads you can reply to from inside the app (replies go through the bot that received the DM), persistent unlimited history you can load on demand (per conversation or all at once), per-thread Load/Clear controls, a **New DM** button that starts a conversation from a raw Discord user ID, and a Combined view showing chat and DMs side by side. The unified reply box switches into DM mode automatically, with its own gradient send button. See [Chat Window → The DM inbox](../chat/chat-window.md#the-dm-inbox-discord-direct-messages).

## Points that match the stream (watch-time gating)

Earning used to tick regardless of whether anything was happening. The gating wave tied points to reality:

- **Watch-time points only while a platform is live.** Stream platforms accrue watch-time (and its points) only while actually live; Discord viewers never earn watch-time points at all, since Discord has no live concept.
- **Discord chat-activity points only in assigned channels.** Discord messages earn chat-activity points only in channels the streamer assigned on each bot — so a bot sitting in a busy foreign server no longer mints points. See [Currency & Rewards](../stream-tools/currency.md).

## Unified balances + the per-platform ledger

Unified balances became the default for linked viewers: all earning and spending routes to **one canonical account**, so the balance is a single spendable pool that works from any linked platform. Because that would otherwise destroy per-platform history, every movement also appends to a **per-platform earnings ledger** (earned/spent per platform, tagged with the platform where the points were actually earned or spent). The viewer profile window shows both: the unified pool and the per-platform breakdown. See [Currency & Rewards](../stream-tools/currency.md).

## The mod system

The newest wave gives your moderation team a private office inside Discord and tier-gated powers:

- **Auto-created private channel.** One click creates (or reuses) a private `#vyral-mods` text channel visible only to your mod role and the bot — that channel is the only place `!mod` commands work, so nothing spills into public chat.
- **Four tiers.** TrialMod, Mod, SeniorMod, and Admin, each inheriting every lower tier's commands, from kick/timeout/purge up through currency edits, giveaway and poll control, ban, and the Admin-only balance setter and mod assignment. Assignments are stored per Discord user and best-effort mirrored to a real Discord role.
- **Tier-gated `!mod` commands.** `!mod <action>` in the mod channel runs the action — moderation (kick, timeout, purge, mute, unban, ban), economy (add/remove currency, set balance), and show control (start/cancel giveaway, draw winners, start/end poll) — and every executed action is written to an audit log.
- **The Moderator dashboard.** Home → Moderator hosts the mod channel setup, the tier assignment list, the action log, and a **command builder** that composes `!mod` commands from templates, previews them, sends them to the mod channel, and saves favorites for one-click reuse. A Moderator shortcut chip in the Chat Hub jumps straight there, Settings → Moderator holds the channel/role/auto-create settings, and every executed action shows up in the affected viewer's profile. See the [Moderator Guide](moderator-guide.md).

## Where things live

| Feature | In the app | Documentation |
|---|---|---|
| Account linking | Chat commands + Discord DMs | [Account Linking](../chat-features/account-linking.md) |
| Viewer Hub settings | Settings → Viewer Hub | [Settings Overview](../settings/overview.md) |
| Viewer profiles | Click a username in chat | [Viewer Profiles](../chat-features/viewer-profiles.md) |
| Multi-platform bonus & unified balances | automatic + Settings → Viewer Hub | [Currency & Rewards](../stream-tools/currency.md) |
| Giveaways & odds boosts | Stream Tools → Giveaways | [Giveaways](../stream-tools/giveaways.md) |
| Vyral Goals | Settings → Viewer Hub + `!goals` | [Vyral Goals](../stream-tools/vyral-goals.md) |
| DM inbox | Chat window → DMs | [Chat Window](../chat/chat-window.md) |
| Discord bots, roles, mod system | Discord section + Home → Moderator | [Discord](../platforms/discord.md), [Moderator Guide](moderator-guide.md) |
| Theming & Settings | Settings | [Settings Overview](../settings/overview.md) |
| Performance monitor | Settings → Performance | [Settings Overview](../settings/overview.md) |

## Status

Everything described on this page is shipped and working. Internal design documents (specs and implementation plans) remain in the repository's `docs/superpowers/` folder for developers; this page and the rest of the in-app documentation are the user-facing versions and are updated as features land.
