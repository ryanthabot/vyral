using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace VyralBot.UI.Services;

public interface IAlertService
{
    Task ShowAlertAsync(
        string title,
        string username,
        string? message = null,
        string? imageUrl = null,
        Views.AlertAnimation animation = Views.AlertAnimation.SlideInFromTop,
        Views.AlertPosition position = Views.AlertPosition.TopCenter,
        int durationSeconds = 5);
    
    Task HideCurrentAlertAsync();
    
    bool IsAlertVisible { get; }
}

public class AlertService : IAlertService
{
    private readonly ILogger<AlertService> _logger;
    private Views.AlertOverlayWindow? _currentAlert;
    private readonly object _lock = new();

    public bool IsAlertVisible => _currentAlert?.IsVisible ?? false;

    public AlertService(ILogger<AlertService> logger)
    {
        _logger = logger;
    }

    public async Task ShowAlertAsync(
        string title,
        string username,
        string? message = null,
        string? imageUrl = null,
        Views.AlertAnimation animation = Views.AlertAnimation.SlideInFromTop,
        Views.AlertPosition position = Views.AlertPosition.TopCenter,
        int durationSeconds = 5)
    {
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                lock (_lock)
                {
                    // Hide current alert if one is showing
                    if (_currentAlert?.IsVisible == true)
                    {
                        _currentAlert.Hide();
                        _currentAlert.Close();
                        _currentAlert = null;
                    }

                    // Create new alert window
                    _currentAlert = new Views.AlertOverlayWindow();
                }

                if (_currentAlert != null)
                {
                    await _currentAlert.ShowAlertAsync(title, username, message, imageUrl, animation, position, durationSeconds);
                    _logger.LogInformation("Alert shown: {Title} - {Username}", title, username);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing alert: {Title} - {Username}", title, username);
        }
    }

    public async Task HideCurrentAlertAsync()
    {
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (_currentAlert?.IsVisible == true)
                {
                    await _currentAlert.HideAlertAsync();
                    _currentAlert.Close();
                    _currentAlert = null;
                    _logger.LogInformation("Alert hidden");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hiding alert");
        }
    }
}
