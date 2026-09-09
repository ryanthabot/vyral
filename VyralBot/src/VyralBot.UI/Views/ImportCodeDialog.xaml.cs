using System.Windows;

namespace VyralBot.UI.Views;

/// <summary>Paste box for importing by share code / offline code / marketplace link.</summary>
public partial class ImportCodeDialog : Window
{
    /// <summary>The pasted code, or null when cancelled.</summary>
    public string? Code { get; private set; }

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

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
