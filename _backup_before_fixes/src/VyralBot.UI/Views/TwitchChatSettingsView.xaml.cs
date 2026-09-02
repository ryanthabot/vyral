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
        
        var logger = App.ServiceProvider?.GetRequiredService<ILogger<TwitchChatSettingsViewModel>>();
        var settingsService = App.ServiceProvider?.GetRequiredService<IChatEventSettingsService>();
        var viewModel = new TwitchChatSettingsViewModel(logger!, settingsService!);
        DataContext = viewModel;
        
        _ = viewModel.InitializeAsync();
    }
}

