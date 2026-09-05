# Discord Bot Roles & Auto-Detected Identity — Implementation Plan (v3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-Discord-bot role assignment (which bot handles account linking, which handles giveaway announcements) with the bot's real name and application ID auto-detected from Discord at connect time — zero manual input.

**Architecture:** Identity + role flags are additive columns on the existing `DiscordBots` rows. `DiscordService` captures `CurrentUser` at Ready and the `DiscordBotManager` persists it. `ChatViewModel` gates DM link intake on the linking-role bot; Discord legs of giveaway announcements route through the giveaway-role bot. Unset roles fall back to Primary (today's behavior), so nothing breaks unconfigured.

**Tech Stack:** .NET 10 WPF (CommunityToolkit.Mvvm, MaterialDesign), Discord.Net, Dapper/SQLite.

## Global Constraints (same as v1/v2 — binding)

- **Build:** `cd C:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\src && dotnet build VyralBot.sln -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/` (never without the override while testing; final launch build uses the plain path). Tests: `dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj -c Debug --nologo -v q -p:BaseOutputPath=VerifyBin/` from repo root — baseline 81/81.
- **Wave agents must NOT run builds/tests or git commands.** All changes stay uncommitted.
- Schema via `AddColumnIfNotExistsAsync` inside `EnsureDatabaseCreatedAsync`; `DiscordBot` entity is defined in `VyralBot\src\VyralBot.Data\IDataService.cs` (~line 9) — not a separate file.
- EventBus/XAML conventions as before: DynamicResource brushes only, dispatcher marshaling, unsubscribe in cleanup.
- DI cycle guard: never add a constructor edge `*Service → ICurrencyService` into anything `CurrencyService` (transitively) depends on. New Discord-layer code depends only on IDataService/IEventBus/ILogger.

## Pinned contracts (Wave-1 produces — ALL other waves code against these EXACTLY)

```csharp
// DiscordBot entity (IDataService.cs) adds:
public string? ApplicationId { get; set; }   // detected Discord snowflake
public string? BotUsername { get; set; }     // detected Discord username
public string? AvatarUrl { get; set; }
public bool RoleLinking { get; set; }        // this bot handles account-link DMs
public bool RoleGiveaway { get; set; }       // this bot sends giveaway announcements

// DiscordBotConfig record (VyralBot.Integrations\Discord\DiscordBotConfig.cs) gains the same 5 properties.

// IDiscordService adds:
string? DetectedBotUsername { get; }
string? DetectedApplicationId { get; }
event Action<string>? BotIdentityDetected;   // arg = manager row id ("BotId"); raised when Ready captures identity

// New file VyralBot.Integrations\Discord\DiscordBotRoles.cs:
public static class DiscordBotRoles {
    public const string Linking = "Linking";
    public const string Giveaway = "GiveawayAnnouncements";
    public static IReadOnlyList<string> All { get; } = new[] { Linking, Giveaway };
}

// IDiscordBotManager adds:
Task<string?> GetBotIdForRoleAsync(string role);       // flagged row id, or null when unassigned
Task<string?> ResolveBotIdForRoleAsync(string role);   // GetBotIdForRoleAsync ?? Primary?.BotId (never null when a bot exists)
Task AssignRoleAsync(string botId, string role, bool assigned); // assigned=true clears the role on all OTHER bots; persists row(s); raises BotsChanged
Task ClearRoleAsync(string role);                               // unassigns from every bot; persists; raises BotsChanged
```
Behavior rules: role resolution is case-sensitive on the constants. `AssignRoleAsync(…, assigned:false)` just clears that one bot. Identity persistence: when `BotIdentityDetected` fires, the manager updates ONLY `ApplicationId`/`BotUsername`/`AvatarUrl` (+ `UpdatedAt`) on that row — never Token/Name/roles/SelectedChannelId — and raises `BotsChanged` so open UI refreshes.

---

### Task V3-1: Data + Discord integration (identity detection + roles + manager API)

**Files (owns fully):**
- Modify: `VyralBot\src\VyralBot.Data\IDataService.cs` (DiscordBot entity only)
- Modify: `VyralBot\src\VyralBot.Data\DapperDataService.cs` (DiscordBots SQL + migrations)
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\IDiscordService.cs`
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\DiscordService.cs`
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\IDiscordBotManager.cs`
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\DiscordBotManager.cs`
- Modify: `VyralBot\src\VyralBot.Integrations\Discord\DiscordBotConfig.cs`
- Create: `VyralBot\src\VyralBot.Integrations\Discord\DiscordBotRoles.cs`

- [ ] Entity + config record + SQL: add the 5 columns to the DiscordBots table block in `EnsureDatabaseCreatedAsync` (5× `AddColumnIfNotExistsAsync`) and include them in the DiscordBots INSERT/UPDATE/SELECT SQL (`SaveDiscordBotAsync` + the row mapping for `GetDiscordBotsAsync`/`GetDiscordBotAsync` — follow the existing column order conventions).
- [ ] `DiscordService`: in the Ready path where `_platform.Username`/`_platform.UserId` are set (~lines 306-331), also set `DetectedBotUsername` / `DetectedApplicationId` (+ build avatar via `CurrentUser.GetAvatarUrl(ImageFormat.Png, 128)?.ToString()`), and raise `BotIdentityDetected(BotId)` once per connection (guard duplicate raises; wrap handler-invoking in try/catch).
- [ ] `DiscordBotManager`: keep a per-service subscription to `BotIdentityDetected` (where it already wires service lifecycle, e.g. where `ServiceStarted` handlers attach) → persist identity to that bot's row via `SaveDiscordBotAsync` (load row by id first, only mutate the 3 identity fields + UpdatedAt) → raise `BotsChanged`. Map all 5 new fields through `DiscordBotConfig` ↔ row conversions (LoadBotsAsync/AddBotAsync/UpdateBotAsync paths).
- [ ] Manager API per pinned contract: `GetBotIdForRoleAsync` (load rows, return flagged id), `ResolveBotIdForRoleAsync`, `AssignRoleAsync` (clear-on-others + persist + `BotsChanged`), `ClearRoleAsync`. Reuse the manager's existing row-loading/persistence helpers — do not open extra DB connections patterns beyond existing ones.
- [ ] Report the final surface + where identity persistence hooks in. (Orchestrator builds.)

### Task V3-2: Chat behavior — linking bot gating + Discord announcement routing

**Files (owns fully):**
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\ChatViewModel.cs`

- [ ] **DM intake gating**: in the Discord DM link/delink intake block (grep the v1/v2 DM intake handlers), before handling: `var linkingBotId = await _discordBotManager.ResolveBotIdForRoleAsync(DiscordBotRoles.Linking);` — compare against the DM's origin bot id (`ResolveDiscordBotOrigin(evt)` / the recorded DM origin; read how the existing intake gets BotId). Handle ONLY when the ids match; when they differ, skip intake silently (the message is then treated as a normal DM — keep the existing non-command DM behavior). If resolution returns null (no bots), proceed as today. Cache the resolution ~30s (static or instance field) to avoid a DB read per DM.
- [ ] **Discord announcement routing**: find ChatViewModel's Discord legs of giveaway-related announcements (giveaway start? winners-drawn announcements, `GiveawayIntakeModeChangedEvent` announcements, scheduler announcements — grep `SendSchedulerAnnouncementAsync` and the giveaway/goal/link announcement helpers). Route the DISCORD destination through `_discordBotManager.ResolveBotIdForRoleAsync(DiscordBotRoles.Giveaway)` (null → current target logic) using `ResolveDiscordSendTarget(roleBotId ?? originBotId)`. Non-giveaway announcements (link events, goal completions) keep today's routing.
- [ ] Careful: `ResolveBotIdForRoleAsync` is async — thread it through without `.Result/.Wait()`; announcements already run in async handlers.
- [ ] Report insertion points. (Orchestrator builds.)

### Task V3-3: Giveaway announcements via giveaway bot

**Files (owns fully):**
- Modify: `VyralBot\src\VyralBot.UI\Services\WorkflowMessageSender.cs`
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\GiveawayViewModel.cs`

- [ ] `WorkflowMessageSender` already injects `IDiscordBotManager` — in its Discord branch, prefer `_discordBotManager.ResolveBotIdForRoleAsync(DiscordBotRoles.Giveaway)` → send via that bot (`manager.Get(botId)`), falling back to the current Discord send logic when unassigned/unconnected. Keep Twitch/Kick legs untouched.
- [ ] `GiveawayViewModel.AnnounceToChatAsync` (~line 717): Discord leg goes through `WorkflowMessageSender` already — no change needed UNLESS the ViewModel sends to Discord some other way (verify; if it does, route through the same role resolution).
- [ ] Report what changed. (Orchestrator builds.)

### Task V3-4: UI — identity display + role pickers + Viewer Hub selector

**Files (owns fully):**
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\DiscordBotsViewModel.cs`
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\DiscordBotDetailViewModel.cs`
- Modify: `VyralBot\src\VyralBot.UI\Views\DiscordBotsView.xaml`
- Modify: `VyralBot\src\VyralBot.UI\Views\DiscordBotDetailView.xaml`
- Modify: `VyralBot\src\VyralBot.UI\ViewModels\ViewerHubViewModel.cs`
- Modify: `VyralBot\src\VyralBot.UI\Views\ViewerHubView.xaml`

- [ ] Read the existing bots list/detail VMs and views FIRST and follow their structure exactly (they already exist for multi-bot management).
- [ ] Bots list cards: show detected username + application ID when present (`🤖 {Name} · {BotUsername ?? "connecting…"} · ID {ApplicationId ?? "—"} `) — refresh on `BotsChanged` (subscribe via manager, unsubscribe in cleanup; the VMs may already do this for status).
- [ ] Bot detail: new "Identity (auto-detected)" card — read-only fields Application ID / Bot Username / Avatar (small image when AvatarUrl present; graceful fallback icon), caption "Detected automatically when the bot connects — nothing to type." New "Bot roles" card: two CheckBoxes "Account linking bot (handles link DMs)" and "Giveaway announcements bot" bound to commands that call `AssignRoleAsync(botId, role, checked)`; after save, reload all bots and reflect single-select state (checking one clears it elsewhere in the UI too). Helper caption: "Leave both unchecked to use your Primary bot for everything."
- [ ] Viewer Hub → Account Linking section: "Account linking Discord bot" ComboBox — items from the manager's bot rows (`{Name} ({BotUsername})`), plus a first item "Primary bot (default)" = `ClearRoleAsync(DiscordBotRoles.Linking)`. Selection → `AssignRoleAsync(botId, Linking, true)`. Load current assignment in `InitializeAsync` (via `GetBotIdForRoleAsync`). Persist nothing new in Config (flags on rows are the store).
- [ ] Report binding names + files. (Orchestrator builds.)

### Task V3-5: Docs

**Files (owns fully):**
- Modify: `VyralBot\AppDocs\platforms\discord.md` (roles + auto-detected identity, per-bot settings)
- Modify: `VyralBot\AppDocs\chat-features\account-linking.md` ("choose your linking bot" section)
- Read-verify `VyralBot\AppDocs\settings\overview.md` Viewer Hub wording still accurate.

- [ ] Document: identity auto-detection (no manual input), role assignment paths (Discord bot detail + Viewer Hub selector), defaults (Primary when unassigned), the multi-bot/dual-host note (a bot shared with another bot platform can still be assigned a role — only one app can hold its gateway session at a time, which the app already guards).
- [ ] Report pages. (Orchestrator builds.)

---

## Execution order

1. Tasks V3-1 … V3-5 ALL PARALLEL (disjoint files; V3-2/3/4 code against V3-1's pinned contracts verbatim).
2. Orchestrator build → fix agent for compile drift.
3. Reviewer (spec vs code, DI-cycle check, UX wording).
4. Final build + test → relaunch app → summary with a step-by-step usage guide.
