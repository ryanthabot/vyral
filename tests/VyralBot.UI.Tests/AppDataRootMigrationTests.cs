using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services;
using Xunit;

namespace VyralBot.UI.Tests;

public class AppDataRootMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _legacy;

    public AppDataRootMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"vyral-root-{Guid.NewGuid():N}");
        _legacy = Path.Combine(Path.GetTempPath(), $"vyral-legacy-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_legacy, recursive: true); } catch { }
    }

    [Fact]
    public void Migrate_CopiesProfilesAndCurrentProfile_FromLegacyLocation()
    {
        var legacyProfile = Path.Combine(_legacy, "profiles", "MainProfile");
        Directory.CreateDirectory(legacyProfile);
        File.WriteAllText(Path.Combine(legacyProfile, "profile.json"), "{}");
        File.WriteAllText(Path.Combine(_legacy, "current_profile.txt"), "MainProfile");

        var ran = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.True(ran);
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

        var ran = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.False(ran);
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

        var ran = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.True(ran);
        Assert.True(File.Exists(Path.Combine(_root, "profiles", "Other", "profile.json")));
        Assert.False(File.Exists(Path.Combine(_root, "current_profile.txt")));
    }

    [Fact]
    public void Migrate_IsNoOp_WhenNoLegacyLocationHasProfiles()
    {
        var ran = AppDataRoot.MigrateLegacyData(
            NullLogger.Instance, _root, new[] { _legacy });

        Assert.False(ran);
        Assert.False(Directory.Exists(Path.Combine(_root, "profiles")));
    }
}
