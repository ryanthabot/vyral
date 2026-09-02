using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView()
    {
        InitializeComponent();
        
        // DataContext is set by the DataTemplate binding to StatisticsViewModel
        Loaded += async (s, e) =>
        {
            if (DataContext is StatisticsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }
}
