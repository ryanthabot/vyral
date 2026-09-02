using System.Windows.Controls;
using VyralBot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Data;

namespace VyralBot.UI.Views;

public partial class TiktokChatSettingsView : UserControl
{
    public TiktokChatSettingsView()
    {
        InitializeComponent();
        var provider = App.ServiceProvider;
        if (provider != null)
        {
            DataContext = provider.GetRequiredService<TiktokChatSettingsViewModel>();
            if (DataContext is TiktokChatSettingsViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        }
        else
        {
            DataContext = new TiktokChatSettingsViewModel(
                provider?.GetService<ILogger<TiktokChatSettingsViewModel>>()!,
                provider?.GetService<IDataService>()!,
                provider?.GetService<VyralBot.Core.Services.EventBus.IEventBus>()!);
        }
    }
}
