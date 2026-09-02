using VyralBot.UI.Models;
using Xunit;

namespace VyralBot.UI.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void Has_nine_builtin_themes() => Assert.Equal(9, ThemeCatalog.BuiltInThemes.Count);

    [Fact]
    public void Theme_ids_are_unique()
        => Assert.Equal(ThemeCatalog.BuiltInThemes.Count, ThemeCatalog.BuiltInThemes.Select(t => t.Id).Distinct().Count());

    [Fact]
    public void Contains_required_themes()
    {
        var ids = ThemeCatalog.BuiltInThemes.Select(t => t.Id).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "native", "light", "dark", "crimson", "azure", "emerald", "frost", "carbon", "ember",
        }, ids);
    }

    [Fact]
    public void Find_returns_null_for_unknown() => Assert.Null(ThemeCatalog.Find("does-not-exist"));

    [Fact]
    public void Find_is_case_insensitive()
    {
        Assert.NotNull(ThemeCatalog.Find("CRIMSON"));
        Assert.Equal("crimson", ThemeCatalog.Find("CRIMSON")!.Id);
    }

    [Fact]
    public void Find_returns_custom_option_for_custom_id()
    {
        var custom = ThemeCatalog.Find("custom");
        Assert.NotNull(custom);
        Assert.False(custom!.IsBuiltIn);
    }

    [Fact]
    public void Logo_variants_are_the_promised_set()
        => Assert.Superset(new HashSet<string> { "native", "red", "blue", "green", "white", "black" },
            ThemeCatalog.ValidLogoVariants.ToHashSet());

    [Fact]
    public void Every_builtin_has_a_valid_logo_variant()
        => Assert.All(ThemeCatalog.BuiltInThemes, t => Assert.Contains(t.LogoVariant, ThemeCatalog.ValidLogoVariants));

    [Fact]
    public void Every_builtin_accent_hex_is_parseable()
        => Assert.All(ThemeCatalog.BuiltInThemes, t => Assert.True(ColorShadeGenerator.TryParseHex(t.AccentHex, out _)));
}
