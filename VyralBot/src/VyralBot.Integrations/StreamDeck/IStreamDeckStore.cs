namespace VyralBot.Integrations.StreamDeck;

/// <summary>Persistence seam for saved actions (JSON document). Wired to app config by the host.</summary>
public interface IStreamDeckStore
{
    Task<string> LoadAsync();
    Task SaveAsync(string json);
}
