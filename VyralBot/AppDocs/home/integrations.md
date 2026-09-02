# Integrations

> A placeholder page reached from the Home card of the same name: a four-slot card grid with exactly one card on it — BotGhost, marked "Coming soon" — and no integration functionality of any kind.

## Overview

The **Integrations** page is where third-party bots and external services will eventually live. Today it is a stub: one card (BotGhost) under a "Third-party integrations and bots" subtitle, every entry labeled *Coming soon*, and nothing clickable. There is no integration logic anywhere in the codebase backing this page — no API clients, no BotGhost code, nothing to configure.

You reach it only from the **Integrations** card on the [Home dashboard](dashboard.md) ("Third-party integrations and bots" — `MainWindow.xaml:611-637`). The sidebar has no Integrations entry, so the Home card is the single way in.

## User Guide

1. On the Home page, click the **Integrations** card (puzzle icon).
2. The page shows its title, a "← BACK TO HOME" button, and a grid of integration cards.
3. That's the entire page. The only card present is **BotGhost** (logo, name, italic "Coming soon"). It renders with a hand cursor but has no click action.

There is nothing to configure, connect, or try here. Connecting streaming platforms is a different, fully working feature — see [Platforms Overview](../platforms/overview.md).

## How It Works (Developer)

[IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) is an **empty class** — no properties, no commands:

```csharp
public partial class IntegrationsViewModel : ViewModelBase
{
}
```

Its only reason to exist is to serve as the navigation key: `MainViewModel.NavigateToIntegrationsAsync` (`MainViewModel.cs:1440-1463`) resolves it, assigns it to `CurrentViewModel`, and the `DataTemplate` map in `MainWindow.xaml:898-899` renders [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) for it. The navigation command runs the standard pipeline (wizard guard, `CurrentViewModel = null`, dispose sweep, scope ceremony) but — unlike most pages — never calls `InitializeAsync()`, because there is nothing to initialize. The VM is registered as a DI singleton (`App.xaml.cs:220`), so the scope dance resolves the same instance every visit; see [App Shell & Navigation](app-shell.md) for why the scopes are no-ops.

The card grid in the view reserves **four card columns** (`IntegrationsView.xaml:33-61` defines seven columns: four card slots separated by 12px gutters) but only column 0 is populated — the XAML is visibly scaffolded for a full row of integrations that were never added. The BotGhost logo is a bundled resource (`Resources/Images/botghost-logo.png`); the "Coming soon" label is plain XAML text, not a status from any service.

## Files & Code

| File | Role |
|---|---|
| [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) | The whole page: back button, title, 4-slot grid with one card (BotGhost, "Coming soon") |
| [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) | Empty ViewModel — exists only as the DataTemplate/navigation key |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToIntegrationsAsync` (`1440-1463`) — the only way in |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Home page Integrations card (`611-637`) and the VM→View DataTemplate (`898-899`) |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | Registers `IntegrationsViewModel` as a singleton (`220`) |

The one card, in full (`IntegrationsView.xaml:44-60`) — note there is no `InputBindings`/`Command` anywhere:

```xml
<!-- BotGhost Card -->
<materialDesign:Card Grid.Column="0" Padding="24" Cursor="Hand">
    <StackPanel HorizontalAlignment="Center">
        <Image Source="/Resources/Images/botghost-logo.png" ... />
        <TextBlock Text="BotGhost" FontSize="14" FontWeight="SemiBold" ... />
        <TextBlock Text="Coming soon" FontSize="11" FontStyle="Italic" ... />
    </StackPanel>
</materialDesign:Card>
```

## Data & Settings

The page persists nothing and reads nothing: no database access, no settings, no per-profile state. The view is identical for every [profile](../getting-started/profiles.md).

## Status & Known Issues

- **No integration functionality exists.** The page is a UI placeholder: one of four card slots filled, zero commands, zero backing services. Any real BotGhost (or other) integration would be new development, not a fix.
- The card shows a hand cursor as if clickable — minor cosmetic misleading.
- Listed as part of the placeholder surface in the [known issues ledger](../reference/known-issues.md); the surrounding navigation machinery itself works fine (see [App Shell & Navigation](app-shell.md)).
