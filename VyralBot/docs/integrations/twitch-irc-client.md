# Twitch IRC Client — LightweightTwitchIrc

## Overview
Custom WebSocket-based Twitch IRC client. Replaces TwitchLib.Client for lower overhead and full control over the WebSocket lifecycle.

## Source File
- **File:** `src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs` (~377 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `sealed class LightweightTwitchIrc : IDisposable`
- **Constructor:** `ILogger<LightweightTwitchIrc>`

## WebSocket Connection
- Uses `System.Net.WebSockets.ClientWebSocket`
- Connects to `wss://irc-ws.chat.twitch.tv:443`

## Public Properties
| Property | Type | Description |
|----------|------|-------------|
| `Username` | `string` | Authenticated username |
| `Channel` | `string` | Joined channel |
| `IsConnected` | `bool` | WebSocket connection state |

## Public Events
| Event | Args | Description |
|-------|------|-------------|
| `MessageReceived` | `TwitchMessageEventArgs` | Chat message received |
| `Connected` | `EventArgs` | IRC connection established |
| `Disconnected` | `EventArgs` | WebSocket closed |
| `AuthenticationFailed` | `EventArgs` | NOTICE login failure |

## Public Methods
| Method | Description |
|--------|-------------|
| `ConnectAsync(string username, string oauthToken, string channel)` | Sends PASS, NICK, CAP REQ (twitch.tv/tags, twitch.tv/commands), JOIN; starts read + keepalive tasks |
| `SendMessageAsync(string message)` | Sends PRIVMSG to joined channel |
| `Disconnect()` | Cancels background tasks, disposes |
| `Dispose()` | Closes WebSocket gracefully via CloseOutputAsync |

## Internal Implementation
- **`ReadMessagesAsync()`** — continuous read loop using `ArrayPool<byte>` for buffer management
- **`ProcessIrcMessage(string)`** — parses IRC message format, extracts tags (badges, display-name, mod, subscriber, etc.), handles PRIVMSG, PING (auto PONG), NOTICE
- **`KeepAliveAsync()`** — sends PING every 60 seconds to maintain connection

## TwitchMessageEventArgs
| Property | Type |
|----------|------|
| `Username` | `string` |
| `Message` | `string` |
| `Channel` | `string` |
| `IsModerator` | `bool` |
| `IsSubscriber` | `bool` |
| `IsVip` | `bool` |
| `IsBroadcaster` | `bool` |
| `Badges` | `Dictionary<string, string>` |

## Connection Flow
```
ConnectAsync()
  1. Create ClientWebSocket
  2. Connect to wss://irc-ws.chat.twitch.tv:443
  3. Send: PASS oauth:{token}
  4. Send: NICK {username}
  5. Send: CAP REQ :twitch.tv/tags twitch.tv/commands
  6. Send: JOIN #{channel}
  7. Start ReadMessagesAsync() background task
  8. Start KeepAliveAsync() background task (PING every 60s)
```

## Usage by TwitchService
```
TwitchService.ConnectAsync()
  → new LightweightTwitchIrc(loggerFactory.CreateLogger())
  → Wire events: MessageReceived, Connected, Disconnected, AuthenticationFailed
  → client.ConnectAsync(username, token, channel)
  → OnMessageReceived → publish TwitchChatMessageEvent to EventBus
```

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs` | WebSocket IRC client (377 lines) |
