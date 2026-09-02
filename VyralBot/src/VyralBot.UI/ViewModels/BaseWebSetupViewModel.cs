using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Threading.Tasks;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Base class for all web-based setup wizards (Twitch, Kick, Ngrok, etc.)
/// Carbon copy of the Twitch/Kick setup wizard pattern
/// </summary>
public abstract partial class BaseWebSetupViewModel : ViewModelBase
{
    protected readonly ILogger _logger;

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
    private string _modalTitle = "";

    [ObservableProperty]
    private string _modalDescription = "";

    [ObservableProperty]
    private string _modalInput = "";

    protected Action<string>? _modalSaveAction;

    protected BaseWebSetupViewModel(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Navigate to a specific step
    /// </summary>
    [RelayCommand]
    public void GoToStep(int step)
    {
        _logger.LogInformation("Going to step {Step}", step);
        CurrentStep = step;
        ShowSuccessScreen = false;
    }

    /// <summary>
    /// Go back one step
    /// </summary>
    [RelayCommand]
    public void GoBack()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            ShowBrowser = false;
            _logger.LogInformation("Going back to step {Step}", CurrentStep);
        }
    }

    /// <summary>
    /// Open modal dialog for input
    /// </summary>
    protected void OpenModal(string title, string description, Action<string> saveAction)
    {
        ModalTitle = title;
        ModalDescription = description;
        ModalInput = "";
        _modalSaveAction = saveAction;
        ShowModal = true;
        _logger.LogInformation("Opening modal: {Title}", title);
    }

    /// <summary>
    /// Close modal dialog
    /// </summary>
    [RelayCommand]
    public void CloseModal()
    {
        ShowModal = false;
        ModalInput = "";
        _modalSaveAction = null;
        _logger.LogInformation("Modal closed");
    }

    /// <summary>
    /// Save modal input and execute the save action
    /// </summary>
    [RelayCommand]
    public void SaveModal()
    {
        if (!string.IsNullOrWhiteSpace(ModalInput))
        {
            _modalSaveAction?.Invoke(ModalInput);
            ShowModal = false;
            ModalInput = "";
            _logger.LogInformation("Modal input saved");
        }
    }

    /// <summary>
    /// Open browser to a URL and show WebView2
    /// </summary>
    protected async Task OpenBrowserAsync(string url)
    {
        _logger.LogInformation("Opening browser to: {Url}", url);
        try
        {
            // Request WebView2 initialization
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            
            // Wait for initialization
            await Task.Delay(500);
            
            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate(url);
                _logger.LogInformation("WebView2 navigated successfully");
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

    /// <summary>
    /// Hide the browser
    /// </summary>
    [RelayCommand]
    public void CloseBrowser()
    {
        ShowBrowser = false;
        _logger.LogInformation("Browser closed");
    }

    /// <summary>
    /// Dispose WebView2 to free resources
    /// </summary>
    protected void DisposeWebView2()
    {
        _logger.LogInformation("Requesting WebView2 disposal");
        WebView2DisposeRequested?.Invoke(this, EventArgs.Empty);
        WebView2 = null;
    }

    /// <summary>
    /// Finish setup and notify parent
    /// </summary>
    [RelayCommand]
    public virtual void FinishSetup()
    {
        _logger.LogInformation("Finishing setup wizard");
        
        // Dispose WebView2 to free resources
        DisposeWebView2();
        
        // Notify parent
        SetupCompleted?.Invoke(this, EventArgs.Empty);
        
        // Reset wizard state
        ResetWizard();
    }

    /// <summary>
    /// Cancel setup and notify parent
    /// </summary>
    [RelayCommand]
    public virtual void CancelSetup()
    {
        _logger.LogInformation("Cancelling setup wizard");
        
        // Dispose WebView2
        DisposeWebView2();
        
        // Reset state
        ResetWizard();
        
        // Notify parent
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reset wizard to initial state
    /// </summary>
    protected virtual void ResetWizard()
    {
        ShowSuccessScreen = false;
        CurrentStep = 1;
        ShowBrowser = false;
        ShowModal = false;
        ModalInput = "";
        _modalSaveAction = null;
    }

    /// <summary>
    /// Complete the wizard and show success screen
    /// </summary>
    [RelayCommand]
    public virtual void CompleteSetup()
    {
        _logger.LogInformation("Setup completed successfully");
        ShowBrowser = false;
        ShowSuccessScreen = true;
    }
}
