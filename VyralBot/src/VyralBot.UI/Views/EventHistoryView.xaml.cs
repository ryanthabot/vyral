using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class EventHistoryView : UserControl
{
    public EventHistoryView()
    {
        InitializeComponent();

        // NOTE: InitializeAsync is invoked by MainViewModel's NavigateToEventHistory
        // before the view is shown — do NOT call it again on Loaded (double load).
    }

    private void OnFilterDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is EventHistoryViewModel viewModel)
        {
            viewModel.SearchCommand.Execute(null);
        }
    }
}
