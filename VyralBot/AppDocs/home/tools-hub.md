# Tools Hub

> A second, larger card grid reached from the Home **Tools** card: twelve working shortcuts into the chat-feature and stream-tool pages — a visual duplicate of the sidebar's grouped navigation, driven entirely by `MainViewModel` commands.

## Overview

The **Tools** page is a launcher, not a tool. It renders two labeled rows of cards — **Chat Features** (Events & Alerts, Currency & Rewards, Commands, Event History) and **Stream Tools** (Audio Settings, Sounds, Statistics, Visual Alerts, Conditional Rules, Giveaways, Polls, Watchtime) — and every card simply navigates to the same page the corresponding sidebar button opens. Nothing on this page owns any state; it exists to give the Home dashboard's "Tools" card somewhere to land.

You reach it from the **Tools** card on the [Home dashboard](dashboard.md) ("Stream tools, chat features, and more" — `MainWindow.xaml:581-607`). The sidebar has no Tools entry; the Home card is the only way in.

## User Guide

Click the **Tools** card on Home. The page shows a title, a "← BACK TO HOME" button, and the card grid. Click any card to navigate; the icon labels match the sidebar names one-for-one.

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

## How It Works (Developer)

[ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) is an **empty class**, exactly like the Integrations hub's:

```csharp
public partial class ToolsViewModel : ViewModelBase
{
}
```

It exists purely as the DataTemplate key (`MainWindow.xaml:893-894` maps it to [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml)). `MainViewModel.NavigateToToolsAsync` (`MainViewModel.cs:1415-1437`) runs the standard navigation pipeline — wizard guard, `CurrentViewModel = null`, dispose sweep, scope ceremony — but never calls `InitializeAsync()`, since there is nothing to initialize. The VM is a DI singleton (`App.xaml.cs:219`), so re-resolution returns the same instance (see [App Shell & Navigation](app-shell.md)).

All behavior lives in the XAML: each card is a `materialDesign:Card` with a `MouseBinding` that reaches **up through the window** to invoke a command on `MainViewModel` — the page's own DataContext is never used for anything. This is the same pattern the Home page cards use:

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

Consequence: the hub is a **static duplicate of sidebar navigation**. If a sidebar command's target changes, the card follows automatically (same command), but the card list itself is hard-coded XAML — adding a tool means editing both `MainWindow.xaml` and `ToolsView.xaml`.

## Files & Code

| File | Role |
|---|---|
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | The whole hub: two rows, twelve cards, each a `MouseBinding` to a `MainViewModel` command |
| [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | Empty ViewModel — navigation key only |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToToolsAsync` (`1415-1437`) plus the eleven target navigation commands the cards invoke |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Home page Tools card (`581-607`), the VM→View DataTemplate (`893-894`), and the sidebar buttons the grid mirrors |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | Registers `ToolsViewModel` as a singleton (`219`) |

## Data & Settings

The hub persists nothing and reads nothing — no database access, no settings, no state of its own. It is identical for every [profile](../getting-started/profiles.md); the pages it navigates to manage their own data.

## Status & Known Issues

- **Works**: all twelve cards navigate correctly; they reuse the exact `MainViewModel` commands the sidebar uses, so behavior is identical to the sidebar entries.
- **Duplicate naming, same destination** — "Events & Alerts" and "Visual Alerts" both open Alert Settings; there is no distinct events page.
- **Sidebar parity** — the sidebar's Events & Alerts and Browser Sources buttons were wired on 2026-08-15, so the former sidebar/hub asymmetry is gone; the hub still omits a Browser Sources card (the sidebar button routes to Audio Settings).
- No keyboard access: cards are mouse-only (`MouseBinding`), unlike sidebar buttons which are real focusable `Button`s.
