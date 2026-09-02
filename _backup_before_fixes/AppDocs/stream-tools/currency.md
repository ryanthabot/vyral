# Currency & Rewards

> The app's largest subsystem: a full viewer economy with earning streams, a rewards store and approval queue, games, banking, auctions, boosts, loyalty tiers, and linked accounts — with an honest split between what actually runs today and what is waiting on dead wiring.

## Overview

Currency & Rewards gives viewers a loyalty currency (default: "Points" with a star symbol) they earn by hanging out in chat, watching, and triggering events, and spend on streamer-defined rewards or games. Balances are per (username, platform), every movement is written to a transaction audit trail, and a whole ecosystem of extras sits on top: daily streak bonuses, a bank with interest, auctions, earn-boost multipliers, loyalty tiers from lifetime earnings, and cross-platform account linking.

Despite living under the Tools hub's "Currency & Rewards" card, this feature is reached from the top-level **Currency** navigation item, not the Stream Tools sidebar group.

The honest current-state picture, up front:

- **What actually earns currency today: event bonuses only.** Follow, subscribe, gift-sub, and raid events on Kick and Twitch award currency from the live event handlers. Everything chat-driven — per-message earning, `!daily`, games, redemptions, the works — runs through the command pipeline that is dead in this build (no code publishes the event it listens to; see [Commands](../chat-features/commands.md)).
- **The Settings tab's Save button does not persist anything.** It reports success and writes nothing. The toggles you see on that page reflect database defaults (or values edited directly in SQLite), not your edits.
- **Several subsystems are implemented but never invoked**, even beyond the pipeline issue: first-chat bonuses, role multipliers, loyalty-tier earn multipliers, and boosts have no callers that apply them to awards. Each is listed under [Status & Known Issues](#status--known-issues).

What genuinely works right now: event-bonus awards (with platform multipliers), the transaction audit trail for those awards, the whole management UI (rewards CRUD, redemption queue, leaderboard, transaction lookup), and the hourly background maintenance loop (decay, bank interest, boost expiry) over whatever data exists.

## User Guide

Open **Currency** in the sidebar (top level, below the chat entries) or the "Currency & Rewards" card on the Tools hub. The page has four tabs; a transaction lookup sits under the Leaderboard tab.

### Settings tab

- **Currency Configuration** — the currency **Name** and **Symbol** shown in command responses.
- **Watch Time Rewards** — enable toggle, **Points Per Interval**, and **Interval (seconds)**. (This configurable path is currently dormant — see How It Works; the per-minute award that runs comes from [Watchtime](watchtime.md) and is fixed at 1 point per minute.)
- **Chat Activity Rewards** — enable toggle, **Points Per Message**, and **Cooldown (seconds)** (per-user minimum time between awards).
- **Event Bonuses** — enable toggles and amounts for **Follow**, **Subscribe**, **Gift Sub** (per sub), and **Raid** (per viewer).

Pressing **Save Settings** shows "Settings saved successfully!" — **but nothing is saved**; the button has no persistence behind it. Treat this tab as read-only until the bug is fixed. Many more settings exist in the database (cheer/host bonuses, streak tiers, multipliers, decay, bank rates, transfer tax) but have no UI — they keep their defaults unless edited directly in SQLite.

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
| Account linking | `!link` |

All of these are currently inert from chat because the command pipeline is dead. Moderator-gated commands reply with an explicit "You must be a moderator to use !x" message rather than silence.

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
| Chat activity | `AwardChatActivityAsync` (`CurrencyService.cs:229`) | every non-command chat message, gated by per-user cooldown | No — its only call site is in the dead pipeline (`ChatViewModel.cs:807`) |
| Watch time (configurable) | `AwardWatchTimeAsync` (`CurrencyService.cs:262`) | intended per-interval award using `WatchTimeInterval`/`WatchTimeAmount` | No — zero callers anywhere |
| Watch time (fixed) | `AddCurrencyAsync(..., 1, "Watchtime reward")` from `WatchtimeService.IncrementActiveWatchtimeAsync` | 1 point per tracked minute | No — activity recording is in the dead pipeline (see [Watchtime](watchtime.md)) |
| Event bonuses | `AwardEventBonusAsync` (`CurrencyService.cs:295`) | Kick/Twitch follow, sub, gift-sub, raid event handlers | **Yes** — the only working earning stream (`ChatViewModel.cs:1735,1824,1894,2123,2203,2345,2388`) |
| Daily bonus + streaks | `ClaimDailyBonusAsync` (`CurrencyService.cs:356`) | `!daily` | No — command pipeline |
| First-chat bonus | `AwardFirstChatBonusAsync` (`CurrencyService.cs:417`) | nothing — zero callers | No |

Event bonus amounts come from `CurrencySetting`: follow 100, subscribe 500, gift-sub 50 per sub (multiplied by the gift count), raid 10 per viewer (multiplied by viewer count), cheer 1 per bit, host/shoutout 200 — each behind its own enable flag. After the base amount is computed, the **platform multiplier** is applied (`GetPlatformMultiplier`, `CurrencyService.cs:493-502`) using `TwitchEarnMultiplier` / `KickEarnMultiplier` / `YouTubeEarnMultiplier`. Note that cheer and host bonuses have no live call sites — the Twitch cheer and Kick host handlers never invoke the award method.

Daily streaks use the `ViewerStreak` entity: once per UTC day, consecutive days increment the streak, a gap resets it to 1, and the bonus is `DailyBonusAmount` (100) plus a streak add-on — day 1 adds 50, day 5+ adds 100, day 10+ adds 250.

### Multipliers — what stacks and what is dormant

- **Platform multipliers** — applied, but only inside `AwardEventBonusAsync`.
- **Role multipliers** (`GetEffectiveMultiplierAsync`, `CurrencyService.cs:432` — sub 2.0, VIP 1.5, mod 1.5, highest wins, not stacking) — implemented, **never called**.
- **Loyalty tier multipliers** (`GetTierMultiplierAsync`) — implemented, **never called**.
- **Boost multipliers** (`CurrencyBoostService.GetActiveMultiplierAsync`, highest active boost wins) — implemented, **never called**; boosts are stored and expired but never affect an award.

### Rewards store and approval queue (CurrencyRewardsService)

`RedeemRewardAsync` (`CurrencyRewardsService.cs:48-106`) first runs `CanRedeemAsync` (`:184-273`), which checks: reward exists and is enabled; the optional availability window (`AvailableFrom`/`AvailableUntil`); wallet balance; the total redemptions cap; the per-user cap (counting non-rejected redemptions); the per-user cooldown; and the optional `RequiredLoyaltyTier` (compared against the user's lifetime `TotalEarned` versus the tier's `MinPoints`). It then deducts the cost, writes a `CurrencyRedemption` row — status `Pending` when the reward requires approval, else `Approved` — and increments the reward's `CurrentRedemptions`. `ApproveRedemptionAsync` / `RejectRedemptionAsync` (refunds the cost as a "Refund" transaction) / `CompleteRedemptionAsync` (`:118-182`) drive the queue. The UI's buttons call these with the hard-coded approver "Admin" (`CurrencyViewModel.cs:320-356`).

### Games (CurrencyGameService)

All games are gated by `CanPlayAsync` (`CurrencyGameService.cs:36-73`): per-game `CurrencyGameSetting` rows (enabled, min/max bet, cooldown seconds — seeded as Slots 10–10,000/30s, Duel 10–5,000/60s, Heist 10–5,000/300s, Roulette 10–10,000/30s) plus a wallet check. Cooldowns are an in-memory dictionary, so they reset on app restart.

- **Slots** (`PlaySlotsAsync`, `:81-132`) — three reels over six symbols; three-of-a-kind pays 10x for 7s, 7x for diamonds, 5x otherwise; any two-of-a-kind pays 2x.
- **Duel** (`InitiateDuelAsync`/`AcceptDuelAsync`, `:134-197`) — a challenge keyed to the opponent expires after 60 seconds; acceptance checks both wallets, then a 50/50 coin flip moves the stake from loser to winner.
- **Heist** (`JoinHeistAsync`/`ResolveHeistAsync`, `:199-288`) — a 60-second join window per platform; entry is deducted on join; resolution rolls each participant against a 30% + 5%-per-participant (max 80%) survival chance and winners split the pot proportionally to their bets. **`ResolveHeistAsync` has no callers** — nothing ever resolves a heist, so entry stakes would be deducted and never paid out even with a live pipeline.
- **Roulette** (`PlayRouletteAsync`, `:290-348`) — spin 0–36; red/black pays 2x, an exact number pays 36x.

### Bank (CurrencyBankService)

Deposit moves currency from wallet to a separate `UserBankAccount`; withdrawal moves it back minus the configured penalty percent. Everything is gated by `BankEnabled` (default **off**), with a bank balance cap (`BankMaxBalance`, default 1,000,000). `ProcessInterestPayoutsAsync` (`CurrencyBankService.cs:118-163`) pays `BankInterestRate` percent (default 1% daily) to every account once per UTC day, capped at the max balance — interest accrues to the bank balance directly and does **not** write a `CurrencyTransaction` row. It is invoked hourly by the decay background service.

### Auctions (CurrencyAuctionService)

Moderators create auctions (item name, description, starting bid, duration in minutes, minimum increment — default 10). `PlaceBidAsync` (`CurrencyAuctionService.cs:56-129`) validates the bid against current+increment, refunds the previous bidder, deducts the new bid, logs a `CurrencyAuctionBid` row, and — when anti-snipe is enabled — extends an auction whose end is under 60 seconds away. Auctions are resolved lazily: a bid arriving after `EndsAt` triggers resolution, and `!auction end` does it explicitly; no background timer ends them. Resolution keeps the winner's stake and announces the win; cancel refunds the current bidder.

### Boosts, tiers, linked accounts

- **`CurrencyBoostService`** — timed multipliers, global or per-platform; `!boost` (moderator) creates them; `TriggerHypeTrainBoostAsync` (level-scaled 1.5x/2x/3x for 15–25 minutes) exists for a hype-train integration that has no caller.
- **`LoyaltyTierService`** — tiers evaluated from lifetime `TotalEarned` with a 10-minute tier-list cache; six tiers are seeded: Newcomer (0), Regular (1,000), Loyal (5,000), Veteran (25,000), Elite (100,000), Legend (500,000), each with an `EarnMultiplier` (1.0 to 2.0). Used by `!rank` and by the rewards gate; the earn multiplier itself is never applied.
- **`LinkedAccountService`** — cross-platform identity: a stable `ViewerId` shared by `LinkedAccount` rows. `InitiateLinkAsync` generates a 6-digit code valid 5 minutes (in memory, and written to the bot's log); `VerifyLinkAsync` matches it and links the account. `!link twitch` on one platform then `!link <platform> <code>` on the other completes a link. Two quirks: the initiation path assumes the same username on both platforms, and the chat reply tells users to type "verify" where the actual expected input is the numeric code (visible only in the bot log).

### Background maintenance (CurrencyDecayBackgroundService)

An hourly loop (`CurrencyDecayBackgroundService.cs:34-66`) that runs three jobs: inactivity **decay** (when `CurrencyDecayEnabled` — off by default — remove `CurrencyDecayPercent` (5%) from users not seen for `CurrencyDecayDays` (30) days), **bank interest** payouts, and **boost expiry**. Decay and boosts write proper transactions ("Decay" type); interest does not.

```csharp
// CurrencyService.cs:295-352 (abridged) — the one earning stream that runs today
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
        // Apply platform-specific multiplier
        var platformMultiplier = GetPlatformMultiplier(settings, platform);
        amount = (int)(amount * platformMultiplier);
        await AddCurrencyAsync(username, platform, amount, reason, "EventBonus");
    }
}
```

```csharp
// CurrencyViewModel.cs:104-113 — the Save button that saves nothing
[RelayCommand]
private Task SaveSettingsAsync()
{
    if (Settings == null) return Task.CompletedTask;
    try
    {
        // Settings are saved through the service which updates the database
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
| [CurrencyViewModel.cs](../../src/VyralBot.UI/ViewModels/CurrencyViewModel.cs) | Page logic: settings edit (non-persistent), rewards CRUD, redemption actions, leaderboard, transaction lookup. |
| [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) | The ledger: balances, transactions, all earning methods, multipliers, economy stats. |
| [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) | Chat-command dispatch (~25 commands) across the sub-services. |
| [CurrencyRewardsService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyRewardsService.cs) | Rewards store, redemption validation, approval queue. |
| [CurrencyGameService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyGameService.cs) | Slots, duel, heist, roulette with per-game limits. |
| [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) | Deposits, withdrawals, daily interest. |
| [CurrencyAuctionService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyAuctionService.cs) | Auction lifecycle, bids, refunds, anti-snipe. |
| [CurrencyBoostService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBoostService.cs) | Timed earn-multiplier boosts (stored, never applied). |
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
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Earning triggers: event bonuses (live), chat activity + command routing (dead pipeline). |
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

`CurrencySetting` fields (entity at [CurrencySetting.cs](../../src/VyralBot.Data/Entities/CurrencySetting.cs)):

- **Identity**: `CurrencyName` ("Points"), `CurrencySymbol` (star), `UnifiedBalanceEnabled`.
- **Watch time**: `WatchTimeEnabled`, `WatchTimeInterval` (60 s), `WatchTimeAmount` (10).
- **Chat activity**: `ChatActivityEnabled`, `ChatActivityCooldown` (120 s), `ChatActivityAmount` (5).
- **Event bonuses**: `FollowBonusEnabled/Amount` (100), `SubBonusEnabled/Amount` (500), `GiftSubBonusEnabled/AmountPerSub` (50), `RaidBonusEnabled/AmountPerViewer` (10), `CheerBonusEnabled/AmountPerBit` (1), `HostBonusEnabled/Amount` (200).
- **Bonuses**: `FirstChatBonusEnabled/Amount` (50), `DailyBonusEnabled/Amount` (100), `StreakBonusEnabled` with `StreakBonusDay1/Day5/Day10` (50/100/250).
- **Multipliers**: `SubMultiplierEnabled/Value` (2.0), `VipMultiplierEnabled/Value` (1.5), `ModMultiplierEnabled/Value` (1.5), `TwitchEarnMultiplier`, `KickEarnMultiplier`, `YouTubeEarnMultiplier` (1.0).
- **Economy management**: `CurrencyDecayEnabled/Percent/Days` (off/5%/30), `BankEnabled/InterestRate/MaxBalance/WithdrawalPenalty/WithdrawalCooldownMinutes` (off/1%/1,000,000/0/0), `TransferTaxPercent` (0).

The transaction audit trail records `TransactionType` values including Earn, Spend, Transfer, Gamble, Refund, EventBonus, DailyBonus, FirstChat, BankDeposit, BankWithdrawal, Decay, Gift, and Admin — every wallet movement except bank interest.

## Status & Known Issues

- **Chat-earning is dead with the command pipeline** — `AwardChatActivityAsync`'s only call site is inside the never-published `ChatMessageReceivedEvent` handler (`ChatViewModel.cs:807`), so per-message earning, `!daily`, games, redemptions, auctions, and every other chat command are inert. Only **event bonuses** actually award currency today. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Settings tab Save does not persist** — `SaveSettingsAsync` (`CurrencyViewModel.cs:104-122`) reports success without calling any save API; the whole Settings tab is effectively read-only. The service's 5-minute settings cache would additionally delay hand-edited database changes from taking effect. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Heists can never pay out** — `ResolveHeistAsync` has zero callers; joining deducts the stake, but the pot is never distributed (independent of the pipeline issue).
- **Boosts never affect earnings** — `GetActiveMultiplierAsync` has no callers; boosts are stored, listed, and expired, but no award path multiplies by them.
- **Role and loyalty-tier multipliers are never applied** — `GetEffectiveMultiplierAsync` and `GetTierMultiplierAsync` have no callers; only platform multipliers (event bonuses only) are live.
- **First-chat bonus and configurable watch-time earning have no callers** — `AwardFirstChatBonusAsync` and `AwardWatchTimeAsync` are dead code; the watch-time award that runs is the fixed 1-per-minute one in [Watchtime](watchtime.md), which ignores `CurrencySetting` toggles.
- **Cheer and host bonuses have no event triggers** — the Twitch cheer and Kick host handlers never call `AwardEventBonusAsync`, so those settings are inert even though the award code supports them.
- **Bank interest bypasses the audit trail** — interest is added directly to `UserBankAccount.BankBalance` with no `CurrencyTransaction` row.
- **Game cooldowns are in-memory** — restart clears them.
- **Banking is off by default** — `BankEnabled` defaults to false and has no UI toggle; enabling it requires editing the database (which the Settings-tab bug makes the only way for any non-default setting).
- **Link verification codes are only visible in the bot log** — and the `!link` reply's instruction text does not match the expected input format.
- **Ledger locking is cosmetic** — `_balanceLock` guards nothing; concurrent awards to the same wallet could race (low practical risk in the current single-handler pipeline).
