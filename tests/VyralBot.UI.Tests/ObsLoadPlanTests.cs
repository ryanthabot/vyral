using System.IO;
using System.Text.Json;
using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

public class ObsLoadPlanTests
{
    private static string Sample() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "obs-scene-collection.sample.json"));

    private static string Filters() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "obs-scene-collection.filters.json"));

    [Fact]
    public void Build_Sample_MapsSourcesInFileOrder()
    {
        var plan = ObsLoadPlanBuilder.Build(Sample());
        Assert.Equal(5, plan.Sources.Count);
        Assert.Equal(
            new[] { "Display Capture", "Chat Overlay", "Just Chatting", "Starting Soon", "Desktop Audio" },
            plan.Sources.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Build_Sample_SerializesSettingsRoundTrip()
    {
        var plan = ObsLoadPlanBuilder.Build(Sample());
        var overlay = plan.Sources.Single(s => s.Name == "Chat Overlay");
        Assert.Equal("browser_source", overlay.Id);
        Assert.Contains("audio.html", overlay.SettingsJson);
        using var settings = JsonDocument.Parse(overlay.SettingsJson);
        Assert.Equal(1920, settings.RootElement.GetProperty("width").GetInt32());
        Assert.Equal("{}", overlay.HotkeysJson);
    }

    [Fact]
    public void Build_Sample_MapsVolumeMutedAndMixers()
    {
        var plan = ObsLoadPlanBuilder.Build(Sample());
        var overlay = plan.Sources.Single(s => s.Name == "Chat Overlay");
        Assert.Equal(0.75f, overlay.Volume);
        Assert.Equal(2u, overlay.Mixers);
        Assert.Equal(1.0f, plan.Sources.Single(s => s.Name == "Display Capture").Volume);

        // Absent scalar fields stay null so the executor skips the native call
        // and libobs applies its own defaults.
        var absent = ObsLoadPlanBuilder.Build("""{"sources":[{"name":"x","id":"fake_source"}]}""");
        Assert.Null(absent.Sources[0].Volume);
        Assert.Null(absent.Sources[0].Muted);
        Assert.Null(absent.Sources[0].Mixers);
    }

    [Fact]
    public void Build_Sample_MapsCurrentSceneAndSceneOrder()
    {
        var plan = ObsLoadPlanBuilder.Build(Sample());
        Assert.Equal("Starting Soon", plan.CurrentSceneName);
        Assert.Equal(new[] { "Starting Soon", "Just Chatting" }, plan.SceneOrder.ToArray());
    }

    [Fact]
    public void Build_Sample_MapsAudioDevices()
    {
        var plan = ObsLoadPlanBuilder.Build(Sample());
        Assert.Equal(2, plan.AudioDevices.Count);
        var desktop = plan.AudioDevices.Single(d => d.ChannelKey == "Desktop1");
        Assert.Equal("Desktop Audio", desktop.Source.Name);
        Assert.Contains("default", desktop.Source.SettingsJson);
        Assert.Equal("Aux1", plan.AudioDevices[1].ChannelKey);
        Assert.Equal("wasapi_input_capture", plan.AudioDevices[1].Source.Id);
    }

    [Fact]
    public void Build_Sample_SceneSourceCarriesItemsInsideSettings()
    {
        var plan = ObsLoadPlanBuilder.Build(Sample());
        var scene = plan.Sources.Single(s => s.Name == "Just Chatting");
        Assert.Equal("scene", scene.Id);
        Assert.Contains("\"items\"", scene.SettingsJson);
    }

    [Fact]
    public void Build_FiltersFixture_ParsesFilterPlans()
    {
        var plan = ObsLoadPlanBuilder.Build(Filters());
        var filters = plan.Sources.Single(s => s.Name == "Display Capture").Filters;
        Assert.Equal(2, filters.Count);
        Assert.Equal("color_filter_v2", filters[0].Id);
        Assert.Equal("Tint", filters[0].Name);
        Assert.True(filters[0].Enabled);
        Assert.Contains("4294956800", filters[0].SettingsJson);
        using var tint = JsonDocument.Parse(filters[0].SettingsJson);
        Assert.Equal(4294956800.0, tint.RootElement.GetProperty("color").GetDouble());
        Assert.Equal("mask_filter", filters[1].Id);
        Assert.Equal("Blur", filters[1].Name);
        Assert.False(filters[1].Enabled);
    }

    [Fact]
    public void Build_MalformedJson_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => ObsLoadPlanBuilder.Build("{ not json"));
    }

    [Fact]
    public void Build_JsonWithoutSourcesArray_ThrowsInvalidDataMentioningCollection()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ObsLoadPlanBuilder.Build("""{"name":"nope"}"""));
        Assert.Contains("scene collection", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
