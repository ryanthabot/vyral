using System.Windows;

namespace VyralBot.UI.Views;

public partial class ErrorDialog : Window
{
    public string ErrorMessage { get; set; }

    public ErrorDialog(string errorMessage)
    {
        InitializeComponent();
        ErrorMessage = errorMessage;
        DataContext = this;
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
