using VyralBot.UI.Services.Hotkey;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-13 full-matrix pass over <see cref="HotkeyGesture"/> parse/format
/// (wave-12, KI-55), complementing Wave12HotkeyGestureTests: every modifier
/// permutation, the Control/Windows/Meta alias space, numpad name variants,
/// case/whitespace tolerance, complete VK ranges (A-Z, 0-9, F1-F12, numpad,
/// named keys) and a wider rejection matrix.
/// </summary>
public class Wave13HotkeyGestureMatrixTests
{
    // ==================================================================
    // Modifier order permutations — all orders parse to the same combo
    // and format to the one canonical order.
    // ==================================================================

    public static IEnumerable<object[]> ModifierPermutations()
    {
        var modifiers = new[] { "Ctrl", "Alt", "Shift", "Win" };
        foreach (var permutation in Permute(modifiers, 0, modifiers.Length - 1))
        {
            yield return new object[] { string.Join("+", permutation) + "+K" };
        }
    }

    private static IEnumerable<string[]> Permute(string[] items, int start, int end)
    {
        if (start == end)
        {
            yield return (string[])items.Clone();
            yield break;
        }

        for (var i = start; i <= end; i++)
        {
            (items[start], items[i]) = (items[i], items[start]);
            foreach (var rest in Permute(items, start + 1, end))
            {
                yield return rest;
            }
            (items[start], items[i]) = (items[i], items[start]);
        }
    }

    [Theory]
    [MemberData(nameof(ModifierPermutations))] // 24 orderings of the 4 modifiers
    public void Parse_AnyModifierOrder_SameCombo_CanonicalFormat(string input)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, gesture.Modifiers);
        Assert.Equal(0x4B, gesture.VirtualKey); // 'K'
        Assert.Equal("Ctrl+Alt+Shift+Win+K", HotkeyGesture.Format(gesture));
    }

    [Fact]
    public void Parse_AliasSpace_ControlWindowsMeta_ResolveIdentically()
    {
        // Cartesian alias space: 2 x 1 x 1 x 3 spellings must all produce the
        // same parsed gesture.
        var ctrlAliases = new[] { "Ctrl", "Control" };
        var winAliases = new[] { "Win", "Windows", "Meta" };

        HotkeyCombo? reference = null;
        foreach (var ctrl in ctrlAliases)
        {
            foreach (var win in winAliases)
            {
                var text = $"{ctrl}+Alt+Shift+{win}+P";
                Assert.True(HotkeyGesture.TryParse(text, out var gesture), text);
                if (reference is { } first)
                {
                    Assert.Equal(first, gesture);
                }
                else
                {
                    reference = gesture;
                }
            }
        }

        Assert.Equal("Ctrl+Alt+Shift+Win+P", HotkeyGesture.Format(reference!.Value));
    }

    // ==================================================================
    // Case / whitespace tolerance
    // ==================================================================

    [Theory]
    [InlineData("CTRL+ALT+1")]
    [InlineData("ctrl+alt+1")]
    [InlineData("cTrL+aLt+1")]
    [InlineData("SHIFT+f5")]
    [InlineData("WINDOWS+S")]
    [InlineData("MeTa+O")]
    public void Parse_IsCaseInsensitive(string input)
    {
        Assert.True(HotkeyGesture.TryParse(input, out _));
    }

    [Theory]
    [InlineData(" Ctrl + Alt + 1 ")]   // padding around the whole string
    [InlineData("Ctrl\t+\tAlt+\t1")]   // tabs around parts
    [InlineData("  shift   +   home  ")]
    public void Parse_ToleratesWhitespaceAroundPlus(string input)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));
        Assert.True(HotkeyGesture.TryParse(HotkeyGesture.Format(gesture), out var reparsed));
        Assert.Equal(gesture, reparsed);
    }

    [Theory]
    [InlineData("Ctrl++A")]    // empty middle segment is removed
    [InlineData("+Ctrl+A")]    // leading '+' tolerated
    [InlineData("Ctrl+A+")]    // trailing '+' tolerated
    public void Parse_TolerantTokenizer_IgnoresEmptySegments(string input)
    {
        // Documented tokenizer behavior: split with RemoveEmptyEntries accepts
        // repeated/edge '+' characters and parses the surviving parts.
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));

        Assert.Equal(HotkeyModifiers.Control, gesture.Modifiers);
        Assert.Equal(0x41, gesture.VirtualKey);
        Assert.Equal("Ctrl+A", HotkeyGesture.Format(gesture));
    }

    // ==================================================================
    // VK mapping — full ranges
    // ==================================================================

    [Fact]
    public void Parse_LettersAtoZ_MapToVirtualKeyCodes()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            Assert.True(HotkeyGesture.TryParse($"Ctrl+{c}", out var gesture), $"Ctrl+{c}");
            Assert.Equal((int)c, gesture.VirtualKey);
            Assert.Equal($"Ctrl+{c}", HotkeyGesture.Format(gesture));
        }
    }

    [Fact]
    public void Parse_LowercaseLetter_NormalizesToUppercaseVK()
    {
        Assert.True(HotkeyGesture.TryParse("ctrl+m", out var gesture));

        Assert.Equal(0x4D, gesture.VirtualKey); // 'M'
        Assert.Equal("Ctrl+M", HotkeyGesture.Format(gesture));
    }

    [Fact]
    public void Parse_DigitsZeroToNine_MapToVirtualKeyCodes()
    {
        for (var d = '0'; d <= '9'; d++)
        {
            Assert.True(HotkeyGesture.TryParse($"Alt+{d}", out var gesture), $"Alt+{d}");
            Assert.Equal((int)d, gesture.VirtualKey); // '0' = 0x30 .. '9' = 0x39
            Assert.Equal($"Alt+{d}", HotkeyGesture.Format(gesture));
        }
    }

    [Fact]
    public void Parse_WpfDigitAlias_D1ToD9_MapToTopRowDigits()
    {
        for (var d = '1'; d <= '9'; d++)
        {
            Assert.True(HotkeyGesture.TryParse($"Ctrl+D{d}", out var gesture), $"Ctrl+D{d}");
            Assert.Equal((int)d, gesture.VirtualKey);
            Assert.Equal($"Ctrl+{d}", HotkeyGesture.Format(gesture));
        }
    }

    [Fact]
    public void Parse_FunctionKeysF1ToF12_MapToVirtualKeyCodes()
    {
        for (var f = 1; f <= 12; f++)
        {
            Assert.True(HotkeyGesture.TryParse($"Shift+F{f}", out var gesture), $"Shift+F{f}");
            Assert.Equal(0x70 + f - 1, gesture.VirtualKey); // F1 = 0x70 .. F12 = 0x7B
            Assert.Equal($"Shift+F{f}", HotkeyGesture.Format(gesture));
        }
    }

    [Fact]
    public void Parse_NumpadDigits_BothSpellings_MapToNumpadVKs()
    {
        for (var d = 0; d <= 9; d++)
        {
            Assert.True(HotkeyGesture.TryParse($"Ctrl+Num{d}", out var shortForm), $"Ctrl+Num{d}");
            Assert.Equal(0x60 + d, shortForm.VirtualKey); // VK_NUMPAD0 = 0x60 .. VK_NUMPAD9 = 0x69
            Assert.Equal($"Ctrl+Num{d}", HotkeyGesture.Format(shortForm));

            Assert.True(HotkeyGesture.TryParse($"Ctrl+Numpad{d}", out var longForm), $"Ctrl+Numpad{d}");
            Assert.Equal(shortForm, longForm); // alias parses to the identical gesture
        }
    }

    [Theory]
    [InlineData("Alt+Space", 0x20, "Space")]
    [InlineData("Ctrl+PageUp", 0x21, "PageUp")]
    [InlineData("Ctrl+Prior", 0x21, "PageUp")]      // WPF alias for PageUp
    [InlineData("Ctrl+PageDown", 0x22, "PageDown")]
    [InlineData("Ctrl+Next", 0x22, "PageDown")]     // WPF alias for PageDown
    [InlineData("Alt+End", 0x23, "End")]
    [InlineData("Alt+Home", 0x24, "Home")]
    [InlineData("Ctrl+Left", 0x25, "Left")]
    [InlineData("Ctrl+Up", 0x26, "Up")]
    [InlineData("Ctrl+Right", 0x27, "Right")]
    [InlineData("Ctrl+Down", 0x28, "Down")]
    [InlineData("Ctrl+Insert", 0x2D, "Insert")]
    [InlineData("Ctrl+Ins", 0x2D, "Insert")]        // alias
    [InlineData("Ctrl+Delete", 0x2E, "Delete")]
    [InlineData("Ctrl+Del", 0x2E, "Delete")]        // alias
    public void Parse_NamedKeys_VKAndCanonicalDisplay(string input, int expectedVk, string expectedDisplayName)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));

        Assert.Equal(expectedVk, gesture.VirtualKey);
        var prefix = input[..input.LastIndexOf('+')];
        Assert.Equal($"{prefix}+{expectedDisplayName}", HotkeyGesture.Format(gesture));
    }

    [Fact]
    public void KeyDisplayName_NamedKeyMatrix()
    {
        Assert.Equal("0", HotkeyGesture.KeyDisplayName(0x30));
        Assert.Equal("9", HotkeyGesture.KeyDisplayName(0x39));
        Assert.Equal("A", HotkeyGesture.KeyDisplayName(0x41));
        Assert.Equal("Z", HotkeyGesture.KeyDisplayName(0x5A));
        Assert.Equal("Num0", HotkeyGesture.KeyDisplayName(0x60));
        Assert.Equal("Num9", HotkeyGesture.KeyDisplayName(0x69));
        Assert.Equal("F1", HotkeyGesture.KeyDisplayName(0x70));
        Assert.Equal("F12", HotkeyGesture.KeyDisplayName(0x7B));
        Assert.Equal("Space", HotkeyGesture.KeyDisplayName(0x20));
        Assert.Equal("PageUp", HotkeyGesture.KeyDisplayName(0x21));
        Assert.Equal("PageDown", HotkeyGesture.KeyDisplayName(0x22));
        Assert.Equal("End", HotkeyGesture.KeyDisplayName(0x23));
        Assert.Equal("Home", HotkeyGesture.KeyDisplayName(0x24));
        Assert.Equal("Left", HotkeyGesture.KeyDisplayName(0x25));
        Assert.Equal("Up", HotkeyGesture.KeyDisplayName(0x26));
        Assert.Equal("Right", HotkeyGesture.KeyDisplayName(0x27));
        Assert.Equal("Down", HotkeyGesture.KeyDisplayName(0x28));
        Assert.Equal("Insert", HotkeyGesture.KeyDisplayName(0x2D));
        Assert.Equal("Delete", HotkeyGesture.KeyDisplayName(0x2E));
    }

    [Theory]
    [InlineData(0)]       // VK_NONE
    [InlineData(0xBA)]    // VK_OEM_1 (';') — not offered
    [InlineData(0x7C)]    // F13 — beyond the offered range
    [InlineData(-1)]
    public void KeyDisplayName_UnsupportedVK_ReturnsEmpty(int virtualKey)
    {
        Assert.Equal(string.Empty, HotkeyGesture.KeyDisplayName(virtualKey));
        Assert.False(HotkeyGesture.IsSupportedVirtualKey(virtualKey));
    }

    [Fact]
    public void Format_DefaultCombo_IsEmpty()
    {
        Assert.Equal(string.Empty, HotkeyGesture.Format(default));
    }

    // ==================================================================
    // Rejections — no modifier, two keys, unknown keys, malformed input
    // ==================================================================

    [Theory]
    [InlineData("A")]                // bare letter key
    [InlineData("9")]                // bare digit
    [InlineData("F5")]               // bare function key
    [InlineData("Num3")]             // bare numpad key
    [InlineData("Space")]            // bare named key
    [InlineData("Ctrl")]             // single modifier, no key
    [InlineData("Ctrl+Alt")]         // modifiers only
    [InlineData("Ctrl+Shift+Win")]   // all modifiers, still key-less
    [InlineData("Ctrl+Ctrl+A")]      // duplicate modifier
    [InlineData("Ctrl+Control+A")]   // duplicate modifier via alias
    [InlineData("Shift+Shift+F1")]   // duplicate shift
    [InlineData("Win+Windows+P")]    // duplicate win via alias
    [InlineData("Ctrl+Alt+1+2")]     // two keys
    [InlineData("1+2")]              // two keys, no modifier
    [InlineData("A+B")]              // two keys, no modifier
    [InlineData("A+B+Ctrl")]         // two keys regardless of position
    [InlineData("Ctrl+Foo")]         // unknown key name
    [InlineData("Ctrl+F0")]          // F0 does not exist
    [InlineData("Ctrl+F13")]         // beyond F12
    [InlineData("Ctrl+F99")]         // far beyond F12
    [InlineData("Ctrl+Num")]         // truncated numpad name
    [InlineData("Ctrl+NumX")]        // numpad with non-digit
    [InlineData("Ctrl+Numpad")]      // truncated long numpad name
    [InlineData("Ctrl+NumpadX")]
    [InlineData("Ctrl+é")]           // non-ASCII single character
    [InlineData("Ctrl+AB")]          // two-letter key that matches no name
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryParse_RejectionMatrix(string? input)
    {
        Assert.False(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Equal(default(HotkeyCombo), gesture);
    }
}
