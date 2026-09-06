using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VyralBot.UI.Controls.FlowCanvas;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Picks the palette template per list row: bold group-header rows
/// (<see cref="FlowPaletteRowViewModel.IsHeader"/>) vs. draggable block rows.
/// </summary>
public sealed class PaletteRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? ItemTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is FlowPaletteRowViewModel { IsHeader: true } ? HeaderTemplate : ItemTemplate;
}

/// <summary>
/// Visual flow editor view (BotGhost-style layout): grouped block palette,
/// <see cref="FlowCanvasControl"/> canvas with floating zoom group, plus the
/// right-hand settings host over a toolbar / action bar. The settings host
/// serves four modes driven by the view-model — None (help card), Block
/// (selected node), Command (flow-wide trigger / options / parameters) and
/// Parameter (a canvas parameter card). Owns only view-level plumbing —
/// canvas attachment (including the "When: …" trigger card, the parameter
/// cards, entry / error-handler targeting and the component add/remove
/// events), palette drag-and-drop, the remaining stay-open popups, keyboard
/// shortcuts (Ctrl+S / Ctrl+Z / Ctrl+Shift+Z) and numeric-only text input;
/// all state lives in <see cref="FlowEditorViewModel"/>.
/// </summary>
public partial class FlowEditorView : UserControl
{
    public FlowEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private FlowEditorViewModel? ViewModel => DataContext as FlowEditorViewModel;

    // ======================================================================
    // Canvas lifetime
    // ======================================================================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.AttachCanvas(FlowCanvas);
            FlowCanvas.TriggerCardClicked += OnTriggerCardClicked;
            FlowCanvas.ParameterCardClicked += OnParameterCardClicked;
            viewModel.UpdateTriggerCard(); // the card may have been set before attach
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FlowCanvas.TriggerCardClicked -= OnTriggerCardClicked;
        FlowCanvas.ParameterCardClicked -= OnParameterCardClicked;
        if (ViewModel is { } viewModel)
        {
            viewModel.DetachCanvas();
        }
    }

    /// <summary>
    /// Clicking the canvas trigger card switches the right panel into Command
    /// mode (the retired TRIGGER popup used to open here).
    /// </summary>
    private void OnTriggerCardClicked(object? sender, EventArgs e)
    {
        ViewModel?.EnterCommandMode();
    }

    /// <summary>
    /// Clicking a canvas parameter card switches the right panel into
    /// Parameter mode for that row (the index addresses the last list the
    /// canvas was given via <c>SetParameterCards</c>).
    /// </summary>
    private void OnParameterCardClicked(object? sender, int index)
    {
        ViewModel?.EnterParameterMode(index);
    }

    // ======================================================================
    // Palette: drag onto the canvas / double-click to drop at the center
    // ======================================================================

    private void OnPaletteItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FlowPaletteRowViewModel { Item: { } item } }
            || ViewModel == null)
        {
            return;
        }

        // Double-click = drop a node at the center of the current view.
        if (e.ClickCount >= 2)
        {
            ViewModel.PlaceBlockCommand.Execute(item);
            e.Handled = true;
            return;
        }

        // Single press = start a drag-drop carrying the block type; the canvas
        // creates the node where it lands (FlowCanvasControl.BlockTypeDataFormat).
        try
        {
            var data = new DataObject(FlowCanvasControl.BlockTypeDataFormat, item.Block.Type);
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception)
        {
            // A failed drag (e.g. released immediately) is not an error.
        }

        e.Handled = true;
    }

    // ======================================================================
    // Block Templates: drag onto the canvas / click to place
    // ======================================================================

    /// <summary>
    /// Second drag format carrying the full template JSON alongside the
    /// canvas's <see cref="FlowCanvasControl.BlockTypeDataFormat"/> payload.
    /// </summary>
    private const string TemplateDataFormat = "vyral-block-template";

    /// <summary>
    /// Block-template row mouse-down, mirroring the palette row behavior:
    /// a double-click places the template at the view center (the primary,
    /// guaranteed-correct path — the template Config + Label are applied to
    /// the fresh node), and a single press starts a canvas drag carrying the
    /// block type (+ the template JSON under a second format). After the drop
    /// completes, the template Config + Label are copied onto the node the
    /// canvas just created; a drag released without a drop falls back to
    /// click-to-place.
    /// </summary>
    private void OnTemplateRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FlowEditorViewModel.FlowTemplateRowViewModel template }
            || ViewModel == null)
        {
            return;
        }

        // Double-click = click-to-place at the center of the current view.
        if (e.ClickCount >= 2)
        {
            ViewModel.PlaceBlockTemplate(template);
            e.Handled = true;
            return;
        }

        // Ids of the existing same-type nodes: the one the canvas creates on
        // drop is the fresh remainder once DoDragDrop returns.
        var knownIds = new HashSet<string>(
            ViewModel.Nodes
                .Where(node => string.Equals(node.BlockType, template.BlockType, StringComparison.Ordinal))
                .Select(node => node.Id),
            StringComparer.Ordinal);

        var data = new DataObject(FlowCanvasControl.BlockTypeDataFormat, template.BlockType);
        data.SetData(TemplateDataFormat, template.ToJsonPayload());

        DragDropEffects result;
        try
        {
            result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception)
        {
            result = DragDropEffects.None; // a failed drag (released instantly) is not an error
        }

        if (result == DragDropEffects.None)
        {
            // Nothing was dropped — treat the gesture as a plain click.
            ViewModel.PlaceBlockTemplate(template);
        }
        else
        {
            // Best-effort: apply the template onto the freshly dropped node.
            ViewModel.ApplyTemplateAfterDrop(template, knownIds);
        }

        e.Handled = true;
    }

    // ======================================================================
    // Toolbar popups (OPTIONS / create-variable / save-template remain; the
    // TRIGGER and PARAMETERS popups retired into the right settings panel).
    // Open-state is VM-owned; because they stay open, this view owns
    // dismissing them.
    // ======================================================================

    private void OnOptionsButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.IsOptionsPopupOpen = !viewModel.IsOptionsPopupOpen;
    }

    /// <summary>
    /// Popups are <c>StaysOpen="True"</c>. This capture closes them on any
    /// press outside — clicks inside a popup land in the popup's own window
    /// and never tunnel through this root, and the OPTIONS toolbar button is
    /// skipped so its Click handler can still toggle its own popup.
    /// </summary>
    private void OnEditorRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || ViewModel is not { } viewModel)
        {
            return;
        }

        if (!viewModel.IsOptionsPopupOpen
            && !viewModel.IsCreateVariablePopupOpen
            && !viewModel.IsTemplatePopupOpen)
        {
            return;
        }

        if (IsWithin(e.OriginalSource as DependencyObject, OptionsButton))
        {
            return; // the toolbar button toggles its own popup in Click
        }

        viewModel.CloseAllPopups();
    }

    /// <summary>True when <paramref name="source"/> sits inside <paramref name="container"/>.</summary>
    private static bool IsWithin(DependencyObject? source, FrameworkElement? container)
    {
        while (source != null && container != null)
        {
            if (ReferenceEquals(source, container))
            {
                return true;
            }

            if (source is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                source = VisualTreeHelper.GetParent(source);
            }
            else
            {
                source = LogicalTreeHelper.GetParent(source);
            }
        }

        return false;
    }

    // ======================================================================
    // Numeric-only input (number fields, timer minutes, rule hours)
    // ======================================================================

    private void OnNumericTextInput(object sender, TextCompositionEventArgs e)
    {
        // Digits plus the minus sign (negative numbers are rare but legal);
        // everything else — including pasted junk via the keyboard — is blocked.
        foreach (char character in e.Text)
        {
            if (!char.IsDigit(character) && character != '-')
            {
                e.Handled = true;
                return;
            }
        }
    }

    // ======================================================================
    // Keyboard shortcuts
    // ======================================================================

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (ViewModel is not { } viewModel)
        {
            return;
        }

        // Escape closes the stay-open popups (explicit — they no longer close
        // themselves; see OnEditorRootPreviewMouseDown).
        if (e.Key == Key.Escape
            && (viewModel.IsOptionsPopupOpen
                || viewModel.IsCreateVariablePopupOpen
                || viewModel.IsTemplatePopupOpen))
        {
            viewModel.CloseAllPopups();
            e.Handled = true;
            return;
        }

        // Ctrl+S saves regardless of focus (typing in a text box must not eat it).
        // Mirrors the SAVE button's IsEnabled binding (IsBusy -> disabled): while
        // a save is in flight the shortcut must be a no-op, or Ctrl+S re-enters
        // the save pipeline.
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!viewModel.IsBusy)
            {
                viewModel.SaveCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        // Inside a text box, Ctrl+Z / Ctrl+Y undo typed text — leave them alone.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift:
            case Key.Y:
                if (viewModel.RedoCommand.CanExecute(null))
                {
                    viewModel.RedoCommand.Execute(null);
                }
                e.Handled = true;
                break;

            case Key.Z:
                if (viewModel.UndoCommand.CanExecute(null))
                {
                    viewModel.UndoCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }
}
