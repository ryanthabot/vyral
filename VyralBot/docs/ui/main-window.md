# Main Window

## Overview
Application's main window hosting the navigation sidebar and content area.

## Source Files
- **XAML:** `src/VyralBot.UI/MainWindow.xaml`
- **Code-behind:** `src/VyralBot.UI/MainWindow.xaml.cs`
- **DataContext:** `MainViewModel` (set in code-behind)

## Layout Structure
- Sidebar (left) — navigation items with expand/collapse sections
- Content area (right) — displays `CurrentViewModel` via DataTemplates

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/MainWindow.xaml` | Main window XAML |
| `src/VyralBot.UI/MainWindow.xaml.cs` | Code-behind |
