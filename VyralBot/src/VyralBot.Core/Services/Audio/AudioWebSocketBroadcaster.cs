using System;
using System.Threading;
using System.Threading.Tasks;

namespace VyralBot.Core.Services.Audio;

public static class AudioWebSocketBroadcaster
{
    public static Func<string, Task>? BroadcastAsync;

    private static int _connectedClientCount;

    /// <summary>
    /// Number of browser-source clients currently connected to the /audio-ws endpoint.
    /// Thread-safe (Interlocked); updated by WebServerService on connect/disconnect.
    /// </summary>
    public static int ConnectedClientCount => Interlocked.CompareExchange(ref _connectedClientCount, 0, 0);

    /// <summary>Called by the web server when an audio WebSocket client connects.</summary>
    public static void ClientConnected() => Interlocked.Increment(ref _connectedClientCount);

    /// <summary>
    /// Called by the web server when an audio WebSocket client disconnects.
    /// Floors at zero so a late decrement racing the reset on server stop cannot go negative.
    /// </summary>
    public static void ClientDisconnected()
    {
        while (true)
        {
            var current = Interlocked.CompareExchange(ref _connectedClientCount, 0, 0);
            if (current == 0)
                break;
            if (Interlocked.CompareExchange(ref _connectedClientCount, current - 1, current) == current)
                break;
        }
    }

    /// <summary>Resets the connected-client count (used when the web server stops).</summary>
    public static void ResetClientCount() => Interlocked.Exchange(ref _connectedClientCount, 0);
}
