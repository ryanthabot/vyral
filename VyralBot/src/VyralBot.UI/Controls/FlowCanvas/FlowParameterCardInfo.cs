namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// Immutable description of one parameter option card rendered above the
/// pinned trigger card (BotGhost-style): <paramref name="Name"/> is the bold
/// card title (a dim "(unnamed)" placeholder renders when empty),
/// <paramref name="Type"/> selects the purple chip glyph (first letter,
/// uppercased) and the grey "… option" subtitle, and
/// <paramref name="HasWarning"/> shows the orange "!" badge overlapping the
/// card's top-right corner. Cards are display + click only — the canvas owns
/// all layout, wiring and interaction.
/// </summary>
/// <param name="Name">Parameter name shown bold on the card (empty → "(unnamed)" placeholder).</param>
/// <param name="Type">Parameter type key ("text", "number", …); drives chip glyph + subtitle.</param>
/// <param name="HasWarning">True renders the orange "!" badge on the card's top-right corner.</param>
public sealed record FlowParameterCardInfo(string Name, string Type, bool HasWarning);
