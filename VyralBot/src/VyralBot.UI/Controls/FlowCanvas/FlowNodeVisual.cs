using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// Port/wire drag payload: the source node, its port key and the pointer
/// position relative to the node visual's top-left corner (world position =
/// Node.X/Y + LocalPosition).
/// </summary>
internal sealed class FlowPortEventArgs : EventArgs
{
    internal FlowPortEventArgs(FlowCanvasNode node, string portKey, Point localPosition)
    {
        Node = node;
        PortKey = portKey;
        LocalPosition = localPosition;
    }

    /// <summary>Node whose output port is being dragged.</summary>
    internal FlowCanvasNode Node { get; }

    /// <summary>Output port key ("next", "true", "case:x", ...).</summary>
    internal string PortKey { get; }

    /// <summary>Pointer position relative to the node visual's top-left corner.</summary>
    internal Point LocalPosition { get; }
}

/// <summary>
/// On-canvas visual for one <see cref="FlowCanvasNode"/> (BotGhost-style card):
/// a dark rounded card with a colored rounded-square icon chip on the left
/// (BlockCatalog color, white emoji glyph) and a text column on the right —
/// the user-editable label (bold white) over the block's catalog description
/// (grey, wrapped, max 2 lines with ellipsis; full text stays in the ToolTip).
/// One solid input port straddles the top edge (center) and the block's output
/// ports (colors resolved through FlowCanvasPalette.ResolvePortColor) are
/// evenly spaced along the bottom edge. Selection renders as a dashed indigo
/// outline with a soft glow.
/// <para>
/// COMPONENT CHIPS: message blocks ("send_message"/"send_embed", or any node
/// whose Config carries "Buttons"/"Menu") render two LIGHT chips just below
/// the card — "⊕ Button" anchored under the bottom-LEFT corner and "⊕ Menu"
/// under the bottom-RIGHT corner (white-ish background, dark text, thin
/// border, 4px corners, always fully labeled). Clicking raises
/// <see cref="ComponentChipAddClicked"/> (re-exposed by the canvas as its
/// public ComponentAddRequested); the button chip dims at the 5-button cap
/// and the menu chip dims once its menu sub-card is wired — a configured menu
/// with a missing card keeps the chip clickable so a press re-creates it.
/// Chips are hit-testable, never start a node drag.
/// </para>
/// <para>
/// COMPONENT DROP DOTS: every button/menu a message node carries derives a
/// grey drop-point port (FlowCanvasNode.GetOutputPorts: "case:buttonN" /
/// "case:menu0"). These are NOT drawn in the regular bottom port row — each
/// renders as a small 8px grey dot just below the card's bottom edge at slot
/// x = width·(i+1)/(n+1), and its wires leave from there (the control draws
/// grey right-angle elbows down to the component sub-cards).
/// </para>
/// <para>
/// COMPONENT SUB-NODES: the two special non-palette block types
/// "button_start"/"menu_start" (placed by the host via
/// FlowCanvasControl.AddComponentNode) render as small (~150px) cards with
/// hardcoded visuals — red 👍 "Button" and blue ☰ "Select Menu" chips — plus
/// the standard input/output ports and drag/wire behavior.
/// </para>
/// <para>
/// ATTACHMENT CARDS: four special non-palette block types render as small
/// (~170px) BotGhost attachment cards — "loop_after" ("After loop"), 
/// "loop_body" ("Loop # times") as plain dark cards WITHOUT an icon chip,
/// "comparison_case" (green "?" chip, title = the node Label) and
/// "comparison_else" (red "?" chip, "Else") — each with a grey input dot on
/// top, a BLUE main-flow output dot on the bottom (their outgoing wires are
/// normal blue beziers) and full drag/select behavior. They are protected:
/// the Delete key never removes one (the editor owns their lifecycle).
/// </para>
/// The control owns all interaction; this class only forwards raw port-drag
/// and chip events and manages the double-click in-place label editor.
/// </summary>
internal sealed class FlowNodeVisual : Border
{
    /// <summary>Fixed node width — keeps port spacing deterministic for wire math.</summary>
    internal const double NodeWidth = 208d;

    /// <summary>Width of the small button/menu component sub-node cards.</summary>
    internal const double ComponentNodeWidth = 150d;

    /// <summary>Width of the small attachment cards (loop/comparison branch cards).</summary>
    internal const double AttachmentCardWidth = 170d;

    /// <summary>Fallback height used before the first WPF measure pass.</summary>
    internal const double DefaultHeight = 60d;

    /// <summary>Fallback height for small component sub-node cards.</summary>
    internal const double ComponentDefaultHeight = 48d;

    private const double ChipSize = 28d;
    private const double ComponentChipSize = 22d;
    private const double SlotDotSize = 8d;   // grey component drop dots under a message card
    private const double SlotDotDrop = 2d;   // drop-dot center sits this far below the card's bottom edge

    /// <summary>Hardcoded visuals for the special non-palette component block types.</summary>
    private static readonly Dictionary<string, (string Icon, string Color, string Subtitle)> ComponentTypes =
        new(StringComparer.Ordinal)
        {
            ["button_start"] = ("👍", FlowCanvasPalette.ButtonStartAccent, "Button"),
            ["menu_start"] = ("☰", FlowCanvasPalette.MenuStartAccent, "Select Menu"),
        };

    /// <summary>
    /// Hardcoded visuals for the special non-palette attachment card types:
    /// fallback title (shown when the node Label is empty), grey subtitle and
    /// the optional rounded "?" chip (null chip color = plain card, no chip).
    /// </summary>
    private static readonly Dictionary<string, (string FallbackTitle, string Subtitle, string? ChipColor, string ChipGlyph)> AttachmentCardTypes =
        new(StringComparer.Ordinal)
        {
            ["loop_after"] = ("After loop", "Run when the loop has finished", null, string.Empty),
            ["loop_body"] = ("Loop # times", "Connected Actions & Conditions will be executed # times.", null, string.Empty),
            ["comparison_case"] = ("Case", "Comparison branch", FlowCanvasPalette.ComparisonCaseAccent, "?"),
            ["comparison_else"] = ("Else", "Else", FlowCanvasPalette.ComparisonElseAccent, "?"),
        };

    /// <summary>Block types that can carry Discord components (match the editor's IsMessageBlock).</summary>
    internal static bool IsComponentCapableBlock(string blockType) =>
        string.Equals(blockType, "send_message", StringComparison.OrdinalIgnoreCase)
        || string.Equals(blockType, "send_embed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Maximum number of buttons before the "⊕ Button" chip dims.</summary>
    private const int MaxButtons = 5;

    private readonly TextBlock _labelText;
    private readonly Rectangle _selectionOutline;
    private readonly Dictionary<string, int> _portIndex = new(StringComparer.Ordinal);
    private readonly List<string> _slotPortKeys = new(); // derived drop-point ports, in slot order
    private readonly bool _isComponentNode;
    private readonly (string FallbackTitle, string Subtitle, string? ChipColor, string ChipGlyph)? _attachmentCard;
    private TextBox? _labelEditor;
    private bool _isMouseOver;

    /// <summary>Raised when the user presses an output port (wire drag begins).</summary>
    internal event EventHandler<FlowPortEventArgs>? OutputPortGrabbed;

    /// <summary>Raised while a wire is being dragged from an output port.</summary>
    internal event EventHandler<FlowPortEventArgs>? OutputPortDrag;

    /// <summary>Raised when a wire drag ends (mouse up) or is aborted (capture lost).</summary>
    internal event EventHandler<FlowPortEventArgs>? OutputPortReleased;

    /// <summary>
    /// Raised when a "⊕ component chip is clicked (kind "button" or "menu").
    /// </summary>
    internal event EventHandler<(FlowCanvasNode Node, string Kind)>? ComponentChipAddClicked;

    /// <summary>
    /// Kept for host compatibility (the canvas re-exposes it as
    /// ComponentRemoveRequested) — the BotGhost-style light chips no longer
    /// carry per-item ✕ glyphs, so the canvas UI no longer raises this;
    /// component removal happens through the editor's settings panel.
    /// </summary>
    internal event EventHandler<(FlowCanvasNode Node, string Kind, int Index)>? ComponentChipRemoveClicked;

    internal FlowNodeVisual(FlowCanvasNode node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        _isComponentNode = IsComponentType(node.BlockType);
        _attachmentCard = AttachmentCardTypes.TryGetValue(node.BlockType, out (string FallbackTitle, string Subtitle, string? ChipColor, string ChipGlyph) style)
            ? style
            : null;
        bool isSmallCard = _isComponentNode || _attachmentCard != null;

        Width = WidthFor(node);
        MinHeight = isSmallCard ? ComponentDefaultHeight : 52d;
        CornerRadius = new CornerRadius(8d);
        Background = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBackground);
        BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBorder);
        BorderThickness = new Thickness(1d);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;

        // Component sub-nodes and attachment cards use hardcoded visuals
        // (BlockCatalog.Find returns null for them); everything else falls
        // back to a grey chip.
        BlockDefinition? block = BlockCatalog.Find(node.BlockType);
        string icon = block?.Icon ?? "▪";
        string accent = block?.Color ?? FlowCanvasPalette.PortNeutral;
        string description = block?.Description ?? string.Empty;
        if (_isComponentNode)
        {
            (icon, accent, description) = ComponentTypes[node.BlockType];
        }
        else if (_attachmentCard != null)
        {
            icon = _attachmentCard.Value.ChipGlyph;
            accent = _attachmentCard.Value.ChipColor ?? FlowCanvasPalette.PortNeutral;
            description = _attachmentCard.Value.Subtitle;
        }

        if (!string.IsNullOrWhiteSpace(description))
            ToolTip = description;

        Grid root = new();

        // ---- card content: colored icon chip + label/description column ----
        // Plain attachment cards (loop_after / loop_body) have NO chip column —
        // just the bold title over the grey subtitle, full-width.
        bool hasChipColumn = !(_attachmentCard != null && _attachmentCard.Value.ChipColor == null);
        double chipSize = _isComponentNode ? ComponentChipSize : _attachmentCard != null ? ComponentChipSize : ChipSize;
        double labelFontSize = isSmallCard ? 12d : 13d;
        Grid content = new() { Margin = new Thickness(10d, 8d, 10d, 8d) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hasChipColumn ? chipSize : 0d) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

        Border? chip = null;
        if (hasChipColumn)
        {
            chip = new Border
            {
                Width = chipSize,
                Height = chipSize,
                CornerRadius = new CornerRadius(6d),
                Background = FlowCanvasPalette.Brush(accent),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = icon,
                    FontSize = isSmallCard ? 12d : 14d,
                    FontWeight = _attachmentCard != null ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(chip, 0);
            content.Children.Add(chip);
        }

        StackPanel textPanel = new() { Margin = new Thickness(hasChipColumn ? 9d : 0d, 0d, 0d, 0d), VerticalAlignment = VerticalAlignment.Center };

        // Attachment cards fall back to their hardcoded title when the Label
        // is empty (the editor normally sets "After loop" / "Loop # times" /
        // the rule summary / "Else" — the Label stays the displayed title).
        string title = node.Label;
        if (_attachmentCard != null && string.IsNullOrWhiteSpace(title))
            title = _attachmentCard.Value.FallbackTitle;

        _labelText = new TextBlock
        {
            Text = title,
            FontSize = labelFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        TextBlock descriptionText = new()
        {
            Text = description,
            FontSize = 10.5d,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.DescriptionText),
            TextWrapping = TextWrapping.Wrap,
            Height = 28d, // ~2 lines at 10.5px; WPF TextBlock has no MaxLines
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0d, 2d, 0d, 0d),
            Visibility = string.IsNullOrWhiteSpace(description) ? Visibility.Collapsed : Visibility.Visible,
        };

        textPanel.Children.Add(_labelText);
        textPanel.Children.Add(descriptionText);
        Grid.SetColumn(textPanel, 1);
        content.Children.Add(textPanel);
        root.Children.Add(content);

        // ---- dashed selection outline (indigo, drawn over the card edge) ----
        _selectionOutline = new Rectangle
        {
            RadiusX = 8d,
            RadiusY = 8d,
            StrokeThickness = 1.5d,
            StrokeDashArray = new DoubleCollection { 4d, 2d },
            Margin = new Thickness(-1d), // centers the 1.5px stroke on the card's outer edge
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_selectionOutline);

        // ---- port layer: input port top-center, outputs along the bottom ----
        Grid portLayer = new();

        InputPort = CreatePort(new PortDescriptor("in", "In", FlowCanvasPalette.PortNeutral), isInput: true);
        InputPort.HorizontalAlignment = HorizontalAlignment.Center;
        InputPort.VerticalAlignment = VerticalAlignment.Top;
        InputPort.Margin = new Thickness(0d, -5d, 0d, 0d);
        portLayer.Children.Add(InputPort);

        // The regular bottom port row EXCLUDES the derived component drop-point
        // ports ("case:buttonN"/"case:menu0") — those render as small grey drop
        // dots just below the bottom edge, spread at width·(i+1)/(n+1), so a
        // message card with buttons + menu shows its grey dots at ~1/3 and
        // ~2/3 while the main "next" dot keeps the bottom-center.
        IReadOnlyList<PortDescriptor> outputs = node.GetOutputPorts();
        _slotPortKeys.Clear();
        Grid outputsGrid = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0d, 0d, 0d, -5d) };
        int rowColumn = 0;
        foreach (PortDescriptor port in outputs)
        {
            if (FlowCanvasNode.IsComponentSlotPort(port.Key))
            {
                _slotPortKeys.Add(port.Key);
                continue;
            }

            outputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });

            Ellipse ellipse = CreatePort(port, isInput: false);
            Grid.SetColumn(ellipse, rowColumn);
            outputsGrid.Children.Add(ellipse);
            _portIndex[port.Key] = rowColumn;
            rowColumn++;
        }

        portLayer.Children.Add(outputsGrid);

        // ---- component drop dots: small grey dots straddling just below the edge ----
        for (int slot = 0; slot < _slotPortKeys.Count; slot++)
        {
            PortDescriptor? port = null;
            foreach (PortDescriptor candidate in outputs)
            {
                if (string.Equals(candidate.Key, _slotPortKeys[slot], StringComparison.Ordinal))
                {
                    port = candidate;
                    break;
                }
            }

            if (port == null)
                continue;

            double slotX = Width * (slot + 1) / (_slotPortKeys.Count + 1);
            Ellipse dot = CreatePort(port.Value, isInput: false);
            dot.Width = SlotDotSize;
            dot.Height = SlotDotSize;
            dot.HorizontalAlignment = HorizontalAlignment.Left;
            dot.VerticalAlignment = VerticalAlignment.Bottom;
            // Center sits SlotDotDrop px below the card's bottom edge: the dot
            // spans [H-4, H+4+2·drop] for an 8px dot with bottom margin -6.
            dot.Margin = new Thickness(slotX - SlotDotSize / 2d, 0d, 0d, -(SlotDotSize / 2d + SlotDotDrop));
            portLayer.Children.Add(dot);
        }

        root.Children.Add(portLayer);

        // ---- component chips hanging under the card (BotGhost look) ----
        FrameworkElement? chipLayer = BuildComponentChips();
        if (chipLayer != null)
            root.Children.Add(chipLayer);

        Child = root;

        MouseEnter += (_, _) =>
        {
            _isMouseOver = true;
            RefreshChrome();
        };
        MouseLeave += (_, _) =>
        {
            _isMouseOver = false;
            RefreshChrome();
        };
    }

    internal FlowCanvasNode Node { get; }

    /// <summary>The single input port ellipse (top-center; wire drop target).</summary>
    internal Ellipse InputPort { get; }

    /// <summary>True for the special non-palette button/menu component block types.</summary>
    internal static bool IsComponentType(string blockType) =>
        !string.IsNullOrEmpty(blockType) && ComponentTypes.ContainsKey(blockType);

    /// <summary>True for the special non-palette attachment card types (loop/comparison branch cards).</summary>
    internal static bool IsAttachmentCardType(string blockType) =>
        !string.IsNullOrEmpty(blockType) && AttachmentCardTypes.ContainsKey(blockType);

    /// <summary>
    /// True for canvas-authored special cards the Delete KEY must never remove
    /// (the attachment cards; their lifecycle belongs to the editor — removing
    /// the parent or unwiring hides them again). They can still be deleted
    /// programmatically via FlowCanvasControl.DeleteNode.
    /// </summary>
    internal static bool IsProtectedType(string blockType) => IsAttachmentCardType(blockType);

    /// <summary>Card width for a node: small for component sub-nodes and attachment cards, standard otherwise.</summary>
    internal static double WidthFor(FlowCanvasNode node) =>
        IsComponentType(node.BlockType) ? ComponentNodeWidth
        : IsAttachmentCardType(node.BlockType) ? AttachmentCardWidth
        : NodeWidth;

    /// <summary>Height fallback for a node before WPF has measured its visual.</summary>
    internal static double DefaultHeightFor(FlowCanvasNode node) =>
        IsComponentType(node.BlockType) || IsAttachmentCardType(node.BlockType)
            ? ComponentDefaultHeight
            : DefaultHeight;

    /// <summary>Local-space center of an output port (relative to the visual's top-left).</summary>
    /// <remarks>
    /// Component drop-point ports report the grey drop dot just below the
    /// card's bottom edge (x = width·(slot+1)/(slotCount+1)); every other port
    /// is evenly spaced along the bottom edge of the row.
    /// </remarks>
    internal Point GetOutputPortLocalPoint(string portKey)
    {
        double width = ActualWidth > 0 ? ActualWidth : WidthFor(Node);
        double height = ActualHeight > 0 ? ActualHeight : DefaultHeightFor(Node);

        int slot = _slotPortKeys.IndexOf(portKey);
        if (slot >= 0)
            return new Point(width * (slot + 1) / (_slotPortKeys.Count + 1), height + SlotDotDrop);

        int index = _portIndex.TryGetValue(portKey, out int found) ? found : 0;
        int count = Math.Max(1, _portIndex.Count);
        return new Point(width * (index + 1) / (count + 1), height);
    }

    /// <summary>Local-space center of the input port (relative to the visual's top-left).</summary>
    internal Point GetInputPortLocalPoint()
    {
        double width = ActualWidth > 0 ? ActualWidth : WidthFor(Node);
        return new Point(width / 2, 0d);
    }

    /// <summary>Re-reads the label from the model (after rename or external edit).</summary>
    internal void RefreshLabel() => _labelText.Text = Node.Label;

    /// <summary>Re-applies the selection/hover border treatment (dashed indigo outline + glow when selected).</summary>
    internal void RefreshChrome()
    {
        if (Node.IsSelected)
        {
            _selectionOutline.Stroke = FlowCanvasPalette.Brush(FlowCanvasPalette.Selection);
            _selectionOutline.Visibility = Visibility.Visible;
            BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.NodeBorder);
            BorderThickness = new Thickness(1d);
            Effect = new DropShadowEffect
            {
                Color = FlowCanvasPalette.Color(FlowCanvasPalette.Selection),
                BlurRadius = 10d,
                ShadowDepth = 0d,
                Opacity = 0.65d,
            };
        }
        else
        {
            _selectionOutline.Visibility = Visibility.Collapsed;
            BorderBrush = FlowCanvasPalette.Brush(_isMouseOver ? FlowCanvasPalette.NodeHover : FlowCanvasPalette.NodeBorder);
            BorderThickness = new Thickness(1d);
            Effect = null;
        }
    }

    /// <summary>Enters the in-place label editor (double-click on the node body).</summary>
    internal void BeginRename()
    {
        if (_labelEditor != null)
            return;

        _labelText.Visibility = Visibility.Collapsed;
        _labelEditor = new TextBox
        {
            Text = Node.Label,
            Style = null, // bypass implicit app-level styles; use explicit colors below
            FontSize = 13d,
            FontWeight = FontWeights.SemiBold,
            Background = FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasBackground),
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.TextPrimary),
            BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.Selection),
            BorderThickness = new Thickness(1d),
            Padding = new Thickness(3d, 1d, 3d, 1d),
            MinWidth = 110d,
            MaxWidth = Width - 58d, // chip (28) + gap (9) + card margins (10+10) + 1px border
        };
        _labelEditor.KeyDown += OnEditorKeyDown;
        _labelEditor.LostFocus += OnEditorLostFocus;

        InplaceReplace(_labelText, _labelEditor);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _labelEditor?.Focus();
            _labelEditor?.SelectAll();
        }));
    }

    // =====================================================================================
    // Component chips ("⊕ Button" / "⊕ Menu") just below the card
    // =====================================================================================

    /// <summary>
    /// Builds the light chip strip just below the card, or null when the node
    /// cannot carry components (non-message block without any Buttons/Menu
    /// config). BotGhost style: ONE "⊕ Button" chip anchored under the
    /// bottom-LEFT corner and ONE "⊕ Menu" chip under the bottom-RIGHT corner
    /// — always fully labeled, light background, dark text, thin border, 4px
    /// corners. The button chip dims at the 5-button cap; the menu chip dims
    /// only while a menu exists AND its sub-card is wired (a Menu config with
    /// no live card keeps the chip clickable so a press re-spawns it).
    /// Clicking raises <see cref="ComponentChipAddClicked"/> (kind
    /// "button"/"menu").
    /// </summary>
    private FrameworkElement? BuildComponentChips()
    {
        if (_isComponentNode || _attachmentCard != null)
            return null;

        int buttonCount = FlowCanvasNode.ComponentButtonCount(Node.Config);
        bool hasMenu = FlowCanvasNode.ComponentHasMenu(Node.Config);
        if (buttonCount == 0 && !hasMenu && !IsComponentCapableBlock(Node.BlockType))
            return null;

        Grid layer = new()
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0d, 0d, 0d, -28d), // hang just below the card edge, over the canvas
            Height = ComponentChipSize,
        };

        // Bottom-LEFT: the "⊕ Button" add chip (dimmed at the 5-button cap).
        Border buttonChip = CreateComponentChip(kind: "button", label: "Button", atCap: buttonCount >= MaxButtons);
        buttonChip.HorizontalAlignment = HorizontalAlignment.Left;
        layer.Children.Add(buttonChip);

        // Bottom-RIGHT: the "⊕ Menu" add chip. Discord allows one menu per
        // message, but the chip only goes dim when the menu is BOTH configured
        // AND its sub-card is actually wired (the case:menu0 port points at a
        // live card). A Menu config whose sub-card is missing (deleted, or an
        // earlier add that never spawned the card) keeps the chip CLICKABLE —
        // the host's add handler then just re-spawns and re-wires the card,
        // mirroring how the button chip stays usable below its cap.
        bool menuWired = Node.GetTarget(FlowCanvasNode.ComponentMenuPortKey) != null;
        Border menuChip = CreateComponentChip(kind: "menu", label: "Menu", atCap: hasMenu && menuWired);
        menuChip.HorizontalAlignment = HorizontalAlignment.Right;
        layer.Children.Add(menuChip);

        return layer;
    }

    /// <summary>
    /// One LIGHT rounded chip ("⊕ Button" / "⊕ Menu"): white-ish background,
    /// dark 12px text, thin border, 4px corners. Hover darkens the background
    /// slightly. At the cap the chip renders dimmed (45% opacity, arrow
    /// cursor) and no longer raises the add event. The chip press is
    /// swallowed so it never starts a node drag.
    /// </summary>
    private Border CreateComponentChip(string kind, string label, bool atCap)
    {
        Border chip = new()
        {
            Background = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipLightBackground),
            BorderBrush = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipLightBorder),
            BorderThickness = new Thickness(1d),
            CornerRadius = new CornerRadius(4d),
            Cursor = atCap ? Cursors.Arrow : Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = atCap ? 0.45d : 1d,
            ToolTip = atCap ? null : $"Add a {label.ToLowerInvariant()}",
        };

        StackPanel panel = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6d, 3d, 6d, 3d) };
        panel.Children.Add(new TextBlock
        {
            Text = "⊕",
            FontSize = 11d,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipLightForeground),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0d, 0d, 3d, 0d),
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12d,
            Foreground = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipLightForeground),
            VerticalAlignment = VerticalAlignment.Center,
        });
        chip.Child = panel;

        // Hover darkening (active chips only); reset on leave.
        chip.MouseEnter += (_, _) =>
        {
            if (!atCap)
                chip.Background = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipLightBackgroundHover);
        };
        chip.MouseLeave += (_, _) =>
        {
            if (!atCap)
                chip.Background = FlowCanvasPalette.Brush(FlowCanvasPalette.ChipLightBackground);
        };

        // Chips must never start a node drag: swallow clicks at the chip level.
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true;

        if (!atCap)
        {
            chip.MouseLeftButtonUp += (_, e) =>
            {
                ComponentChipAddClicked?.Invoke(this, (Node, kind));
                e.Handled = true;
            };
        }

        return chip;
    }

    // =====================================================================================
    // Ports + in-place label editor
    // =====================================================================================

    private Ellipse CreatePort(PortDescriptor port, bool isInput)
    {
        // Output dots follow the shared attachment-vs-main rule (see
        // FlowCanvasPalette.ResolvePortColor): every "next" exit renders
        // MainWire blue (plain actions, component sub-nodes, attachment
        // cards), while attached flows (the loop ports, the component
        // drop-point slots, comparison case/else) and condition/error/case
        // ports keep their descriptor color. The input dot stays neutral.
        string colorHex = isInput
            ? port.Color
            : FlowCanvasPalette.ResolvePortColor(Node.BlockType, port.Key, port.Color);

        Ellipse ellipse = new()
        {
            Width = 10d,
            Height = 10d,
            // Solid BotGhost-style dot; the color comes from the resolved port rule.
            Fill = FlowCanvasPalette.Brush(colorHex),
            Stroke = FlowCanvasPalette.Brush(colorHex),
            StrokeThickness = 1d,
            ToolTip = port.Label,
            Cursor = isInput ? Cursors.Hand : Cursors.Cross,
        };

        if (isInput)
            return ellipse;

        ellipse.MouseLeftButtonDown += (_, e) =>
        {
            ellipse.CaptureMouse();
            OutputPortGrabbed?.Invoke(this, new FlowPortEventArgs(Node, port.Key, e.GetPosition(this)));
            e.Handled = true;
        };
        ellipse.MouseMove += (_, e) =>
        {
            if (ellipse.IsMouseCaptured)
                OutputPortDrag?.Invoke(this, new FlowPortEventArgs(Node, port.Key, e.GetPosition(this)));
        };
        ellipse.MouseLeftButtonUp += (_, e) =>
        {
            if (!ellipse.IsMouseCaptured)
                return;

            OutputPortReleased?.Invoke(this, new FlowPortEventArgs(Node, port.Key, e.GetPosition(this)));
            ellipse.ReleaseMouseCapture(); // LostMouseCapture fires afterwards; control guards on state
            e.Handled = true;
        };
        ellipse.LostMouseCapture += (_, _) =>
        {
            // Aborted drag (capture stolen). The control ignores this when the
            // connection was already completed on mouse-up.
            OutputPortReleased?.Invoke(this, new FlowPortEventArgs(Node, port.Key, Mouse.GetPosition(this)));
        };

        return ellipse;
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e) => CommitRename();

    private void CommitRename()
    {
        TextBox? editor = _labelEditor;
        if (editor == null)
            return;

        _labelEditor = null;
        editor.KeyDown -= OnEditorKeyDown;
        editor.LostFocus -= OnEditorLostFocus;

        string? text = editor.Text?.Trim();
        InplaceReplace(editor, _labelText);
        _labelText.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(text) && !string.Equals(text, Node.Label, StringComparison.Ordinal))
            Node.Label = text; // PropertyChanged drives the control's refresh + GraphChanged
    }

    private void CancelRename()
    {
        TextBox? editor = _labelEditor;
        if (editor == null)
            return;

        _labelEditor = null;
        editor.KeyDown -= OnEditorKeyDown;
        editor.LostFocus -= OnEditorLostFocus;
        InplaceReplace(editor, _labelText);
        _labelText.Visibility = Visibility.Visible;
    }

    private static void InplaceReplace(FrameworkElement oldElement, FrameworkElement newElement)
    {
        if (oldElement.Parent is Panel panel)
        {
            int index = panel.Children.IndexOf(oldElement);
            panel.Children.RemoveAt(index);
            panel.Children.Insert(index, newElement);
        }
    }
}
