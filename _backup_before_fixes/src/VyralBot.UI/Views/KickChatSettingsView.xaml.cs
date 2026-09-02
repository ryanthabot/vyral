using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class KickChatSettingsView : UserControl
{
    public KickChatSettingsView()
    {
        InitializeComponent();

        // Resolve KickChatSettingsViewModel from the application's DI container
        var provider = App.ServiceProvider;
        if (provider != null)
        {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KickChatSettingsViewModel>>();
            var eventSettingsService = provider.GetRequiredService<VyralBot.Core.Services.Chat.IChatEventSettingsService>();
            
            var vm = new KickChatSettingsViewModel(logger, eventSettingsService);
            DataContext = vm;
            
            // Initialize settings asynchronously
            _ = vm.InitializeAsync();
        }
        else
        {
            // Fallback: create transient instance to avoid runtime error in design-time scenarios
            DataContext = new KickChatSettingsViewModel(
                provider?.GetService<Microsoft.Extensions.Logging.ILogger<KickChatSettingsViewModel>>()!,
                provider?.GetService<VyralBot.Core.Services.Chat.IChatEventSettingsService>()!);
        }
    }
}
