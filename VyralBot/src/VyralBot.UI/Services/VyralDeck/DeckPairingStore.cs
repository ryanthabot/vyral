using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.UI.Services.VyralDeck;

/// <summary>Server settings persisted in the profile config KV.</summary>
public sealed class DeckServerSettings
{
    public int Port { get; set; } = 5199;

    public bool AutoStartOnLaunch { get; set; }
}

/// <summary>One phone (or other client) that completed PIN pairing.</summary>
public sealed class PairedDevice
{
    /// <summary>Random 256-bit hex token — the client's credential. Never logged.</summary>
    public string Token { get; set; } = "";

    public string Name { get; set; } = "Phone";

    public DateTime PairedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Runtime-only: does this device currently hold a WebSocket
    /// session? Set by the StreamDeck page from <see cref="DeckServer"/>;
    /// never persisted.</summary>
    [JsonIgnore]
    public bool IsConnected { get; set; }
}

/// <summary>
/// Persisted paired devices + in-memory pairing PIN for the VyralDeck LAN
/// server. PINs live only in memory (they are short-lived by design); device
/// tokens live in the profile config KV so phones stay paired across restarts.
/// </summary>
public sealed class DeckPairingStore
{
    private const string DevicesKey = "VyralDeck.Devices";
    private const string SettingsKey = "VyralDeck.ServerSettings";
    private const int PinLifetimeMinutes = 10;
    private const int MaxFailuresPerMinute = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IDataService _data;
    private readonly ILogger<DeckPairingStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _pin;
    private DateTime _pinExpiresUtc;
    private readonly Queue<DateTime> _failedAttempts = new();

    /// <summary>Raised with the device token after it was unpaired — lets the
    /// server close that device's live WebSocket session immediately.</summary>
    public event Action<string>? DeviceRemoved;

    public DeckPairingStore(IDataService data, ILogger<DeckPairingStore> logger)
    {
        _data = data;
        _logger = logger;
    }

    // ── settings ─────────────────────────────────────────────────────────

    public async Task<DeckServerSettings> GetSettingsAsync()
    {
        var json = await _data.GetConfigValueAsync(SettingsKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DeckServerSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<DeckServerSettings>(json, Json) ?? new DeckServerSettings();
        }
        catch (JsonException)
        {
            return new DeckServerSettings();
        }
    }

    public async Task SaveSettingsAsync(DeckServerSettings settings)
    {
        await _data.SetConfigValueAsync(
            SettingsKey,
            JsonSerializer.Serialize(settings, Json),
            "VyralDeck").ConfigureAwait(false);
    }

    // ── pairing PIN (desktop side) ───────────────────────────────────────

    /// <summary>Generates a fresh 6-digit PIN and shows it in the UI for <see cref="PinLifetimeMinutes"/>.</summary>
    public string NewPin()
    {
        var number = RandomNumberGenerator.GetInt32(0, 1_000_000);
        _pin = number.ToString("D6");
        _pinExpiresUtc = DateTime.UtcNow.AddMinutes(PinLifetimeMinutes);
        return _pin;
    }

    /// <summary>The PIN currently shown in the UI, or "------" when none is active.</summary>
    public string CurrentPinDisplay =>
        _pin is not null && DateTime.UtcNow < _pinExpiresUtc ? _pin : "------";

    public bool HasActivePin => _pin is not null && DateTime.UtcNow < _pinExpiresUtc;

    // ── pairing (server side) ────────────────────────────────────────────

    /// <summary>
    /// Validates a pairing attempt. Rate-limited to <see cref="MaxFailuresPerMinute"/>
    /// failures per rolling minute; a valid PIN is consumed (one use).
    /// Returns the new device on success, null + reason on failure.
    /// </summary>
    public async Task<(PairedDevice? Device, string? Error)> TryPairAsync(string? pin, string? deviceName)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            while (_failedAttempts.Count > 0 && now - _failedAttempts.Peek() > TimeSpan.FromMinutes(1))
            {
                _failedAttempts.Dequeue();
            }

            if (_failedAttempts.Count >= MaxFailuresPerMinute)
            {
                return (null, "Too many failed pairing attempts — wait a minute and try again.");
            }

            var expired = _pin is null || now >= _pinExpiresUtc;
            if (expired || !string.Equals(pin?.Trim(), _pin, StringComparison.Ordinal))
            {
                _failedAttempts.Enqueue(now);
                _logger.LogWarning("VyralDeck pairing attempt failed ({Reason})", expired ? "expired/absent PIN" : "wrong PIN");
                return (null, expired
                    ? "No pairing PIN is active — generate one on the PC's StreamDeck page."
                    : "Wrong PIN — check the StreamDeck page on the PC.");
            }

            // Consumed: a PIN pairs exactly one device.
            _pin = null;
            _pinExpiresUtc = DateTime.MinValue;

            var device = new PairedDevice
            {
                Token = RandomNumberGenerator.GetHexString(32),
                Name = string.IsNullOrWhiteSpace(deviceName) ? "Phone" : deviceName.Trim(),
            };
            var devices = await LoadDevicesLockedAsync().ConfigureAwait(false);
            devices.Add(device);
            await SaveDevicesLockedAsync(devices).ConfigureAwait(false);
            _logger.LogInformation("VyralDeck: device '{Name}' paired", device.Name);
            return (device, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<PairedDevice>> GetDevicesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadDevicesLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Checks a WebSocket token; bumps the device's LastSeen when it matches.</summary>
    public async Task<PairedDevice?> ValidateTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var devices = await LoadDevicesLockedAsync().ConfigureAwait(false);
            var device = devices.FirstOrDefault(d => FixedTimeEquals(d.Token, token));
            if (device is not null)
            {
                device.LastSeenUtc = DateTime.UtcNow;
                await SaveDevicesLockedAsync(devices).ConfigureAwait(false);
            }
            return device;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveDeviceAsync(string token)
    {
        var removedAny = false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var devices = await LoadDevicesLockedAsync().ConfigureAwait(false);
            removedAny = devices.RemoveAll(d => d.Token == token) > 0;
            await SaveDevicesLockedAsync(devices).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (removedAny)
        {
            // Raised outside the gate so handlers (the server closing sockets)
            // can take their time without blocking other pairing operations.
            DeviceRemoved?.Invoke(token);
        }
    }

    // ── storage ──────────────────────────────────────────────────────────

    private async Task<List<PairedDevice>> LoadDevicesLockedAsync()
    {
        var json = await _data.GetConfigValueAsync(DevicesKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PairedDevice>>(json, Json) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "VyralDeck device list was invalid; starting empty");
            return [];
        }
    }

    private async Task SaveDevicesLockedAsync(List<PairedDevice> devices)
    {
        await _data.SetConfigValueAsync(
            DevicesKey,
            JsonSerializer.Serialize(devices, Json),
            "VyralDeck").ConfigureAwait(false);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a ?? "");
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b ?? "");
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
