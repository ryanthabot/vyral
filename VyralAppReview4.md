# VyralBot Review — Part 4: CHAT FEATURES

Read-only code review. Scope: the "Chat Features" area of VyralBot (multi-platform streamer assistant, .NET 10 WPF, projects: `VyralBot.Core`, `VyralBot.UI`, `VyralBot.Data`, `VyralBot.Api`, `VyralBot.Integrations`, `VyralBot.Plugins`, `VyralBot.Scripting`).

---

## 0. What is actually in the "Chat Features" sidebar group

Sidebar definition: `src/VyralBot.UI/MainWindow.xaml:225-295`, expand state in `src/VyralBot.UI/ViewModels/MainViewModel.cs:86` (`_isChatFeaturesExpanded`) and `ToggleChatFeatures()` at `MainViewModel.cs:623-627`.

Items inside the group (in order):

| Sidebar item | Icon | Command binding | Destination |
|---|---|---|---|
| **Events & Alerts** | Bell | *(none — dead button, no Command binding)* | n/a |
| **Currency & Rewards** | CurrencyUsd | `NavigateToCurrencyCommand` | `CurrencyViewModel` (`MainViewModel.cs:797`) |
| **Commands** | CodeBraces | `NavigateToCommandsCommand` | `CommandsViewModel` (`MainViewModel.cs:828`) |
| **Event History** | History | `NavigateToEventHistoryCommand` | `EventHistoryViewModel` |

Notable placement facts (verified in `MainWindow.xaml`):
- "Events & Alerts" button (`MainWindow.xaml:251-259`) has **no Command binding** — clicking it does nothing.
- Several features the task expected under Chat Features actually live elsewhere in the sidebar:
  - **Conditional Rules** → under *Stream Tools* (`MainWindow.xaml:391`, `NavigateToConditionalRulesCommand`)
  - **Event Testing** → a standalone button after Settings (`MainWindow.xaml:464`, `NavigateToEventTestingCommand`)
  - **Variables / Auto-Replies** → no dedicated sidebar entry; Variables UI exists (`VariablesView.xaml` + `VariablesViewModel.cs`, `VariablePickerViewModel.cs`) but is reached from within other editors, not the sidebar.
- Navigation pattern: each `NavigateToX` lazy-creates the VM via DI, nulls `CurrentViewModel` first to release WPF references, calls `DisposeNonEssentialViewModelsAsync(excludeViewModel: ...)`, then sets `CurrentViewModel` (see `MainViewModel.cs:828-857` for Commands).

---

## 1. Commands

### 1.1 UI
- View: `src/VyralBot.UI/Views/CommandsView.xaml` (+ `.cs`) — header "Command System"; three tabs:
  - **Built-in Commands** (📦): read-only DataGrid (Command, Response, Permission) with inline toggles for Enabled / Kick / Twitch and a Usage counter (`CommandsView.xaml:54-145`).
  - **Custom Commands** (⚡): DataGrid (Command, Response, Permission, Cooldowns G/U, Status, Usage) plus an add/edit form whose Response field embeds the inline `VariablePicker` control (`CommandsView.xaml:295-302`) and hints "{username}, {platform}, {balance}, etc." (:295).
  - **Help** (❓): static reference listing the five variables (`{username}`, `{platform}`, `{channel}`, `{balance}`, `{role}`) and the permission hierarchy (`CommandsView.xaml:346-373`).
- ViewModel: `src/VyralBot.UI/ViewModels/CommandsViewModel.cs` (299 lines)
- Reached via sidebar Chat Features → Commands.

Capabilities exposed by `CommandsViewModel`:
- Two lists: `BuiltInCommands` and `CustomCommands` (split on `Command.IsBuiltIn`, `CommandsViewModel.cs:98-104`).
- Add/Edit custom command form: name, response, permission, global cooldown, user cooldown, enabled, per-platform enable (Kick/Twitch). Defaults for a new command: `GlobalCooldown = 5`, `UserCooldown = 30`, permission "Everyone" (`CommandsViewModel.cs:117-130`).
- Validation: name must be non-empty, must start with `!`, `?`, or `.`; response non-empty (`CommandsViewModel.cs:159-175`). Name is lower-cased on save.
- Built-in commands **cannot be edited or deleted** (`StartEditCommand`/`DeleteCommandAsync` guard on `IsBuiltIn`, `CommandsViewModel.cs:135,237`; also enforced server-side in `CommandService`).
- Toggle enable, toggle per-platform enable (`ToggleCommandAsync`, `TogglePlatformAsync`).
- Permission levels offered: `Everyone, Subscriber, VIP, Moderator, Broadcaster` (`CommandsViewModel.cs:57-64`).

### 1.2 Engine
- `src/VyralBot.Core/Services/Commands/CommandService.cs` (+ `ICommandService.cs`).
- **Command prefixes**: `!`, `?`, `.` (`CommandService.cs:15`, `IsCommand()` at :29). Message is a command iff first char is one of these.
- `ExecuteAsync(message, CommandContext)` flow (`CommandService.cs:64-140`):
  1. Parse: strip prefix, split; command name is normalized to `"!" + firstToken.ToLower()` (so `?socials` and `.socials` both resolve to `!socials`).
  2. Look up in DB (`IDataService.GetCommandByNameAsync`).
  3. Skip if disabled, or disabled for the message's platform (`EnabledOnKick` / `EnabledOnTwitch` — only Kick and Twitch flags exist; other platforms are not gated).
  4. Permission check (`HasPermission`, `CommandService.cs:248-259`): hierarchy `everyone < subscriber(sub) < vip < moderator(mod) < broadcaster`; each level passes if the user has that flag or higher (e.g. "subscriber" passes for VIP/mod/broadcaster). Unknown permission string => deny.
  5. Cooldown check (global then per-user) — on cooldown the command is **silently ignored**.
  6. Built-in special-casing (`ExecuteBuiltInCommandAsync`, `CommandService.cs:142-153`): only `!commands`, `!uptime`, `!watchtime` have code; `!socials`, `!discord` and everything else just return the stored `Response` template.
     - `!commands` → lists up to 10 enabled commands the user has permission for on that platform (`ExecuteCommandsListAsync`, :155-173).
     - `!uptime` → placeholder: "Stream uptime tracking not yet implemented. Coming soon!" (:175-180).
     - `!watchtime` → placeholder that returns the user's currency balance: `@{username}, you have {n} points. Watch time tracking coming soon!` (:182-195). (Real `!watchtime` handling exists separately in `WatchtimeCommandService`, see §1.4.)
  7. Variable replacement via `ParseVariables` (:325-353) → `IVariableReplacementService.ReplaceVariablesAsync` (called sync-over-async with `.GetAwaiter().GetResult()`).
  8. Applies cooldowns (writes `CommandCooldown` rows), increments `UsageCount`/`LastUsedAt` (`IncrementCommandUsageAsync`).
- `RegisterCommandAsync` refuses to register a custom command whose name collides with a built-in (`CommandService.cs:42-47`).

### 1.3 Data model & persistence
- Entity: `src/VyralBot.Data/Entities/Command.cs` — `Id, Name, Response, Permission ("Everyone"), GlobalCooldown, UserCooldown, IsEnabled, IsBuiltIn, EnabledOnKick, EnabledOnTwitch, UsageCount, LastUsedAt, CreatedAt, UpdatedAt`.
- Entity: `src/VyralBot.Data/Entities/CommandCooldown.cs` — per (CommandId, Username|null, Platform) expiry rows.
- Persistence: SQLite via Dapper, `src/VyralBot.Data/DapperDataService.cs` — `GetCommandsAsync`/`GetCommandByNameAsync`/`SaveCommandAsync`/`DeleteCommandAsync` (delete guarded by `AND IsBuiltIn = 0`, line 1152), `GetCommandCooldownAsync`/`SaveCommandCooldownAsync`, `IncrementCommandUsageAsync`. `Commands` table schema at :2924.
- **Seeded built-in commands** (`DapperDataService.cs:3172-3540`), inserted only when the table is empty; a second migration pass (:3554-3579) re-adds missing currency commands on existing DBs:
  - `!commands`, `!uptime`, `!socials`, `!discord`, `!watchtime` (chat/meta; templates reference `{commandList}`, `{uptime}`, `{twitter}`, `{discord}`, `{youtube}`, `{username}`, `{watchtime}`)
  - Currency set: `!points`, `!leaderboard`, `!gamble`, `!redeem`, `!rewards`, `!daily`, `!rank`, `!store`, `!slots`, `!duel`, `!accept`, `!heist`, `!roulette`, `!link`, `!give` (Moderator), `!take` (Moderator).
  - Note: the currency commands' seeded `Response` is just help text; actual behavior is implemented in `CurrencyCommandService` (see §1.4/§7) — the `Commands` table rows mostly act as discoverability/enable flags.

### 1.4 How commands enter the system (chat pipeline) — and a critical wiring gap
`src/VyralBot.UI/ViewModels/ChatViewModel.cs:589-800` — `OnChatMessageReceivedAsync(ChatMessageReceivedEvent)` (EventBus-driven) is the intended orchestrator:
1. Platform filter toggles per platform (`TwitchFilterEnabled`, `KickFilterEnabled`, `YoutubeFilterEnabled`, `TiktokFilterEnabled`, `DiscordFilterEnabled`).
2. If `ICommandService.IsCommand(msg)` → build `CommandContext` (username, platform, channel, mod/sub/VIP flags, broadcaster hard-coded `false` with a TODO, currency balance fetched via `ICurrencyService.GetBalanceAsync`), execute, and if a response is produced, send it back to the **originating platform only** (`_twitchService.SendMessageAsync` or `_kickService.SendMessageAsync`) and return.
3. Fallback chain for `!`-messages (each can also reply to Twitch/Kick):
   - `CurrencyCommandService.ProcessCommandAsync` (any `!` message),
   - `GiveawayCommandService.ProcessCommandAsync` (any `!` message; handles `!giveaway ...` and custom entry keywords),
   - `PollCommandService.ProcessCommandAsync` (`!poll*`),
   - `WatchtimeCommandService.ProcessCommandAsync` (`!watchtime*`).
4. Finally records watchtime activity (`WatchtimeService.RecordUserActivityAsync`).
Note: the unified command path (step 2) short-circuits with `return` when a DB command matched; the fallback services run in sequence otherwise (no `return` between them — currency, giveaway, poll, watchtime all get a chance per message).

Fallback service command surfaces (chat-features adjacent, defined outside `CommandService`):
- **Giveaway** (`Services/GiveawayCommandService.cs:35-90`): `!giveaway start|enter|winner|draw|cancel|status` (start/winner/cancel moderator-gated, with visible "Only moderators..." replies), plus a **custom entry keyword**: any `!<keyword>` matching the active giveaway's `EntryKeyword` registers an entry.
- **Poll** (`Services/PollCommandService.cs:35-70`): `!poll start|vote|results|end|cancel|status` (start/end/cancel moderator-gated).
- **Watchtime** (`Services/WatchtimeCommandService.cs:31-60`): `!watchtime` (own totals + this stream), `!watchtime @user`, `!watchtime top|leaderboard|lb [n≤10]`.
- **Currency** aliases: see §8.

**CRITICAL: this entire pipeline is dead in the current build.** The orchestrator subscribes to `ChatMessageReceivedEvent` (`ChatViewModel.cs:220`), but that event type is defined at `ChatViewModel.cs:2871` and **no code anywhere in the solution ever publishes it** (verified by symbol search across all projects; EventBus only exposes generic typed `PublishAsync<TEvent>`, `IEventBus.cs:16`). The real per-platform handlers — `OnTwitchChatMessageReceivedAsync` (`ChatViewModel.cs:503`), `OnKickChatMessageReceivedAsync` (:1611), `OnYouTubeChatMessageReceivedAsync` (:1527) — do echo-filtering and UI rendering only and never process commands, and `VyralBot.Integrations` contains no command processing. Consequently **no chat message can currently trigger `CommandService.ExecuteAsync` or any of the currency/giveaway/poll/watchtime command services**. The Commands page still manages its data, but `!` commands typed in chat do nothing.

---

## 2. Variables (template placeholder system)

### 2.1 Syntax & engine
- Placeholder syntax: `{name}` — matched by regex `\{([a-z0-9]+(-[a-z0-9]+)*)\}` case-insensitive; names must be kebab-case lowercase (`^[a-z0-9]+(-[a-z0-9]+)*$`).
- Engine: `src/VyralBot.Core/Services/Variables/VariableReplacementService.cs` (+ `IVariableReplacementService.cs`), registered singleton (`src/VyralBot.UI/App.xaml.cs:142`).
- `ReplaceVariablesAsync(template, contextVariables, platform)` (:38-71):
  - Loads DB variables (per-platform filter, cached 5 minutes in a `ConcurrentDictionary`, `CacheExpiryMinutes = 5`).
  - Merges context variables over DB variables (**context wins**, case-insensitive keys).
  - Recursive replacement with `MaxNestedDepth = 5` and circular-reference detection per pass (`ReplaceVariablesRecursiveAsync`, :73-115). Unresolvable placeholders are left as-is. On error the original template is returned.
- `InvalidateCacheAsync()` clears the cache — called by the Variables UI after every save/delete so edits apply immediately.
- Only DB variables that have a non-empty `Value` participate (system/runtime variables have `Value = null` and only work when supplied as context variables).

### 2.2 Full seeded variable catalog
Seeded in `src/VyralBot.Data/DapperDataService.cs:3598-3745` (only when `Variables` table is empty):

**System/context variables** (`IsSystem=1, IsEditable=0, Category="Global"`, no stored value — must be supplied by the caller's context):
`username`, `message`, `months` (sub months), `number` (generic: followers, gift subs...), `platform`, `viewers` (raids/hosts), `bits`, `reward` (channel-point reward name), `tier` (sub tier 1000/2000/3000), `gifter`, `raider`, `hoster`, `sender` (donations), `amount` (donations/tips), `balance` (currency balance), `role` (Broadcaster/Moderator/VIP/Subscriber/Viewer), `channel`, `uptime`, `watchtime`.

**Auto-updating platform variables** (`IsAutoUpdating=1, IsEditable=0, Value` synced from webhooks/API, category = platform):
`kick-followercount`, `kick-subscribercount`, `kick-viewcount`, `twitch-followercount`, `twitch-subscribercount`, `twitch-viewcount`.

**Editable social variables** (`IsEditable=1, Category="Social"`):
`twitter`, `discord`, `youtube`, `instagram`, `tiktok`, and `full-socials` (default value `Twitter: {twitter} | Discord: {discord} | YouTube: {youtube}`, flagged `ContainsNestedVariables=1`).

### 2.3 UI
- **Variables manager**: `src/VyralBot.UI/Views/VariablesView.xaml` + `ViewModels/VariablesViewModel.cs`. There is **no sidebar entry** — a `DataTemplate` maps `VariablesViewModel` → `VariablesView` (`MainWindow.xaml:812-813`) and the VM is DI-registered (`App.xaml.cs:205`), but no `NavigateToVariables` command exists in `MainViewModel` (verified: zero `Variables` navigation references). The page is effectively unreachable in the current UI.
  - Capabilities: list with category filter (`All` + DB categories) + text search on name/display name/description (`ApplyFilters`, :141-166); add/edit dialog for editable variables only (`StartEditVariable` refuses `!IsEditable`); delete guarded by `IsEditable || IsSystem` (:298). Saving enforces kebab-case names and duplicate checks, then invalidates the replacement cache.
- **Variable picker (inline insert)**: `Controls/VariablePicker.xaml` + `ViewModels/VariablePickerViewModel.cs`. Attached to **every TextBox app-wide** via the global TextBox style in `src/VyralBot.UI/App.xaml:48-49` → `Behaviors/TextBoxVariableBehavior.EnableVariablePicker=True` (adds a context-menu/insert affordance; behavior wires the picker to the focused `TextBox` at `TextBoxVariableBehavior.cs:107-137`). `SelectVariable` inserts `{name}` at the caret (`VariablePickerViewModel.cs:126-145`). Category inference in `DetermineCategory` (:156-181): `twitch-*`→Twitch, `kick-*`→Kick, `youtube-/trovo-/tiktok-`+`twitter/discord/full-socials`→Social, a hard-coded global list→Global, else Custom.

### 2.4 Consumers (where variables actually resolve)
- `CommandService.ParseVariables` (`Commands/CommandService.cs:325-353`): supplies `username, platform, channel, balance, role` + `CommandContext.CustomVariables` as context, then delegates to `VariableReplacementService` (sync-over-async).
- Manual chat send in `ChatViewModel` (`SendMessageAsync` paths at :934, :1018, :1093, :1187): if the typed message contains `{`, builds context via `BuildContextVariables` (:1332-1357: `bot-username`, `channel`, `platform`) and replaces before sending to Twitch/Kick/etc. (supports "All Platforms" send).
- `AutoReplyService.ProcessTemplate` (simple string replace, see §3).
- Alert/overlay and webhook paths (platform variable auto-updates) are outside this review's core scope but share the same `Variables` table.

---

## 3. Auto-Replies

Two distinct mechanisms share the "auto-reply" name:

### 3.1 Per-event auto-replies (the live mechanism — ChatEventSetting)
- Config lives on `ChatEventSetting` (`src/VyralBot.Data/Entities/ChatEventSetting.cs`): per `(Platform, EventType)` row with `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage` (template), plus sound settings (`SoundEnabled`, `SoundFile`, `SoundVolume`).
- Edited in the **platform chat settings pages** (Platforms → Twitch/Kick): `ViewModels/TwitchChatSettingsViewModel.cs`, `ViewModels/KickChatSettingsViewModel.cs`, `Views/TwitchChatSettingsView.xaml.cs`, `Views/KickChatSettingsView.xaml.cs`.
- Runtime: `Services/Chat/ChatEventSettingsService.cs` — cached read-through dictionary keyed `"{Platform}:{EventType}"` (`GetCacheKey`, :27), lazy-loaded once under a `SemaphoreSlim` (:32-64); `SaveSettingAsync`/`DeleteSettingAsync` write-through; `InvalidateCache()` forces reload. Helpers: `IsEventEnabledAsync` (ShowInChat), `IsAutoReplyEnabledAsync`, `GetAutoReplyMessageAsync`, `GetSoundSettingsAsync`.
- Event handler flow (e.g. Kick follow, `ChatViewModel.cs:1700-1783` `OnKickFollowAsync`):
  1. Load setting; if none → do nothing.
  2. If `ShowInChat` → system message in chat window.
  3. `_soundService.PlayEventSoundAsync(type, platform)` (sounds from Sounds Management).
  4. `ShowEventAlertAsync(...)` — visual alert overlay.
  5. `_currencyService.AwardEventBonusAsync(username, platform, "follow")` — currency reward hook.
  6. If `AutoReplyEnabled && AutoReplyMessage` → rate-limit check via `_autoReplyService.ShouldSendReply` then `ProcessTemplate` with `{username}`, `{platform}` (and event-specific vars) → send via the platform chat service.
- Event types wired to settings in `ChatViewModel`:
  - **Kick**: `Follow`, `Subscribe`, `GiftSub`, `Host`, `KicksGifted`, `RewardRedeemed`, `StreamStatus` (handlers at :1700, :1785, :1860, :1929, :1983, :2028, :2073)
  - **Twitch**: `Follow`, `Subscribe`, `Resub`, `GiftSub`, `Raid`, `Cheer` (handlers at :2089, :2158, :2239, :2307, :2354, :2423)

### 3.2 Conditional auto-reply rules engine (AutoReplyService — partially dormant)
- `src/VyralBot.Core/Services/Chat/AutoReplyService.cs` (+ `IAutoReplyService.cs`), registered `App.xaml.cs:110`.
- `GetAutoReplyAsync(username, platform, eventType, context)` (:61-127) is the full conditional engine: rate-limit check → load `AutoReplyRule` rows via `IDataService.GetAutoReplyRulesByPlatformAndEventAsync` (ordered by `Priority ASC`) → deserialize each rule's `ConditionJson` into `RuleCondition` → evaluate via `IRuleEvaluator.EvaluateCondition` → on match increment `TriggerCount` and return the processed `ReplyTemplate`. **No production caller invokes this method** — grep shows the only references are the interface, DI registration, and the service itself. The live path (§3.1) only uses `ShouldSendReply` + `ProcessTemplate`.
- `AutoReplyRateLimiter` (`Services/Chat/AutoReplyRateLimiter.cs`): per-user+platform+event cooldown, default 30 s (`AutoReplyService` constructs it with `TimeSpan.FromSeconds(30)`, `AutoReplyService.cs:26`); keys `"{platform}:{eventType}:{username}"`; `CleanupExpired()` prunes entries older than 10 minutes (`AutoReplyService.CleanupExpiredEntries` is meant to be called periodically but no timer wired in production code).
- `ProcessTemplate` is a naive `string.Replace` of `{key}` placeholders (case-insensitive) — does NOT use `VariableReplacementService`, so DB/social variables do not resolve in auto-reply templates; only the keys passed in the context dictionary do.
- Entity: `src/VyralBot.Data/Entities/AutoReplyRule.cs` — `Name`, `Platform` ("Twitch"/"Kick"/"Discord"/"All"), `EventType`, `IsEnabled`, `Priority`, `ConditionJson` (format `{"type":"value|status|time","field":"count","operator":"==","value":"100"}`), `ReplyTemplate`, `TriggerCount`, `LastTriggeredAt`. Table + indexes created in `DapperDataService.cs:2374-2391`; CRUD at :1205-1294. **No UI exists for editing AutoReplyRules** — no ViewModel references them.

---

## 4. Chat Events (settings summary)

The "Events & Alerts" sidebar button under Chat Features is dead (no command binding, `MainWindow.xaml:251-259`). The actual per-event configuration surface is:
- `ChatEventSettingsService` (see §3.1) — persisted via `DapperDataService` (`ChatEventSettings` table, `GetAllChatEventSettingsAsync`/`SaveChatEventSettingAsync`/`DeleteChatEventSettingAsync`).
- Twitch/Kick chat settings pages under the **Platforms** sidebar group.
- Event alert visuals (`ShowEventAlertAsync` → `AlertOverlayWindow`), sounds (`SoundService`), and currency bonuses (`CurrencyService.AwardEventBonusAsync`) all hang off these event handlers.
- Root docs describing the design: `CHAT_EVENTS_IMPLEMENTATION_PLAN.md`, `USER_GUIDE_CHAT_EVENTS.md`.

---

## 5. Conditional Rules (Auto-Reply Rules UI)

### 5.1 UI
- View: `src/VyralBot.UI/Views/ConditionalRulesView.xaml` (header literally reads "Auto-Reply Rules"); ViewModel: `ViewModels/ConditionalRulesViewModel.cs`.
- Lives in the sidebar under **Stream Tools** (not Chat Features): `MainWindow.xaml:391` → `NavigateToConditionalRulesCommand`.
- The UI edits **`AutoReplyRule`** rows (same entity/table as §3.2) via `IDataService.GetAutoReplyRulesAsync` / `SaveAutoReplyRuleAsync` / `DeleteAutoReplyRuleAsync`.
- Editor fields: Name, Platform (`All`, `Twitch`, `Kick`, `Discord`), Event Type, Enabled, Priority (lower = higher priority), Condition JSON, Reply Template. Condition builder: Type + Field + Operator + Value, appended into the JSON; a second condition wraps the previous root into a `Complex` condition with **AND** logic (`AddCondition`, `ConditionalRulesViewModel.cs:207-265`). Raw JSON is directly editable.
- Offered lists (`ConditionalRulesViewModel.cs:64-82`):
  - Event types: `Follow, Subscription, Raid, Host, Bits, Donation, SubscriptionGift, ChatMessage, ChannelPointRedemption`
  - Condition types: `Value, Status, Time, Complex`
  - Operators: `Equals, NotEquals, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, Contains, StartsWith, EndsWith, Modulo`
- View help text examples (`ConditionalRulesView.xaml:215-217`): milestone `count % 100 == 0`, `is_subscriber` status check, `time_of_day >= 20:00`. Template variables advertised: `{username}, {platform}, {count}, {amount}` (:227).
- "Test Rule" (`TestRuleAsync`, :273-300) only shows a MessageBox summary — it does **not** evaluate the rule against sample data.

### 5.2 Condition engine (fully implemented)
- Model: `src/VyralBot.Core/Models/Rules/RuleCondition.cs` — `Type` (`Value|Status|Time|Complex`), `Field`, `Operator`, `Value`, `ModuloDivisor` (for milestone checks), recursive `SubConditions` + `LogicalOperator` (`And|Or`). Enums serialize as JSON strings.
- Evaluator: `src/VyralBot.Core/Services/Rules/RuleEvaluator.cs` (`IRuleEvaluator`, registered `App.xaml.cs:109`):
  - **Value**: numeric compare first (int/long/float/double/decimal with epsilon 0.0001 for ==/!=), else string compare (case-insensitive Equals/NotEquals/Contains/StartsWith/EndsWith). Missing context field => false.
  - **Modulo**: for int fields with `ModuloDivisor`, remainder compared to `Value` — milestone detection (`count % 100 == 0`).
  - **Status**: boolean field parsed against `Value` ("true"/"false") with Equals/NotEquals semantics.
  - **Time** (local time): fields `time_of_day` ("HH:mm", `Equals` = within 1 minute), `day_of_week` (day name), `hour` (int), `date` (date compare).
  - **Complex**: recursive `All` (And) / `Any` (Or) over `SubConditions`.
- **Runtime status**: the evaluator is only invoked from `AutoReplyService.GetAutoReplyAsync` (§3.2), which has no production caller. So rules created in this UI are persisted but never fire — the page is write-only in the current build.

### 5.3 Trigger Engine + Action Dispatcher (scaffolding, largely stubs)
- Entities exist: `src/VyralBot.Data/Entities/Trigger.cs` (`EventType`, `Platform`, `Condition`, `CooldownSeconds`, `Permission`, `ActionSequence` = JSON array of action IDs, `FireCount`, `LastFiredAt`) and `Entities/BotAction.cs` (`ActionType` e.g. "SendMessage", "Delay", "HttpRequest", "RunScript", `Configuration` JSON, `TimeoutSeconds`, `RetryOnFailure`, `MaxRetries`). No UI edits these; no service reads them.
- `src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs`: started at app boot (`App.xaml.cs:308-309`) and subscribes to **all** `IEvent` on the EventBus, but `OnEventReceivedAsync` (:126-139) is a TODO stub that only logs; `ReloadTriggersAsync` (:112-121) hard-sets `ActiveTriggerCount = 0`; `FireTriggerAsync` (:84-107) dispatches a placeholder action GUID.
- `src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs`: `ExecuteActionAsync` (:26-65) simulates work with `Task.Delay(100)` and a 30 s linked-CTS timeout (TODO: load action from DB); `ExecuteSequenceAsync` (:71-97) runs actions in order, stop-on-first-failure; `CancelTriggerActions` (:102-119) cancels all running actions (trigger→action mapping not tracked yet). `ActionContext` (`src/VyralBot.Core/Models/ActionContext.cs`) carries `TriggerId`, `Platform`, `Username`, variable dictionary, cancellation token.
- Conclusion: the trigger→condition→action pipeline advertised by these classes is not wired to real behavior; the *effective* automation paths in the app are the per-event handlers in `ChatViewModel` (§3.1/§4) and the command pipeline (§1.4).

---

## 6. Event History

- UI: `src/VyralBot.UI/Views/EventHistoryView.xaml` + `ViewModels/EventHistoryViewModel.cs`. Sidebar: Chat Features → Event History.
- Data: `EventStatistics` table via `IDataService.GetEventStatisticsAsync()` (entity: `src/VyralBot.Data/Entities/EventStatistic.cs` — `Platform`, `EventType`, `Username`, `EventData` JSON, `Timestamp`, `WasAutoReplied`).
- Capabilities:
  - Filters: platform (`All/Kick/Twitch`), event type (`All, Follow, Subscribe, Resub, GiftSub, Raid, Host, Cheer, KicksGifted, RewardRedeemed, StreamStatus`), username substring, date range (default last 7 days) — all applied **in memory** after fetching the full list (`EventHistoryViewModel.cs:98-133`).
  - Client-side pagination (page size 50, next/previous).
  - "Details" (`ViewDetails`, :193-214) shows a MessageBox with the record + pretty-printed `EventData` JSON.
  - "View auto-reply" (`ReplayAutoReplyAsync`, :216-285): loads the `ChatEventSetting` for that platform/event, substitutes `{username}`, `{platform}`, `{eventtype}` plus `count/amount/viewers/bits/months` from `EventData`, and shows the result in a MessageBox. Despite the command name it does **not** re-send anything to chat.
- **Critical gap**: nothing in the codebase ever calls `SaveEventStatisticAsync` (only the interface, `DapperDataService.cs:264`, and the `ProfileAwareDataService.cs:110` wrapper implement/forward it — zero domain callers). The `WasAutoReplied` flag therefore can never be set, and the page will show no rows unless rows are inserted externally. `StatisticsService` (`Services/Statistics/StatisticsService.cs`) reads the same table for the Statistics page.

---

## 7. Event Testing

- UI: `src/VyralBot.UI/Views/EventTestingView.xaml` + `ViewModels/EventTestingViewModel.cs`. Sidebar: standalone "Event Testing" button after Settings (`MainWindow.xaml:464`), not inside Chat Features.
- Purpose: developer tool that publishes synthetic events onto the shared `IEventBus` so all real handlers (ChatViewModel event handlers → chat notification, sound, visual alert, currency bonus, auto-reply) run exactly as for live events.
- Inputs: `TestUsername` (default "TestUser123"), `TestMessage` (default "This is a test message"), plus a timestamped output log (`AddLog`).
- Test event buttons (each publishes the corresponding integration event class from `VyralBot.Integrations.Kick.Events` / `VyralBot.Integrations.Twitch.Events`):
  - **Kick** (`:47-207`): Follow (`FollowerCount=42`), Subscribe (`MonthsSubscribed=3`), GiftSub (`Count=5` + 5 recipient names), Host (`ViewerCount=150`), KicksGifted (`Amount=1000`), RewardRedeemed (`RewardName="Test Reward"`, `Cost=500`).
  - **Twitch** (`:213-386`): Follow, Subscribe (`Tier="1000"`, `MonthsTotal=1`), Resub (`CumulativeMonths=6`, `StreakMonths=4`), GiftSub (`Total=5`, `CumulativeTotal=25`), Raid (`Viewers=200`), Cheer (`Bits=100`).
- Because auto-reply is rate-limited per user/platform/event (30 s, §3.2), repeated clicks of the same test button get suppressed by the rate limiter — visible only in logs.

---

## 8. Currency & Rewards (Chat Features group member — interaction focus)

Full currency internals are another reviewer's scope; documented here because it is a Chat Features sidebar item and is deeply intertwined with chat commands/events.

- UI: `Views/CurrencyView.xaml` + `ViewModels/CurrencyViewModel.cs` (sidebar Chat Features → Currency & Rewards, `MainViewModel.cs:797`). Tabs/areas: settings (`CurrencySetting` edit), leaderboard, custom rewards CRUD (`NewRewardName/Description/Cost/RequiresApproval/Cooldown`), redemptions (approve/deny), transactions. Root doc: `currency-and-reward-system.md`.
- **Chat command surface** — `Services/Currency/CurrencyCommandService.cs:53-78`, invoked from the ChatViewModel fallback chain (§1.4). Command aliases:
  - `!points` / `!balance` / `!currency` (optional `@user`), `!leaderboard` / `!top` `[n≤10]`
  - `!give` / `!take` (moderator only), `!redeem <RewardName> [input]`, `!rewards`
  - `!gamble` / `!bet`, `!daily`, `!rank` / `!tier`, `!store` / `!shop`, `!slots`, `!duel`, `!accept`, `!heist`, `!roulette`, `!link <platform> [code]`
  - `!boost` (mod), `!bid`, `!auction` (mod), `!deposit`, `!withdraw`, `!bank`
  - Moderator-gated commands return "You must be a moderator to use !x" rather than silence (unlike `CommandService` which silently ignores).
- **Reward redemption flow** (`Services/Currency/CurrencyRewardsService.cs:48-106`): `CanRedeemAsync` (cost, cooldown, max redemptions, approval) → deduct currency (`RemoveCurrencyAsync` with reason "Spend") → write `CurrencyRedemption` (`Status = Pending` when `RequiresApproval`, else `Approved`) → increment `CurrentRedemptions`. Approval workflow via `ApproveRedemptionAsync`.
- **Event bonuses** (chat events → currency): `CurrencyService.AwardEventBonusAsync` (`Services/Currency/CurrencyService.cs:295-350+`) — per-event-type amounts gated by `CurrencySetting` flags: `follow` (FollowBonusAmount, default 100), `subscribe/sub` (500), `giftsub(s)` (50/sub × count), `raid` (10/viewer × viewers), `cheer/bits` (1/bit × bits), `host/shoutout` (HostBonusAmount 200). Called from ChatViewModel event handlers (§3.1 step 5).
- **Settings that affect chat features** (`Entities/CurrencySetting.cs`): currency name/symbol (used in command responses), watch-time earn (`WatchTimeInterval` 60 s / `WatchTimeAmount` 10), chat-activity earn (`ChatActivityCooldown` 120 s / `ChatActivityAmount` 5), event bonus toggles/amounts above, role multipliers (Sub 2.0 / VIP 1.5 / Mod 1.5), first-chat bonus (50), daily bonus (100) + streak tiers (day1 50 / day5 100 / day10 250), per-platform earn multipliers (Twitch/Kick/YouTube), decay, banking, transfer tax.

---

## 9. Cross-cutting interactions summary (chat features ↔ rest of app)

- **Chat in → responses out**: every `!`/`?`/`.` message on any connected platform hits `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:589`); responses go back to the **originating** platform only (Twitch/Kick send paths). Unified `CommandService` first, then currency/giveaway/poll/watchtime command services in sequence.
- **Events → chat/sound/alert/currency**: platform events (EventBus) → per-event ChatViewModel handler → 4 side effects gated by `ChatEventSetting`: chat system message, `SoundService.PlayEventSoundAsync` (Sounds Management library), `ShowEventAlertAsync` (AlertOverlayWindow / Visual Alerts), `CurrencyService.AwardEventBonusAsync` (Currency & Rewards) — plus optional auto-reply through the 30 s rate limiter.
- **Variables** are the shared templating layer for command responses and manual chat sends; alert overlays get their data from event properties rather than this service.
- **Profile persistence**: all tables (Commands, CommandCooldowns, Variables, AutoReplyRules, ChatEventSettings, EventStatistics, Currency*) are per-profile SQLite DBs accessed through `ProfileAwareDataService` (`src/VyralBot.UI/Services/ProfileAwareDataService.cs`) which forwards to the active profile's `DapperDataService`; profile switches raise `ProfileSwitchedEvent` and caches (variables, chat event settings) are invalidated accordingly.
- **Webhooks/EventBus**: Kestrel webhook controller publishes events onto the same shared EventBus the UI subscribes to (`App.xaml.cs:313-316`), so webhook-sourced events (Kick follows etc.) drive the same auto-reply/sound/alert/currency pipeline; auto-updating variables (`kick-followercount`, etc.) are synced from webhook data.

## 10. Notable findings / dead code

1. **Chat commands never fire** — `ChatMessageReceivedEvent` (the event the entire command-processing handler subscribes to) is defined at `ChatViewModel.cs:2871` and published by nothing (see §1.4). Applies to unified commands AND currency/giveaway/poll/watchtime chat commands.
2. "Events & Alerts" sidebar button has no command binding — dead UI (`MainWindow.xaml:251-259`).
3. `VariablesView` manager page has a DataTemplate + DI registration but no navigation path — unreachable (`MainWindow.xaml:812`, no `NavigateToVariables` in `MainViewModel`).
4. `AutoReplyService.GetAutoReplyAsync` (conditional rules evaluation) has no callers; Conditional Rules UI writes rules nothing reads. The live auto-reply path uses only `ShouldSendReply` + `ProcessTemplate` of that service.
5. `TriggerEngine`/`ActionDispatcher`/`Trigger`/`BotAction` are stubs (TODOs, `Task.Delay(100)` simulation, `ActiveTriggerCount = 0`) despite being started at boot.
6. `EventStatistics` is never written (`SaveEventStatisticAsync` has zero domain callers) — Event History can only ever be empty.
7. `!uptime` and `!watchtime` built-ins in `CommandService` return "coming soon" placeholders; the seeded enabled `!watchtime` row would shadow `WatchtimeCommandService`'s real implementation whenever the pipeline is re-wired (unified path returns first).
8. Cooldown violations are silently ignored (no user feedback) by design comment in `CommandService.cs:107`.
9. `CommandContext.IsBroadcaster` is hard-coded `false` in the ChatViewModel pipeline (`ChatViewModel.cs:629`) — broadcaster-only commands could never fire from chat.
10. Auto-reply templates only resolve context keys passed by the handler (`{username}`, `{platform}`, event fields) — DB/social variables are NOT resolved there (simple `string.Replace` in `ProcessTemplate`), unlike command responses.
11. Auto-reply rate limiter cleanup (`CleanupExpiredEntries`) is never scheduled; stale keys accumulate for the session (they still expire functionally because timestamps are compared).
12. Command permissions differ between engines: `CommandService` silently drops unauthorized/cooled-down users, while currency/giveaway/poll services reply with explicit "Only moderators can ..." messages.
13. Platform gating in the `Command` entity is Kick/Twitch only (`EnabledOnKick`/`EnabledOnTwitch`); there are no flags for YouTube/TikTok/Discord, and command responses are only ever sent back to Twitch or Kick.
14. What DOES work live from chat events: per-event auto-replies, sounds, visual alerts, currency event bonuses, watchtime activity tracking (`ChatViewModel` Kick/Twitch event handlers, §3.1/§4) — these subscribe to real, published platform events.

## 11. Related docs in-repo

- `CHAT_EVENTS_IMPLEMENTATION_PLAN.md`, `USER_GUIDE_CHAT_EVENTS.md` (chat events design/usage)
- `currency-and-reward-system.md` (command-triggered rewards, event bonuses)
- `docs/features/chat-system.md`, `docs/features/event-system.md`
- `docs/services/trigger-engine.md`, `docs/services/action-dispatcher.md`, `docs/models/trigger-and-action-entities.md`, `docs/models/action-context-model.md`
- `docs/architecture/event-bus.md`, `docs/architecture/data-layer.md`
- `plan-management.md` (profile/plan persistence context)

*Review generated from static code reading; all file/line references verified against the working tree at review time.*

