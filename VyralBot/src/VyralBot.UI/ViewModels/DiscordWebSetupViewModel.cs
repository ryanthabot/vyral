using System;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Setup wizard for Discord. Discord uses a bot-token flow instead of the OAuth
/// authorization-code flow used by Twitch/Kick, so this wizard does not derive from
/// <see cref="BaseOAuthSetupViewModel"/> — the user creates a Discord application,
/// copies its bot token, invites the bot to a server, and connects with the token.
/// The WebView2/step/modal surface mirrors the base class so PlatformsView can host it.
/// </summary>
public partial class DiscordWebSetupViewModel : ViewModelBase
{
    private const string DeveloperPortalUrl = "https://discord.com/developers/applications";

    /// <summary>
    /// Administrator (8) — the owner explicitly wants the bot to request full
    /// permissions so it can manage the server without re-inviting later.
    /// </summary>
    private const long InvitePermissions = 8;

    private readonly ILogger<DiscordWebSetupViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IDiscordService _discordService;

    public WebView2? WebView2 { get; set; }

    public event EventHandler? WebView2InitializeRequested;
    public event EventHandler? WebView2DisposeRequested;
    public event EventHandler? SetupCompleted;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _showSuccessScreen = false;

    [ObservableProperty]
    private bool _showModal = false;

    [ObservableProperty]
    private bool _showBrowser = false;

    [ObservableProperty]
    private bool _tokenSaved = false;

    [ObservableProperty]
    private string _maskedBotToken = "";

    [ObservableProperty]
    private bool _hasInviteUrl = false;

    [ObservableProperty]
    private string? _inviteUrl;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isConnecting = false;

    [ObservableProperty]
    private string _connectedBotUsername = "";

    [ObservableProperty]
    private string _connectStatus = "Not connected";

    [ObservableProperty]
    private string _manualTokenInput = "";

    [ObservableProperty]
    private string _testMessageStatus = "Click 'Send Test Message' to verify the connection";

    [ObservableProperty]
    private string _modalTitle = "";

    [ObservableProperty]
    private string _modalDescription = "";

    [ObservableProperty]
    private string _modalInput = "";

    private string? _botToken;
    private bool _tokenValidated;
    private string? _applicationId;
    private Action<string>? _modalSaveAction;

    public DiscordWebSetupViewModel(
        ILogger<DiscordWebSetupViewModel> logger,
        IDataService dataService,
        IDiscordService discordService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
    }

    /// <summary>
    /// Derives the bot/application id from a Discord bot token: the first segment
    /// before the first '.' is the base64url-encoded numeric user id.
    /// Shared with the Discord dashboard, which rebuilds the invite link from the stored token.
    /// </summary>
    public static string? TryGetApplicationIdFromToken(string? botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return null;

        try
        {
            var firstSegment = botToken.Trim().Split('.')[0];
            if (firstSegment.Length == 0)
                return null;

            // Base64url -> Base64, then fix up padding
            var base64 = firstSegment.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return ulong.TryParse(decoded, out _) ? decoded : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the bot-invite URL (View Channels + Send Messages + Read Message History)
    /// from a stored bot token, or null when the application id cannot be derived.
    /// </summary>
    public static string? BuildInviteUrlFromToken(string? botToken)
    {
        var applicationId = TryGetApplicationIdFromToken(botToken);
        return applicationId is null
            ? null
            : $"https://discord.com/oauth2/authorize?client_id={applicationId}&scope=bot&permissions={InvitePermissions}";
    }

    [RelayCommand]
    private void GoToStep1()
    {
        _logger.LogInformation("GoToStep1 called");
        HideBrowser();
        CurrentStep = 1;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep2()
    {
        _logger.LogInformation("GoToStep2 called");
        HideBrowser();
        CurrentStep = 2;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep3()
    {
        _logger.LogInformation("GoToStep3 called");
        HideBrowser();
        CurrentStep = 3;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep4()
    {
        _logger.LogInformation("GoToStep4 called");
        HideBrowser();
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 1)
        {
            HideBrowser();
            CurrentStep--;
            _logger.LogInformation("Going back to step {Step}", CurrentStep);
        }
    }

    // ===== Step 1: Create Application (embedded Developer Portal) =====

    [RelayCommand]
    private async Task OpenStep1BrowserAsync()
    {
        await OpenDeveloperPortalAsync();
    }

    // ===== Step 2: Create Bot & Copy Token =====

    [RelayCommand]
    private async Task OpenStep2BrowserAsync()
    {
        await OpenDeveloperPortalAsync();
    }

    private async Task OpenDeveloperPortalAsync()
    {
        _logger.LogInformation("Opening Discord Developer Portal in WebView2");
        try
        {
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate(DeveloperPortalUrl);
                _logger.LogInformation("WebView2 navigated to the Discord Developer Portal");
            }
            else
            {
                _logger.LogWarning("WebView2 not initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate WebView2");
        }
    }

    [RelayCommand]
    private void SaveBotToken()
    {
        _logger.LogInformation("SaveBotToken button clicked");
        ModalTitle = "Save Bot Token";
        ModalDescription = "In the Discord Developer Portal, open your application, go to the Bot tab, click 'Reset Token' and copy it. Paste the bot token below.";
        ModalInput = "";
        _modalSaveAction = value =>
        {
            var token = value.Trim();
            if (token.Length == 0)
                return;

            _botToken = token;
            _tokenValidated = false;
            _applicationId = TryGetApplicationIdFromToken(token);
            ApplyTokenDerivedState();

            _logger.LogInformation("Bot token saved (application id derived: {Derived})", _applicationId != null ? "yes" : "no");

            if (!HasInviteUrl)
            {
                ConnectStatus = "Token saved, but the invite link could not be derived from it. You can still connect below.";
            }
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void ContinueFromStep1()
    {
        _logger.LogInformation("Continuing from Step 1");
        HideBrowser();
        CurrentStep = 2;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void ContinueFromStep2()
    {
        _logger.LogInformation("Continuing from Step 2 to Step 3");
        HideBrowser();
        CurrentStep = 3;
        ShowSuccessScreen = false;

        if (!IsConnected)
        {
            ConnectStatus = HasInviteUrl
                ? "Invite the bot to a server, then click 'Connect Bot'."
                : "Paste the bot token in the manual field below, then click 'Use Token'.";
        }
    }

    // ===== Step 3: Invite & Authorize =====

    [RelayCommand]
    private async Task OpenInviteBrowserAsync()
    {
        if (InviteUrl is null)
        {
            ConnectStatus = "Save the bot token first (Step 2) so the invite link can be built.";
            return;
        }

        _logger.LogInformation("Opening Discord bot invite link in WebView2");
        try
        {
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate(InviteUrl);
                _logger.LogInformation("WebView2 navigated to the Discord invite URL");
            }
            else
            {
                _logger.LogWarning("WebView2 not initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate WebView2 to the invite URL");
        }
    }

    [RelayCommand]
    private void CloseBrowser()
    {
        _logger.LogInformation("Closing the embedded browser");
        HideBrowser();
    }

    [RelayCommand]
    private async Task ConnectBotAsync()
    {
        if (IsConnecting)
            return;

        var token = !string.IsNullOrWhiteSpace(_botToken) ? _botToken : ManualTokenInput?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ConnectStatus = "Paste the bot token first — use 'Save Bot Token' in Step 2 or the manual field below.";
            return;
        }

        IsConnecting = true;
        try
        {
            ConnectStatus = "Validating bot token...";
            if (!await _discordService.ValidateTokenAsync(token))
            {
                ConnectStatus = "Invalid bot token. Make sure you copied the whole token from the Bot tab (Step 2).";
                _logger.LogWarning("Discord bot token validation failed");
                return;
            }

            ConnectStatus = "Connecting to Discord...";
            if (!await _discordService.ConnectAsync(token))
            {
                ConnectStatus = "Could not connect to Discord with this token. Please try again.";
                _logger.LogWarning("Discord ConnectAsync returned false");
                return;
            }

            _botToken = token;
            _tokenValidated = true;
            _applicationId = TryGetApplicationIdFromToken(token);
            ApplyTokenDerivedState();

            IsConnected = true;
            ConnectedBotUsername = _discordService.BotUsername ?? "your bot";
            ConnectStatus = $"Connected as {ConnectedBotUsername}";
            _logger.LogInformation("Connected to Discord as {Username}", ConnectedBotUsername);
        }
        catch (Exception ex)
        {
            ConnectStatus = $"Connection failed: {ex.Message}";
            _logger.LogError(ex, "Discord connect failed");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void ContinueFromStep3()
    {
        _logger.LogInformation("Continuing from Step 3 to Step 4");
        HideBrowser();
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }

    // ===== Step 4: Test & Finish =====

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        if (!IsConnected)
        {
            TestMessageStatus = "Connect the bot first (Step 3).";
            return;
        }

        TestMessageStatus = "Creating a private test channel and pinging the server owner...";
        try
        {
            TestMessageStatus = await _discordService.SendOwnerTestMessageAsync();
        }
        catch (Exception ex)
        {
            TestMessageStatus = $"Error: {ex.Message}";
            _logger.LogError(ex, "Failed to send Discord owner test message");
        }
    }

    [RelayCommand]
    private async Task CompleteSetupAsync()
    {
        // Guard: never persist "Connected" unless a token was actually entered and validated.
        if (string.IsNullOrWhiteSpace(_botToken) || !_tokenValidated)
        {
            TestMessageStatus = "A validated bot token is required. Go back to Step 3 and connect the bot first.";
            _logger.LogWarning("Finish attempted without a validated Discord bot token");
            return;
        }

        _logger.LogInformation("Finishing Discord setup and transitioning to connected view");
        try
        {
            var platform = await _dataService.GetPlatformByNameAsync("Discord")
                ?? new Platform
                {
                    Name = "Discord",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow
                };

            platform.Status = "Connected";
            platform.AccessToken = _botToken;
            platform.ClientId = _applicationId;
            platform.Username = ConnectedBotUsername.Length > 0 ? ConnectedBotUsername : _discordService.BotUsername;
            platform.LastError = null;
            platform.ConnectedAt = DateTime.UtcNow;
            platform.UpdatedAt = DateTime.UtcNow;

            await _dataService.SavePlatformAsync(platform);
            _logger.LogInformation("Discord platform row saved as Connected");

            FinishSetup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Discord platform configuration");
            TestMessageStatus = $"Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void FinishSetup()
    {
        DisposeWebView2();

        SetupCompleted?.Invoke(this, EventArgs.Empty);

        // Reset wizard state for the next run
        ShowSuccessScreen = false;
        CurrentStep = 1;
        ShowBrowser = false;
        ShowModal = false;
        TokenSaved = false;
        MaskedBotToken = "";
        HasInviteUrl = false;
        InviteUrl = null;
        IsConnected = false;
        IsConnecting = false;
        ConnectedBotUsername = "";
        ConnectStatus = "Not connected";
        ManualTokenInput = "";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";
        ModalTitle = "";
        ModalDescription = "";
        ModalInput = "";
        _botToken = null;
        _tokenValidated = false;
        _applicationId = null;
        _modalSaveAction = null;
    }

    // ===== Modal =====

    [RelayCommand]
    private void CloseModal()
    {
        _logger.LogInformation("Modal closed");
        ShowModal = false;
        ModalInput = "";
        _modalSaveAction = null;
    }

    [RelayCommand]
    private void SaveModal()
    {
        _logger.LogInformation("SaveModal called");
        if (!string.IsNullOrWhiteSpace(ModalInput) && _modalSaveAction != null)
        {
            _modalSaveAction(ModalInput);
            ShowModal = false;
            ModalInput = "";
            _modalSaveAction = null;
        }
    }

    // ===== Shared plumbing =====

    private void ApplyTokenDerivedState()
    {
        if (string.IsNullOrWhiteSpace(_botToken))
            return;

        TokenSaved = true;
        MaskedBotToken = (_botToken.Length > 8 ? _botToken[..8] : _botToken) + "...";
        InviteUrl = BuildInviteUrlFromToken(_botToken);
        HasInviteUrl = InviteUrl != null;
    }

    private void HideBrowser()
    {
        ShowBrowser = false;
        try
        {
            WebView2?.CoreWebView2?.Navigate("about:blank");
        }
        catch
        {
            // WebView2 might already be torn down
        }
    }

    public void ResetWizard(bool disposeWebView = false)
    {
        _logger.LogInformation("Resetting Discord setup wizard (disposeWebView={DisposeWebView})", disposeWebView);

        CurrentStep = 1;
        ShowSuccessScreen = false;
        ShowBrowser = false;
        ShowModal = false;

        TokenSaved = false;
        MaskedBotToken = "";
        HasInviteUrl = false;
        InviteUrl = null;

        IsConnected = false;
        IsConnecting = false;
        ConnectedBotUsername = "";
        ConnectStatus = "Not connected";
        ManualTokenInput = "";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";

        ModalTitle = "";
        ModalDescription = "";
        ModalInput = "";

        _botToken = null;
        _tokenValidated = false;
        _applicationId = null;
        _modalSaveAction = null;

        if (WebView2?.CoreWebView2 != null)
        {
            try
            {
                if (disposeWebView)
                {
                    DisposeWebView2();
                }
                else
                {
                    WebView2.CoreWebView2.Navigate("about:blank");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset WebView2");
            }
        }

        _logger.LogInformation("Discord wizard reset complete");
    }

    private void DisposeWebView2()
    {
        WebView2DisposeRequested?.Invoke(this, EventArgs.Empty);
        WebView2 = null;
        _logger.LogInformation("WebView2 disposal requested");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
