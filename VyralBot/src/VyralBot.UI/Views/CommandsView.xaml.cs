using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.UI.ViewModels;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;

namespace VyralBot.UI.Views;

/// <summary>
/// Commands page code-behind. Every command edit now happens in the visual
/// flow editor (<see cref="FlowEditorView"/> + <see cref="FlowEditorViewModel"/>)
/// hosted as a full-size overlay — the same pattern as AutomationView:
/// <list type="bullet">
/// <item>NEW COMMAND opens a pure new flow (<c>InitializeAsync(null, null)</c>).</item>
/// <item>Every row's EDIT (built-in or custom) opens the flow that adopted the
/// command (first match on <c>Workflow.SourceCommandId</c>) or, when none
/// exists yet, a flow seeded from the command row via
/// <c>InitializeFromCommandAsync</c>.</item>
/// </list>
/// Saving inside the overlay adopts the command (the legacy Command row is
/// retired), and closing the overlay refreshes the list.
/// </summary>
public partial class CommandsView : UserControl
{
    /// <summary>Editor view model resolved from the app's DI host (lazy).</summary>
    private FlowEditorViewModel? _flowEditorViewModel;

    /// <summary>Marketplace view model resolved from the app's DI host (lazy).</summary>
    private MarketplaceViewModel? _marketplaceViewModel;

    public CommandsView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    /// <summary>Header MARKETPLACE button: opens the marketplace overlay.</summary>
    private async void OnMarketplaceClick(object sender, RoutedEventArgs e)
    {
        var app = Application.Current as App;
        if (app?.Host == null)
        {
            return;
        }

        try
        {
            if (_marketplaceViewModel == null)
            {
                _marketplaceViewModel = app.Host.Services.GetRequiredService<MarketplaceViewModel>();
                _marketplaceViewModel.CloseRequested += OnMarketplaceClosed;
                _marketplaceViewModel.ImportRequested += OnMarketplaceImportRequested;
            }

            MarketplaceHost.Content = new MarketplaceView { DataContext = _marketplaceViewModel };
            MarketplaceOverlay.Visibility = Visibility.Visible;

            await _marketplaceViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            MarketplaceOverlay.Visibility = Visibility.Collapsed;
            MarketplaceHost.Content = null;
            MessageBox.Show($"Could not open the marketplace: {ex.Message}", "Vyral",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnMarketplaceClosed(object? sender, EventArgs e)
    {
        MarketplaceOverlay.Visibility = Visibility.Collapsed;
        MarketplaceHost.Content = null;
    }

    /// <summary>
    /// A shared command was fetched from the marketplace: drop the marketplace
    /// overlay and load the command into the flow editor as a working copy.
    /// </summary>
    private void OnMarketplaceImportRequested(object? sender, (FlowEditorSnapshotDto Snapshot, string Label) request)
    {
        MarketplaceOverlay.Visibility = Visibility.Collapsed;
        MarketplaceHost.Content = null;

        if (_flowEditorViewModel == null && Application.Current is App app && app.Host != null)
        {
            _flowEditorViewModel = app.Host.Services.GetRequiredService<FlowEditorViewModel>();
            _flowEditorViewModel.EditorClosed += OnFlowEditorClosed;
        }

        if (_flowEditorViewModel == null)
        {
            return;
        }

        FlowEditorHost.Content = new FlowEditorView { DataContext = _flowEditorViewModel };
        FlowEditorOverlay.Visibility = Visibility.Visible;
        _flowEditorViewModel.ImportSharedSnapshot(request.Snapshot, request.Label);
    }

    /// <summary>Header / custom-tab button: opens the visual editor on a brand-new flow.</summary>
    private async void OnVisualEditorClick(object sender, RoutedEventArgs e)
    {
        await OpenFlowEditorAsync(null, null);
    }

    /// <summary>
    /// CUSTOM tab IMPORT button: file dialog (.csv/.json/.txt — Nightbot CSV,
    /// StreamElements JSON or plain !name=response text), then the view model
    /// parses it (format auto-detected from the content) and opens the in-page
    /// import preview overlay.
    /// </summary>
    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CommandsViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import commands",
            Filter = "Command exports (*.csv;*.json;*.txt)|*.csv;*.json;*.txt|Nightbot CSV (*.csv)|*.csv|StreamElements JSON (*.json)|*.json|Plain text (*.txt)|*.txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(dialog.FileName);
            await viewModel.RunImportPreviewAsync(content);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read the import file: {ex.Message}", "Vyral",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Toolbar button (Built-in and Custom tabs): edits the selected command in the visual editor.</summary>
    private async void OnCommandEditClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CommandsViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectedCommand == null)
        {
            // Give feedback like the DELETE button does instead of silently
            // doing nothing.
            viewModel.StatusMessage = "Select a command first";
            return;
        }

        await OpenFlowEditorForCommandAsync(viewModel.SelectedCommand);
    }

    /// <summary>Row double-click (Built-in and Custom grids): edits that command in the visual editor.</summary>
    private async void OnCommandsGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandsViewModel { SelectedCommand: not null } viewModel)
        {
            await OpenFlowEditorForCommandAsync(viewModel.SelectedCommand);
        }
    }

    /// <summary>
    /// Shared overlay open path: resolves the editor VM from the app host
    /// services (same lazy pattern as AutomationView), hosts a fresh
    /// FlowEditorView in the overlay, then initializes the editor. The overlay
    /// hides again when the editor raises EditorClosed.
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
            MessageBox.Show($"Could not open the visual builder: {ex.Message}", "Vyral",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Row EDIT path: reuses the flow that already adopted this command
    /// (first match on Workflow.SourceCommandId) so its canvas loads verbatim;
    /// otherwise seeds a fresh flow from the command row. The generated flow
    /// keeps the command id as SourceCommandId, so SAVE adopts the command.
    /// </summary>
    private async Task OpenFlowEditorForCommandAsync(Command command)
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

            Workflow? existingFlow = await FindAdoptingFlowAsync(app, command.Id);

            FlowEditorHost.Content = new FlowEditorView { DataContext = _flowEditorViewModel };
            FlowEditorOverlay.Visibility = Visibility.Visible;

            if (existingFlow != null)
            {
                await _flowEditorViewModel.InitializeAsync(existingFlow, null);
            }
            else
            {
                await _flowEditorViewModel.InitializeFromCommandAsync(command);
            }
        }
        catch (Exception ex)
        {
            FlowEditorOverlay.Visibility = Visibility.Collapsed;
            FlowEditorHost.Content = null;
            MessageBox.Show($"Could not open the visual builder: {ex.Message}", "Vyral",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>First workflow whose SourceCommandId matches the command, or null.</summary>
    private static async Task<Workflow?> FindAdoptingFlowAsync(App app, string commandId)
    {
        try
        {
            IDataService dataService = app.Host.Services.GetRequiredService<IDataService>();
            foreach (var row in await dataService.GetWorkflowsAsync())
            {
                Workflow flow = WorkflowJson.FromRow(row);
                if (string.Equals(flow.SourceCommandId, commandId, StringComparison.Ordinal))
                {
                    return flow; // first match wins
                }
            }
        }
        catch (Exception)
        {
            // Lookup is an optimization — on failure the command is simply
            // re-seeded from its row.
        }

        return null;
    }

    private async void OnFlowEditorClosed(object? sender, EventArgs e)
    {
        FlowEditorOverlay.Visibility = Visibility.Collapsed;
        FlowEditorHost.Content = null;

        if (_flowEditorViewModel != null)
        {
            await _flowEditorViewModel.ShutdownAsync();
        }

        // Refresh the list so adopted (disabled) rows and new flows show up.
        if (DataContext is CommandsViewModel viewModel)
        {
            await viewModel.LoadCommandsAsync();
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

        if (_marketplaceViewModel != null)
        {
            _marketplaceViewModel.CloseRequested -= OnMarketplaceClosed;
            _marketplaceViewModel.ImportRequested -= OnMarketplaceImportRequested;
            _marketplaceViewModel = null;
        }
    }
}
