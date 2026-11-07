# Discord Dashboard (DASHBOARD tab, Bots page)

- **View:** `src/VyralBot.UI/Views/DiscordDashboardView.xaml` (+ `.cs`)
- **ViewModel:** `src/VyralBot.UI/ViewModels/DiscordDashboardViewModel.cs` (DI singleton, registered in `App.xaml.cs`)
- **Host:** `Views/DiscordBotsView.xaml.cs` creates the view lazily behind the
  DASHBOARD header toggle and swaps visibility between the bot list and this
  dashboard (`DashboardHost` ContentControl). The VM is resolved from DI there
  because the dashboard has no navigation path of its own.

## What it shows (all real data, no new backend)

| Section | Data source |
|---|---|
| KPI strip — bots online, servers, text channels, commands (+uses), workflow runs today (+failed) | `IDiscordBotManager.Connected`/`LoadBotsAsync`, `IDataService.GetCommandsAsync`, `IDataService.GetWorkflowExecutionsAsync` |
| Bot Fleet — per-bot card: live status dot (green/red/muted), status text, uptime, identity username, servers + channel counts + guild names, command inventory (usable/pinned/uses/last used), role chips (linking/giveaway) | `IDiscordBotManager.Get(botId)` live service (`IsConnected`, `ConnectedGuilds`, `BotUsername`) + `DiscordBotConfig` fallback; Commands table `BotId`/`UsageCount`/`LastUsedAt` |
| Servers (setup bot) — guilds, member counts, per-guild active-channel picker, invite-link empty state | legacy `IDiscordService` (`ConnectedGuilds`, `SelectedChannelId`, `SetSelectedChannelAsync`) |
| Connection (setup bot) — status dot, Connected-as text, Reconnect/Disconnect/Refresh, Send Test Message | `IDiscordService` + Discord Platform row (`GetPlatformByNameAsync("Discord")`) |
| Recent Mod Actions — top 5: action chip, mod → target, relative time, details tooltip | `IDataService.GetModActionsAsync` (platform-agnostic audit log) |
| Workflow Runs Today — top 5: workflow name, trigger summary, duration, status dot (Completed/Failed), relative time | `IDataService.GetWorkflowExecutionsAsync(null, 200)` filtered to today locally |

Uptime is **session-scoped**: the VM records when it first saw each bot
connected (`_connectedSince`); nothing persisted that timestamp before, so the
label reads "since the dashboard first saw this bot connected in this app
session" (see the XAML tooltip).

## Refresh lifecycle

- 30s `DispatcherTimer` in the VM (`StartAutoRefresh`/`StopAutoRefresh`),
  tick does a guarded full data reload (`Interlocked` re-entrancy guard —
  overlaps with `IDiscordBotManager.BotsChanged` hops are dropped).
- The view hooks `IsVisibleChanged`: shown → timer start + immediate refresh;
  hidden (host toggle collapses `DashboardHost`) or page `Unloaded` → timer
  stop. `Loaded/Unloaded` alone would miss the tab toggle because a collapsed
  element stays loaded.
- `BotsChanged` (background thread) dispatcher-hops to refresh the fleet
  immediately on start/stop/error — same pattern as `DiscordBotsViewModel`.
- `UpdateFromService` is signature-guarded so the 30s tick never rebuilds the
  guild list while the user has an unapplied channel-picker selection.

## Empty states

Every list has one: fleet ("No bots yet — go back to BOTS…"), servers
("No servers yet" + OPEN INVITE LINK), mod actions, workflow runs today.

## Conventions kept

- MaterialDesign dark cards (`CardBackgroundBrush`, `TextPrimaryBrush`,
  `DiscordBrush`, `SuccessGreenBrush`, `ErrorRedBrush`, `TextMutedBrush`),
  panel headers like `RestreamView`.
- The invite link opens through `IProcessManager.StartProcessAsync`
  (zero-orphans golden rule) — never a raw `Process.Start`.
- VM implements `IDisposable` (stops timer, unhooks `BotsChanged`);
  handlers are named methods so unsubscription actually works.
