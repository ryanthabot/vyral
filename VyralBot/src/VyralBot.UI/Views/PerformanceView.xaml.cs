using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Live performance monitor panel for Settings → Performance. The refresh
/// timer only runs while this view is loaded so a closed tab costs nothing.
/// </summary>
public partial class PerformanceView : UserControl
{
    // Parameterless constructor for XAML/DataTemplate instantiation
    // (DataContext arrives from the parent ContentControl binding).
    public PerformanceView()
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
        if (DataContext is PerformanceViewModel viewModel && IsVisible)
        {
            viewModel.Start();
        }
    }

    private void StopIfNeeded()
    {
        if (DataContext is PerformanceViewModel viewModel)
        {
            viewModel.Stop();
        }
    }
}
