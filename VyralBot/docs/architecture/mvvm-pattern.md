# MVVM Pattern

VyralBot uses the **Model-View-ViewModel (MVVM)** pattern across the WPF UI layer, powered by [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) source generators and Microsoft.Extensions.DependencyInjection.

---

## 1. Pattern Overview

```
View (.xaml / .xaml.cs)
  │  DataContext binding
  ▼
ViewModel (: ViewModelBase)
  │  constructor-injected services
  ▼
Services / Data Layer (Core, Integrations, Data)
```

- **Views** are WPF `UserControl` / `Window` classes. They contain XAML layout and minimal code-behind.
- **ViewModels** inherit from `ViewModelBase`. They expose observable properties and commands that Views bind to.
- **Services** (interfaces in `VyralBot.Core`, implementations in `VyralBot.Integrations` / `VyralBot.Data`) handle business logic, platform I/O, and persistence.
- All wiring is done through **constructor injection** via `Host.CreateDefaultBuilder()` in `App.xaml.cs`.

---

## 2. ViewModelBase

**File:** `src/VyralBot.UI/ViewModels/ViewModelBase.cs`

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Base ViewModel class with property change notifications.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
}
```

Key points:

| Aspect | Detail |
|---|---|
| Base class | `ObservableObject` (from CommunityToolkit.Mvvm) |
| `partial` keyword | **Required** — the CommunityToolkit source generators emit companion partial classes for `[ObservableProperty]` and `[RelayCommand]` attributes. |
| `IDisposable` | **Not implemented.** ViewModelBase has no `Dispose()` method. ViewModels that need cleanup expose a `Cleanup()` method instead. |
| `INotifyPropertyChanged` | Provided automatically by `ObservableObject`. |

---

## 3. Property Change Notification with `[ObservableProperty]`

The `[ObservableProperty]` attribute on a private field causes the source generator to emit a public property with `INotifyPropertyChanged` support.

```csharp
public partial class ChatViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private bool _isKickConnected;

    [ObservableProperty]
    private bool _twitchFilterEnabled = true;

    [ObservableProperty]
    private bool _kickFilterEnabled = true;
}
```

The generator produces (conceptually):

```csharp
public bool IsTwitchConnected
{
    get => _isTwitchConnected;
    set => SetProperty(ref _isTwitchConnected, value);
}
```

**Naming convention:** A private field `_fooBar` generates a public property `FooBar`.

For collections that must notify the UI of item-level changes, use `ObservableCollection<T>`:

```csharp
public ObservableCollection<ChatMessage> Messages { get; } = new();
```

---

## 4. Commands with `[RelayCommand]`

The `[RelayCommand]` attribute on a method generates an `IRelayCommand` property that Views bind to.

```csharp
// Async command — generates LoadHistoryCommand (IAsyncRelayCommand)
[RelayCommand]
private async Task LoadHistoryAsync()
{
    // ...
}

// Sync command — generates ClearChatCommand (IRelayCommand)
[RelayCommand]
private void ClearChat()
{
    // ...
}
```

**Naming convention:** A method `DoSomethingAsync` or `DoSomething` generates a property `DoSomethingCommand`.

XAML binding:

```xml
<Button Command="{Binding LoadHistoryCommand}" Content="Load History" />
<Button Command="{Binding ClearChatCommand}" Content="Clear" />
```

---

## 5. View–ViewModel Binding via DI

Views receive their ViewModel through **constructor injection** and assign `DataContext` in code-behind.

**Typical View code-behind** (e.g. `SettingsView.xaml.cs`):

```csharp
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public SettingsView(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
```

Some Views (like `ChatView`) also subscribe to `DataContextChanged` / `Loaded` / `Unloaded` to manage lifecycle hooks on the ViewModel:

```csharp
public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;

    public ChatView()
    {
        InitializeComponent();
        Loaded += ChatView_Loaded;
        Unloaded += ChatView_Unloaded;
        DataContextChanged += ChatView_DataContextChanged;
    }
}
```

**Principle:** Keep code-behind minimal — only lifecycle wiring and platform-specific behavior (scroll tracking, password boxes, etc.) that can't be expressed in XAML bindings.

---

## 6. EventBus Integration Pattern

ViewModels communicate with services and each other through `IEventBus` — an async pub/sub system.

### IEventBus API

**File:** `src/VyralBot.Core/Services/EventBus/IEventBus.cs`

```csharp
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    Task<Guid> SubscribeAsync<TEvent>(Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    void Unsubscribe(Guid subscriptionId);

    int SubscriptionCount { get; }
}
```

### Subscribe pattern

In `InitializeAsync()`, subscribe and store the returned `Guid`:

```csharp
private Guid _twitchSubscriptionId;
private Guid _kickSubscriptionId;

public async Task InitializeAsync()
{
    _twitchSubscriptionId = await _eventBus.SubscribeAsync<TwitchChatMessageEvent>(
        OnTwitchChatMessageReceivedAsync);

    _kickSubscriptionId = await _eventBus.SubscribeAsync<KickChatMessageEvent>(
        OnKickChatMessageReceivedAsync);
}
```

### Unsubscribe pattern

In `Cleanup()`, unsubscribe each stored Guid:

```csharp
public void Cleanup()
{
    if (_twitchSubscriptionId != Guid.Empty)
        _eventBus.Unsubscribe(_twitchSubscriptionId);

    if (_kickSubscriptionId != Guid.Empty)
        _eventBus.Unsubscribe(_kickSubscriptionId);
}
```

### Important

- `SubscribeAsync` returns `Task<Guid>` — always `await` it and store the ID.
- `Unsubscribe` takes the `Guid` — it does **not** take a type parameter.
- Guard with `!= Guid.Empty` before unsubscribing to avoid unnecessary calls.
- Singleton ViewModels should use an `_isInitialized` flag to prevent duplicate subscriptions if `InitializeAsync()` is called more than once.

---

## 7. DI Lifetime Reference

All registrations are in `App.xaml.cs` using `Host.CreateDefaultBuilder()`.

### ViewModels

| Lifetime | ViewModels |
|---|---|
| **Singleton** | `DashboardViewModel`, `PlatformsViewModel`, `TwitchWebSetupViewModel`, `TwitchDashboardViewModel`, `TwitchSettingsViewModel`, `TwitchChatSettingsViewModel`, `YoutubeChatSettingsViewModel`, `TiktokChatSettingsViewModel`, `DiscordChatSettingsViewModel`, `KickWebSetupViewModel`, `KickDashboardViewModel`, `KickSettingsViewModel`, `ChatViewModel`, `CurrencyViewModel`, `CommandsViewModel`, `VariablesViewModel`, `VariablePickerViewModel`, `SettingsViewModel`, `AudioSettingsViewModel`, `SoundsViewModel`, `StatisticsViewModel`, `EventHistoryViewModel`, `AlertSettingsViewModel`, `ConditionalRulesViewModel`, `GiveawayViewModel`, `PollViewModel`, `WatchtimeViewModel`, `EventTestingViewModel`, `MainViewModel`, `WebServerSettingsViewModel` |
| **Transient** | `ProfileSelectorViewModel`, `GoogleDemoSetupViewModel`, `NgrokSetupWizardViewModel` |

**Why Singleton for most ViewModels?** Prevents memory leaks from duplicate instances. Singleton ViewModels retain their state across navigation and are shared wherever injected. The comment in `App.xaml.cs` confirms: *"Use Singleton to prevent memory leaks from duplicate instances."*

**Why Transient for some?** ViewModels like `ProfileSelectorViewModel` are dialog-scoped — a fresh instance is needed each time the dialog opens.

### Views

| Lifetime | Types |
|---|---|
| **Transient** | All Views (`TwitchDashboardView`, `TwitchSettingsView`, `KickDashboardView`, `KickWebSetupView`, `KickSettingsView`, `ChatView`, `CurrencyView`, `SettingsView`, `AudioSettingsView`, `SoundsView`, `StatisticsView`, `EventHistoryView`, `AlertSettingsView`, `ConditionalRulesView`, `EventTestingView`, `NgrokSetupWizardView`) |

Views are Transient because they are WPF `UserControl` instances created by the navigation system. Some Views (e.g. `CommandsView`, `WebServerSettingsView`) are not registered at all — WPF `DataTemplate` creates them lazily from their associated ViewModel.

### Services

| Lifetime | Examples |
|---|---|
| **Singleton** | `IEventBus`, `IDataService`, `ITwitchService`, `IKickService`, `IProfileService`, `WebServerService`, etc. |
| **Transient** | `TwitchOAuthService` |

---

## 8. File Location Conventions

```
src/VyralBot.UI/
├── ViewModels/
│   ├── ViewModelBase.cs          # Base class for all ViewModels
│   ├── MainViewModel.cs          # Shell / navigation ViewModel
│   ├── ChatViewModel.cs          # Unified chat ViewModel
│   ├── DashboardViewModel.cs     # Dashboard page ViewModel
│   ├── KickDashboardViewModel.cs # Platform-specific ViewModels
│   ├── ...
├── Views/
│   ├── ChatView.xaml             # XAML layout
│   ├── ChatView.xaml.cs          # Minimal code-behind
│   ├── SettingsView.xaml
│   ├── SettingsView.xaml.cs
│   ├── ...
├── App.xaml.cs                   # DI container configuration
├── MainWindow.xaml               # Application shell window
```

**Naming rules:**
- ViewModel: `{Feature}ViewModel.cs` in `ViewModels/`
- View: `{Feature}View.xaml` + `{Feature}View.xaml.cs` in `Views/`
- ViewModel and View names correspond 1:1 (e.g. `ChatViewModel` ↔ `ChatView`)

---

## Quick Checklist for New ViewModels

1. Create `{Feature}ViewModel.cs` in `ViewModels/` inheriting `ViewModelBase`.
2. Mark the class `partial` (required for source generators).
3. Use `[ObservableProperty]` on private fields for bindable properties.
4. Use `[RelayCommand]` on methods for bindable commands.
5. Inject dependencies via constructor.
6. If using `IEventBus`, subscribe in `InitializeAsync()`, store `Guid` IDs, unsubscribe in `Cleanup()`.
7. Register in `App.xaml.cs` — typically `AddSingleton<{Feature}ViewModel>()`.
8. Create `{Feature}View.xaml` + code-behind in `Views/`, set `DataContext = viewModel` in constructor.
9. Register the View as `AddTransient<Views.{Feature}View>()` in `App.xaml.cs`.
