using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VyralBot.Integrations.Lights;

/// <summary>
/// Triggers light effects through a user-supplied HTTP webhook
/// (for example Home Assistant or a self-hosted bridge script).
/// </summary>
public sealed class WebhookLightsClient(
    Func<Task<(string Url, string? Token)>> config,
    HttpMessageHandler? handler = null) : ILightsClient
{
    private const string UnconfiguredMessage = "⚠️ Lights webhook is not configured (set it in Settings → Stream FX).";
    // Fail fast against an unreachable endpoint (default HttpClient timeout is 100s).
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = handler is null
        ? SharedHttp
        : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<string> FlashAsync(string? hexColor, int times, int intervalMs, CancellationToken ct = default)
    {
        var (url, token) = await config().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(url)) return UnconfiguredMessage;
        try
        {
            await PostAsync(url, token, new Dictionary<string, object?>
            {
                ["action"] = "flash",
                ["color"] = string.IsNullOrWhiteSpace(hexColor) ? null : hexColor,
                ["times"] = times > 0 ? times : null,
                ["intervalMs"] = intervalMs > 0 ? intervalMs : null,
            }, ct).ConfigureAwait(false);
            return "Webhook light flash triggered.";
        }
        catch (Exception ex)
        {
            return $"❌ Webhook light flash failed: {ex.Message}";
        }
    }

    public async Task<string> SetPowerAsync(bool on, CancellationToken ct = default)
    {
        var (url, token) = await config().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(url)) return UnconfiguredMessage;
        try
        {
            await PostAsync(url, token, new Dictionary<string, object?> { ["action"] = on ? "on" : "off" }, ct)
                .ConfigureAwait(false);
            return on ? "💡 Lights on." : "💡 Lights off.";
        }
        catch (Exception ex)
        {
            return $"❌ Webhook light switch failed: {ex.Message}";
        }
    }

    public Task<List<LightInfo>> GetLightsAsync(CancellationToken ct = default)
    {
        // Generic webhooks have no discovery endpoint; nothing sensible to list.
        return Task.FromResult(new List<LightInfo>());
    }

    private async Task PostAsync(string url, string? token, Dictionary<string, object?> payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
