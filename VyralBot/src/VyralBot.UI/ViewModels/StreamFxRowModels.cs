using CommunityToolkit.Mvvm.ComponentModel;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// One Hue light row in Settings → Stream FX: Enabled marks the light as a
/// flash target for the !flashlights command.
/// </summary>
public partial class HueLightRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Reachable { get; init; }

    [ObservableProperty]
    private bool _enabled;
}

/// <summary>One saved Stream Deck action link row in Settings → Stream FX.</summary>
public partial class DeckActionRow : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Uri { get; set; } = "";
}
