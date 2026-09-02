using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class SoundsView : UserControl
{
    public SoundsView()
    {
        InitializeComponent();
        
        // DataContext is set by the DataTemplate binding to SoundsViewModel
        Loaded += async (s, e) =>
        {
            if (DataContext is SoundsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }
}
