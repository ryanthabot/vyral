using System.IO;
using VyralBot.Integrations.Obs;
using Xunit;

namespace VyralBot.UI.Tests;

public class ObsSceneCollectionDocumentTests
{
    private static string Sample() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "obs-scene-collection.sample.json"));

    [Fact]
    public void Parse_Sample_MapsScenesOrderedBySceneOrder()
    {
        var doc = ObsSceneCollectionDocument.Parse(Sample());
        Assert.Equal(new[] { "Starting Soon", "Just Chatting" }, doc.Scenes.Select(s => s.Name).ToArray());
        Assert.Equal("Starting Soon", doc.CurrentScene);
        Assert.Equal("Fade", doc.CurrentTransition);
        Assert.Equal("1920x1080", doc.Resolution);
        Assert.Equal(5, doc.SourceCount);
    }

    [Fact]
    public void Parse_Sample_MapsSceneItemsAndNonSceneSources()
    {
        var doc = ObsSceneCollectionDocument.Parse(Sample());
        var jc = doc.Scenes.Single(s => s.Name == "Just Chatting");
        Assert.Equal(2, jc.Items.Count);
        var overlay = jc.Items.Single(i => i.SourceName == "Chat Overlay");
        Assert.False(overlay.Locked);
        Assert.Equal(12.5, overlay.Rotation);
        Assert.Equal(1400.0, overlay.PosX);
        Assert.Equal(0.85, overlay.ScaleX);
        Assert.Equal(2, overlay.BoundsType);
        Assert.Contains(doc.NonSceneSources, s => s.Id == "browser_source" && s.Name == "Chat Overlay" && !s.Muted && s.Volume == 0.75);
        Assert.Equal(2, doc.AudioDevices.Count);
    }

    [Fact]
    public void Parse_ValidJsonWithoutSourcesArray_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => ObsSceneCollectionDocument.Parse("""{"name":"nope"}"""));
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsInvalidDataWithReason()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ObsSceneCollectionDocument.Parse("{ not json"));
        Assert.Contains("not a valid OBS scene collection", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
