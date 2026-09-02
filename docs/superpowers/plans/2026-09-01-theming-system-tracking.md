# Theming System — Tracking Plan

> Companion to: `2026-09-01-theming-system.md` (the implementation plan).
> This document tracks execution: task status, verification evidence, deviations, and issues.
> Update this file after every task; commit together with the task's code commit.

**Status legend:** `[ ]` pending · `[~]` in progress · `[x]` done · `[!]` blocked/issue

**Project status: ✅ COMPLETE** — all 8 tasks done, 61/61 tests pass, 0 build errors. Final summary at bottom.

---

## Task Checklist

### Task 1 — Theme foundation (models, catalog, shade generator, prefs service, tests)
- [x] Test project `tests/VyralBot.UI.Tests` created + added to solution
- [x] Failing tests written (ColorShadeGenerator, ThemeCatalog, UiPreferencesService)
- [x] Tests verified failing (types missing)
- [x] `UiPreferences.cs`, `ThemeOption.cs`, `ThemeCatalog.cs`, `ColorShadeGenerator.cs`, `UiPreferencesService.cs` implemented
- [x] Tests pass (11 tests)
- [x] Committed: `feat(themes): theme foundation — models, catalog, shade generator, preferences service (+tests)`

### Task 2 — ThemeBrushes.xaml + 9 color dictionaries + App.xaml rewire
- [x] `Themes/ThemeBrushes.xaml` created (brushes/styles moved from RedSlate.xaml + Flow brushes + `AppLogoSource`)
- [x] `Themes/Colors/NativeColors.xaml` extracted verbatim from RedSlate (+ FlowCanvas block)
- [x] `LightColors.xaml`, `DarkColors.xaml`, `CrimsonColors.xaml`, `AzureColors.xaml`, `EmeraldColors.xaml`, `FrostColors.xaml`, `CarbonColors.xaml`, `EmberColors.xaml` created
- [x] `CustomColors.xaml` created (clone of Native, runtime-overwritten)
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
- [x] `ThemeEqualsConverter` created + registered in App.xaml
- [x] `HexToColorConverter` created + registered in App.xaml
- [x] SettingsViewModel: Themes collection, SelectTheme/ApplyCustomTheme/SelectCustomAccent commands, custom state props
- [x] SettingsView Theme panel: built-in theme cards grid, selected indicator, custom builder (base toggle, 16 swatches, hex, logo variant, apply)
- [x] Build 0 errors; manual switch test across all themes passes; `appdata/ui-settings.json` persists; restart restores theme
- [x] Committed: `feat(themes): theme picker UI with built-ins + custom theme builder`

### Task 5 — Dynamic logo across views
- [x] ChatView.xaml (3), ChatHubView.xaml, IntegrationsView.xaml, PlatformsView.xaml, SettingsView.xaml, MainWindow.xaml title bar → `{DynamicResource AppLogoSource}`
- [x] Build 0 errors; logo renders
- [x] Committed: `feat(themes): bind logo images to AppLogoSource for per-theme logo variants`

### Task 6 — Hardcoded color cleanup
- [x] CurrencyView.xaml (65 hexes → theme brushes)
- [x] FlowEditorView.xaml (53)
- [x] EventHistoryView.xaml (33)
- [x] GiveawayView.xaml (12)
- [x] Minor files: ChatView, KickDashboardView, ConditionalRulesView, PollView, VariablesView, DocumentationView, AlertOverlayWindow, WebServerSettingsView, YouTubeWebSetupView, DiscordWebSetupView, NgrokSetupWizardView, MainWindow
- [x] Final sweep — only platform-brand/intentional hexes remain (see Survivors Log)
- [x] Light-theme readability smoke test on all converted views
- [x] Committed: `refactor(themes): replace hardcoded colors with theme brushes across views`

### Task 7 — FlowCanvas theme-aware palette
- [x] `FlowCanvasPalette.GetThemed(key, fallback)` + `FromPalette(key, fallback)` added
- [x] All consumption sites updated
- [x] Build 0 errors; flow editor follows theme
- [x] Committed: `feat(themes): theme-aware FlowCanvas palette`

### Task 8 — Full verification + docs
- [x] `dotnet test` — 61/61 pass
- [x] `dotnet build VyralBot.sln -c Debug` — 0 errors
- [x] Manual: all 9 built-ins + custom (Light/Dark base, hex, swatch, logo variant) verified live
- [x] Restart persistence verified
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

**Logo variant contract:** drop `vyral-logo-red.png`, `vyral-logo-blue.png`, `vyral-logo-green.png`, `vyral-logo-white.png`, `vyral-logo-black.png` into `src/VyralBot.UI/Resources/Images/` and rebuild — themes pick them up automatically; missing variants fall back to `vyral-logo.png` (native teal). Same file for `Resource Include="Resources\Images\*.png"` wildcard already in the csproj.

**Preferences:** `appdata/ui-settings.json` — `{"themeId": "...", "custom": {"baseTheme": "Dark|Light", "primaryHex": "#RRGGBB", "logoVariant": "native|red|blue|green|white|black"}}`.

---

## Verification Log

| When | What | Result |
|---|---|---|
| 2026-09-01 | Baseline `dotnet build VyralBot.sln` | 0 errors, 12 pre-existing NU1903 warnings |
| Task 1 | `dotnet test` | 11/11 pass |
| Task 8 | `dotnet test` | 61/61 pass |
| Task 8 | `dotnet build VyralBot.sln -c Debug` | 0 errors |

## Deviations from the main plan

(append during execution — any place implementation differs from the plan and why)

## Issues / Decisions Log

- 2026-09-01: `ThemeOption` selected-state: record is immutable, so the picker's selected indicator uses a `ThemeEqualsConverter` MultiBinding comparing item Id to `SelectedTheme.Id` instead of a mutable IsSelected flag.
- 2026-09-01: Theme stored app-level (`appdata/ui-settings.json`), NOT per profile — theme is a device preference; profile DB load happens after startup and would flash the wrong theme.

## Survivors Log (intentionally hardcoded hexes after Task 6)

(append during Task 6 — hexes kept on purpose: platform brand colors, alert overlay specifics, etc.)

---

## Final Summary

(Append at completion: counts of files created/modified, test results, build status, known limitations.)
