using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using VyralBot.Api.Services;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.UI.Views;

namespace VyralBot.UI.ViewModels;

public partial class WebServerSettingsViewModel : ViewModelBase
{
    private readonly ILogger<WebServerSettingsViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly WebhookCoordinatorService? _coordinatorService;
    private readonly NgrokService? _ngrokService;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _port = 5000;

    [ObservableProperty]
    private string _ngrokAuthToken = "";

    [ObservableProperty]
    private string _ngrokRegion = "us";

    [ObservableProperty]
    private bool _autoStartOnLaunch = false;

    [ObservableProperty]
    private string _status = "Stopped";

    [ObservableProperty]
    private string? _publicUrl;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasActiveDependencies;

    [ObservableProperty]
    private string _dependenciesText = "";

    [ObservableProperty]
    private bool _isSetupComplete;

    [ObservableProperty]
    private bool _showNgrokSetup;

    [ObservableProperty]
    private bool _showNgrokSettings;

    private readonly IServiceProvider? _serviceProvider;
    private bool _isInitializing = true;

    public WebServerSettingsViewModel(
        ILogger<WebServerSettingsViewModel> logger,
        IDataService dataService,
        WebhookCoordinatorService? coordinatorService = null,
        NgrokService? ngrokService = null,
        IServiceProvider? serviceProvider = null)
    {
        _logger = logger;
        _dataService = dataService;
        _coordinatorService = coordinatorService;
        _ngrokService = ngrokService;
        _serviceProvider = serviceProvider;

        if (_coordinatorService != null)
        {
            _coordinatorService.StatusChanged += OnStatusChanged;
            _coordinatorService.ErrorOccurred += OnErrorOccurred;
        }


    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing WebServer settings");

        try
        {
            var settings = await _dataService.GetWebServerSettingsAsync();

            IsEnabled = settings.IsEnabled;
            Port = settings.Port;
            NgrokRegion = settings.NgrokRegion;
            AutoStartOnLaunch = settings.AutoStartOnLaunch;
            PublicUrl = settings.CurrentTunnelUrl;

            // Load ngrok auth token from JSON file
            if (_ngrokService != null)
            {
                await _ngrokService.LoadAuthTokenAsync();
                NgrokAuthToken = _ngrokService.AuthToken ?? "";
            }
            else
            {
                NgrokAuthToken = "";
            }

            if (_coordinatorService != null)
            {
                IsRunning = _coordinatorService.IsRunning;
                Status = IsRunning ? $"Running: {_coordinatorService.PublicUrl}" : "Stopped";
                UpdateDependencies();
                
                // Notify commands to update button states
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
            else
            {
                Status = "Service not available";
            }

            // Check if setup is complete
            await CheckSetupStatusAsync();
            
            _isInitializing = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading WebServer settings");
            ErrorMessage = $"Error loading settings: {ex.Message}";
        }
    }

    private Task CheckSetupStatusAsync()
    {
        try
        {
            // Check if ngrok is installed and authenticated
            var isNgrokInstalled = IsNgrokInstalled();
            var hasAuthToken = !string.IsNullOrEmpty(NgrokAuthToken);

            IsSetupComplete = isNgrokInstalled && hasAuthToken;

            if (!IsSetupComplete)
            {
                _logger.LogInformation("Ngrok setup incomplete - Installed: {Installed}, Authenticated: {Auth}",
                    isNgrokInstalled, hasAuthToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking setup status");
            IsSetupComplete = false;
        }

        return Task.CompletedTask;
    }

    private static bool IsNgrokInstalled()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ngrok",
                Arguments = "version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task RunSetupWizardAsync()
    {
        _logger.LogInformation("RunSetupWizard button clicked - opening Ngrok Setup Wizard");
        
        await Task.CompletedTask;
        
        // Navigate to Ngrok Setup Wizard via MainViewModel
        if (_serviceProvider != null)
        {
            var mainViewModel = _serviceProvider.GetService<MainViewModel>();
            if (mainViewModel != null)
            {
                mainViewModel.NavigateToNgrokSetupWizardCommand.Execute(null);
            }
            else
            {
                _logger.LogWarning("MainViewModel not found in service provider");
            }
        }
        else
        {
            _logger.LogWarning("ServiceProvider is null, cannot navigate to Ngrok Setup");
        }
    }

    [RelayCommand]
    private void OpenNgrokConfig()
    {
        _logger.LogInformation("Opening ngrok configuration");
        ShowNgrokSettings = !ShowNgrokSettings;
        ShowNgrokSetup = false;
    }

    [RelayCommand]
    private void CloseNgrokSettings()
    {
        ShowNgrokSettings = false;
        ShowNgrokSetup = false;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_coordinatorService == null) return;

        // Validate setup is complete before starting
        if (!IsSetupComplete)
        {
            ErrorMessage = "Please complete ngrok setup before starting the webhook server. Click 'RUN SETUP' to configure ngrok.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            _logger.LogInformation("Starting webhook infrastructure");

            // Save settings first
            await SaveSettingsAsync();

            var success = await _coordinatorService.StartAsync();

            if (success)
            {
                IsRunning = true;
                PublicUrl = _coordinatorService.PublicUrl;
                Status = $"Running: {PublicUrl}";
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
            else
            {
                ErrorMessage = "Failed to start webhook infrastructure";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting webhook infrastructure");
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStart() => !IsRunning && !IsBusy && _coordinatorService != null;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_coordinatorService == null) return;

        // Check for dependencies
        if (!_coordinatorService.CanDisableWebServer())
        {
            ErrorMessage = "Cannot stop: Active features depend on the web server. Disable dependent features first.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            _logger.LogInformation("Stopping webhook infrastructure");

            await _coordinatorService.StopAsync();

            IsRunning = false;
            PublicUrl = null;
            Status = "Stopped";
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping webhook infrastructure");
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStop() => IsRunning && !IsBusy && _coordinatorService != null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await SaveSettingsAsync();
            _logger.LogInformation("WebServer settings saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving WebServer settings");
            ErrorMessage = $"Error saving: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestNgrokInstallationAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            _logger.LogInformation("Testing ngrok installation");

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ngrok",
                Arguments = "version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    var version = await process.StandardOutput.ReadToEndAsync();
                    Status = $"✓ Ngrok installed: {version.Trim()}";
                }
                else
                {
                    ErrorMessage = "Ngrok not found. Please install ngrok from https://ngrok.com/download";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing ngrok installation");
            ErrorMessage = "Ngrok not found. Please install ngrok from https://ngrok.com/download";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyPublicUrl()
    {
        if (!string.IsNullOrEmpty(PublicUrl))
        {
            try
            {
                System.Windows.Clipboard.SetText(PublicUrl);
                Status = "✓ URL copied to clipboard";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying URL to clipboard");
                ErrorMessage = "Failed to copy URL";
            }
        }
    }

    [RelayCommand]
    private Task OpenPublicUrlAsync()
    {
        if (!string.IsNullOrEmpty(PublicUrl))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"{PublicUrl}/webhooks/health",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening URL");
                ErrorMessage = "Failed to open URL";
            }
        }

        return Task.CompletedTask;
    }

    private async Task SaveSettingsAsync()
    {
        var settings = new WebServerSettings
        {
            Id = 1,
            IsEnabled = IsEnabled,
            Port = Port,
            NgrokRegion = NgrokRegion,
            AutoStartOnLaunch = AutoStartOnLaunch,
            CurrentTunnelUrl = PublicUrl
        };

        _logger.LogInformation("Saving WebServer settings - AutoStartOnLaunch: {AutoStart}, IsEnabled: {IsEnabled}, Port: {Port}", 
            AutoStartOnLaunch, IsEnabled, Port);
        await _dataService.SaveWebServerSettingsAsync(settings);
        _logger.LogInformation("WebServer settings saved successfully");
    }

    private void UpdateDependencies()
    {
        if (_coordinatorService == null) return;

        var dependencies = _coordinatorService.GetActiveDependencies();
        HasActiveDependencies = dependencies.Count > 0;
        DependenciesText = HasActiveDependencies
            ? $"Active dependencies:\n• {string.Join("\n• ", dependencies)}"
            : "No active dependencies";
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Status = status;
        IsRunning = _coordinatorService?.IsRunning ?? false;
        PublicUrl = _coordinatorService?.PublicUrl;
        UpdateDependencies();
        
        // Notify commands to update button states
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        ErrorMessage = error;
    }

    partial void OnAutoStartOnLaunchChanged(bool value)
    {
        // Do not persist changes made during initialization
        if (_isInitializing)
        {
            return;
        }

        // Persist the new setting when changed by the user
        _ = SaveSettingsAsync();

        // Only auto-start when the toggle was changed manually and the infrastructure isn't running
        if (value && !IsRunning)
        {
            _logger.LogInformation("AutoStartOnLaunch enabled - starting webhook infrastructure immediately");
            _ = StartAsync();
        }
    }
}

