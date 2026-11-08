using System.Windows.Controls;
using System.Windows.Input;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class SoundsView : UserControl
{
    public SoundsView()
    {
        InitializeComponent();

        // DataContext is set by the DataTemplate binding to SoundsViewModel
        Loaded += async (s, e) =>
        {
            if (DataContext is SoundsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }

    /// <summary>
    /// Hotkey capture sink (KI-55). Tunnels here before any child handles the
    /// key, so the capture consumes it first. While Alt is held WPF reports
    /// Key.System — the real key is in SystemKey, so resolve it here.
    /// </summary>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SoundsViewModel viewModel)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Mark handled synchronously while a capture is active: the await
        // below would otherwise let Space/Enter re-click the focused capture
        // button before the capture resolves.
        var captureActive = viewModel.CapturingSoundName != null;
        if (captureActive)
        {
            e.Handled = true;
        }

        if (await viewModel.HandleCaptureKeyDownAsync(key, e.KeyboardDevice.Modifiers) && !captureActive)
        {
            e.Handled = true;
        }
    }
}

