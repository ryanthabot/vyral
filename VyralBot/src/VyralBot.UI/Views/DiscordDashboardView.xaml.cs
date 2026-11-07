using System;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Discord dashboard (DASHBOARD tab of the Bots page): KPI strip, bot fleet,
/// servers/active-channel picker, connection card, recent mod actions and
/// workflow runs today. The host (DiscordBotsView) swaps this tab in and out
/// by toggling its visibility, so the 30s auto-refresh timer is driven by
/// <see cref="UIElement.IsVisibleChanged"/> (Loaded/Unloaded alone would miss
/// the tab toggle — a collapsed element stays loaded).
/// </summary>
public partial class DiscordDashboardView : UserControl
{
    public DiscordDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DiscordDashboardViewModel viewModel)
            return;

        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing Discord dashboard: {ex.Message}");
        }

        // Loaded implies visible; the IsVisibleChanged handler is a no-op then.
        viewModel.StartAutoRefresh();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is not DiscordDashboardViewModel viewModel)
            return;

        if (IsVisible)
            viewModel.StartAutoRefresh();
        else
            viewModel.StopAutoRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscordDashboardViewModel viewModel)
            viewModel.StopAutoRefresh();

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        IsVisibleChanged -= OnIsVisibleChanged;
    }
}
