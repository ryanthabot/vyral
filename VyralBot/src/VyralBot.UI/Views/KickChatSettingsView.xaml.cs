using System.Windows.Controls;
using VyralBot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace VyralBot.UI.Views;

public partial class KickChatSettingsView : UserControl
{
    public KickChatSettingsView()
    {
        InitializeComponent();

        // Resolve the singleton from the application's DI container (mirrors the
        // Discord/YouTube/TikTok views) so every popup shares one VM instance.
        var provider = App.ServiceProvider;
        if (provider != null)
        {
            DataContext = provider.GetRequiredService<KickChatSettingsViewModel>();
            if (DataContext is KickChatSettingsViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        }
        else
        {
            // Fallback: design-time / no host scenario
            DataContext = new KickChatSettingsViewModel(
                provider?.GetService<Microsoft.Extensions.Logging.ILogger<KickChatSettingsViewModel>>()!,
                provider?.GetService<VyralBot.Core.Services.Chat.IChatEventSettingsService>()!);
        }
    }
}
