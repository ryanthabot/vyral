# Currency & Rewards

> The app's largest subsystem: a full viewer economy with earning streams, a rewards store and approval queue, games, banking, auctions, boosts, loyalty tiers, and linked accounts — live from chat since the command pipeline was wired up, with a Settings tab that persists (one subset caveat).

## Overview

Currency & Rewards gives viewers a loyalty currency (default: "Points" with a star symbol) they earn by hanging out in chat, watching, and triggering events, and spend on streamer-defined rewards or games. Balances are per (username, platform), every movement is written to a transaction audit trail, and a whole ecosystem of extras sits on top: daily streak bonuses, a bank with interest, auctions, earn-boost multipliers, loyalty tiers from lifetime earnings, and cross-platform account linking.

Despite living under the Tools hub's "Currency & Rewards" card, this feature is reached from the top-level **Currency** navigation item, not the Stream Tools sidebar group.

The current-state picture:

- **Chat earning is live.** Per-message chat-activity earning, `!daily`, games, redemptions, auctions, and every other chat command run through the command pipeline, which is fed by all three chat integrations (KI-1 resolved 2026-08-15).
- **The multiplier stack is applied to earning paths** — platform × boost × tier × role (highest-wins per family, floored) now multiplies event bonuses, watchtime awards, and chat-activity awards.
- **Linked viewers spend from one unified pool.** With [account linking](../chat-features/account-linking.md), all earning and spending routes to a single canonical account, so a viewer's balance is one spendable total that works from any linked platform — while a **per-platform earnings ledger** keeps recording where every point was earned or spent (see [Unified balances & the per-platform ledger](#unified-balances--the-per-platform-ledger)).
- **Earning follows the stream.** Watch-time points accrue only while that platform is actually live (and never on Discord), and Discord chat-activity points are paid only in channels you assigned to your bots (see [Where points can be earned](#where-points-can-be-earned-live--channel-gating)).
- **Watchtime earning is configurable** — `WatchTimeEnabled` / `WatchTimeAmount` / `WatchTimeInterval` are honored. **BEHAVIOR CHANGE:** the defaults award 10 per minute where older builds hard-coded 1 (KI-30 — set Points Per Interval to 1 for the old rate).
- **The Settings tab persists** via the currency service (KI-7 resolved), with one caveat: the data-layer save writes only a subset of `CurrencySetting` fields — name/symbol, watch-time, chat-activity, and the follow/sub/giftsub/raid bonuses. Role multipliers and other columns keep their previous values even after a successful Save (KI-29).

What genuinely works: every earning stream with a trigger, the full game suite (heists self-resolve 60 s after the first join), the bank (interest now writes "Interest" audit rows), auctions, boosts that actually multiply awards, the whole management UI (rewards CRUD, redemption queue, leaderboard, transaction lookup), and the hourly background maintenance loop.

## User Guide

Open **Currency** in the sidebar (top level, below the chat entries) or the "Currency & Rewards" card on the Tools hub. The page has four tabs; a transaction lookup sits under the Leaderboard tab.

### Settings tab

- **Currency Configuration** — the currency **Name** and **Symbol** shown in command responses.
- **Watch Time Rewards** — enable toggle, **Points Per Interval**, and **Interval (seconds)**. This is the live earning path: [Watchtime](watchtime.md) honors these settings (plus multipliers). Default 10 points per 60-second interval — see the KI-30 behavior note.
- **Chat Activity Rewards** — enable toggle, **Points Per Message**, and **Cooldown (seconds)** (per-user minimum time between awards).
- **Event Bonuses** — enable toggles and amounts for **Follow**, **Subscribe**, **Gift Sub** (per sub), and **Raid** (per viewer).

Pressing **Save Settings** persists through `ICurrencyService.SaveSettingsAsync` (with the service cache refreshed) and confirms with "Settings saved successfully!". One caveat: the data-layer save persists only the fields above — cheer/host bonuses, role multipliers, decay, bank, and transfer-tax columns have no UI on this tab and are not written by the save (KI-29); those keep their defaults unless edited directly in SQLite.

### Rewards tab

The streamer's store. The grid lists every reward with name, description, cost, approval flag, cooldown, enabled flag, and redemption count. Buttons:

- **New Reward** — opens the editor with defaults: cost 100, requires approval on, cooldown 0.
- **Edit** / **Save Reward** — change name (required), description, cost (must be > 0), requires-approval, and cooldown seconds. Saving always leaves the reward enabled.
- **Delete** — removes the reward.
- **Toggle Enabled** — enable/disable without deleting.

When a viewer redeems (via `!redeem`), the cost is deducted immediately and the redemption appears in the next tab. Rewards that require approval land as **Pending**; the rest are instantly **Approved**.

### Redemptions tab

The approval queue. Select a redemption and:

- **Approve** — marks it Approved (ready for you to fulfill on stream).
- **Reject** — marks it Rejected **and refunds the cost** to the viewer automatically.
- **Complete** — marks it Completed once you have delivered the reward.

The approver name recorded on processed redemptions is hard-coded to "Admin".

### Leaderboard tab

The top 10 balances with a **Refresh** button, plus the **transaction lookup**: type a username and press **Load Transactions** to see that user's last 100 currency movements (amount, type, reason, balance before/after, timestamp).

### Earning and spending from the viewer's side

Everything viewers do with currency happens through chat commands. The full command surface — about 25 commands — is in the [command catalog](../reference/command-catalog.md); the groups:

| Group | Commands |
|---|---|
| Balance & ranking | `!points` (`!balance`, `!currency`), `!leaderboard` (`!top`), `!rank` (`!tier`) |
| Daily & streaks | `!daily` |
| Rewards store | `!rewards`, `!store` (`!shop`), `!redeem` |
| Games | `!slots`, `!duel` + `!accept`, `!heist`, `!roulette`, `!gamble` (`!bet`) |
| Bank | `!deposit`, `!withdraw`, `!bank` |
| Auctions | `!auction` (moderator), `!bid` |
| Moderation & boosts | `!give`, `!take`, `!boost` (moderator) |
| Account linking | `!link`, `!link <code>` (`!linkcode <code>`), `!linked`, `!delink <platform>` / `!delink <code>` |
| Goals & status | `!goals`, `!mystatus`, `!mp on\|off` |

All of these are live from chat (the command pipeline runs on every connected platform; replies go to Twitch/Kick only). Moderator-gated commands reply with an explicit "You must be a moderator to use !x" message rather than silence.

## How It Works (Developer)

Nine services under `src/VyralBot.Core/Services/Currency/`, all registered as singletons in `App.xaml.cs:117-126` along with the `CurrencyDecayBackgroundService` hosted service. The chat entry point is `CurrencyCommandService.ProcessCommandAsync` (`CurrencyCommandService.cs:43-85`), a switch over the first token that fans out to the sub-services.

### The ledger (CurrencyService)

`CurrencyService` is the core. Balances live in `UserCurrency` rows keyed by (username, platform) with `Balance`, `TotalEarned`, `TotalSpent`, and per-stream timestamps for chat-activity and watch-time cooldowns. Every mutation goes through `AddCurrencyAsync` / `RemoveCurrencyAsync` (`CurrencyService.cs:69-182`), which:

- validate the amount and (for removals) the balance,
- update the row and the lifetime counters,
- write a `CurrencyTransaction` audit row with type, reason, `BalanceBefore`/`BalanceAfter`.

A nominal `_balanceLock` exists but holds nothing — the read-modify-write is not actually serialized. Settings are read through a 5-minute cache (`GetSettingsAsync`, `CurrencyService.cs:25-48`) that self-seeds the database with defaults ("Points" / star symbol) on first read.

### Earning streams

| Stream | Method | Trigger | Live? |
|---|---|---|---|
| Chat activity | `AwardChatActivityAsync` (`CurrencyService.cs:229`) | every non-command chat message, gated by per-user cooldown | **Yes** — called from the live pipeline (`ChatViewModel.cs:807`), with the multiplier stack applied |
| Watch time | configured `WatchTimeAmount` per `WatchTimeInterval` | [Watchtime](watchtime.md) ticker for active viewers | **Yes** — honors `WatchTimeEnabled`/`Amount`/`Interval` and multipliers (KI-30 behavior change: default 10/min vs old 1/min) |
| Event bonuses | `AwardEventBonusAsync` (`CurrencyService.cs:295`) | Kick/Twitch follow, sub, gift-sub, host, raid, cheer event handlers | **Yes** — including the Twitch cheer and Kick host call sites (wired 2026-08-15) |
| Daily bonus + streaks | `ClaimDailyBonusAsync` (`CurrencyService.cs:356`) | `!daily` | **Yes** — via the live command pipeline |
| First-chat bonus | `AwardFirstChatBonusAsync` (`CurrencyService.cs:417`) | first earning event per viewer | Yes, with a semantics caveat — "first currency record", not true first chat (KI-27) |

Event bonus amounts come from `CurrencySetting`: follow 100, subscribe 500, gift-sub 50 per sub (multiplied by the gift count), raid 10 per viewer (multiplied by viewer count), cheer 1 per bit, host/shoutout 200 — each behind its own enable flag. After the base amount is computed, the **multiplier stack** is applied: platform × boost × tier × role (see below), floored with `Math.Floor`.

Daily streaks use the `ViewerStreak` entity: once per UTC day, consecutive days increment the streak, a gap resets it to 1, and the bonus is `DailyBonusAmount` (100) plus a streak add-on — day 1 adds 50, day 5+ adds 100, day 10+ adds 250.

### Multipliers — the stack that now applies

Earning paths apply the full multiplier stack, floored with `Math.Floor`:

- **Platform multipliers** — `TwitchEarnMultiplier` / `KickEarnMultiplier` / `YouTubeEarnMultiplier`.
- **Boost multipliers** — `CurrencyBoostService.GetActiveMultiplierAsync`, highest active boost wins.
- **Loyalty tier multipliers** — `GetTierMultiplierAsync`, from the viewer's lifetime-earnings tier.
- **Role multipliers** — `GetEffectiveMultiplierAsync` (sub 2.0, VIP 1.5, mod 1.5; highest wins, not stacking) when the earning event knows the viewer's role.

The multiplier stack multiplies event bonuses, watchtime awards, and chat-activity awards — and it now includes the **multi-platform bonus** as a final factor (see the section below). Settings for role multipliers are among the fields the Settings-tab save does not persist (KI-29).

### Multi-platform bonus

Viewers who [link platforms](../chat-features/account-linking.md) earn more: the bonus multiplies every earning path by **1.25x at 2 linked platforms, 1.5x at 3, 2.0x at 4, and 2.5x at 5+** (configurable via `MultiPlatform.Bonus2..Bonus5`). Three modes govern it (`MultiPlatform.Mode`, edited under **Settings → Viewer Hub**): **Off** (default — never applies), **OptIn** (only viewers who typed `!mp on`), and **Auto** (every linked viewer). Completed Multiplier-type [Vyral Goals](vyral-goals.md) stack on top. The full walkthrough — ladder table, `!mp`/`!mystatus` behavior, and the goal interplay — lives in [Vyral Goals](vyral-goals.md#the-multi-platform-bonus).

### Unified balances & the per-platform ledger

Before unification, a linked viewer had a separate wallet per platform — points earned on Twitch were unspendable on Kick. Now, when a viewer's accounts are [linked](../chat-features/account-linking.md), **all earning and spending routes to one canonical account row**: the balance is a single spendable pool that works from any linked platform, and the other accounts' rows are zeroed as their balances merge into it. This is on by default (`Linking.UnifiedBalances`, an explicit `false` restores per-platform wallets) and resolves through a short cache, so chat throughput is unaffected.

A single pool alone would silently destroy per-platform history, so every earning or spending movement also appends to the **per-platform earnings ledger**: a running earned/spent total per platform, tagged with the platform where the points were *actually* earned or spent (not the canonical account they landed on). The ledger is atomic at the data layer and can never break an earn or spend — a ledger failure is logged and skipped after the balance is already saved. Unlinked viewers have no viewer ID and no ledger; their single wallet row is the record.

Where this surfaces:

- **The [viewer profile window](../chat-features/viewer-profiles.md)** shows the unified balance *and* the per-platform earned/spent breakdown side by side.
- **[Mod economy commands](../architecture/moderator-guide.md)** (`addcurrency`/`removecurrency`/`setbalance` from the mod channel) adjust the unified pool, so the correction is spendable everywhere the viewer is linked.

### Where points can be earned (live & channel gating)

Two gates keep earning honest, applied to the watch-time and chat-activity streams:

- **Watch-time only accrues while the platform is live.** A viewer idling in a Twitch/Kick/YouTube chat earns watch-time minutes and their points only while that platform is actually broadcasting; the moment the stream ends, accrual stops. **Discord viewers never earn watch-time points** — Discord has no live concept. (If the live-status registry is unavailable, stream platforms fall back to always accruing so nothing silently breaks.)
- **Discord chat-activity points only in assigned channels.** A Discord message earns its per-message points only when the channel it was typed in is one of the streamer's **assigned channels** — the per-bot channel assignments from the [Discord bots pages](../platforms/discord.md#multiple-bots), unioned across bots. A bot sitting in a busy foreign server mints nothing there. Fail-safe: if the assignment list cannot be read, the award goes through (a transient database error should never silently stop earning), and legacy callers that supply no channel id keep the old always-award behavior.

Event bonuses, `!daily`, games, and rewards redemption are not affected by these gates.

### Giveaway odds settings

The per-giveaway follower/sub-tier odds boosts read their bonus sizes from the `Odds.*` config keys (tier 1/2/3 bonuses, follower bonus, per-extra-linked-platform bonus and cap) — defaults and semantics documented in [Giveaways → Odds boosts](giveaways.md#odds-boosts).

### Rewards store and approval queue (CurrencyRewardsService)

`RedeemRewardAsync` (`CurrencyRewardsService.cs:48-106`) first runs `CanRedeemAsync` (`:184-273`), which checks: reward exists and is enabled; the optional availability window (`AvailableFrom`/`AvailableUntil`); wallet balance; the total redemptions cap; the per-user cap (counting non-rejected redemptions); the per-user cooldown; and the optional `RequiredLoyaltyTier` (compared against the user's lifetime `TotalEarned` versus the tier's `MinPoints`). It then deducts the cost, writes a `CurrencyRedemption` row — status `Pending` when the reward requires approval, else `Approved` — and increments the reward's `CurrentRedemptions`. `ApproveRedemptionAsync` / `RejectRedemptionAsync` (refunds the cost as a "Refund" transaction) / `CompleteRedemptionAsync` (`:118-182`) drive the queue. The UI's buttons call these with the hard-coded approver "Admin" (`CurrencyViewModel.cs:320-356`).

### Games (CurrencyGameService)

All games are gated by `CanPlayAsync` (`CurrencyGameService.cs:36-73`): per-game `CurrencyGameSetting` rows (enabled, min/max bet, cooldown seconds — seeded as Slots 10–10,000/30s, Duel 10–5,000/60s, Heist 10–5,000/300s, Roulette 10–10,000/30s) plus a wallet check. Cooldowns are an in-memory dictionary, so they reset on app restart.

- **Slots** (`PlaySlotsAsync`, `:81-132`) — three reels over six symbols; three-of-a-kind pays 10x for 7s, 7x for diamonds, 5x otherwise; any two-of-a-kind pays 2x.
- **Duel** (`InitiateDuelAsync`/`AcceptDuelAsync`, `:134-197`) — a challenge keyed to the opponent expires after 60 seconds; acceptance checks both wallets, then a 50/50 coin flip moves the stake from loser to winner.
- **Heist** (`JoinHeistAsync`/`ResolveHeistAsync`, `:199-288`) — a 60-second join window per platform; entry is deducted on join; resolution rolls each participant against a 30% + 5%-per-participant (max 80%) survival chance and winners split the pot proportionally to their bets. **Heists self-resolve** since 2026-08-15: an internal timer fires 60 s after the first join (race-safe; payouts use the original-case platform name), and `!heist`-less sessions no longer hang. `!auction`-style explicit end (`ResolveHeistAsync`) still works.
- **Roulette** (`PlayRouletteAsync`, `:290-348`) — spin 0–36; red/black pays 2x, an exact number pays 36x.

### Bank (CurrencyBankService)

Deposit moves currency from wallet to a separate `UserBankAccount`; withdrawal moves it back minus the configured penalty percent. Everything is gated by `BankEnabled` (default **off**), with a bank balance cap (`BankMaxBalance`, default 1,000,000). `ProcessInterestPayoutsAsync` (`CurrencyBankService.cs:118-163`) pays `BankInterestRate` percent (default 1% daily) to every account once per UTC day, capped at the max balance — and since 2026-08-15 each payout writes a `CurrencyTransaction` audit row with the reason "Interest". It is invoked hourly by the decay background service.

### Auctions (CurrencyAuctionService)

Moderators create auctions (item name, description, starting bid, duration in minutes, minimum increment — default 10). `PlaceBidAsync` (`CurrencyAuctionService.cs:56-129`) validates the bid against current+increment, refunds the previous bidder, deducts the new bid, logs a `CurrencyAuctionBid` row, and — when anti-snipe is enabled — extends an auction whose end is under 60 seconds away. Auctions are resolved lazily: a bid arriving after `EndsAt` triggers resolution, and `!auction end` does it explicitly; no background timer ends them. Resolution keeps the winner's stake and announces the win; cancel refunds the current bidder.

### Boosts, tiers, linked accounts

- **`CurrencyBoostService`** — timed multipliers, global or per-platform; `!boost` (moderator) creates them; `TriggerHypeTrainBoostAsync` (level-scaled 1.5x/2x/3x for 15–25 minutes) exists for a hype-train integration that has no caller.
- **`LoyaltyTierService`** — tiers evaluated from lifetime `TotalEarned` with a 10-minute tier-list cache; six tiers are seeded: Newcomer (0), Regular (1,000), Loyal (5,000), Veteran (25,000), Elite (100,000), Legend (500,000), each with an `EarnMultiplier` (1.0 to 2.0). Used by `!rank` and by the rewards gate; the earn multiplier itself is never applied.
- **`LinkedAccountService`** — cross-platform identity: a stable `ViewerId` shared by `LinkedAccount` rows. Since the linking wave it is backed by **`LinkCodeService`** (`VyralBot.Core/Services/Linking/`), which owns the live `!link` family: 6-digit codes valid 5 minutes, single-use, redeemed with `!link <code>` / `!linkcode <code>` or via Discord DM — the full flow documented in [Account Linking](../chat-features/account-linking.md). One quirk remains in the older initiation path: it assumes the same username on both platforms.
- **`MultiPlatformBonusService`** — the multi-platform earnings bonus: mode/opt-in/ladder resolution plus goal multipliers, multiplied into every earning path by `ApplyEarnMultipliersAsync`. Details in [Vyral Goals](vyral-goals.md).

### Background maintenance (CurrencyDecayBackgroundService)

An hourly loop (`CurrencyDecayBackgroundService.cs:34-66`) that runs three jobs: inactivity **decay** (when `CurrencyDecayEnabled` — off by default — remove `CurrencyDecayPercent` (5%) from users not seen for `CurrencyDecayDays` (30) days), **bank interest** payouts (each writing an "Interest" transaction), and **boost expiry**. Decay, boosts, and interest all write proper transaction rows.

```csharp
// CurrencyService.cs:295-352 (abridged) — an earning stream with the multiplier stack
public async Task AwardEventBonusAsync(string username, string platform, string eventType, int multiplier = 1)
{
    var settings = await GetSettingsRequiredAsync();
    int amount = 0;
    switch (eventType.ToLower())
    {
        case "follow":
            if (!settings.FollowBonusEnabled) return;
            amount = settings.FollowBonusAmount * multiplier;
            break;
        case "subscribe":
        case "sub":
            if (!settings.SubBonusEnabled) return;
            amount = settings.SubBonusAmount * multiplier;
            break;
        // giftsub / raid / cheer / host cases follow the same pattern
    }

    if (amount > 0)
    {
        // Apply the full multiplier stack (platform × boost × tier × role), floored
        amount = await ApplyEarnMultiplierStackAsync(amount, username, platform, role);
        await AddCurrencyAsync(username, platform, amount, reason, "EventBonus");
    }
}
```

```csharp
// CurrencyViewModel.cs — the Save button persists through the service (cache refreshed)
[RelayCommand]
private async Task SaveSettingsAsync()
{
    if (Settings == null) return;
    try
    {
        await _currencyService.SaveSettingsAsync(Settings);   // writes through + refreshes the cache
        StatusMessage = "Settings saved successfully!";
        _logger.LogInformation("Currency settings saved");
    }
    // ...
}
```

## Files & Code

| File | Role |
|---|---|
| [CurrencyView.xaml](../../src/VyralBot.UI/Views/CurrencyView.xaml) | The 4-tab page (Settings / Rewards / Redemptions / Leaderboard + transactions lookup). |
| [CurrencyViewModel.cs](../../src/VyralBot.UI/ViewModels/CurrencyViewModel.cs) | Page logic: settings edit + persist, rewards CRUD, redemption actions, leaderboard, transaction lookup. |
| [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) | The ledger: balances, transactions, all earning methods, multipliers, economy stats. |
| [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) | Chat-command dispatch (~25 commands) across the sub-services. |
| [CurrencyRewardsService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyRewardsService.cs) | Rewards store, redemption validation, approval queue. |
| [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) | Slots, duel, heist, roulette with per-game limits. |
| [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) | Deposits, withdrawals, daily interest. |
| [CurrencyAuctionService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyAuctionService.cs) | Auction lifecycle, bids, refunds, anti-snipe. |
| [CurrencyBoostService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBoostService.cs) | Timed earn-multiplier boosts (applied by the earning paths). |
| [LoyaltyTierService.cs](../../src/VyralBot.Core/Services/Currency/LoyaltyTierService.cs) | Tier evaluation from lifetime earnings (10-min cache). |
| [LinkedAccountService.cs](../../src/VyralBot.Core/Services/Currency/LinkedAccountService.cs) | Cross-platform identity linking with verification codes. |
| [CurrencyDecayBackgroundService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyDecayBackgroundService.cs) | Hourly maintenance: decay, interest, boost expiry. |
| [CurrencySetting.cs](../../src/VyralBot.Data/Entities/CurrencySetting.cs) | All economy configuration fields (see Data & Settings). |
| [UserCurrency.cs](../../src/VyralBot.Data/Entities/UserCurrency.cs) | Per-user wallet and lifetime counters. |
| [CurrencyTransaction.cs](../../src/VyralBot.Data/Entities/CurrencyTransaction.cs) | Audit trail row for every movement. |
| [CurrencyReward.cs](../../src/VyralBot.Data/Entities/CurrencyReward.cs) / [CurrencyRedemption.cs](../../src/VyralBot.Data/Entities/CurrencyRedemption.cs) | Store items and redemption queue rows. |
| [UserBankAccount.cs](../../src/VyralBot.Data/Entities/UserBankAccount.cs) / [ViewerStreak.cs](../../src/VyralBot.Data/Entities/ViewerStreak.cs) | Bank balances and daily-streak state. |
| [CurrencyAuction.cs](../../src/VyralBot.Data/Entities/CurrencyAuction.cs) / [CurrencyAuctionBid.cs](../../src/VyralBot.Data/Entities/CurrencyAuctionBid.cs) / [CurrencyBoost.cs](../../src/VyralBot.Data/Entities/CurrencyBoost.cs) | Auctions, bids, boosts. |
| [LoyaltyTier.cs](../../src/VyralBot.Data/Entities/LoyaltyTier.cs) / [LinkedAccount.cs](../../src/VyralBot.Data/Entities/LinkedAccount.cs) / [CurrencyGameSetting.cs](../../src/VyralBot.Data/Entities/CurrencyGameSetting.cs) | Tiers, linked identities, per-game limits. |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | SQLite persistence for all of the above; tables created around `DapperDataService.cs:2415-2680`, tier/game seeding at `:2729-2760`. |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Earning triggers: event bonuses, chat activity + command routing (live pipeline). |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | DI registration at `App.xaml.cs:117-126`. |

## Data & Settings

All tables live in the per-profile SQLite database (see [Data Layer](../architecture/data-layer.md) and the [database reference](../reference/database-reference.md)); table-creation sites in `DapperDataService.cs`:

| Table | Created at | Holds |
|---|---|---|
| CurrencySettings | `:2415` | one `CurrencySetting` row |
| UserCurrency | `:2440` | wallets per (username, platform) |
| CurrencyTransactions | `:2460` | the audit trail |
| CurrencyRewards | `:2479` | store items |
| CurrencyRedemptions | `:2501` | redemption queue |
| LinkedAccounts | `:2524` | platform → ViewerId rows |
| ViewerStreaks | `:2540` | daily-streak state |
| LoyaltyTiers | `:2558` | the six seeded tiers |
| CurrencyGameSettings | `:2575` | per-game enable/bets/cooldowns |
| CurrencyBoosts | `:2610` | active and past boosts |
| CurrencyAuctions / CurrencyAuctionBids | `:2628` / `:2652` | auctions and their bids |
| UserBankAccounts | `:2666` | bank balances and interest bookkeeping |
| PlatformEarnings | `:4055` | the per-platform earned/spent ledger for [unified balances](#unified-balances--the-per-platform-ledger), keyed by (ViewerId, Platform) |

`CurrencySetting` fields (entity at [CurrencySetting.cs](../../src/VyralBot.Data/Entities/CurrencySetting.cs)):

- **Identity**: `CurrencyName` ("Points"), `CurrencySymbol` (star), `UnifiedBalanceEnabled`.
- **Watch time**: `WatchTimeEnabled`, `WatchTimeInterval` (60 s), `WatchTimeAmount` (10).
- **Chat activity**: `ChatActivityEnabled`, `ChatActivityCooldown` (120 s), `ChatActivityAmount` (5).
- **Event bonuses**: `FollowBonusEnabled/Amount` (100), `SubBonusEnabled/Amount` (500), `GiftSubBonusEnabled/AmountPerSub` (50), `RaidBonusEnabled/AmountPerViewer` (10), `CheerBonusEnabled/AmountPerBit` (1), `HostBonusEnabled/Amount` (200).
- **Bonuses**: `FirstChatBonusEnabled/Amount` (50), `DailyBonusEnabled/Amount` (100), `StreakBonusEnabled` with `StreakBonusDay1/Day5/Day10` (50/100/250).
- **Multipliers**: `SubMultiplierEnabled/Value` (2.0), `VipMultiplierEnabled/Value` (1.5), `ModMultiplierEnabled/Value` (1.5), `TwitchEarnMultiplier`, `KickEarnMultiplier`, `YouTubeEarnMultiplier` (1.0).
- **Economy management**: `CurrencyDecayEnabled/Percent/Days` (off/5%/30), `BankEnabled/InterestRate/MaxBalance/WithdrawalPenalty/WithdrawalCooldownMinutes` (off/1%/1,000,000/0/0), `TransferTaxPercent` (0).

The transaction audit trail records `TransactionType` values including Earn, Spend, Transfer, Gamble, Refund, EventBonus, DailyBonus, FirstChat, BankDeposit, BankWithdrawal, Interest, Decay, Gift, and Admin — every wallet movement, bank interest included.

## Status & Known Issues

- **Chat-earning is live** — per-message earning, `!daily`, games, redemptions, auctions, and every other chat command run from real chat on all connected platforms (KI-1 resolved 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Settings tab persists, with a subset caveat** — `SaveSettingsAsync` goes through the currency service, but `DapperDataService.SaveCurrencySettingsAsync` writes only the name/symbol, watch-time, chat-activity, and follow/sub/giftsub/raid bonus fields; role multipliers and other columns keep their previous values (KI-29). The service's 5-minute settings cache is refreshed on save, so edits apply immediately.
- **Watchtime rate behavior change (KI-30)** — earning is configurable and multiplier-stacked, but defaults to 10/minute where older builds hard-coded 1/minute.
- **First-chat bonus semantics (KI-27)** — the bonus means "first currency record", not true first chat: a viewer who earned currency any other way first will never receive it.
- **Heists self-resolve** — an internal 60-second timer resolves the heist after the first join (race-safe); stakes are always paid out.
- **Boosts, tier, and role multipliers apply** — the earning paths multiply by platform × boost × tier × role (floored). Role-multiplier settings are among the fields the Settings save does not persist (KI-29).
- **Game cooldowns are in-memory** — restart clears them.
- **Banking is off by default** — `BankEnabled` defaults to false and has no UI toggle; enabling it requires editing the database directly (the Settings save does not write that column — KI-29).
- **Link codes are proper DB rows now** — the live `!link` flow (6-digit codes, 5-minute expiry, single-use) is persisted in the `LinkCodes` table with the correct chat reply; see [Account Linking](../chat-features/account-linking.md).
- **The multi-platform bonus is off by default** — `MultiPlatform.Mode` defaults to `Off`, so linked viewers earn at 1x until the streamer selects OptIn or Auto under Settings → Viewer Hub (see [Vyral Goals](vyral-goals.md#the-multi-platform-bonus)).
- **Unified balances are on by default** — `Linking.UnifiedBalances` defaults to true; setting it to `false` restores per-platform wallets for linked viewers. The ledger keeps its own records and never blocks an earn or spend.
- **Watch-time points follow live status** — stream platforms accrue only while live and Discord viewers never accrue; if the live-status registry is not wired, stream platforms fall back to always accruing (back-compat).
- **Discord chat-activity points respect assigned channels** — channels outside the per-bot assigned lists earn nothing; a failed assignment-list read fails open (awards continue) so a transient error cannot silently stop earning.
- **Ledger locking is cosmetic** — `_balanceLock` guards nothing; concurrent awards to the same wallet could race (low practical risk in the current pipeline).
