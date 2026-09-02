using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class WebServerSettingsView : UserControl
{
    // Parameterless constructor for XAML instantiation (e.g., when used in Settings via ContentControl)
    public WebServerSettingsView()
    {
        InitializeComponent();
        
        // DataContext will be set by parent ContentControl binding
        Loaded += (s, e) =>
        {
            if (DataContext is WebServerSettingsViewModel viewModel)
            {
                _ = viewModel.InitializeAsync();
            }
        };
    }
    
    // Constructor with ViewModel injection (for standalone usage)
    public WebServerSettingsView(WebServerSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // Initialize the view model
        _ = viewModel.InitializeAsync();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
