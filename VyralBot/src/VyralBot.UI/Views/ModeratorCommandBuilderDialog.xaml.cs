using System.Windows;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Modal visual builder for a !mod command, opened from the Moderator page's
/// "Add moderator command" button. Run/Save outcomes are read by the
/// ModeratorViewModel after ShowDialog (same pattern as ConfirmDialog);
/// Cancel just closes.
/// </summary>
public partial class ModeratorCommandBuilderDialog : Window
{
    public bool RunRequested { get; private set; }

    public bool SaveRequested { get; private set; }

    public ModeratorCommandBuilderViewModel Builder => (ModeratorCommandBuilderViewModel)DataContext;

    public ModeratorCommandBuilderDialog()
    {
        InitializeComponent();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!Builder.Validate())
        {
            return;
        }

        RunRequested = true;
        DialogResult = true;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Builder.Validate())
        {
            return;
        }

        SaveRequested = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
