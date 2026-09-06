using System.Windows.Controls;

namespace VyralBot.UI.Views;

/// <summary>
/// Interaction logic for GiveawayView.xaml
/// </summary>
public partial class GiveawayView : UserControl
{
    public GiveawayView()
    {
        InitializeComponent();
    }

    // NOTE: GiveawayViewModel.InitializeAsync is called by MainViewModel on
    // navigation (the dead UserControl_Loaded handler that duplicated it was
    // removed — no Loaded= is wired in GiveawayView.xaml).
}
