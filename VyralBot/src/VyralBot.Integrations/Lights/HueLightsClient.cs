using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VyralBot.Integrations.Lights;

/// <summary>
/// Controls Philips Hue lights over the local bridge API (no SDK).
/// Bridge credentials are re-read through <c>config</c> on every call so
/// settings changes apply without a restart.
/// </summary>
public sealed class HueLightsClient(
    Func<Task<(string? BridgeIp, string? AppKey, IReadOnlyList<string> LightIds)>> config,
    HttpMessageHandler? handler = null,
    Func<int>? defaultFlashTimes = null) : ILightsClient
{
    private const string UnconfiguredMessage = "⚠️ Hue bridge is not configured (set it in Settings → Stream FX).";
    private static readonly HttpClient SharedHttp = new();
    private readonly HttpClient _http = handler is null ? SharedHttp : new HttpClient(handler);

    public async Task<string> FlashAsync(string? hexColor, int times, int intervalMs, CancellationToken ct = default)
    {
        var (bridgeIp, appKey, lightIds) = await config().ConfigureAwait(false);
        if (!IsConfigured(bridgeIp, appKey)) return UnconfiguredMessage;
        try
        {
            // Validate before touching the network so bad colors never flash halfway.
            double[]? xy = hexColor is null ? null : RgbToXy(hexColor);

            var targets = await ResolveTargetsAsync(bridgeIp!, appKey!, lightIds, ct).ConfigureAwait(false);
            if (targets.Count == 0) return "⚠️ No reachable lights found.";

            times = Math.Clamp(times <= 0 ? (defaultFlashTimes ?? (() => 5))() : times, 1, 20);
            intervalMs = Math.Clamp(intervalMs < 50 ? 250 : intervalMs, 50, 5000);

            // Snapshot each target's raw state so the original look can be restored.
            var originals = new List<(string Id, JsonElement State)>();
            foreach (var id in targets)
                originals.Add((id, await GetStateAsync(bridgeIp!, appKey!, id, ct).ConfigureAwait(false)));

            for (var cycle = 0; cycle < times; cycle++)
            {
                var body = FlashBody(xy);
                foreach (var id in targets)
                    await PutStateAsync(bridgeIp!, appKey!, id, body, ct).ConfigureAwait(false);
                if (cycle < times - 1)
                    await Task.Delay(intervalMs, ct).ConfigureAwait(false);
            }

            // Best-effort per light: one failed restore must not leave the
            // remaining lights stuck flashing.
            var restoreFailures = new List<string>();
            foreach (var (id, state) in originals)
            {
                try
                {
                    await PutStateAsync(bridgeIp!, appKey!, id, RestoreBody(state), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    restoreFailures.Add($"{id}: {ex.Message}");
                }
            }

            var message = $"💡 Flashed {targets.Count} light(s) {times}x.";
            return restoreFailures.Count > 0
                ? $"{message} ({restoreFailures.Count} restore(s) failed)"
                : message;
        }
        catch (FormatException)
        {
            return $"❌ Invalid color '{hexColor}'. Use #RRGGBB (for example #FF00AA).";
        }
        catch (Exception ex)
        {
            return $"❌ Hue flash failed: {ex.Message}";
        }
    }

    public async Task<string> SetPowerAsync(bool on, CancellationToken ct = default)
    {
        var (bridgeIp, appKey, lightIds) = await config().ConfigureAwait(false);
        if (!IsConfigured(bridgeIp, appKey)) return UnconfiguredMessage;
        try
        {
            var targets = await ResolveTargetsAsync(bridgeIp!, appKey!, lightIds, ct).ConfigureAwait(false);
            if (targets.Count == 0) return "⚠️ No reachable lights found.";

            var body = on ? "{\"on\":true}" : "{\"on\":false}";
            foreach (var id in targets)
                await PutStateAsync(bridgeIp!, appKey!, id, body, ct).ConfigureAwait(false);
            return on ? "💡 Lights on." : "💡 Lights off.";
        }
        catch (Exception ex)
        {
            return $"❌ Hue power switch failed: {ex.Message}";
        }
    }

    public async Task<List<LightInfo>> GetLightsAsync(CancellationToken ct = default)
    {
        var (bridgeIp, appKey, _) = await config().ConfigureAwait(false);
        if (!IsConfigured(bridgeIp, appKey))
            throw new InvalidOperationException(UnconfiguredMessage);
        try
        {
            return await GetLightsCoreAsync(bridgeIp!, appKey!, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"❌ Could not load Hue lights: {ex.Message}", ex);
        }
    }

    private static bool IsConfigured(string? bridgeIp, string? appKey) =>
        !string.IsNullOrWhiteSpace(bridgeIp) && !string.IsNullOrWhiteSpace(appKey);

    /// <summary>Configured ids win; otherwise fall back to every reachable light on the bridge.</summary>
    private async Task<List<string>> ResolveTargetsAsync(
        string bridgeIp, string appKey, IReadOnlyList<string> lightIds, CancellationToken ct)
    {
        if (lightIds.Count > 0) return [.. lightIds];
        var lights = await GetLightsCoreAsync(bridgeIp, appKey, ct).ConfigureAwait(false);
        return [.. lights.Where(l => l.Reachable).Select(l => l.Id)];
    }

    private async Task<List<LightInfo>> GetLightsCoreAsync(string bridgeIp, string appKey, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"http://{bridgeIp}/api/{appKey}/lights", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var lights = new List<LightInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return lights;
        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            var name = entry.Value.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() : null;
            var on = false;
            var reachable = true;
            if (entry.Value.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object)
            {
                on = state.TryGetProperty("on", out var onEl) && onEl.ValueKind == JsonValueKind.True;
                reachable = !state.TryGetProperty("reachable", out var reachEl) || reachEl.ValueKind != JsonValueKind.False;
            }
            var product = entry.Value.TryGetProperty("productname", out var productEl) && productEl.ValueKind == JsonValueKind.String
                ? productEl.GetString() : null;
            lights.Add(new LightInfo(entry.Name, name ?? entry.Name, on, reachable, product));
        }
        return lights;
    }

    private async Task<JsonElement> GetStateAsync(string bridgeIp, string appKey, string id, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"http://{bridgeIp}/api/{appKey}/lights/{id}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("state", out var state) ? state.Clone() : default;
    }

    private async Task PutStateAsync(string bridgeIp, string appKey, string id, string body, CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PutAsync($"http://{bridgeIp}/api/{appKey}/lights/{id}/state", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string FlashBody(double[]? xy) =>
        xy is null
            ? "{\"alert\":\"select\"}"
            : $"{{\"on\":true,\"xy\":[{Format(xy[0])},{Format(xy[1])}],\"alert\":\"select\"}}";

    private static string RestoreBody(JsonElement state)
    {
        var on = state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("on", out var onEl)
            && onEl.ValueKind == JsonValueKind.True;
        if (state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("xy", out var xy)
            && xy.ValueKind == JsonValueKind.Array
            && xy.GetArrayLength() == 2)
        {
            return $"{{\"on\":{(on ? "true" : "false")},\"xy\":[{Format(xy[0].GetDouble())},{Format(xy[1].GetDouble())}],\"alert\":\"none\"}}";
        }
        return $"{{\"on\":{(on ? "true" : "false")},\"alert\":\"none\"}}";
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Converts a #RRGGBB color to Hue CIE xy chromaticity. Throws FormatException on bad input.</summary>
    internal static double[] RgbToXy(string hexColor)
    {
        var hex = hexColor.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        byte r, g, b;
        try
        {
            if (hex.Length != 6) throw new FormatException();
            r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            throw new FormatException($"'{hexColor}' is not a #RRGGBB color.");
        }

        // sRGB -> linear (standard gamma).
        double lr = r / 255.0, lg = g / 255.0, lb = b / 255.0;
        lr = lr > 0.04045 ? Math.Pow((lr + 0.055) / 1.055, 2.4) : lr / 12.92;
        lg = lg > 0.04045 ? Math.Pow((lg + 0.055) / 1.055, 2.4) : lg / 12.92;
        lb = lb > 0.04045 ? Math.Pow((lb + 0.055) / 1.055, 2.4) : lb / 12.92;

        var x = lr * 0.664511 + lg * 0.154324 + lb * 0.162028;
        var y = lr * 0.283881 + lg * 0.668433 + lb * 0.047685;
        var z = lr * 0.000088 + lg * 0.072310 + lb * 0.986039;

        var sum = x + y + z;
        return sum <= 0 ? [0.3, 0.3] : [x / sum, y / sum];
    }
}
