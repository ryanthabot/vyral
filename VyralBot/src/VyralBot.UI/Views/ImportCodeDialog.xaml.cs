using System.Windows;

namespace VyralBot.UI.Views;

/// <summary>
/// Paste box for importing by share code / offline code / link, with a
/// FROM FILE escape hatch for classic .flow.json documents.
/// </summary>
public partial class ImportCodeDialog : Window
{
    /// <summary>The pasted code, or null when cancelled / file chosen.</summary>
    public string? Code { get; private set; }

    /// <summary>The chosen .flow.json path when FROM FILE was used, otherwise null.</summary>
    public string? FilePath { get; private set; }

    public ImportCodeDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeBox.Text))
        {
            CodeBox.Focus();
            return;
        }

        Code = CodeBox.Text.Trim();
        DialogResult = true;
    }

    private void FromFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import flow",
            Filter = "Vyral flow (*.flow.json)|*.flow.json|JSON (*.json)|*.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return; // stay open — the user may still paste a code
        }

        FilePath = dialog.FileName;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
