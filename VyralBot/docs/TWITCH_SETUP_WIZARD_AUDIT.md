# Twitch Setup Wizard - Comprehensive Audit

> **Historical Reference:** This audit was used to design `BaseWebSetupViewModel` (now at `src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs`). The analysis below documents TwitchWebSetupViewModel's architecture and was the basis for the reusable setup wizard framework. Keep as architectural reference.

## Document Purpose
This document provides a complete analysis of the Twitch OAuth setup wizard to understand its architecture, flow, and components. This analysis served as the foundation for creating the reusable `BaseWebSetupViewModel` framework.

---

## Executive Summary

The Twitch setup wizard is a **4-step OAuth flow** that:
1. Signs user into Twitch
2. Creates an application in Twitch Developer Console
3. Authorizes the app with OAuth
4. Tests the connection

**Key Success Factor**: WebView2 is initialized ON-DEMAND and disposed after use, with visibility managed through property bindings.

---

## Architecture Overview

### Component Structure

```
TwitchWebSetupViewModel (Standalone)
├── Properties (Observable)
│   ├── CurrentStep (1-4)
│   ├── ShowBrowser (WebView2 visibility)
│   ├── ShowModal (Credential input overlay)
│   ├── ShowSuccessScreen (Final screen)
│   ├── BothCredentialsSaved (Step 2 completion)
│   ├── IsOAuthAuthorized (Step 3 completion)
│   └── TestMessageStatus (Step 4 feedback)
├── Events
│   ├── WebView2InitializeRequested (Request WebView2 setup)
│   ├── WebView2DisposeRequested (Cleanup WebView2)
│   └── SetupCompleted (Wizard finished)
└── Commands
    ├── OpenStep1BrowserCommand → Navigate to twitch.tv/login
    ├── ContinueFromStep1 → Hide browser, go to Step 2
    ├── OpenStep2BrowserCommand → Navigate to dev.twitch.tv/console
    ├── SaveClientId/Secret → Show modal for input
    ├── ContinueFromStep2 → Save credentials, go to Step 3
    ├── AuthorizeOAuthCommand → Start OAuth flow
    ├── SendTestMessageCommand → Connect to IRC and test
    └── CompleteSetupCommand → Save to database, finish
```

---

## Step-by-Step Flow Analysis

### STEP 1: Sign In to Twitch

#### ViewModel State
```csharp
CurrentStep = 1
ShowBrowser = false (initially)
ShowModal = false
ShowSuccessScreen = false
```

#### User Actions
1. Clicks "SIGN IN WITH TWITCH" button

#### Command Execution: `OpenStep1BrowserAsync`
```csharp
[RelayCommand]
private async Task OpenStep1BrowserAsync()
{
    _logger.LogInformation("Opening Twitch login page in WebView2");

    // 1. REQUEST WebView2 initialization
    WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

    // 2. WAIT for initialization (500ms)
    await Task.Delay(500);

    // 3. NAVIGATE to Twitch login
    if (WebView2?.CoreWebView2 != null)
    {
        ShowBrowser = true;  // Make WebView2 visible
        WebView2.CoreWebView2.Navigate("https://www.twitch.tv/login");
        _logger.LogInformation("WebView2 navigated to Twitch login");
    }
}
```

#### Code-Behind Response (`TwitchWebSetupView.xaml.cs`)
```csharp
private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
{
    if (WebView2Browser == null) return;

    if (WebView2Browser.CoreWebView2 == null)
    {
        // Initialize WebView2 (first time only)
        await WebView2Browser.EnsureCoreWebView2Async();
    }

    _webViewInitialized = true;

    // Set reference in ViewModel
    if (DataContext is TwitchWebSetupViewModel viewModel)
    {
        viewModel.WebView2 = WebView2Browser;
    }
}
```

#### XAML Visibility Management
```xml
<!-- WebView2 Browser -->
<Border Background="{StaticResource BackgroundDarkBrush}">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding ShowBrowser}" Value="True"/>
                        <Condition Binding="{Binding ShowModal}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <wv2:WebView2 x:Name="WebView2Browser"/>
</Border>

<!-- Step 1 Instructions Panel -->
<StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="1"/>
                        <Condition Binding="{Binding ShowBrowser}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <!-- Instructions content -->
</StackPanel>
```

#### Log Output
```
[18:37:03 INF] Opening Twitch login page in WebView2
[18:37:04 INF] WebView2 navigated to Twitch login
```

#### Completion Action
User clicks "CONTINUE" button → `ContinueFromStep1Command`

```csharp
[RelayCommand]
private void ContinueFromStep1()
{
    _logger.LogInformation("Continuing from Step 1");

    // 1. HIDE the browser
    ShowBrowser = false;

    // 2. Navigate to blank page (don't dispose WebView2)
    if (WebView2?.CoreWebView2 != null)
    {
        WebView2.CoreWebView2.Navigate("about:blank");
    }

    // 3. Move to next step
    GoToStep2();
}
```

---

### STEP 2: Create Application

#### ViewModel State
```csharp
CurrentStep = 2
ShowBrowser = false (initially)
ClientIdSaved = false
ClientSecretSaved = false
BothCredentialsSaved = false
```

#### User Actions
1. Clicks "OPEN DEVELOPER CONSOLE" button
2. Creates app in browser (name, redirect URI, category)
3. Clicks "SAVE CLIENT ID" → Enters value in modal
4. Clicks "SAVE CLIENT SECRET" → Enters value in modal
5. Clicks "CONTINUE" (appears when both saved)

#### Command: `OpenStep2BrowserAsync`
```csharp
[RelayCommand]
private async Task OpenStep2BrowserAsync()
{
    _logger.LogInformation("Opening Twitch Developer Console in WebView2");

    // WebView2 is already initialized from Step 1, just reuse it
    WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
    await Task.Delay(500);

    if (WebView2?.CoreWebView2 != null)
    {
        ShowBrowser = true;
        WebView2.CoreWebView2.Navigate("https://dev.twitch.tv/console/apps");
        _logger.LogInformation("WebView2 navigated to Developer Console");
    }
}
```

#### Modal System for Credential Input

**Command: `SaveClientId`**
```csharp
[RelayCommand]
private void SaveClientId()
{
    _logger.LogInformation("SaveClientId button clicked");

    // Configure modal
    ModalTitle = "Save Client ID";
    ModalDescription = "Copy your Client ID from the Twitch Developer Console and paste it below.";
    ModalInput = "";

    // Set callback action
    _modalSaveAction = (value) =>
    {
        _savedClientId = value;
        MaskedClientId = value.Substring(0, Math.Min(10, value.Length)) + "...";
        ClientIdSaved = true;
        OnPropertyChanged(nameof(ClientIdSaved));

        // Check if both credentials saved
        if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
        {
            BothCredentialsSaved = true;
        }
    };

    // Show modal overlay
    ShowModal = true;
}
```

**Modal XAML**
```xml
<Border Grid.RowSpan="2"
        Background="{DynamicResource OverlayBackgroundBrush}"
        Panel.ZIndex="1000">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ShowModal}" Value="True">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <materialDesign:Card Width="500" Padding="32">
        <StackPanel>
            <TextBlock Text="{Binding ModalTitle}"/>
            <TextBlock Text="{Binding ModalDescription}"/>
            <TextBox Text="{Binding ModalInput}"/>
            <Button Content="SAVE" Command="{Binding SaveModalCommand}"/>
            <Button Content="CANCEL" Command="{Binding CloseModalCommand}"/>
        </StackPanel>
    </materialDesign:Card>
</Border>
```

**Save Modal Action**
```csharp
[RelayCommand]
private void SaveModal()
{
    if (!string.IsNullOrWhiteSpace(ModalInput) && _modalSaveAction != null)
    {
        _modalSaveAction(ModalInput);  // Execute callback
        ShowModal = false;
        ModalInput = "";
        _modalSaveAction = null;
    }
}
```

#### Completion: `ContinueFromStep2Async`
```csharp
[RelayCommand]
private async Task ContinueFromStep2Async()
{
    _logger.LogInformation("Continuing from Step 2 to Step 3");

    // 1. HIDE browser
    ShowBrowser = false;

    // 2. Navigate to blank (reuse WebView2)
    if (WebView2?.CoreWebView2 != null)
    {
        WebView2.CoreWebView2.Navigate("about:blank");
    }

    // 3. SAVE credentials to file FIRST
    if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
    {
        await _oAuthService.SaveCredentialsAsync(_savedClientId, _savedClientSecret);
        _logger.LogInformation("Credentials saved to file, moving to Step 3");
    }

    // 4. Move to Step 3
    CurrentStep = 3;
    ShowSuccessScreen = false;

    // 5. Load credentials for OAuth
    await _oAuthService.LoadCredentialsAsync();
    OAuthStatus = "Ready to authorize. Click the button below.";
}
```

#### Log Output
```
[18:37:06 INF] Opening Twitch Developer Console in WebView2
[18:37:07 INF] WebView2 navigated to Developer Console
[18:37:16 INF] SaveClientId button clicked
[18:37:17 INF] SaveModal called with input: yv1u6wb6sj
[18:37:17 INF] Client ID saved: yv1u6wb6sj...
[18:37:22 INF] SaveClientSecret button clicked
[18:37:23 INF] SaveModal called with input: hd5kuapeom
[18:37:23 INF] Client Secret saved
[18:37:24 INF] Continuing from Step 2 to Step 3
[18:37:24 INF] Credentials saved to file
```

---

### STEP 3: Authorize OAuth

#### ViewModel State
```csharp
CurrentStep = 3
ShowBrowser = false (initially)
IsOAuthAuthorized = false
OAuthStatus = "Ready to authorize. Click the button below."
```

#### User Actions
1. Clicks "AUTHORIZE WITH TWITCH" button
2. Signs into Twitch (in WebView2)
3. Accepts permissions
4. Redirected to localhost:3000 (callback intercepted)
5. Automatically advances to Step 4

#### Command: `AuthorizeOAuthAsync`
```csharp
[RelayCommand]
private async Task AuthorizeOAuthAsync()
{
    _logger.LogInformation("Starting OAuth authorization with WebView2");
    OAuthStatus = "Preparing authorization...";

    try
    {
        // 1. LOAD credentials from file
        await _oAuthService.LoadCredentialsAsync();

        // 2. BUILD OAuth URL
        var url = _oAuthService.BuildAuthorizationUrl();

        if (string.IsNullOrEmpty(url) || !url.Contains("client_id="))
        {
            OAuthStatus = "❌ Client ID not found. Please go back to Step 2.";
            return;
        }

        // 3. REQUEST WebView2 initialization
        WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
        await Task.Delay(500);

        if (WebView2?.CoreWebView2 == null)
        {
            OAuthStatus = "❌ WebView2 not initialized";
            return;
        }

        // 4. SUBSCRIBE to navigation events (catch callback)
        WebView2.CoreWebView2.NavigationStarting += OnOAuthNavigationStarting;

        // 5. SHOW browser and navigate
        ShowBrowser = true;
        OAuthStatus = "✅ Authorizing... Please sign in to Twitch";
        WebView2.CoreWebView2.Navigate(url);

        _logger.LogInformation("WebView2 navigated to OAuth URL");
    }
    catch (Exception ex)
    {
        OAuthStatus = $"❌ Failed to start authorization: {ex.Message}";
    }
}
```

#### OAuth Callback Detection
```csharp
private async void OnOAuthNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
{
    // Check if this is the callback URL
    if (e.Uri.StartsWith("http://localhost:3000"))
    {
        _logger.LogInformation("OAuth callback detected: {Uri}", e.Uri);

        // 1. CANCEL navigation (localhost won't load)
        e.Cancel = true;

        // 2. UNSUBSCRIBE from event
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
        }

        // 3. HIDE browser
        ShowBrowser = false;

        // 4. HANDLE callback on UI thread
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await HandleOAuthCallbackAsync(e.Uri);
        });
    }
}
```

#### OAuth Callback Processing
```csharp
private async Task HandleOAuthCallbackAsync(string url)
{
    _logger.LogInformation("OAuth callback received: {Url}", url);

    try
    {
        // 1. EXCHANGE code for access token
        var result = await _oAuthService.HandleOAuthCallbackAsync(url);

        if (result == null)
        {
            OAuthStatus = "❌ Authorization cancelled or failed";
            ShowBrowser = false;
            return;
        }

        // 2. STORE tokens
        _accessToken = result.Value.AccessToken;
        _refreshToken = result.Value.RefreshToken;
        _authorizedUsername = result.Value.Username;
        ChannelName = _authorizedUsername; // Auto-fill for Step 4

        // 3. UPDATE status
        IsOAuthAuthorized = true;
        ShowBrowser = false;
        OAuthStatus = $"✓ Authorized as {_authorizedUsername}";

        _logger.LogInformation("OAuth authorization successful for user: {Username}", _authorizedUsername);

        // 4. ADVANCE to Step 4 automatically
        CurrentStep = 4;
    }
    catch (Exception ex)
    {
        OAuthStatus = $"❌ Authorization failed: {ex.Message}";
        ShowBrowser = false;
    }
}
```

#### Log Output
```
[18:37:26 INF] Starting OAuth authorization with WebView2
[18:37:26 INF] FULL OAuth URL: https://id.twitch.tv/oauth2/authorize?client_id=yv1u6wb6...
[18:37:26 INF] WebView2 navigated to OAuth URL
[18:37:29 INF] OAuth callback detected: http://localhost:3000/?code=olp3aaryw4...
[18:37:29 INF] OAuth callback received
[18:37:29 INF] Exchanging authorization code for access token
[18:37:30 INF] Successfully obtained access token (expires in 14252s)
[18:37:30 INF] Getting username from Twitch API
[18:37:30 INF] Got username from Twitch: izlrp
[18:37:30 INF] OAuth authorization successful for user: izlrp
```

---

### STEP 4: Test Connection

#### ViewModel State
```csharp
CurrentStep = 4
ChannelName = "izlrp" (auto-filled from OAuth)
TestMessageStatus = "Click 'Send Test Message' to verify the connection"
```

#### User Actions
1. Clicks "SEND TEST MESSAGE" button
2. Waits for IRC connection
3. Sees success message
4. Clicks "✓ IT WORKED!" button

#### Command: `SendTestMessageAsync`
```csharp
[RelayCommand]
private async Task SendTestMessageAsync()
{
    _logger.LogInformation("Sending test message to channel: {Channel}", ChannelName);

    if (string.IsNullOrEmpty(_accessToken))
    {
        TestMessageStatus = "❌ Please complete Step 3 first";
        return;
    }

    TestMessageStatus = "⏳ Connecting to Twitch IRC...";

    try
    {
        // 1. CONNECT to Twitch IRC
        using var client = new TcpClient();
        await client.ConnectAsync("irc.chat.twitch.tv", 6667);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        using var writer = new StreamWriter(stream) { AutoFlush = true };

        // 2. AUTHENTICATE
        await writer.WriteLineAsync($"PASS oauth:{_accessToken}");
        await writer.WriteLineAsync($"NICK {_authorizedUsername}");
        await writer.WriteLineAsync($"JOIN #{ChannelName.ToLower()}");

        TestMessageStatus = "⏳ Sending test message...";

        // 3. WAIT for connection
        string? line;
        bool connected = false;
        var timeout = DateTime.Now.AddSeconds(10);

        while (DateTime.Now < timeout && (line = await reader.ReadLineAsync()) != null)
        {
            _logger.LogInformation("IRC: {Line}", line);

            if (line.Contains("376") || line.Contains("366")) // End of MOTD or NAMES
            {
                connected = true;
                break;
            }
        }

        // 4. SEND test message
        if (connected)
        {
            await writer.WriteLineAsync($"PRIVMSG #{ChannelName.ToLower()} :VyralBot test message - Setup successful! 🎉");
            TestMessageStatus = $"✓ Test message sent to #{ChannelName}! Check your chat.";
        }
    }
    catch (Exception ex)
    {
        TestMessageStatus = $"❌ Error: {ex.Message}";
    }
}
```

#### Completion: `CompleteSetupAsync`
```csharp
[RelayCommand]
private async Task CompleteSetupAsync()
{
    _logger.LogInformation("User confirmed setup is working, saving to database");
    await FinishSetupAutomaticallyAsync();
}

private async Task FinishSetupAutomaticallyAsync()
{
    try
    {
        // 1. CREATE or UPDATE platform entity
        var platform = await _dataService.GetPlatformByNameAsync("Twitch");

        if (platform == null)
        {
            platform = new Platform
            {
                Name = "Twitch",
                Status = "Connected",
                Enabled = true,
                CreatedAt = DateTime.UtcNow
            };
        }

        // 2. SET all properties
        platform.AccessToken = TwitchOAuthService.Encrypt(_accessToken ?? "");
        platform.RefreshToken = TwitchOAuthService.Encrypt(_refreshToken ?? "");
        platform.Username = _authorizedUsername;
        platform.ClientId = _savedClientId;
        platform.ClientSecret = _savedClientSecret;
        platform.TokenExpiresAt = DateTime.UtcNow.AddHours(4);
        platform.Status = "Connected";
        platform.ConnectedAt = DateTime.UtcNow;
        platform.UpdatedAt = DateTime.UtcNow;

        // 3. SAVE to database
        await _dataService.SavePlatformAsync(platform);

        _logger.LogInformation("Twitch platform configuration saved to database");

        // 4. DISPOSE WebView2 immediately
        DisposeWebView2();

        // 5. TRIGGER completion event
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to save Twitch configuration");
        TestMessageStatus = $"❌ Failed to save: {ex.Message}";
    }
}
```

#### Log Output
```
[18:37:31 INF] Sending test message to channel: izlrp
[18:37:32 INF] IRC: :tmi.twitch.tv 001 izlrp :Welcome, GLHF!
[18:37:32 INF] Test message sent successfully
[18:37:33 INF] User confirmed setup is working, saving to database
[18:37:33 INF] Auto-finishing setup for user: izlrp
[18:37:33 INF] Twitch platform configuration saved to database
[18:37:33 INF] WebView2 disposal requested
[18:37:33 INF] Setup completed event received, refreshing platform view
```

---

## WebView2 Lifecycle Management

### Initialization Flow

```
User Clicks Button (e.g., "SIGN IN WITH TWITCH")
    ↓
ViewModel.OpenStep1BrowserAsync()
    ↓
WebView2InitializeRequested?.Invoke(this, EventArgs.Empty)
    ↓
View.OnWebView2InitializeRequested(sender, e)
    ↓
await WebView2Browser.EnsureCoreWebView2Async()
    ↓
viewModel.WebView2 = WebView2Browser
    ↓
ShowBrowser = true (triggers XAML visibility)
    ↓
WebView2.CoreWebView2.Navigate(url)
```

### Disposal Flow

```
User Completes Wizard
    ↓
ViewModel.CompleteSetupAsync()
    ↓
DisposeWebView2()
    ↓
WebView2DisposeRequested?.Invoke(this, EventArgs.Empty)
    ↓
View.OnWebView2DisposeRequested(sender, e)
    ↓
WebView2Browser.Dispose()
    ↓
GC.Collect() (force garbage collection)
```

### Code-Behind Implementation

```csharp
// INITIALIZATION
private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
{
    if (WebView2Browser == null) return;

    // Only initialize if not already initialized
    if (WebView2Browser.CoreWebView2 == null)
    {
        _logger?.LogInformation("Initializing WebView2 on-demand...");
        await WebView2Browser.EnsureCoreWebView2Async();
        _logger?.LogInformation("WebView2 initialized successfully");
    }

    _webViewInitialized = true;

    // Give ViewModel reference to WebView2
    if (DataContext is TwitchWebSetupViewModel viewModel)
    {
        viewModel.WebView2 = WebView2Browser;
    }
}

// DISPOSAL
private void OnWebView2DisposeRequested(object? sender, EventArgs e)
{
    try
    {
        if (WebView2Browser?.CoreWebView2 != null)
        {
            _logger?.LogInformation("Disposing WebView2 to free resources...");

            // Unsubscribe from all events
            WebView2Browser.CoreWebView2.NavigationStarting -= null;

            // Dispose the control
            WebView2Browser.Dispose();
            _webViewInitialized = false;

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _logger?.LogInformation("WebView2 disposed and GC forced");
        }
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Error disposing WebView2");
    }
}
```

---

## XAML Visibility System

### Multi-Condition Visibility Pattern

The wizard uses `MultiDataTrigger` to show/hide panels based on multiple conditions:

```xml
<!-- Pattern: Show when CurrentStep=X AND ShowBrowser=False -->
<StackPanel>
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="1"/>
                        <Condition Binding="{Binding ShowBrowser}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <!-- Content -->
</StackPanel>
```

### Visibility States by Step

| Step | ShowBrowser | ShowModal | Visible Panels |
|------|-------------|-----------|----------------|
| 1 | False | False | Step 1 Instructions |
| 1 | True | False | WebView2 Browser |
| 2 | False | False | Step 2 Instructions |
| 2 | True | False | WebView2 Browser |
| 2 | * | True | Modal Overlay |
| 3 | False | False | Step 3 Instructions |
| 3 | True | False | WebView2 Browser (OAuth) |
| 4 | False | False | Step 4 Test Panel |
| Success | * | * | Success Screen |

---

## Button Visibility Logic

### Bottom Action Bar Buttons

```xml
<!-- BACK Button - Steps 2, 3, 4 -->
<Button Content="← BACK" Command="{Binding GoBackCommand}">
    <Button.Style>
        <Style TargetType="Button" BasedOn="{StaticResource MaterialDesignFlatButton}">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding CurrentStep}" Value="2">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding CurrentStep}" Value="3">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
                <DataTrigger Binding="{Binding CurrentStep}" Value="4">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>

<!-- Step 1 CONTINUE - Shows when NOT in browser mode -->
<Button Content="CONTINUE" Command="{Binding ContinueFromStep1Command}">
    <Button.Style>
        <Style TargetType="Button">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="1"/>
                        <Condition Binding="{Binding ShowBrowser}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>

<!-- Step 2 Credential Buttons - Shows until both saved -->
<StackPanel Orientation="Horizontal">
    <StackPanel.Style>
        <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="2"/>
                        <Condition Binding="{Binding BothCredentialsSaved}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </StackPanel.Style>
    <Button Content="SAVE CLIENT ID" Command="{Binding SaveClientIdCommand}"/>
    <Button Content="SAVE CLIENT SECRET" Command="{Binding SaveClientSecretCommand}"/>
</StackPanel>

<!-- Step 2 CONTINUE - Shows when both credentials saved -->
<Button Content="CONTINUE" Command="{Binding ContinueFromStep2Command}">
    <Button.Style>
        <Style TargetType="Button">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="2"/>
                        <Condition Binding="{Binding BothCredentialsSaved}" Value="True"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>

<!-- Step 3 AUTHORIZE - Shows when NOT authorized -->
<Button Content="AUTHORIZE WITH TWITCH" Command="{Binding AuthorizeOAuthCommand}">
    <Button.Style>
        <Style TargetType="Button">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="3"/>
                        <Condition Binding="{Binding IsOAuthAuthorized}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>

<!-- Step 3 CONTINUE - Shows when authorized -->
<Button Content="CONTINUE" Command="{Binding ContinueFromStep3Command}">
    <Button.Style>
        <Style TargetType="Button">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding CurrentStep}" Value="3"/>
                        <Condition Binding="{Binding IsOAuthAuthorized}" Value="True"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>

<!-- Step 4 IT WORKED - Always visible on Step 4 -->
<Button Content="✓ IT WORKED!" Command="{Binding CompleteSetupCommand}">
    <Button.Style>
        <Style TargetType="Button">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding CurrentStep}" Value="4">
                    <Setter Property="Visibility" Value="Visible"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
</Button>
```

---

## Key Design Patterns

### 1. On-Demand WebView2 Initialization
- WebView2 is NOT initialized until first browser command
- Event-based: `WebView2InitializeRequested` event triggers initialization
- Reusable: Same WebView2 instance across Steps 1, 2, 3

### 2. Navigate to Blank Pattern
```csharp
// Don't dispose - just hide
ShowBrowser = false;
if (WebView2?.CoreWebView2 != null)
{
    WebView2.CoreWebView2.Navigate("about:blank");
}
```

### 3. Modal Callback Pattern
```csharp
private Action<string>? _modalSaveAction;

// Set callback when showing modal
_modalSaveAction = (value) => {
    _savedClientId = value;
    ClientIdSaved = true;
};
ShowModal = true;

// Execute callback when saved
if (_modalSaveAction != null)
{
    _modalSaveAction(ModalInput);
}
```

### 4. MultiDataTrigger Visibility
```xml
<MultiDataTrigger>
    <MultiDataTrigger.Conditions>
        <Condition Binding="{Binding PropertyA}" Value="ValueA"/>
        <Condition Binding="{Binding PropertyB}" Value="ValueB"/>
    </MultiDataTrigger.Conditions>
    <Setter Property="Visibility" Value="Visible"/>
</MultiDataTrigger>
```

### 5. Auto-Advance on Completion
```csharp
// Step 3: After OAuth success, automatically go to Step 4
IsOAuthAuthorized = true;
CurrentStep = 4;
```

---

## Critical Success Factors

### ✅ What Works

1. **WebView2 Reuse**: Same instance across steps 1-3 (no disposal until wizard complete)
2. **Event-Based Init**: WebView2 initialized on-demand via events
3. **Multi-Condition Visibility**: Clean separation of panels using `MultiDataTrigger`
4. **Modal System**: Reusable modal with callback pattern for input
5. **Property-Driven UI**: All visibility controlled by observable properties
6. **Navigation to Blank**: Hides browser without disposing
7. **Automatic Progression**: OAuth callback automatically advances to next step
8. **Complete Cleanup**: WebView2 disposed only at the very end (with GC.Collect)

### ❌ What Doesn't Work (Ngrok Issues)

1. **Missing VerticalAlignment="Stretch"**: WebView2Container ActualHeight = 0
2. **XAML Binding Breaking**: MultiDataTrigger not working properly
3. **Missing Step Panel Hiding**: Step 2 panel not hiding when ShowBrowser=True
4. **No Code-Behind Visibility Management**: Relying entirely on broken XAML bindings

---

## Comparison: Twitch vs Ngrok

| Feature | Twitch Setup | Ngrok Setup |
|---------|-------------|-------------|
| ViewModel Base | `ObservableObject` (standalone) | `ObservableObject` (standalone) |
| WebView2 Init | Event-based, on-demand | Event-based, on-demand |
| WebView2 Disposal | After wizard complete | After wizard complete |
| Navigate to Blank | ✅ Yes (between steps) | ✅ Yes (between steps) |
| Visibility Management | XAML MultiDataTrigger | XAML MultiDataTrigger (BROKEN) |
| Container Alignment | Not specified (defaults) | **Missing Stretch** |
| Step Panel Hiding | ✅ Works via binding | **Not hiding** |
| Code-Behind Help | Minimal | **Added force visibility** |

---

## Recommended Base Wizard Architecture

### Step Type Taxonomy

```csharp
public enum StepType
{
    BrowserOnly,           // Step 1: Just open browser + continue
    BrowserWithSaveItem,   // Step 2: Browser + 1 modal save
    BrowserWithSaveItems2, // Browser + 2 modal saves
    BrowserWithSaveItems3, // Browser + 3 modal saves
    OAuth,                 // Step 3: OAuth flow with callback
    TestConnection,        // Step 4: Send test + confirm
    Success                // Final success screen
}
```

### Base Step Configuration

```csharp
public class WizardStepConfig
{
    public int StepNumber { get; set; }
    public StepType Type { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string ButtonText { get; set; }
    public string BrowserUrl { get; set; }
    public List<SaveItemConfig>? SaveItems { get; set; }
}

public class SaveItemConfig
{
    public string PropertyName { get; set; }
    public string ModalTitle { get; set; }
    public string ModalDescription { get; set; }
    public string ButtonText { get; set; }
}
```

### Example Wizard Definition

```csharp
// Ngrok Wizard = [BrowserOnly, BrowserWithSaveItem, OAuth, TestConnection]
var ngrokWizard = new WizardDefinition
{
    PlatformName = "Ngrok",
    Steps = new List<WizardStepConfig>
    {
        new() {
            StepNumber = 1,
            Type = StepType.BrowserOnly,
            Title = "Install Ngrok",
            BrowserUrl = "ms-windows-store://pdp/?ProductId=9NBLGGH6L5JW",
            ButtonText = "OPEN MICROSOFT STORE"
        },
        new() {
            StepNumber = 2,
            Type = StepType.BrowserWithSaveItem,
            Title = "Get Auth Token",
            BrowserUrl = "https://dashboard.ngrok.com",
            ButtonText = "OPEN NGROK DASHBOARD",
            SaveItems = new List<SaveItemConfig>
            {
                new() {
                    PropertyName = "AuthToken",
                    ModalTitle = "Save Auth Token",
                    ModalDescription = "Copy token from dashboard",
                    ButtonText = "SAVE AUTH TOKEN"
                }
            }
        },
        new() {
            StepNumber = 3,
            Type = StepType.TestConnection,
            Title = "Test Connection",
            ButtonText = "TEST CONNECTION"
        }
    }
};
```

---

## Conclusion

The Twitch setup wizard succeeds because:

1. **WebView2 is initialized ON-DEMAND** via events
2. **Same WebView2 instance** is reused across all steps
3. **Navigate to "about:blank"** hides browser without disposal
4. **Multi-condition visibility** cleanly separates panels
5. **WebView2 disposed ONLY at the end** with forced GC
6. **All visibility driven by observable properties**
7. **VerticalAlignment/HorizontalAlignment defaults work** because XAML bindings work

The Ngrok wizard fails because:

1. **XAML MultiDataTrigger binding is broken**
2. **WebView2Container has ActualHeight = 0** (missing VerticalAlignment="Stretch")
3. **Step panels don't hide when ShowBrowser=True**
4. **Code-behind workarounds don't fix root XAML binding issue**

### Next Steps

1. Create `BaseSetupWizardViewModel` with step type system
2. Create `BaseSetupWizardView.xaml` with step type templates
3. Add **VerticalAlignment="Stretch" HorizontalAlignment="Stretch"** to WebView2Container
4. Use **code-behind visibility management** instead of broken XAML bindings
5. Test with Ngrok wizard using new base classes
