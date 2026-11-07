# Viewer Hub (linking, bonuses, odds, goals)

## Overview
Settings → Viewer Hub panel: link-code settings + live pending-codes monitor,
multi-platform bonus ladder, giveaway odds bonuses, Vyral goal editor and the
linked-viewers browser (with unlink / unlink-all).

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/Views/ViewerHubView.xaml` | View (cards, DataGrids, empty states) |
| `src/VyralBot.UI/ViewModels/ViewerHubViewModel.cs` | ViewModel (singleton, outlives the page) |
| `src/VyralBot.Core/Services/Linking/LinkCodeService.cs` | Link/delink code generation + redemption |
| `src/VyralBot.Core/Services/Linking/SocialGoalService.cs` | Goal seeding, evaluation, rewards |
| `src/VyralBot.Core/Services/Linking/MultiPlatformBonusService.cs` | Bonus ladder reader |
| `src/VyralBot.Core/Services/Currency/LinkedAccountService.cs` | LinkedAccount rows, unlink, identity resolve |
| `src/VyralBot.Core/Services/Currency/CurrencyService.cs` | `ConsolidateViewerBalancesAsync` (unified pool) |

## Chat commands (all platforms routed through CurrencyCommandService)
- `!link` — generate a 6-digit code (crypto-random, unique, single-use, expires per `Linking.CodeExpiryMinutes`, default 5 min; a new code invalidates the viewer's previous unredeemed one)
- `!link <code>` / `!linkcode <code>` — redeem from the OTHER platform's chat
- `!linked` — list the caller's linked platforms
- `!delink <platform>` — generate a removal code; `!delink <code>` — confirm on the account being removed
- `!goals` — show progress; `!mp` — multi-platform opt-in

## Linking semantics (merge + balances)
- Identity = `ViewerId`; one `LinkedAccounts` row per (Username, Platform) —
  `UNIQUE(Username, Platform)`. Engine supports 7 platforms (Twitch, Kick,
  YouTube, Discord, TikTok, Facebook, Rumble); live chat exists for Twitch,
  Kick, YouTube, Discord and TikTok — Facebook/Rumble can be linked only once
  their chat integrations ship.
- Merge target: the CODE CREATOR's ViewerId wins; the redeemer's rows are
  re-pointed to it. Guard: one account per platform per identity (no merging
  two accounts of the same platform); merges that would clash are refused.
- Unified balances (`Linking.UnifiedBalances`, default on): on link, all
  accounts' points are SUMMED onto a canonical row — Discord account if the
  group has one, else the oldest account — and the other rows are zeroed.
  Per-platform `PlatformEarnings` history is never touched.
- Unlink (single, group, or `!delink`): the row is deleted and its
  `ViewerPlatformStatus.ViewerId` is nulled (follow/sub history retained, but
  it stops counting toward the group). Points are NOT split back — the merged
  total stays with the remaining group (no duplication, no double-spend).
- `AccountsLinkedEvent` is published after every link/delink merge.

## Goals
- Types: `LinkedPlatforms`, `FollowPlatforms`, `SubAnyPlatform`, `SubTier`;
  rewards: `Currency` (paid on completion), `Multiplier`/`Badge` (declarative).
- Progress is event-driven: `ViewerStatusUpdatedEvent` (sub/follow recorded)
  AND `AccountsLinkedEvent` (link/delink) both trigger evaluation for the
  viewer identity, so linking 2 platforms completes "Bridge Builder"
  immediately. Completions are idempotent rows (`UNIQUE(GoalId, ViewerId)`).
- `Goals.Enabled` toggle gates evaluation only (goals stay listed).

## Performance notes
- Pending-codes list refreshes on a 30s DispatcherTimer that runs ONLY while
  the ViewerHub section is visible: `SettingsViewModel.OnSelectedCategoryChanged`
  stops/restarts it on category switch, and `SettingsViewModel.Dispose` stops
  it when the Settings page is left (the DI scope disposes the page VM).
- `IX_LinkCodes_Unredeemed` (partial index, `WHERE RedeemedAt IS NULL`) serves
  the 30s poll; `GetUnredeemedLinkCodesByViewerAsync` (IX_LinkCodes_ViewerId)
  serves delink-code invalidation — no recent-N scans on hot paths.
- Expired codes (>24h) are purged once at app startup (`CleanupExpiredAsync`).
- Linked-viewers browser loads on demand only; the search box filters the
  in-memory list and never queries the database.
