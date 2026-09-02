using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class KickSettingsView : UserControl
{
        public KickSettingsView()
        {
            InitializeComponent();
        }

    private void WebhookSettings_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Note: User will need to manually navigate to Settings -> API & Webhooks
        // A full navigation implementation would require MainWindow changes
        e.Handled = true;
    }
}
