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

    private void IngestKeyBox_Loaded(object sender, RoutedEventArgs e)
    {
        // The ingest key is masked like the destination stream keys — PasswordBox
        // is not bindable, so seed it from the page view model when the box
        // loads. Assigning Password fires PasswordChanged, which writes the same
        // value straight back.
        if (sender is PasswordBox passwordBox &&
            passwordBox.DataContext is RestreamViewModel viewModel)
        {
            passwordBox.Password = viewModel.IngestStreamKey ?? "";
        }
    }

    private void IngestKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Feed the masked ingest key to the page view model manually; the
        // save/start flows keep reading the IngestStreamKey property.
        if (sender is PasswordBox passwordBox &&
            passwordBox.DataContext is RestreamViewModel viewModel)
        {
            viewModel.IngestStreamKey = passwordBox.Password;
        }
    }
}
