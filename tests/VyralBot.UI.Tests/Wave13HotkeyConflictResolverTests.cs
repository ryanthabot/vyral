using VyralBot.UI.Services.Hotkey;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-13 depth pass over the pure <see cref="HotkeyConflict"/> resolver
/// (wave-12, KI-55), complementing Wave12HotkeyGestureTests: equivalent-gesture
/// conflicts across modifier ORDER, alias spellings (Windows/Meta, Numpad/Num,
/// D1) and whitespace; non-conflicts for different keys or modifier sets; and
/// invalid candidate handling.
/// </summary>
public class Wave13HotkeyConflictResolverTests
{
    private static Dictionary<string, string> Map(params (string Sound, string Gesture)[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sound, gesture) in entries)
        {
            map[sound] = gesture;
        }

        return map;
    }

    // ==================================================================
    // Same combo expressed differently — must conflict
    // ==================================================================

    [Fact]
    public void StoredGestureInDifferentModifierOrder_Conflicts()
    {
        var map = Map(("airhorn.wav", "Alt+Ctrl+1")); // reversed order

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+Alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("airhorn.wav", otherSound);
    }

    [Theory]
    [InlineData("Windows+P", "Meta+P")]  // stored long alias vs candidate short alias
    [InlineData("Meta+P", "Windows+P")]  // and the reverse
    [InlineData("Win+P", "windows+p")]   // short alias vs long alias, mixed casing
    public void WinKeyAliases_ConflictAcrossSpellings(string stored, string candidate)
    {
        var map = Map(("airhorn.wav", stored));

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", candidate, out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("airhorn.wav", otherSound);
    }

    [Fact]
    public void NumpadAlias_NumVersusNumpad_Conflicts()
    {
        var map = Map(("airhorn.wav", "Ctrl+Numpad3"));

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+Num3", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("airhorn.wav", otherSound);
    }

    [Fact]
    public void WpfDigitAlias_D1VersusPlainOne_Conflicts()
    {
        var map = Map(("airhorn.wav", "Ctrl+D1"));

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "Ctrl+1", out _);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
    }

    [Fact]
    public void CandidateWithWhitespaceAndCasing_MatchesStoredGesture()
    {
        var map = Map(("airhorn.wav", "ctrl+alt+1"));

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", "  CTRL + ALT + 1 ", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("airhorn.wav", otherSound);
    }

    [Fact]
    public void SameSound_EquivalentSpelling_IsSameSound()
    {
        var map = Map(("airhorn.wav", "CONTROL+alt+1"));

        var kind = HotkeyConflict.Resolve(map, "AIRHORN.wav", "ctrl+alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedBySameSound, kind);
        Assert.Null(otherSound);
    }

    [Fact]
    public void MixedMap_ReportsTheMatchingEntry()
    {
        // Two valid saved gestures plus one unparsable row: only the matching
        // entry may be reported, and the unparsable row is skipped.
        var map = Map(
            ("airhorn.wav", "Ctrl+Alt+2"),
            ("bonk.wav", "Shift+Q"),
            ("legacy.wav", "garbage"));

        var kind = HotkeyConflict.Resolve(map, "zap.wav", "shift+q", out var otherSound);

        Assert.Equal(HotkeyConflictKind.UsedByOtherSound, kind);
        Assert.Equal("bonk.wav", otherSound);
    }

    // ==================================================================
    // Different combos — no conflict
    // ==================================================================

    [Theory]
    [InlineData("Ctrl+Alt+1", "Ctrl+Alt+2")]   // different digit key
    [InlineData("Ctrl+A", "Ctrl+B")]           // different letter key
    [InlineData("Ctrl+1", "Alt+1")]            // different modifier set
    [InlineData("Ctrl+1", "Ctrl+Alt+1")]       // modifier subset is NOT equal
    [InlineData("Ctrl+Alt+Shift+Win+K", "Ctrl+Alt+Shift+K")] // missing Win
    [InlineData("Ctrl+F1", "Ctrl+F2")]         // different function key
    [InlineData("Ctrl+Num1", "Ctrl+Num2")]     // different numpad key
    [InlineData("Ctrl+Num1", "Ctrl+1")]        // numpad 1 is NOT top-row 1
    public void DifferentCombos_DoNotConflict(string stored, string candidate)
    {
        var map = Map(("airhorn.wav", stored));

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", candidate, out var otherSound);

        Assert.Equal(HotkeyConflictKind.None, kind);
        Assert.Null(otherSound);
    }

    [Fact]
    public void EmptyMap_NoConflict()
    {
        var kind = HotkeyConflict.Resolve(Map(), "bonk.wav", "Ctrl+Alt+1", out var otherSound);

        Assert.Equal(HotkeyConflictKind.None, kind);
        Assert.Null(otherSound);
    }

    // ==================================================================
    // Invalid candidates — never collide (rejected earlier by the caller)
    // ==================================================================

    [Theory]
    [InlineData("not-a-gesture")]
    [InlineData("A")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Foo")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnparsableCandidate_ReturnsNone(string? gesture)
    {
        var map = Map(("airhorn.wav", "Ctrl+Alt+1"));

        var kind = HotkeyConflict.Resolve(map, "bonk.wav", gesture!, out var otherSound);

        Assert.Equal(HotkeyConflictKind.None, kind);
        Assert.Null(otherSound);
    }
}
