using VyralBot.UI.Services.Hotkey;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave12 (KI-55 soundboard hotkeys) — pure logic of GlobalHotkeyService:
/// gesture-string parse/format round-trips and the conflict resolver over the
/// persisted {sound: gesture} map. No window/P-Invoke needed for these.
/// </summary>
public class Wave12HotkeyGestureTests
{
    // ------------------------------------------------------------------
    // Parse + format round-trips
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Ctrl+Alt+1")]          // canonical form is stable
    [InlineData("ctrl + alt + 1")]      // case + padding insensitive
    [InlineData("Alt+Ctrl+1")]          // modifier order free
    [InlineData("Control+Alt+1")]       // "Control" alias
    [InlineData("CTRL+ALT+D1")]         // WPF-style D1 digit name not used; plain digit
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Ctrl+Alt+Num3")]       // numpad digit
    [InlineData("Ctrl+Numpad3")]        // numpad alias
    [InlineData("Alt+Shift+Win+P")]     // all four modifiers
    [InlineData("Ctrl+Space")]
    [InlineData("Alt+PageUp")]
    [InlineData("Shift+Home")]
    [InlineData("Ctrl+Alt+Z")]
    public void ParseFormat_RoundTrip_NormalizesToCanonical(string input)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));

        var formatted = HotkeyGesture.Format(gesture);

        // Round-trip: parsing the formatted output yields the same gesture, and
        // formatting is idempotent.
        Assert.True(HotkeyGesture.TryParse(formatted, out var reparsed));
        Assert.Equal(gesture, reparsed);
        Assert.Equal(formatted, HotkeyGesture.Format(reparsed));
    }

    [Theory]
    [InlineData("Ctrl+Alt+1", "Ctrl+Alt+1")]
    [InlineData("control+alt+1", "Ctrl+Alt+1")]
    [InlineData("Alt+Ctrl+1", "Ctrl+Alt+1")]
    [InlineData("shift+ctrl+F10", "Ctrl+Shift+F10")]
    [InlineData("WIN+ALT+S", "Alt+Win+S")]
    [InlineData("Meta+O", "Win+O")]
    [InlineData("Ctrl + Num7", "Ctrl+Num7")]
    [InlineData("Ctrl+Delete", "Ctrl+Delete")]
    public void Format_ProducesCanonicalDisplay(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));

        Assert.Equal(expected, HotkeyGesture.Format(gesture));
    }

    // ------------------------------------------------------------------
    // Virtual-key + modifier-bit correctness
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Ctrl+Alt+1", 0x31)]
    [InlineData("Ctrl+Alt+0", 0x30)]
    [InlineData("Ctrl+A", 0x41)]
    [InlineData("Ctrl+Alt+Z", 0x5A)]
    [InlineData("Ctrl+F1", 0x70)]
    [InlineData("Ctrl+F12", 0x7B)]
    [InlineData("Ctrl+Num0", 0x60)]
    [InlineData("Ctrl+Num9", 0x69)]
    [InlineData("Alt+Space", 0x20)]
    [InlineData("Alt+Delete", 0x2E)]
    public void Parse_MapsToExpectedVirtualKey(string input, int expectedVk)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));

        Assert.Equal(expectedVk, gesture.VirtualKey);
    }

    [Fact]
    public void ToRegisterModifiers_IncludesModBitsAndNoRepeat()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+1", out var gesture));

        var modifiers = HotkeyGesture.ToRegisterModifiers(gesture);

        // MOD_ALT | MOD_CONTROL | MOD_NOREPEAT (Win32 values).
        Assert.Equal(0x0001u | 0x0002u | HotkeyGesture.ModNoRepeat, modifiers);
    }

    [Fact]
    public void ToRegisterModifiers_AllFourModifiers()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+Shift+Win+F4", out var gesture));

        var modifiers = HotkeyGesture.ToRegisterModifiers(gesture);

        Assert.Equal(0x0001u | 0x0002u | 0x0004u | 0x0008u | HotkeyGesture.ModNoRepeat, modifiers);
    }

    // ------------------------------------------------------------------
    // Rejections
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]              // bare key: not global-hotkey-safe
    [InlineData("F5")]             // bare function key
    [InlineData("Ctrl")]           // modifier only
    [InlineData("Ctrl+Alt")]       // modifiers only, no key
    [InlineData("Ctrl+Ctrl+1")]    // duplicate modifier
    [InlineData("Ctrl+Alt+1+2")]   // two keys
    [InlineData("Ctrl+Foo")]       // unknown key name
    [InlineData("Ctrl+Alt+F13")]   // beyond F12
    [InlineData("Ctrl+!")]         // unsupported character
    public void TryParse_RejectsInvalidGestures(string? input)
    {
        Assert.False(HotkeyGesture.TryParse(input, out var gesture));

        Assert.Equal(default(HotkeyCombo), gesture);
    }

    [Fact]
    public void IsSupportedVirtualKey_AcceptsKnownKeysOnly()
    {
        Assert.True(HotkeyGesture.IsSupportedVirtualKey(0x31));  // '1'
        Assert.True(HotkeyGesture.IsSupportedVirtualKey(0x5A));  // 'Z'
        Assert.True(HotkeyGesture.IsSupportedVirtualKey(0x70));  // F1
        Assert.True(HotkeyGesture.IsSupportedVirtualKey(0x21));  // PageUp

        Assert.False(HotkeyGesture.IsSupportedVirtualKey(0xBA)); // VK_OEM_1 (';') — not offered
        Assert.False(HotkeyGesture.IsSupportedVirtualKey(0));    // VK_NONE
    }

    // ------------------------------------------------------------------
    // Conflict detection (pure part of the assignment flow)
    // ------------------------------------------------------------------

    [Fact]
    public void Conflict_DetectsOtherSound()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["airhorn.wav"] = "Ctrl+Alt+1",
        };

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+Alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("airhorn.wav", otherSound);
    }

    [Fact]
    public void Conflict_SameSoundSameGesture_IsSameSound()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["airhorn.wav"] = "Ctrl+Alt+1",
        };

        var kind = HotkeyConflict.Resolve(map, "airhorn.wav", "Ctrl+Alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedBySameSound, kind);
        Assert.Null(otherSound);
    }

    [Fact]
    public void Conflict_MatchesAcrossEquivalentSpellings()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["airhorn.wav"] = "Control+Alt+1", // stored with the "Control" alias
        };

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "ctrl+alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("airhorn.wav", otherSound);
    }

    [Fact]
    public void Conflict_SoundNameComparisonIsCaseInsensitive()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AirHorn.wav"] = "Ctrl+Alt+1",
        };

        var kind = HotkeyConflict.Resolve(map, "airhorn.wav", "Ctrl+Alt+1", out _);

        Assert.Equal(HotkeyConflictKind.UsedBySameSound, kind);
    }

    [Fact]
    public void Conflict_FreeGesture_IsNone()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["airhorn.wav"] = "Ctrl+Alt+1",
        };

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+Alt+2", out var otherSound);

        Assert.Equal(HotkeyConflictKind.None, kind);
        Assert.Null(otherSound);
    }

    [Fact]
    public void Conflict_EmptyMap_IsNone()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+Alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.None, kind);
        Assert.Null(otherSound);
    }

    [Fact]
    public void Conflict_UnparsableStoredGesture_NeverCollides()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["airhorn.wav"] = "not-a-gesture",
        };

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+Alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.None, kind);
        Assert.Null(otherSound);
    }

    // ------------------------------------------------------------------
    // WPF-key capture conversion (pure mapping, no window needed)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(System.Windows.Input.Key.D1, System.Windows.Input.ModifierKeys.Control, "Ctrl+1")]
    [InlineData(System.Windows.Input.Key.NumPad3, System.Windows.Input.ModifierKeys.Control, "Ctrl+Num3")]
    [InlineData(System.Windows.Input.Key.F5, System.Windows.Input.ModifierKeys.Alt, "Alt+F5")]
    [InlineData(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt, "Ctrl+Alt+A")]
    [InlineData(System.Windows.Input.Key.Space, System.Windows.Input.ModifierKeys.Shift, "Shift+Space")]
    public void FromWpfKey_BuildsCanonicalGesture(
        System.Windows.Input.Key key,
        System.Windows.Input.ModifierKeys modifiers,
        string expected)
    {
        Assert.True(HotkeyGesture.FromWpfKey(key, modifiers, out var gesture));

        Assert.Equal(expected, HotkeyGesture.Format(gesture));
    }

    [Theory]
    [InlineData(System.Windows.Input.Key.LeftCtrl, System.Windows.Input.ModifierKeys.Control)]   // modifier-only press
    [InlineData(System.Windows.Input.Key.RightAlt, System.Windows.Input.ModifierKeys.Alt)]
    [InlineData(System.Windows.Input.Key.D1, System.Windows.Input.ModifierKeys.None)]            // no modifier
    public void FromWpfKey_RejectsIncompleteCombos(
        System.Windows.Input.Key key,
        System.Windows.Input.ModifierKeys modifiers)
    {
        Assert.False(HotkeyGesture.FromWpfKey(key, modifiers, out _));
    }
}
