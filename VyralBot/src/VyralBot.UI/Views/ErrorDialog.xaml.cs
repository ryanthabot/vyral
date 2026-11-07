using System.Windows;

namespace VyralBot.UI.Views;

/// <summary>
/// Borderless crash dialog (dark theme). Shows a full exception report —
/// type, message, stack, inner chain — already secret-scrubbed by
/// <see cref="Services.CrashReport"/> before it reaches this dialog, with a
/// copy button so users can paste the report into a bug ticket.
///
/// REENTRANCY: a second exception while the dialog is open (very common when
/// state is corrupted) must not stack another modal window on top. Use
/// <see cref="ShowReport"/> — it appends follow-up reports to the open
/// dialog instead of creating a second one.
/// </summary>
public partial class ErrorDialog : Window
{
    /// <summary>The dialog currently being shown, if any (UI thread only).</summary>
    private static ErrorDialog? _openDialog;

    public string ErrorMessage { get; set; }

    public ErrorDialog(string errorMessage)
    {
        InitializeComponent();
        ErrorMessage = errorMessage;
        DataContext = this;
    }

    /// <summary>True while a crash dialog is on screen. UI thread only.</summary>
    public static bool IsOpen => _openDialog != null;

    /// <summary>
    /// Shows the report in a modal crash dialog — or, when one is already
    /// open (crash while the crash dialog is up), APPENDS to it instead of
    /// stacking a second modal. Must be called on the UI thread; returns
    /// false only when the dialog could not be shown at all.
    /// </summary>
    public static bool ShowReport(string title, string report)
    {
        var open = _openDialog;
        if (open != null)
        {
            open.AppendDetails(report);
            return true;
        }

        var dialog = new ErrorDialog(report) { Title = title };
        try
        {
            // Owner keeps the dialog on top of a (possibly still-rendered)
            // main window; skipped during early boot when none exists yet.
            if (Application.Current?.MainWindow is { IsLoaded: true } main && !ReferenceEquals(main, dialog))
            {
                dialog.Owner = main;
            }

            _openDialog = dialog;
            dialog.Closed += static (_, _) => _openDialog = null;
            dialog.ShowDialog();
            return true;
        }
        catch
        {
            _openDialog = null;
            try { dialog.Close(); } catch { /* already gone */ }
            return false;
        }
    }

    /// <summary>
    /// Appends a follow-up report to this open dialog (the reentrancy path —
    /// the separator makes the boundary visible in both the box and the
    /// clipboard copy). Thread-safe: marshals to the dialog's dispatcher.
    /// </summary>
    public void AppendDetails(string report)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendDetails(report));
            return;
        }

        const string separator = "\n\n========== Another error occurred while this dialog was open ==========\n\n";
        ErrorMessage += separator + report;
        ErrorTextBox.AppendText(separator + report);
        ErrorTextBox.ScrollToEnd();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ErrorMessage);

            // Show brief confirmation
            var originalContent = ((System.Windows.Controls.Button)sender).Content;
            ((System.Windows.Controls.Button)sender).Content = "✓ COPIED!";

            Task.Delay(1500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    ((System.Windows.Controls.Button)sender).Content = originalContent;
                });
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
