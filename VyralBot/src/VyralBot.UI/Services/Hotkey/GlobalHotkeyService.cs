using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Audio;
using VyralBot.Data;

namespace VyralBot.UI.Services.Hotkey;

/// <summary>
/// Modifier flags for a hotkey gesture, mirroring the Win32 MOD_* constants
/// used by RegisterHotKey (values are the API bits so conversion is a cast).
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,     // MOD_ALT
    Control = 0x0002, // MOD_CONTROL
    Shift = 0x0004,   // MOD_SHIFT
    Win = 0x0008,     // MOD_WIN
}

/// <summary>
/// A parsed global-hotkey gesture: modifier combination plus one virtual key.
/// Pure value type — parse/format round-trips are unit-testable without a window.
/// </summary>
public readonly record struct HotkeyCombo(HotkeyModifiers Modifiers, int VirtualKey);

/// <summary>
/// Pure parsing/formatting for hotkey gesture strings ("Ctrl+Alt+1").
/// Kept free of any window/P-Invoke dependency so the logic is testable.
/// Canonical form: modifiers in Ctrl, Alt, Shift, Win order + key display name.
/// </summary>
public static class HotkeyGesture
{
    // MOD_NOREPEAT avoids auto-repeat firing the hotkey while held.
    public const uint ModNoRepeat = 0x4000;

    /// <summary>
    /// Parses a gesture string. Accepts "Ctrl"/"Control", "Win"/"Windows"/"Meta"
    /// aliases, any casing, spaces around '+', and any modifier order. Requires
    /// at least one modifier and exactly one supported key.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyCombo gesture)
    {
        gesture = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false; // needs modifier(s) + a key
        }

        var modifiers = HotkeyModifiers.None;
        var virtualKey = -1;

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                if (modifiers.HasFlag(modifier))
                {
                    return false; // duplicate modifier ("Ctrl+Ctrl+A")
                }

                modifiers |= modifier;
                continue;
            }

            if (virtualKey >= 0)
            {
                return false; // more than one non-modifier key
            }

            if (!TryKeyFromName(part, out var key))
            {
                return false; // unsupported/unknown key name
            }

            virtualKey = key;
        }

        if (modifiers == HotkeyModifiers.None || virtualKey < 0)
        {
            return false; // modifier-only or key-less
        }

        gesture = new HotkeyCombo(modifiers, virtualKey);
        return true;
    }

    /// <summary>Formats a gesture in canonical display form ("Ctrl+Alt+1").</summary>
    public static string Format(HotkeyCombo gesture)
    {
        var builder = new System.Text.StringBuilder(24);
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            builder.Append("Ctrl+");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            builder.Append("Alt+");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            builder.Append("Shift+");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            builder.Append("Win+");
        }

        builder.Append(KeyDisplayName(gesture.VirtualKey));
        return builder.ToString();
    }

    /// <summary>Display name for a virtual key (empty when unsupported).</summary>
    public static string KeyDisplayName(int virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString(); // '0'..'9' (top row)
        }

        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString(); // 'A'..'Z'
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return "Num" + (virtualKey - 0x60); // numpad 0..9
        }

        if (virtualKey is >= 0x70 and <= 0x7B)
        {
            return "F" + (virtualKey - 0x6F); // F1..F12
        }

        return virtualKey switch
        {
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            _ => string.Empty,
        };
    }

    /// <summary>True when the virtual key is one this feature can register/display.</summary>
    public static bool IsSupportedVirtualKey(int virtualKey)
    {
        return KeyDisplayName(virtualKey).Length > 0;
    }

    /// <summary>Builds the RegisterHotKey modifier word (MOD_* bits | MOD_NOREPEAT).</summary>
    public static uint ToRegisterModifiers(HotkeyCombo gesture)
    {
        return (uint)gesture.Modifiers | ModNoRepeat;
    }

    /// <summary>
    /// Converts a WPF key press (capture UI) into a gesture. Modifier-only keys and
    /// unsupported virtual keys return false so the capture keeps waiting. Callers
    /// must resolve Alt combinations first: WPF reports Key.System while Alt is held,
    /// so pass <c>e.SystemKey</c> in that case.
    /// </summary>
    public static bool FromWpfKey(Key key, ModifierKeys modifiers, out HotkeyCombo gesture)
    {
        gesture = default;

        var wpfModifiers = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            wpfModifiers |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            wpfModifiers |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            wpfModifiers |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            wpfModifiers |= HotkeyModifiers.Win;
        }

        if (wpfModifiers == HotkeyModifiers.None)
        {
            return false; // global hotkeys need at least one modifier
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.System or Key.None or Key.DeadCharProcessed)
        {
            return false; // modifier-only / non-key events
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!IsSupportedVirtualKey(virtualKey))
        {
            return false;
        }

        gesture = new HotkeyCombo(wpfModifiers, virtualKey);
        return true;
    }

    private static bool TryParseModifier(string text, out HotkeyModifiers modifier)
    {
        switch (text.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = HotkeyModifiers.Control;
                return true;
            case "alt":
                modifier = HotkeyModifiers.Alt;
                return true;
            case "shift":
                modifier = HotkeyModifiers.Shift;
                return true;
            case "win":
            case "windows":
            case "meta":
                modifier = HotkeyModifiers.Win;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryKeyFromName(string text, out int virtualKey)
    {
        virtualKey = -1;
        if (text.Length == 1)
        {
            var c = char.ToUpperInvariant(text[0]);
            if (c is >= '0' and <= '9')
            {
                virtualKey = c; // '0'=0x30 .. '9'=0x39
                return true;
            }

            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c; // 'A'=0x41 .. 'Z'=0x5A
                return true;
            }

            return false;
        }

        var name = text.ToLowerInvariant();
        if (name.StartsWith("num", StringComparison.Ordinal) &&
            name.Length == 4 &&
            name[3] is >= '0' and <= '9')
        {
            virtualKey = 0x60 + (name[3] - '0'); // VK_NUMPAD0..9
            return true;
        }

        if (name.Length == 2 && name[0] == 'd' && name[1] is >= '0' and <= '9')
        {
            virtualKey = 0x30 + (name[1] - '0'); // "D1" = WPF alias for top-row '1'
            return true;
        }

        if (name.Length == 2 && name[0] == 'f' && name[1] is >= '1' and <= '9')
        {
            virtualKey = 0x70 + (name[1] - '0' - 1); // F1..F9
            return true;
        }

        if (name.Length == 3 && name[0] == 'f')
        {
            // F10..F12
            if (int.TryParse(name.AsSpan(1), out var fn) && fn is >= 10 and <= 12)
            {
                virtualKey = 0x70 + (fn - 1);
                return true;
            }
        }

        if (name.StartsWith("numpad", StringComparison.Ordinal) &&
            name.Length == 7 &&
            name[6] is >= '0' and <= '9')
        {
            virtualKey = 0x60 + (name[6] - '0'); // "Numpad1" alias for "Num1"
            return true;
        }

        virtualKey = name switch
        {
            "space" => 0x20,
            "pageup" or "prior" => 0x21,
            "pagedown" or "next" => 0x22,
            "end" => 0x23,
            "home" => 0x24,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "insert" or "ins" => 0x2D,
            "delete" or "del" => 0x2E,
            _ => -1,
        };
        return virtualKey >= 0;
    }
}

/// <summary>Outcome of checking a gesture against the saved sound-hotkey map.</summary>
public enum HotkeyConflictKind
{
    None,
    UsedBySameSound,
    UsedByOtherSound,
}

/// <summary>Pure conflict-resolution helper over the persisted {sound: gesture} map.</summary>
public static class HotkeyConflict
{
    /// <summary>
    /// Checks whether <paramref name="gesture"/> collides with a saved assignment.
    /// Gestures are compared by their parsed value (so "Control+Alt+1" matches
    /// a stored "Ctrl+Alt+1"); unparsable stored values never collide.
    /// </summary>
    public static HotkeyConflictKind Resolve(
        IReadOnlyDictionary<string, string> assignments,
        string soundName,
        string gesture,
        out string? otherSound)
    {
        otherSound = null;
        if (!HotkeyGesture.TryParse(gesture, out var parsed))
        {
            return HotkeyConflictKind.None; // invalid gestures are rejected earlier
        }

        foreach (var (savedSound, savedGesture) in assignments)
        {
            if (!HotkeyGesture.TryParse(savedGesture, out var saved))
            {
                continue;
            }

            if (saved != parsed)
            {
                continue;
            }

            if (string.Equals(savedSound, soundName, StringComparison.OrdinalIgnoreCase))
            {
                return HotkeyConflictKind.UsedBySameSound;
            }

            otherSound = savedSound;
            return HotkeyConflictKind.UsedByOtherSound;
        }

        return HotkeyConflictKind.None;
    }
}

/// <summary>Outcome of <see cref="GlobalHotkeyService.SetSoundHotkeyAsync"/>.</summary>
public enum SoundHotkeyOutcome
{
    Registered,
    ReplacedExisting,
    InvalidGesture,
    ConflictOtherSound,
    OsRejected,
    LimitReached,
    NoWindowAttached,
}

/// <summary>Result payload for a hotkey assignment attempt.</summary>
public sealed record SoundHotkeyResult(SoundHotkeyOutcome Outcome, string? ConflictSound = null)
{
    public static readonly SoundHotkeyResult Registered = new(SoundHotkeyOutcome.Registered);
    public static readonly SoundHotkeyResult ReplacedExisting = new(SoundHotkeyOutcome.ReplacedExisting);
    public static readonly SoundHotkeyResult InvalidGesture = new(SoundHotkeyOutcome.InvalidGesture);
    public static readonly SoundHotkeyResult OsRejected = new(SoundHotkeyOutcome.OsRejected);
    public static readonly SoundHotkeyResult LimitReached = new(SoundHotkeyOutcome.LimitReached);
    public static readonly SoundHotkeyResult NoWindowAttached = new(SoundHotkeyOutcome.NoWindowAttached);

    public static SoundHotkeyResult Conflict(string conflictSound)
        => new(SoundHotkeyOutcome.ConflictOtherSound, conflictSound);
}

/// <summary>
/// Global soundboard hotkeys (KI-55): registers Win32 RegisterHotKey slots on the
/// main window's HWND so streamers can fire sounds while a game has focus.
///
/// Singleton that OUTLIVES the Sounds page VM — the registration table, WM_HOTKEY
/// dispatch and playback all live here; the page only edits assignments.
/// Lifecycle: MainWindow.OnSourceInitialized calls <see cref="AttachToWindow"/>,
/// MainViewModel.OnProfileLoadedAsync (startup + every profile switch) calls
/// <see cref="RestoreSavedHotkeysAsync"/>, App.OnExit disposes (unregisters all).
///
/// Register/Unregister P/Invoke calls must run on the window's UI thread (that
/// thread owns the hotkey); public APIs marshal themselves via the captured
/// Dispatcher, so callers may be on any thread.
///
/// Persistence: config key "Sounds.Hotkeys" (category "Sounds") as JSON
/// { "sound file name": "Ctrl+Alt+1" } via <see cref="IDataService"/>.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    /// <summary>Windows hotkey message forwarded by MainWindow.WndProc.</summary>
    public const int WmHotkey = 0x0312;

    /// <summary>Upper bound on simultaneously registered hotkeys.</summary>
    public const int MaxSlots = 10;

    private const string ConfigKey = "Sounds.Hotkeys";
    private const string ConfigCategory = "Sounds";

    private sealed class HotkeyEntry
    {
        public int Id;
        public string Gesture = string.Empty;
        public string SoundName = string.Empty;
    }

    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly ISoundService _soundService;
    private readonly IDataService _dataService;
    private readonly object _sync = new();

    // Registration table + lookup indexes. Guarded by _sync; ids are app-private 1..N.
    private readonly Dictionary<int, HotkeyEntry> _entries = new(MaxSlots);
    private readonly Dictionary<string, int> _byGesture = new(MaxSlots, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _bySound = new(MaxSlots, StringComparer.OrdinalIgnoreCase);

    private Window? _window;
    private IntPtr _hwnd;
    private int _nextId = 1;
    private bool _disposed;

    public GlobalHotkeyService(
        ILogger<GlobalHotkeyService> logger,
        ISoundService soundService,
        IDataService dataService)
    {
        _logger = logger;
        _soundService = soundService;
        _dataService = dataService;
    }

    /// <summary>
    /// Binds the service to the main window. Call once from
    /// MainWindow.OnSourceInitialized (the HWND exists there). The window is
    /// created once per app run and never recreated, so its HWND is stable for
    /// the process lifetime; if it is ever closed, WM_HOTKEY is not delivered
    /// anyway and Closed unregisters everything as a backstop.
    /// </summary>
    public void AttachToWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        lock (_sync)
        {
            if (_window == window)
            {
                return; // idempotent
            }

            _window = window;
            _hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }

        window.Closed += (_, _) => UnregisterAll();
        _logger.LogInformation("Global hotkey service attached to the main window (HWND {Hwnd:x})", _hwnd);
    }

    /// <summary>
    /// WM_HOTKEY sink. MainWindow.WndProc forwards wParam (the hotkey id).
    /// Returns true when the id belongs to this service (message handled).
    /// </summary>
    public bool HandleWmHotkey(IntPtr wParam)
    {
        var id = wParam.ToInt32();
        string soundName;
        lock (_sync)
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                return false;
            }

            soundName = entry.SoundName;
        }

        // Fire-and-forget playback: never block the message pump (SoundService
        // already pushes local playback to Task.Run internally).
        _ = PlayHotkeySoundAsync(soundName);
        return true;
    }

    /// <summary>
    /// Assigns (or replaces) a sound's global hotkey: validates the gesture,
    /// rejects conflicts with another sound, registers the OS hotkey immediately
    /// and persists the map. Same-sound reassignment replaces the old hotkey.
    /// </summary>
    public async Task<SoundHotkeyResult> SetSoundHotkeyAsync(string soundName, string gesture)
    {
        if (string.IsNullOrWhiteSpace(soundName) || string.IsNullOrWhiteSpace(gesture))
        {
            return SoundHotkeyResult.InvalidGesture;
        }

        if (!HotkeyGesture.TryParse(gesture, out var parsed))
        {
            return SoundHotkeyResult.InvalidGesture;
        }

        var canonical = HotkeyGesture.Format(parsed);
        var map = await ReadHotkeyMapAsync();
        var conflict = HotkeyConflict.Resolve(map, soundName, canonical, out var otherSound);
        if (conflict == HotkeyConflictKind.UsedByOtherSound)
        {
            _logger.LogInformation("Hotkey {Gesture} rejected - already assigned to '{Sound}'", canonical, otherSound);
            return SoundHotkeyResult.Conflict(otherSound!);
        }

        var result = await RunOnWindowDispatcherAsync(() => RegisterCore(soundName, canonical));
        if (result.Outcome is SoundHotkeyOutcome.Registered or SoundHotkeyOutcome.ReplacedExisting)
        {
            map[soundName] = canonical;
            await WriteHotkeyMapAsync(map);
        }

        return result;
    }

    /// <summary>
    /// Removes a sound's hotkey: unregisters the OS slot and erases the map entry.
    /// </summary>
    public async Task ClearSoundHotkeyAsync(string soundName)
    {
        if (string.IsNullOrWhiteSpace(soundName))
        {
            return;
        }

        var removed = await RunOnWindowDispatcherAsync(() => UnregisterBySoundCore(soundName));
        if (removed)
        {
            var map = await ReadHotkeyMapAsync();
            if (map.Remove(soundName))
            {
                await WriteHotkeyMapAsync(map);
            }
        }
    }

    /// <summary>
    /// Reads the persisted {sound: gesture} map (for the Sounds page display).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetSoundHotkeyMapAsync()
    {
        return await ReadHotkeyMapAsync();
    }

    /// <summary>
    /// Re-registers the saved hotkeys for the active profile. Called from
    /// MainViewModel.OnProfileLoadedAsync (app start + profile switches):
    /// unregisters the previous profile's slots first, then registers each saved
    /// entry. Entries whose sound file no longer exists are skipped with an info
    /// log (kept in config so the Sounds page can show and fix them).
    /// </summary>
    public async Task RestoreSavedHotkeysAsync()
    {
        var map = await ReadHotkeyMapAsync();
        if (map.Count == 0)
        {
            await RunOnWindowDispatcherAsync(UnregisterAllCore);
            return;
        }

        var restored = 0;
        foreach (var (soundName, gesture) in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!HotkeyGesture.TryParse(gesture, out var parsed))
            {
                _logger.LogInformation("Skipping saved hotkey '{Gesture}' for '{Sound}' - unparsable gesture", gesture, soundName);
                continue;
            }

            if (!_soundService.SoundFileExists(soundName))
            {
                _logger.LogInformation("Skipping saved hotkey '{Gesture}' for '{Sound}' - sound file missing", gesture, soundName);
                continue;
            }

            var result = await RunOnWindowDispatcherAsync(() => RegisterCore(soundName, HotkeyGesture.Format(parsed)));
            if (result.Outcome is SoundHotkeyOutcome.Registered or SoundHotkeyOutcome.ReplacedExisting)
            {
                restored++;
            }
            else
            {
                _logger.LogInformation("Saved hotkey '{Gesture}' for '{Sound}' not registered ({Outcome})", gesture, soundName, result.Outcome);
            }
        }

        _logger.LogInformation("Restored {Count} global sound hotkey(s) for the active profile", restored);
    }

    /// <summary>Unregisters every hotkey (shutdown / window-closed backstop). Idempotent.</summary>
    public void UnregisterAll()
    {
        var window = _window;
        if (window == null)
        {
            return;
        }

        if (window.Dispatcher.CheckAccess())
        {
            UnregisterAllCore();
            return;
        }

        // Wrong thread (host disposal): schedule the release instead of blocking.
        // Windows also releases hotkeys when the registering thread exits, so
        // this cannot leak past process shutdown.
        _ = window.Dispatcher.InvokeAsync(UnregisterAllCore, DispatcherPriority.Send);
    }

    /// <summary>Disposes: unregisters all hotkeys. Safe to call multiple times.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }

    // ---------------------------------------------------------------------
    // Core (must run on the window's UI thread — RegisterHotKey is owned by
    // the thread that called it, and that thread's pump delivers WM_HOTKEY)
    // ---------------------------------------------------------------------

    private SoundHotkeyResult RegisterCore(string soundName, string canonicalGesture)
    {
        var parsed = ParseOrThrow(canonicalGesture);
        lock (_sync)
        {
            if (_window == null || _hwnd == IntPtr.Zero)
            {
                return SoundHotkeyResult.NoWindowAttached;
            }

            // Same sound, same gesture: already live, nothing to do.
            if (_bySound.TryGetValue(soundName, out var existingId) &&
                _entries.TryGetValue(existingId, out var existing) &&
                string.Equals(existing.Gesture, canonicalGesture, StringComparison.OrdinalIgnoreCase))
            {
                return SoundHotkeyResult.ReplacedExisting;
            }

            // Free the sound's previous slot first so a reassignment never
            // hits LimitReached for its own old entry, and so the OS never sees
            // the same gesture registered twice during the swap.
            UnregisterBySoundCore(soundName);

            if (_entries.Count >= MaxSlots)
            {
                return SoundHotkeyResult.LimitReached;
            }

            if (_byGesture.ContainsKey(canonicalGesture))
            {
                // Live-table guard: gesture taken by another sound whose map row
                // was not loaded — treat as a conflict.
                var other = _entries.Values.First(e => string.Equals(e.Gesture, canonicalGesture, StringComparison.OrdinalIgnoreCase));
                return SoundHotkeyResult.Conflict(other.SoundName);
            }

            var id = _nextId++;
            if (!RegisterHotKey(_hwnd, id, HotkeyGesture.ToRegisterModifiers(parsed), (uint)parsed.VirtualKey))
            {
                _logger.LogInformation("RegisterHotKey rejected {Gesture} - the combo is already claimed by another application", canonicalGesture);
                return SoundHotkeyResult.OsRejected;
            }

            var entry = new HotkeyEntry { Id = id, Gesture = canonicalGesture, SoundName = soundName };
            _entries[id] = entry;
            _byGesture[canonicalGesture] = id;
            _bySound[soundName] = id;

            _logger.LogInformation("Registered global hotkey {Gesture} for '{Sound}' (slot {Slot}/{Max})", canonicalGesture, soundName, _entries.Count, MaxSlots);
            return SoundHotkeyResult.Registered;
        }
    }

    private static HotkeyCombo ParseOrThrow(string gesture)
    {
        return HotkeyGesture.TryParse(gesture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unparsable gesture '{gesture}' reached registration");
    }

    private bool UnregisterBySoundCore(string soundName)
    {
        lock (_sync)
        {
            if (!_bySound.Remove(soundName, out var id) || !_entries.Remove(id, out var entry))
            {
                return false;
            }

            _byGesture.Remove(entry.Gesture);
            UnregisterHotKey(_hwnd, id);
            _logger.LogInformation("Unregistered global hotkey {Gesture} for '{Sound}'", entry.Gesture, entry.SoundName);
            return true;
        }
    }

    private void UnregisterAllCore()
    {
        lock (_sync)
        {
            foreach (var entry in _entries.Values)
            {
                try
                {
                    UnregisterHotKey(_hwnd, entry.Id);
                }
                catch
                {
                    // Window already destroyed — nothing left to release.
                }
            }

            if (_entries.Count > 0)
            {
                _logger.LogInformation("Unregistered all {Count} global hotkey(s)", _entries.Count);
            }

            _entries.Clear();
            _byGesture.Clear();
            _bySound.Clear();
        }
    }

    private async Task PlayHotkeySoundAsync(string soundName)
    {
        try
        {
            // Same master-volume path as the Sounds page test-play button: read
            // the shared AudioSettings blob, scale 0-100 into 0..1 (identical to
            // SoundService.PlayEventSoundAsync's live volume math).
            var masterPercent = 100;
            var settingsJson = await _dataService.GetConfigValueAsync("AudioSettings");
            if (!string.IsNullOrEmpty(settingsJson))
            {
                var settings = JsonSerializer.Deserialize<AudioSettings>(settingsJson);
                if (settings != null)
                {
                    masterPercent = Math.Clamp(settings.MasterVolume, 0, 100);
                }
            }

            _logger.LogInformation("Hotkey fired: playing '{Sound}' at master volume {Volume}%", soundName, masterPercent);
            await _soundService.PlaySoundFileAsync(soundName, masterPercent / 100f);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing hotkey sound '{Sound}'", soundName);
        }
    }

    private async Task<T> RunOnWindowDispatcherAsync<T>(Func<T> action)
    {
        var window = _window;
        if (window == null)
        {
            if (typeof(T) == typeof(SoundHotkeyResult))
            {
                return (T)(object)SoundHotkeyResult.NoWindowAttached;
            }

            return default!;
        }

        if (window.Dispatcher.CheckAccess())
        {
            return action();
        }

        var operation = window.Dispatcher.InvokeAsync(action);
        return await operation.Task.ConfigureAwait(true);
    }

    private Task RunOnWindowDispatcherAsync(Action action)
    {
        var window = _window;
        if (window == null)
        {
            return Task.CompletedTask; // nothing registered yet, nothing to release
        }

        if (window.Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return window.Dispatcher.InvokeAsync(action).Task;
    }

    private async Task<Dictionary<string, string>> ReadHotkeyMapAsync()
    {
        try
        {
            var json = await _dataService.GetConfigValueAsync(ConfigKey);
            if (string.IsNullOrEmpty(json))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map != null
                ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogInformation("Saved hotkey map unreadable - starting from empty");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading the saved hotkey map");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteHotkeyMapAsync(Dictionary<string, string> map)
    {
        try
        {
            var json = JsonSerializer.Serialize(map);
            await _dataService.SetConfigValueAsync(ConfigKey, json, ConfigCategory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting the hotkey map");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
