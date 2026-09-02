using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Api.Services;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Microsoft.Web.WebView2.Wpf;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Ngrok setup wizard - standalone implementation matching Twitch's pattern
/// </summary>
public partial class NgrokSetupViewModel : ObservableObject
{
    private readonly ILogger<NgrokSetupViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly NgrokService _ngrokService;
    private readonly WebServerService _webServerService;

    public WebView2? WebView2 { get; set; }

    public event EventHandler? WebView2InitializeRequested;
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
    private string _modalTitle = "";

    [ObservableProperty]
    private string _modalDescription = "";

    [ObservableProperty]
    private string _modalInput = "";

    private string? _savedToken;
    private Action<string>? _modalSaveAction;
    private string? _authToken;
    
    [ObservableProperty]
    private bool _isNgrokInstalled = false;
    
    [ObservableProperty]
    private bool _testPassed = false;
    
    [ObservableProperty]
    private bool _isTesting = false;
    
    [ObservableProperty]
    private string _statusMessage = "";
    
    [ObservableProperty]
    private string _publicUrl = "";

    [ObservableProperty]
    private string _testMessageStatus = "Click 'TEST CONNECTION' to verify the connection";

    public NgrokSetupViewModel(
        ILogger<NgrokSetupViewModel> logger,
        IDataService dataService,
        NgrokService ngrokService,
        WebServerService webServerService)
    {
        _logger = logger;
        _dataService = dataService;
        _ngrokService = ngrokService;
        _webServerService = webServerService;
        _ = CheckInstallationAsync();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    public void ResetWizardState()
    {
        _logger.LogInformation("Resetting ngrok wizard state");
        CurrentStep = 1;
        ShowSuccessScreen = false;
        ShowBrowser = false;
        ShowModal = false;
        TokenSaved = false;
        TestPassed = false;
        _savedToken = null;
        
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        _ = CheckInstallationAsync();
    }

    [RelayCommand]
    private void GoToStep1()
    {
        _logger.LogInformation("GoToStep1 called");
        CurrentStep = 1;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep2()
    {
        _logger.LogInformation("GoToStep2 called");
        CurrentStep = 2;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep4()
    {
        _logger.LogInformation("GoToStep4 called");
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }
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
    private async Task OpenStep2BrowserAsync()
    {
        _logger.LogInformation("Opening Ngrok Dashboard in WebView2");
        _logger.LogInformation("BEFORE: ShowBrowser={ShowBrowser}, ShowModal={ShowModal}, CurrentStep={CurrentStep}", ShowBrowser, ShowModal, CurrentStep);
        try
        {
            // Request WebView2 initialization if not already done
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            
            // Wait a moment for initialization
            await Task.Delay(500);
            
            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                ShowModal = false;
                _logger.LogInformation("AFTER SETTING: ShowBrowser={ShowBrowser}, ShowModal={ShowModal}, CurrentStep={CurrentStep}", ShowBrowser, ShowModal, CurrentStep);
                WebView2.CoreWebView2.Navigate("https://dashboard.ngrok.com/get-started/your-authtoken");
                _logger.LogInformation("WebView2 navigated to Ngrok Dashboard");
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
    private void ContinueFromStep1()
    {
        _logger.LogInformation("Continuing from Step 1");
        ShowBrowser = false;
        
        // Don't dispose WebView2 - just hide it for reuse
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        CurrentStep = 2;
    }

    [RelayCommand]
    private async Task ContinueFromStep2Async()
    {
        _logger.LogInformation("Continuing from Step 2 to Step 4");
        ShowBrowser = false;
        
        // Don't dispose WebView2 - just hide it for reuse
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        // Save credentials if we have them
        if (!string.IsNullOrEmpty(_savedToken))
        {
            await SaveCredentialsAsync(_savedToken);
            _logger.LogInformation("Token saved, moving to Step 4");
        }
        
        // Move to Step 4 after credentials are saved
        CurrentStep = 4;
        ShowSuccessScreen = false;
        TestMessageStatus = "Ready to test. Click 'TEST CONNECTION' below.";
    }

    [RelayCommand]
    private void SaveAuthToken()
    {
        _logger.LogInformation("SaveAuthToken button clicked");
        ModalTitle = "Save Auth Token";
        ModalDescription = "Copy your authentication token from the ngrok dashboard and paste it below.";
        ModalInput = "";
        _modalSaveAction = (value) =>
        {
            _savedToken = value;
            TokenSaved = true;
            OnPropertyChanged(nameof(TokenSaved));
            _logger.LogInformation("Auth Token saved");
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void ConfirmModalSave()
    {
        _logger.LogInformation("ConfirmModalSave clicked, input: {Input}", ModalInput);
        _modalSaveAction?.Invoke(ModalInput);
        ShowModal = false;
        ModalInput = "";
    }

    [RelayCommand]
    private void CancelModal()
    {
        _logger.LogInformation("CancelModal clicked");
        ShowModal = false;
        ModalInput = "";
    }

    private async Task SaveCredentialsAsync(string token)
    {
        _authToken = token;
        
        try
        {
            // Save to JSON file via NgrokService
            await _ngrokService.SaveAuthTokenAsync(_authToken.Trim());

            // Run ngrok config add-authtoken command
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ngrok",
                Arguments = $"config add-authtoken {_authToken.Trim()}",
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
                    _logger.LogInformation("Ngrok auth token saved successfully");
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

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestPassed = false;
        await SendTestMessageAsync();
        TestPassed = TestMessageStatus.Contains("✓");
        IsTesting = false;
    }

    [RelayCommand]
    private void CompleteSetup()
    {
        _logger.LogInformation("Setup completed successfully");
        ShowSuccessScreen = false;
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void FinishSetup()
    {
        _logger.LogInformation("Finishing setup wizard");
        ShowSuccessScreen = false;
        CurrentStep = 1;
        ShowBrowser = false;
        ShowModal = false;
        
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task SendTestMessageAsync()
    {
        _logger.LogInformation("Testing ngrok connection");
        
        StatusMessage = "⏳ Validating auth token...";
        TestMessageStatus = StatusMessage;
        
        try
        {
            // Load auth token from JSON file
            await _ngrokService.LoadAuthTokenAsync();
            var authToken = _ngrokService.AuthToken;
            
            if (string.IsNullOrWhiteSpace(authToken))
            {
                StatusMessage = "✗ No auth token configured";
                TestMessageStatus = StatusMessage;
                return;
            }
            
            // Check if token looks valid (ngrok tokens are typically 2-part strings)
            if (authToken.Length < 20 || !authToken.Contains("_"))
            {
                StatusMessage = "✗ Auth token appears invalid - ngrok tokens should be in format: 2abc...xyz_4def...";
                TestMessageStatus = StatusMessage;
                _logger.LogWarning("Auth token appears invalid: {Token}", authToken.Substring(0, Math.Min(5, authToken.Length)));
                return;
            }
            
            StatusMessage = "⏳ Starting web server...";
            TestMessageStatus = StatusMessage;
            
            // Start web server
            var webServerStarted = await _webServerService.StartAsync(5000);
            if (!webServerStarted)
            {
                StatusMessage = "✗ Failed to start web server - Port 5000 may be in use";
                TestMessageStatus = StatusMessage;
                return;
            }

            StatusMessage = "⏳ Starting ngrok tunnel...";
            TestMessageStatus = StatusMessage;
            await Task.Delay(500);

            var settings = await _dataService.GetWebServerSettingsAsync();

            // Start ngrok tunnel
            var ngrokStarted = await _ngrokService.StartTunnelAsync(
                5000,
                authToken,
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

                // Update settings
                settings.CurrentTunnelUrl = publicUrl;
                await _dataService.SaveWebServerSettingsAsync(settings);
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
