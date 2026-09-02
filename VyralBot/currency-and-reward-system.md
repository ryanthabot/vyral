# Currency System Upgrade Plan

## Current State Summary

The existing currency system is already solid with:
- **Earning**: watchtime, chat activity, event bonuses (follow/sub/gift sub/raid/cheer/host), daily bonus with streaks, first-chat bonus
- **Spending**: reward store with approval queue, categories, cooldowns, per-user/global limits, availability windows
- **Commands**: `!balance`, `!leaderboard`, `!daily`, `!store`, `!redeem`, `!rank`, `!give`, `!take`
- **Data**: full transaction audit trail, per-platform balances, multiplier system (sub/VIP/mod/per-platform)
- **Settings**: highly configurable earning rates, multipliers, decay flag (disabled)

**However**, the code references several delegate services that **don't exist yet**:
- `ILoyaltyTierService` — referenced in reward validation, commands
- `ILinkedAccountService` — referenced in account linking commands
- `ICurrencyGameService` — referenced in game commands (`!gamble`, `!slots`, `!roulette`, `!duel`, `!heist`)

These are the biggest gaps. Beyond that, there are several comprehensive features missing from what a top-tier streaming economy should have.

---

## Upgrade Plan — 10 Phases

All phases are **platform-agnostic by design**: services accept `(username, platform)` tuples, subscribe to generic EventBus events, and never reference platform-specific APIs. When TikTok/Discord are integrated later, they'll just publish the same event types and everything works.

---

### Phase 1: Loyalty Tiers System `[NEW SERVICE]`
**Priority: HIGH** — Already referenced in `CurrencyRewardsService.CanRedeemAsync()` and `CurrencyCommandService`

| Item | Description |
|------|-------------|
| `ILoyaltyTierService` / `LoyaltyTierService` | New service in `Core/Services/Currency/` |
| `LoyaltyTier` entity | Name, MinTotalEarned threshold, Multiplier bonus, Badge emoji, Color |
| `LoyaltyTierSettings` entity | Enable/disable, tier definitions, auto-promote/demote |
| DB table `LoyaltyTiers` | Tier definitions (Bronze/Silver/Gold/Platinum/Diamond) |
| DB table `UserLoyaltyTier` | Per-user current tier, promotion date, next tier progress % |
| `GetUserTierAsync(username, platform)` | Calculate tier from `TotalEarned` |
| `GetTierProgressAsync(username, platform)` | Progress bar data toward next tier |
| Tier-locked rewards | Already wired — `CurrencyReward.RequiredLoyaltyTier` exists |
| Tier multiplier stacking | Tier bonus on top of role multiplier |
| `!rank` / `!tier` command enhancement | Show tier name, badge, progress to next |
| Seed default tiers | Bronze(0), Silver(1000), Gold(5000), Platinum(25000), Diamond(100000) |

---

### Phase 2: Currency Games System `[NEW SERVICE]`
**Priority: HIGH** — Commands exist in `CurrencyCommandService` but delegate to missing `ICurrencyGameService`

| Item | Description |
|------|-------------|
| `ICurrencyGameService` / `CurrencyGameService` | New service in `Core/Services/Currency/` |
| `CurrencyGameSettings` entity | Per-game enable/disable, min/max bet, cooldown, odds |
| DB table `CurrencyGameSettings` | Configuration per game type |
| DB table `CurrencyGameHistory` | Game results log (user, game, bet, outcome, payout) |
| **Gamble** (`!gamble <amount>`) | 50/50 double-or-nothing, configurable odds |
| **Slots** (`!slots <amount>`) | 3-reel with symbol table, tiered payouts (2x, 5x, 10x, jackpot 50x) |
| **Roulette** (`!roulette <amount> <color/number>`) | Red/black (2x), number (36x), ranges |
| **Duel** (`!duel @user <amount>` / `!accept`) | PvP challenge with timeout, both bet same amount, winner takes all |
| **Heist** (`!heist <amount>`) | Multi-player cooperative — more joiners = higher success %, timed join window |
| Game cooldowns | Per-user per-game cooldown tracking |
| House edge config | Configurable house advantage per game (default 2-5%) |
| Jackpot pool | Small % of all game losses feed a progressive jackpot |
| Anti-abuse | Min/max bets, cooldowns, daily loss limits |

---

### Phase 3: Cross-Platform Account Linking `[NEW SERVICE]`
**Priority: HIGH** — Referenced in `!link` command

| Item | Description |
|------|-------------|
| `ILinkedAccountService` / `LinkedAccountService` | New service in `Core/Services/Currency/` |
| `LinkedAccount` entity | PrimaryUsername, PrimaryPlatform, LinkedUsername, LinkedPlatform, VerifiedAt |
| DB table `LinkedAccounts` | Account link records |
| `!link <platform> [code]` | Generate/verify 6-digit code to link accounts |
| Verification flow | User types `!link kick` on Twitch → gets code → types `!verify <code>` on Kick |
| Unified balance mode | When `CurrencySetting.UnifiedBalance = true`, all linked accounts share one balance |
| Unified leaderboard | Aggregate linked accounts for leaderboard ranking |
| Cross-platform transfer | Transfer between own linked accounts (no cost) |
| Merge balances | On link, option to merge or keep separate |
| Unlink support | `!unlink <platform>` with confirmation |

---

### Phase 4: Economy Events & Multiplier Boosts `[NEW]`
**Priority: MEDIUM** — Streamer engagement tool

| Item | Description |
|------|-------------|
| `ICurrencyBoostService` / `CurrencyBoostService` | Manages active boosts |
| `CurrencyBoost` entity | Multiplier, Duration, StartedAt, Reason, IsActive |
| DB table `CurrencyBoosts` | Active and historical boosts |
| Streamer-triggered boosts | "Start 2x earning for 30 minutes" from UI |
| Hype train escalation | Auto-boost on consecutive subs/cheers (1.5x → 2x → 3x) |
| Scheduled boosts | Set boosts for specific days/times (e.g., "Happy Hour Fridays") |
| Boost stacking rules | Configurable: additive vs. multiplicative vs. highest-wins |
| Chat announcement | Auto-announce boost start/end in all connected platforms |
| Boost history | Log of all boosts for analytics |

---

### Phase 5: Currency Auctions `[NEW]`
**Priority: MEDIUM** — Engagement-driven spending

| Item | Description |
|------|-------------|
| `ICurrencyAuctionService` / `CurrencyAuctionService` | Auction management |
| `CurrencyAuction` entity | ItemName, Description, StartingBid, CurrentBid, CurrentBidder, EndsAt, Status |
| DB table `CurrencyAuctions` | Auction records |
| DB table `CurrencyAuctionBids` | Bid history per auction |
| `!bid <amount>` | Place/raise bid in active auction |
| Anti-snipe | Auto-extend by 30s if bid placed in last 60s |
| Min bid increment | Configurable percentage or flat amount |
| Auto-refund | Previous bidder auto-refunded when outbid |
| Auction results | Winner announcement, history log |
| UI auction management | Create/monitor auctions from currency settings page |

---

### Phase 6: Banking & Interest System `[NEW]`
**Priority: MEDIUM** — Long-term engagement

| Item | Description |
|------|-------------|
| `ICurrencyBankService` / `CurrencyBankService` | Bank mechanics |
| `UserBankAccount` entity | Username, Platform, BankBalance, LastInterestPayout |
| DB table `UserBankAccounts` | Bank deposit balances |
| `!deposit <amount>` | Move from wallet to bank |
| `!withdraw <amount>` | Move from bank to wallet (cooldown optional) |
| `!bank` | Check bank balance and interest earned |
| Interest payouts | Configurable rate (e.g., 1% daily), paid on a schedule |
| Deposit cap | Max bankable amount per user (prevents hoarding) |
| Withdrawal penalty | Optional % lost on early withdrawal |
| Bank balance excluded from games | Can't gamble banked currency |

---

### Phase 7: Currency Decay & Taxation `[ENHANCE EXISTING]`
**Priority: LOW** — Already flagged in `CurrencySetting` but disabled

| Item | Description |
|------|-------------|
| Implement decay logic | `DecayPercentage` (5%) after `DecayDaysInactive` (30) — settings exist |
| `CurrencyDecayBackgroundService` | Hosted service running daily to apply decay |
| Exemptions | Subscribers exempt, linked accounts exempt, minimum balance threshold |
| Decay notification | Warn users via chat X days before decay |
| Transaction type "Decay" | Logged in audit trail |
| Tax on transfers | Optional % taken from user-to-user gifts (configurable, default 0%) |
| Tax on games | Optional % house take (separate from game odds) |

---

### Phase 8: Enhanced Reward Store `[ENHANCE EXISTING]`
**Priority: MEDIUM** — Improve the existing reward/store system

| Item | Description |
|------|-------------|
| Reward categories expansion | Predefined: "Chat", "Stream", "Sound", "Visual", "Custom" |
| Limited-edition rewards | Stock count, when gone they're gone |
| Seasonal/rotating rewards | Auto-enable/disable on schedule |
| Reward bundles | Buy a bundle of multiple rewards at discount |
| Reward gifting | `!gift <reward> @user` — buy a reward for someone else |
| Flash sales | Timed discount on specific rewards (50% off for 1 hour) |
| Reward preview | Description with image URL for UI display |
| `ActionSequenceId` integration | Wire rewards to trigger action sequences (already field exists) |

---

### Phase 9: Economy Analytics Dashboard `[NEW UI]`
**Priority: LOW** — Streamer insight tool

| Item | Description |
|------|-------------|
| Economy overview panel | Total circulation, daily earn/spend, active users (partially exists via `GetEconomyStatsAsync`) |
| Inflation/deflation tracking | Trend of total currency in circulation over time |
| Top earners/spenders charts | Bar chart with WPF charting |
| Earning breakdown | Pie chart: watchtime vs. chat vs. events vs. games |
| Currency velocity | How fast currency changes hands |
| Game profitability | Win/loss ratios per game type |
| Reward popularity | Most redeemed rewards |
| Platform distribution | Currency distribution across Twitch/Kick/YouTube/TikTok/Discord |
| Export data | CSV export of economy data |

---

### Phase 10: Integration with Other Features `[ENHANCE]`
**Priority: LOW** — Cross-feature synergy

| Item | Description |
|------|-------------|
| Giveaway entry cost | Pay X currency to enter giveaway (optional) |
| Poll vote weight | 1 currency = 1 extra vote (optional weighted polls) |
| Command unlock | Certain commands require loyalty tier or X balance |
| Alert overlay integration | Show currency milestones as alerts |
| Sound trigger rewards | Reward that plays a sound effect (wire to Audio system) |
| Watchtime bonus tiers | Higher watchtime = higher per-interval payout |

---

## Platform Readiness Matrix

Every feature uses the existing `(username, platform)` pattern. No platform-specific code enters the currency layer:

| Feature | Twitch | Kick | YouTube | TikTok | Discord |
|---------|--------|------|---------|--------|---------|
| Earn via chat | Yes | Yes | Yes | Ready* | Ready* |
| Earn via watchtime | Yes | Yes | Yes | Ready* | N/A |
| Event bonuses | Yes (all events) | Yes (all events) | Partial (chat/member) | Ready* | Ready* |
| Chat commands | Yes | Yes | Yes | Ready* | Ready* |
| Games | Yes | Yes | Yes | Ready* | Ready* |
| Account linking | Yes | Yes | Yes | Ready* | Ready* |
| Boosts | Platform-agnostic — works everywhere |
| Auctions | Platform-agnostic — works everywhere |
| Banking | Platform-agnostic — works everywhere |

\* **Ready** = The service accepts any platform string. When TikTok/Discord publish their events to EventBus, the currency system will process them with zero code changes.

---

## Implementation Order Recommendation

1. **Phase 1** (Loyalty Tiers) + **Phase 2** (Games) + **Phase 3** (Account Linking) — These are **already referenced** in existing code and are blocking existing command paths
2. **Phase 4** (Boosts) + **Phase 8** (Enhanced Store) — High engagement value
3. **Phase 5** (Auctions) + **Phase 6** (Banking) — New spending mechanics
4. **Phase 7** (Decay) + **Phase 9** (Analytics) + **Phase 10** (Integrations) — Polish
