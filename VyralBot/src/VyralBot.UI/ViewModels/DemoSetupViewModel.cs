using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Demo setup wizard to show the reusable BaseWebSetupViewModel in action
/// This is a carbon copy of how Twitch/Kick setup wizards work
/// </summary>
public partial class DemoSetupViewModel : BaseWebSetupViewModel
{
    public DemoSetupViewModel(ILogger<DemoSetupViewModel> logger) : base(logger)
    {
    }

    [RelayCommand]
    private async Task OpenStep1BrowserAsync()
    {
        await OpenBrowserAsync("https://github.com");
    }

    [RelayCommand]
    private async Task OpenStep2BrowserAsync()
    {
        await OpenBrowserAsync("https://www.google.com");
    }

    [RelayCommand]
    private async Task OpenStep3BrowserAsync()
    {
        await OpenBrowserAsync("https://stackoverflow.com");
    }

    [RelayCommand]
    private void OpenClientIdModal()
    {
        OpenModal(
            "Enter Client ID",
            "Enter your application's Client ID from the developer console",
            (input) =>
            {
                // Demo only: the value is deliberately discarded — nothing is
                // persisted and nothing is logged (secrets rule).
                _logger.LogInformation("Demo Client ID captured (demo only — value not persisted, not logged)");
            });
    }

    [RelayCommand]
    private void ContinueFromStep1()
    {
        if (CurrentStep == 1)
        {
            GoToStep(2);
        }
    }

    [RelayCommand]
    private void ContinueFromStep2()
    {
        if (CurrentStep == 2)
        {
            GoToStep(3);
        }
    }

    [RelayCommand]
    private void ContinueFromStep3()
    {
        if (CurrentStep == 3)
        {
            CompleteSetup();
        }
    }
}
