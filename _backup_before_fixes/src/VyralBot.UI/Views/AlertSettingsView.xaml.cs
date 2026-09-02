using System.Windows.Controls;

namespace VyralBot.UI.Views;

public partial class AlertSettingsView : UserControl
{
    public AlertSettingsView()
    {
        InitializeComponent();
        
        Loaded += async (s, e) =>
        {
            if (DataContext is ViewModels.AlertSettingsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }
}
