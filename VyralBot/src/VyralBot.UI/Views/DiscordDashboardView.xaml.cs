using System;
using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Discord dashboard page: bot connection status, servers the bot is in,
/// active-channel picker, and a test-message button. No WebView2 needed here.
/// </summary>
public partial class DiscordDashboardView : UserControl
{
    public DiscordDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscordDashboardViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing Discord dashboard: {ex.Message}");
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
