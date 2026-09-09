using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.UI.ViewModels;
using VyralBot.Workflows.Models;

namespace VyralBot.UI.Views;

/// <summary>
/// Moderator page code-behind. Hosts the visual flow editor
/// (<see cref="FlowEditorView"/> + <see cref="FlowEditorViewModel"/>) as a
/// full-size overlay — the same pattern as AutomationView: the Moderator
/// Commands card's "Add moderator command" button opens it on the
/// mod_command preset, and each workflow row's "Edit in visual builder"
/// button opens it on that workflow.
/// </summary>
public partial class ModeratorView : UserControl
{
    /// <summary>Editor view model taken from the page VM's DI singleton (lazy).</summary>
    private FlowEditorViewModel? _flowEditorViewModel;

    public ModeratorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ModeratorViewModel old)
        {
            old.FlowEditorRequested -= OnFlowEditorRequested;
        }

        if (e.NewValue is ModeratorViewModel viewModel)
        {
            viewModel.FlowEditorRequested += OnFlowEditorRequested;
        }
    }

    /// <summary>Card button: opens the visual editor on a new moderator command.</summary>
    private void OnFlowEditorRequested() => _ = OpenFlowEditorAsync(null);

    /// <summary>Row button: opens the visual editor on that moderator command workflow.</summary>
    private void OnModCommandCanvasClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModCommandWorkflowRow row })
        {
            _ = OpenFlowEditorAsync(row.Workflow);
        }
    }

    /// <summary>
    /// Shared overlay open path: hosts a fresh FlowEditorView in the overlay,
    /// then initializes the editor — <see cref="FlowEditorViewModel.StartNewModCommandAsync"/>
    /// for a new command, <see cref="FlowEditorViewModel.InitializeAsync(Workflow?, string?)"/>
    /// for an existing one. The overlay hides again when the editor raises EditorClosed.
    /// </summary>
    private async Task OpenFlowEditorAsync(Workflow? workflow)
    {
        if (DataContext is not ModeratorViewModel viewModel)
        {
            return;
        }

        try
        {
            if (_flowEditorViewModel == null)
            {
                _flowEditorViewModel = viewModel.FlowEditor;
                _flowEditorViewModel.EditorClosed += OnFlowEditorClosed;
            }

            FlowEditorHost.Content = new FlowEditorView { DataContext = _flowEditorViewModel };
            FlowEditorOverlay.Visibility = Visibility.Visible;

            if (workflow == null)
            {
                await _flowEditorViewModel.StartNewModCommandAsync();
            }
            else
            {
                await _flowEditorViewModel.InitializeAsync(workflow, null);
            }
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

        // Refresh the underlying list so saved commands show up immediately.
        if (DataContext is ModeratorViewModel viewModel)
        {
            await viewModel.LoadModCommandWorkflowsAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Detach from the (singleton) editor VM so a recreated view never
        // leaves stale handler references behind.
        if (_flowEditorViewModel != null)
        {
            _flowEditorViewModel.EditorClosed -= OnFlowEditorClosed;
            _flowEditorViewModel = null;
        }
    }
}
