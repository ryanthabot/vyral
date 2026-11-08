# Tools Hub

> A second, larger card grid reached from the Home **Tools** card — a visual duplicate of the sidebar's grouped navigation — **plus the Death Counter card** (1.1.0): a big live count, +/−/set/reset buttons, the OBS widget URL, and the change history, all sharing state with the `!death` chat commands.

## Overview

The **Tools** page is a launcher *and* one real tool. It renders two labeled rows of cards — **Chat Features** (Events & Alerts, Currency & Rewards, Commands, Event History) and **Stream Tools** (Audio Settings, Sounds, Statistics, Visual Alerts, Conditional Rules, Giveaways, Polls, Watchtime) — and every card simply navigates to the same page the corresponding sidebar button opens. Above the grid sits the **Death Counter** card (added 1.1.0): the current count in huge type, `+1` / `−1` / **Set** / **Reset** buttons, a chat-command hint ("Chat: !death (count) • mods: !death+, !death-, !death set <n>, !death reset"), a copyable **OBS widget URL**, an editable label, and the last changes (first 10, newest first — "No changes yet" until the first one). The card, the `!death` chat command family, and the [/counters/death overlay](../stream-tools/browser-sources.md) all read and write the same two config rows, so a mod's `!death+` in chat bumps the card within moments (the service's `CountChanged` event reloads it).

You reach it from the **Tools** card on the [Home dashboard](dashboard.md) ("Stream tools, chat features, and more" — `MainWindow.xaml:581-607`). The sidebar has no Tools entry; the Home card is the only way in.

## User Guide

Click the **Tools** card on Home. The page shows a title, a "← BACK TO HOME" button, the Death Counter card, and the card grid. Click any card to navigate; the icon labels match the sidebar names one-for-one.

| Row | Card | Opens |
|---|---|---|
| Chat Features | **Events & Alerts** | The [Alert Settings](../stream-tools/visual-alerts.md) page |
| Chat Features | **Currency & Rewards** | [Currency](../stream-tools/currency.md) |
| Chat Features | **Commands** | [Commands](../chat-features/commands.md) |
| Chat Features | **Event History** | [Event History](../chat-features/event-history.md) |
| Stream Tools | **Audio Settings** | [Audio](../stream-tools/audio.md) |
| Stream Tools | **Sounds** | [Sounds](../stream-tools/sounds.md) |
| Stream Tools | **Statistics** | [Statistics](../stream-tools/statistics.md) |
| Stream Tools | **Visual Alerts** | The same [Alert Settings](../stream-tools/visual-alerts.md) page |
| Stream Tools | **Conditional Rules** | [Conditional Rules](../chat-features/conditional-rules.md) |
| Stream Tools | **Giveaways** | [Giveaways](../stream-tools/giveaways.md) |
| Stream Tools | **Polls** | [Polls](../stream-tools/polls.md) |
| Stream Tools | **Watchtime** | [Watchtime](../stream-tools/watchtime.md) |

Two things worth knowing before you rely on the grid:

- **"Events & Alerts" and "Visual Alerts" are the same destination.** Both cards bind `NavigateToAlertSettingsCommand` (`ToolsView.xaml:59` and `174`) and land on the Alert Settings page — there is no separate events page behind the first card.
- **The sidebar's identically-named buttons work too.** Sidebar Events & Alerts and sidebar Browser Sources were wired on 2026-08-15 (to Alert Settings and Audio Settings respectively), so every sidebar entry now navigates; this hub remains the larger, duplicate launcher.

The grid does *not* include a Browser Sources card — that destination is the sidebar button, which routes to the [Audio Settings](../stream-tools/audio.md) page. Browser-source functionality is documented under [Browser Sources](../stream-tools/browser-sources.md) and [Audio](../stream-tools/audio.md).

### The Death Counter card

- **The count** — the current value in 56 px type, with your label under it ("Deaths" by default — the label edit box is right there; changes persist debounced as `DeathCounter.Label` and rebuild the widget URL live).
- **Change it** — **+1** and **−1** buttons, a **Set** box + button (any value 0–999,999), and **Reset** (back to 0, no confirmation — every change is logged and lands in the history below).
- **The chat commands** — the same counter is driven from chat: `!death` (everyone), `!death+` / `!death-` / `!death set <n>` / `!death reset` / `!death add <n>` (mods). Full reference in the [command catalog](../reference/command-catalog.md#death-counter-commands).
- **OBS widget** — copy the URL (e.g. `http://localhost:5000/deathcounter.html?label=Deaths`) and add it as an OBS **Browser Source**; it polls the count every 3 s and restyles itself from URL params (`label`, `size`, `color` — see [Browser Sources → Death counter](../stream-tools/browser-sources.md#death-counter-deathcounterhtml)). The port follows Web Server settings (default 5000).
- **History** — the last changes (newest first, up to 10 shown; the store keeps 50): who changed it, by how much, and when.

## How It Works (Developer)

[ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) owns the **Death Counter card's** state (1.1.0 — the VM was empty before that): it injects `IDeathCounterService` + `IDataService`, loads the count/history/label and the web-server port in `InitializeAsync`, subscribes to the service's `CountChanged` event so chat-side `!death+` bumps the display, and rebuilds the widget URL whenever the label changes. Mutations from the card go through the same service methods the chat commands use — one code path, one semaphore-guarded store.

The card grid below is still pure XAML: each card is a `materialDesign:Card` with a `MouseBinding` that reaches **up through the window** to invoke a command on `MainViewModel` — the page's own DataContext is only used by the death counter. This is the same pattern the Home page cards use:

```xml
<!-- ToolsView.xaml:56-60 — the card has no own logic, just a borrowed command -->
<materialDesign:Card Grid.Column="0" Style="{StaticResource ToolCardButton}">
    <materialDesign:Card.InputBindings>
        <MouseBinding Command="{Binding DataContext.NavigateToAlertSettingsCommand,
                                        RelativeSource={RelativeSource AncestorType=Window}}"
                      MouseAction="LeftClick"/>
    </materialDesign:Card.InputBindings>
    ...
```

Consequence: the hub's grid remains a **static duplicate of sidebar navigation**. If a sidebar command's target changes, the card follows automatically (same command), but the card list itself is hard-coded XAML — adding a tool means editing both `MainWindow.xaml` and `ToolsView.xaml`. The death counter, by contrast, is real state on the page's own VM.

## Files & Code

| File | Role |
|---|---|
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | The whole hub: the Death Counter card (:258-375) plus two rows, twelve cards, each a `MouseBinding` to a `MainViewModel` command |
| [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | Death Counter card state: count, label, history, widget URL, CountChanged subscription (1.1.0) |
| [DeathCounterService.cs](../../src/VyralBot.Core/Services/Counters/DeathCounterService.cs) | The shared counter (config rows + history + `CountChanged`), driven by card and `!death` commands alike |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToToolsAsync` plus the eleven target navigation commands the cards invoke |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Home page Tools card, the VM→View DataTemplate, and the sidebar buttons the grid mirrors |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | Registers `ToolsViewModel` as a singleton |

## Data & Settings

| Key | Store | Meaning |
|---|---|---|
| `DeathCounter.Count` | Configs (per-profile) | The count itself (plain int, 0–999,999). |
| `DeathCounter.History` | Configs | JSON, newest-first, capped at 50 `{ at, delta, by }` events. |
| `DeathCounter.Label` | Configs | The card/overlay label (default "Deaths"). |

The card grid itself persists nothing and reads nothing.

## Status & Known Issues

- **Works**: all twelve cards navigate correctly; they reuse the exact `MainViewModel` commands the sidebar uses, so behavior is identical to the sidebar entries.
- **Death counter is one store, three surfaces** — the card, the `!death` chat family, and the OBS widget all read/write the same config rows (chat changes reach the card via `CountChanged`; the widget polls every 3 s).
- **Duplicate naming, same destination** — "Events & Alerts" and "Visual Alerts" both open Alert Settings; there is no distinct events page.
- **Sidebar parity** — the sidebar's Events & Alerts and Browser Sources buttons were wired on 2026-08-15, so the former sidebar/hub asymmetry is gone; the hub still omits a Browser Sources card (the sidebar button routes to Audio Settings).
- No keyboard access for the grid cards: they are mouse-only (`MouseBinding`), unlike sidebar buttons which are real focusable `Button`s. The death counter's +/−/set/reset are real buttons.
