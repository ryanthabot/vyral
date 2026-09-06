using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class EventHistoryView : UserControl
{
    public EventHistoryView()
    {
        InitializeComponent();
        
        // DataContext is set by the DataTemplate binding to EventHistoryViewModel
        Loaded += async (s, e) =>
        {
            if (DataContext is EventHistoryViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }

    private void OnFilterDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is EventHistoryViewModel viewModel)
        {
            viewModel.SearchCommand.Execute(null);
        }
    }
}
