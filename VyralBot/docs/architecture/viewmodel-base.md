# ViewModelBase

**Component:** Base Class
**Location:** `src/VyralBot.UI/ViewModels/ViewModelBase.cs`

---

## Overview

`ViewModelBase` is the abstract base class for all ViewModels in VyralBot. It inherits from CommunityToolkit.Mvvm's `ObservableObject`, which provides `INotifyPropertyChanged` and `INotifyPropertyChanging` support. The class itself contains no additional members — it serves as a thin, consistent base type that all ViewModels derive from.

> **Note:** This class does **not** implement `IDisposable`. ViewModels that need resource cleanup must implement it themselves.

---

## Source Code

```csharp
// File: src/VyralBot.UI/ViewModels/ViewModelBase.cs
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

---

## Key Details

### `partial` Keyword

The class is declared `partial` because CommunityToolkit.Mvvm relies on **C# source generators**. When derived ViewModels use attributes like `[ObservableProperty]` or `[RelayCommand]`, the source generator emits a second partial class file containing the generated property or command code. If the class (or any class in its hierarchy) is not `partial`, the generator cannot produce valid output.

### Inheritance: `ObservableObject`

`ObservableObject` (from `CommunityToolkit.Mvvm.ComponentModel`) implements both `INotifyPropertyChanged` and `INotifyPropertyChanging`. Through this inheritance, every ViewModel automatically has access to:

| Member | Type | Description |
|--------|------|-------------|
| `PropertyChanged` | Event | Raised after a property value changes |
| `PropertyChanging` | Event | Raised before a property value changes |
| `OnPropertyChanged()` | Method | Manually raises `PropertyChanged` |
| `OnPropertyChanging()` | Method | Manually raises `PropertyChanging` |
| `SetProperty()` | Method | Sets a field value and raises both change events |

### Imported Namespaces

- **`CommunityToolkit.Mvvm.ComponentModel`** — Provides `ObservableObject` and the `[ObservableProperty]` attribute for source-generated properties.
- **`CommunityToolkit.Mvvm.Input`** — Provides `[RelayCommand]` for source-generated `ICommand` implementations.

Both namespaces are imported at the base class level so they are readily available throughout the ViewModel layer.

---

## Usage Patterns in Derived ViewModels

### Observable Properties

Derived ViewModels declare private fields annotated with `[ObservableProperty]`. The source generator produces a public property with change notification built in.

```csharp
public partial class ExampleViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private bool _isConnected;
}
```

The generator produces properties like:

```csharp
public string Username
{
    get => _username;
    set => SetProperty(ref _username, value);
}
```

### Relay Commands

Methods annotated with `[RelayCommand]` get a corresponding `IRelayCommand` property generated automatically.

```csharp
public partial class ExampleViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        StatusMessage = "Connecting...";
        // perform connection logic
    }
}
```

The generator produces a `ConnectCommand` property of type `IAsyncRelayCommand` that the UI can bind to.

### Combining Both

A typical derived ViewModel uses both attributes together:

```csharp
public partial class ChatViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _chatInput = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput))
            return;

        IsSending = true;
        // send logic
        ChatInput = string.Empty;
        IsSending = false;
    }
}
```

---

## What This Class Does NOT Provide

- **No `IDisposable` implementation.** Derived ViewModels that hold subscriptions, timers, or other disposable resources must implement `IDisposable` themselves.
- **No built-in navigation, messaging, or service-locator logic.** These concerns are handled elsewhere in the application architecture.
- **No additional fields or methods.** The class body is intentionally empty; all functionality comes from `ObservableObject` and the CommunityToolkit source generators.

---

## Related Documentation

- [MVVM Pattern](mvvm-pattern.md)
- [Dependency Injection](dependency-injection.md)
- [Event Bus System](event-bus.md)
