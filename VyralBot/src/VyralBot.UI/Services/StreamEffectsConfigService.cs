using VyralBot.Data;

namespace VyralBot.UI.Services;

/// <summary>
/// Point-in-time Stream FX settings read from the config KV. Re-read per call
/// so settings changes apply without a restart (same pattern as LightsConfig).
/// </summary>
public sealed record StreamEffectsSnapshot(
    string Provider,
    string? BridgeIp,
    string? AppKey,
    IReadOnlyList<string> LightIds,
    string WebhookUrl,
    string? WebhookToken,
    int FlashTimes,
    int FlashIntervalMs,
    string FlashColor,
    string DeckActionsJson);

/// <summary>
/// Wraps the IDataService config KV for the stream-effect feature (lights
/// providers, flash defaults, Stream Deck actions). Reads are by key only
/// (GetConfigValueAsync has no category parameter); the "StreamEffects"
/// category is stored alongside each write so the Settings page can enumerate
/// the feature's keys via GetConfigsByCategoryAsync.
/// </summary>
public sealed class StreamEffectsConfigService
{
    public const string Category = "StreamEffects";

    public const string LightsProviderKey = "Lights.Provider";
    public const string HueBridgeIpKey = "Lights.Hue.BridgeIp";
    public const string HueAppKeyKey = "Lights.Hue.AppKey";
    public const string HueLightIdsKey = "Lights.Hue.LightIds";
    public const string WebhookUrlKey = "Lights.Webhook.Url";
    public const string WebhookTokenKey = "Lights.Webhook.Token";
    public const string FlashTimesKey = "Lights.Flash.Times";
    public const string FlashIntervalMsKey = "Lights.Flash.IntervalMs";
    public const string FlashColorKey = "Lights.Flash.Color";
    public const string DeckActionsKey = "StreamDeck.Actions";

    public const string DefaultProvider = "None";
    public const int DefaultFlashTimes = 5;
    public const int DefaultFlashIntervalMs = 250;
    public const string DefaultFlashColor = "#FF00AA";

    private readonly IDataService _data;

    public StreamEffectsConfigService(IDataService data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>Raw config read; empty/missing values fall back to <paramref name="fallback"/>.</summary>
    public async Task<string> GetAsync(string key, string fallback = "")
    {
        var value = await _data.GetConfigValueAsync(key).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>Writes one config value under the StreamEffects category.</summary>
    public Task SetAsync(string key, string value) =>
        _data.SetConfigValueAsync(key, value, Category);

    /// <summary>
    /// Reads all Stream FX settings with defaults applied (unparseable ints
    /// fall back to their defaults, LightIds are a trimmed CSV).
    /// </summary>
    public async Task<StreamEffectsSnapshot> GetSnapshotAsync()
    {
        var provider = await GetAsync(LightsProviderKey, DefaultProvider).ConfigureAwait(false);
        var bridgeIp = NullIfEmpty(await GetAsync(HueBridgeIpKey).ConfigureAwait(false));
        var appKey = NullIfEmpty(await GetAsync(HueAppKeyKey).ConfigureAwait(false));
        var lightIds = ParseLightIds(await GetAsync(HueLightIdsKey).ConfigureAwait(false));
        var webhookUrl = await GetAsync(WebhookUrlKey).ConfigureAwait(false);
        var webhookToken = NullIfEmpty(await GetAsync(WebhookTokenKey).ConfigureAwait(false));
        var flashTimes = ParseInt(await GetAsync(FlashTimesKey).ConfigureAwait(false), DefaultFlashTimes);
        var flashIntervalMs = ParseInt(await GetAsync(FlashIntervalMsKey).ConfigureAwait(false), DefaultFlashIntervalMs);
        var flashColor = await GetAsync(FlashColorKey, DefaultFlashColor).ConfigureAwait(false);
        var deckActionsJson = await GetAsync(DeckActionsKey, "[]").ConfigureAwait(false);

        return new StreamEffectsSnapshot(
            provider, bridgeIp, appKey, lightIds, webhookUrl, webhookToken,
            flashTimes, flashIntervalMs, flashColor, deckActionsJson);
    }

    /// <summary>Convenience writer for the Settings page (all keys in one call).</summary>
    public async Task SaveSnapshotCoreSettingsAsync(StreamEffectsSnapshot s)
    {
        await SetAsync(LightsProviderKey, s.Provider).ConfigureAwait(false);
        await SetAsync(HueBridgeIpKey, s.BridgeIp ?? string.Empty).ConfigureAwait(false);
        await SetAsync(HueAppKeyKey, s.AppKey ?? string.Empty).ConfigureAwait(false);
        await SetAsync(HueLightIdsKey, string.Join(",", s.LightIds)).ConfigureAwait(false);
        await SetAsync(WebhookUrlKey, s.WebhookUrl).ConfigureAwait(false);
        await SetAsync(WebhookTokenKey, s.WebhookToken ?? string.Empty).ConfigureAwait(false);
        await SetAsync(FlashTimesKey, s.FlashTimes.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await SetAsync(FlashIntervalMsKey, s.FlashIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
        await SetAsync(FlashColorKey, s.FlashColor).ConfigureAwait(false);
        await SetAsync(DeckActionsKey, s.DeckActionsJson).ConfigureAwait(false);
    }

    /// <summary>CSV split with trim, dropping empties ("A, B,,C" → A,B,C).</summary>
    private static IReadOnlyList<string> ParseLightIds(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
