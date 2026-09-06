using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
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
        // Navigate to the app's Settings page (API & Webhooks section) instead of
        // swallowing the click: publish through the shared EventBus so MainViewModel
        // routes the navigation (mirrors how other code-behinds reach the app).
        e.Handled = true;

        try
        {
            if (App.ServiceProvider?.GetService<IEventBus>() is { } eventBus)
            {
                _ = eventBus.PublishAsync(new NavigateToPageEvent { PageKey = AppPages.Settings, Source = "System" });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to publish NavigateToPageEvent from KickSettingsView: {ex}");
        }
    }
}
