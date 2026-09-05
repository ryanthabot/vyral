# Linking Hardening + Viewer Profiles Implementation Plan (v2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make link codes unexploitable (one account per platform per identity, two-sided `!delink` removal, admin removal for banned users) and let the host click any username in chat to open a full viewer profile window.

**Architecture:** Extends the shipped v1 linking system (`LinkCodeService`, `LinkedAccounts`, `LinkCodes` table). Delink codes reuse the `LinkCodes` table with new `Purpose`/`TargetPlatform` columns. Viewer profiles are a new aggregate service + WPF window opened from a clickable username in ChatView.

**Tech Stack:** .NET 10 WPF (CommunityToolkit.Mvvm, MaterialDesign), Dapper/SQLite, xUnit.

## Global Constraints (same as v1 plan — binding)

- **Build:** `cd C:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\src && dotnet build VyralBot.sln -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/` — NEVER without the BaseOutputPath override (running app locks bin\). Current baseline: 0 errors, 64/64 tests green via `dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/` from repo root.
- **Wave agents must NOT run builds/tests or git commands.** All changes stay uncommitted.
- Platform strings: "Twitch", "Kick", "YouTube", "Discord", "TikTok", "Facebook", "Rumble"; platform comparisons `StringComparer.OrdinalIgnoreCase`.
- Every new `IDataService` member MUST be implemented in `DapperDataService` AND forwarded in `VyralBot.UI\Services\ProfileAwareDataService.cs`.
- Schema changes only via `AddColumnIfNotExistsAsync` / `CREATE TABLE IF NOT EXISTS` in `EnsureDatabaseCreatedAsync`.
- EventBus records extend `EventBase`; `SubscribeAsync` Guids stored + unsubscribed in cleanup; UI mutations dispatcher-marshaled; XAML uses `DynamicResource` theme brushes only.
- Config keys dotted (`Linking.*`); no new settings tables.

---

### Task V2-1: Data layer — delink columns + profile queries

**Files:**
- Modify: `VyralBot\src\VyralBot.Data\Entities\LinkCode.cs` (add `Purpose` default "Link", `TargetPlatform` nullable string)
- Modify: `VyralBot\src\VyralBot.Data\IDataService.cs`
- Modify: `VyralBot\src\VyralBot.Data\DapperDataService.cs`
- Modify: `VyralBot\src\VyralBot.UI\Services\ProfileAwareDataService.cs`

**Interfaces (Produces — exact):**
```csharp
// LinkCode entity adds:
public string Purpose { get; set; } = "Link";        // "Link" | "Delink"
public string? TargetPlatform { get; set; }          // Delink only: platform being removed

// IDataService adds:
Task<IEnumerable<ViewerGoalCompletion>> GetViewerGoalCompletionsByViewerIdAsync(string viewerId);
Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesForUserAsync(string username, string platform);
```
- [ ] Add both `AddColumnIfNotExistsAsync` calls for `LinkCodes` (`Purpose TEXT NOT NULL DEFAULT 'Link'`, `TargetPlatform TEXT`) in `EnsureDatabaseCreatedAsync`; include both columns in `SaveLinkCodeAsync` INSERT/UPDATE SQL and the entity mapping.
- [ ] Implement the two query methods (follow the file's existing patterns; `GetGiveawayEntriesForUserAsync` filters `GiveawayEntries` by `Username` + `Platform`, `ORDER BY EnteredAt DESC LIMIT 100`).
- [ ] Forward both in `ProfileAwareDataService`.
- [ ] Report members added. (Orchestrator builds.)

### Task V2-2: LinkCodeService — one-per-platform + delink + chat commands

**Files (owns fully):**
- Modify: `VyralBot\src\VyralBot.Core\Services\Linking\ILinkCodeService.cs`
- Modify: `VyralBot\src\VyralBot.Core\Services\Linking\LinkCodeService.cs`
- Modify: `VyralBot\src\VyralBot.Core\Services\Currency\CurrencyCommandService.cs` (`!delink` command routing only)

**Interfaces (Produces — exact):**
```csharp
public record DelinkResult(bool Success, string Message);
public interface ILinkCodeService {
    // existing members unchanged...
    Task<(string Code, DateTime ExpiresAt)> GenerateDelinkCodeAsync(string username, string platform, string targetPlatform);
    Task<DelinkResult> RedeemDelinkCodeAsync(string code, string username, string platform);
}
```
- [ ] **One-per-platform in `RedeemCodeAsync`** (before the merge): target identity's accounts (`GetLinkedAccountsByViewerIdAsync(code.ViewerId)`) must not contain a row with the redeemer's platform (OrdinalIgnoreCase), excluding the creator's own row; if violated → `LinkRedeemResult(false, "Your link group already has a {platform} account ({name}). Remove it first with !delink.", null, [])`. Merge-conflict check: if the redeemer has their own identity with an account on a platform the target identity ALREADY has (other than the platform being redeemed) → reject with "Both link groups have a {platform} account — remove one with !delink first."
- [ ] **`GenerateDelinkCodeAsync`**: resolve caller's ViewerId (`GetViewerIdAsync`; null → `InvalidOperationException("You have no linked accounts")`). Target platform normalized/validated against the 7 known names. Guards: targetPlatform == caller's platform → reject ("You can't authorize removal of the account you're typing from"); caller's identity has no account on targetPlatform → reject ("No {platform} account is linked to your group"); identity has exactly one account total → reject ("This is your only linked account — ask the streamer to remove it"). Invalidate prior unredeemed Delink codes for the same viewer+target. Generate 6-digit code (reuse the collision-checked helper), save with `Purpose="Delink"`, `TargetPlatform=targetPlatform`, same expiry config.
- [ ] **`RedeemDelinkCodeAsync`**: lookup code → unknown/used/expired messages mirroring RedeemCodeAsync ("That code can't be used" family). Validate `Purpose=="Delink"`. Resolve redeemer's ViewerId; must equal code's ViewerId ("That code wasn't issued for this account group"). `platform` must equal `code.TargetPlatform` ("Type !delink <code> from {TargetPlatform}"). Then: find and delete the `LinkedAccount` row for (username, platform); null out `ViewerPlatformStatus.ViewerId` for that (username, platform) if a row exists (fetch → set → `SaveViewerPlatformStatusAsync`); mark code redeemed. Publish `AccountsLinkedEvent` (same record, `LinkedPlatformNames` = remaining platforms) so UI/chat lists refresh. Message: `"✅ {platform} account removed from your link group. {N} platform(s) remain: {names}"` or, when none remain, "Your accounts are no longer linked together."
- [ ] **`!delink` in `CurrencyCommandService`** (same handler style as `!link`): no args → usage help ("From one linked account: !delink <platform> — then on the account to remove: !delink <code>"); `!delink <platform-name>` → GenerateDelinkCodeAsync → reply with code + "Now type '!delink {code}' on {platform} within {n} minutes."; `!delink <6-digit>` → RedeemDelinkCodeAsync → result message. Gate on `Linking.Enabled` like `!link`.
- [ ] Report final API + all new reply strings. (Orchestrator builds.)

### Task V2-3: Chat click → viewer profile window

**Files (owns fully):**
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\ChatViewModel.cs` (click command + ChatMessage change ONLY)
- Modify: `VyralBot\src\VyralBot.UI\Views\ChatView.xaml` (username Button template ONLY)

**Consumes (built in parallel by V2-4 — code against this contract exactly):**
```csharp
// namespace VyralBot.UI.ViewModels
public partial class ViewerProfileViewModel : ViewModelBase {
    public Task InitializeAsync(string username, string platform); // call BEFORE showing the window
}
// namespace VyralBot.UI.Views — ctor takes the VM:
public ViewerProfileWindow(ViewerProfileViewModel viewModel) { ... InitializeComponent(); DataContext = viewModel; }
```
- [ ] In ChatView.xaml's message DataTemplate, the username `TextBlock` (~line 402, `Text="{Binding Username}"`) becomes a `Button` styled as plain text (Template with Borderless/transparent background — check if a TextBlock-like button style exists in the app's resources or ThemeBrushes siblings; if none, inline a minimal ControlTemplate), `Command="{Binding DataContext.OpenViewerProfileCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"`-style binding to the VM (match how other per-row commands bind in this view), `CommandParameter="{Binding}"`, `Cursor="Hand"`, ToolTip "View viewer profile".
- [ ] ChatViewModel: `[RelayCommand] OpenViewerProfileAsync(object? parameter)` — parameter is the `ChatMessage` (defined at the bottom of this file ~line 5187; use Username + Platform properties; if Platform is empty on some message types, fall back to the VM's currently selected platform filter). Resolve services via `_serviceProvider`-style field the class already uses (read the ctor; ChatViewModel already receives IServiceProvider or similar — if not, resolve through the existing pattern used by other VM-launched windows in this codebase, e.g. ProfileSelectorWindow). On UI thread (the click is UI): `var vm = App.ServiceProvider.GetRequiredService<ViewerProfileViewModel>(); await vm.InitializeAsync(username, platform); var win = new ViewerProfileWindow(vm) { Owner = Application.Current.MainWindow }; win.ShowDialog();` wrapped in try/catch with the class's existing error surface. If a profile window is already open for the same username+platform, bring it to front instead of opening a duplicate (track the last open window in a field).
- [ ] Verify ChatMessage actually carries `Platform` (read ~line 5187); if a message can lack it, resolve fallback as described.
- [ ] Report insertion points. (Orchestrator builds.)

### Task V2-4: Viewer profile service + window

**Files:**
- Create: `VyralBot\src\VyralBot.Core\Services\Linking\IViewerProfileService.cs` + `ViewerProfileService.cs`
- Create: `VyralBot\src\VyralBot.UI\ViewModels\ViewerProfileViewModel.cs`
- Create: `VyralBot\src\VyralBot.UI\Views\ViewerProfileWindow.xaml` (+ `.xaml.cs`)
- Modify: `VyralBot\src\VyralBot.UI\App.xaml.cs` (DI ONLY: `AddTransient<ViewerProfileViewModel>()` + `IViewerProfileService` singleton near the other Linking services; note it's a Host builder style file — follow how sibling services are registered)
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\ViewerHubViewModel.cs` (Linked Viewers section: per-account unlink — each row's platforms become individual account entries with their own Unlink button; keep any whole-group action if present)

**Interfaces (Produces — exact contract V2-3 codes against):** `ViewerProfileViewModel.InitializeAsync(string username, string platform)` (public Task), `ViewerProfileWindow(ViewerProfileViewModel viewModel)`.

**Profile content (aggregate in `ViewerProfileService.GetProfileAsync(username, platform) → ViewerProfileSnapshot`):**
- Identity: ViewerId (via `ILinkedAccountService.GetViewerIdAsync`; null → snapshot with just the raw account info + "not linked"), all `LinkedAccount` rows for it, each marked IsCurrent when (username, platform) matches.
- Per-account status: `ViewerPlatformStatus` rows for the ViewerId (sub tier, follower, months, last seen).
- Economy: `UserCurrency` rows for each linked (username, platform) pair — balance, TotalEarned, TotalSpent; plus `GetCurrencyTransactionsAsync(username, platform, 25)` per account (or aggregated recent list).
- Activity: `UserWatchtime` rows (TotalMinutes, StreamSessionMinutes, FirstSeenAt, LastSeenAt, IsActive) + `ViewerStreak` rows.
- Goals: `GetViewerGoalCompletionsByViewerIdAsync(viewerId)` joined with `GetSocialGoalsAsync()` names.
- Giveaways: `GetGiveawayEntriesForUserAsync(username, platform)` per account + the giveaway names (via `GetGiveawaysAsync()`) and IsWinner flags.
- Perks: `GetActiveViewerPerksAsync(username, platform)` per account.
- Events: `GetEventStatisticsAsync(username-filtered)` — check the signature; if it can't filter by username, use `StatisticsService`'s `GetUserStatisticsAsync(username)` if present (read `IStatisticsService`) — show TotalEvents + EventsByType highlights.
Snapshot = immutable record/graph of plain rows the VM binds directly.

- [ ] `ViewerProfileViewModel`: `[ObservableProperty]` sections (Identity header, LinkedAccounts collection with per-row Unlink command calling `ILinkCodeService.UnlinkAsync(username, platform)` + admin note when it's the current account's platform; status grid; economy; activity; goals; giveaways; perks; recent transactions), `InitializeAsync` loads via the service on a background thread and marshals results to the dispatcher; refresh button; null-viewerId renders the "not linked" state gracefully. Unlink button per row labeled with the account name, ToolTip "Remove this account from the link group (banned-user override)".
- [ ] `ViewerProfileWindow`: borderless-chrome consistent with the app (read how `ProfileSelectorWindow`/`ConfirmDialog` do window chrome — reuse the same pattern), title "Viewer — {username} ({platform})", scrollable sections with MaterialDesign cards, DynamicResource brushes, close button. Width ~720, height ~640, resizable.
- [ ] Viewer Hub per-account unlink per the Files list.
- [ ] Report binding names + DI additions. (Orchestrator builds.)

### Task V2-5: Docs + tests

**Files:**
- Modify: `VyralBot\AppDocs\chat-features\account-linking.md` (one-per-platform rule, full `!delink` flow, banned-user path)
- Create: `VyralBot\AppDocs\chat-features\viewer-profiles.md` (host guide: click username → profile; what each section shows)
- Modify: `VyralBot\AppDocs\settings\overview.md` (Viewer Hub per-account unlink mention) and `VyralBot\AppDocs\appdocs.json` (register viewer-profiles.md)
- Create: `tests\VyralBot.UI.Tests\LinkGuardTests.cs`

- [ ] Docs per above; tone matches existing pages.
- [ ] `LinkGuardTests.cs`: tests for whatever pure logic the delink/one-per-platform guards expose — if `LinkCodeService` guards are DB-coupled (they are), extract the small pure decision(s) where trivial OR write tests that document guard behavior via a lightweight fake `IDataService` limited to the methods the guards touch (only if practical without mocking frameworks — the project uses plain xUnit with hand-written fakes; check `ViewerStatusService` fixer note about existing fake IDataService implementations and follow that pattern). Minimum: assert the guard-rejection MESSAGE STRINGS match the plan copy exactly (constant strings in the service — extract `public static class LinkMessages` if needed so tests reference constants, not literals). Adapt to what's in the tree; do not over-engineer.
- [ ] Report tests added + doc pages. (Orchestrator builds + runs suite.)

---

## Execution order

1. Tasks V2-1 … V2-5 ALL PARALLEL (disjoint files; V2-3 codes against V2-4's pinned contract; V2-4 must not deviate from it).
2. Orchestrator build → fix agent for any compile drift.
3. Reviewer agent (spec coverage vs the v2 spec section + quality).
4. Final build + test → summary.
