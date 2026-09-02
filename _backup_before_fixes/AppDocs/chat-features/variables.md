# Variables

> Template placeholders in `{kebab-case}` syntax — a shared resolution engine with a 5-minute cache and nested-variable support, a seeded catalog of 31 variables, a picker attached to every text box in the app, and a manager page under Settings.

## Overview

Variables let one message template adapt to the moment it is sent. You write `Thanks for the follow {username}!` and, when the message goes out, `{username}` becomes the actual viewer's name. VyralBot ships 31 seeded variables in three families: **system/context variables** (like `{username}` and `{platform}`) that are filled in from the situation at send time, **auto-updating platform variables** (like `{twitch-followercount}`) that track live channel stats, and **editable social variables** (like `{twitter}` and `{discord}`) that you fill in once and reuse everywhere.

Where variables are actually resolved differs by feature, and the differences matter:

- **Command responses** get the full engine — DB variables (socials, platform stats) *and* per-command context (`{username}`, `{balance}`, ...) all resolve.
- **Manually sent chat messages** also get the full engine whenever the text contains a `{`.
- **Auto-reply templates** do *not* use this engine — they go through a simple string-replace over just the keys the event handler supplies, so `{twitter}` and other DB variables never resolve there. See [Auto-Replies](auto-replies.md).

The full variable list is also catalogued in the [Variables Catalog](../reference/variables-catalog.md).

## User Guide

### Inserting a variable into any text box

Every text box in the app has the variable picker built in (a global WPF style attaches it). Two ways to use it:

1. **Right-click any text box** and choose **Variables** from the context menu, or
2. Click a dedicated picker button where one is shown (for example next to the Response field when editing a command on the [Commands](commands.md) page).

The picker opens a searchable, category-filtered list of every variable. Clicking one inserts `{name}` **at the caret position** and closes the picker. Categories in the picker: All, Global, Twitch, Kick, Social, plus any custom categories you create.

> The picker lists only variables that currently have a **value** (it loads them through the same cache the replacement engine uses). System/context variables like `{username}` have no stored value, so they do not appear in the picker even though they work in command responses — the five command variables are documented on the Help tab of the Commands page.

### Managing variables: Settings → Variables

The Variables manager lives in **Settings → Variables** (there is no dedicated sidebar entry for it). It provides:

- **Category filter** (`All` plus the categories stored in the database) and a **text search** across name, display name, and description.
- **Add / Edit** of *editable* variables only — the seeded system and auto-updating variables are read-only and refuse editing with a status message.
- **Delete**, also guarded: a variable can only be deleted if it is editable and not a system variable.
- New names must be **lowercase kebab-case** (`my-variable`, matching `^[a-z0-9]+(-[a-z0-9]+)*$`) and unique — the manager enforces both before saving.

Every save or delete **immediately invalidates the replacement cache**, so edits apply to the next message without waiting out the 5-minute cache window.

### Filling in your socials

The six seeded social variables are the ones you are meant to edit: `twitter`, `discord`, `youtube`, `instagram`, `tiktok`, and the composite `full-socials` (default `Twitter: {twitter} | Discord: {discord} | YouTube: {youtube}`). Put your handles/URLs in the first five and `{full-socials}` anywhere will expand to all of them — the engine resolves variables nested inside other variable values.

### The seeded catalog

**19 system/context variables** — no stored value; they only resolve when the calling feature supplies them as context:

`username`, `message`, `months`, `number`, `platform`, `viewers`, `bits`, `reward`, `tier`, `gifter`, `raider`, `hoster`, `sender`, `amount`, `balance`, `role`, `channel`, `uptime`, `watchtime`

**6 auto-updating platform variables** — stored value kept in sync from webhook/API data, seeded at `0`:

`kick-followercount`, `kick-subscribercount`, `kick-viewcount`, `twitch-followercount`, `twitch-subscribercount`, `twitch-viewcount`

**6 editable social variables** — `twitter`, `discord`, `youtube`, `instagram`, `tiktok`, `full-socials` (nested, see above).

## How It Works (Developer)

The engine is [VariableReplacementService.cs](../../src/VyralBot.Core/Services/Variables/VariableReplacementService.cs), registered as a singleton. `ReplaceVariablesAsync(template, contextVariables, platform)`:

1. Loads DB variables from the `Variables` table through a **5-minute cache** (`CacheExpiryMinutes = 5`, a `ConcurrentDictionary` keyed by platform or `"all"`). A platform filter excludes variables whose `Platforms` column does not contain that platform.
2. **Merges context variables over DB variables — context wins** (case-insensitive keys).
3. Only DB variables with a non-empty `Value` participate; system variables (`Value = null`) only work when supplied by the caller's context.
4. Runs **recursive replacement** (`ReplaceVariablesRecursiveAsync`) with `MaxNestedDepth = 5` — this is what lets `full-socials` reference `{twitter}` etc. Circular references are detected per pass and skipped; unresolvable placeholders are left as-is; on any error the original template is returned unchanged.

The placeholder grammar is the regex `\{([a-z0-9]+(-[a-z0-9]+)*)\}` (case-insensitive) — names must be kebab-case.

### Consumers — where variables actually resolve

| Consumer | Engine | Context supplied |
|---|---|---|
| Command responses ([Commands](commands.md)) | Full service (sync-over-async from `CommandService.ParseVariables`) | `username`, `platform`, `channel`, `balance`, `role` + custom per-command variables |
| Manual chat sends ([Sending Messages](../chat/sending-messages.md)) | Full service, only when the typed text contains `{` | `BuildContextVariables`: `bot-username`, `channel`, `platform` |
| Auto-reply templates ([Auto-Replies](auto-replies.md)) | **Simple `string.Replace`** (`AutoReplyService.ProcessTemplate`) — DB/social variables do NOT resolve | Only the keys the event handler passes (`{username}`, `{platform}`, event fields) |

### The app-wide picker

A global TextBox style in [App.xaml](../../src/VyralBot.UI/App.xaml) (~line 48) sets `TextBoxVariableBehavior.EnableVariablePicker=True` on **every TextBox in the application**, adding a Variables item to the standard context menu. [TextBoxVariableBehavior.cs](../../src/VyralBot.UI/Behaviors/TextBoxVariableBehavior.cs) wires the menu click: it finds the owning TextBox, opens a centered popup hosting the [VariablePicker](../../src/VyralBot.UI/Controls/VariablePicker.xaml) control, resolves the picker's ViewModel from DI, and even tries to infer the platform from the DataContext's `SelectedPlatform`. `VariablePickerViewModel.SelectVariable` inserts `{name}` at the caret and refocuses the box. Category inference (`DetermineCategory`): `twitch-*` → Twitch, `kick-*` → Kick, `youtube-/trovo-/tiktok-` and `twitter/discord/full-socials` → Social, a hard-coded list of globals → Global, everything else → Custom.

## Files & Code

| File | Role |
|---|---|
| [VariableReplacementService.cs](../../src/VyralBot.Core/Services/Variables/VariableReplacementService.cs) | Resolution engine: cache, merge, nested recursion, validation, invalidation |
| [IVariableReplacementService.cs](../../src/VyralBot.Core/Services/Variables/IVariableReplacementService.cs) | Service interface |
| [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) | Manager page ViewModel: filters, guarded add/edit/delete, cache invalidation |
| [VariablesView.xaml](../../src/VyralBot.UI/Views/VariablesView.xaml) | Manager page layout (list + dialogs) |
| [VariablesView.xaml.cs](../../src/VyralBot.UI/Views/VariablesView.xaml.cs) | Code-behind (dialog open animations) |
| [VariablePicker.xaml](../../src/VyralBot.UI/Controls/VariablePicker.xaml) / [VariablePicker.xaml.cs](../../src/VyralBot.UI/Controls/VariablePicker.xaml.cs) | Reusable inline picker control |
| [VariablePickerViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablePickerViewModel.cs) | Picker list/search/categories; caret insertion; category inference |
| [TextBoxVariableBehavior.cs](../../src/VyralBot.UI/Behaviors/TextBoxVariableBehavior.cs) | Attached behavior adding the Variables context-menu item to any TextBox |
| [App.xaml](../../src/VyralBot.UI/App.xaml) | Global TextBox style enabling the picker app-wide |
| [SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml) | Hosts the Variables manager in the Settings → Variables category (line 833) |
| [Variable.cs](../../src/VyralBot.Data/Entities/Variable.cs) | Entity: name, display name, category, value, flags (`IsEditable`, `IsSystem`, `IsAutoUpdating`, `ContainsNestedVariables`), platforms |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `Variables` table CRUD + seeding of the 31 defaults (lines 3583-3737) |

Nested resolution with depth limit, the core of the engine:

```csharp
private const int MaxNestedDepth = 5;
private const int CacheExpiryMinutes = 5;

// ReplaceVariablesRecursiveAsync: replaces {placeholders}, then recurses while
// new placeholders appeared (nested variables), up to MaxNestedDepth.
if (replacedAny && VariablePlaceholderRegex.IsMatch(result))
{
    result = await ReplaceVariablesRecursiveAsync(result, variables, depth + 1);
}
```

The merge rule — context beats the database:

```csharp
// Merge context variables with database variables (context takes priority)
var allVariables = new Dictionary<string, string>(dbVariables, StringComparer.OrdinalIgnoreCase);
if (contextVariables != null)
{
    foreach (var kvp in contextVariables)
        allVariables[kvp.Key] = kvp.Value;
}
```

## Data & Settings

- **Variables** table (per-profile SQLite, see [Database Reference](../reference/database-reference.md)): one row per variable. Columns include `Name` (kebab-case), `DisplayName`, `Category`, `Prefix` (e.g. `kick-`), `Value` (null for system variables), `MaxLength`, `IsEditable`, `IsSystem`, `IsAutoUpdating`, `ContainsNestedVariables`, `Platforms`, `LastSyncedAt`.
- Seeding happens once, only when the table is empty: 19 system + 6 auto-updating + 6 social = 31 rows.
- The 5-minute in-memory cache lives in the service, not the database; profile switches invalidate it via `InvalidateCacheAsync`.

## Status & Known Issues

- **Auto-reply templates bypass this engine.** `AutoReplyService.ProcessTemplate` is a simple string replace over handler-supplied keys, so `{twitter}`, `{full-socials}`, and the auto-updating platform variables never resolve in auto-replies — only command responses and manual sends get the full catalog. See [Auto-Replies](auto-replies.md). — [known issues ledger](../reference/known-issues.md)
- The **VariablePicker only shows variables with values**, so system/context variables (`{username}`, `{balance}`, ...) never appear in it even though they work in command responses. The five command variables are instead documented on the Commands page Help tab.
- The Variables manager has **no sidebar entry** — it is reachable only via Settings → Variables. (It is fully wired there: category button, hosted panel, working CRUD.)
- The cache is 5 minutes; edits through the manager invalidate it immediately, but **external edits** to the database (e.g. by hand) take up to 5 minutes to surface.
- `{uptime}` and `{watchtime}` are seeded as system variables but nothing currently populates them (`!uptime`/`!watchtime` return placeholders — see [Commands](commands.md)).
- The biggest consumer of context variables — command responses — is currently inert because the command pipeline is dead; see [Commands: Status & Known Issues](commands.md#status--known-issues). Manual chat sends and auto-replies still resolve variables today.
