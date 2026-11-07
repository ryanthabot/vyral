# Discord Bot Detail (per-bot dashboard)

- **View:** `src/VyralBot.UI/Views/DiscordBotDetailView.xaml` (+ `.cs` — also
  hosts the full-size visual command builder overlay)
- **ViewModel:** `src/VyralBot.UI/ViewModels/DiscordBotDetailViewModel.cs`
  (DI singleton, reused for every bot; loaded per navigation via
  `LoadAsync(botId)` from `MainViewModel`)
- **Runtime doc:** `services/discord-bot-runtime.md` (manager, events,
  welcome/auto-role handler)

## Cards (right column)

| Card | What it does |
|---|---|
| Connection | Start/Stop/Reconnect/Refresh + live status dot |
| Identity (auto-detected) | ApplicationId / BotUsername / avatar detected at connect (read-only) |
| Bot roles | Single-select linking/giveaway checkboxes (`IDiscordBotManager.AssignRoleAsync`) |
| Startup | Per-bot `AutoStart` flag (load/flip/save via `UpdateBotAsync`) |
| Welcome & Auto-Role | See below |
| Test Message | `SendOwnerTestMessageAsync` to a private test channel |
| Commands | Built-in mod commands (read-only) + this bot's custom commands; EDIT opens the visual builder overlay |
| Integrations | `DiscordBotIntegrationsCard` control (external services + conflicts) |

## Welcome & Auto-Role card

Per-bot "welcome message + auto-role on join", applied live by the manager's
`DiscordMemberJoinedEvent` handler (see the runtime doc for the wiring).

- **Enable toggle** → `WelcomeEnabled` master switch.
- **Message TextBox** → `WelcomeMessage` template; `{user}` (mention),
  `{username}`, `{server}` are replaced on join. Inherits the app-wide
  `TextBoxVariableBehavior` variable-picker context menu.
- **Channel picker** → `WelcomeChannelId` from the bot's FIRST guild
  (`WelcomeChannels`, rebuilt by `LoadWelcomeAsync` on page load and after
  Start/Reconnect); an empty id ("(no welcome channel)") clears the pick.
- **Role picker** → `WelcomeRoleId` from
  `IDiscordService.GetGuildRolesAsync(guildId)` (@everyone and managed roles
  skipped — they cannot be a valid auto-role); "(no auto-role)" clears it.
- **SAVE** → `SaveWelcomeSettingsAsync` persists a `DiscordBotWelcomeSettings`
  through `IDiscordBotManager.SaveWelcomeSettingsAsync` (Configs key
  `DiscordBot.{botId}.WelcomeSettings`). A null selection (bot offline,
  pickers unavailable) keeps the previously stored id instead of wiping it.

## Conventions kept

- MaterialDesign dark cards, `DynamicResource` brushes, panel headers like
  `RestreamView`; pickers disabled (`HasWelcomeOptions`) while the bot is
  offline with a muted hint line.
- No `Process.Start` (invite link goes through `IProcessManager`);
  info-level logging; no `.Result`/`.Wait()`.
