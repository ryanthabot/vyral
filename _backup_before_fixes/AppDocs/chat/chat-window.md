# Chat Window

> The unified chat page: one merged, scrolling feed of messages from Twitch, Kick, and YouTube, with per-platform badges, colors, timestamps, auto-scroll, on-demand history, and quick-settings popups.

## Overview

The Chat Window is VyralBot's main chat surface. Instead of watching three separate browser tabs, you see every message from every connected platform in a single list. Each message shows the platform it came from (logo + username color), the badges its author has (broadcaster, moderator, VIP, subscriber, verified), and a timestamp. Event notifications such as follows and raids appear in the same feed as gold "System" messages.

The window also has the tools around the feed: an auto-scroll engine with a "new messages" jump banner, buttons to load or delete stored chat history, per-platform quick-settings popups, and the message input bar for [sending messages](sending-messages.md).

## User Guide

### Opening the chat window

Click **Chat** in the sidebar. (The **Chat** card on the Home page opens the [Chat Settings](chat-settings.md) hub instead — that page configures chat, it is not the feed.)

### Reading the feed

Each line in the list shows:

- **Timestamp** in `h:mm tt` format (for example `9:41 PM`).
- **Platform logo** — the Twitch, Kick, or YouTube icon.
- **Badges** — small images next to the username: broadcaster, moderator, VIP, subscriber, verified. Hover a badge to see its name in a tooltip.
- **Username** in the color the platform reported (streamers and mods often have custom colors).
- **Message text**, wrapped.
- **System/event messages** (follows, subs, raids, bans) render in gold with the username "System". See [Event Reactions](event-reactions.md).

The list is capped at the **500 most recent messages**; older ones are trimmed automatically to keep memory usage flat. Long lists scroll smoothly because rows are virtualized and recycled.

### Auto-scroll and the "new messages" banner

- When you are at the bottom of the list, new messages scroll into view automatically.
- Scroll up (more than about 20 pixels from the bottom) and auto-scroll turns off so you can read undisturbed. A collapsed banner appears at the top of the feed.
- While scrolled up, unread messages are counted and the banner shows **"N new messages"**.
- Click the banner (or scroll back to the bottom) to jump to the latest messages and re-enable auto-scroll.
- You can also force auto-scroll on/off with the **Toggle Auto-Scroll** button in the header (the down-arrow icon).

### Header controls

Left side of the header:

- **Per-platform icon buttons** — each opens a quick-settings popup for that platform's chat (see [Chat Settings](chat-settings.md)). These buttons only appear for connected platforms (Kick's appears when the channel is configured, even if disconnected).
- **All Platforms (Vyral logo) button** — opens a popup with *Start Giveaway / Start Poll / Send Announcement / Trigger Event*. These four buttons are **visual placeholders with no functionality** — see [Known Issues](../reference/known-issues.md).

Right side of the header, in order:

| Button | What it does |
|---|---|
| **Toggle Auto-Scroll** (down-arrow icon) | Enables/disables automatic scrolling to new messages. |
| **Load Chat History** (history icon) | Loads stored past messages from the database into the feed (see below). |
| **Clear Chat** (eraser icon) | Empties the on-screen list only. Nothing is deleted from the database. |
| **Delete Chat History** (database-remove icon) | After a confirmation dialog, permanently deletes stored history for connected channels from the database, then clears the view. |

### Loading chat history

- History is **not** loaded when you open the chat window — this is deliberate (loading it eagerly caused a large memory spike during development).
- Click **Load Chat History** to pull up to **200 messages per platform** (Kick and Twitch only) from the database and insert them, in chronological order, above the live messages.
- The button's tooltip says "50 messages" — that is stale text; the actual limit is 200 per platform.
- The load runs once per session; the button will not re-load or duplicate already-loaded messages.
- **YouTube messages are never persisted**, so YouTube has no history to load. See [Known Issues](../reference/known-issues.md).

### What the chat window does NOT have

Being honest about the current build:

- **No moderation** — no banned-word filter, no timeout/ban/purge/delete actions, no per-message context menu. The only moderation you will see is a passive red "X was banned by Y" system message when Kick sends a moderation webhook.
- **No emote rendering** — messages display as plain text, even on Twitch.
- **No mention/highlight detection** — usernames are not clickable and mentions are not highlighted.
- **No message search**.
- **No popout window** — chat lives only inside the main window.
- **No TikTok/Discord messages** — those platforms have no integration yet; their header buttons and settings exist but nothing flows through them. See [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md).

## How It Works (Developer)

- The page is [ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml) (558 lines) + [ChatView.xaml.cs](../../src/VyralBot.UI/Views/ChatView.xaml.cs), bound to the singleton [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) (2,880 lines).
- `ChatViewModel` is registered as a **DI singleton** and is created during profile load *before* platform auto-connect, specifically so its EventBus subscriptions exist before any message can arrive ([MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) `OnProfileLoadedAsync`).
- `InitializeAsync()` guards with `_isInitialized` and subscribes (via `IEventBus.SubscribeAsync<T>`) to roughly 20 event types (ChatViewModel.cs:207-270): the three per-platform chat message events, the generic `ChatMessageReceivedEvent`, routing/connection change events, 7 Kick event types, and 6 Twitch event types.
- Incoming per-platform handlers (`OnTwitchChatMessageReceivedAsync` ChatViewModel.cs:503, `OnKickChatMessageReceivedAsync` :1611, `OnYouTubeChatMessageReceivedAsync` :1527) each: check the platform display-filter flag, check **echo suppression**, then append a `ChatMessage` to `Messages` and trim to 500.
- **Echo suppression**: before the VM sends anything, it records the lowercased text in `_recentTwitchMessages` / `_recentKickMessages` / `_recentYoutubeMessages`. Incoming messages whose text matches an entry newer than 10 seconds (15 seconds for YouTube, because of polling) are dropped, so the bot does not render its own outgoing messages twice. This replaced an older bot-username filter that blocked *all* of the streamer's own messages, even ones typed on the platform website.
- **Display filters**: `TwitchFilterEnabled`, `KickFilterEnabled`, `YoutubeFilterEnabled`, `TiktokFilterEnabled`, `DiscordFilterEnabled` drop incoming messages per platform. They update live from `PlatformChatRoutingChangedEvent` (`OnPlatformRoutingChangedAsync`, ChatViewModel.cs:840). Kick's flag is bootstrapped from the Kick settings VM (ChatViewModel.cs:275-283). Twitch's defaults to `true` and has no publisher — see [Chat Settings](chat-settings.md) and [Known Issues](../reference/known-issues.md).
- **Auto-scroll engine** lives in the view's code-behind: it locates the inner `ScrollViewer` in the visual tree, uses `ScrollChanged` to detect whether the user is within 20 px of the bottom, and toggles `_vm.AutoScroll`. The `Messages.CollectionChanged` handler either scrolls to end or increments `_unreadCount` and shows the "N new messages" banner (ChatView.xaml.cs:109-185). Collection-changed subscription is attached/detached on Loaded/Unloaded/DataContextChanged to avoid double-subscribe.
- **Badges**: `ChatMessage.ComputeBadgeImages()` (ChatViewModel.cs:2785) maps badge keys case-insensitively (broadcaster/moderator/vip/subscriber/verified) to Twitch CDN URLs (`static-cdn.jtvnw.net`) for *both* Twitch and Kick messages; unknown badge keys render nothing. Images are cached in a static `ConcurrentDictionary`; `LoadBadgeImage` supports `data:image/...;base64` URIs and HTTP downloads with a browser User-Agent and hardcoded Kick referer/origin headers (ChatViewModel.cs:2737). Display priority: broadcaster < moderator < vip < subscriber < verified (ChatViewModel.cs:2848). A much richer [BadgesToImagesConverter.cs](../../src/VyralBot.UI/Converters/BadgesToImagesConverter.cs) (50+ Twitch badges, Kick CDN, local Vyral badge) exists but the message template binds `BadgeImages` directly, so the converter is effectively unused.
- **Lifecycle**: `Cleanup()` (ChatViewModel.cs:2510) unsubscribes all ~20 subscriptions, resets IDs and `_isInitialized`, and clears state — required so the singleton can re-subscribe after a profile switch. Called from [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) on shutdown and from MainViewModel on profile switches.
- **Quick-settings popups** are hosted in [LazyPopupContent.cs](../../src/VyralBot.UI/Controls/LazyPopupContent.cs), which defers constructing the popup's view until it is first opened (a memory optimization) — see [Chat Settings](chat-settings.md).

### How messages arrive

```
Twitch IRC WebSocket ──┐
Kick Pusher WebSocket ─┼→ typed event → EventBus → ChatViewModel → filter flag → echo check → render
YouTube polling loop ──┘                                    └→ IChatMessageStore → SQLite (Kick/Twitch only)
```

Transports: Twitch uses the custom [LightweightTwitchIrc.cs](../../src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs); Kick uses KickLib's Pusher client in [KickLibChatService.cs](../../src/VyralBot.Integrations/Kick/KickLibChatService.cs); YouTube polls the Live Chat API in [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs). Details in [Twitch](../platforms/twitch.md), [Kick](../platforms/kick.md), and [YouTube](../platforms/youtube.md).

## Files & Code

| File | Role |
|---|---|
| [ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml) | The chat page layout: header buttons, popups, message list template, input bar |
| [ChatView.xaml.cs](../../src/VyralBot.UI/Views/ChatView.xaml.cs) | Auto-scroll engine, unread-count banner, collection-changed wiring |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Singleton VM: subscriptions, incoming handling, filters, echo suppression, history, `ChatMessage` model |
| [LazyPopupContent.cs](../../src/VyralBot.UI/Controls/LazyPopupContent.cs) | Deferred-content popup host for quick-settings views |
| [PlatformToImageConverter.cs](../../src/VyralBot.UI/Converters/PlatformToImageConverter.cs) | Platform name → logo image |
| [PlatformToBrushConverter.cs](../../src/VyralBot.UI/Converters/PlatformToBrushConverter.cs) | Platform name → color brush (SEND button, selector) |
| [BadgesToImagesConverter.cs](../../src/VyralBot.UI/Converters/BadgesToImagesConverter.cs) | Rich badge table (currently unused by the message template) |
| [DataChatMessageStore.cs](../../src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) | DB-backed history store (Kick/Twitch only) |
| [TwitchChatMessageEvent.cs](../../src/VyralBot.Integrations/Twitch/Events/TwitchChatMessageEvent.cs) | Twitch chat event payload |
| [KickChatMessageEvent.cs](../../src/VyralBot.Integrations/Kick/Events/KickChatMessageEvent.cs) | Kick chat event payload |
| [YouTubeChatMessageEvent.cs](../../src/VyralBot.Integrations/YouTube/Events/YouTubeChatMessageEvent.cs) | YouTube chat event payload |
| [ChatMessage.cs](../../src/VyralBot.Data/Entities/ChatMessage.cs) | Persisted history row entity |

The message row template (ChatView.xaml, `ListBox.ItemTemplate`) — timestamp, platform logo (style with per-platform DataTriggers), badge images, colored username, wrapped text:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

    <!-- Timestamp -->
    <TextBlock Grid.Column="0" Text="{Binding Timestamp, StringFormat='h:mm tt'}"
               FontSize="10" Foreground="{StaticResource TextSecondaryBrush}"/>

    <!-- Platform Logo + Badges + Username -->
    <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="0,0,8,0">
        <Image Style="{StaticResource PlatformLogoStyle}" Margin="0,0,6,0"/>

        <!-- Badges - using Twitch badge images for both platforms -->
        <ItemsControl ItemsSource="{Binding BadgeImages}" Margin="0,0,4,0">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Image Width="18" Height="18" Source="{Binding Image}"
                           ToolTip="{Binding Name}" Stretch="Uniform"/>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <TextBlock Text="{Binding Username}" FontWeight="SemiBold"
                   Foreground="{Binding Color}" VerticalAlignment="Center"/>
        <TextBlock Text=":" Foreground="{StaticResource TextSecondaryBrush}" Margin="2,0"/>
    </StackPanel>

    <!-- Message -->
    <TextBlock Grid.Column="2" Text="{Binding Message}" TextWrapping="Wrap"/>
</Grid>
```

The auto-scroll decision in code-behind (ChatView.xaml.cs:109) — stay glued to the bottom unless the user scrolled away, re-arm when they return:

```csharp
private void ChatScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
{
    var sv = sender as ScrollViewer;
    if (sv == null) return;

    // If content height changed (new messages), and user was at bottom before, keep them at bottom
    if (e.ExtentHeightChange != 0)
    {
        if (_isUserAtBottom && _vm?.AutoScroll == true)
        {
            ScrollToEnd(); // New content appended - scroll to end
            return;
        }
    }

    // Update _isUserAtBottom flag based on current offset
    var tolerance = 20.0; // pixels
    var wasAtBottom = _isUserAtBottom;
    _isUserAtBottom = (sv.VerticalOffset + sv.ViewportHeight) >= (sv.ExtentHeight - tolerance);

    if (!wasAtBottom && _isUserAtBottom)      // user returned to bottom
    {
        _vm.AutoScroll = true;
        _unreadCount = 0;                      // Clear unread count and hide banner
        NewMessagesButton.Visibility = Visibility.Collapsed;
    }
    else if (wasAtBottom && !_isUserAtBottom) // user scrolled up
    {
        _vm.AutoScroll = false;
    }
}
```

And the unread counter that drives the banner (ChatView.xaml.cs:153) — counting new items while parked mid-scroll:

```csharp
private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    if (e.Action != NotifyCollectionChangedAction.Add) return;

    // If AutoScroll is enabled, always scroll to end so new messages are visible.
    if (_vm != null && _vm.AutoScroll)
    {
        Dispatcher.BeginInvoke(() => ScrollToEnd(), DispatcherPriority.Background);
        return;
    }

    // If AutoScroll is disabled (user scrolled up), increment unread counter and show banner
    _unreadCount += e.NewItems?.Count ?? 1;
    Dispatcher.BeginInvoke(() =>
    {
        NewMessagesButton.Content = _unreadCount == 1 ? "1 new message" : $"{_unreadCount} new messages";
        NewMessagesButton.Visibility = Visibility.Visible;
    }, DispatcherPriority.Background);
}
```

## Data & Settings

| Data | Where | Notes |
|---|---|---|
| Live feed messages | In-memory `ObservableCollection` (VM) | Capped at 500, trimmed from the start |
| Persisted chat history | `ChatMessages` table (per-profile SQLite) | Written/read by `DataChatMessageStore`; Kick + Twitch only |
| `ChatMessage` entity | Platform, Channel, MessageId, UserId, Username, DisplayName, Content, Timestamp, Color, IsModerator/IsSubscriber/IsOwner, BadgesJson, IsSystemMessage, CreatedAt | Badges serialized to JSON on save |
| Platform display flags | `Platform` entity: `TwitchChatEnabled`, `KickChatEnabled`, `YoutubeChatEnabled`, `TiktokChatEnabled`, `DiscordChatEnabled` | Drive the per-platform display filters; see [Chat Settings](chat-settings.md) |
| Echo-suppression keys | In-memory dictionaries per platform | 10 s window (15 s YouTube); never persisted |

The in-UI `ChatMessage` model (bottom of ChatViewModel.cs:2657) is a plain `INotifyPropertyChanged` class distinct from the DB entity: Platform, Username, Message, Timestamp, Color, IsModerator/IsSubscriber/IsVip/IsBroadcaster, IsSystemMessage, a `Badges` dictionary, and computed `BadgeImages`.

## Status & Known Issues

- **Works**: merged live feed for Twitch/Kick/YouTube, badges, colors, timestamps, auto-scroll + unread banner, on-demand history (Kick/Twitch), clear/delete history, quick-settings popups, event system messages.
- **The chat command pipeline is dead**: the generic `ChatMessageReceivedEvent` handler that would run commands, currency, giveaways, polls, and watchtime is subscribed but nothing ever publishes that event — `!commands` typed in chat do nothing. See [Commands](../chat-features/commands.md) and the [known issues ledger](../reference/known-issues.md).
- **Twitch messages cannot be filtered out**: the "Chat Enabled" toggle for Twitch is cosmetic; Twitch has no routing-event publisher. [Known issues](../reference/known-issues.md).
- **YouTube history is not persisted**: `DataChatMessageStore` only maps Kick/Twitch events, so YouTube messages show live but never load from history. [Known issues](../reference/known-issues.md).
- **All-Platforms popup buttons are placeholders**: Start Giveaway / Start Poll / Send Announcement / Trigger Event have no command bindings (ChatView.xaml:94-111). [Known issues](../reference/known-issues.md).
- **Echo suppression is content-based**: a viewer typing the exact same text the bot sent within the window is also filtered out. [Known issues](../reference/known-issues.md).
- **Tooltip drift**: Load History says "50 messages", loads up to 200 per platform. [Known issues](../reference/known-issues.md).
- **No moderation, emotes, search, mentions, or popout** (see above). [Known issues](../reference/known-issues.md).
