using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class AudioSettingsView : UserControl
{
    public AudioSettingsView()
    {
        InitializeComponent();
        
        // DataContext is set by the DataTemplate binding to AudioSettingsViewModel
        Loaded += async (s, e) =>
        {
            if (DataContext is AudioSettingsViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }
}
