using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VyralBot.UI.Services;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Dashboard ViewModel showing overview and statistics. Also owns the
/// first-run Home "Get started" strip state (dismiss persists per app,
/// visibility additionally collapses once any platform connects —
/// the connection flags live on MainViewModel and are bound in XAML).
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly UiPreferencesService _uiPreferences;

    [ObservableProperty]
    private int _activeTriggerCount;

    [ObservableProperty]
    private int _totalActionCount;

    [ObservableProperty]
    private int _connectedPlatformCount;

    [ObservableProperty]
    private string _status = "Ready";

    /// <summary>True once the user dismissed the Home "Get started" strip.</summary>
    [ObservableProperty]
    private bool _isGettingStartedDismissed;

    public DashboardViewModel(UiPreferencesService uiPreferences)
    {
        _uiPreferences = uiPreferences;
        IsGettingStartedDismissed = uiPreferences.Load().HomeGettingStartedDismissed;
        LoadDashboard();
    }

    [RelayCommand]
    private void DismissGettingStarted()
    {
        IsGettingStartedDismissed = true;
        try
        {
            // Load fresh so a dismiss never clobbers theme/shade prefs
            // saved elsewhere during this session.
            var preferences = _uiPreferences.Load();
            preferences.HomeGettingStartedDismissed = true;
            _uiPreferences.Save(preferences);
        }
        catch (Exception)
        {
            // Best-effort persistence; the strip stays hidden for this session.
        }
    }

    private void LoadDashboard()
    {
        // TODO: Load actual data from services
        ActiveTriggerCount = 0;
        TotalActionCount = 0;
        ConnectedPlatformCount = 0;
    }
}
