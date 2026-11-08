using System.Windows.Controls;
using VyralBot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.UI.Views;

public partial class YoutubeChatSettingsView : UserControl
{
    public YoutubeChatSettingsView()
    {
        InitializeComponent();
            var provider = App.ServiceProvider;
            if (provider != null)
            {
                DataContext = provider.GetRequiredService<YoutubeChatSettingsViewModel>();
                if (DataContext is YoutubeChatSettingsViewModel vm)
                {
                    _ = vm.InitializeAsync();
                }
            }
            else
            {
                DataContext = new YoutubeChatSettingsViewModel(
                    provider?.GetService<ILogger<YoutubeChatSettingsViewModel>>()!,
                    provider?.GetService<IDataService>()!,
                    provider?.GetService<VyralBot.Core.Services.EventBus.IEventBus>()!,
                    provider?.GetService<VyralBot.Core.Services.Chat.IChatEventSettingsService>()!);
            }
    }
}
