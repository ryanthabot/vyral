using System.Windows;

namespace VyralBot.UI.Views;

public partial class ConfirmDialog : Window
{
        public bool Result { get; private set; }

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Replaces the dialog's main message (used by the Settings data-management
        /// confirmations; defaults to the navigation-warning text).
        /// </summary>
        public void SetMessage(string message)
        {
            MessageText.Text = message;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        DialogResult = false;
        Close();
    }
}
