# TikTok

> TikTok is a first-class platform **identity** across Vyral's cross-platform features — linking, the multi-platform bonus, goals, and giveaway platform lists — but there is **no live TikTok chat integration yet**: the setup wizard is still a stub, so no TikTok messages arrive and nothing can be sent to TikTok.

## Overview

TikTok shows up in Vyral in two different ways, and the distinction matters:

1. **As a platform identity** — fully supported today. The string `"TikTok"` is one of the seven platform names the whole cross-platform stack understands: [account linking](../chat-features/account-linking.md) can carry a TikTok identity (`!delink tiktok` validates the name), the [multi-platform bonus](../stream-tools/vyral-goals.md#the-multi-platform-bonus) counts it, [Vyral Goals](../stream-tools/vyral-goals.md) evaluate against it, and a giveaway's Allowed Platforms list accepts it.
2. **As a connected chat platform** — not yet. The wizard looks real but is a UI shell with commented-out OAuth; finishing it writes an honest `Status="Coming Soon"` row and nothing treats that as connected. The full stub anatomy lives in [TikTok & OBS](tiktok-discord-obs.md).

In practice this means a viewer **cannot yet generate or redeem a link code from TikTok** (there is no TikTok chat to type `!link` into) — but a `LinkedAccounts` row with `Platform = "TikTok"` is already a complete, first-class citizen everywhere else. The moment a TikTok chat integration lands, existing identities start counting toward bonuses and goals with zero migration.

## User Guide

### What you can do with TikTok today

- **Nothing on the connection side.** Skip the wizard — it is on the [Platforms](overview.md) page, all four steps are cosmetic, and the result is a "Coming Soon" row you would only disconnect again. Details in [TikTok & OBS](tiktok-discord-obs.md).
- **Plan around the identity.** If your community runs a TikTok account alongside Twitch/Kick, the cross-platform features are already built to include it as soon as chat arrives: a viewer who will link TikTok is one `!link <code>` away from pushing their [bonus ladder](../stream-tools/vyral-goals.md#the-multi-platform-bonus) up a rung (e.g. 2 → 3 linked platforms moves the multiplier from 1.25x to 1.5x).

### Where the TikTok identity already appears

| Feature | How TikTok participates |
|---|---|
| Account linking | `!delink tiktok` recognizes the name; `LinkedAccounts` rows can hold `"TikTok"`; [platform matrix](../chat-features/account-linking.md#platform-support-matrix). |
| Multi-platform bonus | Every linked `"TikTok"` identity raises the viewer's linked-platform count and multiplier. |
| Vyral Goals | `LinkedPlatforms`/`FollowPlatforms`/`SubAnyPlatform` goal aggregates count TikTok rows. |
| Giveaways | `Allowed Platforms` accepts `TikTok` in the comma list — entries from a TikTok integration would validate today. |
| Chat routing / events | Not yet — no TikTok service exists to produce messages or follows/subs. |

## How It Works (Developer)

- **The platform-name plumbing is done.** `CurrencyCommandService.NormalizePlatformName` maps `tiktok → "TikTok"` alongside the other six names; `LinkCodeService`'s per-platform enable keys (`Linking.Platforms.TikTok`) and every goal/bonus aggregate treat the string like any other platform. Per the global architecture rule, platform identities are plain strings — `"TikTok"`, never an enum.
- **The missing half is the integration service.** There is no TikTok project under `VyralBot.Integrations` — no OAuth service, no chat service, no events. The stub wizard (`TikTokWebSetupViewModel`) is documented line-by-line in [TikTok & OBS](tiktok-discord-obs.md#the-tiktok-shell); the [Discord](discord.md) rewrite is the template for wiring a non-OAuth-style platform end to end.
- **The `TiktokChatEnabled` routing flag already exists** on the `Platform` row and in chat settings — it persists but has nothing to route today.

## Files & Code

| File | Role |
|---|---|
| [TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) | The stub wizard (writes an honest "Coming Soon" row). |
| [TikTokWebSetupView.xaml](../../src/VyralBot.UI/Views/TikTokWebSetupView.xaml) | The wizard UI — fully built, cosmetically real. |
| [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) | `NormalizePlatformName` — `tiktok` → `"TikTok"` among the seven supported identities. |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Platform row rendering; a "Coming Soon" TikTok row gets no connection UI. |

## Status & Known Issues

- **No TikTok chat/events integration exists** — implementing it means a new OAuth + chat service under `VyralBot.Integrations`, DI registration in [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs), and pointing the wizard at them (KI-56 in the [known issues ledger](../reference/known-issues.md)).
- **The wizard writes honest "Coming Soon" state** (fixed 2026-08-15) — nothing treats the row as connected; see [TikTok & OBS](tiktok-discord-obs.md).
- **Identity-level support is complete** — linking, bonuses, goals, and platform lists all accept `"TikTok"` today; only chat I/O is missing.
