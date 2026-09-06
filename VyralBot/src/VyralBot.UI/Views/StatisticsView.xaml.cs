using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView()
    {
        InitializeComponent();

        // DataContext is set by the DataTemplate binding to StatisticsViewModel.
        // NOTE: no Loaded-time InitializeAsync here — MainViewModel already calls
        // StatisticsViewModel.InitializeAsync() during navigation; loading again
        // here would double-run the (10k-row) statistics queries on every visit.
    }
}
