# Theme System

## Overview
WPF resource dictionary based theming. Resources are defined in XAML resource dictionaries under the Resources directory.

## Source Files
- **Resources directory:** `src/VyralBot.UI/Resources/`
- **App.xaml:** `src/VyralBot.UI/App.xaml` — merges resource dictionaries

## Theme Application
Resource dictionaries are merged in `App.xaml` and applied globally. Styles, colors, and templates are defined as static resources.

## Key Directories
| Path | Description |
|------|-------------|
| `src/VyralBot.UI/Resources/` | Resource dictionaries (styles, colors, templates) |
| `src/VyralBot.UI/App.xaml` | Application resources and dictionary merging |
