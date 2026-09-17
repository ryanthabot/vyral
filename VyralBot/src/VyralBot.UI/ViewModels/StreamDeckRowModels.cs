using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VyralBot.UI.Services.VyralDeck;

namespace VyralBot.UI.ViewModels;

/// <summary>WPF wrapper for one deck button slot (grid cell).</summary>
public partial class DeckButtonViewModel : ObservableObject
{
    private DeckButton _model;

    public DeckButtonViewModel(DeckButton model, int slot)
    {
        _model = model;
        Slot = slot;
    }

    public int Slot { get; }

    public string ButtonId => _model.Id;

    public DeckButton Model => _model;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _toolTipText = "Empty slot";

    [ObservableProperty]
    private Brush _buttonBrush = MakeBrush("#21262D");

    [ObservableProperty]
    private double _opacity = 0.45;

    /// <summary>Re-reads the wrapped model into bindable properties.</summary>
    public void RefreshFrom(DeckButton model)
    {
        _model = model;
        Label = model.Label;
        Icon = model.Icon;
        IsEmpty = model.IsEmpty;
        ToolTipText = model.IsEmpty ? "Empty slot — Edit Mode → tap to configure" : DeckActionCatalog.Describe(model);
        ButtonBrush = MakeBrush(model.IsEmpty ? "#21262D" : model.ColorHex);
        Opacity = model.IsEmpty ? 0.45 : 1.0;
    }

    private static Brush MakeBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            var fallback = new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2D));
            fallback.Freeze();
            return fallback;
        }
    }
}

/// <summary>WPF wrapper for one deck page (a chip in the page row + its grid).</summary>
public partial class DeckPageViewModel : ObservableObject
{
    private readonly DeckPage _model;

    public DeckPageViewModel(DeckPage model)
    {
        _model = model;
        Name = model.Name;
        Buttons = new ObservableCollection<DeckButtonViewModel>(
            model.Buttons.Select((b, i) => new DeckButtonViewModel(b, i)));
    }

    public string PageId => _model.Id;

    public DeckPage Model => _model;

    [ObservableProperty]
    private string _name = "Page 1";

    public ObservableCollection<DeckButtonViewModel> Buttons { get; }

    public void RefreshFrom(DeckPage model)
    {
        Name = model.Name;
        for (var i = 0; i < Buttons.Count && i < model.Buttons.Count; i++)
        {
            Buttons[i].RefreshFrom(model.Buttons[i]);
        }
    }
}
