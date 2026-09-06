using System.Windows.Controls;
using VyralBot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Chat;

namespace VyralBot.UI.Views;

public partial class TwitchChatSettingsView : UserControl
{
    public TwitchChatSettingsView()
    {
        InitializeComponent();

        // Resolve the singleton from the application's DI container (mirrors the
        // Discord/YouTube/TikTok views) so every popup shares one VM instance.
        var provider = App.ServiceProvider;
        if (provider != null)
        {
            DataContext = provider.GetRequiredService<TwitchChatSettingsViewModel>();
            if (DataContext is TwitchChatSettingsViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        }
        else
        {
            // Fallback: design-time / no host scenario
            DataContext = new TwitchChatSettingsViewModel(
                provider?.GetService<ILogger<TwitchChatSettingsViewModel>>()!,
                provider?.GetService<IChatEventSettingsService>()!);
        }
    }
}
