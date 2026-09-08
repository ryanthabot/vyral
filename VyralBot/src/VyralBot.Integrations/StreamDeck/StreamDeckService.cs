using System.Text.Json;

namespace VyralBot.Integrations.StreamDeck;

/// <summary>
/// Stores named Stream Deck action links and fires them through the official
/// streamdeck:// URI scheme (Stream Deck app 6.4+, no plugin required).
/// </summary>
public sealed class StreamDeckService : IStreamDeckService
{
    private const string Scheme = "streamdeck://";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IStreamDeckStore _store;
    private readonly Action<string> _launch;

    public StreamDeckService(IStreamDeckStore store, Action<string>? launchOverride = null)
    {
        _store = store;
        _launch = launchOverride ?? LaunchViaShell;
    }

    public async Task<List<StreamDeckAction>> GetActionsAsync()
    {
        var json = await _store.LoadAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<List<StreamDeckAction>>(json, JsonOpts) ?? [];
    }

    public async Task<StreamDeckResult> SaveActionAsync(string name, string uri)
    {
        if (string.IsNullOrWhiteSpace(name)) return new(false, "Give the action a name.");
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return new(false, "URI must start with streamdeck:// (Stream Deck app 6.4+ action link).");

        uri = uri.Trim();
        // The OS handler is registered in lowercase; keep the scheme canonical while preserving the payload.
        if (!uri.StartsWith(Scheme, StringComparison.Ordinal))
            uri = Scheme + uri[Scheme.Length..];

        var actions = await GetActionsAsync().ConfigureAwait(false);
        actions.RemoveAll(a => a.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        actions.Add(new StreamDeckAction(Guid.NewGuid().ToString("N"), name.Trim(), uri));
        await _store.SaveAsync(JsonSerializer.Serialize(actions, JsonOpts)).ConfigureAwait(false);
        return new(true, $"Saved Stream Deck action '{name.Trim()}'.");
    }

    public async Task DeleteActionAsync(string id)
    {
        var actions = await GetActionsAsync().ConfigureAwait(false);
        actions.RemoveAll(a => a.Id == id);
        await _store.SaveAsync(JsonSerializer.Serialize(actions, JsonOpts)).ConfigureAwait(false);
    }

    public async Task<string> TriggerAsync(string actionIdOrName)
    {
        var actions = await GetActionsAsync().ConfigureAwait(false);
        var action = actions.FirstOrDefault(a => a.Id.Equals(actionIdOrName, StringComparison.OrdinalIgnoreCase))
                  ?? actions.FirstOrDefault(a => a.Name.Equals(actionIdOrName, StringComparison.OrdinalIgnoreCase));
        if (action is null) return $"❌ Stream Deck action '{actionIdOrName}' not found.";
        try
        {
            _launch(action.Uri);
            return $"🎛️ Stream Deck action '{action.Name}' triggered.";
        }
        catch (Exception ex)
        {
            return $"❌ Stream Deck action failed: {ex.Message}";
        }
    }

    private static void LaunchViaShell(string uri) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = uri, UseShellExecute = true });
}
