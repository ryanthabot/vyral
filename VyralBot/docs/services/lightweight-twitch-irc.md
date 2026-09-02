# Lightweight Twitch IRC Client

## Overview
Custom WebSocket-based Twitch IRC client. Replaces TwitchLib.Client for lower overhead. Handles IRC protocol parsing, badge extraction, and keep-alive.

## Source Files

### LightweightTwitchIrc
- **File:** `src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs` (~377 lines)
- **Namespace:** `VyralBot.Integrations.Twitch`
- **Class:** `sealed class LightweightTwitchIrc : IDisposable`
- **Constructor:** `ILogger<LightweightTwitchIrc>`

### WebSocket Connection
- Uses `System.Net.WebSockets.ClientWebSocket`
- Connects to `wss://irc-ws.chat.twitch.tv:443`

### Public Properties
- `string Username` — authenticated username
- `string Channel` — joined channel
- `bool IsConnected` — WebSocket connection state

### Public Events
- `MessageReceived` — fires with `TwitchMessageEventArgs` (Username, Message, Channel, IsModerator, IsSubscriber, IsVip, IsBroadcaster, Badges dictionary)
- `Connected` — fires on successful IRC connection
- `Disconnected` — fires on WebSocket close
- `AuthenticationFailed` — fires on NOTICE login failure

### Public Methods
- `ConnectAsync(string username, string oauthToken, string channel)` — sends PASS, NICK, CAP REQ (twitch.tv/tags, twitch.tv/commands), JOIN, then starts read + keepalive tasks
- `SendMessageAsync(string message)` — sends PRIVMSG to joined channel
- `Disconnect()` — cancels background tasks, disposes
- `Dispose()` — closes WebSocket gracefully (CloseOutputAsync)

### Internal Implementation
- `ReadMessagesAsync()` — continuous read loop using `ArrayPool<byte>` for buffer management
- `ProcessIrcMessage(string)` — parses IRC message format, extracts tags (badges, display-name, mod, subscriber, etc.), handles PRIVMSG, PING (sends PONG), NOTICE
- `KeepAliveAsync()` — sends PING every 60 seconds to maintain connection

### TwitchMessageEventArgs
- `string Username`
- `string Message`
- `string Channel`
- `bool IsModerator`
- `bool IsSubscriber`
- `bool IsVip`
- `bool IsBroadcaster`
- `Dictionary<string, string> Badges`

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs` | WebSocket IRC client (377 lines) |
