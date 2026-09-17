using Microsoft.Extensions.Logging;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Hosting;
using VyralBot.Integrations.Obs.WebSocket;

namespace VyralBot.UI.Services.VyralDeck;

/// <summary>
/// Lazily-connected obs-websocket client shared by every deck OBS action.
/// Locates the local OBS install (same source as the OBS page), reads the
/// plugin's remote-control config, connects on first use and keeps the socket
/// for later presses — reconnecting transparently after a failed/lost one.
/// </summary>
public sealed class ObsRemoteBridge : IDisposable
{
    private readonly ILogger<ObsRemoteBridge> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ObsWebSocketClient? _client;

    public ObsRemoteBridge(ILogger<ObsRemoteBridge> logger)
    {
        _logger = logger;
    }

    public async Task<string> SetSceneAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return "❌ Pick a scene name for this button first.";
        }

        return await UseClientAsync(
            client => client.SetCurrentProgramSceneAsync(sceneName.Trim()),
            $"Scene '{sceneName.Trim()}'").ConfigureAwait(false);
    }

    public async Task<string> StartStreamAsync() =>
        await UseClientAsync(client => client.StartStreamAsync(), "Stream start").ConfigureAwait(false);

    public async Task<string> StopStreamAsync() =>
        await UseClientAsync(client => client.StopStreamAsync(), "Stream stop").ConfigureAwait(false);

    /// <summary>Runs one request against a connected client; drops the cached socket on any failure.</summary>
    private async Task<string> UseClientAsync(Func<ObsWebSocketClient, Task> request, string what)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var client = await EnsureConnectedAsync().ConfigureAwait(false);
            try
            {
                await request(client).ConfigureAwait(false);
                return $"✅ {what} — done.";
            }
            catch (Exception ex)
            {
                // The socket is probably stale (OBS restarted, machine slept) —
                // drop it so the next press starts a fresh connection.
                DropClient(client);
                _logger.LogWarning(ex, "VyralDeck OBS request '{What}' failed", what);
                return $"❌ {what} failed: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            return $"❌ {what} failed: {ex.Message}";
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ObsWebSocketClient> EnsureConnectedAsync()
    {
        if (_client is { IsConnected: true } live)
        {
            return live;
        }

        var install = new ObsInstallLocator().Locate();
        if (install is null)
        {
            throw new InvalidOperationException("OBS is not installed (or not found) on this machine.");
        }

        var config = ObsWebSocketConfigEditor.Read(install.ConfigDir);
        if (config is null || !config.ServerEnabled)
        {
            throw new InvalidOperationException(
                "OBS remote control is disabled — enable it in OBS → Tools → WebSocket Server Settings.");
        }

        if (config.AuthRequired && string.IsNullOrEmpty(config.ServerPassword))
        {
            throw new InvalidOperationException(
                "OBS remote control requires a password — set one in OBS → Tools → WebSocket Server Settings.");
        }

        var client = new ObsWebSocketClient(config.ServerPassword ?? "", config.Port);
        client.StatusChanged += OnStatusChanged;
        await client.ConnectAsync().ConfigureAwait(false);
        _client = client;
        return client;
    }

    private void OnStatusChanged(ObsRemoteStatus status)
    {
        if (!status.Connected && _client is { } client)
        {
            // Remote end closed (OBS quit). Null out so the next press reconnects.
            client.StatusChanged -= OnStatusChanged;
            _client = null;
            client.Dispose();
        }
    }

    private void DropClient(ObsWebSocketClient client)
    {
        if (ReferenceEquals(_client, client))
        {
            _client = null;
        }

        client.StatusChanged -= OnStatusChanged;
        client.Dispose();
    }

    public void Dispose()
    {
        if (_client is { } client)
        {
            _client = null;
            client.StatusChanged -= OnStatusChanged;
            client.Dispose();
        }

        _gate.Dispose();
    }
}
