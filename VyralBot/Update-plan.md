## Home Page Redesign Plan (v3)

### Overview of ALL Changes

---

### Phase 1: Home Page Cards — Reorder, Rename, Replace

**Current (MainWindow.xaml Dashboard DataTemplate, ~lines 509-630):**

| Position | Card | Command |
|---|---|---|
| Col 0 | Actions | None |
| Col 2 | Commands | None |
| Col 4 | Platforms | `NavigateToPlatformsCommand` |
| Col 6 | Stream Apps | None |

**New:**

| Position | Card | Icon | Description | Command |
|---|---|---|---|---|
| Col 0 | **Connections** | `Connection` | "Manage your platform connections" | `NavigateToPlatformsCommand` (existing) |
| Col 2 | **Chat** | `Chat` | "Complete chat management for all platforms" | **New** `NavigateToChatHubCommand` |
| Col 4 | **Tools** | `Toolbox` | "Stream tools, chat features, and more" | **New** `NavigateToToolsCommand` |
| Col 6 | **Integrations** | `Puzzle` | "Third-party integrations and bots" | **New** `NavigateToIntegrationsCommand` |

---

### Phase 2: "Connections" Title Update

Only the window title when viewing the Platforms page:

| File | Change |
|---|---|
| MainViewModel.cs | `"VyralBot - Platforms"` → `"VyralBot - Connections"` (2 occurrences) |

Sidebar "Platforms" label, class names, command names — all **unchanged**.

---

### Phase 3: New "Chat Hub" View (the big one)

This is **NOT** the existing sidebar Chat. This is a brand-new comprehensive chat management section.

**New files:**
| File | Purpose |
|---|---|
| `ViewModels/ChatHubViewModel.cs` | Manages 5 platform tabs, loads all per-platform settings |
| `Views/ChatHubView.xaml` + `.xaml.cs` | Tabbed UI with platform buttons at top |

**ChatHubViewModel responsibilities:**
- `SelectedPlatform` property (string: "Twitch", "Kick", "YouTube", "TikTok", "Discord")
- `IsTwitchConnected`, `IsKickConnected`, `IsYouTubeConnected`, `IsTikTokConnected`, `IsDiscordConnected` — loaded from DB
- Embeds existing per-platform chat settings VMs: `TwitchChatSettingsViewModel`, `KickChatSettingsViewModel`, `YoutubeChatSettingsViewModel`, `TiktokChatSettingsViewModel`, `DiscordChatSettingsViewModel`
- Per-platform feature participation toggles (loaded from DB/settings):
  - **Chat enabled** (uses existing `Platform.TwitchChatEnabled` etc.)
  - **Auto-start chat**
  - **Auto-replies enabled**
  - **Commands enabled on this platform** (mirrors `CommandsViewModel.EnabledOnTwitch`/`EnabledOnKick`)
  - **Giveaway participation** (mirrors `Giveaway.AllowedPlatforms` — shows which platforms can enter)
  - **Poll participation** (same pattern)
  - **Currency earning enabled**
  - All event-specific toggles (follow show-in-chat, auto-reply, messages — already in the per-platform chat settings VMs)

**Layout:**

```
┌──────────────────────────────────────────────────────────┐
│ ← BACK TO HOME                                           │
│                                                           │
│ Chat Management                                           │
│ Complete chat settings for all platforms                   │
│                                                           │
│ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐  │
│ │ Twitch │ │  Kick  │ │YouTube │ │ TikTok │ │Discord │  │
│ │  [img] │ │ [img]  │ │ [img]  │ │ [img]  │ │ [img]  │  │
│ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘  │
│                                                           │
│ ═══════════════════════════════════════════════════════   │
│                                                           │
│  (Selected: Twitch)          [🔴 Not Connected - grayed] │
│                               OR [🟢 Connected]          │
│                                                           │
│  ─── General ───                                          │
│  ☐ Chat Enabled              ☐ Auto-Start Chat           │
│  ☐ Auto-Replies Enabled                                   │
│                                                           │
│  ─── Feature Participation ───                            │
│  ☐ Commands on this platform  ☐ Currency earning          │
│  ☐ Giveaway entries           ☐ Poll voting               │
│                                                           │
│  ─── Event Settings ───                                   │
│  (Expanders for each event type with show-in-chat,        │
│   auto-reply toggle, and auto-reply message — the same    │
│   content as TwitchChatSettingsView/KickChatSettingsView)  │
│                                                           │
│  ─── Follow Events ───                                    │
│  ☐ Show in chat    ☐ Auto-reply                           │
│  Message: [Thanks for the follow @{username}!           ] │
│                                                           │
│  ─── Subscribe Events ───                                 │
│  ... (etc for all platform-specific events)               │
│                                                           │
│  [Save Settings]                                          │
└──────────────────────────────────────────────────────────┘
```

**Key behavior:**
- **Disconnected platforms:** Tab is visible but content is grayed out / disabled with a banner "Connect this platform in Connections to manage chat settings"
- **Connected platforms:** Fully editable, all toggles active
- Each platform tab shows different events (Twitch has Follow/Sub/Resub/GiftSub/Raid/Cheer; Kick has Follow/Sub/GiftSub/Host/KicksGifted/RewardRedeemed/StreamStatus; YouTube/TikTok/Discord are simpler with general toggles)

**Existing per-platform chat settings VMs already exist** — the ChatHubViewModel creates scoped instances and delegates to them. The event settings XAML can be reused or inlined.

---

### Phase 4: New "Tools" Hub View

Same as before — a launcher page.

**New files:**
| File | Purpose |
|---|---|
| `ViewModels/ToolsViewModel.cs` | Lightweight VM |
| `Views/ToolsView.xaml` + `.xaml.cs` | Two-category grid of card buttons |

**Chat Features section (4 buttons):** Events & Alerts, Currency & Rewards, Commands, Event History
**Stream Tools section (9 buttons):** Browser Sources, Audio Settings, Sounds, Statistics, Visual Alerts, Conditional Rules, Giveaways, Polls, Watchtime

Each button uses existing `MainViewModel` commands via `RelativeSource AncestorType=Window`.

---

### Phase 5: New "Integrations" Hub View

**New files:**
| File | Purpose |
|---|---|
| `ViewModels/IntegrationsViewModel.cs` | Lightweight VM |
| `Views/IntegrationsView.xaml` + `.xaml.cs` | Integration cards grid |
| `Resources/Images/botghost-logo.png` | BotGhost mascot image |

Shows a BotGhost card button (placeholder — no functional integration yet).

---

### Phase 6: Documentation Navigation

**New files:**
| File | Purpose |
|---|---|
| `ViewModels/DocumentationViewModel.cs` | Lightweight VM |
| `Views/DocumentationView.xaml` + `.xaml.cs` | Placeholder docs page |

**MainWindow.xaml:** Add command binding to Documentation border in Support section.

---

### Phase 7: DI Registration + MainViewModel Wiring

**MainViewModel.cs additions:**
- Fields: `_chatHubScope`, `_chatHubViewModel`, `_toolsScope`, `_toolsViewModel`, `_integrationsScope`, `_integrationsViewModel`, `_documentationScope`, `_documentationViewModel`
- Commands: `NavigateToChatHubCommand`, `NavigateToToolsCommand`, `NavigateToIntegrationsCommand`, `NavigateToDocumentationCommand`
- Dispose entries for all 4 new VMs in `DisposeNonEssentialViewModelsAsync`

**DI file:** Register `ChatHubViewModel`, `ToolsViewModel`, `IntegrationsViewModel`, `DocumentationViewModel`

**MainWindow.xaml:** Add 4 DataTemplate mappings

---

### Complete File Summary

| Action | File |
|---|---|
| **Edit** | MainWindow.xaml — Replace 4 home cards, add 4 DataTemplates, Documentation command |
| **Edit** | MainViewModel.cs — 2 title renames, 4 new nav commands + fields + dispose |
| **Edit** | DI registration — 4 new VMs |
| **Create** | `ChatHubViewModel.cs` — **Complex** — 5-tab platform manager with settings |
| **Create** | `ChatHubView.xaml` + `.cs` — Tabbed UI |
| **Create** | `ToolsViewModel.cs` — Lightweight |
| **Create** | `ToolsView.xaml` + `.cs` — Category grid |
| **Create** | `IntegrationsViewModel.cs` — Lightweight |
| **Create** | `IntegrationsView.xaml` + `.cs` — Cards grid |
| **Create** | `DocumentationViewModel.cs` — Lightweight |
| **Create** | `DocumentationView.xaml` + `.cs` — Placeholder |
| **Locate** | `botghost-logo.png` — Image asset |

### What is NOT touched
- Sidebar navigation (labels, commands, dropdowns — all unchanged)
- Existing sidebar "Chat" button → still goes to `ChatViewModel` (quick chat view)
- Class names (`PlatformsViewModel`, `ChatViewModel`, etc.)
- Existing per-platform chat settings VMs (reused by ChatHubViewModel)
- Any existing view functionality

### Key Distinction: Sidebar Chat vs Home Chat Hub

| | Sidebar "Chat" | Home "Chat" Card |
|---|---|---|
| **ViewModel** | `ChatViewModel` (existing) | `ChatHubViewModel` (new) |
| **Purpose** | Quick live chat — send/receive messages, filter by platform | Comprehensive settings — all event toggles, feature participation, per-platform config |
| **Content** | Message list, input box, platform filter | Tabbed platform settings with event configs |
| **Navigation** | Sidebar Chat button | Home page Chat card only |
