# Settings View

## Overview
XAML view for application settings with profile management, platform configuration, web server settings, and variables management.

## Source Files
- **XAML:** `src/VyralBot.UI/Views/SettingsView.xaml`
- **Code-behind:** `src/VyralBot.UI/Views/SettingsView.xaml.cs`
- **DataContext:** `SettingsViewModel`

## Features
- Category sidebar (Profile, Platforms, etc.)
- Profile CRUD (create, switch, delete)
- PIN entry dialogs
- Platform connection grid
- Web server settings (via WebServerSettingsViewModel)
- Variables management (via VariablesViewModel)
- Notifications panel: master toggles for event messages in chat (`Notifications.ShowEventsInChat`), event sounds (`Notifications.EventSoundsEnabled`), do-not-disturb/focus mode (`Notifications.DoNotDisturb`, mutes event sounds + overlay alerts), and a Preview Notification button — see `docs/features/settings-system.md`
- Stream FX panel: lights provider (Hue/Webhook) with bridge Discover/Pair, flash defaults, Stream Deck action links, and the `!flashlights`/`!lightson`/`!lightsoff`/`!streamdeck` command reference — see `docs/services/stream-effects-lights.md`. The Hue app key and webhook bearer token are masked PasswordBoxes driven from code-behind (`HueAppKeyBox_PasswordChanged`, `WebhookTokenBox_PasswordChanged`), like the PIN boxes.

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/Views/SettingsView.xaml` | Settings view XAML |
| `src/VyralBot.UI/Views/SettingsView.xaml.cs` | Code-behind |
