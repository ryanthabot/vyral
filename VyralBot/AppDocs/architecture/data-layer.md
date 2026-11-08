# Data Layer

> All persistence flows through one facade — `IDataService` — whose profile-aware decorator re-points a SQLite database on every single call, making every table, token, and setting per-profile automatically.

## Overview

The persistence stack is three layers:

```
Callers (services, ViewModels, controllers)
        │
        ▼
IDataService            ← 200+ method facade (src/VyralBot.Data/IDataService.cs)
        │
        ▼
ProfileAwareDataService ← decorator (UI layer); resolves the CURRENT profile on every call
        │
        ▼
DapperDataService       ← the only real repository (SQLite + Dapper)
```

- `DapperDataService` is the sole `IDataService` implementation that touches a database. It owns schema creation and every query, written with Dapper against `Microsoft.Data.Sqlite`.
- `ProfileAwareDataService` wraps it and is what DI actually hands out (`services.AddSingleton<IDataService, ProfileAwareDataService>()`). It is the mechanism that makes *everything* per-profile — no caller ever needs to know which profile is active.
- The `Configs` table doubles as a small key/value store for miscellaneous settings that never got their own columns.

Related pages: [Profiles](../getting-started/profiles.md) for the user-facing model, [Startup & Lifecycle](startup-lifecycle.md) for when `EnsureDatabaseCreatedAsync` runs, [Settings Overview](../settings/overview.md) for which setting lives where.

## Flow

### Per-profile resolution on every call

`ProfileAwareDataService` has no database of its own. Every facade method routes through `GetCurrentService()`:

1. Ask `IProfileService.GetCurrentProfileName()` (reads `appdata/current_profile.txt`).
2. If no profile is selected, fall back to the literal profile name `"temp"` — this is why an `appdata/profiles/temp/data/vyralbot.db` appears on disk after running without a profile.
3. If the resolved name differs from the cached one, build a fresh `DapperDataService` pointed at `appdata/profiles/<Name>/data/vyralbot.db` (creating the directory).

There is no connection string swap, no per-call parameter — the swap happens implicitly because *every* call re-checks the profile name. Profile switching therefore "just works" for the database, whatever code does the reading.

### `ClearConnection` — deleting a profile's database

`ClearConnection()` (`ProfileAwareDataService.cs:48-64`) exists for one purpose: releasing SQLite's file locks so a profile directory can be deleted. It nulls the cached service, calls `SqliteConnection.ClearAllPools()`, and forces `GC.Collect()` + `WaitForPendingFinalizers()`. The Settings page's delete flow calls it, waits 200 ms, GCs again, and only then recursively deletes the directory.

### Schema creation and migration

`DapperDataService.EnsureDatabaseCreatedAsync()` (`DapperDataService.cs:3390+`) creates **50 tables** with `CREATE TABLE IF NOT EXISTS` — Platforms, DiscordBots, Configs, Triggers, Actions, EventLogs, WebServerSettings, WebhookSubscriptions, ChatMessages, ChatEventSettings, AutoReplyRules, EventStatistics, the Currency family (settings, user currency, transactions, rewards, redemptions, linked accounts, streaks, loyalty tiers, game settings, perks, boosts, auctions, auction bids, bank accounts), Giveaways + entries, Polls + votes, UserWatchtime, Variables, Commands, CommandCooldowns, Quotes, Workflows + WorkflowExecutions, moderation tables and more — plus 88 indexes. It runs at startup on the profile-aware service.

There is no migration framework. Column additions are handled inline: `pragma_table_info` checks followed by `ALTER TABLE ADD COLUMN` (for example the per-platform chat-enabled flags, `DapperDataService.cs:3434+`). Adding a column means editing this method; there is no version table.

### What lives where

| Data | Location | Notes |
|---|---|---|
| Active profile name | `appdata/current_profile.txt` | Plain text, one name |
| Profile metadata, PIN hash, `AutoLogin` | `appdata/profiles/<Name>/profile.json` | `Settings` is a string dictionary; the only key currently used is `AutoLogin` |
| **Everything below** | per-profile `data/vyralbot.db` | All 34 tables |
| Platform rows (tokens, status, chat flags, Kick channel IDs) | `Platforms` | Twitch user tokens are DPAPI-encrypted in-row; Kick/YouTube tokens are plaintext by design |
| Web server settings (port, region, auto-start, tunnel URL) | `WebServerSettings` | Single row, Id=1 |
| Kick webhook subscriptions | `WebhookSubscriptions` | Written by the coordinator |
| Per-event display/auto-reply settings | `ChatEventSettings` | Cached by `ChatEventSettingsService` |
| Custom variables | `Variables` | Seeded with 31 rows on creation |
| ngrok auth token | `<profile>/ngrok_credentials.json` | Plaintext JSON, *outside* the DB, written only by the ngrok wizard |
| Twitch OAuth client id/secret | `<profile>/twitch_credentials.json` | Plaintext JSON |

The on-disk layout is fully portable — no registry, no `%APPDATA%`; deleting `appdata/` is a complete reset. `ProfileService` resolves the app root by walking six directories up from the exe (`src/VyralBot.UI/bin/Debug/net10.0-windows` → repo root) and falls back to the exe directory for published builds.

### `Configs` keys actually used

The `Configs` table supports `Category` and `IsEncrypted` columns; `IsEncrypted` is used by no caller, and `Category` is only set by the Support link keys ("Support"). The keys read or written by application code:

| Key | Written by | Contents |
|---|---|---|
| `AudioSettings` | Audio settings page | One JSON blob (toggles, master volume, port) |
| `AlertSettings` | Alert settings page | One JSON blob: master toggle, duration, animation, position, per-event switches (read by `AlertService` on real alerts) |
| `Kick.AutoRepliesEnabled` | Kick settings page | `"True"`/`"False"` |
| `Support.DiscordUrl` / `Support.IdeasUrl` | Settings → Profile → Support Links | URLs opened by the Home support rows (category "Support") |
| `Kick.AllowTestWebhooks` | Manually (database operators) | Opt-in flag for the Kick webhook test-payload signature bypass (default absent = fail-closed) |
| `EventSound_{Platform}_{eventType}` | Sounds page | Sound file name for the event |
| `EventVolume_{Platform}_{eventType}` | Sounds page | Per-event volume |

For the full table-by-table reference see [Database Reference](../reference/database-reference.md).

## Key classes

| Class | Role |
|---|---|
| `IDataService` | The facade every consumer depends on — 200+ async methods covering platforms, config, currency, commands, giveaways, polls, watchtime, variables, webhooks, chat |
| `ProfileAwareDataService` | Decorator: per-call profile resolution, temp fallback, `ClearConnection` |
| `DapperDataService` | SQLite + Dapper repository; `EnsureDatabaseCreatedAsync` schema; inline ALTER TABLE migrations |
| `ProfileService` | Owns `appdata/` layout, `current_profile.txt`, profile CRUD |
| `ChatEventSettingsService` | Cached read wrapper over `ChatEventSettings` (cache keyed by `platform:eventType`, invalidated on profile switch) |

## Files & Code

| File | Role |
|---|---|
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | The facade interface |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | The repository and schema (table creation starts at line ~2099) |
| [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) | The decorator — short and worth reading in full |
| [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) | appdata layout, current-profile file, profile CRUD/switch |
| [WebServerEntities.cs](../../src/VyralBot.Data/Entities/WebServerEntities.cs) | Example entities (`WebServerSettings`, `WebhookSubscription`) |
| [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) | Cached wrapper — invalidates on `ProfileSwitchedEvent` |

The heart of the whole layer — every call re-resolves the profile:

```csharp
// ProfileAwareDataService.cs
private DapperDataService GetCurrentService()
{
    var profileName = _profileService.GetCurrentProfileName() ?? "temp";

    // If profile changed, recreate the service with new database path
    if (_currentService == null || _currentProfileName != profileName)
    {
        _currentProfileName = profileName;
        var profileDataPath = _profileService.GetProfileDataPath(profileName);
        Directory.CreateDirectory(profileDataPath);
        var dbPath = Path.Combine(profileDataPath, "vyralbot.db");
        _currentService = new DapperDataService($"Data Source={dbPath}", _logger);
    }
    return _currentService;
}

// Every one of the 200+ facade methods is a one-liner:
public Task<Platform?> GetPlatformByNameAsync(string name)
    => GetCurrentService().GetPlatformByNameAsync(name);
```

Releasing file locks so a profile can be deleted:

```csharp
// ProfileAwareDataService.cs — ClearConnection
public void ClearConnection()
{
    _currentService = null;
    _currentProfileName = null;
    SqliteConnection.ClearAllPools();   // otherwise pooled connections hold the .db file
    GC.Collect();
    GC.WaitForPendingFinalizers();
}
```

## Gotchas

- **`appdata/profiles/temp/` accumulates data** written before any profile exists (first-run usage, or if `current_profile.txt` is missing). It is never cleaned up automatically.
- **`ChatEventSettingsService` caches per profile switch lifecycle** — the cache key is `platform:eventType` with no profile component, but the service subscribes to `ProfileSwitchedEvent` and calls `InvalidateCache()` on every switch (fixed 2026-08-15), so stale rows are no longer served across profiles.
- **Schema changes require hand-editing `EnsureDatabaseCreatedAsync`** — no migration runner, no version tracking; `ALTER TABLE` blocks are appended per feature.
- **Token encryption is inconsistent**: Twitch user tokens are DPAPI-encrypted inside the `Platforms` row; Kick and YouTube tokens are stored plaintext (deliberate, per code comments, and documented); client secrets live in plaintext JSON files. See [Platforms Overview](../platforms/overview.md).
- **`EventStatistics` is written by the 13 event handlers** — [Event History](../chat-features/event-history.md) and [Statistics](../stream-tools/statistics.md) have real data; the six ShowInChat-gated handlers skip recording when Show-in-chat is off (KI-26 in the [known issues ledger](../reference/known-issues.md)).
- **The `IsEncrypted` and `Category` columns of `Configs` are dead** — present in schema, used by no caller.
- **Dapper and SQL strings live in one very large file** (`DapperDataService.cs`, ~3000 lines); there are no repositories per aggregate despite the facade's breadth.
