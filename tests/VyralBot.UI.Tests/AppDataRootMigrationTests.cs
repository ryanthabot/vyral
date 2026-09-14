using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services;
using Xunit;

namespace VyralBot.UI.Tests;

public class AppDataRootMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _legacy;
    private readonly string _legacy2;

    public AppDataRootMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"vyral-root-{Guid.NewGuid():N}");
        _legacy = Path.Combine(Path.GetTempPath(), $"vyral-legacy-{Guid.NewGuid():N}");
        _legacy2 = Path.Combine(Path.GetTempPath(), $"vyral-legacy2-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _legacy, _legacy2 })
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Migrate_CopiesProfilesAndCurrentProfile_FromLegacyLocation()
    {
        var legacyProfile = Path.Combine(_legacy, "profiles", "MainProfile");
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(legacyProfile, "profile.json"), "{}");
        File.WriteAllText(Path.Combine(_legacy, "current_profile.txt"), "MainProfile");

        var adopted = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.Equal(_legacy, adopted);
        Assert.True(File.Exists(Path.Combine(_root, "profiles", "MainProfile", "profile.json")));
        Assert.Equal("MainProfile",
            File.ReadAllText(Path.Combine(_root, "current_profile.txt")).Trim());
    }

    [Fact]
    public void Migrate_NeverTouchesANonEmptyRoot()
    {
        var existingProfile = Path.Combine(_root, "profiles", "Existing");
        Directory.CreateDirectory(existingProfile);
        File.WriteAllText(Path.Combine(existingProfile, "keep.txt"), "data");

        var legacyProfile = Path.Combine(_legacy, "profiles", "Legacy");
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(legacyProfile, "profile.json"), "{}");

        var adopted = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.Null(adopted);
        Assert.True(File.Exists(Path.Combine(existingProfile, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, "profiles", "Legacy")));
    }

    [Fact]
    public void Migrate_SkipsCurrentProfile_WhenItsProfileDidNotMove()
    {
        // Legacy says the active profile is "Gone" but only "Other" migrates
        // cleanly — a dangling current_profile.txt must not be copied.
        var legacyProfile = Path.Combine(_legacy, "profiles", "Other");
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(legacyProfile, "profile.json"), "{}");
        File.WriteAllText(Path.Combine(_legacy, "current_profile.txt"), "Gone");

        var adopted = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.NotNull(adopted);
        Assert.True(File.Exists(Path.Combine(_root, "profiles", "Other", "profile.json")));
        Assert.False(File.Exists(Path.Combine(_root, "current_profile.txt")));
    }

    [Fact]
    public void Migrate_IsNoOp_WhenNoLegacyLocationHasProfiles()
    {
        var adopted = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.Null(adopted);
        Assert.False(Directory.Exists(Path.Combine(_root, "profiles")));
    }

    [Fact]
    public void Migrate_PrefersTheMostRecentlyUsedLegacyLocation()
    {
        // Two folders an exe was run from at different times: _legacy2 is the
        // newer one and must win even though _legacy is listed first.
        var older = Path.Combine(_legacy, "profiles", "Old");
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(older, "profile.json"), "{}");
        Directory.SetLastWriteTimeUtc(Path.Combine(_legacy, "profiles"), DateTime.UtcNow.AddDays(-5));

        var newer = Path.Combine(_legacy2, "profiles", "New");
        Directory.CreateDirectory(newer);
        File.WriteAllText(Path.Combine(newer, "profile.json"), "{}");

        var adopted = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy, _legacy2 });

        Assert.Equal(_legacy2, adopted);
        Assert.True(File.Exists(Path.Combine(_root, "profiles", "New", "profile.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "profiles", "Old")));
    }
}
