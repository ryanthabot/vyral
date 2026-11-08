using VyralBot.UI.Services;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-6 regression coverage for the portable flow-document round-trip
/// (<see cref="FlowSnapshotService"/>): the wave-4 export fix made top-level
/// <c>description</c>/<c>enabled</c> and the trigger's
/// <c>minimumTier</c>/<c>platforms</c> survive
/// <see cref="FlowSnapshotService.BuildDocument"/> →
/// <see cref="FlowSnapshotService.ParseDocument"/>, so a shared command or a
/// re-imported <c>.flow.json</c> keeps its metadata (previously a shared
/// <c>!mod</c> command silently fell back to tier "Mod" and re-imported flows
/// lost their description/enabled flag).
/// </summary>
public class FlowSnapshotRoundTripWave5Tests
{
    private static FlowEditorSnapshotDto RoundTrip(FlowEditorSnapshotDto snapshot)
    {
        var json = FlowSnapshotService.BuildDocument(snapshot);
        var parsed = FlowSnapshotService.ParseDocument(json);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void RoundTrip_PreservesDescription_TrimsWhitespace()
    {
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f1",
            Name = "Purge flow",
            Description = "  Removes spam messages in waves.  "
        });

        Assert.Equal("Removes spam messages in waves.", parsed.Description);
    }

    [Fact]
    public void RoundTrip_PreservesEnabledFalse()
    {
        // Enabled defaults to true on the DTO, so a dropped "enabled" flag
        // would silently re-enable a disabled export — assert the false case.
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f2",
            Name = "Disabled flow",
            Description = "paused",
            Enabled = false
        });

        Assert.False(parsed.Enabled);
    }

    [Fact]
    public void RoundTrip_PreservesEnabledTrue()
    {
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f3",
            Name = "Enabled flow",
            Enabled = true
        });

        Assert.True(parsed.Enabled);
    }

    [Fact]
    public void RoundTrip_PreservesBlankDescription_AsNull()
    {
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f4",
            Name = "No description",
            Description = "   "
        });

        Assert.Null(parsed.Description);
    }

    [Fact]
    public void RoundTrip_PreservesTriggerMinimumTier()
    {
        var snapshot = new FlowEditorSnapshotDto
        {
            Id = "f5",
            Name = "Mod command",
            Trigger = new FlowTriggerDto
            {
                Type = "mod_command",
                CommandName = "!purge",
                MinimumTier = "  Admin  "
            }
        };

        var parsed = RoundTrip(snapshot);

        Assert.NotNull(parsed.Trigger);
        Assert.Equal("Admin", parsed.Trigger!.MinimumTier);
    }

    [Fact]
    public void RoundTrip_BlankMinimumTier_NormalizesToNull()
    {
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f6",
            Name = "Non-mod command",
            Trigger = new FlowTriggerDto { Type = "command", CommandName = "!socials", MinimumTier = "   " }
        });

        Assert.Null(parsed.Trigger!.MinimumTier);
    }

    [Fact]
    public void RoundTrip_PreservesTriggerPlatforms()
    {
        var snapshot = new FlowEditorSnapshotDto
        {
            Id = "f7",
            Name = "Scoped command",
            Trigger = new FlowTriggerDto
            {
                Type = "command",
                CommandName = "!discord",
                Platforms = new List<string> { "Twitch", "Kick", "Discord" }
            }
        };

        var json = FlowSnapshotService.BuildDocument(snapshot);
        Assert.Contains("\"platforms\"", json, StringComparison.Ordinal);

        var parsed = FlowSnapshotService.ParseDocument(json);
        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.Trigger!.Platforms);
        Assert.Equal(new List<string> { "Twitch", "Kick", "Discord" }, parsed.Trigger!.Platforms!);
    }

    [Fact]
    public void RoundTrip_EmptyPlatformsList_NormalizesToNull_AllPlatforms()
    {
        // None toggled on = run on every platform; the document stores null,
        // not an empty list, so the import side sees "all platforms".
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f8",
            Name = "All platforms",
            Trigger = new FlowTriggerDto { Type = "command", CommandName = "!hi", Platforms = new List<string>() }
        });

        Assert.Null(parsed.Trigger!.Platforms);
    }

    [Fact]
    public void RoundTrip_CarriesDescriptionEnabledMinimumTierAndPlatforms_Together()
    {
        // The full wave-4/wave-5 metadata set on one document — the way a real
        // marketplace sharecode arrives.
        var parsed = RoundTrip(new FlowEditorSnapshotDto
        {
            Id = "f9",
            Name = "Sharecode flow",
            Description = "Shared mod purge",
            Enabled = false,
            Trigger = new FlowTriggerDto
            {
                Type = "mod_command",
                CommandName = "!slow",
                MinimumTier = "Mod",
                Platforms = new List<string> { "Twitch" },
                Description = "Sets slow mode"
            }
        });

        Assert.Equal("Shared mod purge", parsed.Description);
        Assert.False(parsed.Enabled);
        Assert.Equal("Mod", parsed.Trigger!.MinimumTier);
        Assert.Equal(new List<string> { "Twitch" }, parsed.Trigger!.Platforms!);
        Assert.Equal("Sets slow mode", parsed.Trigger!.Description);
    }

    [Fact]
    public void ParseDocument_MalformedJson_ReturnsNull()
    {
        Assert.Null(FlowSnapshotService.ParseDocument("{ not a flow document"));
    }
}
