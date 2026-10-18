using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Api.Services;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Ngrok Setup wizard - standalone setup for ngrok integration
/// </summary>
public partial class NgrokSetupWizardViewModel : BaseOAuthSetupViewModel
{
    // Configuration Properties
    public override string WizardTitle => "Ngrok Setup";
    public override string PlatformName => "Ngrok";
    public override string Step1Title => "Step 1: Install Ngrok";
    public override string Step1ButtonText => "DOWNLOAD FROM MICROSOFT STORE";
    public override string Step2Title => "Step 2: Sign in to ngrok";
    public override string Step2ButtonText => "OPEN NGROK SIGN IN";
    public override string Step3Title => "Step 3: Access ngrok Dashboard";
    public override string Step3ButtonText => "OPEN NGROK DASHBOARD";
    public override string Step4Title => "Step 4: Verify Ngrok Setup";

    // Ngrok-specific properties
    [ObservableProperty]
    private bool _isNgrokInstalled = false;
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private bool _tokenSaved = false;
    
    [ObservableProperty]
    private bool _testPassed = false;
    
    [ObservableProperty]
    private bool _isTesting = false;
    
    [ObservableProperty]
    private string _publicUrl = "";

    private string? _savedNgrokAuth;

    private readonly NgrokService _ngrokService;
    private readonly WebServerService _webServerService;
    private readonly IServiceProvider _serviceProvider;

    public NgrokSetupWizardViewModel(
        ILogger<NgrokSetupWizardViewModel> logger,
        IDataService dataService,
        NgrokService ngrokService,
        WebServerService webServerService,
        IServiceProvider serviceProvider)
        : base(logger, dataService)
    {
        _ngrokService = ngrokService;
        _webServerService = webServerService;
        _serviceProvider = serviceProvider;
        _ = CheckInstallationAsync();
        _ = RestoreSavedTokenAsync();
    }

    // Ngrok Step 1 Commands
    [RelayCommand]
    private void OpenStep1External()
    {
        try
        {
            _logger.LogInformation("Opening Microsoft Store for ngrok download");
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-windows-store://pdp/?productid=9MVS1J51GMK6",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Microsoft Store");
        }
    }

    [RelayCommand]
    private async Task CheckInstallationAsync()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ngrok",
                Arguments = "version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    IsNgrokInstalled = true;
                    StatusMessage = $"✓ Ngrok is installed: {output.Trim()}";
                    _logger.LogInformation("Ngrok installation verified");
                }
                else
                {
                    IsNgrokInstalled = false;
                    StatusMessage = "Ngrok is not installed or not in PATH";
                }
            }
        }
        catch (Exception ex)
        {
            IsNgrokInstalled = false;
            StatusMessage = "Ngrok is not installed";
            _logger.LogDebug(ex, "Ngrok not found");
        }
    }

    // Ngrok Step 3 Command - Single auth token save
    [RelayCommand]
    private void SaveAuthToken()
    {
        ModalTitle = "Save Auth Token";
        ModalDescription = "Copy your authentication token from the ngrok dashboard and paste it below.";
        ModalInput = "";
        _modalSaveAction = async (value) =>
        {
            await Task.CompletedTask;
            _savedNgrokAuth = value.Trim(); // Store in memory only
            TokenSaved = true;
            _logger.LogInformation("Ngrok auth token saved to memory");
        };
        ShowModal = true;
    }

    // Ngrok Step 3 Command - Open dashboard in WebView2
    [RelayCommand]
    private async Task OpenStep3BrowserAsync()
    {
        _logger.LogInformation("Opening ngrok dashboard in Step 3");
        
        try
        {
            OnWebView2InitializeRequested();
            _logger.LogInformation("WebView2 initialization requested for Step 3, waiting...");
            await Task.Delay(1000);
            
            if (WebView2?.CoreWebView2 != null)
            {
                ShowModal = false;
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate("https://dashboard.ngrok.com/get-started/your-authtoken");
                _logger.LogInformation("Navigated to ngrok auth token page");
            }
            else
            {
                _logger.LogWarning("WebView2 not initialized after event for Step 3");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate WebView2 in Step 3");
        }
    }

    protected override string GetStep1Url()
    {
        // Step 1 doesn't use WebView2 anymore - it's installation check
        return "";
    }

    protected override string GetStep2Url()
    {
        return "https://dashboard.ngrok.com/login";
    }

    protected override async Task SaveCredentialsAsync(string clientId, string clientSecret)
    {
        // Ngrok does not use OAuth client credentials. The auth token is collected
        // separately (SaveAuthToken) and persisted via NgrokService - nothing to
        // write to environment variables (see KI-22 in the known issues ledger).
        await Task.CompletedTask;
    }

    protected override async Task LoadCredentialsAsync()
    {
        // No OAuth client credentials for ngrok — reload the saved auth token
        // (same per-profile store OnCompleteSetupAsync writes via NgrokService)
        // so the wizard reflects the persisted state.
        await RestoreSavedTokenAsync();
    }

    /// <summary>
    /// Reads the auth token back from the per-profile credentials file via
    /// NgrokService and mirrors it into <see cref="_savedNgrokAuth"/> (so
    /// TestConnection works on a reopened wizard) and <see cref="TokenSaved"/>
    /// (so the UI shows the saved state). The token value is never logged.
    /// </summary>
    private async Task RestoreSavedTokenAsync()
    {
        try
        {
            await _ngrokService.LoadAuthTokenAsync();
            var saved = _ngrokService.AuthToken;
            _savedNgrokAuth = string.IsNullOrWhiteSpace(saved) ? null : saved;
            TokenSaved = _savedNgrokAuth is not null;
            if (TokenSaved)
            {
                _logger.LogInformation("Ngrok auth token restored from profile credentials ({Length} chars)", _savedNgrokAuth!.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore the saved ngrok auth token");
        }
    }

    protected override async Task OnCompleteSetupAsync()
    {
        _logger.LogInformation("Saving ngrok auth token to JSON file after successful test");
        
        if (!string.IsNullOrEmpty(_savedNgrokAuth))
        {
            try
            {
                // Save to JSON file via NgrokService
                await _ngrokService.SaveAuthTokenAsync(_savedNgrokAuth);

                // Run ngrok config add-authtoken command
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "ngrok",
                    Arguments = $"config add-authtoken {_savedNgrokAuth}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process != null)
                {
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation("Ngrok auth token saved successfully to JSON file and ngrok config");
                    }
                    else
                    {
                        var error = await process.StandardError.ReadToEndAsync();
                        _logger.LogError("Ngrok authentication failed: {Error}", error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save ngrok credentials");
            }
        }
        else
        {
            _logger.LogWarning("No auth token to save");
        }
    }

    protected override string BuildAuthorizationUrl()
    {
        if (string.IsNullOrEmpty(_savedClientId))
            return "";
        
        return $"https://dashboard.ngrok.com/oauth/authorize?client_id={_savedClientId}&redirect_uri=http://localhost:3000&response_type=code&scope=openid%20email%20profile";
    }

    protected override async Task<(string AccessToken, string RefreshToken, string Username)?> HandleOAuthCallbackAsync(string url)
    {
        await Task.CompletedTask;
        // Ngrok implementation - return dummy values for now
        return ("ngrok_access_token", "ngrok_refresh_token", "NgrokUser");
    }

    protected override void OnFinishSetup()
    {
        // Reset ngrok-specific state
        _savedNgrokAuth = null;
        TokenSaved = false;
        TestPassed = false;
        IsTesting = false;
        IsNgrokInstalled = false;
        StatusMessage = "";
        PublicUrl = "";
        TestMessageStatus = "Click 'TEST CONNECTION' to verify the connection";
        
        _logger.LogInformation("Ngrok wizard state reset, disposing Settings");

        // Dispose SettingsViewModel so it reloads with the completed ngrok setup.
        // Deliberately do NOT navigate here: SetupCompleted (fired right after this)
        // already makes MainViewModel navigate, and a second navigation here would
        // race with it.
        var mainViewModel = _serviceProvider.GetService<MainViewModel>();
        if (mainViewModel != null)
        {
            mainViewModel.DisposeSettingsViewModel();
            _logger.LogInformation("SettingsViewModel disposed - navigation handled by the SetupCompleted handler");
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestPassed = false;
        await SendTestMessageAsync();
        TestPassed = TestMessageStatus.Contains("✓");
        IsTesting = false;
    }

    protected override async Task SendTestMessageAsync()
    {
        _logger.LogInformation("Testing ngrok connection");
        
        StatusMessage = "⏳ Validating auth token...";
        TestMessageStatus = StatusMessage;
        
        try
        {
            // Validate auth token from memory
            if (string.IsNullOrWhiteSpace(_savedNgrokAuth))
            {
                StatusMessage = "✗ No auth token configured";
                TestMessageStatus = StatusMessage;
                return;
            }
            
            // Check if token looks valid (ngrok tokens are typically 2-part strings)
            if (_savedNgrokAuth.Length < 20 || !_savedNgrokAuth.Contains("_"))
            {
                StatusMessage = "✗ Auth token appears invalid - ngrok tokens should be in format: 2abc...xyz_4def...";
                TestMessageStatus = StatusMessage;
                // Masked: never log any fragment of the token.
                _logger.LogWarning("Saved auth token appears invalid (length {Length})", _savedNgrokAuth.Length);
                return;
            }
            
            StatusMessage = "⏳ Starting web server...";
            TestMessageStatus = StatusMessage;
            
            // Tunnel/web-server port comes from the persisted web-server settings (5000 fallback)
            var settings = await _dataService.GetWebServerSettingsAsync();
            var port = settings.Port > 0 ? settings.Port : 5000;

            // Start web server
            var webServerStarted = await _webServerService.StartAsync(port);
            if (!webServerStarted)
            {
                StatusMessage = $"✗ Failed to start web server - Port {port} may be in use";
                TestMessageStatus = StatusMessage;
                return;
            }

            StatusMessage = "⏳ Starting ngrok tunnel...";
            TestMessageStatus = StatusMessage;
            await Task.Delay(500);
            
            // Start ngrok tunnel using memory token
            var ngrokStarted = await _ngrokService.StartTunnelAsync(
                port,
                _savedNgrokAuth,
                settings.NgrokRegion);

            if (!ngrokStarted)
            {
                StatusMessage = "✗ Failed to start ngrok tunnel - Check authentication token is correct";
                TestMessageStatus = StatusMessage;
                await _webServerService.StopAsync();
                return;
            }

            var publicUrl = _ngrokService.PublicUrl;
            PublicUrl = publicUrl ?? "";
            StatusMessage = $"⏳ Testing webhook endpoint: {publicUrl}";
            TestMessageStatus = StatusMessage;

            // Test health endpoint
            await Task.Delay(1000);

            using var httpClient = new System.Net.Http.HttpClient 
            { 
                Timeout = TimeSpan.FromSeconds(10) 
            };
            
            var response = await httpClient.GetAsync($"{publicUrl}/webhooks/health");

            if (response.IsSuccessStatusCode)
            {
                StatusMessage = $"✓ All tests passed! Tunnel: {publicUrl}";
                TestMessageStatus = StatusMessage;
                TestPassed = true;
                _logger.LogInformation("Ngrok setup test successful");
            }
            else
            {
                StatusMessage = $"✗ Health check failed - Status: {response.StatusCode}";
                TestMessageStatus = StatusMessage;
                TestPassed = false;
            }

            // Clean up
            await Task.Delay(2000);
            await _ngrokService.StopTunnelAsync();
            await _webServerService.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing ngrok connection");
            StatusMessage = $"✗ Test failed: {ex.Message}";
            TestMessageStatus = StatusMessage;
            TestPassed = false;

            // Clean up on error
            try
            {
                await _ngrokService.StopTunnelAsync();
                await _webServerService.StopAsync();
            }
            catch { }
        }
    }
}
