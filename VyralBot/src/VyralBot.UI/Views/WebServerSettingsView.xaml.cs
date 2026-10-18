using System.Diagnostics;
using System.Windows;
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
                _ = InitializeTokenBoxAsync(viewModel);
            }
        };
    }

    // Constructor with ViewModel injection (for standalone usage)
    public WebServerSettingsView(WebServerSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Initialize the view model
        _ = InitializeTokenBoxAsync(viewModel);
    }

    private async System.Threading.Tasks.Task InitializeTokenBoxAsync(WebServerSettingsViewModel viewModel)
    {
        await viewModel.InitializeAsync();

        // The ngrok auth token is masked in a PasswordBox (not bindable);
        // populate the box from the value the view model loaded. Setting
        // Password fires PasswordChanged, which writes the same value back.
        NgrokAuthTokenBox.Password = viewModel.NgrokAuthToken ?? "";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void NgrokAuthTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The ngrok auth token is masked like the other platform secrets — feed
        // it to the view model manually because PasswordBox.Password is not
        // bindable. The save flow keeps reading the NgrokAuthToken property.
        if (DataContext is WebServerSettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.NgrokAuthToken = passwordBox.Password;
        }
    }
}
