using MaterialDesignThemes.Wpf;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class PollView : UserControl
{
    public PollView()
    {
        InitializeComponent();
    }

    private async void CreatePollButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PollViewModel vm)
        {
            // A cancelled earlier edit must not leave the dialog in
            // "Save Changes" mode for a fresh create.
            vm.ResetEditor();
        }

        var content = CreatePollDialog.DialogContent;
        if (content == null)
        {
            return; // Nothing to show
        }

        await DialogHost.Show(content, "RootDialog");
    }

    private async void EditPollButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not PollViewModel vm)
        {
            return;
        }

        // Loads the selected Pending poll into the form; the command
        // validates status and refuses non-Pending selections (dialog stays
        // closed in that case — check IsEditingPoll).
        await vm.EditPollCommand.ExecuteAsync(null);

        if (!vm.IsEditingPoll)
        {
            return;
        }

        var content = CreatePollDialog.DialogContent;
        if (content == null)
        {
            return; // Nothing to show
        }

        await DialogHost.Show(content, "RootDialog");
    }
}
