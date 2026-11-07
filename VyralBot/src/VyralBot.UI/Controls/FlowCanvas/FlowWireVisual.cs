using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// One wire on the canvas: from an output port of the source node to the
/// input port of the target node. Two geometric styles, chosen by the caller
/// (the control's grey-attachment rule) through <see cref="Orthogonal"/>:
/// a cubic bezier (default — colored wires: blue main, green conditions,
/// amber error/default/case) or a RIGHT-ANGLE elbow (attachment wires whose
/// source port renders grey — component drop points, the loop's
/// "After loop"/"Loop times" ports, comparison case/else): vertical drop from
/// the source dot, horizontal run, vertical into the target's top dot.
/// Hosts a fat transparent hit path under a 2px colored stroke (the stroke
/// color comes from the source port); selection brightens and thickens the
/// stroke. DELETABLE wires (blue main wires and green condition branches —
/// everything that is neither attachment grey nor amber) carry exactly ONE ⊗
/// connection marker centered on the path MIDDLE (bezier t = 0.5 point, or
/// the middle of an elbow's horizontal run). It is a CLICK-TO-DISCONNECT
/// button: clicking it raises <see cref="DisconnectRequested"/> (the control
/// clears the source node's routing field, the wire hides and GraphChanged
/// fires so undo captures it). Grey attachment wires and amber error/default
/// wires are NOT deletable — they render NO marker at all. The wire BODY
/// keeps click-to-select and stays non-deletable — Delete never removes a
/// selected wire; only marker clicks and node deletion clear connections.
/// </summary>
internal sealed class FlowWireVisual : Canvas
{
    private const double MarkerSize = 8d;      // visible ⊗ ring diameter
    private const double MarkerHitSize = 14d;  // generous transparent click target around the ring

    // NOT cached in static fields: the ⊗ marker brushes resolve the LIVE
    // palette on every use, so wire rebuilds after a theme change render the
    // markers in the fresh attachment grey (static readonly fields would keep
    // the palette colors captured at type-initialization time forever).
    private static SolidColorBrush MarkerStroke => FlowCanvasPalette.Brush(FlowCanvasPalette.PortNeutral);
    private static SolidColorBrush MarkerStrokeHover =>
        FlowCanvasPalette.Brush(FlowCanvasPalette.Brighten(FlowCanvasPalette.Color(FlowCanvasPalette.PortNeutral), 0.65d));

    private readonly Path _hitPath;
    private readonly Path _strokePath;
    private readonly FrameworkElement? _disconnectMarker; // null on grey/amber (non-deletable) wires
    private readonly SolidColorBrush _baseBrush;
    private bool _selected;
    private bool _orthogonal;

    /// <summary>
    /// Raised when the user clicks the ⊗ connection marker. The control
    /// clears <see cref="Source"/>'s routing field for <see cref="PortKey"/>.
    /// Only raised on deletable wires (markers exist only there).
    /// </summary>
    internal event EventHandler? DisconnectRequested;

    /// <summary>Node the wire leaves from (its routing field owns the target id).</summary>
    internal FlowCanvasNode Source { get; }

    /// <summary>Output port key on the source node ("next", "true", "case:x", ...).</summary>
    internal string PortKey { get; }

    /// <summary>Target node id (kept in sync with Source's routing field by the control).</summary>
    internal string TargetId { get; }

    internal FlowWireVisual(FlowCanvasNode source, string portKey, string targetId, Color color)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        PortKey = portKey ?? throw new ArgumentNullException(nameof(portKey));
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));

        // Frozen: the wire never restyles this brush (selection swaps in a new
        // brightened instance), and a frozen brush can be used by the render
        // thread without a copy — one per wire, 100+ wires per canvas.
        _baseBrush = new SolidColorBrush(color);
        _baseBrush.Freeze();
        _strokePath = new Path
        {
            Stroke = _baseBrush,
            StrokeThickness = 2d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _hitPath = new Path
        {
            Stroke = Brushes.Transparent, // transparent strokes are still hit-testable
            StrokeThickness = 12d,
        };

        // The single ⊗ disconnect marker exists ONLY on deletable wires (not
        // attachment grey, not amber). It sits above the stroke, centered on
        // the path midpoint. A marker press is swallowed inside the marker,
        // so the fat hit path never sees it (no wire selection) and no drag
        // can begin.
        _disconnectMarker = HasDisconnectMarker(color) ? CreateConnectionMarker() : null;
        if (_disconnectMarker != null)
            HookDisconnectMarker(_disconnectMarker, RaiseDisconnect);

        Children.Add(_hitPath);
        Children.Add(_strokePath);
        if (_disconnectMarker != null)
            Children.Add(_disconnectMarker);
        Cursor = Cursors.Hand;
    }

    private void RaiseDisconnect() => DisconnectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// True when a wire of this resolved color is DELETABLE and gets its one
    /// midpoint ⊗ disconnect marker: blue main wires and green condition
    /// branches. Attachment grey (<see cref="FlowCanvasPalette.PortNeutral"/>
    /// — loop ports, component drop points, comparison case/else, the error
    /// handler card wire) and amber (<see cref="FlowCanvasPalette.CasePortColor"/>,
    /// the error/default ports) are attachment-style, non-deletable flows —
    /// they render NO marker.
    /// </summary>
    internal static bool HasDisconnectMarker(Color color) =>
        !FlowCanvasPalette.IsAttachmentColor(color)
        && color != FlowCanvasPalette.Color(FlowCanvasPalette.CasePortColor);

    /// <summary>
    /// True renders the wire as a right-angle elbow (BotGhost attachment
    /// style) instead of the default bezier. Set at creation by the control —
    /// grey (attachment) source ports get elbows, colored ports stay bezier.
    /// </summary>
    internal bool Orthogonal
    {
        get => _orthogonal;
        set
        {
            if (_orthogonal == value)
                return;

            _orthogonal = value;
            RefreshGeometry();
        }
    }

    /// <summary>Selected wires render thicker and brighter.</summary>
    internal bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;

            _selected = value;
            _strokePath.StrokeThickness = value ? 3.5d : 2d;
            _strokePath.Stroke = value
                ? new SolidColorBrush(FlowCanvasPalette.Brighten(_baseBrush.Color, 0.45d))
                : _baseBrush;
        }
    }

    private Point _lastStart;
    private Point _lastEnd;

    /// <summary>Recomputes the wire (elbow or bezier) between two world-space port points.</summary>
    internal void UpdateGeometry(Point start, Point end)
    {
        _lastStart = start;
        _lastEnd = end;
        RefreshGeometry();
        if (_disconnectMarker != null)
            CenterMarker(_disconnectMarker, PathMidpoint(start, end, _orthogonal)); // follows node drags
    }

    /// <summary>Rebuilds the path data from the cached port points (style-aware).</summary>
    private void RefreshGeometry()
    {
        Geometry geometry = _orthogonal ? CreateElbow(_lastStart, _lastEnd) : CreateBezier(_lastStart, _lastEnd);
        _hitPath.Data = geometry;
        _strokePath.Data = geometry;
    }

    /// <summary>Centers an ⊗ marker on the given path point (the wire's midpoint).</summary>
    internal static void CenterMarker(FrameworkElement marker, Point pathPoint)
    {
        Canvas.SetLeft(marker, pathPoint.X - MarkerHitSize / 2d);
        Canvas.SetTop(marker, pathPoint.Y - MarkerHitSize / 2d);
    }

    /// <summary>
    /// The point at the visual MIDDLE of the wire path — where the single ⊗
    /// disconnect marker sits. Bezier: the curve point at t = 0.5 via the
    /// cubic Bernstein weights (P0 + 3·P1 + 3·P2 + P3) / 8 — for this
    /// symmetric vertical-S curve it lands exactly on the segment midpoint.
    /// Orthogonal elbow: the middle of the horizontal run (the collapsed
    /// straight-vertical form uses the segment midpoint).
    /// </summary>
    internal static Point PathMidpoint(Point start, Point end, bool orthogonal)
    {
        if (orthogonal)
        {
            if (ElbowIsStraight(start, end))
                return new Point(start.X, (start.Y + end.Y) / 2d);

            return new Point((start.X + end.X) / 2d, ElbowRunY(start, end)); // mid of the horizontal run
        }

        double offset = BezierControlOffset(start, end);
        double x = (start.X + 3d * start.X + 3d * end.X + end.X) / 8d; // == (start.X + end.X) / 2
        double y = (start.Y + 3d * (start.Y + offset) + 3d * (end.Y - offset) + end.Y) / 8d;
        return new Point(x, y);
    }

    /// <summary>
    /// Small grey circle with an × — the BotGhost connection-point visual,
    /// now a live disconnect button: hit-testable (generous transparent
    /// target around the 8px ring), Hand cursor, hover highlight.
    /// </summary>
    internal static FrameworkElement CreateConnectionMarker()
    {
        Ellipse ring = new()
        {
            Width = MarkerSize,
            Height = MarkerSize,
            Fill = FlowCanvasPalette.Brush(FlowCanvasPalette.CanvasBackground),
            Stroke = MarkerStroke,
            StrokeThickness = 1d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        Path cross = new()
        {
            Width = MarkerSize,
            Height = MarkerSize,
            Stroke = MarkerStroke,
            StrokeThickness = 1d,
            Data = Geometry.Parse("M2,2 L6,6 M6,2 L2,6"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        // The transparent square padding makes the tiny marker easy to click;
        // the inner ring/cross stay hit-test-invisible so the grid itself
        // receives every press/release.
        Grid marker = new()
        {
            Width = MarkerHitSize,
            Height = MarkerHitSize,
            Background = Brushes.Transparent,
            RenderTransformOrigin = new Point(0.5d, 0.5d), // hover scale stays centered
        };
        marker.Children.Add(ring);
        marker.Children.Add(cross);
        return marker;
    }

    /// <summary>
    /// Makes an ⊗ marker a disconnect button: Hand cursor, "Click to
    /// disconnect" tooltip, hover highlight (slightly bigger + brighter) and
    /// a click (press AND release on the marker) that invokes
    /// <paramref name="onDisconnect"/>. The press is swallowed so it never
    /// reaches the wire body (no selection) or any node drag.
    /// </summary>
    internal static void HookDisconnectMarker(FrameworkElement marker, Action onDisconnect)
    {
        marker.Cursor = Cursors.Hand;
        marker.ToolTip = "Click to disconnect";
        marker.MouseEnter += (_, _) => SetMarkerHover(marker, hovered: true);
        marker.MouseLeave += (_, _) => SetMarkerHover(marker, hovered: false);
        marker.MouseLeftButtonDown += (_, e) => e.Handled = true;
        marker.MouseLeftButtonUp += (_, e) =>
        {
            onDisconnect();
            e.Handled = true;
        };
    }

    /// <summary>Hover highlight for an ⊗ marker: 1.3x bigger, brighter strokes.</summary>
    private static void SetMarkerHover(FrameworkElement marker, bool hovered)
    {
        marker.RenderTransform = hovered ? new ScaleTransform(1.3d, 1.3d) : null;
        if (marker is not Grid grid)
            return;

        foreach (UIElement child in grid.Children)
        {
            if (child is Shape shape)
                shape.Stroke = hovered ? MarkerStrokeHover : MarkerStroke;
        }
    }

    /// <summary>Vertical control-point reach of the S curve (shared by the bezier and its midpoint).</summary>
    private static double BezierControlOffset(Point start, Point end)
    {
        double vertical = Math.Abs(end.Y - start.Y);
        return Math.Clamp(vertical * 0.5d, 40d, 160d);
    }

    /// <summary>True when the elbow's horizontal run collapses to a straight vertical line.</summary>
    private static bool ElbowIsStraight(Point start, Point end) => Math.Abs(end.X - start.X) < 0.5d;

    /// <summary>Y of the elbow's horizontal run (shared by the elbow and its midpoint).</summary>
    private static double ElbowRunY(Point start, Point end) => Math.Max(start.Y + 14d, (start.Y + end.Y) / 2d);

    /// <summary>
    /// Cubic bezier tuned for top-to-bottom flow: control points extend
    /// vertically from each end, giving the Blueprint-style smoothed S shape.
    /// </summary>
    internal static Geometry CreateBezier(Point start, Point end)
    {
        double offset = BezierControlOffset(start, end);

        PathFigure figure = new()
        {
            StartPoint = start,
            IsClosed = false,
            Segments =
            {
                new BezierSegment(
                    new Point(start.X, start.Y + offset),
                    new Point(end.X, end.Y - offset),
                    end,
                    true),
            },
        };

        return new PathGeometry { Figures = { figure } };
    }

    /// <summary>
    /// Right-angle (orthogonal elbow) route used for attachment wires: a
    /// vertical drop from the source dot, a horizontal run at the midpoint
    /// height and a vertical entry into the target's top dot. When both ports
    /// already share an X (e.g. a drop dot centered over its sub-card) the
    /// run collapses to a straight vertical line. The bend height is kept at
    /// least a stub below the source so short hops still read as an elbow.
    /// </summary>
    internal static Geometry CreateElbow(Point start, Point end)
    {
        PathFigure figure = new() { StartPoint = start, IsClosed = false };

        if (ElbowIsStraight(start, end))
        {
            figure.Segments.Add(new LineSegment(new Point(start.X, end.Y), true));
        }
        else
        {
            double midY = ElbowRunY(start, end);
            figure.Segments.Add(new LineSegment(new Point(start.X, midY), true));
            figure.Segments.Add(new LineSegment(new Point(end.X, midY), true));
            figure.Segments.Add(new LineSegment(end, true));
        }

        return new PathGeometry { Figures = { figure } };
    }
}
