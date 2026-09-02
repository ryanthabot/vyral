using System.Windows.Controls;
using VyralBot.UI.ViewModels;

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

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is GiveawayViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
