# View Code-Behind Patterns

## Overview
Code-behind files for XAML views follow minimal patterns. Most logic is in ViewModels; code-behind is used primarily for UI-specific concerns that can't be handled in XAML bindings.

## Common Patterns

### DataContext Assignment
Most views receive their DataContext through DI or parent DataTemplate bindings. Some set it explicitly in constructor.

### UI Behaviors
Code-behind handles:
- Window close/minimize events
- WebView2 initialization (setup wizards)
- Scroll-to-bottom on new messages (ChatView)
- Drag-and-drop operations
- Focus management

### Controls
- **VariablePicker** — `src/VyralBot.UI/Controls/VariablePicker.xaml(.cs)` — reusable variable selection control
- **LazyPopupContent** — `src/VyralBot.UI/Controls/LazyPopupContent.cs` — deferred popup loading

### Behaviors
- **TextBoxVariableBehavior** — `src/VyralBot.UI/Behaviors/TextBoxVariableBehavior.cs` — attached behavior for variable insertion in text boxes

## Services in UI Layer
| File | Description |
|------|-------------|
| `src/VyralBot.UI/Services/AlertService.cs` | IAlertService implementation for alert display |
| `src/VyralBot.UI/Services/ProfileAwareDataService.cs` | IDataService wrapper routing to per-profile databases |
| `src/VyralBot.UI/Services/ProfileSettingsLoader.cs` | Loads profile settings on switch |
| `src/VyralBot.UI/Services/WebView2EnvironmentManager.cs` | WebView2 runtime management |
| `src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs` | IChatMessageStore database-backed implementation |

## MainWindow
- **XAML:** `src/VyralBot.UI/MainWindow.xaml`
- **Code-behind:** `src/VyralBot.UI/MainWindow.xaml.cs`
- DataContext set to `MainViewModel` in code-behind or via DI
