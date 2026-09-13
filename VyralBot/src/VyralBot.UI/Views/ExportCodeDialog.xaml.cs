using System.Windows;

namespace VyralBot.UI.Views;

/// <summary>Which export route the user picked in the export chooser.</summary>
public enum ExportFlowChoice
{
    /// <summary>Publish to the marketplace, get an online VYRAL-… share code.</summary>
    OnlineCode,

    /// <summary>Copy a self-contained VYRAL1-… offline code to the clipboard.</summary>
    OfflineCode,

    /// <summary>Classic .flow.json file save.</summary>
    File
}

/// <summary>
/// Export chooser for the flow editor: online share code, offline code,
/// or classic file export.
/// </summary>
public partial class ExportCodeDialog : Window
{
    /// <summary>The picked route, valid when ShowDialog returned true.</summary>
    public ExportFlowChoice Choice { get; private set; } = ExportFlowChoice.OnlineCode;

    public ExportCodeDialog()
    {
        InitializeComponent();
    }

    private void Online_Click(object sender, RoutedEventArgs e) => Close(ExportFlowChoice.OnlineCode);

    private void Offline_Click(object sender, RoutedEventArgs e) => Close(ExportFlowChoice.OfflineCode);

    private void File_Click(object sender, RoutedEventArgs e) => Close(ExportFlowChoice.File);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Close(ExportFlowChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
