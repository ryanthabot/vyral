namespace VyralBot.Integrations.Lights;

/// <summary>Point-in-time lights settings; captured per call so settings changes apply without a restart.</summary>
public sealed record LightsConfig(
    string Provider,
    string? HueBridgeIp = null,
    string? HueAppKey = null,
    IReadOnlyList<string>? HueLightIds = null,
    string? WebhookUrl = null,
    string? WebhookToken = null);

/// <summary>
/// Facade that routes ILightsClient calls to the configured provider
/// ("Hue" | "Webhook" | "None"). Config is re-read per call so settings
/// changes apply without a restart.
/// </summary>
public sealed class LightsManager(Func<Task<LightsConfig>> config, HttpMessageHandler? handler = null) : ILightsClient
{
    public const string NotConfiguredMessage =
        "⚠️ Lights are not configured (pick a provider in Settings → Stream FX).";

    /// <summary>Flash debounce: one flash loop at a time. Rapid deck/chat
    /// triggers that arrive while a flash runs are dropped (not queued) so
    /// concurrent loops can never fight over light state or flood the bridge.</summary>
    private readonly SemaphoreSlim _flashLock = new(1, 1);

    public async Task<string> FlashAsync(string? hexColor, int times, int intervalMs, CancellationToken ct = default)
    {
        if (!await _flashLock.WaitAsync(0).ConfigureAwait(false))
        {
            return "⚠️ A light effect is already running — skipped.";
        }

        try
        {
            var client = await CreateClientAsync().ConfigureAwait(false);
            return client is null
                ? NotConfiguredMessage
                : await client.FlashAsync(hexColor, times, intervalMs, ct).ConfigureAwait(false);
        }
        finally
        {
            _flashLock.Release();
        }
    }

    public async Task<string> SetPowerAsync(bool on, CancellationToken ct = default)
    {
        var client = await CreateClientAsync().ConfigureAwait(false);
        return client is null ? NotConfiguredMessage : await client.SetPowerAsync(on, ct).ConfigureAwait(false);
    }

    public async Task<List<LightInfo>> GetLightsAsync(CancellationToken ct = default)
    {
        var client = await CreateClientAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException(NotConfiguredMessage);
        return await client.GetLightsAsync(ct).ConfigureAwait(false);
    }

    private async Task<ILightsClient?> CreateClientAsync()
    {
        var snapshot = await config().ConfigureAwait(false);
        switch ((snapshot.Provider ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "HUE":
                return new HueLightsClient(
                    () => Task.FromResult((snapshot.HueBridgeIp, snapshot.HueAppKey, snapshot.HueLightIds ?? [])),
                    handler);
            case "WEBHOOK":
                return new WebhookLightsClient(
                    () => Task.FromResult((snapshot.WebhookUrl ?? string.Empty, snapshot.WebhookToken)),
                    handler);
            default:
                return null;
        }
    }
}
