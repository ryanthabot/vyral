using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Service for managing chat event settings with caching.
/// </summary>
public class ChatEventSettingsService : IChatEventSettingsService
{
    private readonly IDataService _dataService;
    private readonly ILogger<ChatEventSettingsService> _logger;
    private readonly Dictionary<string, ChatEventSetting> _settingsCache;
    private readonly SemaphoreSlim _cacheLock;
    private bool _isCacheLoaded;

    public ChatEventSettingsService(IDataService dataService, ILogger<ChatEventSettingsService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsCache = new Dictionary<string, ChatEventSetting>();
        _cacheLock = new SemaphoreSlim(1, 1);
        _isCacheLoaded = false;
    }

    private string GetCacheKey(string platform, string eventType)
    {
        return $"{platform}:{eventType}";
    }

    private async Task EnsureCacheLoadedAsync()
    {
        if (_isCacheLoaded)
            return;

        await _cacheLock.WaitAsync();
        try
        {
            if (_isCacheLoaded)
                return;

            var allSettings = await _dataService.GetAllChatEventSettingsAsync();
            _settingsCache.Clear();

            foreach (var setting in allSettings)
            {
                var key = GetCacheKey(setting.Platform, setting.EventType);
                _settingsCache[key] = setting;
            }

            _isCacheLoaded = true;
            _logger.LogInformation("Loaded {Count} chat event settings into cache", _settingsCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load chat event settings cache");
            throw;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<ChatEventSetting?> GetSettingAsync(string platform, string eventType)
    {
        await EnsureCacheLoadedAsync();

        var key = GetCacheKey(platform, eventType);
        
        await _cacheLock.WaitAsync();
        try
        {
            return _settingsCache.TryGetValue(key, out var setting) ? setting : null;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<IEnumerable<ChatEventSetting>> GetAllSettingsAsync(string? platform = null)
    {
        await EnsureCacheLoadedAsync();

        await _cacheLock.WaitAsync();
        try
        {
            if (string.IsNullOrEmpty(platform))
            {
                return _settingsCache.Values.ToList();
            }

            return _settingsCache.Values
                .Where(s => s.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task SaveSettingAsync(ChatEventSetting setting)
    {
        if (setting == null)
            throw new ArgumentNullException(nameof(setting));

        await _dataService.SaveChatEventSettingAsync(setting);

        var key = GetCacheKey(setting.Platform, setting.EventType);
        
        await _cacheLock.WaitAsync();
        try
        {
            _settingsCache[key] = setting;
            _logger.LogInformation("Saved chat event setting: {Platform}/{EventType}", setting.Platform, setting.EventType);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task DeleteSettingAsync(Guid id)
    {
        await _dataService.DeleteChatEventSettingAsync(id);

        await _cacheLock.WaitAsync();
        try
        {
            var keyToRemove = _settingsCache.FirstOrDefault(kvp => kvp.Value.Id == id).Key;
            if (keyToRemove != null)
            {
                _settingsCache.Remove(keyToRemove);
                _logger.LogInformation("Deleted chat event setting: {Id}", id);
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<bool> IsEventEnabledAsync(string platform, string eventType)
    {
        var setting = await GetSettingAsync(platform, eventType);
        return setting?.ShowInChat ?? false;
    }

    public async Task<bool> IsAutoReplyEnabledAsync(string platform, string eventType)
    {
        var setting = await GetSettingAsync(platform, eventType);
        return setting?.AutoReplyEnabled ?? false;
    }

    public async Task<string?> GetAutoReplyMessageAsync(string platform, string eventType)
    {
        var setting = await GetSettingAsync(platform, eventType);
        return setting?.AutoReplyMessage;
    }

    public async Task<(bool Enabled, string? SoundFile, int Volume)> GetSoundSettingsAsync(string platform, string eventType)
    {
        var setting = await GetSettingAsync(platform, eventType);
        return (
            setting?.SoundEnabled ?? false,
            setting?.SoundFile,
            setting?.SoundVolume ?? 50
        );
    }

    public void InvalidateCache()
    {
        _cacheLock.Wait();
        try
        {
            _settingsCache.Clear();
            _isCacheLoaded = false;
            _logger.LogInformation("Chat event settings cache invalidated");
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
