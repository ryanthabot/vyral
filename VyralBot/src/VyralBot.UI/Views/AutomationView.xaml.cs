using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.UI.ViewModels;
using VyralBot.Workflows.Models;

namespace VyralBot.UI.Views;

/// <summary>
/// Automation page code-behind. The structured workflow builder
/// (AutomationViewModel) is attached by the MainWindow DataTemplate; this class
/// additionally hosts the visual flow editor
/// (<see cref="FlowEditorView"/> + <see cref="FlowEditorViewModel"/>) as a
/// full-size overlay: the header "Visual Editor" button opens it on a new flow,
/// and each workflow row's "Canvas" button opens it on that workflow.
/// </summary>
public partial class AutomationView : UserControl
{
    /// <summary>Editor view model resolved from the app's DI host (lazy).</summary>
    private FlowEditorViewModel? _flowEditorViewModel;

    public AutomationView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    /// <summary>Header button: opens the visual editor on a brand-new flow.</summary>
    private async void OnVisualEditorClick(object sender, RoutedEventArgs e)
    {
        await OpenFlowEditorAsync(null, null);
    }

    /// <summary>Row button (Canvas): opens the visual editor on that workflow.</summary>
    private async void OnWorkflowCanvasClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkflowListItemViewModel item })
        {
            await OpenFlowEditorAsync(item.Workflow, null);
        }
    }

    /// <summary>
    /// Shared overlay open path: resolves the editor VM from the app host
    /// services (same lazy pattern as the PlatformsView setup views), hosts a
    /// fresh FlowEditorView in the overlay, then initializes the editor. The
    /// overlay hides again when the editor raises EditorClosed.
    /// </summary>
    private async Task OpenFlowEditorAsync(Workflow? workflow, string? defaultPlatform)
    {
        var app = Application.Current as App;
        if (app?.Host == null)
        {
            return;
        }

        try
        {
            if (_flowEditorViewModel == null)
            {
                _flowEditorViewModel = app.Host.Services.GetRequiredService<FlowEditorViewModel>();
                _flowEditorViewModel.EditorClosed += OnFlowEditorClosed;
            }

            FlowEditorHost.Content = new FlowEditorView { DataContext = _flowEditorViewModel };
            FlowEditorOverlay.Visibility = Visibility.Visible;

            await _flowEditorViewModel.InitializeAsync(workflow, defaultPlatform);
        }
        catch (Exception ex)
        {
            FlowEditorOverlay.Visibility = Visibility.Collapsed;
            FlowEditorHost.Content = null;
            MessageBox.Show($"Could not open the visual editor: {ex.Message}", "Vyral",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnFlowEditorClosed(object? sender, EventArgs e)
    {
        FlowEditorOverlay.Visibility = Visibility.Collapsed;
        FlowEditorHost.Content = null;

        if (_flowEditorViewModel != null)
        {
            await _flowEditorViewModel.ShutdownAsync();
        }

        // Refresh the underlying list so saved flows show up immediately.
        if (DataContext is AutomationViewModel viewModel)
        {
            await viewModel.LoadWorkflowsAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Detach from the (possibly singleton) editor VM so a recreated view
        // never leaves stale handler references behind.
        if (_flowEditorViewModel != null)
        {
            _flowEditorViewModel.EditorClosed -= OnFlowEditorClosed;
            _flowEditorViewModel = null;
        }
    }
}
