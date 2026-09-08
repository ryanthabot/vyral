namespace VyralBot.Integrations.StreamDeck;

public sealed record StreamDeckResult(bool Success, string Message);

public interface IStreamDeckService
{
    Task<List<StreamDeckAction>> GetActionsAsync();
    Task<StreamDeckResult> SaveActionAsync(string name, string uri);
    Task DeleteActionAsync(string id);
    /// <summary>Launches a saved action URI via the OS shell. Returns a user-facing string; never throws.</summary>
    Task<string> TriggerAsync(string actionIdOrName);
}
