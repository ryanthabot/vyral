using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using VyralBot.UI.Services;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// Unreal-Blueprint-style node canvas for authoring a workflow graph on a dark
/// pannable/zoomable surface. Renders and edits a collection of
/// <see cref="FlowCanvasNode"/> view models in place (the host owns the
/// collection and maps it to <see cref="VyralBot.Workflows.Models.Workflow"/>).
/// <para>
/// INTERACTION MODEL (summary for docs):
/// • Canvas: 6000x4000 world surface with a dot grid that scales with zoom,
///   hosted in a ScrollViewer — drag empty canvas to pan or use the scrollbars.
/// • Zoom: mouse wheel zooms 25%–200% around the cursor; ZoomIn/ZoomOut step
///   ±10% around the viewport center; ResetZoom returns to 100%;
///   FitToContent frames every node. Shift+wheel scrolls horizontally.
/// • Nodes: click to select (blue highlight, raises NodeSelected), click the
///   background to deselect (NodeSelected(null)), drag the body to move
///   (wires follow live; GraphChanged fires once at drag end), double-click to
///   rename in place, Ctrl+D to duplicate (+24/+24, new id, no wires),
///   Delete to remove (also clears every wire/routing field referencing it).
/// • Ports/wires: drag from a colored output port (bottom of a node) to
///   another node's input port to connect — self-connects, duplicates and
///   cycles are rejected (rejected wires flash red briefly). Output ports
///   replace their wire when re-dragged; INPUT ports hold at most one wire —
///   a new connection landing on an occupied input displaces the old one
///   (the previous source's routing entry is cleared). Wire/port colors
///   resolve through FlowCanvasPalette.ResolvePortColor: the descriptor color
///   EXCEPT "next" ports, which render MainWire blue (grey is reserved for
///   settings-attached flows — the loop's ports, component drop points,
///   comparison case/else). Click a wire BODY to
///   select/highlight it; wires are LOCKED — Delete never removes one (rewire
///   by dragging again; deleting a node still clears its wires). Deletable
///   wires (blue main wires, green condition branches) carry exactly ONE ⊗
///   CLICK-TO-DISCONNECT marker centered on the path middle: clicking it
///   clears the source node's routing field (the wire hides and GraphChanged
///   fires so undo captures it); hovering the marker enlarges/brightens it.
///   Grey attachment wires and amber error/default wires are non-deletable
///   and render NO marker at all.
///   STYLE RULE: wires leaving a GREY port (component drop points, the loop's
///   "After loop"/"Loop times" ports, comparison case/else) render as
///   RIGHT-ANGLE elbows (BotGhost attachment look); colored wires (blue main,
///   green conditions, amber error/default/case) stay beziers.
/// • Component chips: message blocks render two LIGHT chips just below the
///   card — "⊕ Button" under the bottom-left corner, "⊕ Menu" under the
///   bottom-right corner (always fully labeled; the button chip dims at the
///   5-button cap, the menu chip dims while its sub-card is wired — a Menu
///   config with no live card keeps the chip clickable so a press re-spawns
///   the card). Chips raise ComponentAddRequested; AddComponentNode
///   spawns the matching small button_start/menu_start sub-node card DIRECTLY
///   BELOW the parent (centered on its grey drop dot) and wires the parent's
///   derived drop-point port ("case:buttonN"/"case:menu0") to it, which draws
///   the grey elbow. RemoveComponentNode deletes one.
/// • Attachment cards: the special non-palette block types
///   "loop_after"/"loop_body"/"comparison_case"/"comparison_else" render as
///   small plain/chipped BotGhost cards with a grey input dot and a BLUE main
///   output dot (their chains wire onward like normal blocks). Create them
///   (and their grey elbow from any parent port) with AddAttachmentNode.
///   They drag/select/rename like normal nodes but the Delete key (and Ctrl+D)
///   never removes one.
/// • Pinned cards: the trigger card (amber ⚡, SetTriggerCard) floats above the
///   entry node area and has a blue output port — dragging it onto a node sets
///   that node as the main-flow entry (EntryTargetId/EntryNodeChanged/
///   SetEntryTarget, rendered as a locked MainWire-blue wire styled like a
///   normal wire). The error-handler card (red ⚠️) is a draggable second
///   pinned card with the same port pattern (ErrorTargetId/ErrorNodeChanged/
///   SetErrorTarget). Clicking the trigger card raises NodeSelected(null) +
///   TriggerCardClicked WITHOUT stealing keyboard focus; the error card raises
///   NodeSelected(null) only. Pinned wires are locked, redrawn live, and keep
///   the attachment rule: blue for the trigger's main-flow entry, grey for the
///   error-handler's attached chain. Clicking the ⊗ marker on the entry
///   wire disconnects the trigger card (SetEntryTarget(null) +
///   EntryNodeChanged(null)); the grey error wire renders no marker (it can
///   only be re-dragged to retarget).
/// • Parameter cards: SetParameterCards renders a horizontal row of small
///   display-only option cards (purple type-initial chip, bold name,
///   "… option" subtitle, orange "!" warning badge, one grey bottom dot)
///   centered above the trigger card; a grey wire drops from each card's dot
///   and merges into the trigger card's top input point. Clicking a card
///   raises ParameterCardClicked(index) after the usual NodeSelected(null) —
///   cards never steal keyboard focus, never drag and cannot be deleted.
///   null/empty hides the row; the row also hides with the trigger card.
/// • Palette: host drag-and-drop with the DataObject format
///   <see cref="BlockTypeDataFormat"/> ("vyral-block-type") drops a new node
///   at the pointer (pan+zoom accounted for) via CreateNode.
/// • Canvas controls: three white square buttons pinned to the control's
///   bottom-left corner, outside the scroll surface — fit-to-view ⤢, reset
///   zoom ⌂, and auto-organize ⤓ ("Auto-organize layout": a deterministic
///   top-down layered rearrangement of every block based on its connections;
///   one GraphChanged so undo captures the whole rearrangement, then
///   fit-to-view).
/// • Switch nodes derive one extra output port per case from the node's
///   Config["Cases"] (comma-separated); the STATIC BlockCatalog.GetOutputs
///   ports always render regardless of Config (so e.g. "loop" keeps its
///   "After loop"/"Loop times" ports). Call <see cref="RefreshNode"/> after
///   editing a node's Config in place to refresh ports, chips and wires.
/// </para>
/// </summary>
public class FlowCanvasControl : Control
{
    /// <summary>DataObject format string the host palette uses when dragging a block type onto the canvas.</summary>
    public const string BlockTypeDataFormat = "vyral-block-type";

    /// <summary>Special non-palette block type of a button component sub-node (hardcoded red 👍 card).</summary>
    public const string ComponentButtonBlockType = "button_start";

    /// <summary>Special non-palette block type of a select-menu component sub-node (hardcoded blue ☰ card).</summary>
    public const string ComponentMenuBlockType = "menu_start";

    /// <summary>Special non-palette block type of the loop's "After loop" attachment card (plain dark card, no chip).</summary>
    public const string LoopAfterBlockType = "loop_after";

    /// <summary>Special non-palette block type of the loop's "Loop # times" body attachment card (plain dark card, no chip).</summary>
    public const string LoopBodyBlockType = "loop_body";

    /// <summary>Special non-palette block type of a comparison branch card (green "?" chip, title = rule summary).</summary>
    public const string ComparisonCaseBlockType = "comparison_case";

    /// <summary>Special non-palette block type of the comparison "Else" card (red "?" chip).</summary>
    public const string ComparisonElseBlockType = "comparison_else";

    private const double CanvasWidth = 6000d;
    private const double CanvasHeight = 4000d;
    private const double MinZoom = 0.25d;
    private const double MaxZoom = 2.0d;
    private const double ZoomStep = 1.1d;
    private const double GridSpacing = 20d;
    private const double TriggerCardWidth = 240d;
    private const double TriggerCardOffsetY = 120d; // trigger card floats this far above the topmost node
    private const double TriggerCardFallbackHeight = 64d;
    private const double ParameterCardWidth = 150d;      // parameter option cards match the component sub-node cards
    private const double ParameterCardFallbackHeight = 48d;
    private const double ParameterCardSpacing = 24d;     // horizontal gap between cards in the row
    private const double ParameterCardGapY = 70d;        // cards float this far above the trigger card
    private const double AttachmentCardGapY = 70d;       // component/attachment cards hang this far below their parent's bottom edge
    private const double CanvasButtonSize = 28d;
    private const double OrganizeSlotWidth = 170d; // auto-organize: horizontal pitch within a depth level
    private const double OrganizeRowHeight = 140d; // auto-organize: vertical pitch between depth levels
    private const double OrganizeTreeGap = 60d;    // auto-organize: horizontal gap between neighboring trees
    private const string ErrorHandlerTitle = "Error Handler";
    private const string ErrorHandlerSubtitle = "Handle errors that occur during the command execution";

    private const string PartScrollViewer = "PART_ScrollViewer";
    private const string PartContentCanvas = "PART_ContentCanvas";
    private const string PartWireLayer = "PART_WireLayer";
    private const string PartParameterLayer = "PART_ParameterLayer";
    private const string PartTriggerLayer = "PART_TriggerLayer";
    private const string PartNodeLayer = "PART_NodeLayer";
    private const string PartOverlayLayer = "PART_OverlayLayer";
    private const string PartFitButton = "PART_FitButton";
    private const string PartResetButton = "PART_ResetButton";
    private const string PartOrganizeButton = "PART_OrganizeButton";

    private readonly Dictionary<string, FlowNodeVisual> _visuals = new(StringComparer.Ordinal);
    private readonly List<FlowWireVisual> _wires = new();
    private readonly ScaleTransform _scaleTransform = new(1d, 1d);

    private ObservableCollection<FlowCanvasNode> _nodes = new();
    private FlowCanvasNode? _selectedNode;
    private FlowWireVisual? _selectedWire;
    private bool _selectionSync;
    private bool _syncingWires;

    private ScrollViewer? _scrollViewer;
    private Canvas? _contentCanvas;
    private Canvas? _wireLayer;
    private Canvas? _parameterLayer;
    private Canvas? _triggerLayer;
    private Canvas? _nodeLayer;
    private Canvas? _overlayLayer;
    private Button? _fitButton;
    private Button? _resetButton;
    private Button? _organizeButton;

    private Border? _triggerCard;
    private TextBlock? _triggerTitleText;
    private TextBlock? _triggerSubtitleText;
    private string _triggerCardTitle = string.Empty;
    private string _triggerCardSubtitle = string.Empty;

    private Border? _errorCard;
    private TextBlock? _errorTitleText;
    private TextBlock? _errorSubtitleText;
    private double? _errorCardX; // draggable world position (independent of the trigger card once dragged)
    private double? _errorCardY;

    private string? _entryTargetId; // node wired to the trigger card's output (main-flow entry)
    private string? _errorTargetId; // node wired to the error-handler card's output
    private FlowPinnedWireVisual? _entryWire;
    private FlowPinnedWireVisual? _errorWire;
    private List<FlowParameterCardInfo> _parameterCards = new(); // last SetParameterCards snapshot (drives the row above the trigger card)

    private DragMode _mode = DragMode.None;
    private Point _panStartScreen;
    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private bool _panMoved;
    private FlowNodeVisual? _dragVisual;
    private Point _dragStartWorld;
    private double _nodeStartX;
    private double _nodeStartY;
    private bool _nodeMoved;
    private FlowCanvasNode? _connectNode;
    private Point _pendingStart; // world point the pending wire leaves from (cached at drag start)
    private bool _pendingOrthogonal; // pending wire previews the elbow style for grey attachment ports
    private Path? _pendingWire;
    private Point _errorDragStartWorld;
    private double _errorDragStartX;
    private double _errorDragStartY;

    /// <summary>Raised when a node is selected (value) or deselected (null, e.g. background click).</summary>
    public event EventHandler<FlowCanvasNode?>? NodeSelected;

    /// <summary>Raised after every graph mutation: add, delete, connect, disconnect, drag-end move, label edit.</summary>
    public event EventHandler? GraphChanged;

    /// <summary>Raised when the pinned trigger card is clicked (after NodeSelected(null) fires).</summary>
    public event EventHandler? TriggerCardClicked;

    /// <summary>
    /// Raised when a parameter card is clicked (after NodeSelected(null)
    /// fires). The value is the 0-based index into the list passed to the most
    /// recent <see cref="SetParameterCards"/> call.
    /// </summary>
    public event EventHandler<int>? ParameterCardClicked;

    /// <summary>
    /// Raised when the entry wire's target changes: the node id wired to the
    /// trigger card's output port, or null when unwired/displaced/deleted.
    /// The editor persists this as Workflow.EntryNodeId.
    /// </summary>
    public event EventHandler<string?>? EntryNodeChanged;

    /// <summary>
    /// Raised when the error-handler wire's target changes: the first node of
    /// the error chain, or null when unwired/displaced/deleted.
    /// </summary>
    public event EventHandler<string?>? ErrorNodeChanged;

    /// <summary>
    /// Raised when a "+" component chip on a node is clicked.
    /// Kind is "button" or "menu" — the host should call
    /// <see cref="AddComponentNode"/> to spawn the matching sub-node.
    /// </summary>
    public event EventHandler<(string NodeId, string Kind)>? ComponentAddRequested;

    /// <summary>
    /// Raised when the ✕ glyph on an existing component chip is clicked.
    /// Kind is "button" or "menu"; Index is the 0-based button index (0 for menu).
    /// The host should update Config and call <see cref="RemoveComponentNode"/>
    /// for the orphaned sub-node, then <see cref="RefreshNode(string)"/>.
    /// </summary>
    public event EventHandler<(string NodeId, string Kind, int Index)>? ComponentRemoveRequested;

    /// <summary>Registered for <see cref="Zoom"/>; clamped to 0.25–2.0 by the zoom methods.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(FlowCanvasControl), new PropertyMetadata(1.0d, OnZoomChanged));

    /// <summary>Current zoom factor (0.25–2.0, 1.0 = 100%).</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        private set => SetValue(ZoomProperty, value);
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowCanvasControl control)
        {
            double zoom = (double)e.NewValue;
            control._scaleTransform.ScaleX = zoom;
            control._scaleTransform.ScaleY = zoom;
        }
    }

    /// <summary>
    /// The rendered/edited node collection (in place). Assigning null
    /// default-constructs a fresh collection. Assigning a new collection
    /// rebuilds the canvas.
    /// </summary>
    public ObservableCollection<FlowCanvasNode> Nodes
    {
        get => _nodes;
        set
        {
            ObservableCollection<FlowCanvasNode> collection = value ?? new ObservableCollection<FlowCanvasNode>();
            if (ReferenceEquals(_nodes, collection))
                return;

            foreach (FlowNodeVisual visual in _visuals.Values)
                visual.Node.PropertyChanged -= OnNodePropertyChanged;

            _nodes.CollectionChanged -= OnNodesCollectionChanged;
            _nodes = collection;
            _nodes.CollectionChanged += OnNodesCollectionChanged;
            RebuildAllVisuals();
        }
    }

    /// <summary>The currently selected node, or null.</summary>
    public FlowCanvasNode? SelectedNode => _selectedNode;

    /// <summary>
    /// Id of the node wired to the trigger card's output port (the main-flow
    /// entry), or null. Changed by dragging the trigger card's port onto a node
    /// or via <see cref="SetEntryTarget"/>; raised through
    /// <see cref="EntryNodeChanged"/> on every change (including null when the
    /// entry node is deleted).
    /// </summary>
    public string? EntryTargetId => _entryTargetId;

    /// <summary>
    /// Id of the first node of the error chain (wired to the error-handler
    /// card's output port), or null. Changed by dragging the error card's port
    /// onto a node or via <see cref="SetErrorTarget"/>; raised through
    /// <see cref="ErrorNodeChanged"/> on every change.
    /// </summary>
    public string? ErrorTargetId => _errorTargetId;

    /// <summary>Builds the part-named visual tree (ScrollViewer → world canvas → wire/parameter/trigger/node/overlay layers).</summary>
    public FlowCanvasControl()
    {
        Focusable = true;
        AllowDrop = true;
        Template = BuildTemplate();
        _nodes.CollectionChanged += OnNodesCollectionChanged;

        // Live re-theme: while loaded, a ThemeManager theme switch rebuilds the
        // whole canvas with the refreshed palette (see OnThemeChanged). The
        // subscription itself lives in OnApplyTemplate (template-applied path);
        // Unloaded drops it so a discarded editor never leaks the handler.
        Unloaded += (_, _) => ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    // =====================================================================================
    // Public API
    // =====================================================================================

    /// <summary>Zooms in by 10% around the viewport center (clamped to 200%).</summary>
    public void ZoomIn() => ZoomAt(Zoom * ZoomStep, ViewportCenter());

    /// <summary>Zooms out by 10% around the viewport center (clamped to 25%).</summary>
    public void ZoomOut() => ZoomAt(Zoom / ZoomStep, ViewportCenter());

    /// <summary>Resets zoom to 100% around the viewport center.</summary>
    public void ResetZoom() => ZoomAt(1.0d, ViewportCenter());

    /// <summary>Zooms and pans so every node is visible (no-op zoom when the canvas is empty).</summary>
    public void FitToContent()
    {
        if (_scrollViewer == null)
            return;

        if (_visuals.Count == 0)
        {
            ResetZoom();
            return;
        }

        Rect bounds = Rect.Empty;
        foreach (FlowNodeVisual visual in _visuals.Values)
        {
            double width = visual.ActualWidth > 0 ? visual.ActualWidth : FlowNodeVisual.WidthFor(visual.Node);
            double height = visual.ActualHeight > 0 ? visual.ActualHeight : FlowNodeVisual.DefaultHeightFor(visual.Node);
            Rect nodeBounds = new(visual.Node.X, visual.Node.Y, width, height);
            bounds = bounds.IsEmpty ? nodeBounds : Rect.Union(bounds, nodeBounds);
        }

        double margin = 80d;
        double contentWidth = Math.Max(1d, bounds.Width + margin * 2d);
        double contentHeight = Math.Max(1d, bounds.Height + margin * 2d);
        double zoom = Math.Min(_scrollViewer.ViewportWidth / contentWidth, _scrollViewer.ViewportHeight / contentHeight);
        if (double.IsNaN(zoom) || double.IsInfinity(zoom) || zoom <= 0d)
            zoom = 1d;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);

        Zoom = zoom;
        _scrollViewer.UpdateLayout(); // extents must catch up with the new scale before scrolling

        double centerX = (bounds.Left + bounds.Width / 2d) * zoom;
        double centerY = (bounds.Top + bounds.Height / 2d) * zoom;
        _scrollViewer.ScrollToHorizontalOffset(centerX - _scrollViewer.ViewportWidth / 2d);
        _scrollViewer.ScrollToVerticalOffset(centerY - _scrollViewer.ViewportHeight / 2d);
    }

    /// <summary>
    /// Creates a node of the given block type: new GUID "N" id, label from the
    /// BlockCatalog title, Config prefilled with each ParamField's DefaultValue.
    /// Raises <see cref="GraphChanged"/> and selects the new node.
    /// </summary>
    public FlowCanvasNode CreateNode(string blockType, double x, double y)
    {
        if (string.IsNullOrWhiteSpace(blockType))
            throw new ArgumentException("A block type key is required.", nameof(blockType));

        BlockDefinition? block = BlockCatalog.Find(blockType);
        FlowCanvasNode node = new()
        {
            BlockType = blockType,
            Label = block?.Title ?? blockType,
            X = ClampX(x, FlowNodeVisual.NodeWidth),
            Y = ClampY(y, 96d),
        };

        if (block != null)
        {
            foreach (ParamField param in block.Params)
            {
                if (!string.IsNullOrEmpty(param.DefaultValue))
                    node.Config[param.Key] = param.DefaultValue!;
            }
        }

        // Freshly PLACED comparisons start in multi-rule mode with zero rules
        // ("[]"): the Else port shows right away, the editor auto-attaches the
        // wired Else card, and the engine routes every execution through the
        // (unwired-at-first) else branch — the same shape as a fresh loop.
        // Loaded flows are NOT seeded: a legacy comparison (no key) keeps its
        // True/False ports and outputs until a settings-panel edit migrates it.
        if (string.Equals(blockType, "comparison", StringComparison.OrdinalIgnoreCase))
            node.Config["Comparisons"] = "[]";

        Nodes.Add(node); // CollectionChanged wires the visual
        SelectNode(node);
        RaiseGraphChanged();
        return node;
    }

    /// <summary>
    /// Removes a node and clears every wire/routing field on the remaining
    /// nodes that references it. Raises <see cref="GraphChanged"/>.
    /// </summary>
    public void DeleteNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
            return;

        FlowCanvasNode? node = FindNode(nodeId);
        if (node == null)
            return;

        // Clear every routing field on the remaining nodes that points at it.
        bool clearedSlotPort = false; // a component sub-card was unhooked — the source chip must wake up
        _syncingWires = true;
        try
        {
            foreach (FlowCanvasNode other in _nodes)
            {
                if (ReferenceEquals(other, node))
                    continue;

                bool changed = false;
                if (string.Equals(other.NextTargetId, nodeId, StringComparison.Ordinal))
                {
                    other.NextTargetId = null;
                    changed = true;
                }
                if (string.Equals(other.BranchTrueTargetId, nodeId, StringComparison.Ordinal))
                {
                    other.BranchTrueTargetId = null;
                    changed = true;
                }
                if (string.Equals(other.BranchFalseTargetId, nodeId, StringComparison.Ordinal))
                {
                    other.BranchFalseTargetId = null;
                    changed = true;
                }
                if (string.Equals(other.BranchErrorTargetId, nodeId, StringComparison.Ordinal))
                {
                    other.BranchErrorTargetId = null;
                    changed = true;
                }
                if (other.BranchTargets != null && other.BranchTargets.ContainsValue(nodeId))
                {
                    string[] keys = new string[other.BranchTargets.Count];
                    other.BranchTargets.Keys.CopyTo(keys, 0);
                    foreach (string key in keys)
                    {
                        if (string.Equals(other.BranchTargets[key], nodeId, StringComparison.Ordinal))
                        {
                            other.BranchTargets.Remove(key);
                            // BranchTargets stores the STRIPPED case value for
                            // component slots ("menu0" for "case:menu0").
                            clearedSlotPort |= FlowCanvasNode.IsComponentSlotPort(BlockCatalog.PortCasePrefix + key);
                        }
                    }

                    if (other.BranchTargets.Count == 0)
                        other.BranchTargets = null;
                    changed = true;
                }

                if (changed)
                {
                    UpdateWiresForNode(other);
                    if (clearedSlotPort)
                    {
                        // A deleted component sub-card frees its parent's chip
                        // (e.g. the "⊕ Menu" chip stays clickable again) —
                        // rebuild the card so the chip reflects that at once.
                        RebuildNodeVisual(other);
                        UpdateWireGeometryFor(other);
                        clearedSlotPort = false;
                    }
                }
            }
        }
        finally
        {
            _syncingWires = false;
        }

        _nodes.Remove(node); // CollectionChanged → DetachNodeVisual (visual + its wires)
        RaiseGraphChanged();
    }

    /// <summary>
    /// Rebuilds one node's visual (ports) and its wires. Call after editing a
    /// node's Config in place — e.g. changing a switch node's "Cases" changes
    /// its derived case ports.
    /// </summary>
    public void RefreshNode(string nodeId)
    {
        FlowCanvasNode? node = FindNode(nodeId);
        if (node == null)
            return;

        RebuildNodeVisual(node);
        UpdateWiresForNode(node);
        UpdateWireGeometryFor(node);
    }

    /// <summary>
    /// Shows the pinned trigger card (BotGhost's yellow root node) — amber ⚡
    /// chip + title + subtitle — floating above the topmost node and
    /// panning/zooming with the world. An empty <paramref name="title"/> hides
    /// the card. The card is not part of <see cref="Nodes"/> and cannot be
    /// deleted, but it exposes a blue output port: dragging it onto a node (or
    /// calling <see cref="SetEntryTarget"/>) wires the locked MainWire-blue
    /// entry wire (styled like a normal main-flow wire) and raises
    /// <see cref="EntryNodeChanged"/>. Clicking the card raises
    /// <see cref="TriggerCardClicked"/> (plus the usual NodeSelected(null))
    /// WITHOUT stealing keyboard focus.
    /// </summary>
    public void SetTriggerCard(string title, string subtitle)
    {
        _triggerCardTitle = title ?? string.Empty;
        _triggerCardSubtitle = subtitle ?? string.Empty;
        ApplyTriggerCard();
    }

    /// <summary>
    /// Sets the main-flow entry node (the target of the locked MainWire-blue
    /// wire from the trigger card — styled like a normal main-flow wire).
    /// Passing null unwires. Raises
    /// <see cref="EntryNodeChanged"/> when the value changes. Re-dragging the
    /// trigger card's port to another node displaces the wire the same way.
    /// </summary>
    public void SetEntryTarget(string? nodeId)
    {
        if (string.Equals(_entryTargetId, nodeId, StringComparison.Ordinal))
            return;

        _entryTargetId = nodeId;
        UpdatePinnedWireGeometry();
        EntryNodeChanged?.Invoke(this, _entryTargetId);
    }

    /// <summary>
    /// Sets the first node of the error chain (the target of the locked grey
    /// wire from the draggable error-handler card). Passing null unwires.
    /// Raises <see cref="ErrorNodeChanged"/> when the value changes.
    /// </summary>
    public void SetErrorTarget(string? nodeId)
    {
        if (string.Equals(_errorTargetId, nodeId, StringComparison.Ordinal))
            return;

        _errorTargetId = nodeId;
        UpdatePinnedWireGeometry();
        ErrorNodeChanged?.Invoke(this, _errorTargetId);
    }

    /// <summary>
    /// Creates a small component sub-node card DIRECTLY BELOW <paramref name="parentNodeId"/>:
    /// kind "button" → a red 👍 "Start" / Button card
    /// (<see cref="ComponentButtonBlockType"/>), kind "menu" → a blue ☰
    /// "Basic Select Menu" / Select Menu card (<see cref="ComponentMenuBlockType"/>).
    /// BotGhost placement: the card is centered on its slot's grey drop dot
    /// under the parent ("button<i>index</i>" slots first, then the menu slot,
    /// spread evenly along the parent's bottom edge — 2 slots land at ~1/3 and
    /// ~2/3 of the card width), Y = parent bottom + ~70. The card is WIRED to
    /// the parent's matching derived drop-point port ("case:buttonN" /
    /// "case:menu0", stored in the parent's <see cref="FlowCanvasNode.BranchTargets"/>),
    /// which the canvas renders as a grey right-angle elbow from the drop dot
    /// down into the card's top input dot. Also sets Config["ComponentKey"]
    /// (kind+index, informational), selects the new card and raises
    /// <see cref="GraphChanged"/>. Returns the new node, or null when the
    /// parent/kind is unknown.
    /// </summary>
    public FlowCanvasNode? AddComponentNode(string parentNodeId, string kind, int index)
    {
        if (string.IsNullOrWhiteSpace(parentNodeId) || string.IsNullOrWhiteSpace(kind))
            return null;

        string blockType;
        string label;
        string portKey;
        if (string.Equals(kind, "button", StringComparison.OrdinalIgnoreCase))
        {
            blockType = ComponentButtonBlockType;
            label = "Start";
            portKey = FlowCanvasNode.ComponentButtonPortPrefix + index;
        }
        else if (string.Equals(kind, "menu", StringComparison.OrdinalIgnoreCase))
        {
            blockType = ComponentMenuBlockType;
            label = "Basic Select Menu";
            portKey = FlowCanvasNode.ComponentMenuPortKey;
        }
        else
        {
            return null;
        }

        FlowCanvasNode? parent = FindNode(parentNodeId);
        if (parent == null)
            return null;

        FlowCanvasNode node = CreateAttachedCard(parent, portKey, blockType, label, FlowNodeVisual.ComponentNodeWidth);
        node.Config["ComponentKey"] = $"{kind}{index}"; // informational pairing hint for the host

        Nodes.Add(node); // CollectionChanged wires the visual
        WireParentPort(parent, portKey, node.Id); // grey drop-dot elbow: parent port → sub-card input
        if (FlowCanvasNode.IsComponentSlotPort(portKey))
        {
            // The parent's "⊕ Menu" chip dim state depends on this wiring (it
            // stays clickable until the menu sub-card is wired), so rebuild
            // the parent card to reflect the new port immediately.
            RebuildNodeVisual(parent);
            UpdateWireGeometryFor(parent);
        }

        SelectNode(node);
        RaiseGraphChanged();
        return node;
    }

    /// <summary>
    /// Creates one of the special attachment cards (<see cref="LoopAfterBlockType"/>,
    /// <see cref="LoopBodyBlockType"/>, <see cref="ComparisonCaseBlockType"/>,
    /// <see cref="ComparisonElseBlockType"/>) directly below <paramref name="parentNodeId"/>'s
    /// <paramref name="portKey"/> port dot and WIRES the parent's port to it —
    /// grey ports (the loop's "After loop" ("next") / "Loop times"
    /// ("case:body"), comparison's case/else ports once the catalog declares
    /// them grey) then render their BotGhost right-angle elbow down into the
    /// card's top input dot. <paramref name="label"/> is the card title
    /// ("After loop" / "Loop # times" / the rule summary / "Else"). Selects
    /// the new card and raises <see cref="GraphChanged"/>. Returns the new
    /// node, or null when the parent, port or block type is unknown.
    /// </summary>
    public FlowCanvasNode? AddAttachmentNode(string parentNodeId, string portKey, string blockType, string label)
    {
        if (string.IsNullOrWhiteSpace(parentNodeId) || string.IsNullOrWhiteSpace(portKey))
            return null;
        if (!FlowNodeVisual.IsAttachmentCardType(blockType))
            return null;

        FlowCanvasNode? parent = FindNode(parentNodeId);
        if (parent == null)
            return null;

        FlowCanvasNode node = CreateAttachedCard(parent, portKey, blockType, label, FlowNodeVisual.AttachmentCardWidth);
        Nodes.Add(node);
        WireParentPort(parent, portKey, node.Id);
        SelectNode(node);
        RaiseGraphChanged();
        return node;
    }

    /// <summary>
    /// Builds (but does not yet add) a small card centered on the parent's
    /// <paramref name="portKey"/> port dot: X = dot X − width/2, Y = parent
    /// bottom + <see cref="AttachmentCardGapY"/>.
    /// </summary>
    private FlowCanvasNode CreateAttachedCard(FlowCanvasNode parent, string portKey, string blockType, string label, double cardWidth)
    {
        FlowNodeVisual? parentVisual = GetVisual(parent.Id);
        Point local = parentVisual != null
            ? parentVisual.GetOutputPortLocalPoint(portKey)
            : new Point(FlowNodeVisual.WidthFor(parent) / 2d, FlowNodeVisual.DefaultHeightFor(parent));

        return new FlowCanvasNode
        {
            BlockType = blockType,
            Label = label,
            X = ClampX(parent.X + local.X - cardWidth / 2d, cardWidth),
            Y = ClampY(parent.Y + local.Y + AttachmentCardGapY, FlowNodeVisual.ComponentDefaultHeight),
        };
    }

    /// <summary>
    /// Wires <paramref name="parent"/>'s output port to <paramref name="targetId"/>
    /// and rebuilds the parent's wire visuals (the routing-field entry drives
    /// the grey elbow). The <c>_syncingWires</c> guard suppresses the
    /// PropertyChanged-driven rebuild so this refreshes exactly once.
    /// </summary>
    private void WireParentPort(FlowCanvasNode parent, string portKey, string targetId)
    {
        _syncingWires = true;
        try
        {
            parent.SetTarget(portKey, targetId);
        }
        finally
        {
            _syncingWires = false;
        }

        UpdateWiresForNode(parent);
    }

    /// <summary>
    /// Deletes a component sub-node card and its wires (same cleanup as
    /// <see cref="DeleteNode"/>). Raises <see cref="GraphChanged"/>.
    /// </summary>
    public void RemoveComponentNode(string componentNodeId) => DeleteNode(componentNodeId);

    /// <summary>
    /// Renders the BotGhost-style parameter option cards: a horizontal row of
    /// small display-only cards (purple type-initial chip, bold name — or a
    /// dim "(unnamed)" placeholder — grey "… option" subtitle, orange "!"
    /// badge on warning) centered above the pinned trigger card, each with one
    /// grey bottom dot feeding a grey merge wire into the trigger card's top
    /// input point. Cards pan/zoom with the world, are not draggable and
    /// cannot be deleted; clicking one raises <see cref="ParameterCardClicked"/>
    /// (0-based index into <paramref name="parameters"/>) after the usual
    /// NodeSelected(null) — without stealing keyboard focus. Passing null or
    /// an empty list hides the whole row; the row also hides whenever the
    /// trigger card is hidden (empty title). Repeated calls rebuild the row.
    /// </summary>
    public void SetParameterCards(IReadOnlyList<FlowParameterCardInfo>? parameters)
    {
        // Snapshot: records are immutable, but detach from the caller's list.
        _parameterCards = parameters == null || parameters.Count == 0
            ? new List<FlowParameterCardInfo>()
            : new List<FlowParameterCardInfo>(parameters);
        ApplyParameterCards();
    }

    // =====================================================================================
    // Template + part wiring
    // =====================================================================================

    private static ControlTemplate BuildTemplate()
    {
        FrameworkElementFactory root = new(typeof(Grid), "PART_Root");
        root.SetValue(Panel.BackgroundProperty, FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasBackground));

        FrameworkElementFactory scrollViewer = new(typeof(ScrollViewer), PartScrollViewer);
        scrollViewer.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scrollViewer.SetValue(ScrollViewer.FocusableProperty, false);

        FrameworkElementFactory contentCanvas = new(typeof(Canvas), PartContentCanvas);
        contentCanvas.SetValue(FrameworkElement.WidthProperty, CanvasWidth);
        contentCanvas.SetValue(FrameworkElement.HeightProperty, CanvasHeight);
        contentCanvas.SetValue(Panel.BackgroundProperty, CreateDotGridBrush());
        contentCanvas.SetValue(UIElement.AllowDropProperty, true);

        FrameworkElementFactory wireLayer = new(typeof(Canvas), PartWireLayer);
        wireLayer.SetValue(FrameworkElement.WidthProperty, CanvasWidth);
        wireLayer.SetValue(FrameworkElement.HeightProperty, CanvasHeight);

        // Parameter option cards + their merge wires live on their own layer:
        // above normal wires, below the pinned cards — and it is never cleared
        // by RebuildAllVisuals, so the row survives collection rebuilds.
        FrameworkElementFactory parameterLayer = new(typeof(Canvas), PartParameterLayer);
        parameterLayer.SetValue(FrameworkElement.WidthProperty, CanvasWidth);
        parameterLayer.SetValue(FrameworkElement.HeightProperty, CanvasHeight);

        FrameworkElementFactory triggerLayer = new(typeof(Canvas), PartTriggerLayer);
        triggerLayer.SetValue(FrameworkElement.WidthProperty, CanvasWidth);
        triggerLayer.SetValue(FrameworkElement.HeightProperty, CanvasHeight);

        FrameworkElementFactory nodeLayer = new(typeof(Canvas), PartNodeLayer);
        nodeLayer.SetValue(FrameworkElement.WidthProperty, CanvasWidth);
        nodeLayer.SetValue(FrameworkElement.HeightProperty, CanvasHeight);

        FrameworkElementFactory overlayLayer = new(typeof(Canvas), PartOverlayLayer);
        overlayLayer.SetValue(FrameworkElement.WidthProperty, CanvasWidth);
        overlayLayer.SetValue(FrameworkElement.HeightProperty, CanvasHeight);
        overlayLayer.SetValue(Panel.IsHitTestVisibleProperty, false);

        contentCanvas.AppendChild(wireLayer);
        contentCanvas.AppendChild(parameterLayer);
        contentCanvas.AppendChild(triggerLayer);
        contentCanvas.AppendChild(nodeLayer);
        contentCanvas.AppendChild(overlayLayer);
        scrollViewer.AppendChild(contentCanvas);
        root.AppendChild(scrollViewer);

        // Canvas controls (fit-to-view / reset zoom / auto-organize), pinned
        // to the control's bottom-left corner — outside the scroll surface so
        // they never pan.
        FrameworkElementFactory controlsPanel = new(typeof(StackPanel));
        controlsPanel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        controlsPanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        controlsPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(12d));
        controlsPanel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        FrameworkElementFactory fitButton = CreateCanvasButton(PartFitButton, "⤢", "Fit to view");
        controlsPanel.AppendChild(fitButton);

        FrameworkElementFactory resetButton = CreateCanvasButton(PartResetButton, "⌂", "Reset zoom (100%)");
        resetButton.SetValue(FrameworkElement.MarginProperty, new Thickness(0d, 6d, 0d, 0d));
        controlsPanel.AppendChild(resetButton);

        FrameworkElementFactory organizeButton = CreateCanvasButton(PartOrganizeButton, "⤓", "Auto-organize layout");
        organizeButton.SetValue(FrameworkElement.MarginProperty, new Thickness(0d, 6d, 0d, 0d));
        controlsPanel.AppendChild(organizeButton);

        root.AppendChild(controlsPanel);

        return new ControlTemplate(typeof(FlowCanvasControl)) { VisualTree = root };
    }

    /// <summary>White 28x28 rounded-square canvas control button with a dark glyph.</summary>
    private static FrameworkElementFactory CreateCanvasButton(string name, string glyph, string toolTip)
    {
        FrameworkElementFactory button = new(typeof(Button), name);
        button.SetValue(FrameworkElement.StyleProperty, null); // bypass implicit app-level styles
        button.SetValue(FrameworkElement.WidthProperty, CanvasButtonSize);
        button.SetValue(FrameworkElement.HeightProperty, CanvasButtonSize);
        button.SetValue(UIElement.FocusableProperty, false); // keep Delete/Ctrl+D on the canvas
        button.SetValue(Control.PaddingProperty, new Thickness(0d));
        button.SetValue(Control.BorderThicknessProperty, new Thickness(0d));
        button.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
        button.SetValue(Control.ForegroundProperty, FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasButtonForeground));
        button.SetValue(TextElement.FontSizeProperty, 13d);
        button.SetValue(FrameworkElement.ToolTipProperty, toolTip);

        ControlTemplate buttonTemplate = new(typeof(Button));
        FrameworkElementFactory border = new(typeof(Border), "PART_ButtonBorder");
        border.SetValue(Border.BackgroundProperty, FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasButtonBackground));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4d));
        border.SetValue(Border.EffectProperty, new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 8d,
            ShadowDepth = 1.5d,
            Direction = 270d,
            Opacity = 0.35d,
        });
        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        buttonTemplate.VisualTree = border;

        Trigger hover = new() { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasButtonHover))
        {
            TargetName = "PART_ButtonBorder",
        });
        buttonTemplate.Triggers.Add(hover);

        button.SetValue(Button.TemplateProperty, buttonTemplate);
        return button;
    }

    /// <summary>Tiled dot-grid DrawingBrush (20px spacing; scales with the canvas' layout transform).</summary>
    private static DrawingBrush CreateDotGridBrush()
    {
        DrawingGroup drawing = new();
        drawing.Children.Add(new GeometryDrawing
        {
            Brush = FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasBackground),
            Geometry = new RectangleGeometry(new Rect(0d, 0d, GridSpacing, GridSpacing)),
        });
        drawing.Children.Add(new GeometryDrawing
        {
            Brush = FlowCanvasPalette.Brush(FlowCanvasPalette.GridDot),
            Geometry = new EllipseGeometry(new Point(GridSpacing / 2d, GridSpacing / 2d), 1.2d, 1.2d),
        });

        DrawingBrush brush = new()
        {
            Drawing = drawing,
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0d, 0d, GridSpacing, GridSpacing),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0d, 0d, GridSpacing, GridSpacing),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _scrollViewer = Template.FindName(PartScrollViewer, this) as ScrollViewer;
        Canvas? contentCanvas = Template.FindName(PartContentCanvas, this) as Canvas;
        _wireLayer = Template.FindName(PartWireLayer, this) as Canvas;
        _parameterLayer = Template.FindName(PartParameterLayer, this) as Canvas;
        _triggerLayer = Template.FindName(PartTriggerLayer, this) as Canvas;
        _nodeLayer = Template.FindName(PartNodeLayer, this) as Canvas;
        _overlayLayer = Template.FindName(PartOverlayLayer, this) as Canvas;

        if (_fitButton != null)
            _fitButton.Click -= OnFitButtonClicked;
        _fitButton = Template.FindName(PartFitButton, this) as Button;
        if (_fitButton != null)
            _fitButton.Click += OnFitButtonClicked;

        if (_resetButton != null)
            _resetButton.Click -= OnResetButtonClicked;
        _resetButton = Template.FindName(PartResetButton, this) as Button;
        if (_resetButton != null)
            _resetButton.Click += OnResetButtonClicked;

        if (_organizeButton != null)
            _organizeButton.Click -= OnOrganizeButtonClicked;
        _organizeButton = Template.FindName(PartOrganizeButton, this) as Button;
        if (_organizeButton != null)
            _organizeButton.Click += OnOrganizeButtonClicked;

        if (contentCanvas != null)
        {
            contentCanvas.LayoutTransform = _scaleTransform;
            if (!ReferenceEquals(_contentCanvas, contentCanvas))
            {
                _contentCanvas = contentCanvas;
                contentCanvas.MouseLeftButtonDown += OnCanvasMouseDown;
                contentCanvas.MouseMove += OnCanvasMouseMove;
                contentCanvas.MouseLeftButtonUp += OnCanvasMouseUp;
                contentCanvas.LostMouseCapture += OnCanvasLostCapture;
                contentCanvas.DragOver += OnCanvasDragOver;
                contentCanvas.Drop += OnCanvasDrop;
            }
        }

        ApplyTriggerCard(); // rebuild the trigger card against the (new) layer
        ApplyErrorCard();   // build/reattach the error-handler card
        ApplyParameterCards(); // rebuild the parameter row against the (new) layer
        EnsurePinnedWires();
        RebuildAllVisuals();

        // Live re-theme (subscribe once, however often the template applies —
        // unsubscribing first keeps a template re-application from stacking
        // duplicate handlers; Unloaded removes it entirely).
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
    }

    // =====================================================================================
    // Live re-theme (ThemeManager.ThemeChanged)
    // =====================================================================================

    /// <summary>
    /// Re-themes the OPEN canvas when the user switches themes in Settings:
    /// refreshes the palette, rebuilds the pinned trigger/error cards and the
    /// locked pinned wires (they are created once and would otherwise keep
    /// stale brushes), then rebuilds the control template — the root
    /// background, the dot grid and the three canvas buttons bake palette
    /// brushes at build time — and lets <see cref="OnApplyTemplate"/> run its
    /// full pipeline (pinned cards, parameter row, ALL node visuals and
    /// wires) against the refreshed <see cref="FlowCanvasPalette"/>.
    /// </summary>
    private void OnThemeChanged()
    {
        FlowCanvasPalette.RefreshFromResources();

        RecreatePinnedCards(); // cached cards keep stale accent/background brushes — force a rebuild
        RecreatePinnedWires(); // locked entry/error wires were created with the old wire colors

        // A new template instance re-bakes every brush the template owns
        // (root background, dot grid, canvas buttons). ApplyTemplate forces
        // OnApplyTemplate right away so the rebuild is not deferred to the
        // next layout pass.
        Template = BuildTemplate();
        ApplyTemplate();
    }

    /// <summary>Drops the cached pinned trigger/error cards so ApplyTriggerCard/ApplyErrorCard rebuild them with the new palette.</summary>
    private void RecreatePinnedCards()
    {
        if (_triggerCard != null)
        {
            (_triggerCard.Parent as Panel)?.Children.Remove(_triggerCard);
            _triggerCard = null;
            _triggerTitleText = null;
            _triggerSubtitleText = null;
        }

        if (_errorCard != null)
        {
            (_errorCard.Parent as Panel)?.Children.Remove(_errorCard);
            _errorCard = null;
            // _errorCardX/_errorCardY are deliberately KEPT — the dragged
            // position survives the re-theme (ApplyErrorCard reuses them).
        }
    }

    /// <summary>Drops the locked pinned wires so EnsurePinnedWires recreates them with the new wire colors.</summary>
    private void RecreatePinnedWires()
    {
        if (_entryWire != null)
        {
            _entryWire.DisconnectRequested -= OnPinnedWireDisconnectRequested;
            _wireLayer?.Children.Remove(_entryWire);
            _entryWire = null;
        }

        if (_errorWire != null)
        {
            _errorWire.DisconnectRequested -= OnPinnedWireDisconnectRequested;
            _wireLayer?.Children.Remove(_errorWire);
            _errorWire = null;
        }
    }

    // =====================================================================================
    // Pinned cards: trigger (entry) + error handler
    // =====================================================================================

    /// <summary>Creates/shows/hides the trigger card from the stored title/subtitle.</summary>
    private void ApplyTriggerCard()
    {
        if (_triggerLayer == null)
            return;

        if (_triggerCard == null)
        {
            _triggerCard = BuildPinnedCard(
                "⚡", FlowCanvasPalette.TriggerAccent, FlowCanvasPalette.MainWire,
                out _triggerTitleText, out _triggerSubtitleText, out Ellipse outputPort);
            HookPinnedPort(outputPort, DragMode.ConnectEntry);
            _triggerCard.MouseLeftButtonDown += OnTriggerCardMouseDown;
            _triggerLayer.Children.Add(_triggerCard);
        }

        bool visible = _triggerCardTitle.Length > 0;
        _triggerCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        EnsureOnLayer(_triggerCard, _triggerLayer);
        if (!visible)
        {
            UpdatePinnedWireGeometry(); // the entry wire hangs off the card — hide it with the card
            UpdateParameterCardLayout(); // the parameter row anchors to the trigger card — hide it too
            return;
        }

        _triggerTitleText!.Text = _triggerCardTitle;
        _triggerSubtitleText!.Text = _triggerCardSubtitle;
        UpdateTriggerCardPosition();
    }

    /// <summary>Creates (once) and positions the pinned error-handler card.</summary>
    private void ApplyErrorCard()
    {
        if (_triggerLayer == null)
            return;

        if (_errorCard == null)
        {
            _errorCard = BuildPinnedCard(
                "⚠️", FlowCanvasPalette.ErrorCardAccent, FlowCanvasPalette.PortNeutral,
                out _errorTitleText, out _errorSubtitleText, out Ellipse outputPort);
            _errorTitleText.Text = ErrorHandlerTitle;
            _errorSubtitleText.Text = ErrorHandlerSubtitle;
            HookPinnedPort(outputPort, DragMode.ConnectError);
            _errorCard.MouseLeftButtonDown += OnErrorCardMouseDown;
            _errorCard.MouseMove += OnErrorCardMouseMove;
            _errorCard.MouseLeftButtonUp += OnErrorCardMouseUp;
            _errorCard.LostMouseCapture += OnErrorCardLostCapture;
            _triggerLayer.Children.Add(_errorCard);
        }

        if (_errorCardX == null || _errorCardY == null)
        {
            // Default spot: to the right of the trigger card (BotGhost keeps the
            // error handler off to the side). The user can drag it anywhere.
            double x = TriggerCardWidth + 28d;
            double y = 4d;
            if (_triggerCard is { Visibility: Visibility.Visible })
            {
                x = Canvas.GetLeft(_triggerCard) + (_triggerCard.Width > 0 ? _triggerCard.Width : TriggerCardWidth) + 28d;
                y = Canvas.GetTop(_triggerCard);
            }

            _errorCardX = Math.Clamp(x, 4d, Math.Max(4d, CanvasWidth - TriggerCardWidth - 4d));
            _errorCardY = Math.Clamp(y, 4d, Math.Max(4d, CanvasHeight - 4d));
        }

        Canvas.SetLeft(_errorCard, _errorCardX.Value);
        Canvas.SetTop(_errorCard, _errorCardY.Value);
        EnsureOnLayer(_errorCard, _triggerLayer);
        UpdatePinnedWireGeometry();
    }

    /// <summary>Re-parents a pinned card onto the current trigger layer (template re-application safety).</summary>
    private void EnsureOnLayer(FrameworkElement element, Canvas? layer)
    {
        if (layer == null || ReferenceEquals(element.Parent, layer))
            return;

        (element.Parent as Panel)?.Children.Remove(element);
        layer.Children.Add(element);
    }

    /// <summary>
    /// BotGhost-style pinned card: colored chip + bold title + grey subtitle,
    /// with an output port straddling the bottom edge (drag it onto a node to
    /// wire the card's locked output wire). The port/wire color follows the
    /// attachment-vs-main rule: <see cref="FlowCanvasPalette.MainWire"/> blue
    /// for the trigger card (main flow), attachment grey for the error card.
    /// </summary>
    private Border BuildPinnedCard(string glyph, string accentHex, string portColorHex, out TextBlock titleText, out TextBlock subtitleText, out Ellipse outputPort)
    {
        Border card = new()
        {
            Width = TriggerCardWidth,
            CornerRadius = new CornerRadius(10d),
            Background = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBackground),
            BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBorder),
            BorderThickness = new Thickness(1d),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
        };

        Grid root = new();

        Grid content = new() { Margin = new Thickness(12d, 10d, 12d, 10d) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32d) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        Border chip = new()
        {
            CornerRadius = new CornerRadius(7d),
            Background = FlowCanvasPalette.Brush(accentHex),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 16d,
                Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(chip, 0);

        StackPanel textPanel = new() { Margin = new Thickness(10d, 0d, 0d, 0d), VerticalAlignment = VerticalAlignment.Center };
        titleText = new TextBlock
        {
            FontSize = 14d,
            FontWeight = FontWeights.SemiBold,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        subtitleText = new TextBlock
        {
            FontSize = 10.5d,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.DescriptionText),
            TextWrapping = TextWrapping.Wrap,
            Height = 28d, // ~2 lines at 10.5px; WPF TextBlock has no MaxLines
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0d, 2d, 0d, 0d),
        };
        textPanel.Children.Add(titleText);
        textPanel.Children.Add(subtitleText);
        Grid.SetColumn(textPanel, 1);

        content.Children.Add(chip);
        content.Children.Add(textPanel);
        root.Children.Add(content);

        outputPort = new Ellipse
        {
            Width = 10d,
            Height = 10d,
            Fill = FlowCanvasPalette.Brush(portColorHex),
            Stroke = FlowCanvasPalette.Brush(portColorHex),
            StrokeThickness = 1d,
            ToolTip = "Drag onto a block to connect",
            Cursor = Cursors.Cross,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0d, 0d, 0d, -5d),
        };
        root.Children.Add(outputPort);

        card.Child = root;
        return card;
    }

    /// <summary>
    /// Wires a pinned card's output port into the shared connection-drag state
    /// (pending wire previews in the card's wire color — MainWire blue for the
    /// trigger card, attachment grey for the error card; on release over a
    /// node the card's target is set).
    /// </summary>
    private void HookPinnedPort(Ellipse port, DragMode mode)
    {
        port.MouseLeftButtonDown += (_, e) =>
        {
            if (_contentCanvas == null || _overlayLayer == null)
                return;

            port.CaptureMouse();
            _mode = mode;
            _pendingStart = mode == DragMode.ConnectEntry ? TriggerCardPortWorldPoint() : ErrorCardPortWorldPoint();
            // Entry = main flow (blue bezier); error = attachment (grey elbow).
            bool isEntry = mode == DragMode.ConnectEntry;
            _pendingOrthogonal = !isEntry;
            string pendingColor = isEntry ? FlowCanvasPalette.MainWire : FlowCanvasPalette.PortNeutral;
            _pendingWire = new Path
            {
                Stroke = FlowCanvasPalette.Brush(pendingColor),
                StrokeThickness = 2d,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            _overlayLayer.Children.Add(_pendingWire);
            UpdatePendingWire(e.GetPosition(_contentCanvas));
            e.Handled = true; // keep the card's click handler out of a wire drag
        };
        port.MouseMove += (_, e) =>
        {
            if (mode != DragMode.ConnectEntry && mode != DragMode.ConnectError)
                return;
            if (_mode != mode || !port.IsMouseCaptured || _contentCanvas == null)
                return;

            UpdatePendingWire(e.GetPosition(_contentCanvas));
        };
        port.MouseLeftButtonUp += (_, e) =>
        {
            if (!port.IsMouseCaptured)
                return;
            if (_mode == DragMode.ConnectEntry || _mode == DragMode.ConnectError)
                CompletePinnedConnect(_mode, _contentCanvas != null ? e.GetPosition(_contentCanvas) : default);

            port.ReleaseMouseCapture(); // LostMouseCapture fires afterwards; the guard above no-ops it
            e.Handled = true;
        };
        port.LostMouseCapture += (_, _) =>
        {
            // Aborted drag (capture stolen). No-ops when the connection was
            // already completed on mouse-up.
            if (_mode == DragMode.ConnectEntry || _mode == DragMode.ConnectError)
                CompletePinnedConnect(_mode, _contentCanvas != null ? Mouse.GetPosition(_contentCanvas) : default);
        };
    }

    /// <summary>Finishes (or cancels) a pinned-card wire drag over the given world point.</summary>
    private void CompletePinnedConnect(DragMode mode, Point world)
    {
        if (_mode != mode)
            return;

        CleanupPendingWire();
        _mode = DragMode.None;

        FlowCanvasNode? target = FindNodeVisualAt(world, exclude: null)?.Node;
        if (target == null)
            return; // released over empty canvas — just cancel

        if (mode == DragMode.ConnectEntry)
            SetEntryTarget(target.Id);
        else
            SetErrorTarget(target.Id);
    }

    /// <summary>World-space point of the trigger card's output port (bottom-center).</summary>
    private Point TriggerCardPortWorldPoint()
    {
        double x = 4d + TriggerCardWidth / 2d;
        double y = 4d + TriggerCardFallbackHeight;
        if (_triggerCard is { Visibility: Visibility.Visible })
        {
            double width = _triggerCard.Width > 0 ? _triggerCard.Width : TriggerCardWidth;
            double height = _triggerCard.ActualHeight > 0 ? _triggerCard.ActualHeight : TriggerCardFallbackHeight;
            x = Canvas.GetLeft(_triggerCard) + width / 2d;
            y = Canvas.GetTop(_triggerCard) + height;
        }

        return new Point(x, y);
    }

    /// <summary>World-space point of the error-handler card's output port (bottom-center).</summary>
    private Point ErrorCardPortWorldPoint()
    {
        double x = _errorCardX ?? (TriggerCardWidth + 28d);
        double y = _errorCardY ?? 4d;
        double height = _errorCard is { ActualHeight: > 0 } ? _errorCard.ActualHeight : TriggerCardFallbackHeight;
        return new Point(x + TriggerCardWidth / 2d, y + height);
    }

    /// <summary>
    /// Pins the card horizontally centered over the content bounds, ~120px above
    /// the topmost node (clamped to the world origin when the canvas is empty).
    /// Recomputed on collection changes and SetTriggerCard only — it keeps its
    /// world position while nodes are dragged.
    /// </summary>
    private void UpdateTriggerCardPosition()
    {
        if (_triggerCard != null && _triggerCard.Visibility == Visibility.Visible)
        {
            double cardWidth = _triggerCard.Width > 0 ? _triggerCard.Width : TriggerCardWidth;
            double x = 4d;
            double y = 4d;

            if (_visuals.Count > 0)
            {
                Rect bounds = Rect.Empty;
                foreach (FlowNodeVisual visual in _visuals.Values)
                {
                    double width = visual.ActualWidth > 0 ? visual.ActualWidth : FlowNodeVisual.WidthFor(visual.Node);
                    double height = visual.ActualHeight > 0 ? visual.ActualHeight : FlowNodeVisual.DefaultHeightFor(visual.Node);
                    Rect nodeBounds = new(visual.Node.X, visual.Node.Y, width, height);
                    bounds = bounds.IsEmpty ? nodeBounds : Rect.Union(bounds, nodeBounds);
                }

                x = bounds.Left + bounds.Width / 2d - cardWidth / 2d;
                y = bounds.Top - TriggerCardOffsetY;
            }

            x = Math.Clamp(x, 4d, Math.Max(4d, CanvasWidth - cardWidth - 4d));
            y = Math.Clamp(y, 4d, Math.Max(4d, CanvasHeight - 4d));
            Canvas.SetLeft(_triggerCard, x);
            Canvas.SetTop(_triggerCard, y);
        }

        UpdatePinnedWireGeometry();
        UpdateParameterCardLayout(); // the parameter row + merge wires follow the trigger card
    }

    private void OnTriggerCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Deliberately NO Focus()/Capture here: stealing keyboard focus closed
        // the editor's trigger popup the moment the card was clicked.
        SelectNode(null); // the usual deselection path — raises NodeSelected(null)
        SelectWire(null);
        TriggerCardClicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnErrorCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_errorCard == null)
            return;

        // No Focus() — same popup-safety rule as the trigger card.
        SelectNode(null); // click raises NodeSelected(null) only (no popup event)
        SelectWire(null);

        // Thumb-style drag within the world (position need not persist).
        _errorDragStartWorld = _contentCanvas != null ? e.GetPosition(_contentCanvas) : e.GetPosition(_errorCard);
        _errorDragStartX = _errorCardX ?? Canvas.GetLeft(_errorCard);
        _errorDragStartY = _errorCardY ?? Canvas.GetTop(_errorCard);
        _errorCard.CaptureMouse();
        _errorCard.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    /// <summary>Moves the error-handler card while it holds mouse capture.</summary>
    private void OnErrorCardMouseMove(object sender, MouseEventArgs e)
    {
        if (_errorCard == null || !_errorCard.IsMouseCaptured || _contentCanvas == null)
            return;

        Point world = e.GetPosition(_contentCanvas);
        _errorCardX = ClampX(_errorDragStartX + (world.X - _errorDragStartWorld.X), TriggerCardWidth);
        _errorCardY = ClampY(_errorDragStartY + (world.Y - _errorDragStartWorld.Y), TriggerCardFallbackHeight);
        Canvas.SetLeft(_errorCard, _errorCardX.Value);
        Canvas.SetTop(_errorCard, _errorCardY.Value);
        UpdatePinnedWireGeometry();
        e.Handled = true;
    }

    private void OnErrorCardMouseUp(object sender, MouseButtonEventArgs e)
    {
        EndErrorCardDrag();
        e.Handled = true;
    }

    private void OnErrorCardLostCapture(object? sender, MouseEventArgs e) => EndErrorCardDrag();

    private void EndErrorCardDrag()
    {
        if (_errorCard == null || !_errorCard.IsMouseCaptured)
            return;

        _errorCard.ReleaseMouseCapture();
        _errorCard.Cursor = Cursors.Hand;
    }

    /// <summary>Creates (once) and attaches the locked pinned wires (entry + error).</summary>
    private void EnsurePinnedWires()
    {
        if (_wireLayer == null)
            return;

        // The entry wire IS the main flow → MainWire blue bezier; the
        // error-handler wire is a settings-attached flow → attachment grey
        // rendered as a right-angle elbow. Created once; only the deletable
        // entry wire carries the ⊗-marker disconnect (the grey error wire
        // renders no marker — it is re-dragged to retarget instead).
        _entryWire ??= CreatePinnedWire(FlowCanvasPalette.Color(FlowCanvasPalette.MainWire));
        _errorWire ??= CreatePinnedWire(FlowCanvasPalette.Color(FlowCanvasPalette.PortNeutral), orthogonal: true);

        if (!_wireLayer.Children.Contains(_entryWire))
            _wireLayer.Children.Add(_entryWire);
        if (!_wireLayer.Children.Contains(_errorWire))
            _wireLayer.Children.Add(_errorWire);

        UpdatePinnedWireGeometry();
    }

    /// <summary>
    /// Recomputes the locked pinned wires from the cards to their target
    /// nodes (MainWire blue from the trigger card, attachment grey from the
    /// error-handler card). A wire shows only while its card is visible and
    /// its target node exists; both are non-deletable by design.
    /// </summary>
    private void UpdatePinnedWireGeometry()
    {
        UpdatePinnedWire(_entryWire, _entryTargetId, TriggerCardPortWorldPoint(), requireTriggerCard: true);
        UpdatePinnedWire(_errorWire, _errorTargetId, ErrorCardPortWorldPoint(), requireTriggerCard: false);
    }

    private void UpdatePinnedWire(FlowPinnedWireVisual? wire, string? targetId, Point start, bool requireTriggerCard)
    {
        if (wire == null)
            return;

        FlowCanvasNode? target = targetId != null ? FindNode(targetId) : null;
        bool triggerVisible = !requireTriggerCard || (_triggerCard is { Visibility: Visibility.Visible });
        if (target == null || !triggerVisible)
        {
            wire.Visibility = Visibility.Collapsed;
            return;
        }

        wire.Visibility = Visibility.Visible;
        wire.UpdateGeometry(start, InputPortWorldPoint(target));
    }

    /// <summary>Creates a pinned wire pre-subscribed to the disconnect handler.</summary>
    private FlowPinnedWireVisual CreatePinnedWire(Color color, bool orthogonal = false)
    {
        FlowPinnedWireVisual wire = new(color, orthogonal);
        wire.DisconnectRequested += OnPinnedWireDisconnectRequested;
        return wire;
    }

    /// <summary>
    /// Click-to-disconnect on the ⊗ midpoint marker of a deletable PINNED
    /// wire (only the blue entry wire carries one): unwires the matching
    /// pinned card — entry → <see cref="SetEntryTarget"/>(null), error →
    /// <see cref="SetErrorTarget"/>(null), kept sender-based for safety —
    /// which hides the wire and raises EntryNodeChanged(null) /
    /// ErrorNodeChanged(null) so the host persists the cleared routing field.
    /// Deliberately no Focus() here (the same popup-safety rule as the pinned
    /// cards themselves).
    /// </summary>
    private void OnPinnedWireDisconnectRequested(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _entryWire))
            SetEntryTarget(null);
        else if (ReferenceEquals(sender, _errorWire))
            SetErrorTarget(null);
    }

    private void OnFitButtonClicked(object sender, RoutedEventArgs e) => FitToContent();

    private void OnResetButtonClicked(object sender, RoutedEventArgs e) => ResetZoom();

    private void OnOrganizeButtonClicked(object sender, RoutedEventArgs e) => AutoOrganizeLayout();

    // =====================================================================================
    // Parameter cards (BotGhost-style option cards above the trigger card)
    // =====================================================================================

    /// <summary>
    /// Rebuilds the whole parameter layer (merge wires first, then cards, so
    /// cards render above their own wire) from the stored snapshot and
    /// repositions it. Called from <see cref="SetParameterCards"/> and on
    /// template re-application; cheap because parameter lists are tiny.
    /// </summary>
    private void ApplyParameterCards()
    {
        if (_parameterLayer == null)
            return;

        _parameterLayer.Children.Clear();

        for (int i = 0; i < _parameterCards.Count; i++)
        {
            int index = i; // capture for the click handler

            // The grey merge wire: card's bottom dot → trigger card's top
            // input point (recomputed in UpdateParameterCardLayout).
            Path mergeWire = new()
            {
                Stroke = FlowCanvasPalette.Brush(FlowCanvasPalette.PortNeutral),
                StrokeThickness = 2d,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false, // pure decoration — clicks fall through
            };
            _parameterLayer.Children.Add(mergeWire);

            Border card = BuildParameterCard(_parameterCards[i]);
            card.MouseLeftButtonDown += (_, e) =>
            {
                // Deliberately NO Focus()/Capture — the same popup-safety rule
                // as the trigger card: deselect, mark handled, then raise.
                SelectNode(null); // the usual deselection path — raises NodeSelected(null)
                SelectWire(null);
                ParameterCardClicked?.Invoke(this, index);
                e.Handled = true;
            };
            _parameterLayer.Children.Add(card);
        }

        UpdateParameterCardLayout();
    }

    /// <summary>
    /// One ~150px option card: purple rounded-square chip with the type's
    /// initial (⚙ when the type is empty), bold white name (dim "(unnamed)"
    /// placeholder when empty), grey "… option" subtitle, an orange "!" badge
    /// overlapping the top-right corner on warning and ONE grey output dot on
    /// the bottom edge (display-only — no port drag, no input port).
    /// </summary>
    private static Border BuildParameterCard(FlowParameterCardInfo info)
    {
        Border card = new()
        {
            Width = ParameterCardWidth,
            MinHeight = ParameterCardFallbackHeight,
            CornerRadius = new CornerRadius(8d),
            Background = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBackground),
            BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBorder),
            BorderThickness = new Thickness(1d),
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
        };

        Grid root = new();

        Grid content = new() { Margin = new Thickness(10d, 8d, 10d, 8d) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22d) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        string type = (info.Type ?? string.Empty).Trim();
        string glyph = type.Length > 0 ? char.ToUpperInvariant(type[0]).ToString() : "⚙";
        Border chip = new()
        {
            Width = 22d,
            Height = 22d,
            CornerRadius = new CornerRadius(6d),
            Background = FlowCanvasPalette.Brush(FlowCanvasPalette.ParameterAccent),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 12d,
                FontWeight = FontWeights.Bold,
                Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(chip, 0);

        bool unnamed = string.IsNullOrWhiteSpace(info.Name);
        StackPanel textPanel = new() { Margin = new Thickness(8d, 0d, 0d, 0d), VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = unnamed ? "(unnamed)" : info.Name,
            FontSize = 12d,
            FontWeight = unnamed ? FontWeights.Normal : FontWeights.SemiBold,
            Foreground = FlowCanvasPalette.Brush(unnamed ? FlowCanvasPalette.DescriptionText : FlowCanvasPalette.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = ParameterSubtitle(type),
            FontSize = 10d,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.DescriptionText),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0d, 1d, 0d, 0d),
        });
        Grid.SetColumn(textPanel, 1);

        content.Children.Add(chip);
        content.Children.Add(textPanel);
        root.Children.Add(content);

        // Grey output dot straddling the bottom edge — decoration only.
        root.Children.Add(new Ellipse
        {
            Width = 10d,
            Height = 10d,
            Fill = FlowCanvasPalette.Brush(FlowCanvasPalette.PortNeutral),
            Stroke = FlowCanvasPalette.Brush(FlowCanvasPalette.PortNeutral),
            StrokeThickness = 1d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0d, 0d, 0d, -5d),
            IsHitTestVisible = false, // clicks fall through to the card body
        });

        // Orange "!" badge overlapping the top-right corner (notification style).
        if (info.HasWarning)
        {
            Grid badge = new()
            {
                Width = 16d,
                Height = 16d,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0d, -6d, -6d, 0d),
                IsHitTestVisible = false,
            };
            badge.Children.Add(new Ellipse
            {
                Fill = FlowCanvasPalette.Brush(FlowCanvasPalette.ParameterWarning),
                Stroke = FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasBackground), // separation ring against the card edge
                StrokeThickness = 2d,
            });
            badge.Children.Add(new TextBlock
            {
                Text = "!",
                FontSize = 9d,
                FontWeight = FontWeights.Bold,
                Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            root.Children.Add(badge);
        }

        card.Child = root;

        // Hover highlight (border brightens, like the component chips).
        card.MouseEnter += (_, _) => card.BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipBorderHover);
        card.MouseLeave += (_, _) => card.BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBorder);

        return card;
    }

    /// <summary>BotGhost-style type → subtitle ("A text option" style).</summary>
    private static string ParameterSubtitle(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "text" => "Text option",
            "number" => "Number option",
            "user" => "User option",
            "channel" => "Channel option",
            "role" => "Role option",
            "choice" => "Choice option",
            "attachment" => "Attachment option",
            "" => "Option",
            _ => char.ToUpperInvariant(type[0]) + type[1..] + " option",
        };
    }

    /// <summary>
    /// Positions the parameter row in world space — centered over the trigger
    /// card's X, cards ~24px apart, floating ~70px above the trigger card —
    /// and redraws each grey merge bezier from the card's bottom dot into the
    /// trigger card's top-center input point (wires converge near the target,
    /// matching the BotGhost screenshot). Hides the whole layer when the
    /// trigger card is hidden or no parameters are set. Recomputed whenever
    /// the trigger card moves (<see cref="UpdateTriggerCardPosition"/>) or the
    /// parameter set changes (<see cref="ApplyParameterCards"/>).
    /// </summary>
    private void UpdateParameterCardLayout()
    {
        if (_parameterLayer == null)
            return;

        // Children alternate: merge wire at 2*i, card at 2*i + 1 (see
        // ApplyParameterCards). The layer may still be empty during template
        // re-application (ApplyTriggerCard runs first) — skip until rebuilt.
        bool layerReady = _parameterLayer.Children.Count >= 2 * _parameterCards.Count;
        if (!layerReady || _parameterCards.Count == 0
            || _triggerCard == null || _triggerCard.Visibility != Visibility.Visible)
        {
            if (layerReady)
                _parameterLayer.Visibility = Visibility.Collapsed;
            return;
        }

        _parameterLayer.Visibility = Visibility.Visible;

        double triggerWidth = _triggerCard.Width > 0 ? _triggerCard.Width : TriggerCardWidth;
        double triggerX = Canvas.GetLeft(_triggerCard);
        double triggerY = Canvas.GetTop(_triggerCard);
        double centerX = triggerX + triggerWidth / 2d;
        Point triggerTopInput = new(centerX, triggerY); // trigger card's top-center input point

        double totalWidth = _parameterCards.Count * ParameterCardWidth + (_parameterCards.Count - 1) * ParameterCardSpacing;
        double rowX = centerX - totalWidth / 2d;
        double rowY = Math.Clamp(triggerY - ParameterCardFallbackHeight - ParameterCardGapY, 4d, Math.Max(4d, CanvasHeight - ParameterCardFallbackHeight - 4d));

        for (int i = 0; i < _parameterCards.Count; i++)
        {
            double cardX = Math.Clamp(rowX + i * (ParameterCardWidth + ParameterCardSpacing), 4d, Math.Max(4d, CanvasWidth - ParameterCardWidth - 4d));

            Border? card = _parameterLayer.Children[2 * i + 1] as Border;
            if (card != null)
            {
                Canvas.SetLeft(card, cardX);
                Canvas.SetTop(card, rowY);
            }

            if (_parameterLayer.Children[2 * i] is Path mergeWire)
            {
                double cardHeight = card is { ActualHeight: > 0 } ? card.ActualHeight : ParameterCardFallbackHeight;
                Point start = new(cardX + ParameterCardWidth / 2d, rowY + cardHeight);
                mergeWire.Data = FlowWireVisual.CreateBezier(start, triggerTopInput);
            }
        }
    }

    // =====================================================================================
    // Visual tree sync
    // =====================================================================================

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    foreach (FlowCanvasNode node in e.NewItems)
                        AddNodeVisual(node);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (FlowCanvasNode node in e.OldItems)
                        DetachNodeVisual(node);
                }
                break;

            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
                RebuildAllVisuals();
                break;
        }
    }

    private void RebuildAllVisuals()
    {
        if (_nodeLayer == null)
            return;

        // _visuals is exactly the set of nodes with an active PropertyChanged
        // subscription, so detaching through it also covers Reset/Clear,
        // where the collection no longer reports the removed items.
        foreach (FlowNodeVisual visual in _visuals.Values)
            visual.Node.PropertyChanged -= OnNodePropertyChanged;

        _nodeLayer.Children.Clear();
        if (_wireLayer != null)
        {
            _wireLayer.Children.Clear();
            EnsurePinnedWires(); // locked pinned wires survive collection rebuilds
        }

        _visuals.Clear();
        _wires.Clear();
        _selectedWire = null;
        _selectedNode = null;

        foreach (FlowCanvasNode node in _nodes)
        {
            node.PropertyChanged += OnNodePropertyChanged;
            AddVisualCore(node);
            if (node.IsSelected)
                _selectedNode = node;
        }

        // Pre-routed loads (DB workflows, drafts, imports) carry their routing
        // in the node objects BEFORE the canvas subscribed to PropertyChanged,
        // so no routing change ever fires here — wires must be built explicitly.
        foreach (FlowCanvasNode node in _nodes)
            UpdateWiresForNode(node);

        UpdateAllWireGeometry();
        UpdateTriggerCardPosition();
    }

    private void AddNodeVisual(FlowCanvasNode node)
    {
        if (_nodeLayer == null || _visuals.ContainsKey(node.Id))
            return;

        node.PropertyChanged += OnNodePropertyChanged;
        AddVisualCore(node);
        UpdateTriggerCardPosition();
    }

    private void AddVisualCore(FlowCanvasNode node)
    {
        if (_nodeLayer == null)
            return;

        FlowNodeVisual visual = new(node);
        visual.MouseLeftButtonDown += OnNodeMouseDown;
        visual.MouseMove += OnNodeMouseMove;
        visual.MouseLeftButtonUp += OnNodeMouseUp;
        visual.LostMouseCapture += OnNodeDragLostCapture;
        visual.OutputPortGrabbed += OnOutputPortGrabbed;
        visual.OutputPortDrag += OnOutputPortDrag;
        visual.OutputPortReleased += OnOutputPortReleased;
        visual.ComponentChipAddClicked += OnComponentChipAddClicked;
        visual.ComponentChipRemoveClicked += OnComponentChipRemoveClicked;
        visual.SizeChanged += (_, _) => UpdateWireGeometryFor(node);

        Canvas.SetLeft(visual, node.X);
        Canvas.SetTop(visual, node.Y);
        if (node.IsSelected)
            visual.RefreshChrome();

        _nodeLayer.Children.Add(visual);
        _visuals[node.Id] = visual;
    }

    private void OnComponentChipAddClicked(object? sender, (FlowCanvasNode Node, string Kind) e) =>
        ComponentAddRequested?.Invoke(this, (e.Node.Id, e.Kind));

    private void OnComponentChipRemoveClicked(object? sender, (FlowCanvasNode Node, string Kind, int Index) e) =>
        ComponentRemoveRequested?.Invoke(this, (e.Node.Id, e.Kind, e.Index));

    private void DetachNodeVisual(FlowCanvasNode node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;

        if (_visuals.TryGetValue(node.Id, out FlowNodeVisual? visual))
        {
            _visuals.Remove(node.Id);
            if (visual.IsMouseCaptured)
                visual.ReleaseMouseCapture();
            _nodeLayer?.Children.Remove(visual);
        }

        RemoveWiresSourcedFrom(node.Id);
        RemoveWiresTargeting(node.Id, clearSourceFields: true);

        // A deleted entry/error target unwires the matching pinned card and
        // must be reported so the editor can persist Workflow.EntryNodeId etc.
        if (string.Equals(_entryTargetId, node.Id, StringComparison.Ordinal))
        {
            _entryTargetId = null;
            EntryNodeChanged?.Invoke(this, null);
        }

        if (string.Equals(_errorTargetId, node.Id, StringComparison.Ordinal))
        {
            _errorTargetId = null;
            ErrorNodeChanged?.Invoke(this, null);
        }

        if (ReferenceEquals(_selectedNode, node))
        {
            _selectedNode = null;
            NodeSelected?.Invoke(this, null);
        }

        UpdateTriggerCardPosition(); // also refreshes the pinned wire geometry
    }

    private void RebuildNodeVisual(FlowCanvasNode node)
    {
        if (_nodeLayer == null)
            return;

        if (_visuals.Remove(node.Id, out FlowNodeVisual? old))
            _nodeLayer.Children.Remove(old);

        AddVisualCore(node);
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FlowCanvasNode node)
            return;

        string name = e.PropertyName ?? string.Empty;
        if (name == nameof(FlowCanvasNode.X))
        {
            if (_visuals.TryGetValue(node.Id, out FlowNodeVisual? visual))
                Canvas.SetLeft(visual, node.X);
            UpdateWireGeometryFor(node);
        }
        else if (name == nameof(FlowCanvasNode.Y))
        {
            if (_visuals.TryGetValue(node.Id, out FlowNodeVisual? visual))
                Canvas.SetTop(visual, node.Y);
            UpdateWireGeometryFor(node);
        }
        else if (name == nameof(FlowCanvasNode.Label))
        {
            if (_visuals.TryGetValue(node.Id, out FlowNodeVisual? visual))
                visual.RefreshLabel();
            RaiseGraphChanged();
        }
        else if (name == nameof(FlowCanvasNode.BlockType) || name == nameof(FlowCanvasNode.Config))
        {
            // Block type (and Config for switch cases) changes the port layout.
            RebuildNodeVisual(node);
            UpdateWiresForNode(node);
            UpdateWireGeometryFor(node);
        }
        else if (name == nameof(FlowCanvasNode.IsSelected))
        {
            SyncSelectionFromNode(node);
        }
        else if (name == nameof(FlowCanvasNode.NextTargetId)
                 || name == nameof(FlowCanvasNode.BranchTrueTargetId)
                 || name == nameof(FlowCanvasNode.BranchFalseTargetId)
                 || name == nameof(FlowCanvasNode.BranchErrorTargetId)
                 || name == nameof(FlowCanvasNode.BranchTargets))
        {
            if (!_syncingWires)
            {
                // The "⊕ Menu" chip's dim state reads the case:menu0 wiring
                // (the chip is clickable until its sub-card is wired) — an
                // unguarded wiring change on a block carrying a menu config
                // must rebuild the card so the chip stays truthful. Guarded
                // changes (AddComponentNode / DeleteNode) refresh the parent
                // visual explicitly at their call sites instead.
                if (FlowCanvasNode.ComponentHasMenu(node.Config))
                    RebuildNodeVisual(node);

                UpdateWiresForNode(node);
                UpdateWireGeometryFor(node);
            }
        }
    }

    // =====================================================================================
    // Wires
    // =====================================================================================

    private void UpdateWiresForNode(FlowCanvasNode source)
    {
        if (_wireLayer == null)
            return;

        for (int i = _wires.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_wires[i].Source, source))
                continue;

            if (ReferenceEquals(_selectedWire, _wires[i]))
                _selectedWire = null;
            _wireLayer.Children.Remove(_wires[i]);
            _wires.RemoveAt(i);
        }

        foreach (KeyValuePair<string, string?> wire in source.OutgoingWires)
        {
            if (string.IsNullOrEmpty(wire.Value))
                continue;

            FlowCanvasNode? target = FindNode(wire.Value!);
            if (target == null)
                continue; // dangling routing field (e.g. authored externally)

            // The grey-attachment rule doubles as the GEOMETRY rule: wires
            // leaving a grey port (component drop points, the loop's
            // "After loop"/"Loop times" ports, comparison case/else) render as
            // right-angle elbows; colored wires (blue main, green conditions,
            // amber error/default/case) stay beziers.
            Color color = PortColor(source, wire.Key);
            FlowWireVisual visual = new(source, wire.Key, wire.Value!, color)
            {
                Orthogonal = FlowCanvasPalette.IsAttachmentColor(color),
                ToolTip = $"{PortLabel(source, wire.Key)} → {target.Label}",
            };
            visual.MouseLeftButtonDown += OnWireMouseDown;
            visual.DisconnectRequested += OnWireDisconnectRequested;
            _wireLayer.Children.Add(visual);
            _wires.Add(visual);
            UpdateWireGeometry(visual);
        }
    }

    private void UpdateWireGeometry(FlowWireVisual wire)
    {
        FlowNodeVisual? sourceVisual = GetVisual(wire.Source.Id);
        FlowNodeVisual? targetVisual = GetVisual(wire.TargetId);
        if (sourceVisual == null || targetVisual == null)
        {
            wire.Visibility = Visibility.Collapsed;
            return;
        }

        wire.Visibility = Visibility.Visible;
        Point localStart = sourceVisual.GetOutputPortLocalPoint(wire.PortKey);
        Point start = new(wire.Source.X + localStart.X, wire.Source.Y + localStart.Y);

        Point localEnd = targetVisual.GetInputPortLocalPoint();
        Point end = new(targetVisual.Node.X + localEnd.X, targetVisual.Node.Y + localEnd.Y);

        wire.UpdateGeometry(start, end);
    }

    private void UpdateWireGeometryFor(FlowCanvasNode node)
    {
        foreach (FlowWireVisual wire in _wires)
        {
            if (ReferenceEquals(wire.Source, node) || string.Equals(wire.TargetId, node.Id, StringComparison.Ordinal))
                UpdateWireGeometry(wire);
        }

        UpdatePinnedWireGeometry(); // the locked entry/error wires follow node drags live
    }

    private void UpdateAllWireGeometry()
    {
        foreach (FlowWireVisual wire in _wires)
            UpdateWireGeometry(wire);
    }

    private void RemoveWiresSourcedFrom(string nodeId)
    {
        for (int i = _wires.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_wires[i].Source.Id, nodeId, StringComparison.Ordinal))
                continue;

            if (ReferenceEquals(_selectedWire, _wires[i]))
                _selectedWire = null;
            _wireLayer?.Children.Remove(_wires[i]);
            _wires.RemoveAt(i);
        }
    }

    private void RemoveWiresTargeting(string nodeId, bool clearSourceFields)
    {
        _syncingWires = true;
        try
        {
            for (int i = _wires.Count - 1; i >= 0; i--)
            {
                FlowWireVisual wire = _wires[i];
                if (!string.Equals(wire.TargetId, nodeId, StringComparison.Ordinal))
                    continue;

                if (clearSourceFields)
                    wire.Source.SetTarget(wire.PortKey, null);

                if (ReferenceEquals(_selectedWire, wire))
                    _selectedWire = null;
                _wireLayer?.Children.Remove(wire);
                _wires.RemoveAt(i);
            }
        }
        finally
        {
            _syncingWires = false;
        }
    }

    private void OnWireMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FlowWireVisual wire)
            return;

        Focus();
        SelectWire(wire);
        e.Handled = true;
    }

    private void SelectWire(FlowWireVisual? wire)
    {
        if (ReferenceEquals(_selectedWire, wire))
            return;

        _selectedWire?.IsSelected = false;
        _selectedWire = wire;
        if (wire != null)
            wire.IsSelected = true;
    }

    /// <summary>
    /// Click-to-disconnect on the ⊗ midpoint marker of a normal deletable
    /// wire (grey/amber wires render no marker): clears the
    /// source node's routing field for the wire's port
    /// (<see cref="FlowCanvasNode.SetTarget"/>(portKey, null)), which removes
    /// the wire visual, and raises <see cref="GraphChanged"/> so undo captures
    /// the disconnection. For a component drop-point elbow the orphaned
    /// button/menu sub-card is deleted too (its attachment is the wire). The
    /// wire body keeps click-to-select and Delete never deletes wires — only
    /// marker clicks and node deletion clear connections.
    /// </summary>
    private void OnWireDisconnectRequested(object? sender, EventArgs e)
    {
        if (sender is not FlowWireVisual wire)
            return;

        Focus();
        if (ReferenceEquals(_selectedWire, wire))
            SelectWire(null);

        _syncingWires = true; // suppress the PropertyChanged-driven rebuild — refresh explicitly below
        try
        {
            wire.Source.SetTarget(wire.PortKey, null);
        }
        finally
        {
            _syncingWires = false;
        }

        UpdateWiresForNode(wire.Source); // the routing field is now null → this wire visual is removed

        // Component sub-cards are ATTACHMENTS of their drop dot ("unwiring
        // hides them again"): disconnecting the grey elbow deletes the orphaned
        // button/menu card (which also frees the parent's chip for a re-add)
        // instead of leaving it floating unwired beside the node. DeleteNode
        // also rebuilds the parent card so its chips reflect the cleared port.
        if (FlowCanvasNode.IsComponentSlotPort(wire.PortKey) &&
            FindNode(wire.TargetId) is { } orphanedSubCard)
        {
            DeleteNode(orphanedSubCard.Id);
            return; // DeleteNode raised GraphChanged
        }

        RaiseGraphChanged();
    }

    private static Color PortColor(FlowCanvasNode node, string portKey)
    {
        foreach (PortDescriptor port in node.GetOutputPorts())
        {
            if (string.Equals(port.Key, portKey, StringComparison.Ordinal))
            {
                // ResolvePortColor is the single attachment-vs-main rule: it
                // overrides the descriptor grey with MainWire blue for plain
                // action "next" ports and leaves every attached port's color
                // (grey/green/amber) untouched.
                return FlowCanvasPalette.Color(FlowCanvasPalette.ResolvePortColor(node.BlockType, port.Key, port.Color));
            }
        }

        return FlowCanvasPalette.Color(FlowCanvasPalette.PortNeutral);
    }

    private static string PortLabel(FlowCanvasNode node, string portKey)
    {
        foreach (PortDescriptor port in node.GetOutputPorts())
        {
            if (string.Equals(port.Key, portKey, StringComparison.Ordinal))
                return port.Label;
        }

        return portKey;
    }

    // =====================================================================================
    // Connection drag + validation
    // =====================================================================================

    private void OnOutputPortGrabbed(object? sender, FlowPortEventArgs e)
    {
        if (_overlayLayer == null)
            return;

        _mode = DragMode.Connect;
        _connectNode = e.Node;
        _pendingStart = OutputPortWorldPoint(e.Node, e.PortKey);
        // The pending preview matches the style the finished wire will get:
        // grey attachment ports preview as elbows, colored ports as beziers.
        _pendingOrthogonal = FlowCanvasPalette.IsAttachmentColor(PortColor(e.Node, e.PortKey));

        _pendingWire = new Path
        {
            Stroke = FlowCanvasPalette.Brush(PortColor(e.Node, e.PortKey)),
            StrokeThickness = 2d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        _overlayLayer.Children.Add(_pendingWire);
        UpdatePendingWire(WorldFromPortArgs(e));
    }

    private void OnOutputPortDrag(object? sender, FlowPortEventArgs e)
    {
        if (_mode != DragMode.Connect || !ReferenceEquals(e.Node, _connectNode))
            return;

        UpdatePendingWire(WorldFromPortArgs(e));
    }

    private void OnOutputPortReleased(object? sender, FlowPortEventArgs e)
    {
        // Runs for both mouse-up and capture-lost; only the first call acts.
        if (_mode != DragMode.Connect || !ReferenceEquals(e.Node, _connectNode))
            return;

        FlowCanvasNode source = e.Node;
        FlowNodeVisual? targetVisual = FindNodeVisualAt(WorldFromPortArgs(e), GetVisual(source.Id));
        FlowCanvasNode? target = targetVisual?.Node;

        CleanupPendingWire();
        _mode = DragMode.None;
        _connectNode = null;

        if (target == null)
            return; // released over empty canvas — just cancel

        TryConnect(source, e.PortKey, target);
    }

    private void TryConnect(FlowCanvasNode source, string portKey, FlowCanvasNode target)
    {
        if (ReferenceEquals(source, target))
        {
            FlashRejected(source, portKey, target);
            return;
        }

        if (string.Equals(source.GetTarget(portKey), target.Id, StringComparison.Ordinal))
            return; // duplicate of the existing wire — silently ignore

        if (CreatesCycle(source, target))
        {
            FlashRejected(source, portKey, target);
            return;
        }

        // Input ports hold at most ONE incoming wire: clear whichever routing
        // entry (Next/BranchTrue/BranchFalse/BranchError/Branches) on any node
        // currently points at this target before connecting the new source.
        // Output ports already replace their own wire on re-drag.
        DisplaceIncomingConnections(target);

        source.SetTarget(portKey, target.Id);
        UpdateWiresForNode(source);
        RaiseGraphChanged();
    }

    /// <summary>
    /// Enforces the single-connection rule per INPUT port: every wire that
    /// currently lands on <paramref name="target"/> (from any node and any
    /// output port) is removed by clearing the previous source's routing field.
    /// Visuals are refreshed via <see cref="UpdateWiresForNode"/> under the
    /// <c>_syncingWires</c> guard (mirrors <see cref="DeleteNode(string)"/>).
    /// </summary>
    private void DisplaceIncomingConnections(FlowCanvasNode target)
    {
        _syncingWires = true;
        try
        {
            foreach (FlowCanvasNode node in _nodes)
            {
                List<KeyValuePair<string, string?>>? toClear = null;
                foreach (KeyValuePair<string, string?> wire in node.OutgoingWires)
                {
                    if (!string.IsNullOrEmpty(wire.Value) && string.Equals(wire.Value, target.Id, StringComparison.Ordinal))
                        (toClear ??= new List<KeyValuePair<string, string?>>()).Add(wire);
                }

                if (toClear == null)
                    continue;

                foreach (KeyValuePair<string, string?> wire in toClear)
                    node.SetTarget(wire.Key, null);

                UpdateWiresForNode(node); // PropertyChanged is suppressed by the guard — refresh explicitly
            }
        }
        finally
        {
            _syncingWires = false;
        }
    }

    /// <summary>True when wiring source → target would close a directed cycle.</summary>
    private bool CreatesCycle(FlowCanvasNode source, FlowCanvasNode target)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        Stack<string> stack = new();
        stack.Push(target.Id);

        while (stack.Count > 0)
        {
            string id = stack.Pop();
            if (string.Equals(id, source.Id, StringComparison.Ordinal))
                return true;

            if (!visited.Add(id))
                continue;

            FlowCanvasNode? node = FindNode(id);
            if (node == null)
                continue;

            foreach (KeyValuePair<string, string?> wire in node.OutgoingWires)
            {
                if (!string.IsNullOrEmpty(wire.Value))
                    stack.Push(wire.Value!);
            }
        }

        return false;
    }

    /// <summary>Brief red flash of the attempted wire before it disappears.</summary>
    private void FlashRejected(FlowCanvasNode source, string portKey, FlowCanvasNode target)
    {
        if (_overlayLayer == null)
            return;

        Path flash = new()
        {
            Stroke = FlowCanvasPalette.Brush(FlowCanvasPalette.Rejected),
            StrokeThickness = 2.5d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = FlowWireVisual.CreateBezier(OutputPortWorldPoint(source, portKey), InputPortWorldPoint(target)),
            IsHitTestVisible = false,
        };
        _overlayLayer.Children.Add(flash);

        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(320) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _overlayLayer?.Children.Remove(flash);
        };
        timer.Start();
    }

    private void UpdatePendingWire(Point end)
    {
        if (_pendingWire == null)
            return;

        _pendingWire.Data = _pendingOrthogonal
            ? FlowWireVisual.CreateElbow(_pendingStart, end)
            : FlowWireVisual.CreateBezier(_pendingStart, end);
    }

    private void CleanupPendingWire()
    {
        if (_pendingWire == null)
            return;

        _overlayLayer?.Children.Remove(_pendingWire);
        _pendingWire = null;
    }

    private Point OutputPortWorldPoint(FlowCanvasNode node, string portKey)
    {
        FlowNodeVisual? visual = GetVisual(node.Id);
        Point local = visual != null
            ? visual.GetOutputPortLocalPoint(portKey)
            : new Point(FlowNodeVisual.WidthFor(node) / 2d, FlowNodeVisual.DefaultHeightFor(node));
        return new Point(node.X + local.X, node.Y + local.Y);
    }

    private Point InputPortWorldPoint(FlowCanvasNode node)
    {
        FlowNodeVisual? visual = GetVisual(node.Id);
        Point local = visual != null ? visual.GetInputPortLocalPoint() : new Point(FlowNodeVisual.WidthFor(node) / 2d, 0d);
        return new Point(node.X + local.X, node.Y + local.Y);
    }

    private Point WorldFromPortArgs(FlowPortEventArgs e) => new(e.Node.X + e.LocalPosition.X, e.Node.Y + e.LocalPosition.Y);

    private FlowNodeVisual? FindNodeVisualAt(Point world, FlowNodeVisual? exclude)
    {
        if (_nodeLayer == null)
            return null;

        for (int i = _nodeLayer.Children.Count - 1; i >= 0; i--)
        {
            if (_nodeLayer.Children[i] is not FlowNodeVisual visual || ReferenceEquals(visual, exclude))
                continue;

            double width = visual.ActualWidth > 0 ? visual.ActualWidth : FlowNodeVisual.WidthFor(visual.Node);
            double height = visual.ActualHeight > 0 ? visual.ActualHeight : FlowNodeVisual.DefaultHeightFor(visual.Node);
            Rect bounds = new(visual.Node.X, visual.Node.Y, width, height);
            bounds.Inflate(6d, 6d);
            if (bounds.Contains(world))
                return visual;
        }

        return null;
    }

    // =====================================================================================
    // Node drag / selection
    // =====================================================================================

    private void OnNodeMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FlowNodeVisual visual || e.ChangedButton != MouseButton.Left)
            return;

        Focus();

        if (e.ClickCount == 2)
        {
            visual.BeginRename();
            e.Handled = true;
            return;
        }

        SelectNode(visual.Node);

        _mode = DragMode.NodeDrag;
        _dragVisual = visual;
        _dragStartWorld = _contentCanvas != null ? e.GetPosition(_contentCanvas) : e.GetPosition(visual);
        _nodeStartX = visual.Node.X;
        _nodeStartY = visual.Node.Y;
        _nodeMoved = false;
        visual.CaptureMouse();
        e.Handled = true;
    }

    private void OnNodeMouseMove(object? sender, MouseEventArgs e)
    {
        if (_mode != DragMode.NodeDrag || sender is not FlowNodeVisual visual || !ReferenceEquals(visual, _dragVisual))
            return;
        if (_contentCanvas == null)
            return;

        Point world = e.GetPosition(_contentCanvas);
        double dx = world.X - _dragStartWorld.X;
        double dy = world.Y - _dragStartWorld.Y;
        if (Math.Abs(dx) > 0.5d || Math.Abs(dy) > 0.5d)
            _nodeMoved = true;
        if (!_nodeMoved)
            return;

        double width = visual.ActualWidth > 0 ? visual.ActualWidth : FlowNodeVisual.WidthFor(visual.Node);
        double height = visual.ActualHeight > 0 ? visual.ActualHeight : FlowNodeVisual.DefaultHeightFor(visual.Node);
        visual.Node.X = ClampX(_nodeStartX + dx, width);
        visual.Node.Y = ClampY(_nodeStartY + dy, height);
        e.Handled = true;
    }

    private void OnNodeMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_mode != DragMode.NodeDrag || sender is not FlowNodeVisual visual || !ReferenceEquals(visual, _dragVisual))
            return;

        if (visual.IsMouseCaptured)
            visual.ReleaseMouseCapture();
        _mode = DragMode.None;
        _dragVisual = null;
        if (_nodeMoved)
            RaiseGraphChanged(); // move events throttle to drag end
        e.Handled = true;
    }

    private void OnNodeDragLostCapture(object? sender, MouseEventArgs e)
    {
        if (_mode != DragMode.NodeDrag || sender is not FlowNodeVisual visual || !ReferenceEquals(visual, _dragVisual))
            return;

        _mode = DragMode.None;
        _dragVisual = null;
        if (_nodeMoved)
            RaiseGraphChanged();
    }

    private void SelectNode(FlowCanvasNode? node) => SelectNode(node, raiseEvent: true);

    private void SelectNode(FlowCanvasNode? node, bool raiseEvent)
    {
        if (_selectionSync)
            return;

        _selectionSync = true;
        try
        {
            if (!ReferenceEquals(_selectedNode, node))
            {
                FlowCanvasNode? previous = _selectedNode;
                _selectedNode = node;

                if (previous != null)
                {
                    previous.IsSelected = false;
                    if (_visuals.TryGetValue(previous.Id, out FlowNodeVisual? oldVisual))
                        oldVisual.RefreshChrome();
                }

                if (node != null)
                {
                    node.IsSelected = true;
                    if (_visuals.TryGetValue(node.Id, out FlowNodeVisual? newVisual))
                        newVisual.RefreshChrome();
                }
            }

            SelectWire(null);
            if (raiseEvent)
                NodeSelected?.Invoke(this, node);
        }
        finally
        {
            _selectionSync = false;
        }
    }

    private void SyncSelectionFromNode(FlowCanvasNode node)
    {
        if (_selectionSync)
            return;

        if (node.IsSelected)
        {
            SelectNode(node);
        }
        else if (ReferenceEquals(_selectedNode, node))
        {
            _selectedNode = null;
            if (_visuals.TryGetValue(node.Id, out FlowNodeVisual? visual))
                visual.RefreshChrome();
            NodeSelected?.Invoke(this, null);
        }
    }

    // =====================================================================================
    // Pan
    // =====================================================================================

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_contentCanvas == null || _scrollViewer == null)
            return;
        if (!ReferenceEquals(e.OriginalSource, _contentCanvas))
            return; // bare canvas background only — nodes/ports/wires handle themselves

        Focus();
        SelectNode(null);
        SelectWire(null);

        _mode = DragMode.Pan;
        _panStartScreen = e.GetPosition(_scrollViewer);
        _panStartOffsetX = _scrollViewer.HorizontalOffset;
        _panStartOffsetY = _scrollViewer.VerticalOffset;
        _panMoved = false;
        _contentCanvas.CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_mode != DragMode.Pan || _scrollViewer == null || _contentCanvas == null)
            return;
        if (!_contentCanvas.IsMouseCaptured)
            return;

        Point current = e.GetPosition(_scrollViewer);
        double dx = current.X - _panStartScreen.X;
        double dy = current.Y - _panStartScreen.Y;
        if (Math.Abs(dx) > 3d || Math.Abs(dy) > 3d)
            _panMoved = true;

        _scrollViewer.ScrollToHorizontalOffset(_panStartOffsetX - dx);
        _scrollViewer.ScrollToVerticalOffset(_panStartOffsetY - dy);
        e.Handled = true;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode != DragMode.Pan)
            return;

        if (_contentCanvas != null && _contentCanvas.IsMouseCaptured)
            _contentCanvas.ReleaseMouseCapture();
        Cursor = null;
        _mode = DragMode.None;
        e.Handled = true;
    }

    private void OnCanvasLostCapture(object? sender, MouseEventArgs e)
    {
        if (_mode == DragMode.Pan)
        {
            _mode = DragMode.None;
            Cursor = null;
        }
    }

    // =====================================================================================
    // Zoom
    // =====================================================================================

    private void ZoomAt(double newZoom, Point viewportPoint)
    {
        double clamped = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(clamped - Zoom) < 0.0001d)
            return;

        ScrollViewer? scrollViewer = _scrollViewer;
        if (scrollViewer == null)
        {
            Zoom = clamped;
            return;
        }

        // World point currently under the anchor (world == canvas coordinates).
        double worldX = (scrollViewer.HorizontalOffset + viewportPoint.X) / Zoom;
        double worldY = (scrollViewer.VerticalOffset + viewportPoint.Y) / Zoom;

        Zoom = clamped;
        scrollViewer.UpdateLayout(); // extents must catch up with the new scale before scrolling

        scrollViewer.ScrollToHorizontalOffset(worldX * clamped - viewportPoint.X);
        scrollViewer.ScrollToVerticalOffset(worldY * clamped - viewportPoint.Y);
    }

    private Point ViewportCenter() => new(_scrollViewer?.ViewportWidth / 2d ?? 400d, _scrollViewer?.ViewportHeight / 2d ?? 300d);

    /// <inheritdoc />
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (_scrollViewer == null)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        double factor = e.Delta > 0 ? ZoomStep : 1d / ZoomStep;
        ZoomAt(Zoom * factor, e.GetPosition(_scrollViewer));
        e.Handled = true;
    }

    // =====================================================================================
    // Keyboard
    // =====================================================================================

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Keyboard.FocusedElement is TextBoxBase)
            return; // let the in-place label editor edit its text

        if (e.Key == Key.Delete)
        {
            if (_selectedNode != null)
            {
                // Attachment cards ("loop_after"/"loop_body"/"comparison_case"/
                // "comparison_else") are PROTECTED: Delete never removes them —
                // their lifecycle belongs to the editor (parent node / routing).
                if (!FlowNodeVisual.IsProtectedType(_selectedNode.BlockType))
                    DeleteNode(_selectedNode.Id);
                e.Handled = true;
            }
            else if (_selectedWire != null)
            {
                // Wires are LOCKED — Delete never removes one (rewiring is done
                // by dragging again; node deletion clears its own wires).
                // Delete only clears the wire highlight.
                SelectWire(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_selectedNode != null && !FlowNodeVisual.IsProtectedType(_selectedNode.BlockType))
            {
                DuplicateSelected();
                e.Handled = true;
            }
        }
    }

    private void DuplicateSelected()
    {
        FlowCanvasNode? source = _selectedNode;
        if (source == null)
            return;

        FlowCanvasNode copy = new()
        {
            BlockType = source.BlockType,
            Label = source.Label,
            X = ClampX(source.X + 24d, FlowNodeVisual.WidthFor(source)),
            Y = ClampY(source.Y + 24d, FlowNodeVisual.DefaultHeightFor(source)),
            Config = new Dictionary<string, string>(source.Config),
        };
        // Id defaults to a fresh GUID "N"; wires are intentionally not copied.

        _nodes.Add(copy);
        SelectNode(copy);
        RaiseGraphChanged();
    }

    // =====================================================================================
    // Auto-organize layout (⤓ canvas button)
    // =====================================================================================

    /// <summary>
    /// Deterministic layered auto-organize ("organize all blocks based on
    /// their connections starting from the top going down"). Builds the graph
    /// from every <see cref="FlowCanvasNode.OutgoingWires"/> entry with a
    /// valid target (Next/BranchTrue/BranchFalse/BranchError/Branches/case
    /// ports), picks tree roots in priority order — (a) the entry node
    /// (<see cref="EntryTargetId"/>), (b) remaining unreferenced main nodes,
    /// (c) component sub-node chains ("button_start"/"menu_start" roots),
    /// (d) the error-handler chain (<see cref="ErrorTargetId"/>) — and lays
    /// each tree out top-to-bottom: depth = longest path from the root
    /// (iterative relaxation so condition False-branches don't understate
    /// depth; capped at the node count so even an externally-authored cycle
    /// terminates), Y = treeTopY + depth * <see cref="OrganizeRowHeight"/>,
    /// and each depth level gets consecutive X slots starting at treeX
    /// (<see cref="OrganizeSlotWidth"/> apart) so branches fan out while
    /// chains stay in one column. treeX advances past each tree (its column
    /// count, guarded to at least the widest depth level so fan-outs never
    /// overlap the next tree). Roots with no edges (lone blocks) share a final
    /// column. treeTopY/treeX start at the current top-most/left-most node
    /// (clamped ≥ 60) so the first tree lands roughly where the graph was.
    /// Positions snap (no animation), then a single <see cref="GraphChanged"/>
    /// lets undo capture the whole rearrangement, followed by
    /// <see cref="FitToContent"/> for a tidy view.
    /// </summary>
    private void AutoOrganizeLayout()
    {
        if (_nodes.Count == 0)
            return;

        // ---- 1. Graph: node id → child ids (valid targets, port order) + incoming set.
        Dictionary<string, List<string>> children = new(StringComparer.Ordinal);
        HashSet<string> hasIncoming = new(StringComparer.Ordinal);
        foreach (FlowCanvasNode node in _nodes)
        {
            List<string> targets = new();
            foreach (KeyValuePair<string, string?> wire in node.OutgoingWires)
            {
                if (string.IsNullOrEmpty(wire.Value) || FindNode(wire.Value!) == null)
                    continue;
                if (!targets.Contains(wire.Value!))
                    targets.Add(wire.Value!);
            }

            children[node.Id] = targets;
            foreach (string target in targets)
                hasIncoming.Add(target);
        }

        // ---- 2. Roots in priority order: (a) entry, (b) unreferenced main
        // nodes, (c) component sub-node chains, (d) the error-handler chain.
        // Roots with no outgoing edges are lone blocks → final column.
        HashSet<string> assigned = new(StringComparer.Ordinal);
        List<FlowCanvasNode> treeRoots = new();
        List<FlowCanvasNode> loneRoots = new();

        void AddRoot(FlowCanvasNode root)
        {
            if (!assigned.Add(root.Id))
                return;

            (children[root.Id].Count > 0 ? treeRoots : loneRoots).Add(root);
        }

        FlowCanvasNode? entry = _entryTargetId != null ? FindNode(_entryTargetId) : null;
        if (entry != null && !hasIncoming.Contains(entry.Id))
            AddRoot(entry); // (a)

        string? errorRootId = _errorTargetId;
        foreach (FlowCanvasNode node in _nodes)
        {
            if (!hasIncoming.Contains(node.Id)
                && !FlowNodeVisual.IsComponentType(node.BlockType)
                && !string.Equals(node.Id, errorRootId, StringComparison.Ordinal))
            {
                AddRoot(node); // (b)
            }
        }

        foreach (FlowCanvasNode node in _nodes)
        {
            if (!hasIncoming.Contains(node.Id) && FlowNodeVisual.IsComponentType(node.BlockType))
                AddRoot(node); // (c)
        }

        FlowCanvasNode? errorRoot = errorRootId != null ? FindNode(errorRootId) : null;
        if (errorRoot != null && !hasIncoming.Contains(errorRoot.Id))
            AddRoot(errorRoot); // (d)

        // ---- 3. Land the first tree roughly where the graph was.
        double minY = double.MaxValue;
        double minX = double.MaxValue;
        foreach (FlowCanvasNode node in _nodes)
        {
            minY = Math.Min(minY, node.Y);
            minX = Math.Min(minX, node.X);
        }

        double treeTopY = Math.Max(60d, minY);
        double treeX = Math.Max(60d, minX);

        foreach (FlowCanvasNode root in treeRoots)
        {
            // BFS discovery order — children follow their parent's order and
            // the source node's port order (next/true/false/error/cases).
            List<FlowCanvasNode> order = new();
            HashSet<string> visited = new(StringComparer.Ordinal) { root.Id };
            Queue<FlowCanvasNode> queue = new();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                FlowCanvasNode current = queue.Dequeue();
                order.Add(current);
                foreach (string childId in children[current.Id])
                {
                    FlowCanvasNode? child = FindNode(childId);
                    if (child == null || !visited.Add(childId))
                        continue;
                    queue.Enqueue(child);
                }
            }

            // Longest-path depth from the root via iterative relaxation; the
            // iteration cap (node count) guarantees termination even if
            // externally-authored data contains a cycle.
            Dictionary<string, int> depth = new(StringComparer.Ordinal);
            foreach (FlowCanvasNode node in order)
                depth[node.Id] = 0;

            for (int iteration = 0; iteration < order.Count; iteration++)
            {
                bool changed = false;
                foreach (FlowCanvasNode node in order)
                {
                    foreach (string childId in children[node.Id])
                    {
                        if (depth.TryGetValue(childId, out int childDepth)
                            && childDepth < depth[node.Id] + 1)
                        {
                            depth[childId] = childDepth + 1;
                            changed = true;
                        }
                    }
                }

                if (!changed)
                    break;
            }

            // Each depth level is its own horizontal run of slots starting at
            // treeX, so branches fan out while straight chains stay vertical.
            int maxDepth = 0;
            int widestLevel = 0;
            Dictionary<int, int> slotByDepth = new();
            foreach (FlowCanvasNode node in order)
            {
                int level = depth[node.Id];
                int slot = slotByDepth.TryGetValue(level, out int next) ? next : 0;
                slotByDepth[level] = slot + 1;
                maxDepth = Math.Max(maxDepth, level);
                widestLevel = Math.Max(widestLevel, slot + 1);

                node.X = ClampX(treeX + slot * OrganizeSlotWidth, FlowNodeVisual.WidthFor(node));
                node.Y = ClampY(treeTopY + level * OrganizeRowHeight, FlowNodeVisual.DefaultHeightFor(node));
                assigned.Add(node.Id);
            }

            // Advance past this tree; the widest-level guard keeps a wide
            // fan-out (e.g. a switch with many cases) from overlapping the
            // next tree even when it exceeds maxDepth + 1 columns.
            treeX += Math.Max(maxDepth + 1, widestLevel) * OrganizeSlotWidth + OrganizeTreeGap;
        }

        // ---- 4. Final column: lone roots (priority order) + any node the
        // trees couldn't reach (defensive against externally-authored cycles).
        int loneIndex = 0;
        foreach (FlowCanvasNode root in loneRoots)
        {
            root.X = ClampX(treeX, FlowNodeVisual.WidthFor(root));
            root.Y = ClampY(treeTopY + loneIndex * OrganizeRowHeight, FlowNodeVisual.DefaultHeightFor(root));
            loneIndex++;
        }

        foreach (FlowCanvasNode node in _nodes)
        {
            if (assigned.Contains(node.Id))
                continue;

            node.X = ClampX(treeX, FlowNodeVisual.WidthFor(node));
            node.Y = ClampY(treeTopY + loneIndex * OrganizeRowHeight, FlowNodeVisual.DefaultHeightFor(node));
            loneIndex++;
        }

        UpdateTriggerCardPosition(); // re-centers the trigger card + pinned wires over the moved content
        RaiseGraphChanged();         // one event — undo captures the whole rearrangement
        FitToContent();
    }

    // =====================================================================================
    // Palette drag & drop
    // =====================================================================================

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(BlockTypeDataFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(BlockTypeDataFormat))
            return;
        if (e.Data.GetData(BlockTypeDataFormat) is not string blockType || string.IsNullOrWhiteSpace(blockType))
            return;

        // GetPosition against the world canvas already accounts for pan + zoom.
        Point world = _contentCanvas != null ? e.GetPosition(_contentCanvas) : e.GetPosition(this);
        FlowCanvasNode node = CreateNode(blockType, world.X - FlowNodeVisual.NodeWidth / 2d, world.Y - 24d);
        SelectNode(node);
        e.Handled = true;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private FlowCanvasNode? FindNode(string id)
    {
        foreach (FlowCanvasNode node in _nodes)
        {
            if (string.Equals(node.Id, id, StringComparison.Ordinal))
                return node;
        }

        return null;
    }

    private FlowNodeVisual? GetVisual(string nodeId) => _visuals.TryGetValue(nodeId, out FlowNodeVisual? visual) ? visual : null;

    private void RaiseGraphChanged() => GraphChanged?.Invoke(this, EventArgs.Empty);

    private static double ClampX(double value, double width)
    {
        double max = Math.Max(4d, CanvasWidth - width - 4d);
        return Math.Clamp(value, 4d, max);
    }

    private static double ClampY(double value, double height)
    {
        double max = Math.Max(4d, CanvasHeight - height - 4d);
        return Math.Clamp(value, 4d, max);
    }

    /// <summary>Current pointer interaction state.</summary>
    private enum DragMode
    {
        None,
        Pan,
        NodeDrag,
        Connect,
        ConnectEntry, // dragging the trigger card's output port (sets EntryTargetId)
        ConnectError, // dragging the error card's output port (sets ErrorTargetId)
    }
}
