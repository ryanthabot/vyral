using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// A locked wire from a pinned card to a regular node's input port:
/// the trigger card → entry node renders in <see cref="FlowCanvasPalette.MainWire"/>
/// blue as a bezier (it IS the main flow — styled exactly like a normal
/// <see cref="FlowWireVisual"/>), the error-handler card → first error-chain
/// node stays attachment grey and renders as a RIGHT-ANGLE elbow (same
/// attachment style as <see cref="FlowWireVisual"/> with Orthogonal=true).
/// Same visual language as <see cref="FlowWireVisual"/>
/// — 2px stroke over a fat area, identical thickness/caps — but NOT backed by
/// a node routing field: the canvas keeps the target id in
/// <see cref="FlowCanvasControl.EntryTargetId"/> /
/// <see cref="FlowCanvasControl.ErrorTargetId"/> and re-renders via
/// <see cref="UpdateGeometry"/>. The wire BODY is not hit-testable (clicks
/// fall through to the canvas) and stays locked/non-deletable. Deletable
/// pinned wires (the blue entry wire) carry exactly ONE ⊗ marker centered on
/// the path MIDDLE (bezier t = 0.5): clicking it raises
/// <see cref="DisconnectRequested"/> and the control unwires the pinned card
/// (entry → SetEntryTarget(null) + EntryNodeChanged(null)) — disconnecting
/// the entry wire is how the trigger card gets un-pinned. The grey
/// error-handler wire renders NO marker at all (it is a non-deletable
/// attachment — it can only be re-dragged from the card's port to retarget,
/// which works via input displacement).
/// </summary>
internal sealed class FlowPinnedWireVisual : Canvas
{
    private readonly Path _strokePath;
    private readonly FrameworkElement? _disconnectMarker; // entry (blue) only — the grey error wire has none

    /// <summary>
    /// Raised when the user clicks the ⊗ connection marker of a deletable
    /// pinned wire. The canvas unwires this pinned card's target (only the
    /// entry wire currently carries a marker).
    /// </summary>
    public event EventHandler? DisconnectRequested;

    /// <param name="color">
    /// Wire color: <see cref="FlowCanvasPalette.MainWire"/> for the trigger
    /// card's entry wire, <see cref="FlowCanvasPalette.PortNeutral"/> grey for
    /// the error-handler card's attached wire.
    /// </param>
    /// <param name="orthogonal">
    /// True renders a right-angle elbow instead of the default bezier (the
    /// canvas passes true for the grey error-handler attachment wire).
    /// </param>
    internal FlowPinnedWireVisual(Color color, bool orthogonal = false)
    {
        Orthogonal = orthogonal;
        _strokePath = new Path
        {
            Stroke = FlowCanvasPalette.Brush(color),
            StrokeThickness = 2d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false, // locked body — clicks pass through to the canvas
        };

        // Same deletable rule as normal wires: the blue entry wire gets its
        // one midpoint ⊗ disconnect (un-pin) marker; the grey error-handler
        // attachment wire gets none.
        _disconnectMarker = FlowWireVisual.HasDisconnectMarker(color)
            ? FlowWireVisual.CreateConnectionMarker()
            : null;
        if (_disconnectMarker != null)
            FlowWireVisual.HookDisconnectMarker(_disconnectMarker, () => DisconnectRequested?.Invoke(this, EventArgs.Empty));

        Children.Add(_strokePath);
        if (_disconnectMarker != null)
            Children.Add(_disconnectMarker);
        Visibility = Visibility.Collapsed;
    }

    /// <summary>True renders the right-angle attachment elbow instead of a bezier.</summary>
    private bool Orthogonal { get; }

    /// <summary>Recomputes the wire (elbow or bezier) between two world-space port points.</summary>
    internal void UpdateGeometry(Point start, Point end)
    {
        _strokePath.Data = Orthogonal
            ? FlowWireVisual.CreateElbow(start, end)
            : FlowWireVisual.CreateBezier(start, end);
        if (_disconnectMarker != null)
            FlowWireVisual.CenterMarker(_disconnectMarker, FlowWireVisual.PathMidpoint(start, end, Orthogonal)); // follows card/node drags
    }
}
