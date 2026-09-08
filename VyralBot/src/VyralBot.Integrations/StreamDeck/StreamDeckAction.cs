namespace VyralBot.Integrations.StreamDeck;

/// <summary>A saved Stream Deck action link (Stream Deck app 6.4+ supports streamdeck:// action URIs).</summary>
public sealed record StreamDeckAction(string Id, string Name, string Uri);
