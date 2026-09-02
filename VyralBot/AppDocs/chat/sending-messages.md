# Sending Messages

> Sending chat from Vyral: pick a platform (or all of them), type your message, optionally use `{variables}`, and the app delivers it through each platform's own API — with echo suppression so your sends don't double-render.

## Overview

The input bar at the bottom of the [chat window](chat-window.md) can send messages two ways: to the **currently selected platform**, or to **every connected platform at once** with the round Vyral-logo "send to all" button. Text you type can contain [template variables](../chat-features/variables.md) like `{channel}` or `{bot-username}` — anything wrapped in braces is replaced before the message leaves the app.

Sending works for **Twitch, Kick, YouTube, and Discord**. Discord messages are posted as a bot account: manual sends go through the **Primary** bot (the first connected one) to its own selected channel, while replies to a Discord chat message route back through the bot that received it (see [Discord](../platforms/discord.md)). TikTok has no integration yet, so there is no send path for it.

## User Guide

### Send to the selected platform

1. At the bottom of the chat window, click the **platform icon button** on the left of the input bar to open the platform selector.
2. Pick a destination from the list — **All Platforms** (if any platform is connected), **Twitch**, **Kick**, **YouTube**, or **Discord**. Only connected platforms appear; the list refreshes automatically when platforms connect or disconnect.
3. Type your message in the box.
4. Press **Enter** or click **SEND**. The SEND button is tinted with the selected platform's color so you always know where the message is going.

After sending, the message appears in the feed as your own message with a broadcaster badge — and when the platform echoes it back, the echo is filtered out so you do not see it twice.

### Send to all connected platforms

Click the **round Vyral-logo button** to the right of SEND. This broadcasts the current input to **every connected platform simultaneously** (Twitch + Kick + YouTube + Discord in parallel), regardless of what the platform selector says. In the feed you get one teal "Vyral" entry representing the multi-post, rather than four separate copies.

If some platforms fail (for example YouTube while you are offline), you get a single **"Partial Send Failure"** dialog listing each failed platform and why; the platforms that worked still got the message.

### Using variables in what you type

If your message contains a `{`, Vyral runs [variable replacement](../chat-features/variables.md) before sending. Available context variables when sending manually:

| Variable | Resolves to |
|---|---|
| `{bot-username}` | The bot/streamer account name on the selected platform |
| `{channel}` | The connected channel name on the selected platform |
| `{platform}` | The selected platform's name |

Database variables (like your social links — `{twitter}`, `{discord}`, `{full-socials}` — and auto-updating counters) also resolve here, since manual sends go through the full variable service. Unresolvable placeholders are left as-is. Note: for **All-Platforms** sends, no platform context is passed, so only database variables resolve — `{bot-username}`/`{channel}` will not.

### Platform-specific sending behavior

| Platform | How it sends | Notes |
|---|---|---|
| **Twitch** | IRC `PRIVMSG` over the chat WebSocket | Must be connected to the channel. |
| **Kick** | Kick chat API — **sent as your own broadcaster account**, not a separate bot | The OAuth token is auto-refreshed if it is about to expire. |
| **YouTube** | `liveChat/messages` API POST | Only works while you are live; sending while offline fails with an error. On auth failure the token is refreshed and the send retried once. |
| **Discord** | Discord REST send via the gateway client | Manual sends go through the **Primary** bot to **its selected channel** (per-bot, picked on the bot's dashboard — see [Discord](../platforms/discord.md)) and are posted **as the bot account**, not a user. |
| **TikTok** | Not implemented | The send code path contains a `TODO` for this platform. See [TikTok & OBS](../platforms/tiktok-discord-obs.md). |

## How It Works (Developer)

- The entry point is `SendMessageAsync` ([ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs):881). If the selected platform is the "All Platforms" pseudo-option, it delegates to `SendToAllPlatformsAsync` (:1161).
- **Per-platform send branch** (Twitch case at :911, Kick at :995, YouTube at :1081, Discord at :1669), each branch does the same four steps:
  1. If the text contains `{`, build context via `BuildContextVariables` (:1332) and call `IVariableReplacementService.ReplaceVariablesAsync`.
  2. Record the lowercased message text in the platform's echo-suppression dictionary **before** sending.
  3. Call the platform service's `SendMessageAsync`.
  4. Append a synthetic `ChatMessage` to the feed with a broadcaster badge (Twitch `broadcaster=1`; Kick `Broadcaster=Broadcaster` — the PascalCase key works because badge matching is case-insensitive; YouTube and Discord `broadcaster=1`; the Discord entry carries the bot's username and blurple color).
- **Echo suppression**: incoming handlers drop any message whose trimmed, lowercased text matches a recent send within **10 seconds** (15 for YouTube, because its polling loop makes timing less predictable) **and whose author is one of the bot's own sending accounts**. The account scoping was tightened on 2026-08-15 — a viewer typing identical text is no longer filtered. (This replaced an older "bot username" filter that blocked every message the streamer typed, including from the platform website.)
- **Send-to-all fan-out**: `SendToAllPlatformsAsync` runs one `Task.Run` per connected platform in parallel, collects per-platform error strings under a lock, awaits all tasks, then adds a single synthetic feed entry (`Platform="Vyral"`, username "Vyral", teal `#26A69A`, custom "Vyral" badge). If any errors were collected it shows one MessageBox. Echo keys for *all* connected platforms are recorded up front so none of the platforms echo the broadcast back into the feed.
- **Transports**:
  - Twitch: `ITwitchService.SendMessageAsync` ([TwitchService.cs](../../src/VyralBot.Integrations/Twitch/TwitchService.cs):163) delegates to the custom IRC client ([LightweightTwitchIrc.cs](../../src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs)).
  - Kick: `IKickService.SendMessageAsync` ([KickLibChatService.cs](../../src/VyralBot.Integrations/Kick/KickLibChatService.cs):193) refreshes the OAuth token if it expires within 5 minutes, then calls `SendMessageAsUserAsync(broadcasterUserId, ...)` (:224) — messages go out **under the streamer's account**.
  - YouTube: `SendMessageAsync` ([YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs):173) POSTs to the Live Chat API and retries once after a token refresh on 401/403.
  - Discord: `IDiscordService.SendMessageAsync` ([DiscordService.cs](../../src/VyralBot.Integrations/Discord/DiscordService.cs):312) resolves the selected channel's snowflake and posts through the Discord.Net client — as the bot, to the selected channel only.
  - TikTok: no implementation — the switch in `SendToAllPlatformsAsync` carries a `// TODO: Add TikTok, etc.` comment.

## Files & Code

| File | Role |
|---|---|
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | `SendMessageAsync` (:881), `SendToAllPlatformsAsync` (:1161), `BuildContextVariables` (:1944), echo dictionaries (:62-78) |
| [ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml) | Input bar: platform selector popup, TextBox (Enter binding), SEND button, send-to-all button |
| [TwitchService.cs](../../src/VyralBot.Integrations/Twitch/TwitchService.cs) | Twitch send via IRC PRIVMSG |
| [LightweightTwitchIrc.cs](../../src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs) | The WebSocket IRC client that writes the PRIVMSG |
| [KickLibChatService.cs](../../src/VyralBot.Integrations/Kick/KickLibChatService.cs) | Kick send as broadcaster with token auto-refresh |
| [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs) | YouTube send via Live Chat API |
| [VariableReplacementService.cs](../../src/VyralBot.Core/Services/Variables/VariableReplacementService.cs) | `{placeholder}` resolution for typed text |
| [PlatformToBrushConverter.cs](../../src/VyralBot.UI/Converters/PlatformToBrushConverter.cs) | Colors the SEND button from the selected platform |

The send-to-all parallel fan-out (ChatViewModel.cs:1161, trimmed) — one task per platform, errors collected, single synthetic feed entry after:

```csharp
foreach (var platform in ConnectedPlatforms.Where(p => !p.IsAllPlatforms))
{
    sendTasks.Add(Task.Run(async () =>
    {
        try
        {
            switch (platform.Name)
            {
                case "Twitch":
                    if (_twitchService.IsConnected)
                        await _twitchService.SendMessageAsync(messageText);
                    else
                        lock (errors) { errors.Add("Twitch: Not connected"); }
                    break;
                case "Kick":
                    if (_kickService.IsConnected)
                        await _kickService.SendMessageAsync(messageText);
                    else
                        lock (errors) { errors.Add("Kick: Not connected"); }
                    break;
                case "Discord":
                    if (_discordService.IsConnected)
                        await _discordService.SendMessageAsync(messageText);
                    else
                        lock (errors) { errors.Add("Discord: Not connected"); }
                    break;
                // TODO: Add TikTok, etc.
            }
        }
        catch (Exception ex)
        {
            lock (errors) { errors.Add($"{platform.Name}: {ex.Message}"); }
        }
    }));
}
```

Echo-suppression key recording before a single-platform Twitch send (ChatViewModel.cs:957) — recorded *before* the await because the echo can arrive very fast:

```csharp
// Track sent message BEFORE sending (echo arrives very fast!)
// Key uses message-only (no username) so it matches regardless of BotUsername
var twitchMessageKey = messageText.Trim().ToLowerInvariant();
lock (_recentMessagesLock)
{
    _recentTwitchMessages[twitchMessageKey] = DateTime.Now;
}

await _twitchService.SendMessageAsync(messageText);
```

Context variables available to manual sends (ChatViewModel.cs:1944) — Twitch, Kick, and Discord have real values; other platforms get placeholders:

```csharp
private Dictionary<string, string> BuildContextVariables(string platformName)
{
    var contextVars = new Dictionary<string, string>();
    switch (platformName)
    {
        case "Twitch":
            contextVars["bot-username"] = _twitchService.BotUsername ?? "Bot";
            contextVars["channel"] = _twitchService.Channel ?? "";
            break;
        case "Kick":
            contextVars["bot-username"] = _kickService.BotUsername ?? "Bot";
            contextVars["channel"] = _kickService.Channel ?? "";
            break;
        case "Discord":
            contextVars["bot-username"] = _discordService.BotUsername ?? "Bot";
            contextVars["channel"] = _discordService.SelectedChannelId ?? "";
            break;
        default:                       // YouTube / others
            contextVars["bot-username"] = "Bot";
            contextVars["channel"] = "";
            break;
    }
    contextVars["platform"] = platformName;
    return contextVars;
}
```

## Data & Settings

Nothing about sending is persisted. The pieces involved at runtime:

| Data | Where | Notes |
|---|---|---|
| `MessageInput` | In-memory VM property | Cleared after a successful send |
| `ConnectedPlatforms` / `SelectedPlatform` | In-memory VM list | Rebuilt by `UpdateConnectedPlatforms()` (ChatViewModel.cs:517) on connect/disconnect; TikTok hardcoded out |
| Echo-suppression keys | `_recentTwitchMessages` / `_recentKickMessages` / `_recentYoutubeMessages` / `_recentDiscordMessages` dictionaries | 10 s window (15 s YouTube); content-keyed, matched only against the bot's own sending accounts |
| Platform connection state | `Platform` rows (per-profile SQLite) | Drives `IsConnected` checks per service |

## Status & Known Issues

- **Works**: per-platform send to Twitch/Kick/YouTube/Discord, parallel send-to-all with partial-failure reporting, variable replacement in typed text, echo suppression, synthetic feed entries.
- **TikTok sending is not implemented** — explicit `TODO` in the fan-out switch; the platform cannot appear in the selector today. See [known issues](../reference/known-issues.md).
- **Discord sends go through a bot to that bot's selected channel** — manual sends and send-to-all use the **Primary** bot (first connected, falling back to the setup bot); replies to an incoming message use the bot that received it. Each bot's active channel is picked on its per-bot dashboard ([Discord](../platforms/discord.md)). See the [known issues ledger](../reference/known-issues.md) (KI-34, KI-38).
- **Kick sends as the broadcaster's account**, not a bot account — intentional given the OAuth scopes, but worth knowing if you expect a separate bot identity. [Known issues](../reference/known-issues.md).
- **Echo suppression is content + account based**: only messages from the bot's own sending accounts are filtered within the 10/15-second window; viewers typing identical text are unaffected (tightened 2026-08-15). [Known issues](../reference/known-issues.md).
- **YouTube sends require an active live broadcast**; offline sends fail with an error dialog. [Known issues](../reference/known-issues.md).
- **`BuildContextVariables` only differentiates Twitch, Kick, and Discord** — YouTube sends get `bot-username=Bot` and an empty `channel`, so those placeholders resolve to dummy values there. [Known issues](../reference/known-issues.md).
- Command responses (the other direction of chat traffic) ride the live command pipeline — see [Commands](../chat-features/commands.md) and the [known issues ledger](../reference/known-issues.md).
