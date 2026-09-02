using System.Windows.Controls;
using VyralBot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.UI.Views;

public partial class DiscordChatSettingsView : UserControl
{
    public DiscordChatSettingsView()
    {
        InitializeComponent();
        var provider = App.ServiceProvider;
        if (provider != null)
        {
            DataContext = provider.GetRequiredService<DiscordChatSettingsViewModel>();
            if (DataContext is DiscordChatSettingsViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        }
        else
        {
            DataContext = new DiscordChatSettingsViewModel(
                provider?.GetService<ILogger<DiscordChatSettingsViewModel>>()!,
                provider?.GetService<IDataService>()!,
                provider?.GetService<VyralBot.Core.Services.EventBus.IEventBus>()!);
        }
    }
}
