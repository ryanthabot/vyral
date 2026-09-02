using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Interface for managing chat event settings.
/// </summary>
public interface IChatEventSettingsService
{
    /// <summary>
    /// Gets the settings for a specific event type on a platform.
    /// </summary>
    Task<ChatEventSetting?> GetSettingAsync(string platform, string eventType);

    /// <summary>
    /// Gets all event settings, optionally filtered by platform.
    /// </summary>
    Task<IEnumerable<ChatEventSetting>> GetAllSettingsAsync(string? platform = null);

    /// <summary>
    /// Saves or updates event settings.
    /// </summary>
    Task SaveSettingAsync(ChatEventSetting setting);

    /// <summary>
    /// Deletes event settings by ID.
    /// </summary>
    Task DeleteSettingAsync(Guid id);

    /// <summary>
    /// Checks if an event should be shown in chat.
    /// </summary>
    Task<bool> IsEventEnabledAsync(string platform, string eventType);

    /// <summary>
    /// Checks if auto-reply is enabled for an event.
    /// </summary>
    Task<bool> IsAutoReplyEnabledAsync(string platform, string eventType);

    /// <summary>
    /// Gets the auto-reply message template for an event.
    /// </summary>
    Task<string?> GetAutoReplyMessageAsync(string platform, string eventType);

    /// <summary>
    /// Gets sound settings for an event.
    /// </summary>
    Task<(bool Enabled, string? SoundFile, int Volume)> GetSoundSettingsAsync(string platform, string eventType);

    /// <summary>
    /// Clears the settings cache, forcing reload on next access.
    /// </summary>
    void InvalidateCache();
}
