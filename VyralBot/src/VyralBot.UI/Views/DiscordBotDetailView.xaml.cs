using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.UI.ViewModels;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;

namespace VyralBot.UI.Views;

/// <summary>
/// Per-bot Discord dashboard code-behind: connection status, servers the bot is
/// in, active-channel picker, and a test-message button — scoped to one bot
/// resolved from the bot manager. Loading is driven by MainViewModel navigation
/// (LoadAsync(botId)). Also hosts the visual command builder
/// (<see cref="FlowEditorView"/> + <see cref="FlowEditorViewModel"/>) as a
/// full-size overlay: the Commands card's "Visual Command Builder" button opens
/// it on a new Discord flow, and every command row's EDIT opens the flow that
/// adopted the command or a flow seeded from it (same ownership model as the
/// Commands page).
/// </summary>
public partial class DiscordBotDetailView : UserControl
{
    /// <summary>Editor view model resolved from the app's DI host (lazy).</summary>
    private FlowEditorViewModel? _flowEditorViewModel;

    public DiscordBotDetailView()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    /// <summary>The bot row this dashboard shows (null before the first LoadAsync).</summary>
    private string? PageBotId => (DataContext as DiscordBotDetailViewModel)?.BotId is { Length: > 0 } botId ? botId : null;

    /// <summary>Commands-card button: opens the visual editor on a new Discord flow.</summary>
    private async void OnVisualCommandBuilderClick(object sender, RoutedEventArgs e)
    {
        // Discord is the platform context for anything authored on a bot page;
        // the page's bot scopes the command row the flow's save creates.
        await OpenFlowEditorAsync(null, "Discord", PageBotId);
    }

    /// <summary>Built-in moderation row EDIT: opens (or seeds) the flow for that command.</summary>
    private async void OnBuiltInCommandEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DiscordModCommandInfo modCommand })
        {
            await OpenFlowEditorForCommandAsync(CreateTransientModCommand(modCommand));
        }
    }

    /// <summary>Custom command row EDIT: opens (or seeds) the flow for that command.</summary>
    private async void OnBotCommandEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BotCommandRowViewModel row } && row.Command != null)
        {
            await OpenFlowEditorForCommandAsync(row.Command);
        }
    }

    /// <summary>
    /// Shared overlay open path: resolves the editor VM from the app host
    /// services (same lazy pattern as AutomationView), hosts a fresh
    /// FlowEditorView in the overlay, then initializes the editor. The overlay
    /// hides again when the editor raises EditorClosed.
    /// </summary>
    private async Task OpenFlowEditorAsync(Workflow? workflow, string? defaultPlatform, string? ownerBotId)
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

            // Discord is the platform context — the default trigger platform
            // and role/channel pickers lean on the connected bots; ownerBotId
            // scopes the command row the flow's save creates/adopts.
            await _flowEditorViewModel.InitializeAsync(workflow, defaultPlatform, ownerBotId);
        }
        catch (Exception ex)
        {
            FlowEditorOverlay.Visibility = Visibility.Collapsed;
            FlowEditorHost.Content = null;
            MessageBox.Show($"Could not open the visual command builder: {ex.Message}", "Vyral",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Row EDIT path (same ownership model as the Commands page): reuses the
    /// flow that already adopted this command (first match on
    /// Workflow.SourceCommandId) so its canvas loads verbatim; otherwise seeds
    /// a fresh flow from the command row. SAVE then adopts the command.
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
                await _flowEditorViewModel.InitializeAsync(existingFlow, null, PageBotId);
            }
            else
            {
                await _flowEditorViewModel.InitializeFromCommandAsync(command, PageBotId);
            }
        }
        catch (Exception ex)
        {
            FlowEditorOverlay.Visibility = Visibility.Collapsed;
            FlowEditorHost.Content = null;
            MessageBox.Show($"Could not open the visual command builder: {ex.Message}", "Vyral",
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

    /// <summary>
    /// The built-in moderation commands (kick, ban, …) are handled in-Discord by
    /// DiscordModCommandHandler and have no Commands-table row, so each EDIT
    /// synthesizes a transient built-in row with a stable id
    /// ("discord-mod:{name}"). Seeding and SAVE use that id as the flow's
    /// SourceCommandId, so the same flow reopens on the next EDIT and the
    /// save-time adoption shadows the name via each bot's DisabledBuiltIns.
    /// </summary>
    private static Command CreateTransientModCommand(DiscordModCommandInfo info)
    {
        // "purge / clear" style labels collapse to their first command name.
        string name = info.Name.Split(' ', '/')[0].Trim().TrimStart('!', '.').ToLowerInvariant();

        return new Command
        {
            Id = $"discord-mod:{name}",
            Name = $"!{name}",
            Response = string.Empty,
            Permission = "Moderator",
            IsBuiltIn = true,
            IsEnabled = true,
            EnabledOnKick = false,
            EnabledOnTwitch = false,
            BotId = null
        };
    }

    private async void OnFlowEditorClosed(object? sender, EventArgs e)
    {
        FlowEditorOverlay.Visibility = Visibility.Collapsed;
        FlowEditorHost.Content = null;

        if (_flowEditorViewModel != null)
        {
            await _flowEditorViewModel.ShutdownAsync();
        }

        // Refresh the dashboard so adopted commands / disabled rows / newly
        // saved flows show up immediately.
        if (DataContext is DiscordBotDetailViewModel viewModel)
        {
            try
            {
                await viewModel.RefreshAsync();
                await viewModel.LoadBotCommandsAsync();
            }
            catch
            {
                // Refresh is best-effort — never block the overlay close.
            }
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
