using MaterialDesignThemes.Wpf;
using System.Windows.Controls;

namespace VyralBot.UI.Views;

public partial class PollView : UserControl
{
    public PollView()
    {
        InitializeComponent();
    }

    private async void CreatePollButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var content = CreatePollDialog.DialogContent;
        if (content == null)
        {
            return; // Nothing to show
        }

        await DialogHost.Show(content, "RootDialog");
    }
}
