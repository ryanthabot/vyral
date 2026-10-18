using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.UI.Services;

public interface IAlertService
{
    /// <summary>
    /// Shows an alert overlay. When <paramref name="applySettings"/> is true (the default),
    /// the persisted alert settings are the source of truth: the master enable toggle and
    /// per-event toggles gate whether the alert shows at all, and the stored duration,
    /// animation, and position override the values passed by the caller.
    /// Pass false to show the alert exactly as specified (used by the settings page's Test Alert).
    /// </summary>
    Task ShowAlertAsync(
        string title,
        string username,
        string? message = null,
        string? imageUrl = null,
        Views.AlertAnimation animation = Views.AlertAnimation.SlideInFromTop,
        Views.AlertPosition position = Views.AlertPosition.TopCenter,
        int durationSeconds = 5,
        bool applySettings = true);

    Task HideCurrentAlertAsync();

    bool IsAlertVisible { get; }

    /// <summary>Loads the persisted alert settings; returns defaults when nothing valid is stored.</summary>
    Task<AlertSettings> GetAlertSettingsAsync();

    /// <summary>Persists the given alert settings under the Configs key "AlertSettings".</summary>
    Task SaveAlertSettingsAsync(AlertSettings settings);
}

/// <summary>
/// Visual-alert settings persisted as a JSON blob under the Configs key "AlertSettings"
/// in the per-profile database. Serialized with default (PascalCase) property names,
/// matching the AudioSettings blob convention.
/// </summary>
public class AlertSettings
{
    public bool AlertsEnabled { get; set; } = true;
    public int DurationSeconds { get; set; } = 5;
    public string Animation { get; set; } = nameof(Views.AlertAnimation.SlideInFromTop);
    public string Position { get; set; } = nameof(Views.AlertPosition.TopCenter);
    public bool FollowAlertsEnabled { get; set; } = true;
    public bool SubscribeAlertsEnabled { get; set; } = true;
    public bool GiftSubAlertsEnabled { get; set; } = true;
    public bool RaidAlertsEnabled { get; set; } = true;
    public bool HostAlertsEnabled { get; set; } = true;
    public bool CheerAlertsEnabled { get; set; } = true;
}

public class AlertService : IAlertService
{
    private const string SettingsConfigKey = "AlertSettings";

    // Bounded FIFO: at most 10 alerts wait while another is on screen; the
    // oldest is dropped when the cap is exceeded so the queue cannot grow.
    private const int MaxPendingAlerts = 10;

    // Matches the 1..15 range of the duration slider on the Alert Settings page.
    private const int MinDurationSeconds = 1;
    private const int MaxDurationSeconds = 15;

    /// <summary>
    /// Maps the alert titles passed by the ChatViewModel event handlers to the per-event
    /// toggles persisted in <see cref="AlertSettings"/>. Titles that are not listed here
    /// (e.g. "Kicks Gifted!", "Reward Redeemed!", "Test Alert!") always show when alerts
    /// are enabled — they have no dedicated toggle on the settings page.
    /// </summary>
    private static readonly Dictionary<string, string> TitleToEventMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["New Follower!"] = "follow",
        ["New Subscriber!"] = "subscribe",
        ["Resubscribed!"] = "subscribe",
        ["Gift Sub!"] = "giftsub",
        ["Gift Subs!"] = "giftsub",
        ["New Host!"] = "host",
        ["Raid!"] = "raid",
        ["Bits!"] = "cheer"
    };

    private readonly ILogger<AlertService> _logger;
    private readonly IDataService _dataService;
    private Views.AlertOverlayWindow? _currentAlert;
    private readonly object _lock = new();
    private readonly Queue<QueuedAlert> _pendingAlerts = new();
    private bool _isShowing;

    /// <summary>An alert request waiting in the FIFO queue while another alert is on screen.</summary>
    private sealed record QueuedAlert(
        string Title,
        string Username,
        string? Message,
        string? ImageUrl,
        Views.AlertAnimation Animation,
        Views.AlertPosition Position,
        int DurationSeconds);

    public bool IsAlertVisible => _currentAlert?.IsVisible ?? false;

    public AlertService(ILogger<AlertService> logger, IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;
    }

    public async Task ShowAlertAsync(
        string title,
        string username,
        string? message = null,
        string? imageUrl = null,
        Views.AlertAnimation animation = Views.AlertAnimation.SlideInFromTop,
        Views.AlertPosition position = Views.AlertPosition.TopCenter,
        int durationSeconds = 5,
        bool applySettings = true)
    {
        try
        {
            var effectiveAnimation = animation;
            var effectivePosition = position;
            var effectiveDuration = durationSeconds;

            if (applySettings)
            {
                var settings = await GetAlertSettingsAsync();

                if (!settings.AlertsEnabled)
                {
                    _logger.LogDebug("Alerts disabled in settings - skipping alert '{Title}'", title);
                    return;
                }

                if (!IsEventAlertEnabled(settings, title))
                {
                    _logger.LogDebug("Alerts for the event of '{Title}' disabled in settings - skipping", title);
                    return;
                }

                // Persisted settings are the source of truth for styling; the
                // hard-coded values callers pass are intentionally overridden.
                effectiveAnimation = ParseAnimation(settings.Animation);
                effectivePosition = ParsePosition(settings.Position);
                effectiveDuration = Math.Clamp(settings.DurationSeconds, MinDurationSeconds, MaxDurationSeconds);
            }

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    lock (_lock)
                    {
                        // FIFO queue: if an alert is currently on screen, enqueue this
                        // request instead of killing the visible alert (last-wins).
                        if (_isShowing)
                        {
                            var queued = new QueuedAlert(
                                title, username, message, imageUrl,
                                effectiveAnimation, effectivePosition, effectiveDuration);

                            // Drop-oldest so the queue stays bounded.
                            while (_pendingAlerts.Count >= MaxPendingAlerts)
                            {
                                _pendingAlerts.Dequeue();
                            }

                            _pendingAlerts.Enqueue(queued);
                            _logger.LogDebug("Alert queued ({Count} pending): {Title} - {Username}",
                                _pendingAlerts.Count, title, username);
                            return;
                        }

                        _isShowing = true;
                    }

                    await ShowCore(new QueuedAlert(
                        title, username, message, imageUrl,
                        effectiveAnimation, effectivePosition, effectiveDuration));
                    _logger.LogInformation("Alert shown: {Title} - {Username}", title, username);
                }
                catch (Exception ex)
                {
                    // Own try/catch: this body runs inside a Dispatcher async operation,
                    // so exceptions here would otherwise escape the surrounding catch.
                    _logger.LogError(ex, "Error showing alert: {Title} - {Username}", title, username);

                    lock (_lock)
                    {
                        _isShowing = false;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing alert: {Title} - {Username}", title, username);
        }
    }

    /// <summary>
    /// Creates and shows the overlay window for the given request.
    /// Must be called on the UI thread with <see cref="_isShowing"/> already true.
    /// </summary>
    private Task ShowCore(QueuedAlert request)
    {
        Views.AlertOverlayWindow alert;
        lock (_lock)
        {
            alert = new Views.AlertOverlayWindow();
            _currentAlert = alert;
        }

        // When the window closes, show the next queued alert (if any).
        alert.Closed += OnAlertWindowClosed;

        return alert.ShowAlertAsync(
            request.Title,
            request.Username,
            request.Message,
            request.ImageUrl,
            request.Animation,
            request.Position,
            request.DurationSeconds);
    }

    private async void OnAlertWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Views.AlertOverlayWindow closed)
        {
            closed.Closed -= OnAlertWindowClosed;
        }

        QueuedAlert? next = null;
        lock (_lock)
        {
            _currentAlert = null;

            if (_pendingAlerts.Count > 0)
            {
                next = _pendingAlerts.Dequeue();
            }
            else
            {
                _isShowing = false;
            }
        }

        if (next != null)
        {
            try
            {
                await ShowCore(next);
                _logger.LogInformation("Alert shown from queue: {Title} - {Username}", next.Title, next.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing queued alert: {Title} - {Username}", next.Title, next.Username);
                lock (_lock)
                {
                    _isShowing = false;
                }
            }
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
                    // The overlay fades out and closes itself; the Closed handler
                    // below clears _currentAlert and drains the pending queue.
                    await _currentAlert.HideAlertAsync();
                    _logger.LogInformation("Alert hidden");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hiding alert");
        }
    }

    public async Task<AlertSettings> GetAlertSettingsAsync()
    {
        try
        {
            var settingsJson = await _dataService.GetConfigValueAsync(SettingsConfigKey);
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                var settings = JsonSerializer.Deserialize<AlertSettings>(settingsJson);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading alert settings - using defaults");
        }

        return new AlertSettings();
    }

    public async Task SaveAlertSettingsAsync(AlertSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var settingsJson = JsonSerializer.Serialize(settings);
        await _dataService.SetConfigValueAsync(SettingsConfigKey, settingsJson);
        _logger.LogInformation("Alert settings saved: {Json}", settingsJson);
    }

    private static bool IsEventAlertEnabled(AlertSettings settings, string? title)
    {
        if (string.IsNullOrEmpty(title) || !TitleToEventMap.TryGetValue(title, out var eventType))
        {
            // Titles without a dedicated per-event toggle always show.
            return true;
        }

        return eventType switch
        {
            "follow" => settings.FollowAlertsEnabled,
            "subscribe" => settings.SubscribeAlertsEnabled,
            "giftsub" => settings.GiftSubAlertsEnabled,
            "raid" => settings.RaidAlertsEnabled,
            "host" => settings.HostAlertsEnabled,
            "cheer" => settings.CheerAlertsEnabled,
            _ => true
        };
    }

    private static Views.AlertAnimation ParseAnimation(string? name) =>
        Enum.TryParse<Views.AlertAnimation>(name, true, out var result)
            ? result
            : Views.AlertAnimation.SlideInFromTop;

    private static Views.AlertPosition ParsePosition(string? name) =>
        Enum.TryParse<Views.AlertPosition>(name, true, out var result)
            ? result
            : Views.AlertPosition.TopCenter;
}
