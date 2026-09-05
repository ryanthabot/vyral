# Cross-Platform Viewer Identity, Multi-Platform Rewards & Live Giveaways — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One unified viewer identity per person across Discord/Twitch/Kick/YouTube (+ TikTok/Facebook/Rumble-ready), with code-based account linking, multi-platform point bonuses, tier-aware giveaway odds, a "Vyral Goals" promotion system, and giveaways that are deletable, live-updating, and traffic-adaptive.

**Architecture:** Extends the existing `LinkedAccounts`/`LinkedAccountService`/EventBus foundations. New persistence via additive SQLite tables; new services in `VyralBot.Core\Services\Linking\` and `...\Currency\`; giveaway rework stays inside `GiveawayService` with EventBus push to a debounced `GiveawayViewModel`; admin UI is a new "Viewer Hub" Settings category. Spec: `docs/superpowers/specs/2026-09-04-cross-platform-viewer-identity-design.md`.

**Tech Stack:** .NET 10 WPF (MVVM, CommunityToolkit.Mvvm), Dapper + SQLite, Discord.Net, TwitchLib, KickLib, YouTube Data API, xUnit.

## Global Constraints

- **Build command (MANDATORY, use exactly this):** `cd C:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\src && dotnet build VyralBot.sln -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/` — the user's app is running and locks `bin\`; NEVER build without `-p:BaseOutputPath=VerifyBin/`. Baseline: 0 errors, 18 warnings.
- **Wave agents must NOT run builds or tests** (concurrent MSBuild corrupts `obj\`). The orchestrator builds after each wave and dispatches fixers.
- **NO `git commit` / `git add` by task agents.** The working tree holds the user's uncommitted WIP; staging any shared file would commit their work. All changes stay unstaged. Exception: the orchestrator already committed only the spec/plan docs.
- Platform identities are **strings**: `"Twitch"`, `"Kick"`, `"YouTube"`, `"Discord"`, `"TikTok"`, `"Facebook"`, `"Rumble"`. Never introduce an enum.
- Follow existing patterns exactly: entities in `VyralBot.Data\Entities\` (string `Id = Guid.NewGuid().ToString()`, `CreatedAt = DateTime.UtcNow`, TEXT timestamps ISO-8601 via `DateTime.UtcNow.ToString("o")`-style storage consistent with neighboring code); schema only via `CREATE TABLE IF NOT EXISTS` + `AddColumnIfNotExistsAsync` inside `DapperDataService.EnsureDatabaseCreatedAsync()`; every new `IDataService` member MUST be forwarded in `VyralBot.UI\Services\ProfileAwareDataService.cs` or the solution won't compile.
- Events: records extending `EventBase` (namespace `VyralBot.Core.Services.EventBus.Events` for Core; platform events in the integration's `Events\` folder), published via injected `IEventBus.PublishAsync` wrapped in try/catch.
- ViewModels: CommunityToolkit `[ObservableProperty]`/`[RelayCommand]`, `ViewModelBase` base, dispatcher marshaling via `System.Windows.Application.Current.Dispatcher.InvokeAsync`, unsubscribe EventBus subscriptions in cleanup (`Unsubscribe(guid)` + reset field to `Guid.Empty`).
- XAML: never hardcode colors — use `DynamicResource` theme brushes (`PrimaryBrush`, `CardBackgroundBrush`, `TextPrimaryBrush`, `SuccessGreenBrush`, `ErrorRedBrush`, etc.). MaterialDesign styles as in sibling views.
- Config settings: dotted keys via `IDataService.GetConfigValueAsync/SetConfigValueAsync`; defaults must match the spec exactly.
- All user-facing strings hardcoded English (no .resx in this codebase).
- Chat replies: keep concise, emoji-prefixed like existing commands (`!link` uses "🔗").

---

### Task 1: Data layer — entities, schema, IDataService surface

**Files:**
- Create: `VyralBot\src\VyralBot.Data\Entities\LinkCode.cs`
- Create: `VyralBot\src\VyralBot.Data\Entities\ViewerPlatformStatus.cs`
- Create: `VyralBot\src\VyralBot.Data\Entities\ViewerPreferences.cs`
- Create: `VyralBot\src\VyralBot.Data\Entities\SocialGoal.cs`
- Create: `VyralBot\src\VyralBot.Data\Entities\ViewerGoalCompletion.cs`
- Modify: `VyralBot\src\VyralBot.Data\Entities\Giveaway.cs` (add `OddsBoostEnabled`)
- Modify: `VyralBot\src\VyralBot.Data\Entities\Platform.cs` (add Facebook/Rumble flags)
- Modify: `VyralBot\src\VyralBot.Data\IDataService.cs`
- Modify: `VyralBot\src\VyralBot.Data\DapperDataService.cs`
- Modify: `VyralBot\src\VyralBot.UI\Services\ProfileAwareDataService.cs`

**Interfaces (Produces — later tasks consume these EXACT signatures):**

```csharp
// Entities (namespace VyralBot.Data.Entities)
public class LinkCode {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Code { get; set; } = "";            // 6-digit string
    public string ViewerId { get; set; } = "";
    public string CreatedByUsername { get; set; } = "";
    public string CreatedByPlatform { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string? RedeemedByUsername { get; set; }
    public string? RedeemedByPlatform { get; set; }
    public DateTime? RedeemedAt { get; set; }
}
public class ViewerPlatformStatus {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? ViewerId { get; set; }
    public bool IsSubscriber { get; set; }
    public int SubTier { get; set; }                  // 0 none, 1..3
    public int SubscriberMonths { get; set; }
    public bool IsFollower { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
public class ViewerPreferences {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ViewerId { get; set; } = "";
    public bool MultiPlatformBonusOptIn { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class SocialGoal {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string GoalType { get; set; } = "";        // "LinkedPlatforms"|"FollowPlatforms"|"SubAnyPlatform"|"SubTier"
    public int TargetCount { get; set; } = 1;
    public string RequiredPlatforms { get; set; } = "All"; // csv or "All"
    public string RewardType { get; set; } = "Currency";   // "Currency"|"Multiplier"|"Badge"
    public double RewardValue { get; set; }                // points, multiplier factor, or unused
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class ViewerGoalCompletion {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Guid GoalId { get; set; }
    public string ViewerId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Platform { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public bool RewardApplied { get; set; }
}
// Giveaway.cs: add  public bool OddsBoostEnabled { get; set; }
// Platform.cs: add FacebookChatEnabled, FacebookChatAutoStart, RumbleChatEnabled, RumbleChatAutoStart (bool)
```

```csharp
// IDataService additions (grouped under "// Link Codes", "// Viewer Platform Status",
// "// Viewer Preferences", "// Social Goals" region comments)
Task<LinkCode?> GetLinkCodeAsync(string code);
Task SaveLinkCodeAsync(LinkCode code);
Task InvalidateLinkCodesForViewerAsync(string viewerId);      // marks unredeemed codes expired
Task<IEnumerable<LinkCode>> GetActiveLinkCodesAsync();        // RedeemedAt IS NULL AND ExpiresAt > now
Task<IEnumerable<LinkCode>> GetRecentLinkCodesAsync(int limit = 50); // ORDER BY CreatedAt DESC
Task DeleteLinkCodeAsync(string id);
Task<IEnumerable<LinkedAccount>> GetAllLinkedAccountsAsync();
Task UpdateLinkedAccountViewerAsync(string accountId, string newViewerId);
Task DeleteLinkedAccountsByViewerIdAsync(string viewerId);
Task<ViewerPlatformStatus?> GetViewerPlatformStatusAsync(string username, string platform);
Task SaveViewerPlatformStatusAsync(ViewerPlatformStatus status);
Task<IEnumerable<ViewerPlatformStatus>> GetViewerStatusesByViewerIdAsync(string viewerId);
Task<ViewerPreferences?> GetViewerPreferencesAsync(string viewerId);
Task SaveViewerPreferencesAsync(ViewerPreferences preferences);
Task<IEnumerable<SocialGoal>> GetSocialGoalsAsync();          // ORDER BY SortOrder
Task<SocialGoal?> GetSocialGoalAsync(Guid id);
Task SaveSocialGoalAsync(SocialGoal goal);                    // exists-check upsert
Task DeleteSocialGoalAsync(Guid id);
Task<ViewerGoalCompletion?> GetViewerGoalCompletionAsync(Guid goalId, string viewerId);
Task SaveViewerGoalCompletionAsync(ViewerGoalCompletion completion);
Task<int> GetGoalCompletionCountAsync(Guid goalId);
```

- [ ] **Step 1:** Read `DapperDataService.EnsureDatabaseCreatedAsync` (the LinkedAccounts block ~line 2902 is the model) and the `SaveLinkedAccountAsync` upsert pattern (~line 799).
- [ ] **Step 2:** Create the 5 entity classes exactly as declared above.
- [ ] **Step 3:** Add to `EnsureDatabaseCreatedAsync` — tables `LinkCodes` (`Code TEXT NOT NULL UNIQUE`, index `IX_LinkCodes_ViewerId`), `ViewerPlatformStatus` (`UNIQUE(Username, Platform)`, index on `ViewerId`), `ViewerPreferences` (`ViewerId TEXT NOT NULL UNIQUE`), `SocialGoals`, `ViewerGoalCompletions` (`UNIQUE(GoalId, ViewerId)`, index on `GoalId`) — plus `AddColumnIfNotExistsAsync` calls: `("Giveaways","OddsBoostEnabled","INTEGER NOT NULL DEFAULT 0")`, `("Platforms","FacebookChatEnabled","INTEGER NOT NULL DEFAULT 0")`, `("Platforms","FacebookChatAutoStart","INTEGER NOT NULL DEFAULT 0")`, `("Platforms","RumbleChatEnabled","INTEGER NOT NULL DEFAULT 0")`, `("Platforms","RumbleChatAutoStart","INTEGER NOT NULL DEFAULT 0")`.
- [ ] **Step 4:** Add all `IDataService` members + `DapperDataService` implementations in a new `#region` (follow the LinkedAccount region verbatim in style: Dapper `QueryFirstOrDefaultAsync`/`QueryAsync`, exists-check-then-UPDATE/INSERT upserts, `Guid` handled as string). `SaveGiveawayAsync`'s INSERT/UPDATE SQL must include the new `OddsBoostEnabled` column; `GetGiveawaysAsync`/`GetGiveawayAsync`/`GetActiveGiveawayAsync` SELECTs map it (verify the row mapper handles it — Dapper maps by name).
- [ ] **Step 5:** Forward every new member in `ProfileAwareDataService.cs` (one-line pass-throughs to `GetCurrentService().X(...)`).
- [ ] **Step 6:** Add `LinkCodes`/`SocialGoals`/`ViewerGoalCompletions`/`ViewerPlatformStatus`/`ViewerPreferences` to the `InventoryTables` whitelist (~line 2383).
- [ ] **Step 7:** Report completion with the list of members added. (Orchestrator builds.)

---

### Task 2: Discord DM-by-channel-id

**Files:**
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\IDiscordService.cs`
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\DiscordService.cs` (impl of `SendDmAsync` is ~line 2480 — the model to follow)

**Interfaces (Produces):**
```csharp
// IDiscordService — new member:
Task<bool> SendDmToChannelAsync(string channelId, string content, string? embedJson = null);
```

- [ ] **Step 1:** Implement in `DiscordService`: parse `channelId` as `ulong` (try/catch → false), resolve `Client.GetChannel(id) as IMessageChannel`, null → false, then `await channel.SendMessageAsync(text, embed: parsedEmbed)`. Return true on success, log failures at Warning. Guard `Client?.ConnectionState != ConnectionState.Connected` → false.
- [ ] **Step 2:** Report completion. (Used later by ChatViewModel for DM replies and link codes.)

---

### Task 3: Core services — events, link codes, viewer status, bonus, goals

**Files (ALL new):**
- Create: `VyralBot\src\VyralBot.Core\Services\EventBus\Events\ViewerEvents.cs`
- Create: `VyralBot\src\VyralBot.Core\Services\EventBus\Events\GiveawayEvents.cs`
- Create: `VyralBot\src\VyralBot.Core\Services\Linking\ILinkCodeService.cs` + `LinkCodeService.cs`
- Create: `VyralBot\src\VyralBot.Core\Services\Linking\IViewerStatusService.cs` + `ViewerStatusService.cs`
- Create: `VyralBot\src\VyralBot.Core\Services\Linking\IMultiPlatformBonusService.cs` + `MultiPlatformBonusService.cs`
- Create: `VyralBot\src\VyralBot.Core\Services\Linking\ISocialGoalService.cs` + `SocialGoalService.cs`

**Depends on:** Task 1 (data members) merged. Constructor-inject `IDataService`, `ILinkedAccountService` (exists: `GetOrCreateViewerIdAsync(username, platform)`, `GetLinkedAccountsAsync(viewerId)`, `UnlinkAccountAsync(username, platform)`), `ILogger<T>`, `IEventBus` as needed. Settings read via `IDataService.GetConfigValueAsync(key)` with `int.TryParse`/`double.TryParse` + defaults.

**Interfaces (Produces — exact):**

```csharp
// ViewerEvents.cs
public record AccountsLinkedEvent : EventBase { public string ViewerId = ""; public string Username = ""; public string Platform = ""; public string LinkedPlatformNames = ""; }
public record ViewerStatusUpdatedEvent : EventBase { public string Username = ""; public string Platform = ""; public int SubTier; public bool IsSubscriber; public bool IsFollower; }

// GiveawayEvents.cs
public record GiveawayStartedEvent : EventBase { public Guid GiveawayId; public string Title = ""; public string EntryKeyword = ""; }
public record GiveawayEntryEvent : EventBase { public Guid GiveawayId; public string Username = ""; public string Platform = ""; public int TotalEntries; public int UniqueParticipants; public bool IsTimed; }
public record GiveawayWinnersDrawnEvent : EventBase { public Guid GiveawayId; public string Title = ""; public string WinnersText = ""; }
public record GiveawayCancelledEvent : EventBase { public Guid GiveawayId; public string Title = ""; }
public record GiveawayIntakeModeChangedEvent : EventBase { public Guid GiveawayId; public string Mode = ""; public int EntriesPerMinute; } // Mode: "Live"|"Timed"
```
(Follow the property style of the existing `GiveawayAutoEndedEvent` record in `Events\GiveawayPollEvents.cs` — use `required`/init or positional style to match; the shapes above are the contract.)

```csharp
public record LinkRedeemResult(bool Success, string Message, string? ViewerId, IReadOnlyList<string> LinkedPlatforms);
public interface ILinkCodeService {
    Task<(string Code, DateTime ExpiresAt)> GenerateCodeAsync(string username, string platform);
    Task<LinkRedeemResult> RedeemCodeAsync(string code, string username, string platform);
    Task<IEnumerable<LinkedAccount>> GetLinkedAccountsForAsync(string username, string platform);
    Task<bool> UnlinkAsync(string username, string platform);
    Task CleanupExpiredAsync(); // deletes codes expired > 24h
}
public interface IViewerStatusService {
    Task RecordSubscriptionAsync(string username, string platform, int tier, int months);
    Task RecordFollowAsync(string username, string platform);
    Task<ViewerPlatformStatus?> GetStatusAsync(string username, string platform);
    Task<int> GetBestSubTierAcrossLinkedAsync(string viewerId);
    Task<bool> IsFollowerAnywhereAsync(string viewerId);
    Task<int> GetLinkedPlatformCountAsync(string viewerId);
}
public interface IMultiPlatformBonusService {
    Task<double> GetMultiplierAsync(string username, string platform); // 1.0 when disabled
    Task SetOptInAsync(string username, string platform, bool optedIn);
    Task<bool> GetOptInAsync(string username, string platform);
}
public interface ISocialGoalService {
    Task SeedDefaultsAsync(); // idempotent, no-op when any goal exists
    Task EvaluateViewerAsync(string viewerId, string username, string platform);
    Task<IEnumerable<SocialGoal>> GetGoalsAsync();
    Task SaveGoalAsync(SocialGoal goal);
    Task DeleteGoalAsync(Guid id);
    Task<int> GetCompletionCountAsync(Guid goalId);
}
// Static pure helpers (test targets — put each in its service's file, public):
public static class SubTierParser { public static int Parse(string? raw); } // "1000"->1 "2000"->2 "3000"->3 "Prime"->1 else 0
public static class MultiPlatformBonusLadder { public static double Resolve(int platformCount, double b2, double b3, double b4, double b5); } // <2 -> 1.0; 2..4 -> respective; >=5 -> b5
public static class GoalEvaluator { public static bool IsMet(SocialGoal goal, int linkedPlatformCount, int followedPlatformCount, int subbedPlatformCount, int bestSubTier); }
public static class EntryRateWindow { /* rolling window */ public void Record(DateTime utcNow); public int CountInLastMinute(DateTime utcNow); } // instance class, thread-safe (lock or ConcurrentQueue pruning)
```

- [ ] **Step 1:** `LinkCodeService.GenerateCodeAsync` — honor `Linking.Platforms.<platform>` config (default "true"; false → throw `InvalidOperationException("Linking is disabled for <platform>")`), expiry `Linking.CodeExpiryMinutes` (default 5). Resolve `GetOrCreateViewerIdAsync`, `InvalidateLinkCodesForViewerAsync` (old codes die), loop-generate `RandomNumberGenerator.GetInt32(100000,999999).ToString()` until `GetLinkCodeAsync` returns null, save, return.
- [ ] **Step 2:** `RedeemCodeAsync` — normalize code (trim, digits only). Look up; null → `(false, "Invalid or expired code. Request a new one with !link", null, [])`. `RedeemedAt != null` → "That code was already used." `ExpiresAt < UtcNow` → "That code has expired — request a new one with !link." Reject self-redeem (same username+platform as creator) → "You're already linked on that platform." Then merge: `GetOrCreateViewerIdAsync(redeemer)`; if different from code's ViewerId → `GetLinkedAccountsByViewerIdAsync(redeemerViewerId)` and `UpdateLinkedAccountViewerAsync` each row to the code's ViewerId (re-link win: if the redeemer's accounts include one equal to the creator's platform+username pair it's the same row — skip). Save `LinkedAccount` for the redeemer with the code's ViewerId, mark code redeemed (`RedeemedByUsername/Platform/At`). Publish `AccountsLinkedEvent` with the full linked-platform name list. Success message: `"✅ Linked! This account is now connected to {N} platform(s): {names}"`.
- [ ] **Step 3:** `ViewerStatusService` — `RecordSubscriptionAsync` upserts (keep max tier/months, set `IsSubscriber=true`, backfill `ViewerId` via `GetViewerIdAsync` when null) and publishes `ViewerStatusUpdatedEvent`; `RecordFollowAsync` same with `IsFollower=true`; the three aggregate methods query by ViewerId (statuses without ViewerId are ignored). Tier from `SubTierParser.Parse`.
- [ ] **Step 4:** `MultiPlatformBonusService.GetMultiplierAsync` — mode config `MultiPlatform.Mode` ("Off" default | "OptIn" | "Auto"). Off → 1.0. Resolve ViewerId (`ILinkedAccountService.GetViewerIdAsync`; null → 1.0); OptIn && !`GetOptInAsync` → 1.0. Count = distinct Platforms among linked accounts; ladder with `MultiPlatform.Bonus2/3/4/5` (defaults 1.25/1.5/2.0/2.5). Goal multipliers: keep a `ConcurrentDictionary<string viewerId, double>` of max "Multiplier"-goal reward value, updated when `SocialGoalService` completes a multiplier goal (both services live in the same wave — wire them via the `ViewerGoalCompletedEvent` subscription inside `MultiPlatformBonusService`'s constructor using the injected `IEventBus`); documented limitation: multiplier goals apply from process start onward (no DB hydration). Cache the final result per `username:platform` for 5 minutes.
- [ ] **Step 5:** `SocialGoalService.SeedDefaultsAsync` — when `GetSocialGoalsAsync()` is empty insert: ("Bridge Builder", link 2 platforms, Currency 500), ("Everywhere At Once", link 4 platforms, Badge 0, sort 2), ("Loyal Follower", follow on 3 platforms, Currency 250), ("Tier 2 Supporter", SubTier target 2, Currency 1000). `EvaluateViewerAsync` — gather linked count (ViewerStatusService), followed platforms / subbed platforms counts (distinct platforms among statuses with IsFollower/IsSubscriber), best tier; for each enabled goal not already completed (`GetViewerGoalCompletionAsync`), if `GoalEvaluator.IsMet` → save completion, apply reward (Currency → `ICurrencyService.AddCurrencyAsync(username, platform, (int)RewardValue, "Vyral goal: " + Name, "Bonus")`; Badge/Multiplier → no direct action), publish `ViewerGoalCompletedEvent { ViewerId, Username, Platform, GoalName, RewardDescription }` (add this record to ViewerEvents.cs).
- [ ] **Step 6:** All `PublishAsync` calls wrapped in try/catch with `_logger.LogWarning`. Report completion listing the public API of each service.

---

### Task 4: Giveaway core — events, delete, odds, adaptive intake

**Files:**
- Modify: `VyralBot\src\VyralBot.Core\Services\GiveawayService.cs` (owns fully)
- Modify: `VyralBot\src\VyralBot.Core\Services\GiveawayCommandService.cs`

**Depends on:** Task 1 + Task 3 merged (events, `IViewerStatusService`, `EntryRateWindow`, `GiveawayOdds`... `GiveawayOdds` is defined HERE).

**Interfaces (Produces):**
```csharp
// GiveawayService ctor becomes:
public GiveawayService(IDataService dataService, IEventBus eventBus,
    ILinkedAccountService linkedAccounts, IViewerStatusService viewerStatus,
    ILogger<GiveawayService> logger)
// CreateGiveawayAsync gains a trailing param:  ..., bool oddsBoostEnabled = false
// New members:
Task<bool> DeleteGiveawayAsync(Guid giveawayId);   // false (no delete) when Status == Active
Task<GiveawayIntakeMode> CurrentIntakeMode { get; } // enum GiveawayIntakeMode { Live, Timed } in GiveawayService.cs file
Task<int> GetCurrentEntriesPerMinuteAsync();
// Static test target (in GiveawayService.cs, public):
public static class GiveawayOdds {
    public static (int BonusEntries, string Breakdown) Calculate(
        bool isSubscriberNow, int subscriberBonusEntries,
        int bestLinkedTier, int t1Bonus, int t2Bonus, int t3Bonus,
        bool followerAnywhere, int followerBonus,
        int linkedPlatformCount, int perExtraPlatformBonus, int perExtraPlatformCap)
    { /* 1 sub bonus only when isSubscriberNow; tier bonus = tier1->t1, 2->t2, 3->t3, else 0;
         follower bonus when followerAnywhere; platform bonus = Min(cap, Max(0, linkedPlatformCount - 1)) * perExtraPlatformBonus;
         Breakdown like "Sub (+1), Tier 3 (+3), Follower (+1), 3 linked platforms (+2)" or "" when zero */ }
}
```

- [ ] **Step 1:** Publish events: `StartGiveawayAsync` → `GiveawayStartedEvent`; `DrawWinnersAsync` → `GiveawayWinnersDrawnEvent` (`WinnersText` = new `Winners` value, Title included); `CancelGiveawayAsync` → `GiveawayCancelledEvent`; `EnterGiveawayAsync` success → `GiveawayEntryEvent` (Live mode only — Timed mode publishes one aggregated event per flush instead, with the last username/platform and updated totals). All wrapped try/catch.
- [ ] **Step 2:** `DeleteGiveawayAsync` — Active → `false`; else `_dataService.DeleteGiveawayAsync(id)` → true.
- [ ] **Step 3:** Odds in `EnterGiveawayAsync` — when `giveaway.OddsBoostEnabled`: resolve `viewerId = await _linkedAccounts.GetViewerIdAsync(username, platform)`; `bestTier = viewerId != null ? await _viewerStatus.GetBestSubTierAcrossLinkedAsync(viewerId) : 0`; `followerAnywhere = viewerId != null && await _viewerStatus.IsFollowerAnywhereAsync(viewerId)`; `linkedCount = viewerId != null ? await _viewerStatus.GetLinkedPlatformCountAsync(viewerId) : 1`. Config keys (read once per call via cached settings accessor, 30s TTL): `Odds.Tier1BonusEntries` (1), `Odds.Tier2BonusEntries` (2), `Odds.Tier3BonusEntries` (3), `Odds.FollowerBonusEntries` (1), `Odds.LinkedPlatformBonusEntries` (1), `Odds.LinkedPlatformBonusCap` (3). `EntryCount = 1 + legacySubBonus + GiveawayOdds.Calculate(...)`; write breakdown into the entry's `Message` (prefix existing message with breakdown + "; ").
- [ ] **Step 4:** Adaptive intake — `EntryRateWindow _rateWindow`; on every keyword entry attempt `_rateWindow.Record(UtcNow)`. Check mode each entry: `rate >= Giveaway.LiveMaxEntriesPerMinute` (default 120) && Live → switch Timed (publish `GiveawayIntakeModeChangedEvent("Timed", rate)`, increment metrics `_metrics?.Increment("Giveaway","mode_switches")` — inject `IMetricsRegistry?` optional, null-tolerant); Timed && `rate <= Giveaway.TimedResumeEntriesPerMinute` (default 60) → switch Live. Timed mode: enqueue `EntryRequest` records into `ConcurrentQueue<EntryRequest>` (bounded 10,000 — overflow returns `(false, "Entry queue is full — try again shortly")`); a lazy `Task` flush loop (`PeriodicTimer` 1s) drains when the oldest entry is ≥ `Giveaway.TimedFlushSeconds` (default 10) old OR queue > 500: dedupe per (username,platform) keeping the latest flags, call the internal single-entry persist path for each, recompute totals once, publish one aggregated `GiveawayEntryEvent(IsTimed=true)`, increment `entries_batched`. On Timed→Live switch flush immediately. Entries in Timed mode skip chat confirmation (see Task 4 Step 5 + `IsTimed` in the event).
- [ ] **Step 5:** `GiveawayCommandService.ProcessCommandAsync` — when the service's `CurrentIntakeMode == Timed`, throttle per-entry confirmations: return the normal confirmation only if this username hasn't been confirmed in 60s (static `ConcurrentDictionary<string,DateTime>`), else return `null`. `!giveaway status` gains `"Mode: Live/Timed (N entries/min)"`.
- [ ] **Step 6:** Report completion with the final ctor signature + new members. (DI is rewired in Task 11.)

---

### Task 5: Chat integration — link commands, DM routing, status hooks

**Files:**
- Modify: `VyralBot\src\VyralBot.Core\Services\Currency\CurrencyCommandService.cs` (owns fully)
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\ChatViewModel.cs` (owns fully — large file, surgical edits only)

**Depends on:** Tasks 1–4 merged.

**Interfaces (Consumes):** `ILinkCodeService`, `IViewerStatusService`, `ISocialGoalService`, `IMultiPlatformBonusService`, `SendDmToChannelAsync(channelId, content, embedJson)` on `IDiscordService`, event records from Task 3, `GiveawayIntakeModeChangedEvent`.

- [ ] **Step 1:** Ctor-inject `ILinkCodeService`, `IViewerStatusService`, `ISocialGoalService`, `IMultiPlatformBonusService` into `CurrencyCommandService` (DI wiring lands in Task 11 — keep ctor params defaulted where possible is NOT allowed; just add params, Task 11 updates registration).
- [ ] **Step 2:** Rework `HandleLinkCommandAsync`: `!link` (no args) → `GenerateCodeAsync` → reply `"🔗 Your link code: {code} (expires in {n} min). Send it from another platform: type '!link {code}' in that platform's chat, or DM it to the Discord bot."`; `!link <digits>` or `!linkcode <digits>` → `RedeemCodeAsync` → result message; `!linked` → list platforms or "No linked accounts yet — type !link to start"; `!unlink <platform>` → `UnlinkAsync` + confirm. Gate everything on `Linking.Enabled` config (default true).
- [ ] **Step 3:** New commands: `!goals` → enabled goals + caller progress (linked count, follow count, sub count, best tier vs targets) via `ISocialGoalService` + `IViewerStatusService`; `!mystatus` → linked platforms + current multi-platform multiplier + goals done; `!mp on|off` → `SetOptInAsync` (reply confirms; works only when `MultiPlatform.Mode == "OptIn"` else explain current mode).
- [ ] **Step 4:** `ChatViewModel` Discord DM routing — in the `DiscordChatMessageEvent` handler (where `BotId`/`ChannelId` are available), when `evt.Channel == "DirectMessage"` record `(ChannelId, BotId, timestamp)` into a small `_lastDiscordDmByBot` map; extend the existing `ResolveDiscordBotOrigin` area so Discord replies originating from a DM message (matched by recency, 30s window) call `SendDmToChannelAsync(channelId, response)` on the right bot (`_discordBotManager.Get(botId)` fallback `_discordService`) instead of the selected channel. DM command replies must never go to the stream channel.
- [ ] **Step 5:** `ChatViewModel` DM link intake — inside `OnChatMessageReceivedAsync` BEFORE the normal command path: when `evt.Platform == "Discord"` and the message (trimmed, case-insensitive) is `link`, `!link`, or `connect` → call `ILinkCodeService.GenerateCodeAsync(evt.Username, "Discord")` and reply **in-DM** with the Task 5 Step 2 message; when it's a bare 6-digit code → `RedeemCodeAsync` and reply in-DM. Mark the message handled (return before giveaway/poll/currency fallthrough). Wrap in try/catch, reply "⚠️ Couldn't process that — try again" on error.
- [ ] **Step 6:** `ChatViewModel` status hooks — subscribe (existing subscription pattern, ids in fields, unsubscribed in cleanup): `TwitchSubscribeEvent`/`TwitchResubEvent` → `RecordSubscriptionAsync(username, "Twitch", SubTierParser.Parse(evt.Tier), months)`; `KickSubscribeEvent` → tier 1; `YouTubeMembershipEvent` (record lands in Task 8 — write the handler against the plan spec: `VyralBot.Integrations.YouTube.Events.YouTubeMembershipEvent { Username, UserId, Level, IsMilestone }`) → tier 1; `TwitchFollowEvent`/`KickFollowEvent` → `RecordFollowAsync`; `AccountsLinkedEvent` → chat announcement `🔗 {Username} just linked {Platform} — now on {N} platforms!` sent to every connected platform + `ISocialGoalService.EvaluateViewerAsync`; `ViewerGoalCompletedEvent` → announcement `🏆 {Username} completed '{GoalName}'! {RewardDescription}`; `GiveawayIntakeModeChangedEvent` → `Mode == "Timed" ? "⏱ High traffic — giveaway entries are being batched every few seconds." : "⚡ Traffic normalized — giveaway entries are live again."`. All announcements via the existing per-platform send helpers with `IsConnected` guards.
- [ ] **Step 7:** Report every insertion point (method + line) touched in ChatViewModel.

---

### Task 6: CurrencyService multiplier hook

**Files:**
- Modify: `VyralBot\src\VyralBot.Core\Services\Currency\CurrencyService.cs` (owns fully)

**Depends on:** Task 3 merged.

- [ ] **Step 1:** Add ctor param `IMultiPlatformBonusService? multiPlatformBonus = null` (nullable so existing DI/tests don't break; Task 11 passes the real one).
- [ ] **Step 2:** In `ApplyEarnMultipliersAsync` (~line 542): after the loyalty-tier factor, `if (_multiPlatformBonus != null) { try { final *= await _multiPlatformBonus.GetMultiplierAsync(username, platform); } catch (Exception ex) { _logger.LogWarning(ex, "Multi-platform bonus failed"); } }`.
- [ ] **Step 3:** Report the diff summary. No interface changes.

---

### Task 7: Giveaway UI — live updates, delete, intake chip, odds toggle

**Files:**
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\GiveawayViewModel.cs` (owns fully)
- Modify: `VyralBot\src\VyralBot.UI\Views\GiveawayView.xaml` (owns fully)

**Depends on:** Task 4 merged (events + service API).

- [ ] **Step 1:** Ctor gains `IEventBus`. Subscribe in `InitializeAsync` (`_isInitialized` guard, Guid fields): all five giveaway events. Each handler: set `_reloadPending = true` and start a 1-second `DispatcherTimer` (created once) if not running → on tick: stop timer, if selected giveaway still exists re-select by Id after `LoadGiveawaysAsync()`, reload entries for selection, update `IntakeModeDisplay`/`EntriesPerMinuteDisplay` from `CurrentIntakeMode`/`GetCurrentEntriesPerMinuteAsync()`. `GiveawayWinnersDrawnEvent` also sets `StatusMessage` ("🎉 Winners drawn for '{Title}'"). Unsubscribe all in cleanup.
- [ ] **Step 2:** `[RelayCommand] DeleteSelectedGiveawayAsync` — guard null; block Active with `StatusMessage` ("Cancel the giveaway before deleting it"); confirm via existing `ConfirmDialog` (`new ConfirmDialog("Delete giveaway '{Name}' and all its entries?")` — match `PollViewModel.DeletePollAsync` ~line 402 usage); on confirm `await _giveawayService.DeleteGiveawayAsync(id)` → reload + `StatusMessage`.
- [ ] **Step 3:** Create form: add `[ObservableProperty] bool _newGiveawayOddsBoostEnabled = false;` ("Enable follower/sub-tier odds boosts") passed as the new trailing arg to `CreateGiveawayAsync`.
- [ ] **Step 4:** XAML — Delete button (icon + tooltip) visible when `SelectedGiveaway != null` and status is Pending/Completed/Cancelled (DataTrigger pattern next to Cancel); live status chips near the stats cards: "LIVE"/"TIMED" (SuccessGreenBrush/ErrorRedBrush… use `SuccessGreenBrush`/`WarningBrush`) bound to `IntakeModeDisplay`, plus "{EntriesPerMinuteDisplay} entries/min"; odds toggle `ToggleButton`/checkbox in the create form bound to `NewGiveawayOddsBoostEnabled`. All brushes via `DynamicResource`.
- [ ] **Step 5:** Report completion + any binding names added.

---

### Task 8: YouTube membership structured event

**Files:**
- Create: `VyralBot\src\VyralBot.Integrations\YouTube\Events\YouTubeMembershipEvent.cs`
- Modify: `VyralBot\src\VyralBot.Integrations\YouTube\YouTubeChatService.cs`

- [ ] **Step 1:** Record (namespace `VyralBot.Integrations.YouTube.Events`, style of `YouTubeChatMessageEvent`): `public record YouTubeMembershipEvent : EventBase { public string Username = ""; public string UserId = ""; public string Level = ""; public bool IsMilestone; }`.
- [ ] **Step 2:** In `ProcessChatMessageAsync`/`BuildSpecialEventMessage` path where `newSponsorEvent`/`memberMilestoneChatEvent` are detected (~lines 545-594): also publish `YouTubeMembershipEvent { Username = channelId/displayName, UserId = authorDetails.channelId, Level = memberLevelName ?? "", IsMilestone = type == milestone }` via the same safe publish used for chat messages.
- [ ] **Step 3:** Report completion.

---

### Task 9: Viewer Hub settings UI

**Files:**
- Create: `VyralBot\src\VyralBot.UI\ViewModels\ViewerHubViewModel.cs`
- Create: `VyralBot\src\VyralBot.UI\Views\ViewerHubView.xaml` (+ `.xaml.cs` — plain `InitializeComponent`, copy `PerformanceView.xaml.cs`)
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\SettingsViewModel.cs` (ctor + accessor + load call ONLY)
- Modify: `VyralBot\src\VyralBot.UI\Views\SettingsView.xaml` (sidebar entry + ContentControl + Facebook/Rumble grid cards, see Task 10 note)
- Modify: `VyralBot\src\VyralBot.UI\MainWindow.xaml` (DataTemplate only)

**Depends on:** Tasks 1–4 merged. Model: the Performance tab wiring (spec explorer report §1).

- [ ] **Step 1:** `ViewerHubViewModel` (CommunityToolkit, `InitializeAsync()` + `_isLoaded` guards, auto-save in `OnXChanged` via `SetConfigValueAsync`): sections — **Linking**: `LinkingEnabled` (`Linking.Enabled`, default true), `CodeExpiryMinutes` (5), 7 platform toggles (`Linking.Platforms.{name}`), pending-codes `ObservableCollection<LinkCodeRow>` (Code, CreatedBy, Platform, ExpiresIn, Delete command → `DeleteLinkCodeAsync`) refreshed by a 30s `DispatcherTimer` while visible; **Multi-Platform Bonus**: `MpMode` ("Off"|"OptIn"|"Auto" via three RadioButtons bound to a string property), `Bonus2/3/4/5` (double, 1.25/1.5/2.0/2.5); **Giveaway Odds**: `Tier1/2/3Bonus`, `FollowerBonus`, `PerPlatformBonus`, `PlatformCap` ints; **Vyral Goals**: `ObservableCollection<SocialGoalRow>` CRUD (Add/Save/Delete via `ISocialGoalService`, fields Name/GoalType ComboBox of the 4 types/Target/RequiredPlatforms/RewardType ComboBox/RewardValue/Enabled, completions count label); **Linked Viewers**: search box + `ObservableCollection<LinkedViewerRow>` (ViewerId, platforms display, Unlink button → `DeleteLinkedAccountsByViewerIdAsync` for viewer-scoped or `UnlinkAsync` single) — paged/lazy, only loads on demand (Refresh button) to avoid heavy queries.
- [ ] **Step 2:** `ViewerHubView.xaml` — MaterialDesign cards stacked in a ScrollViewer, one per section above, headers with `PackIcon`, all brushes `DynamicResource`, consistent with `SettingsView` inline panels.
- [ ] **Step 3:** `SettingsViewModel`: ctor-inject + `public ViewerHubViewModel ViewerHubViewModel => ...;` + call `await _viewerHubViewModel.InitializeAsync();` in `InitializeAsync` (guard: only when category first selected is fine — follow PerformanceViewModel pattern exactly).
- [ ] **Step 4:** `SettingsView.xaml`: sidebar button "Viewer Hub" (`CommandParameter="ViewerHub"`, `PackIconKind.LinkVariant`) placed after Commands; `<ContentControl Visibility="...ConverterParameter=ViewerHub" Content="{Binding ViewerHubViewModel}"/>`; add Facebook Gaming + Rumble cards to the Platforms UniformGrid copying the TikTok card, each bound to `SelectFacebookCommand` / `SelectRumbleCommand` (those commands are created by Task 10 in the same wave — write the bindings exactly as named; the wave-end build validates them).
- [ ] **Step 5:** `MainWindow.xaml`: `<DataTemplate DataType="{x:Type vm:ViewerHubViewModel}"><local:ViewerHubView/></DataTemplate>` next to the Performance one.
- [ ] **Step 6:** Report all binding names.

---

### Task 10: Platform stubs — Facebook Gaming & Rumble cards

**Files:**
- Modify: `VyralBot\src\VyralBot.UI\Views\PlatformsView.xaml` (owns fully)
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\PlatformsViewModel.cs` (owns fully)
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\PlatformStartupSettingsViewModel.cs`
- Create: `VyralBot\AppDocs\platforms\facebook.md`, `VyralBot\AppDocs\platforms\rumble.md` (NOTE: TikTok doc is Task 15's — do NOT edit appdocs.json here)

- [ ] **Step 1:** `PlatformsViewModel`: `[ObservableProperty]` status flags + `[RelayCommand] SelectFacebookAsync` / `SelectRumbleAsync` following the minimal path: set `StatusMessage = "Facebook Gaming support is coming soon — account linking is already compatible."` (same for Rumble). No setup wizards, no connection logic.
- [ ] **Step 2:** `PlatformsView.xaml`: two cards in the card grid copying the TikTok card (logo reuse: use a generic `PackIcon` instead of a missing PNG), `Is{Platform}Connected` DataTriggers defaulting to disconnected, click → the new commands.
- [ ] **Step 3:** `PlatformStartupSettingsViewModel`: add entries with `isImplemented: false` (TikTok pattern) for Facebook/Rumble so the startup list renders them consistently.
- [ ] **Step 4:** Docs md files: short "coming soon" pages describing planned chat + account-linking support per platform (match the tone/structure of `AppDocs\platforms\kick.md`).
- [ ] **Step 5:** Report completion. (Do NOT touch `SettingsView.xaml` or `appdocs.json` — owned by Tasks 9/15.)

---

### Task 11: DI wiring

**Files:**
- Modify: `VyralBot\src\VyralBot.UI\App.xaml.cs` (owns fully)

**Depends on:** Tasks 3, 4, 5, 6 merged (final ctor signatures from their reports).

- [ ] **Step 1:** Register singletons next to `ILinkedAccountService` (~line 142): `ILinkCodeService, LinkCodeService`, `IViewerStatusService, ViewerStatusService`, `IMultiPlatformBonusService, MultiPlatformBonusService`, `ISocialGoalService, SocialGoalService`.
- [ ] **Step 2:** Update `GiveawayService` registration for the new ctor (IEventBus, ILinkedAccountService, IViewerStatusService resolve automatically).
- [ ] **Step 3:** Update `CurrencyService` registration to pass the `IMultiPlatformBonusService` (resolve from provider or reorder registration above it).
- [ ] **Step 4:** Update `CurrencyCommandService` + `GiveawayViewModel` + `SettingsViewModel` registrations for any new ctor params.
- [ ] **Step 5:** Add `services.AddSingleton<ViewerHubViewModel>()`.
- [ ] **Step 6:** In startup (`OnStartup`, after `EnsureDatabaseCreatedAsync`): resolve `ISocialGoalService` and fire-and-forget `SeedDefaultsAsync()`; resolve `ILinkCodeService` similarly for a one-time `CleanupExpiredAsync()`.
- [ ] **Step 7:** Report the registration diff.

---

### Task 12: Unit tests for pure logic

**Files:**
- Create: `C:\Users\user\OneDrive\Desktop\VyralBot\tests\VyralBot.UI.Tests\SubTierParserTests.cs`
- Create: `...\tests\VyralBot.UI.Tests\MultiPlatformBonusLadderTests.cs`
- Create: `...\tests\VyralBot.UI.Tests\GoalEvaluatorTests.cs`
- Create: `...\tests\VyralBot.UI.Tests\GiveawayOddsTests.cs`
- Create: `...\tests\VyralBot.UI.Tests\EntryRateWindowTests.cs`

**Depends on:** Tasks 3–4 merged. Convention: `{Class}Tests`, `Method_Scenario_ExpectedResult`, plain `[Fact]`, direct `new` (see `MetricsRegistryTests.cs`).

- [ ] **Step 1:** `SubTierParser`: "1000"→1, "2000"→2, "3000"→3, "Prime"→1, "prime"→1, null→0, ""→0, "999"→0.
- [ ] **Step 2:** `MultiPlatformBonusLadder.Resolve`: 1→1.0; 2→b2; 3→b3; 4→b4; 7→b5; defaults ladder check (1.25/1.5/2.0/2.5).
- [ ] **Step 3:** `GoalEvaluator.IsMet`: each of the 4 goal types met/not-met cases including `RequiredPlatforms` filtering ("All" vs csv) and boundary TargetCount equality.
- [ ] **Step 4:** `GiveawayOdds.Calculate`: no boosts → (0, ""); sub-only; tier3+follower+4 platforms with caps → exact bonus sum + breakdown string contains "(+3)"; cap clamping (6 platforms, cap 3 → platform part = 3×perPlatform).
- [ ] **Step 5:** `EntryRateWindow`: record 5, count in last minute = 5; entries older than 60s pruned; concurrent `Record` from 4 tasks × 1000 doesn't throw and counts match total.
- [ ] **Step 6:** Report the test count. (Orchestrator runs `dotnet test`.)

---

### Task 13: Integration build-fix pass

**Files:** whatever the build errors require (orchestrator assigns after running the build).

- [ ] **Step 1:** Run the Global Constraints build command. Fix every error (missing forwarders in `ProfileAwareDataService`, ctor drift vs Task 11, XAML binding/type errors, namespace misses). Re-run until 0 errors.
- [ ] **Step 2:** Run `cd C:\Users\user\OneDrive\Desktop\VyralBot && dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/` until green.
- [ ] **Step 3:** Report every fix.

---

### Task 14: Spec-coverage review

**Files:** read-only review; reports issues (orchestrator dispatches fixers).

- [ ] **Step 1:** Against the spec, verify each flow exists in code: Discord DM code generate/redeem (DM replies stay in-DM); `!link`/`!link <code>`/`!linkcode`/`!linked`/`!unlink`; merge-on-redeem; `ViewerPlatformStatus` recording incl. Twitch tier; multi-platform multiplier hooked into `ApplyEarnMultipliersAsync` with Off/OptIn/Auto; giveaway odds breakdown + `OddsBoostEnabled` gate + create-form toggle; giveaway delete (Active blocked) with confirm; five live events → debounced VM reloads; intake Live↔Timed with hysteresis + chat announcements; goals seeding/evaluation/rewards + `!goals`; Viewer Hub sections; Facebook/Rumble/TikTok stub surfaces; docs + appdocs.json (Task 15).
- [ ] **Step 2:** Check cross-task type consistency (event property names, config keys, command names) and pattern violations (hardcoded colors, non-forwarded IDataService members, unsubscribed handlers).
- [ ] **Step 3:** Report a numbered issue list with file:line, severity, and exact fix.

---

### Task 15: Docs — AppDocs pages + manifest

**Files:**
- Create: `VyralBot\AppDocs\chat-features\account-linking.md` (user guide: DM flow, code flow, commands table, platform support matrix incl. TikTok/Facebook/Rumble readiness)
- Create: `VyralBot\AppDocs\stream-tools\vyral-goals.md` (goals, rewards, `!goals`/`!mystatus`/`!mp`)
- Create: `VyralBot\AppDocs\platforms\tiktok.md`
- Modify: `VyralBot\AppDocs\stream-tools` giveaway page (delete/live/adaptive intake sections — find the existing giveaways page under `AppDocs\stream-tools\`)
- Modify: `VyralBot\AppDocs\reference` or currency page (multi-platform bonus + odds settings)
- Modify: `VyralBot\AppDocs\settings\overview.md` (Viewer Hub entry) and `VyralBot\AppDocs\appdocs.json` (register ALL new pages incl. Task 10's facebook.md/rumble.md)

- [ ] **Step 1:** Read `appdocs.json` + two existing pages for tone; write the pages; register everything.
- [ ] **Step 2:** Report the pages added/edited.

---

## Execution order (orchestrator)

1. Task 1 (data layer) → orchestrator build.
2. Tasks 2, 3 in parallel → build.
3. Tasks 4, 5, 6, 7, 8 in parallel (disjoint files) → build.
4. Tasks 9, 10, 11, 12, 15 in parallel (disjoint files) → build + test (Task 13 dispatched on failure).
5. Task 14 review → fixers as needed → final build + test → summary.
