using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Viewer Hub settings panel (Settings → Viewer Hub). Runs the pending
/// link-codes refresh only while this panel is visible so a hidden tab
/// costs nothing.
/// </summary>
public partial class ViewerHubView : UserControl
{
    // Parameterless constructor for XAML/DataTemplate instantiation
    // (DataContext arrives from the parent ContentControl binding).
    public ViewerHubView()
    {
        InitializeComponent();

        Loaded += (_, _) => StartIfVisible();
        Unloaded += (_, _) => StopIfNeeded();

        // Category switches inside Settings collapse this panel without
        // unloading it — pause the refresh timer in that case too.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                StartIfVisible();
            }
            else
            {
                StopIfNeeded();
            }
        };
    }

    private void StartIfVisible()
    {
        if (DataContext is ViewerHubViewModel viewModel && IsVisible)
        {
            viewModel.StartCodeRefresh();
        }
    }

    private void StopIfNeeded()
    {
        if (DataContext is ViewerHubViewModel viewModel)
        {
            viewModel.StopCodeRefresh();
        }
    }
}
