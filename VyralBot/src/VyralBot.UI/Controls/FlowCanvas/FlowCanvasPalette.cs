using System;
using System.Windows.Media;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// Shared dark-theme palette and small color helpers for the flow canvas
/// visuals (nodes, ports, wires, grid).
/// </summary>
internal static class FlowCanvasPalette
{
    public const string CanvasBackground = "#1A1A2E";
    public const string GridDot = "#2A2A4A";
    public const string NodeBackground = "#2B2D42";
    public const string NodeBorder = "#374151";
    public const string NodeHover = "#3D3F5C";
    public const string Selection = "#6366F1";       // dashed indigo selection (BotGhost style)
    public const string TextPrimary = "#FFFFFF";
    public const string TextSecondary = "#9CA3AF";
    public const string DescriptionText = "#9AA0A6"; // grey description line on node/trigger cards
    public const string PortNeutral = "#9CA3AF";
    public const string MainWire = "#3B82F6";       // action blue — main-flow "next" wires/dots on normal blocks
    public const string CasePortColor = "#F59E0B";
    public const string TriggerAccent = "#F59E0B";   // amber chip of the pinned trigger card
    public const string Rejected = "#EF4444";
    public const string CanvasButtonBackground = "#FFFFFF"; // white square canvas controls (fit/reset)
    public const string CanvasButtonHover = "#F3F4F6";
    public const string CanvasButtonForeground = "#1F2937";

    public const string ChipBackground = "#1E1F22";  // (legacy dark chip — kept for reference)
    public const string ChipBorder = "#4A4C58";
    public const string ChipBorderHover = "#6B6E80"; // chip hover highlight
    public const string ChipLightBackground = "#E9EAEE"; // BotGhost LIGHT component chips under a node
    public const string ChipLightBackgroundHover = "#DEE0E6"; // light chip hover (slight darken)
    public const string ChipLightBorder = "#C4C6CE"; // thin light-chip border
    public const string ChipLightForeground = "#1F2937"; // dark chip text
    public const string ErrorCardAccent = "#ED4245"; // red ⚠️ chip of the pinned error-handler card
    public const string ButtonStartAccent = "#ED4245"; // button component sub-node icon chip
    public const string MenuStartAccent = "#5865F2";   // select-menu component sub-node icon chip
    public const string ComparisonCaseAccent = "#10B981"; // green "?" chip of a comparison branch card
    public const string ComparisonElseAccent = "#ED4245"; // red "?" chip of the comparison "Else" card

    public const string ParameterAccent = "#A855F7";  // purple icon chip of the parameter option cards
    public const string ParameterWarning = "#F59E0B"; // orange "!" badge on a parameter card with a warning

    /// <summary>
    /// THE port/wire color rule (single source of truth — used for the output
    /// port dots on the node cards AND for the wires leaving those ports).
    /// ATTACHMENT-VS-MAIN: grey (#9CA3AF, the catalog descriptor for "next")
    /// is reserved for settings-ATTACHED flows — the loop's "Loop times"
    /// ("case:body") and "After loop" ("next") ports, the component drop-point
    /// ports ("case:buttonN"/"case:menu0" under a message card), and any other
    /// grey descriptor (comparison case/else once the catalog declares them
    /// grey). EVERY other "next" port is a MAIN-FLOW continuation and gets the
    /// action blue (<see cref="MainWire"/>): plain actions, the component
    /// sub-nodes' ("button_start"/"menu_start") bottom exits and the
    /// attachment cards' ("loop_after"/"loop_body"/"comparison_case"/
    /// "comparison_else") bottom exits — their chains are normal blue wires.
    /// Condition True/False stay green and the derived switch case ports stay
    /// amber — everything else passes through. Grey source ports also switch
    /// the wire GEOMETRY to a right-angle elbow (see
    /// <see cref="FlowWireVisual.CreateElbow"/>); colored wires stay beziers.
    /// </summary>
    public static string ResolvePortColor(string blockType, string portKey, string descriptorColor)
    {
        if (string.Equals(portKey, BlockCatalog.PortNext, StringComparison.Ordinal)
            && !string.Equals(blockType, "loop", StringComparison.OrdinalIgnoreCase))
        {
            // Plain actions, component sub-nodes and attachment cards are all
            // main-flow continuations → blue. The loop keeps its grey
            // "After loop" attachment port.
            return MainWire;
        }

        return descriptorColor;
    }

    /// <summary>
    /// True when <paramref name="color"/> is the attachment grey — i.e. the
    /// port/wire is a settings-ATTACHED flow rather than a colored main/
    /// branch flow. The control uses this to pick the wire style: grey
    /// attachment wires render as right-angle elbows, colored wires as
    /// beziers (see <see cref="FlowWireVisual.CreateElbow"/>).
    /// </summary>
    public static bool IsAttachmentColor(System.Windows.Media.Color color) =>
        color == Color(PortNeutral);

    /// <summary>Parses a "#RRGGBB" string, falling back to gray on bad input.</summary>
    public static Color Color(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    /// <summary>
    /// Creates a new (unfrozen) solid brush so per-node visuals can restyle
    /// their own brushes freely without affecting other nodes.
    /// </summary>
    public static SolidColorBrush Brush(string hex) => new(Color(hex));

    /// <summary>Brush from an already-parsed color (frozen).</summary>
    public static SolidColorBrush Brush(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Blends a color toward white by the given amount (0-1) — used for selected wires.</summary>
    public static Color Brighten(Color color, double amount)
    {
        byte r = (byte)Math.Min(255d, color.R + (255d - color.R) * amount);
        byte g = (byte)Math.Min(255d, color.G + (255d - color.G) * amount);
        byte b = (byte)Math.Min(255d, color.B + (255d - color.B) * amount);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }
}
