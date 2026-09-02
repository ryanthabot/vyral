# VyralBot Theming System

How the app's theme system works, how to switch themes, and how to extend it.

## Using Themes

**Settings → Theme** in the app:

- **Built-in Themes** — 9 one-click cards: Native Teal (default), Light, Dark, Crimson Forge, Azure Depths, Emerald Grove, Arctic Frost, Carbon Graphite, Sunset Ember.
- **Custom Theme** — pick a Light or Dark base, an accent color (swatch grid or `#RRGGBB` hex), and a logo color; then *Apply Custom Theme*. Hover/pressed/light/dark shades are generated automatically from your accent.

Selection persists to `appdata/ui-settings.json` (app-level, shared by all profiles) and is re-applied at startup before the main window renders:

```json
{
  "themeId": "crimson",
  "custom": { "baseTheme": "Dark", "primaryHex": "#EF4444", "logoVariant": "red" }
}
```

## Logo Color Variants

Every theme declares a preferred logo color. The app looks for
`src/VyralBot.UI/Resources/Images/vyral-logo-{variant}.png` and falls back to
`vyral-logo.png` (the native teal logo) when the variant file doesn't exist.

Supported variant names: `native`, `red`, `blue`, `green`, `white`, `black`.

**To add a logo color:** drop `vyral-logo-red.png` (etc.) into
`src/VyralBot.UI/Resources/Images/` and rebuild — the csproj's
`Resource Include="Resources\Images\*.png"` wildcard picks it up and themes
requesting that variant display it automatically (title bar, chat, window/taskbar icon).

## Architecture

```
App.xaml merged dictionaries:
  1. MaterialDesign BundledTheme + MaterialDesign3.Defaults
  2. Themes/ThemeBrushes.xaml        ← all brushes/styles, defined ONCE
  3. Themes/Colors/{Theme}Colors.xaml ← ONLY Color entries, swapped at runtime
```

- **ThemeBrushes.xaml** defines every `SolidColorBrush` as
  `Color="{DynamicResource SomeColor}"`. Because brushes never change identity,
  every `StaticResource`/`DynamicResource` brush reference in the app's XAML
  re-resolves its color the moment the colors dictionary is swapped — that is
  the entire switch mechanism.
- **ThemeManager** (`Services/ThemeManager.cs`) swaps the colors dictionary,
  syncs MaterialDesign's base theme + primary color via `PaletteHelper`,
  resolves the logo variant into the `AppLogoSource` resource, persists the
  choice, and raises `ThemeChanged` (MainWindow uses it to update its icon).
- **Custom themes** load the Light or Dark colors dictionary and then override
  the accent-derived keys (`PrimaryColor`, `PrimaryHover`, `BorderAccent`,
  `InputFocusBorder`, `GlassBorder`, ...) with shades from
  `ColorShadeGenerator` (HSL math).
- **Flow canvas** (`Controls/FlowCanvas/FlowCanvasPalette.cs`) resolves its
  surface/text roles from the theme's `FlowCanvas*` color resources at draw
  time; node-type accent colors (trigger amber, condition green, etc.) stay
  fixed by design.

### The color key set

Every dictionary in `Themes/Colors/` must define the **same key set** (about
85 keys): primary shades, backgrounds/surfaces, overlays, text, borders,
semantics, platform brands, inputs, chat badges, and the FlowCanvas block.
Platform brand colors (Twitch purple, Kick green, YouTube red, TikTok,
Discord blurple) and chat badge colors are identical in every theme.

## Adding a Built-in Theme

1. Create `Themes/Colors/{Name}Colors.xaml` — copy an existing dark or light
   dictionary and change the palette values (keep every key).
2. Add a `ThemeOption` row in `Models/ThemeCatalog.cs`
   (id, display name, isLight, accent hex, logo variant, preview bg hex).
3. Rebuild — the picker card, startup application, and MaterialDesign sync all
   derive from the catalog + the dictionary.

## Tests

`tests/VyralBot.UI.Tests/` covers the pure logic: shade generation, hex
parsing, catalog integrity, and preferences persistence (including the
camelCase JSON regression). Run with:

```
dotnet test tests/VyralBot.UI.Tests/VyralBot.UI.Tests.csproj
```
