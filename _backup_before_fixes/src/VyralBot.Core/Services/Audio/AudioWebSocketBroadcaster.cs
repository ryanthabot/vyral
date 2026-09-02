using System;
using System.Threading.Tasks;

namespace VyralBot.Core.Services.Audio;

public static class AudioWebSocketBroadcaster
{
    public static Func<string, Task>? BroadcastAsync;
}
