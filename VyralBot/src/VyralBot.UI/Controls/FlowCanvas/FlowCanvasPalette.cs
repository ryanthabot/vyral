using System;
using System.Collections.Generic;
using System.Windows.Media;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// Shared palette and small color helpers for the flow canvas visuals
/// (nodes, ports, wires, grid). Every surface/text role is a LIVE property
/// that resolves from the active theme's FlowCanvas* COLOR resources
/// (Themes/Colors/*.xaml — swapped at runtime by ThemeManager) and falls back
/// to the matching const default when the key is missing (designer/tests), so
/// the canvas re-themes with the rest of the app on ThemeManager.ThemeChanged.
/// Semantic colors
/// (condition green, error amber, the per-component icon chip accents and the
/// light component chips) have no FlowCanvas* resource keys and stay constant.
/// </summary>
internal static class FlowCanvasPalette
{
    // =====================================================================================
    // Fallback defaults — used only when the matching FlowCanvas* resource is missing
    // =====================================================================================

    private const string FallbackCanvasBackground = "#1A1A2E";
    private const string FallbackGridDot = "#2A2A4A";
    private const string FallbackNodeBackground = "#2B2D42";
    private const string FallbackNodeBorder = "#374151";
    private const string FallbackNodeHover = "#3D3F5C";
    private const string FallbackSelection = "#6366F1";        // dashed indigo selection (BotGhost style)
    private const string FallbackTextPrimary = "#FFFFFF";
    private const string FallbackTextSecondary = "#9CA3AF";
    private const string FallbackDescriptionText = "#9AA0A6";  // grey description line on node/trigger cards
    private const string FallbackPortNeutral = "#9CA3AF";
    private const string FallbackMainWire = "#3B82F6";         // action blue — main-flow "next" wires/dots on normal blocks
    private const string FallbackCasePortColor = "#F59E0B";
    private const string FallbackTriggerAccent = "#F59E0B";    // amber chip of the pinned trigger card
    private const string FallbackRejected = "#EF4444";
    private const string FallbackCanvasButtonBackground = "#FFFFFF"; // white square canvas controls (fit/reset)
    private const string FallbackCanvasButtonHover = "#F3F4F6";
    private const string FallbackCanvasButtonForeground = "#1F2937";
    private const string FallbackChipBackground = "#1E1F22";   // (legacy dark chip — kept for reference)
    private const string FallbackChipBorder = "#4A4C58";
    private const string FallbackChipBorderHover = "#6B6E80";  // chip hover highlight

    // =====================================================================================
    // Theme-resolved roles (FlowCanvas* COLOR resources, live per access)
    // =====================================================================================

    public static string CanvasBackground => Resolve("FlowCanvasBackground", FallbackCanvasBackground);
    public static string GridDot => Resolve("FlowCanvasGridDot", FallbackGridDot);
    public static string NodeBackground => Resolve("FlowCanvasNodeBackground", FallbackNodeBackground);
    public static string NodeBorder => Resolve("FlowCanvasNodeBorder", FallbackNodeBorder);
    public static string NodeHover => Resolve("FlowCanvasNodeHover", FallbackNodeHover);
    public static string Selection => Resolve("FlowCanvasSelection", FallbackSelection);
    public static string TextPrimary => Resolve("FlowCanvasTextPrimary", FallbackTextPrimary);
    public static string TextSecondary => Resolve("FlowCanvasTextSecondary", FallbackTextSecondary);
    public static string DescriptionText => Resolve("FlowCanvasDescriptionText", FallbackDescriptionText);
    public static string PortNeutral => Resolve("FlowCanvasPortNeutral", FallbackPortNeutral);
    public static string MainWire => Resolve("FlowCanvasMainWire", FallbackMainWire);
    public static string CasePortColor => Resolve("FlowCanvasCasePort", FallbackCasePortColor);
    public static string TriggerAccent => Resolve("FlowCanvasTriggerAccent", FallbackTriggerAccent);
    public static string Rejected => Resolve("FlowCanvasRejected", FallbackRejected);
    public static string CanvasButtonBackground => Resolve("FlowCanvasButtonBackground", FallbackCanvasButtonBackground);
    public static string CanvasButtonHover => Resolve("FlowCanvasButtonHover", FallbackCanvasButtonHover);
    public static string CanvasButtonForeground => Resolve("FlowCanvasButtonForeground", FallbackCanvasButtonForeground);
    public static string ChipBackground => Resolve("FlowCanvasChipBackground", FallbackChipBackground);
    public static string ChipBorder => Resolve("FlowCanvasChipBorder", FallbackChipBorder);
    public static string ChipBorderHover => Resolve("FlowCanvasChipBorderHover", FallbackChipBorderHover);

    // =====================================================================================
    // Semantic/component accents — no FlowCanvas* resource keys, intentionally constant
    // =====================================================================================

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
    /// Raw catalog descriptor hex → theme resource key. BlockCatalog port/block
    /// colors are compile-time constants in the Workflows project (grey
    /// "next"/attachment, amber error, the action-blue block accent), so this
    /// value→key map routes THEM through the themed roles too. The palette
    /// members above resolve their own keys directly and no longer depend on
    /// this table. Last write wins for shared values (amber → case port).
    /// </summary>
    private static readonly Dictionary<string, string> ThemedResourceKeys = new()
    {
        [FallbackCanvasBackground] = "FlowCanvasBackground",
        [FallbackGridDot] = "FlowCanvasGridDot",
        [FallbackNodeBackground] = "FlowCanvasNodeBackground",
        [FallbackNodeBorder] = "FlowCanvasNodeBorder",
        [FallbackNodeHover] = "FlowCanvasNodeHover",
        [FallbackSelection] = "FlowCanvasSelection",
        [FallbackTextPrimary] = "FlowCanvasTextPrimary",
        [FallbackTextSecondary] = "FlowCanvasTextSecondary",
        [FallbackDescriptionText] = "FlowCanvasDescriptionText",
        [FallbackPortNeutral] = "FlowCanvasPortNeutral",
        [FallbackMainWire] = "FlowCanvasMainWire",
        [FallbackCasePortColor] = "FlowCanvasCasePort", // also the amber error/default port + trigger accent
        [FallbackRejected] = "FlowCanvasRejected",
        [FallbackCanvasButtonBackground] = "FlowCanvasButtonBackground",
        [FallbackCanvasButtonHover] = "FlowCanvasButtonHover",
        [FallbackCanvasButtonForeground] = "FlowCanvasButtonForeground",
        [FallbackChipBackground] = "FlowCanvasChipBackground",
        [FallbackChipBorder] = "FlowCanvasChipBorder",
        [FallbackChipBorderHover] = "FlowCanvasChipBorderHover",
    };

    /// <summary>
    /// Explicit invalidation hook the canvas calls before rebuilding its
    /// visuals after a theme change. The palette holds NO cached state — every
    /// member re-reads Application.Current.Resources on access — so this is a
    /// deliberate no-op today; it keeps the re-theme sequence explicit and
    /// gives a future cached implementation its single invalidation point.
    /// </summary>
    public static void RefreshFromResources()
    {
    }

    /// <summary>
    /// Resolves a palette role: the current theme's FlowCanvas* COLOR resource
    /// when present, otherwise the compiled-in fallback hex. Returns the hex
    /// STRING so every existing <see cref="Color(string)"/> /
    /// <see cref="Brush(string)"/> call site keeps working unchanged.
    /// </summary>
    private static string Resolve(string resourceKey, string fallbackHex)
    {
        if (System.Windows.Application.Current?.Resources[resourceKey] is System.Windows.Media.Color themed)
            return ToHex(themed);

        return fallbackHex;
    }

    /// <summary>Formats a color as "#RRGGBB" (with "#AARRGGBB" when not opaque).</summary>
    private static string ToHex(System.Windows.Media.Color color) =>
        color.A == 0xFF
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// THE port/wire color rule (single source of truth — used for the output
    /// port dots on the node cards AND for the wires leaving those ports).
    /// ATTACHMENT-VS-MAIN: grey (<see cref="PortNeutral"/>, the catalog
    /// descriptor for "next") is reserved for settings-ATTACHED flows — the
    /// loop's "Loop times" ("case:body") and "After loop" ("next") ports, the
    /// component drop-point ports ("case:buttonN"/"case:menu0" under a message
    /// card), and any other grey descriptor (comparison case/else once the
    /// catalog declares them grey). EVERY other "next" port is a MAIN-FLOW
    /// continuation and gets the action blue (<see cref="MainWire"/>): plain
    /// actions, the component sub-nodes' ("button_start"/"menu_start") bottom
    /// exits and the attachment cards' ("loop_after"/"loop_body"/
    /// "comparison_case"/"comparison_else") bottom exits — their chains are
    /// normal blue wires. Condition True/False stay green and the derived
    /// switch case ports stay amber — everything else passes through. Grey
    /// source ports also switch the wire GEOMETRY to a right-angle elbow (see
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

    /// <summary>Parses a "#RRGGBB" string, preferring the themed color for known palette roles.</summary>
    public static Color Color(string hex)
    {
        // Theme-aware: RAW catalog descriptor hexes (BlockCatalog constants —
        // attachment grey, amber error, action blue) resolve through the
        // active theme's resources; the themed palette properties return
        // already-resolved hexes that simply parse below.
        if (hex is not null && ThemedResourceKeys.TryGetValue(hex, out var resourceKey))
        {
            if (System.Windows.Application.Current?.TryFindResource(resourceKey) is System.Windows.Media.Color themed)
                return themed;
        }

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
