using System.Windows;
using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class RestreamView : UserControl
{
    public RestreamView()
    {
        InitializeComponent();
    }

    private void StreamKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Destination stream keys are masked in a PasswordBox (not bindable) and
        // live inside per-destination row templates, so each box is populated
        // from its row's view model when the container is realized. Setting
        // Password fires PasswordChanged, which writes the same value back.
        if (sender is PasswordBox passwordBox &&
            passwordBox.DataContext is RestreamDestinationItemViewModel destination)
        {
            passwordBox.Password = destination.StreamKey ?? "";
        }
    }

    private void StreamKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The stream key is masked like the other platform secrets — feed it to
        // the row's view model manually; the save/start flow keeps reading the
        // StreamKey property.
        if (sender is PasswordBox passwordBox &&
            passwordBox.DataContext is RestreamDestinationItemViewModel destination)
        {
            destination.StreamKey = passwordBox.Password;
        }
    }
}
