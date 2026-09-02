# Chat Variables & Multi-Platform Messaging Implementation Plan

> **STATUS: READY FOR IMPLEMENTATION**  
> **Last Updated:** December 12, 2025  
> **Architecture Validated:** ✅ Verified against actual codebase structure

---

## 🚀 QUICK START FOR IMPLEMENTING AGENT

### Implementation Order (MUST FOLLOW)
```
Phase 1 → Phase 2 → Phase 3 → Phase 4
```

### Minimum Viable Product
Complete **Phase 1 + Phase 2** for basic variable replacement in chat (~2-3 hours)

### First Steps
1. Read Phase 1 completely
2. Inject `IVariableReplacementService` into `ChatViewModel`
3. Modify `SendMessageAsync` to replace variables before sending
4. Test: Send `{discord}` in chat, see actual Discord link appear

### Reference Files to Study
- `src/VyralBot.UI/ViewModels/ChatViewModel.cs` - Main chat logic (lines 759-890: SendMessageAsync)
- `src/VyralBot.UI/Controls/VariablePicker.xaml` - Existing picker component
- `src/VyralBot.Core/Services/Variables/IVariableReplacementService.cs` - Variable service interface
- `src/VyralBot.Core/Services/Commands/CommandService.cs` - Existing variable parsing pattern

---

## Overview

Enable right-click variable insertion in all text inputs (added to existing cut/copy/paste context menu), variable replacement in manual chat messages, and send-to-all-platforms functionality with Vyral logo button for unified broadcasting of messages with variable support (e.g., `{giveaway-start}` to all connected chats).

## Current State Analysis

### Existing Components

#### Variable System (Already Implemented)
- ✅ **IVariableReplacementService** - Fully functional with caching and nested support
- ✅ **Variables Database Table** - Stores system and custom variables
- ✅ **VariablePicker Control** - Working picker in CommandsView
- ✅ **VariablesViewModel** - Management UI in Settings
- ❌ **Variable replacement in chat** - Currently only works in command responses
- ❌ **Context menu integration** - Not implemented globally
- ❌ **Compact mode** - Picker always shows edit/add buttons

#### Chat System
- ✅ **ChatViewModel** - Main chat logic in `src/VyralBot.UI/ViewModels/ChatViewModel.cs` (2273 lines)
- ✅ **SendMessageAsync** - Method at line 759, handles Twitch/Kick sending
- ✅ **Platform Selection** - `SelectedPlatform` property with Twitch/Kick options
- ✅ **ConnectedPlatforms** - Observable collection tracking active platforms
- ❌ **Send to all platforms** - Not implemented
- ❌ **Variable replacement** - Not called in manual messages

#### Platform Services
- ✅ **ITwitchService.SendMessageAsync** - Working
- ✅ **IKickService.SendMessageAsync** - Working with echo filtering
- ✅ **Platform detection** - IsConnected properties tracked
- ❌ **Multi-platform broadcast** - No logic to send to multiple platforms

### Current Issues

1. **No variable replacement in manual chat messages** - Variables like `{discord}` are sent as-is
2. **No context menu for variable insertion** - Users can't easily discover/insert variables
3. **No send-to-all functionality** - Must manually switch platforms to broadcast
4. **VariablePicker always shows edit buttons** - Takes up space when used for quick insertion

---

## Implementation Phases

> **IMPORTANT**: Each phase is self-contained and can be tested independently. Complete each phase fully before moving to the next.

---

## PHASE 1: Variable Replacement in Chat Messages (Core Feature)
**Estimated Time: 1-2 hours**  
**Dependencies: IVariableReplacementService already implemented**  
**Deliverables: Variables work in manual chat messages**

### Step 1.1: Inject IVariableReplacementService into ChatViewModel

**Modify: `src/VyralBot.UI/ViewModels/ChatViewModel.cs`**

Add to constructor dependencies (around line 40):

```csharp
private readonly IVariableReplacementService? _variableReplacementService;

public ChatViewModel(
    ILogger<ChatViewModel> logger,
    IEventBus eventBus,
    ITwitchService twitchService,
    IKickService kickService,
    KickSettingsViewModel kickSettings,
    IChatMessageStore chatStore,
    IChatEventSettingsService eventSettingsService,
    IAutoReplyService autoReplyService,
    ICurrencyService? currencyService = null,
    ICurrencyCommandService? currencyCommandService = null,
    ICommandService? commandService = null,
    ISoundService? soundService = null,
    IAlertService? alertService = null,
    GiveawayCommandService? giveawayCommandService = null,
    PollCommandService? pollCommandService = null,
    WatchtimeService? watchtimeService = null,
    WatchtimeCommandService? watchtimeCommandService = null,
    IVariableReplacementService? variableReplacementService = null) // ADD THIS
{
    _logger = logger;
    _eventBus = eventBus;
    _twitchService = twitchService;
    _kickService = kickService;
    _kickSettings = kickSettings;
    _chatStore = chatStore;
    _eventSettingsService = eventSettingsService;
    _autoReplyService = autoReplyService;
    _currencyService = currencyService;
    _currencyCommandService = currencyCommandService;
    _commandService = commandService;
    _soundService = soundService;
    _alertService = alertService;
    _giveawayCommandService = giveawayCommandService;
    _pollCommandService = pollCommandService;
    _watchtimeService = watchtimeService;
    _watchtimeCommandService = watchtimeCommandService;
    _variableReplacementService = variableReplacementService; // ADD THIS
}
```

**Note:** Service is already registered in `App.xaml.cs`, no DI changes needed.

### Step 1.2: Create BuildContextVariables Helper Method

**Add to `ChatViewModel.cs`** (after `SendMessageAsync`, around line 900):

```csharp
private Dictionary<string, string> BuildContextVariables(string platformName)
{
    var contextVars = new Dictionary<string, string>();
    
    // Platform-specific context
    switch (platformName)
    {
        case "Twitch":
            contextVars["bot-username"] = _twitchService.BotUsername ?? "Bot";
            contextVars["channel"] = _twitchService.Channel ?? "";
            break;
            
        case "Kick":
            contextVars["bot-username"] = _kickService.BotUsername ?? "Bot";
            contextVars["channel"] = _kickService.Channel ?? "";
            break;
            
        // Add other platforms as they're implemented
        default:
            contextVars["bot-username"] = "Bot";
            contextVars["channel"] = "";
            break;
    }
    
    // Global context variables
    contextVars["platform"] = platformName;
    
    return contextVars;
}
```

### Step 1.3: Modify SendMessageAsync to Replace Variables

**Modify: `ChatViewModel.cs` SendMessageAsync method (line 759)**

Find the section where `messageText` is set for each platform. Currently it's:

```csharp
var messageText = MessageInput;
```

Replace BOTH occurrences (Twitch and Kick cases) with:

```csharp
var messageText = MessageInput;

// Replace variables if service is available
if (_variableReplacementService != null && messageText.Contains('{'))
{
    try
    {
        var contextVars = BuildContextVariables(SelectedPlatform.Name);
        messageText = await _variableReplacementService.ReplaceVariablesAsync(
            messageText, 
            contextVars, 
            SelectedPlatform.Name);
        _logger.LogInformation("Variables replaced in message. Original: '{Original}', Replaced: '{Replaced}'", 
            MessageInput, messageText);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error replacing variables in message, using original text");
        // Continue with original messageText if replacement fails
    }
}
```

**Location in Twitch case:** Line ~787  
**Location in Kick case:** Line ~821

### Phase 1 Validation Checklist
- [ ] `IVariableReplacementService` injected into `ChatViewModel`
- [ ] `BuildContextVariables` method compiles and returns context dictionary
- [ ] Twitch case calls variable replacement before sending
- [ ] Kick case calls variable replacement before sending
- [ ] Sending `{discord}` in chat shows actual Discord link in sent message
- [ ] Sending `{platform}` shows "Twitch" or "Kick" based on selected platform
- [ ] Sending `{bot-username}` shows bot's actual username
- [ ] Original message preserved if variable replacement fails
- [ ] Detailed logging shows original and replaced messages

---

## PHASE 2: Compact Variable Picker Mode
**Estimated Time: 1-2 hours**  
**Dependencies: Phase 1 complete**  
**Deliverables: VariablePicker has compact mode without edit buttons**

### Step 2.1: Add IsCompactMode Dependency Property

**Modify: `src/VyralBot.UI/Controls/VariablePicker.xaml.cs`**

Add after existing dependency properties (after line 40):

```csharp
public static readonly DependencyProperty IsCompactModeProperty =
    DependencyProperty.Register(
        nameof(IsCompactMode),
        typeof(bool),
        typeof(VariablePicker),
        new PropertyMetadata(false));

public bool IsCompactMode
{
    get => (bool)GetValue(IsCompactModeProperty);
    set => SetValue(IsCompactModeProperty, value);
}
```

### Step 2.2: Hide Edit/Add Buttons in Compact Mode

**Modify: `src/VyralBot.UI/Controls/VariablePicker.xaml`**

Find the edit and add buttons in the variable list (around line 120-140). Wrap them with visibility binding:

```xaml
<!-- Edit/Add buttons section -->
<StackPanel Orientation="Horizontal" 
            HorizontalAlignment="Right"
            Visibility="{Binding IsCompactMode, 
                        RelativeSource={RelativeSource AncestorType=UserControl}, 
                        Converter={StaticResource InverseBoolToVisibilityConverter}}">
    <!-- Existing edit/add buttons here -->
</StackPanel>
```

**Note:** If `InverseBoolToVisibilityConverter` doesn't exist, add to `App.xaml`:

```xaml
<BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>
```

Then use `BoolToVisibilityConverter` with `ConverterParameter=Inverse` or create custom converter.

### Step 2.3: Update VariablePickerViewModel to Handle Compact Mode

**Modify: `src/VyralBot.UI/ViewModels/VariablePickerViewModel.cs`**

Add property (around line 20):

```csharp
[ObservableProperty]
private bool _isCompactMode;
```

Pass to ViewModel when initializing from control code-behind.

### Phase 2 Validation Checklist
- [ ] `IsCompactMode` property compiles without errors
- [ ] Setting `IsCompactMode="True"` hides edit/add buttons
- [ ] Setting `IsCompactMode="False"` shows all buttons (existing behavior)
- [ ] Variable selection/insertion still works in compact mode
- [ ] Search and category filters remain visible in compact mode

---

## PHASE 3: Global TextBox Context Menu with Variables
**Estimated Time: 2-3 hours**  
**Dependencies: Phase 2 complete**  
**Deliverables: Right-click any TextBox shows "Variables" menu item**

### Step 3.1: Create TextBoxVariableBehavior Attached Property

**Create: `src/VyralBot.UI/Behaviors/TextBoxVariableBehavior.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VyralBot.UI.Controls;

namespace VyralBot.UI.Behaviors;

public static class TextBoxVariableBehavior
{
    public static readonly DependencyProperty EnableVariablePickerProperty =
        DependencyProperty.RegisterAttached(
            "EnableVariablePicker",
            typeof(bool),
            typeof(TextBoxVariableBehavior),
            new PropertyMetadata(false, OnEnableVariablePickerChanged));

    public static bool GetEnableVariablePicker(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnableVariablePickerProperty);
    }

    public static void SetEnableVariablePicker(DependencyObject obj, bool value)
    {
        obj.SetValue(EnableVariablePickerProperty, value);
    }

    private static void OnEnableVariablePickerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox && e.NewValue is bool enable && enable)
        {
            textBox.Loaded += OnTextBoxLoaded;
        }
    }

    private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        
        textBox.Loaded -= OnTextBoxLoaded;
        
        // Don't add to PasswordBox types
        if (textBox is PasswordBox) return;
        
        // Create or extend existing context menu
        if (textBox.ContextMenu == null)
        {
            textBox.ContextMenu = new ContextMenu();
        }
        
        // Check if Variables menu item already exists
        foreach (var item in textBox.ContextMenu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Header?.ToString() == "Variables")
                return; // Already added
        }
        
        // Create Variables menu item
        var variablesMenuItem = new MenuItem
        {
            Header = "Variables",
            Icon = new PackIcon { Kind = PackIconKind.Variable }
        };
        
        // Create popup for variable picker
        var popup = new Popup
        {
            PlacementTarget = textBox,
            Placement = PlacementMode.Top,
            AllowsTransparency = true,
            StaysOpen = false
        };
        
        var variablePicker = new VariablePicker
        {
            TargetTextBox = textBox,
            IsCompactMode = true
        };
        
        // Try to get platform from DataContext
        if (textBox.DataContext != null)
        {
            var platformProperty = textBox.DataContext.GetType().GetProperty("SelectedPlatform");
            if (platformProperty != null)
            {
                var platform = platformProperty.GetValue(textBox.DataContext);
                if (platform != null)
                {
                    var nameProperty = platform.GetType().GetProperty("Name");
                    if (nameProperty != null)
                    {
                        variablePicker.Platform = nameProperty.GetValue(platform)?.ToString();
                    }
                }
            }
        }
        
        popup.Child = variablePicker;
        
        variablesMenuItem.Click += (s, args) =>
        {
            popup.IsOpen = true;
        };
        
        // Add separator before standard commands if context menu has items
        if (textBox.ContextMenu.Items.Count > 0)
        {
            textBox.ContextMenu.Items.Insert(0, new Separator());
        }
        
        textBox.ContextMenu.Items.Insert(0, variablesMenuItem);
    }
}
```

### Step 3.2: Register Behavior Globally in App.xaml

**Modify: `src/VyralBot.UI/App.xaml`**

Add namespace:

```xaml
xmlns:behaviors="clr-namespace:VyralBot.UI.Behaviors"
```

Add implicit style for TextBox (in Resources section):

```xaml
<Style TargetType="TextBox" BasedOn="{StaticResource MaterialDesignTextBox}">
    <Setter Property="behaviors:TextBoxVariableBehavior.EnableVariablePicker" Value="True"/>
</Style>
```

### Step 3.3: Add Required PackIcon Import

**Ensure in behavior file:**

```csharp
using MaterialDesignThemes.Wpf; // For PackIcon
```

### Phase 3 Validation Checklist
- [ ] Right-click any TextBox shows "Variables" menu item
- [ ] "Variables" has variable icon
- [ ] Clicking "Variables" opens compact variable picker popup
- [ ] Popup positions above TextBox
- [ ] Clicking variable inserts at cursor position
- [ ] Standard Cut/Copy/Paste/Select All still work
- [ ] PasswordBox types excluded from behavior
- [ ] Existing context menus preserved and extended

---

## PHASE 4: Send to All Platforms with Retry Logic
**Estimated Time: 3-4 hours**  
**Dependencies: Phase 1 complete**  
**Deliverables: Vyral button sends to all connected platforms with retry**

### Step 4.1: Add Vyral Logo Asset

**Create: `src/VyralBot.UI/Assets/vyral-logo.png`**

Save the provided Vyral logo image to this location.

**Update .csproj if needed:**

```xml
<ItemGroup>
    <Resource Include="Assets\vyral-logo.png" />
</ItemGroup>
```

### Step 4.2: Add SendToAllPlatforms Property to ChatViewModel

**Modify: `src/VyralBot.UI/ViewModels/ChatViewModel.cs`**

Add properties (around line 80):

```csharp
[ObservableProperty]
private bool _sendToAllPlatforms = false;

[ObservableProperty]
private string _statusMessage = string.Empty;

private System.Threading.Timer? _statusMessageTimer;
```

### Step 4.3: Create SendToAllConnectedPlatformsAsync Method

**Add to `ChatViewModel.cs`** (after `SendMessageAsync`):

```csharp
private async Task SendToAllConnectedPlatformsAsync(string message)
{
    _logger.LogInformation("🌐 Starting send to all connected platforms. Message: '{Message}'", message);
    
    var successPlatforms = new List<string>();
    var failedPlatforms = new List<(string Name, Exception? Error)>();
    
    // First attempt: Try all connected platforms
    var tasks = new List<Task>();
    
    foreach (var platform in ConnectedPlatforms)
    {
        tasks.Add(SendToSinglePlatformAsync(platform.Name, message, successPlatforms, failedPlatforms));
    }
    
    await Task.WhenAll(tasks);
    
    // Retry logic: Wait 500ms and retry failed platforms
    if (failedPlatforms.Count > 0)
    {
        _logger.LogWarning("⚠️ {Count} platform(s) failed, waiting 500ms before retry...", failedPlatforms.Count);
        UpdateStatusMessage($"Sending to {successPlatforms.Count}/{ConnectedPlatforms.Count} platforms. Retrying {failedPlatforms.Count}...");
        
        await Task.Delay(500);
        
        var retryList = failedPlatforms.ToList();
        failedPlatforms.Clear();
        
        foreach (var (platformName, _) in retryList)
        {
            await SendToSinglePlatformAsync(platformName, message, successPlatforms, failedPlatforms);
        }
    }
    
    // Final status
    var totalPlatforms = ConnectedPlatforms.Count;
    var finalSuccessCount = successPlatforms.Count;
    
    if (failedPlatforms.Count == 0)
    {
        _logger.LogInformation("✅ Message sent to all {Count} platforms successfully", finalSuccessCount);
        UpdateStatusMessage($"✅ Sent to all {finalSuccessCount} platform(s)", 5000);
    }
    else
    {
        var failedNames = string.Join(", ", failedPlatforms.Select(f => f.Name));
        _logger.LogWarning("⚠️ Message sent to {Success}/{Total} platforms. Failed: {Failed}", 
            finalSuccessCount, totalPlatforms, failedNames);
        UpdateStatusMessage($"⚠️ Sent to {finalSuccessCount}/{totalPlatforms} platforms. {failedNames} failed.", 8000);
        
        // Log detailed errors
        foreach (var (name, error) in failedPlatforms)
        {
            _logger.LogWarning(error, "Failed to send to {Platform}", name);
        }
    }
}

private async Task SendToSinglePlatformAsync(
    string platformName, 
    string message, 
    List<string> successList, 
    List<(string, Exception?)> failureList)
{
    try
    {
        _logger.LogInformation("📤 Attempting send to {Platform}...", platformName);
        
        // Replace variables per-platform
        var processedMessage = message;
        if (_variableReplacementService != null && message.Contains('{'))
        {
            var contextVars = BuildContextVariables(platformName);
            processedMessage = await _variableReplacementService.ReplaceVariablesAsync(
                message, contextVars, platformName);
        }
        
        switch (platformName)
        {
            case "Twitch":
                if (_twitchService.IsConnected)
                {
                    await _twitchService.SendMessageAsync(processedMessage);
                    
                    // Add sent message to UI
                    var twitchMessage = new ChatMessage
                    {
                        Platform = "Twitch",
                        Username = _twitchService.BotUsername ?? "Bot",
                        Message = processedMessage,
                        Timestamp = DateTime.Now,
                        Color = "#9146FF",
                        IsBroadcaster = true,
                        Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
                    };
                    twitchMessage.InitializeBadges();
                    await Application.Current.Dispatcher.InvokeAsync(() => Messages.Add(twitchMessage));
                    
                    successList.Add(platformName);
                    _logger.LogInformation("✅ Successfully sent to {Platform}", platformName);
                }
                else
                {
                    throw new InvalidOperationException($"{platformName} is not connected");
                }
                break;
                
            case "Kick":
                if (_kickService.IsConnected)
                {
                    // Track sent message for echo filtering
                    var messageKey = $"{_kickService.BotUsername?.ToLowerInvariant()}:{processedMessage.Trim()}";
                    lock (_recentMessagesLock)
                    {
                        _recentKickMessages[messageKey] = DateTime.Now;
                    }
                    
                    await _kickService.SendMessageAsync(processedMessage);
                    
                    // Add sent message to UI
                    var kickMessage = new ChatMessage
                    {
                        Platform = "Kick",
                        Username = _kickService.BotUsername ?? "Bot",
                        Message = processedMessage,
                        Timestamp = DateTime.Now,
                        Color = "#53FC18",
                        IsBroadcaster = true,
                        Badges = new Dictionary<string, string> { { "Broadcaster", "Broadcaster" } }
                    };
                    kickMessage.InitializeBadges();
                    await Application.Current.Dispatcher.InvokeAsync(() => Messages.Add(kickMessage));
                    
                    successList.Add(platformName);
                    _logger.LogInformation("✅ Successfully sent to {Platform}", platformName);
                }
                else
                {
                    throw new InvalidOperationException($"{platformName} is not connected");
                }
                break;
                
            // Add YouTube, TikTok, Discord cases as they're implemented
            
            default:
                _logger.LogWarning("Platform {Platform} not implemented for multi-send", platformName);
                break;
        }
    }
    catch (Exception ex)
    {
        failureList.Add((platformName, ex));
        _logger.LogWarning(ex, "❌ Failed to send to {Platform}", platformName);
    }
}

private void UpdateStatusMessage(string message, int autoClearMs = 5000)
{
    StatusMessage = message;
    
    // Cancel existing timer
    _statusMessageTimer?.Dispose();
    
    // Auto-clear after delay
    _statusMessageTimer = new System.Threading.Timer(_ =>
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = string.Empty;
        });
    }, null, autoClearMs, System.Threading.Timeout.Infinite);
}
```

### Step 4.4: Update SendMessageAsync to Support Send-to-All

**Modify: `ChatViewModel.cs` SendMessageAsync method (line 759)**

Replace the entire method with:

```csharp
[RelayCommand]
private async Task SendMessageAsync()
{
    _logger.LogInformation("SendMessageAsync called. MessageInput: '{Input}', SendToAll: {SendToAll}", 
        MessageInput, SendToAllPlatforms);
    
    if (string.IsNullOrWhiteSpace(MessageInput))
    {
        _logger.LogWarning("MessageInput is empty or whitespace");
        return;
    }

    if (!SendToAllPlatforms && SelectedPlatform == null)
    {
        _logger.LogWarning("No platform selected for sending message");
        System.Windows.MessageBox.Show("No platform selected. Please connect to a platform first.", 
            "No Platform", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
    }

    try
    {
        if (SendToAllPlatforms)
        {
            // Send to all connected platforms
            if (ConnectedPlatforms.Count == 0)
            {
                System.Windows.MessageBox.Show("No platforms connected.", "Not Connected", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            
            var messageText = MessageInput;
            await SendToAllConnectedPlatformsAsync(messageText);
        }
        else
        {
            // Original single-platform send logic
            _logger.LogInformation("Attempting to send message to {Platform}: {Message}", 
                SelectedPlatform!.Name, MessageInput);

            switch (SelectedPlatform.Name)
            {
                case "Twitch":
                    _logger.LogInformation("Twitch IsConnected: {IsConnected}, Channel: {Channel}", 
                        _twitchService.IsConnected, _twitchService.Channel);
                    if (_twitchService.IsConnected)
                    {
                        var messageText = MessageInput;
                        
                        // Replace variables
                        if (_variableReplacementService != null && messageText.Contains('{'))
                        {
                            try
                            {
                                var contextVars = BuildContextVariables(SelectedPlatform.Name);
                                messageText = await _variableReplacementService.ReplaceVariablesAsync(
                                    messageText, contextVars, SelectedPlatform.Name);
                                _logger.LogInformation("Variables replaced. Original: '{Original}', Replaced: '{Replaced}'", 
                                    MessageInput, messageText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error replacing variables, using original text");
                            }
                        }
                        
                        _logger.LogInformation("Sending message to Twitch...");
                        await _twitchService.SendMessageAsync(messageText);
                        _logger.LogInformation("Message sent to Twitch successfully");
                        
                        // Add sent message to chat
                        var twitchMessage = new ChatMessage
                        {
                            Platform = "Twitch",
                            Username = _twitchService.BotUsername ?? "Bot",
                            Message = messageText,
                            Timestamp = DateTime.Now,
                            Color = "#9146FF",
                            IsModerator = false,
                            IsSubscriber = false,
                            IsVip = false,
                            IsBroadcaster = true,
                            Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
                        };
                        twitchMessage.InitializeBadges();
                        Messages.Add(twitchMessage);
                        
                        _logger.LogInformation("Added sent message to chat UI");
                    }
                    else
                    {
                        _logger.LogWarning("Twitch is not connected");
                        System.Windows.MessageBox.Show("Twitch connection lost. Please reconnect.", 
                            "Not Connected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                    break;

                case "Kick":
                    _logger.LogInformation("Kick IsConnected: {IsConnected}, Channel: {Channel}", 
                        _kickService.IsConnected, _kickService.Channel);
                    if (_kickService.IsConnected)
                    {
                        var messageText = MessageInput;
                        
                        // Replace variables
                        if (_variableReplacementService != null && messageText.Contains('{'))
                        {
                            try
                            {
                                var contextVars = BuildContextVariables(SelectedPlatform.Name);
                                messageText = await _variableReplacementService.ReplaceVariablesAsync(
                                    messageText, contextVars, SelectedPlatform.Name);
                                _logger.LogInformation("Variables replaced. Original: '{Original}', Replaced: '{Replaced}'", 
                                    MessageInput, messageText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error replacing variables, using original text");
                            }
                        }
                        
                        // Track sent message for echo filtering
                        var messageKey = $"{_kickService.BotUsername?.ToLowerInvariant()}:{messageText.Trim()}";
                        lock (_recentMessagesLock)
                        {
                            _recentKickMessages[messageKey] = DateTime.Now;
                            _logger.LogInformation("🔒 Tracking sent Kick message: {MessageKey}", messageKey);
                        }
                        
                        _logger.LogInformation("Sending message to Kick...");
                        await _kickService.SendMessageAsync(messageText);
                        _logger.LogInformation("Message sent to Kick successfully");
                        
                        // Add sent message to chat
                        var sentMessage = new ChatMessage
                        {
                            Platform = "Kick",
                            Username = _kickService.BotUsername ?? "Bot",
                            Message = messageText,
                            Timestamp = DateTime.Now,
                            Color = "#53FC18",
                            IsModerator = false,
                            IsSubscriber = false,
                            IsVip = false,
                            IsBroadcaster = true,
                            Badges = new Dictionary<string, string> { { "Broadcaster", "Broadcaster" } }
                        };
                        sentMessage.InitializeBadges();
                        Messages.Add(sentMessage);
                        
                        _logger.LogInformation("Added sent message to chat UI");
                    }
                    else
                    {
                        _logger.LogWarning("Kick is not connected");
                        System.Windows.MessageBox.Show("Kick connection lost. Please reconnect.", 
                            "Not Connected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                    break;

                default:
                    _logger.LogWarning("Unknown platform: {Platform}", SelectedPlatform.Name);
                    break;
            }
        }

        MessageInput = "";
        _logger.LogInformation("Message input cleared");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error sending message");
        System.Windows.MessageBox.Show($"Error sending message: {ex.Message}", "Error", 
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
}
```

### Step 4.5: Add Vyral Button to ChatView

**Modify: `src/VyralBot.UI/Views/ChatView.xaml`**

Find the platform selector popup (around line 60-90). Add Vyral button alongside Twitch/Kick:

```xaml
<!-- Inside platform selector Popup's ItemsControl or StackPanel -->

<!-- Vyral - Send to All Button -->
<Button Command="{Binding SelectSendToAllCommand}"
        Style="{StaticResource MaterialDesignFlatButton}"
        Height="48"
        Margin="0,0,0,4"
        HorizontalContentAlignment="Left"
        Padding="12,0">
    <StackPanel Orientation="Horizontal">
        <Image Source="/Assets/vyral-logo.png"
               Width="24"
               Height="24"
               Margin="0,0,12,0"/>
        <TextBlock Text="Send to All Platforms"
                   VerticalAlignment="Center"
                   FontSize="13"
                   Foreground="{DynamicResource TextPrimaryBrush}"/>
        <Border Background="#4DB8A8"
                CornerRadius="8"
                Padding="6,2"
                Margin="8,0,0,0"
                Visibility="{Binding SendToAllPlatforms, Converter={StaticResource BoolToVisibilityConverter}}">
            <TextBlock Text="ACTIVE"
                       FontSize="10"
                       FontWeight="Bold"
                       Foreground="White"/>
        </Border>
    </StackPanel>
</Button>
```

### Step 4.6: Add SelectSendToAllCommand

**Add to `ChatViewModel.cs`**:

```csharp
[RelayCommand]
private void SelectSendToAll()
{
    SendToAllPlatforms = true;
    SelectedPlatform = null; // Clear single platform selection
    IsPlatformSelectorOpen = false;
    _logger.LogInformation("✅ Send to All Platforms enabled");
}
```

Also update `SelectPlatform` command to disable send-to-all:

```csharp
[RelayCommand]
private void SelectPlatform(PlatformOption platform)
{
    SelectedPlatform = platform;
    SendToAllPlatforms = false; // Disable send-to-all when selecting specific platform
    IsPlatformSelectorOpen = false;
    _logger.LogInformation("Selected platform: {Platform}", platform.Name);
}
```

### Step 4.7: Add Status Message Snackbar to ChatView

**Modify: `src/VyralBot.UI/Views/ChatView.xaml`**

Add at bottom of Grid (after message input section):

```xaml
<!-- Status Message Snackbar -->
<materialDesign:Snackbar Grid.Row="2"
                         MessageQueue="{materialDesign:MessageQueue}"
                         x:Name="StatusSnackbar"
                         Margin="16,0,16,16"
                         VerticalAlignment="Bottom">
    <materialDesign:SnackbarMessage Content="{Binding StatusMessage}"
                                    ActionContent="DISMISS"/>
</materialDesign:Snackbar>
```

**Alternative:** Use simple TextBlock if Snackbar is complex:

```xaml
<TextBlock Grid.Row="2"
           Text="{Binding StatusMessage}"
           Foreground="{DynamicResource TextPrimaryBrush}"
           FontSize="12"
           Margin="16,8"
           VerticalAlignment="Bottom"
           HorizontalAlignment="Center"
           Visibility="{Binding StatusMessage, Converter={StaticResource StringNullOrEmptyToVisibilityConverter}}"/>
```

### Phase 4 Validation Checklist
- [ ] Vyral logo saved to `Assets/vyral-logo.png`
- [ ] Vyral button appears in platform selector
- [ ] Clicking Vyral button enables `SendToAllPlatforms`
- [ ] Send button sends to all connected platforms when enabled
- [ ] Each platform receives message with platform-specific variables replaced
- [ ] Failed platforms retry after 500ms delay
- [ ] Status message shows success/failure count
- [ ] Detailed logging for each platform send attempt
- [ ] Switching to specific platform disables send-to-all
- [ ] Messages appear in chat UI for all successful sends
- [ ] Echo filtering still works for Kick messages

---

## File Manifest

### Files to Modify

**Phase 1:**
- `src/VyralBot.UI/ViewModels/ChatViewModel.cs` - Add variable replacement logic

**Phase 2:**
- `src/VyralBot.UI/Controls/VariablePicker.xaml.cs` - Add IsCompactMode property
- `src/VyralBot.UI/Controls/VariablePicker.xaml` - Hide buttons in compact mode
- `src/VyralBot.UI/ViewModels/VariablePickerViewModel.cs` - Add compact mode handling

**Phase 3:**
- `src/VyralBot.UI/App.xaml` - Add global TextBox style with behavior

**Phase 4:**
- `src/VyralBot.UI/ViewModels/ChatViewModel.cs` - Add send-to-all logic
- `src/VyralBot.UI/Views/ChatView.xaml` - Add Vyral button and status display

### Files to Create

**Phase 3:**
- `src/VyralBot.UI/Behaviors/TextBoxVariableBehavior.cs` - Attached behavior for context menu

**Phase 4:**
- `src/VyralBot.UI/Assets/vyral-logo.png` - Vyral logo image

---

## Testing Checklist

### Phase 1: Variable Replacement
- [ ] Send `{discord}` → Shows actual Discord link
- [ ] Send `{platform}` → Shows "Twitch" or "Kick"
- [ ] Send `{bot-username}` → Shows bot's username
- [ ] Send `{full-socials}` → Nested variables work
- [ ] Send plain text → No changes, works normally
- [ ] Variables work on Twitch
- [ ] Variables work on Kick
- [ ] Error handling: Invalid variable → Original text sent

### Phase 2: Compact Mode
- [ ] `IsCompactMode="True"` → Edit/add buttons hidden
- [ ] `IsCompactMode="False"` → All buttons visible
- [ ] Variable selection works in compact mode
- [ ] Search works in compact mode
- [ ] Category filter works in compact mode

### Phase 3: Context Menu
- [ ] Right-click TextBox → "Variables" menu appears
- [ ] "Variables" has icon
- [ ] Click "Variables" → Popup opens
- [ ] Popup shows compact picker (no edit buttons)
- [ ] Insert variable → Text appears at cursor
- [ ] Standard Cut/Copy/Paste still work
- [ ] Works on all TextBoxes (chat, commands, settings)
- [ ] PasswordBox excluded

### Phase 4: Send to All
- [ ] Vyral button visible in platform selector
- [ ] Click Vyral → `SendToAllPlatforms` enabled
- [ ] Send message → All platforms receive it
- [ ] Platform-specific variables replaced per platform
- [ ] Twitch gets `{twitch-followercount}` value
- [ ] Kick gets `{kick-followercount}` value
- [ ] One platform fails → Others succeed
- [ ] Failed platform retries after 500ms
- [ ] Retry succeeds → Status shows all successful
- [ ] Retry fails → Status shows failure
- [ ] Status message auto-dismisses
- [ ] Click specific platform → Send-to-all disabled
- [ ] Logging shows all attempts

---

## Design Decisions

### Variable Replacement Priority
1. Context variables (username, platform, channel)
2. Database variables (discord, twitter, youtube)
3. Platform-specific variables (twitch-followercount, kick-followercount)

### Send-to-All Retry Logic
- **First Attempt:** Try all platforms simultaneously with `Task.WhenAll`
- **Delay:** 500ms wait before retry
- **Retry:** One retry attempt per failed platform
- **Logging:** Detailed logs for debugging each attempt
- **User Feedback:** Single status message with counts

### Context Menu vs Popup
- **Approach:** Attached behavior adds "Variables" to existing context menu
- **Rationale:** Preserves standard Cut/Copy/Paste, familiar UX
- **Exclusions:** PasswordBox types excluded for security

### Compact Mode Design
- **Hidden:** Edit and Add buttons
- **Visible:** Search, category filter, variable list, close button
- **Purpose:** Quick insertion without management clutter

---

## Summary

| Phase | Deliverable | Time | Dependencies |
|-------|-------------|------|--------------|
| 1 | Variable replacement in chat | 1-2h | None |
| 2 | Compact picker mode | 1-2h | Phase 1 |
| 3 | Global context menu | 2-3h | Phase 2 |
| 4 | Send to all platforms | 3-4h | Phase 1 |

**Minimum Viable Implementation: Phases 1-2 (2-4 hours)**  
**Full Implementation: All Phases (7-11 hours)**

---

**Plan Created:** December 12, 2025  
**Status:** ✅ READY FOR IMPLEMENTATION