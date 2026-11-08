using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class NgrokSetupWizardView : UserControl
{
    public NgrokSetupWizardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize WebView2 on demand when user clicks a button
        if (DataContext is NgrokSetupWizardViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested += OnWebView2DisposeRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        if (WebView2Browser == null)
            return;

        // Always ensure WebView2 is initialized (re-initialize if needed)
        try
        {
            if (WebView2Browser.CoreWebView2 == null)
            {
                await WebView2Browser.EnsureCoreWebView2Async();
            }

            if (DataContext is NgrokSetupWizardViewModel viewModel)
            {
                viewModel.WebView2 = WebView2Browser;
            }
        }
        catch
        {
            // Failed to initialize WebView2
        }
    }

    private void OnWebView2DisposeRequested(object? sender, EventArgs e)
    {
        try
        {
            if (WebView2Browser?.CoreWebView2 != null)
            {
                // Unsubscribe from all events
                WebView2Browser.CoreWebView2.NavigationStarting -= null;

                // Dispose the control
                WebView2Browser.Dispose();

                // Force garbage collection to free memory immediately
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch
        {
            // Error disposing WebView2
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is NgrokSetupWizardViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested -= OnWebView2DisposeRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// Named handler (unsubscribed in OnUnloaded, per the zero-orphan pattern):
    /// when the modal closes (save/cancel), clear the masked token box so a
    /// reopened modal never shows the previous token.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NgrokSetupWizardViewModel.ShowModal)
            && sender is NgrokSetupWizardViewModel { ShowModal: false })
        {
            ModalInputBox.Password = "";
        }
    }

    /// <summary>
    /// Feeds the masked PasswordBox into the VM's ModalInput (Password is not
    /// bindable by design). The value is never logged anywhere.
    /// </summary>
    private void ModalInputBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is NgrokSetupWizardViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.ModalInput = passwordBox.Password;
        }
    }

    private void FinishSetup_Click(object sender, RoutedEventArgs e)
    {
        // Reload PlatformsViewModel to detect credentials
        if (DataContext is NgrokSetupWizardViewModel viewModel)
        {
            viewModel.FinishSetupCommand.Execute(null);
        }
    }
}
