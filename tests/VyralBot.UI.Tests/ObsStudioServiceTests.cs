using VyralBot.Integrations.Obs.Engine;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Embedded-OBS studio service, pure parts only: the scene_order reordering
/// applied to obs_enum_scenes output (which walks creation order and loses
/// the collection's order). No test here touches obs.dll.
/// </summary>
public class ObsStudioServiceTests
{
    private static List<ObsSceneInfo> Scenes(params string[] names) =>
        names.Select(name => new ObsSceneInfo(name)).ToList();

    [Fact]
    public void OrderScenes_CreationOrder_ReorderedToCollectionSceneOrder()
    {
        // Scenes come back from obs_enum_scenes in creation order; the
        // collection's scene_order is what OBS Studio shows the user.
        var enumerated = Scenes("Starting Soon", "Live", "BRB");

        var ordered = ObsStudioService.OrderScenes(enumerated, ["Live", "BRB", "Starting Soon"]);

        Assert.Equal(["Live", "BRB", "Starting Soon"], ordered.Select(s => s.Name));
    }

    [Fact]
    public void OrderScenes_SceneMissingFromOrder_KeepsEnumerationOrderAfterRankedOnes()
    {
        var enumerated = Scenes("A", "Wildcard", "B", "C");

        var ordered = ObsStudioService.OrderScenes(enumerated, ["C", "A"]);

        Assert.Equal(["C", "A", "Wildcard", "B"], ordered.Select(s => s.Name));
    }

    [Fact]
    public void OrderScenes_EmptyOrder_KeepsEnumerationOrder()
    {
        var enumerated = Scenes("Second", "First");

        var ordered = ObsStudioService.OrderScenes(enumerated, []);

        Assert.Equal(["Second", "First"], ordered.Select(s => s.Name));
    }

    [Fact]
    public void OrderScenes_NameMatchIsCaseSensitive_LikeObsSceneNames()
    {
        var enumerated = Scenes("game", "Game");

        // "Game" (capital) is in scene_order; "game" is not — it must NOT
        // borrow "Game"'s rank and instead stay an unmatched scene.
        var ordered = ObsStudioService.OrderScenes(enumerated, ["Game"]);

        Assert.Equal(["Game", "game"], ordered.Select(s => s.Name));
    }

    [Fact]
    public void OrderScenes_DuplicateEntryInOrder_FirstOccurrenceWins()
    {
        var enumerated = Scenes("X", "Y");

        var ordered = ObsStudioService.OrderScenes(enumerated, ["Y", "X", "Y"]);

        Assert.Equal(["Y", "X"], ordered.Select(s => s.Name));
    }
}
