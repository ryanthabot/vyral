# Theming System — Tracking Plan

> Companion to: `2026-09-01-theming-system.md` (the implementation plan).
> This document tracks execution: task status, verification evidence, deviations, and issues.
> Update this file after every task; commit together with the task's code commit.

**Status legend:** `[ ]` pending · `[~]` in progress · `[x]` done · `[!]` blocked/issue

**Project status: ✅ COMPLETE** — all 8 tasks done, 22/22 tests pass, 0 build errors, launch-verified (native, crimson, and light themes all confirmed rendering).

---

## Task Checklist

### Task 1 — Theme foundation (models, catalog, shade generator, prefs service, tests)
- [x] Test project `tests/VyralBot.UI.Tests` created + added to solution
- [x] Failing tests written (ColorShadeGenerator, ThemeCatalog, UiPreferencesService)
- [x] Tests verified failing (types missing)
- [x] `UiPreferences.cs`, `ThemeOption.cs`, `ThemeCatalog.cs`, `ColorShadeGenerator.cs`, `UiPreferencesService.cs` implemented
- [x] Tests pass
- [x] Committed: `feat(themes): theme foundation - models, catalog, shade generator, preferences service (+tests)`

### Task 2 — ThemeBrushes.xaml + color dictionaries + App.xaml rewire
- [x] `Themes/ThemeBrushes.xaml` created (brushes/styles moved from RedSlate.xaml + Flow brushes + `AppLogoSource`)
- [x] `Themes/Colors/NativeColors.xaml` extracted verbatim from RedSlate (+ FlowCanvas block)
- [x] Light, Dark, Crimson, Azure, Emerald, Frost, Carbon, Ember, Custom dictionaries created
- [x] App.xaml merged dictionaries rewired; App.xaml scrollbar thumb → theme brushes
- [x] Build 0 errors; visuals identical to baseline (native)
- [x] `RedSlate.xaml` deleted, zero references remain
- [x] Committed: `feat(themes): split theme colors from brushes; add 10 theme palettes`

### Task 3 — ThemeManager + startup wiring + window icon
- [x] `Services/ThemeManager.cs` implemented (apply/switch, MD PaletteHelper sync, logo variant resolution + cache, ThemeChanged event)
- [x] App.xaml.cs: DI registration + `ThemeManager.Initialize()` before MainWindow shows
- [x] MainWindow icon from `ResolveLogoImage` + live update on ThemeChanged
- [x] Build 0 errors; startup smoke test passes
- [x] Committed: `feat(themes): ThemeManager runtime switching + logo variant resolution`

### Task 4 — Theme picker UI + SettingsViewModel
- [x] `ThemeEqualsConverter` + `HexToColorConverter` created and registered in App.xaml
- [x] SettingsViewModel: Themes collection, SelectTheme/ApplyCustomTheme/SelectCustomAccent commands, custom state props, InitializeAsync loads current theme state
- [x] SettingsView Theme panel: 3×3 theme card grid with swatch previews + selected indicator; custom builder (base toggle, 16 swatches, hex field, logo variant combo, apply button); logo variant note
- [x] Build 0 errors; Settings → Theme renders (verified in running app)
- [x] Committed: `feat(themes): theme picker UI with built-ins + custom theme builder`

### Task 5 — Dynamic logo across views
- [x] MainWindow.xaml title bar + window Icon, ChatView.xaml (3×) → `{DynamicResource AppLogoSource}`
- [x] Build 0 errors; logo renders
- [x] Committed: `feat(themes): bind logo images to AppLogoSource for per-theme logo variants`

### Task 6 — Hardcoded color cleanup
- [x] CurrencyView.xaml (65 hexes → theme brushes; VS-gray palette eliminated)
- [x] EventHistoryView.xaml (33), GiveawayView.xaml (12) → theme/semantic brushes
- [x] FlowEditorView.xaml — canvas surface/text brushes re-pointed to `FlowCanvas*` resources; node-type accent colors and literal swatch-picker parameters intentionally kept
- [x] Minor files: ConditionalRulesView (VS-gray), PollView (semantic), VariablesView (semantic + overlays), DocumentationView, WebServerSettingsView, YouTubeWebSetupView, DiscordWebSetupView
- [x] Light-theme readability verified in running app (no dark-on-dark text)
- [x] Committed: `refactor(themes): replace hardcoded colors with theme brushes across views`

### Task 7 — FlowCanvas theme-aware palette
- [x] `FlowCanvasPalette` gains a value→resource-key reverse map; `Color(hex)` resolves themed `FlowCanvas*` resources first, consts remain fallbacks — zero call-site churn
- [x] Build 0 errors
- [x] Committed: `feat(themes): theme-aware FlowCanvas palette`

### Task 8 — Full verification + docs
- [x] `dotnet test` — 22/22 pass (incl. camelCase JSON regression test)
- [x] `dotnet build VyralBot.sln` (Debug + Release) — 0 errors
- [x] Launch-verified via `appdata/ui-settings.json`: `Theme applied: native` → teal UI; `Theme applied: crimson` → dark red UI (screenshot); `Theme applied: light` → light UI, fully readable (screenshot); restored to native + app left running
- [x] `docs/ui/THEMING.md` written
- [x] Committed: `docs: theming system complete`

---

## Theme Registry (shipped)

| Id | Name | Base | Accent | Logo variant | Dictionary |
|---|---|---|---|---|---|
| native | Native Teal | Dark | #14B8A6 | native | `Themes/Colors/NativeColors.xaml` |
| light | Light | Light | #0D9488 | native | `Themes/Colors/LightColors.xaml` |
| dark | Dark | Dark | #14B8A6 | white | `Themes/Colors/DarkColors.xaml` |
| crimson | Crimson Forge | Dark | #EF4444 | red | `Themes/Colors/CrimsonColors.xaml` |
| azure | Azure Depths | Dark | #3B82F6 | blue | `Themes/Colors/AzureColors.xaml` |
| emerald | Emerald Grove | Dark | #10B981 | green | `Themes/Colors/EmeraldColors.xaml` |
| frost | Arctic Frost | Light | #0284C7 | black | `Themes/Colors/FrostColors.xaml` |
| carbon | Carbon Graphite | Dark | #94A3B8 | white | `Themes/Colors/CarbonColors.xaml` |
| ember | Sunset Ember | Dark | #F97316 | native | `Themes/Colors/EmberColors.xaml` |
| custom | Custom | user | user | user | base dict + runtime overrides |

**Logo variant contract:** drop `vyral-logo-red.png`, `vyral-logo-blue.png`, `vyral-logo-green.png`, `vyral-logo-white.png`, `vyral-logo-black.png` into `src/VyralBot.UI/Resources/Images/` and rebuild — themes pick them up automatically; missing variants fall back to `vyral-logo.png` (native teal). The csproj wildcard `Resource Include="Resources\Images\*.png"` already embeds new files.

**Preferences:** `appdata/ui-settings.json` — `{"themeId": "...", "custom": {"baseTheme": "Dark|Light", "primaryHex": "#RRGGBB", "logoVariant": "native|red|blue|green|white|black"}}`.

---

## Verification Log

| When | What | Result |
|---|---|---|
| 2026-09-01 | Baseline `dotnet build VyralBot.sln` | 0 errors, 12 pre-existing NU1903 warnings |
| Task 1 | `dotnet test` (initial) | 21/21 pass |
| Task 8 | `dotnet test` (final) | 22/22 pass |
| Task 8 | `dotnet build` Debug + Release | 0 errors |
| Task 8 | Launch: persisted `native` | `Theme applied: native` — teal UI confirmed |
| Task 8 | Launch: persisted `crimson` | `Theme applied: crimson` — dark red UI confirmed (screenshot) |
| Task 8 | Launch: persisted `light` | `Theme applied: light` — light UI readable, no dark-on-dark (screenshot) |
| Task 8 | Restart persistence | theme re-applies before window renders each launch |

## Deviations from the main plan

- **UiPreferencesService JSON options:** the plan's code serialized with `JsonOptions` but deserialized without them; `System.Text.Json` is case-sensitive by default, so a camelCase `themeId` silently loaded as native. Fixed (`PropertyNameCaseInsensitive = true` + options passed to `Deserialize`) and locked in with the `Load_reads_camel_case_json` regression test.
- **LogoVariantExists:** `StreamResourceInfo` is not `IDisposable`; dropped the `using` (plan code didn't compile as written).
- **Test project path:** solution lives at `VyralBot/src/`, tests at repo root — ProjectReference is `..\..\VyralBot\src\...` (plan had `..\..\src\...`).
- **FlowCanvas approach:** instead of adding per-call-site helpers, `FlowCanvasPalette.Color()` resolves themed resources via a value→resource-key reverse map — const strings stay source-compatible everywhere.
- **Task 6 scope:** AlertOverlayWindow (#DD000000 backdrop, cyan alert accents) and KickDashboard (#1A1A1A/#53FC18 Kick-brand surfaces) kept hardcoded intentionally — they are platform/alert-brand surfaces, not theme colors.
- **Picker click-through in the live app was not driven by automation** (the user's screen had an active Discord video making click targets unstable); the picker calls the same `ThemeManager.ApplyTheme` path that was verified three times via persisted settings at startup, and the panel was verified rendering in Settings.

## Issues / Decisions Log

- 2026-09-01: `ThemeOption` selected-state: record is immutable, so the picker's selected indicator uses a `ThemeEqualsConverter` MultiBinding comparing item Id to `SelectedTheme.Id` instead of a mutable IsSelected flag.
- 2026-09-01: Theme stored app-level (`appdata/ui-settings.json`), NOT per profile — theme is a device preference; profile DB load happens after startup and would flash the wrong theme.
- 2026-09-01: OneDrive synced an external YouTube-dashboard feature into the workspace mid-session (incl. a transient syntax error in `YouTubeDashboardViewModel.cs` that was corrected by the next sync). Folded in as a separate commit; theme work built on top.
- 2026-09-01: Found two VyralBot processes after test-launch (single-instance mutex message box); killed the duplicate, kept one test instance, and left the app running on native at the end.

## Survivors Log (intentionally hardcoded hexes after Task 6)

- Platform brand colors: Twitch `#9146FF` family, Kick `#53FC18`, YouTube `#FF0000`/`#CC0000`, TikTok `#00F2EA`/`#FF0050`, Discord `#5865F2` — brand identity, constant across themes.
- `FlowEditorView.xaml`: node-type accent brushes (indigo/purple/violet/green/red/blue/pink/yellow), `FlowInkBrush`, light-element grays (`#E5E7EB`, `#F3F4F6`, `#7F1D1D`), and the swatch-picker `CommandParameter` hexes (feature payload).
- `AlertOverlayWindow.xaml`: `#DD000000` backdrop + cyan accents — OBS overlay design is intentionally theme-independent.
- `KickDashboardView.xaml`: Kick-brand dark surfaces (`#1A1A1A`/`#0F0F0F`) beside the Kick green.
- Decorative translucent tints (`#2010B981`, `#1A4CAF50`, `#1AF44336`, `#33E05252`) — alpha tints that read correctly on both light and dark bases.
- `DropShadowEffect Color="#000000"` — shadows.

---

## Final Summary

**Shipped:** a complete theming system — 9 built-in themes (Native Teal, Light, Dark, Crimson Forge, Azure Depths, Emerald Grove, Arctic Frost, Carbon Graphite, Sunset Ember) + a custom theme builder (light/dark base, accent swatches/hex, auto-generated hover/pressed shades), per-theme logo color variants with automatic fallback until the variant PNGs are added, live switching from Settings → Theme, and app-level persistence that re-applies before the window renders.

**Numbers:** 10 new XAML theme dictionaries + 1 shared brush dictionary; 6 new C# files in the UI project (models/services); 2 new converters; 1 new test project (22 tests); ~15 views de-hardcoded; 2 docs written. Build: 0 errors. Tests: 22/22.

**Known limitations:** already-open windows show baked canvas-node colors in the flow editor until reopened after a switch; MaterialDesign's own control chrome updates via PaletteHelper (verified live); adding an 11th logo variant name requires extending `ThemeCatalog.ValidLogoVariants`.
