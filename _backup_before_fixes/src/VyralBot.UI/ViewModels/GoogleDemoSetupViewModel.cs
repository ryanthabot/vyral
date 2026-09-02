using Microsoft.Extensions.Logging;
using VyralBot.Data;
using System;
using System.Threading.Tasks;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Google Demo setup wizard - demonstrates the reusable BaseOAuthSetupViewModel
/// Only needs to configure URLs, branding, and platform-specific OAuth logic
/// </summary>
public partial class GoogleDemoSetupViewModel : BaseOAuthSetupViewModel
{
    // Configuration Properties
    public override string WizardTitle => "Google Demo Setup Wizard";
    public override string PlatformName => "Google";
    public override string Step1Title => "Step 1: Sign In to Google";
    public override string Step1ButtonText => "SIGN IN WITH GOOGLE";
    public override string Step2Title => "Step 2: Create Application";
    public override string Step2ButtonText => "OPEN DEVELOPER CONSOLE";
    public override string Step3Title => "Step 3: Authorize Account";
    public override string Step3ButtonText => "AUTHORIZE WITH GOOGLE";
    public override string Step4Title => "Step 4: Test Connection";

    public GoogleDemoSetupViewModel(
        ILogger<GoogleDemoSetupViewModel> logger,
        IDataService dataService)
        : base(logger, dataService)
    {
    }

    protected override string GetStep1Url()
    {
        return "https://accounts.google.com/";
    }

    protected override string GetStep2Url()
    {
        return "https://console.cloud.google.com/";
    }

    protected override async Task SaveCredentialsAsync(string clientId, string clientSecret)
    {
        await Task.CompletedTask;
        try
        {
            Environment.SetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_ID", clientId);
            Environment.SetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_SECRET", clientSecret);
            Environment.SetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_ID", clientId, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_SECRET", clientSecret, EnvironmentVariableTarget.User);
            _logger.LogInformation("Demo credentials saved to environment variables");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save demo credentials");
        }
    }

    protected override async Task LoadCredentialsAsync()
    {
        await Task.CompletedTask;
        _savedClientId = Environment.GetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_ID");
        _savedClientSecret = Environment.GetEnvironmentVariable("VYRALBOT_DEMO_CLIENT_SECRET");
        _logger.LogInformation("Demo credentials loaded from environment");
    }

    protected override string BuildAuthorizationUrl()
    {
        if (string.IsNullOrEmpty(_savedClientId))
            return "";
        
        return $"https://accounts.google.com/o/oauth2/v2/auth?client_id={_savedClientId}&redirect_uri=http://localhost:3000&response_type=code&scope=openid%20email%20profile";
    }

    protected override async Task<(string AccessToken, string RefreshToken, string Username)?> HandleOAuthCallbackAsync(string url)
    {
        await Task.CompletedTask;
        // Demo implementation - just return dummy values
        return ("demo_access_token", "demo_refresh_token", "DemoUser");
    }

    protected override async Task SendTestMessageAsync()
    {
        _logger.LogInformation("Sending test message to channel: {Channel}", ChannelName);
        
        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            TestMessageStatus = "❌ Please enter a channel name first";
            return;
        }

        if (string.IsNullOrEmpty(_accessToken))
        {
            TestMessageStatus = "❌ Please complete Step 3 first (OAuth authorization)";
            return;
        }
            
        TestMessageStatus = "⏳ Connecting to demo service...";
        
        try
        {
            await Task.Delay(1000);
            TestMessageStatus = $"✓ Test message sent to #{ChannelName}! (Demo mode)";
            _logger.LogInformation("Demo test message sent successfully");
        }
        catch (Exception ex)
        {
            TestMessageStatus = $"❌ Error: {ex.Message}";
            _logger.LogError(ex, "Failed to send test message");
        }
    }
}
