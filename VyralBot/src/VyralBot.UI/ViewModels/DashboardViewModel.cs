using CommunityToolkit.Mvvm.ComponentModel;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Dashboard ViewModel showing overview and statistics.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _activeTriggerCount;

    [ObservableProperty]
    private int _totalActionCount;

    [ObservableProperty]
    private int _connectedPlatformCount;

    [ObservableProperty]
    private string _status = "Ready";

    public DashboardViewModel()
    {
        LoadDashboard();
    }

    private void LoadDashboard()
    {
        // TODO: Load actual data from services
        ActiveTriggerCount = 0;
        TotalActionCount = 0;
        ConnectedPlatformCount = 0;
    }
}
