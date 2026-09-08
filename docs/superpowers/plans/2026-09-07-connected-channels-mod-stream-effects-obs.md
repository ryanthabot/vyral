# Connected Channels + Tiered Mod Stream Commands + OBS Import — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add (1) a Connected Channels tab to Chat that registers every Discord channel the bot monitors with purpose tags (Moderation / Admin / Support / Logging), (2) tier-gated moderator commands that run in purpose-tagged channels and trigger stream effects (Philips Hue / webhook lights flashing, Stream Deck actions via the official `streamdeck://` URI scheme), and (3) verbatim OBS scene-collection JSON import — stored as raw bytes, browsable in-app, re-exportable byte-identical ("100% the same, no conversion").

**Architecture:** .NET 10 WPF (MVVM, CommunityToolkit.Mvvm, MaterialDesignThemes). Persistence is SQLite via Dapper (`VyralBot.Data`), domain services in `VyralBot.Core` (must NOT reference `VyralBot.Integrations` — bridging is done via seam interfaces implemented in `VyralBot.UI\Services` and registered in `App.xaml.cs`), hardware integrations in `VyralBot.Integrations`, UI in `VyralBot.UI`. Events flow in-process over `VyralBot.Core` `IEventBus`. Mod commands already exist in two engines (`DiscordModCommandHandler` `!`-commands, `ModCommandEngine` `!mod`-commands) with the `ModTiers` ladder (TrialMod→Mod→SeniorMod→Admin) — this plan extends them instead of replacing them.

**Tech Stack:** C# / .NET 10 WPF, Discord.Net 3.20.1, Dapper + Microsoft.Data.Sqlite, System.Text.Json, xunit. **Zero new NuGet packages** — Hue uses `HttpClient`, Stream Deck uses `Process.Start` on `streamdeck://` URIs, OBS JSON uses `System.Text.Json`.

## Global Constraints (every task implicitly includes these)

- Target framework `net10.0-windows`; no new NuGet/external dependencies anywhere.
- `VyralBot.Core` may not reference `VyralBot.Integrations`. Integrations may not reference `VyralBot.Data` — pass config into Integrations services via injected delegates, not `IDataService`.
- DB changes are additive only: `CREATE TABLE IF NOT EXISTS` inside `DapperDataService.EnsureDatabaseCreatedAsync()`. Match the exact style of the neighboring `ModRoleAssignments` table (≈ line 4060 of `VyralBot.Data\DapperDataService.cs`).
- UI: only `{DynamicResource ...}` theme brushes; only valid `PackIconKind` values (an invalid kind crashes at XAML parse — see commit c9e5f4d).
- **Legacy fallback rule:** when no rows exist in `ConnectedChannels` for a bot, all existing behavior is preserved exactly (commands allowed in any channel, no filtering). New gating activates only once the user configures channels.
- Tests live in `tests\VyralBot.UI.Tests\` (xunit). Run all with `dotnet test tests\VyralBot.UI.Tests\VyralBot.UI.Tests.csproj`. Build solution: `dotnet build VyralBot\src\VyralBot.sln`.
- Commit style (repo convention): `feat(scope): lowercase summary`, scopes used here: `chat`, `mod`, `lights`, `deck`, `obs`, `data`.
- Every task ends with: build passes + its own tests pass + committed.

## Repo layout quick reference (all paths relative to `C:\Users\user\OneDrive\Desktop\VyralBot`)

- Solution: `VyralBot\src\VyralBot.sln`; projects under `VyralBot\src\` (`VyralBot.UI`, `VyralBot.Core`, `VyralBot.Data`, `VyralBot.Integrations`, …).
- Chat page: `VyralBot\src\VyralBot.UI\Views\ChatView.xaml` + `ViewModels\ChatViewModel.cs` (6,614 lines — source-generated MVVM partials; add properties near `_showDmPanel` line ≈281).
- Discord: `VyralBot\src\VyralBot.Integrations\Discord\DiscordService.cs` (4,938 lines), `IDiscordService.cs`, `DiscordModCommandHandler.cs`, `Models\DiscordGuildInfo.cs`.
- Mod tiers: `VyralBot\src\VyralBot.Core\Services\Moderation\ModTiers.cs`, `ModCommandEngine.cs`, `ModRoleService.cs`, `IModRoleService.cs`; UI seam: `VyralBot\src\VyralBot.UI\Services\ModDiscordBotAdapter.cs`; seam wiring `VyralBot\src\VyralBot.UI\App.xaml.cs:440-515`.
- DI: `VyralBot\src\VyralBot.UI\App.xaml.cs` (singleton registrations ≈ lines 110-260).
- Config KV: `IDataService.GetConfigValueAsync` / `SetConfigValueAsync(key, value, category)` (see `SettingsViewModel.cs:578` for usage).
- Import/export precedent: `VyralBot\src\VyralBot.UI\ViewModels\FlowEditorViewModel.cs:5597-5613` (`OpenFileDialog` + `JsonSerializer`).
- Navigation recipe: `[RelayCommand] NavigateToXxxAsync` in `MainViewModel.cs` (e.g. `NavigateToChatAsync` ≈ line 1064) + sidebar Button in `MainWindow.xaml` + `DataTemplate` in `MainWindow.xaml` ContentControl.Resources (≈ lines 547-985).

## Shared Contracts (implemented in Tasks 1-3; later tasks consume EXACTLY these)

```csharp
// VyralBot.Data\Entities\ConnectedChannel.cs
namespace VyralBot.Data.Entities;

/// <summary>A Discord channel a bot monitors, tagged with purpose CSV ("Moderation,Support,Logging").</summary>
public class ConnectedChannel
{
    public int Id { get; set; }
    public string BotId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string GuildName { get; set; } = "";
    public string Purposes { get; set; } = "";
    public bool MonitoringEnabled { get; set; } = true;
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}

// VyralBot.Data\Entities\DiscordRoleTierAssignment.cs
namespace VyralBot.Data.Entities;

/// <summary>Maps a Discord role to a mod tier ("TrialMod"|"Mod"|"SeniorMod"|"Admin"). Members holding the role gain the tier.</summary>
public class DiscordRoleTierAssignment
{
    public int Id { get; set; }
    public string BotId { get; set; } = "";
    public string GuildId { get; set; } = "";
    public string DiscordRoleId { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string Tier { get; set; } = "";
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}

// VyralBot.Data\Entities\ObsSceneCollection.cs
namespace VyralBot.Data.Entities;

/// <summary>An imported OBS scene collection. RawJsonBytes is the VERBATIM file — the canonical store; everything else is a derived projection.</summary>
public class ObsSceneCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public byte[] RawJsonBytes { get; set; } = Array.Empty<byte>();
    public string SourcePath { get; set; } = "";
    public int SceneCount { get; set; }
    public int SourceCount { get; set; }
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}
```

```csharp
// VyralBot.Data\IDataService.cs — additions (implement in DapperDataService):
Task<List<ConnectedChannel>> GetConnectedChannelsAsync(string botId);
Task<ConnectedChannel> UpsertConnectedChannelAsync(ConnectedChannel channel);
Task DeleteConnectedChannelAsync(string botId, string channelId);
Task<int> CountConnectedChannelsAsync(string botId);

Task<List<DiscordRoleTierAssignment>> GetRoleTierAssignmentsAsync(string botId);
Task UpsertRoleTierAssignmentAsync(DiscordRoleTierAssignment assignment);
Task DeleteRoleTierAssignmentAsync(string botId, string discordRoleId);

Task<List<ObsSceneCollection>> GetObsSceneCollectionsAsync();
Task<ObsSceneCollection?> GetObsSceneCollectionAsync(int id);
Task<ObsSceneCollection> SaveObsSceneCollectionAsync(ObsSceneCollection collection); // insert when Id==0, else update
Task DeleteObsSceneCollectionAsync(int id);
```

```csharp
// VyralBot.Core\Services\Channels\ChannelPurposes.cs  (constants + CSV helpers)
namespace VyralBot.Core.Services.Channels;

public static class ChannelPurposes
{
    public const string Moderation = "Moderation";
    public const string Admin = "Admin";
    public const string Support = "Support";
    public const string Logging = "Logging";
    public static readonly string[] All = { Moderation, Admin, Support, Logging };

    public static bool IsValid(string? purpose) =>
        All.Any(p => string.Equals(p, purpose?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static List<string> Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(p => All.FirstOrDefault(a => string.Equals(a, p, StringComparison.OrdinalIgnoreCase)))
                 .OfType<string>().Distinct().ToList();

    public static string ToCsv(IEnumerable<string> purposes) => string.Join(",", purposes);

    public static bool HasPurpose(string? csv, string purpose) => Parse(csv).Contains(purpose);
}

// VyralBot.Core\Services\Channels\IConnectedChannelService.cs
namespace VyralBot.Core.Services.Channels;
public interface IConnectedChannelService
{
    Task<List<VyralBot.Data.Entities.ConnectedChannel>> GetChannelsAsync(string botId);
    Task<VyralBot.Data.Entities.ConnectedChannel> UpsertAsync(VyralBot.Data.Entities.ConnectedChannel channel);
    Task DeleteAsync(string botId, string channelId);
    Task<bool> IsMonitoredAsync(string botId, string channelId);
    /// <summary>True when the channel is tagged Moderation or Admin. Legacy rule: also true when the bot has ZERO configured channels.</summary>
    Task<bool> AcceptsCommandsAsync(string botId, string channelId);
    Task<IReadOnlyList<VyralBot.Data.Entities.ConnectedChannel>> GetByPurposeAsync(string botId, string purpose);
}

// VyralBot.Core\Services\StreamEffects\StreamEffectEvent.cs
namespace VyralBot.Core.Services.StreamEffects;
/// <summary>EffectType: "LightFlash" | "LightScene" | "StreamDeckAction".
/// Payload JSON: {"color":"#FF00AA","times":5} | {"state":"on"} | {"action":"hype"}.</summary>
public record StreamEffectEvent(string EffectType, string Payload, string RequestedBy, string ChannelId);

// VyralBot.Core\Services\Moderation\IModTierResolver.cs
namespace VyralBot.Core.Services.Moderation;
/// <summary>Effective tier = max(DB assignment, Discord-role mapping). Canonical name or null.</summary>
public interface IModTierResolver { Task<string?> ResolveTierAsync(string botId, string? guildId, string userId, string username); }
```

**Config keys** (all category `"StreamEffects"`, via `IDataService` config KV):
`Lights.Provider` ("None"|"Hue"|"Webhook"), `Lights.Hue.BridgeIp`, `Lights.Hue.AppKey`, `Lights.Hue.LightIds` (CSV, empty=all), `Lights.Webhook.Url`, `Lights.Webhook.Token`, `Lights.Flash.Times` (default 5), `Lights.Flash.IntervalMs` (default 250), `Lights.Flash.Color` (default `#FF00AA`), `StreamDeck.Actions` (JSON array `[{"id","name","uri"}]`).

---

### Task 1: Data layer — three new tables + IDataService methods

**Files:** Create `VyralBot\src\VyralBot.Data\Entities\ConnectedChannel.cs`, `DiscordRoleTierAssignment.cs`, `ObsSceneCollection.cs` (code above). Modify `VyralBot\src\VyralBot.Data\IDataService.cs` + `DapperDataService.cs`. Test `tests\VyralBot.UI.Tests\DataLayerNewTablesTests.cs`.

**Interfaces:** Produces — the 10 `IDataService` methods exactly as declared above.

Steps: (1) failing tests — construct `DapperDataService` with a temp-file path (mirror existing data tests in the project); cover: ConnectedChannel upsert round-trip, upsert-updates-same-row (UNIQUE conflict) + Count, role-tier upsert/delete, OBS save-insert/update/delete with byte-exact `RawJsonBytes`. (2) run → FAIL. (3) implement — migrations SQL:

```sql
CREATE TABLE IF NOT EXISTS ConnectedChannels (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BotId TEXT NOT NULL, ChannelId TEXT NOT NULL, ChannelName TEXT NOT NULL DEFAULT '',
    GuildId TEXT NOT NULL DEFAULT '', GuildName TEXT NOT NULL DEFAULT '',
    Purposes TEXT NOT NULL DEFAULT '', MonitoringEnabled INTEGER NOT NULL DEFAULT 1, AddedAtUtc TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ConnectedChannels_Bot_Channel ON ConnectedChannels(BotId, ChannelId);

CREATE TABLE IF NOT EXISTS DiscordRoleTierAssignments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BotId TEXT NOT NULL, GuildId TEXT NOT NULL DEFAULT '',
    DiscordRoleId TEXT NOT NULL, RoleName TEXT NOT NULL DEFAULT '', Tier TEXT NOT NULL, AssignedAtUtc TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS IX_RoleTier_Bot_Role ON DiscordRoleTierAssignments(BotId, DiscordRoleId);

CREATE TABLE IF NOT EXISTS ObsSceneCollections (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL, RawJsonBytes BLOB NOT NULL, SourcePath TEXT NOT NULL DEFAULT '',
    SceneCount INTEGER NOT NULL DEFAULT 0, SourceCount INTEGER NOT NULL DEFAULT 0, ImportedAtUtc TEXT NOT NULL);
```

Upsert:

```sql
INSERT INTO ConnectedChannels (BotId, ChannelId, ChannelName, GuildId, GuildName, Purposes, MonitoringEnabled, AddedAtUtc)
VALUES (@BotId, @ChannelId, @ChannelName, @GuildId, @GuildName, @Purposes, @MonitoringEnabled, @AddedAtUtc)
ON CONFLICT(BotId, ChannelId) DO UPDATE SET
    ChannelName=excluded.ChannelName, GuildId=excluded.GuildId, GuildName=excluded.GuildName,
    Purposes=excluded.Purposes, MonitoringEnabled=excluded.MonitoringEnabled;
```

(4) tests PASS + build PASS. (5) commit `feat(data): connected channels, role-tier mapping, obs scene collection tables`.

### Task 2: Core stream-effect contracts + ModTiers additions

**Files:** Create `VyralBot.Core\Services\StreamEffects\StreamEffectEvent.cs`. Modify `VyralBot.Core\Services\Moderation\ModTiers.cs` (arrays become cumulative with `flashlights` added to Mod, `lightson`/`lightsoff` to SeniorMod, `streamdeck` to Admin; update class doc-comment). Test: extend `tests\VyralBot.UI.Tests\ModTierTests.cs` with `[InlineData]` theory (`Mod/flashlights=true`, `TrialMod/flashlights=false`, `SeniorMod/lightson=true`, `Mod/lightson=false`, `Admin/streamdeck=true`, `SeniorMod/streamdeck=false`) + `MinimumTier` checks (`flashlights→Mod`, `lightsoff→SeniorMod`, `streamdeck→Admin`).

Steps: failing tests → run FAIL → implement → run PASS → commit `feat(mod): tier-gated stream effect verbs in the ModTiers ladder`.

### Task 3: Stream Deck service (Integrations)

**Files:** Create `VyralBot.Integrations\StreamDeck\IStreamDeckService.cs`, `StreamDeckService.cs`, `StreamDeckAction.cs`, `IStreamDeckStore.cs`. Test `tests\VyralBot.UI.Tests\StreamDeckServiceTests.cs`.

**Interfaces:** Produces —

```csharp
public sealed record StreamDeckAction(string Id, string Name, string Uri);
public interface IStreamDeckStore { Task<string> LoadAsync(); Task SaveAsync(string json); }
public record Result(bool Success, string Message);
public interface IStreamDeckService
{
    Task<List<StreamDeckAction>> GetActionsAsync();
    Task<Result> SaveActionAsync(string name, string uri);
    Task DeleteActionAsync(string id);
    Task<string> TriggerAsync(string actionIdOrName); // user-facing string, never throws
}
```

Tests (FakeStore over JSON `[{\"id\":\"a1\",\"name\":\"Hype\",\"uri\":\"streamdeck://plugins/com.elgato.cpu/act?x=1\"}]`): save rejects non-`streamdeck://` URIs; save normalizes scheme to lowercase + upserts by name (case-insensitive); trigger by name case-insensitive returns message containing the name, unknown → "not found". Implementation per plan: JSON list in store, `Process.Start(ProcessStartInfo { FileName = uri, UseShellExecute = true })`, try/catch → `❌` strings. Steps: failing tests → FAIL → implement → PASS → commit `feat(deck): stream deck action store + streamdeck:// URI trigger`.

### Task 4: OBS scene-collection document model (verbatim parse)

**Files:** Create `VyralBot.Integrations\Obs\ObsSceneCollectionDocument.cs`; `tests\VyralBot.UI.Tests\Fixtures\obs-scene-collection.sample.json` + csproj `<None Include="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />`; test `ObsSceneCollectionDocumentTests.cs`.

**Fixture:** realistic collection with `current_scene`/`scene_order` (Starting Soon, Just Chatting), 5 sources: `dshow_input`, `browser_source` (url/css/volume 0.75), scene "Just Chatting" with 2 items (Display Capture locked at 0,0 scale 1; Chat Overlay rot 12.5 pos 1400,240 scale 0.85 bounds_type 2), empty scene "Starting Soon", `wasapi_output_capture`; `transitions` (Fade 300) + `current_transition`; `resolution` 1920x1080; `DesktopAudioDevice1`/`AuxAudioDevice1`; `modules`.

**Produces (records + class, full code in execution):**

```csharp
public sealed record ObsSourceSummary(string Name, string Id, string? Uuid, double Volume, bool Muted, int FilterCount);
public sealed record ObsSceneItemSummary(string SourceName, string? SourceUuid, bool Visible, bool Locked, double Rotation, double PosX, double PosY, double ScaleX, double ScaleY, int BoundsType);
public sealed record ObsSceneSummary(string Name, string? Uuid, IReadOnlyList<ObsSceneItemSummary> Items);
public sealed record ObsAudioDeviceSummary(string Name, string? SourceId);
public sealed class ObsSceneCollectionDocument { string RawJson; string? CurrentScene/CurrentProgramScene/CurrentTransition/Resolution; IReadOnlyList<...> Scenes/NonSceneSources/AudioDevices; int SourceCount;
    static ObsSceneCollectionDocument Parse(string rawJson); } // throws InvalidDataException for malformed JSON or missing "sources" array
```

Tests: sample maps scenes in scene_order (Starting Soon, Just Chatting); items/pos/scale/rot/bounds_type/volume parsed; valid JSON without sources array → InvalidDataException; malformed JSON → InvalidDataException mentioning "not a valid OBS scene collection". Resolution string "1920x1080". Steps: fixture + failing tests → FAIL → implement (JsonDocument read-only projection, scene = source with `id=="scene"`, audio from DesktopAudioDevice1/AuxAudioDevice1..4 keys) → PASS → commit `feat(obs): verbatim scene-collection parser (read-only projection)`.

### Task 5: Core channel-purpose service

**Files:** Create `VyralBot.Core\Services\Channels\ChannelPurposes.cs`, `IConnectedChannelService.cs`, `ConnectedChannelService.cs` (full code in Shared Contracts). Test `ConnectedChannelServiceTests.cs`: CSV parse normalizes case/junk + ToCsv round-trip; HasPurpose theory; **AcceptsCommands: no configured channels → allow-all (legacy)**; tagged+monitored → true; Logging-tagged → false; MonitoringEnabled=false → false; unregistered channel → false. Fake `IDataService` with explicit-throw stubs for other members (match existing fake patterns in the test project). Steps: failing → FAIL → implement → PASS → commit `feat(mod): connected-channel purpose service with command gating`.

### Task 6: Lights integration (Integrations)

**Files:** Create `VyralBot.Integrations\Lights\ILightsClient.cs`, `LightInfo.cs`, `HueLightsClient.cs`, `WebhookLightsClient.cs`, `LightsManager.cs`. Test `LightsClientTests.cs` (RecordingHandler mock, no network).

**Interfaces:**

```csharp
public sealed record LightInfo(string Id, string Name, bool On, bool Reachable, string? ProductName);
public interface ILightsClient
{
    Task<string> FlashAsync(string? hexColor, int times, int intervalMs, CancellationToken ct = default);
    Task<string> SetPowerAsync(bool on, CancellationToken ct = default);
    Task<List<LightInfo>> GetLightsAsync(CancellationToken ct = default);
}
public sealed class LightsManager(Func<Task<(string Provider, string? BridgeIp, string? AppKey, IReadOnlyList<string> LightIds, string WebhookUrl, string? WebhookToken)>> configLoader) : ILightsClient
```

Hue details: bridge base `http://{ip}/api/{key}`; lights GET `/lights`; flash = for each cycle PUT `/lights/{id}/state` `{"on":true,"xy":[x,y],"alert":"select"}` (or `{"alert":"select"}` when no color), delay intervalMs, then restore original on/xy + `"alert":"none"`; clamp times 1-20 (default from delegate), interval 50-5000 (default 250); unconfigured → "⚠️ Lights are not configured."; `RgbToXy` sRGB→CIE xy (gamma-corrected Philips formula). Tests: GetLights parses two-light JSON; Flash sends 2 lights × 3 cycles + 2 restores; RGB→xy known colors (#FF0000 → 0.64/0.33, #00FF00 → 0.3/0.6, 2 decimals); unconfigured friendly message. Webhook: POST JSON `{"action":"flash","color":...,"times":...,"intervalMs":...}` + optional `Authorization: Bearer`, asserted via RecordingHandler. All public methods return friendly strings, never throw. Steps: failing → FAIL → implement → PASS → commit `feat(lights): hue + webhook light clients with flash/power/enumerate`.

### Task 7: OBS import/export store + persistence

**Files:** Create `VyralBot.Integrations\Obs\ObsSceneCollectionStore.cs` (code below). Test `ObsSceneCollectionStoreTests.cs` (real DapperDataService temp DB + Task 4 fixture file).

```csharp
public sealed record ObsImportResult(bool Success, string Message, VyralBot.Data.Entities.ObsSceneCollection? Collection, ObsSceneCollectionDocument? Document);
public sealed class ObsSceneCollectionStore(VyralBot.Data.IDataService data)
{
    // ImportFromFileAsync: File.ReadAllBytesAsync (exact bytes incl. BOM) → Parse(Encoding.UTF8.GetString(bytes))
    //   → SaveObsSceneCollectionAsync(Name=file name w/o ext, RawJsonBytes=bytes, SourcePath, SceneCount=document.Scenes.Count, SourceCount=document.SourceCount)
    //   → catch InvalidDataException → its message; other ex → "Import failed: ..."
    // ExportToFileAsync(id, path): GetObsSceneCollectionAsync → File.WriteAllBytesAsync(entity.RawJsonBytes) — byte-identical guarantee
    // LoadDocumentAsync(id): parse projection for the tree view; (null, error) on failure
}
```

Test: copy fixture bytes → temp file → import → SceneCount==2, SourceCount==5, stored bytes == fixture bytes → export to second temp path → exported bytes sequence-equal fixture bytes. Steps: failing → FAIL → implement → PASS → commit `feat(obs): byte-exact scene collection import/export store`.

### Task 8: Discord role→tier mapping + effective-tier resolver

**Files:** Modify `IDiscordService.cs` + `DiscordService.cs`, `Models\DiscordGuildInfo.cs` (new `DiscordRoleInfo(string Id, string Name, int Position, uint ColorRaw, bool Managed)`), `Core\Services\Moderation\IModDiscordBot.cs`, `IModRoleService.cs`, `ModRoleService.cs`, `ModCommandEngine.cs`, `UI\Services\ModDiscordBotAdapter.cs`. Create `Core\Services\Moderation\IModTierResolver.cs`, `UI\Services\ModTierResolver.cs`. Test `ModTierResolverTests.cs`.

**Produces:** `IDiscordService.GetGuildRolesAsync(guildId)` (guild.Roles by Position desc → DiscordRoleInfo), `GetMemberRoleIdsAsync(guildId, userId)` (reuse `ResolveGuildMemberAsync` ≈ line 1142 — check its exact signature; `member?.RoleIds` → strings); same two on `IModDiscordBot` (adapter delegates); `IModRoleService.GetTierFromRolesAsync(botId, roleIds)` = highest-rank canonical tier among `GetRoleTierAssignmentsAsync(botId)` matching roleIds; `ModTierResolver.ResolveTierAsync(botId, guildId, userId, username)` = max(DB `GetTierAsync`, role mapping via bot when guildId non-empty; try/catch → assignment tier). `ModCommandEngine`: replace direct `GetTierAsync` lookup with `IModTierResolver` (new ctor param; update seam construction in `App.xaml.cs`); `ModChannelMessage` gains `string? GuildId` (default null keeps behavior). Tests: role tier beats lower assignment; assignment beats lower role; no guild → skip role lookup. Steps: failing → FAIL → implement → PASS + build → commit `feat(mod): discord role to tier mapping + effective tier resolver`.

### Task 9: Stream-effect command router + channel gating

**Files:** Create `Core\Services\StreamEffects\StreamEffectCommandRouter.cs`. Modify `ChatViewModel.cs` (`TryHandleDiscordModCommandAsync` ≈2423-2470), `App.xaml.cs` (DI + DiscordChatSubscriber seam ≈440-515), `DiscordChatMessageEvent.cs` (+`GuildId`), `DiscordService.cs` (populate it ≈1748). Test `StreamEffectCommandRouterTests.cs`.

```csharp
public sealed record StreamEffectCommandContext(string BotId, string? GuildId, string ChannelId, string SenderUserId, string SenderUsername, string Message);
public sealed class StreamEffectCommandRouter(IModTierResolver tiers, IEventBus eventBus, VyralBot.Data.IDataService data)
{
    public static readonly string[] Verbs = { "flashlights", "lightson", "lightsoff", "streamdeck" };
    // TryHandleAsync: tokenize (strip !/. prefix); verb not in Verbs → null (fall through).
    // tier = ResolveTierAsync; !ModTiers.CanRun → "⛔ {user}, '{verb}' requires {MinimumTier} tier or higher."
    // BuildEffect: flashlights → ("LightFlash", {"color":#hexOrNull,"times":1-20 or null});
    //              lightson/off → ("LightScene", {"state":"on"/"off"}); streamdeck → ("StreamDeckAction", {"action":"joined args"})
    // publish StreamEffectEvent, audit to ModActions mirroring ModCommandEngine's insert (≈line 252;
    //   Command="streamfx:"+verb, Tier=tier??"none", Argument=ChannelId, ExecutedBy=username), return "✨ '{verb}' triggered by {user}."
}
```

Wiring: (a) event gains `GuildId`, populated at construction; (b) `DiscordChatSubscriber` forwards when channel == mod channel **OR** `await connectedChannels.AcceptsCommandsAsync(botId, channelId)`; populates `ModChannelMessage.GuildId`; (c) `ChatViewModel.TryHandleDiscordModCommandAsync`: first `TryHandleAsync` the stream verbs (reply via the SAME path existing replies use — `SendDiscordReplyAsync` incl. error surface) and return; then before Engine A: `if (!await _connectedChannels.AcceptsCommandsAsync(evt.BotId, evt.ChannelId)) return;`. New ctor params `IConnectedChannelService`, `StreamEffectCommandRouter` + DI registrations. Tests (fakes for resolver/eventBus/IDataService): Mod runs `!flashlights #00FFAA 7` → event published with payload containing color+7 + `✨` reply + audit row; TrialMod → no event + `requires Mod tier`; Admin `!streamdeck hype` → StreamDeckAction `{"action":"hype"}`; SeniorMod `lightsoff` → LightScene `{"state":"off"}`; `!kick someone` → null. Steps: failing → FAIL → implement → PASS + build → commit `feat(mod): tier-gated stream effect commands routed from discord chat with channel gating`.

### Task 10: Stream-effect subscriber + DI wiring

**Files:** Create `UI\Services\StreamEffectsConfigService.cs`, `UI\Services\StreamEffectSubscriber.cs`, `DelegateStreamDeckStore` (10-line IStreamDeckStore adapter over two delegates, same file). Modify `App.xaml.cs`.

`StreamEffectsConfigService`: wraps IDataService config KV; `GetSnapshotAsync()` → `StreamEffectsSnapshot(Provider, BridgeIp, AppKey, LightIds, WebhookUrl, WebhookToken, FlashTimes=5, FlashIntervalMs=250, FlashColor="#FF00AA", DeckActionsJson)`; `SetAsync(key, value)`. `StreamEffectSubscriber(IEventBus, ILightsClient, IStreamDeckService, StreamEffectsConfigService)`: StartAsync subscribes StreamEffectEvent (mirror WorkflowTriggerService/ChatViewModel subscription style); dispatch: LightFlash → FlashAsync(payload color/times, snapshot defaults); LightScene → SetPowerAsync(state=="on"); StreamDeckAction → TriggerAsync(action); unknown → logged; `ILogger.LogInformation` per effect. DI: `AddSingleton<StreamEffectsConfigService>()`, `ILightsClient` → LightsManager over snapshot, `IStreamDeckStore` → DelegateStreamDeckStore(snapshot.DeckActionsJson / SetAsync), `IStreamDeckService`, `StreamEffectSubscriber`; start where DiscordChatSubscriber starts, try/catch+log. No unit tests (composition layer) — verified by build + Task 15. Commit `feat(lights): dispatch stream effect events to hue/webhook lights and stream deck`.

### Task 11: Chat page — Connected Channels tab + panel

**Files:** Create `UI\ViewModels\ConnectedChannelItem.cs`. Modify `ChatViewModel.cs`, `ChatView.xaml`.

`ConnectedChannelItem : ObservableObject`: init-only BotId/ChannelId/ChannelName/GuildName/ExistsInGuild + `[ObservableProperty]` MonitoringEnabled/IsModeration/IsAdmin/IsSupport/IsLogging + `PurposeSummary` ("General" when empty) + `PurposeCsv`.

ChatViewModel: `[ObservableProperty] bool _showChannelsPanel` (handler: exclusive with `_showDmPanel` mirroring `OnShowDmPanelChanged`; on show → `LoadConnectedChannelsAsync()`); `ConnectedChannels` collection; `RefreshChannelsCommand` → merge `bot.ConnectedGuilds` text channels (primary bot id resolved the same way `TryHandleDiscordModCommandAsync` gets `originBot`) with stored rows (stored values overlaid; stored rows missing from snapshot appended with `ExistsInGuild=false`, greyed); sorted monitored-first then name. Toggle commands persist via `_connectedChannels.UpsertAsync(...)` / `DeleteAsync` when nothing enabled; `SetAsMainChannelCommand` → `_discordService.SetSelectedChannelAsync`; `OpenChannelInChatCommand` → existing Discord channel-filter behavior + close panel. New ctor param `IConnectedChannelService` (DI registered in Task 9).

ChatView.xaml: third ToggleButton after Combined (≈ line 308, PackIcon `AccessPoint`, text "Channels", bound `ShowChannelsPanel`); chat-grid style gains `DataTrigger ShowChannelsPanel=True → Collapsed`; channels panel Grid (full area, BooleanToVisibilityConverter) with header (title + count + Refresh PackIcon `Refresh`), legacy-mode hint text, ListBox rows: PackIcon `Pound`, name + guild/purpose subtitle, Monitor/Mod/Admin/Support/Log checkboxes (`Command` + `CommandParameter` via `RelativeSource AncestorType=UserControl`; if that fights two-way IsChecked, use item-level `OnXxxChanged` persistence instead — match repo precedent), main-channel star (`StarOutline`), open (`ArrowRight`). Verify build + manual: persist across restart, mutual exclusion with DMs, chat unchanged when off. Commit `feat(chat): connected channels tab with purpose tags and monitor toggles`.

### Task 12: Moderator page — role tiers + command channels + templates

**Files:** Modify `ModeratorViewModel.cs`, `ModeratorView.xaml`.

ViewModel: `RoleTierRow : ObservableObject` (DiscordRoleId, RoleName, `[ObservableProperty] string? selectedTier`); `RoleTiers` + `LoadRolesCommand` (first guild from `ConnectedGuilds`, `GetGuildRolesAsync`, overlay `GetRoleTierAssignmentsAsync(botId)`); `SaveRoleTierCommand(row)` → Upsert or Delete (tier null/None); `CommandChannels` from `GetByPurposeAsync` Moderation+Admin (placeholder row "No command channels configured — commands work in every channel (legacy mode)"); extend the existing `ModCommandTemplate` list with `flashlights [#hex] [1-20]` (Mod), `lightson`/`lightsoff` (SeniorMod), `streamdeck <action name>` (Admin) matching the existing template shape.

View: "Command channels" card (ItemsControl, PackIcon `Pound`, purpose chip, placeholder text) + "Discord role tiers" card (ListBox rows: role name + tier ComboBox None/TrialMod/Mod/SeniorMod/Admin bound to SelectedTier, change → SaveRoleTierCommand; Load button). Verify build + manual (roles load when bot connected, tier persists across restart). Commit `feat(mod): role-to-tier mapping editor, command channel list, stream effect templates`.

### Task 13: Settings — "Stream FX" category

**Files:** Modify `SettingsViewModel.cs`, `SettingsView.xaml`.

ViewModel (inject StreamEffectsConfigService, ILightsClient, IStreamDeckService): properties SelectedLightProvider/HueBridgeIp/HueAppKey/WebhookUrl/WebhookToken/FlashColor/FlashTimes/FlashIntervalMs/HueLights(list Id/Name/Enabled)/DeckActions(list Id/Name/Uri)/NewDeckActionName/NewDeckActionUri/StatusMessage. Commands: LoadStreamFxSettings, SaveStreamFxSettings (all keys incl. LightIds CSV + StreamDeck.Actions JSON), DiscoverHueBridge (GET https://discovery.meethue.com → first internalipaddress), PairHueBridge (POST http://{ip}/api `{"devicetype":"vyralbot#streamfx","generateclientkey":true}`; type 101 error → "Press the link button on the bridge and retry"), LoadHueLights (GetLightsAsync), AddDeckAction (SaveActionAsync → result to StatusMessage), RemoveDeckAction, TestDeckAction (TriggerAsync), TestFlash (FlashAsync(FlashColor, FlashTimes, FlashIntervalMs)).

View: sidebar button category "StreamFx" (PackIcon `Flash`) + panel (EqualsToVisibilityConverter) with three cards mirroring existing markup: **Lights** (provider ComboBox None/Hue/Webhook; Hue IP + Discover + Pair + AppKey + light checkbox list; webhook URL/token + "POSTs {action,color,times} JSON — Home Assistant, SmartThings, LIFX cloud, etc."; flash defaults + Test); **Stream Deck** ("Requires Stream Deck app 6.4+. Paste an action link (streamdeck://…)."; name/URI + Add + list with Remove/Test); **Commands reference** (`!flashlights [#hex] [times]` Mod+, `!lightson`/`!lightsoff` SeniorMod+, `!streamdeck <name>` Admin; note: only in Moderation/Admin-tagged channels or the mod channel). Verify build + manual save/restart round-trip. Commit `feat(settings): stream FX category for hue/webhook lights and stream deck actions`.

### Task 14: OBS section — sidebar nav + UI

**Files:** Create `Views\ObsView.xaml` + `.cs`, `ViewModels\ObsViewModel.cs`. Modify `MainViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs` (VM registration mirroring IntegrationsViewModel).

ObsViewModel (inject ObsSceneCollectionStore): `Collections` rows (Id, Name, SceneCount, SourceCount, ImportedAtText), `SelectedCollection`, `DocumentTree` (file-local `record ObsTreeNode(string Name, string Detail, IReadOnlyList<ObsTreeNode> Children)`: root → Scenes (scene → items) / Sources / Audio), ImportCommand (exact FlowEditorViewModel:5597 OpenFileDialog pattern, Filter "OBS scene collection (*.json)|*.json"), LoadCollectionCommand (LoadDocumentAsync → tree), ExportCommand (SaveFileDialog Name+".json" → ExportToFileAsync → StatusMessage), DeleteCommand (mirror ClearHistoryCommand confirm convention), LoadCommand. ObsView: toolbar (Import/Refresh) + left 280px ListBox + right Browse/Export"(byte-identical)"/Delete + TreeView (HierarchicalDataTemplate over Children) + StatusMessage bottom; DynamicResource brushes only. Navigation: `[RelayCommand] NavigateToObsAsync` cloned from NavigateToIntegrationsCommand (CheckSetupInProgress, dispose previous, `Title = "Vyral - OBS"`); sidebar Button (PackIcon `Video`, label "OBS"); DataTemplate `ObsViewModel → ObsView`; add to DisposeNonEssential list mirroring IntegrationsViewModel. Verify build + manual: import fixture → Browse shows scenes/items incl. rot 12.5 + locked; export byte-identical (fc /b); delete works. Commit `feat(obs): sidebar OBS section with verbatim import, scene browser, byte-identical export`.

### Task 15: Final integration verification + repair pass

1. `dotnet build VyralBot\src\VyralBot.sln` + `dotnet test tests\VyralBot.UI.Tests\VyralBot.UI.Tests.csproj` — 0 errors, ALL tests green (existing suites intact).
2. Wiring audit (grep): IConnectedChannelService registered + in ChatViewModel; IModTierResolver impl registered + in router + engine; StreamEffectSubscriber started; LightsManager/IStreamDeckService registered; ObsViewModel + DataTemplate + sidebar + NavigateToObsCommand; DiscordChatMessageEvent.GuildId populated; ModChannelMessage.GuildId populated.
3. Manual smoke: Channels tab persists; Mod-tier `!flashlights` in Moderation-tagged channel → reply + subscriber log; untagged channel blocked (all channels when none configured); OBS byte-identical round-trip; Stream FX settings round-trip.
4. Dispatch code-reviewer over the full diff; fix Critical/Important findings.
5. Commit repairs: `fix: integration pass repairs for channels/streamfx/obs`.

---

## Subagent dispatch map (15 agents)

- **Wave 1 (parallel):** Tasks 1, 2, 3, 4
- **Wave 2 (parallel):** Tasks 5, 6, 7, 8 (5 and 8 need Task 1; 7 needs 1+4)
- **Wave 3:** Task 9 → then Task 10 (App.xaml.cs serialized) → then Tasks 11 + 12 (parallel)
- **Wave 4 (parallel):** Tasks 13, 14
- **Wave 5:** Task 15

Dispatch each task to a `code-writer` agent with: the task text verbatim + Global Constraints + Shared Contracts. Each agent reads its own anchor files first.
