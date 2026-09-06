using System.Windows;
using System.Windows.Input;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Admin viewer-profile window: identity, linked accounts (with per-account
/// unlink), platform status, economy, activity, Vyral goals, giveaway entries,
/// perks and event statistics for one viewer. Borderless chrome consistent
/// with <see cref="ConfirmDialog"/>; draggable via its title bar and resizable.
/// </summary>
public partial class ViewerProfileWindow : Window
{
    public ViewerProfileWindow(ViewerProfileViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove throws when the mouse button is no longer pressed — safe to ignore.
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
