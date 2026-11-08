# Chat Window

> The unified chat page: one merged, scrolling feed of messages from Twitch, Kick, YouTube, and Discord, with per-platform badges, colors, timestamps, auto-scroll, on-demand history, and quick-settings popups — plus a Discord DM inbox that groups direct messages into per-user conversations you can read and reply to from inside the app.

## Overview

The Chat Window is Vyral's main chat surface. Instead of watching four separate browser tabs, you see every message from every connected platform in a single list. Each message shows the platform it came from (logo + username color), the badges its author has (broadcaster, moderator, VIP, subscriber, verified), and a timestamp. Event notifications such as follows and raids appear in the same feed as gold "System" messages.

The window also has the tools around the feed: an auto-scroll engine with a "new messages" jump banner, buttons to load or delete stored chat history, per-platform quick-settings popups, the message input bar for [sending messages](sending-messages.md), and the Discord DM inbox (see [The DM inbox](#the-dm-inbox-discord-direct-messages)).

## User Guide

### Opening the chat window

Click **Chat** in the sidebar. (The **Chat** card on the Home page opens the [Chat Settings](chat-settings.md) hub instead — that page configures chat, it is not the feed.)

### Reading the feed

Each line in the list shows:

- **Timestamp** in `h:mm tt` format (for example `9:41 PM`) — can be hidden with the header's timestamp toggle.
- **Platform logo** — the Twitch, Kick, YouTube, or Discord icon.
- **Badges** — small images next to the username: broadcaster, moderator, VIP, subscriber, verified. Hover a badge to see its name in a tooltip.
- **Username** in the color the platform reported (streamers and mods often have custom colors).
- **Message text**, wrapped.
- **System/event messages** (follows, subs, raids, bans) render in gold with the username "System". See [Event Reactions](event-reactions.md).
- **Platform chat-state messages** — each platform posts its chat status as a gold System line so you can verify everything is working (and see WHY when it isn't): *"Twitch/Kick chat connected — listening to #channel"*, *"YouTube connected — your channel isn't live right now…"*, *"YouTube live chat is active"*, *"Discord bot is connected, but no chat channel is selected — Discord messages won't appear here until you pick one on the Discord page"*, plus disconnect lines. These are app-only — nothing is ever broadcast to any platform. YouTube's full state list lives on [its platform page](../platforms/youtube.md).

The list is capped at the **500 most recent messages**; older ones are trimmed automatically to keep memory usage flat. Long lists scroll smoothly because rows are virtualized and recycled.

### The DM inbox (Discord direct messages)

Discord direct messages no longer appear in the main feed — they live only in the **DM panel**, so channel talk and private messages never mix:

- **Inbox list** — one row per viewer you have exchanged DMs with, showing the viewer's name, a short preview of the latest message, and an **unread badge** counting messages you have not opened yet.
- **Threads** — click a conversation to open it as a thread; **← All conversations** takes you back to the inbox list.
- **Persistent, unlimited history** — every DM is saved to the database as it arrives; nothing ages out. A thread loads its recent messages by default, and the thread's **Load** button pulls the full saved history into the view (its **Clear** button deletes just that conversation's saved history).
- **Load all / Clear all** — the inbox header has the whole-inbox versions: **Load all** opens every saved conversation with its full history at once, and **Clear all** (after a confirmation) deletes **all** saved DM history. Use with intent — Clear all is irreversible.
- **New DM by user ID** — the **New DM** button expands an inline input where you paste a raw Discord user ID; Vyral resolves the user, opens (or creates) the conversation, and it becomes a replyable thread even if that person has never messaged the bot before. Validation and API errors show right under the input.
- **Unified reply box** — the same input box at the bottom of the chat page serves both chat and DMs: with a thread open it switches into DM mode (the placeholder reads "Reply as Vyral Bot (DM)..."), the send button takes on a Discord-blue gradient, and channel-only controls (like the platform selector) hide so a DM draft cannot misfire into a channel. Replies go through **the bot that received the DM**, so the viewer always sees the answer from the same account they messaged.
- **Layout** — toggle **DMs** to show the DM panel, or pick **Combined** for a side-by-side view with the chat feed on the left and the DM inbox on the right.

The [account-linking DM flow](../chat-features/account-linking.md) flows through the same inbox — a viewer DMing `link` or a code is just another conversation, and the linking bot's answer lands as a DM too. DM conversations also appear in the viewer's [profile window](../chat-features/viewer-profiles.md), so the full private history is visible without leaving the profile.

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
| **Decrease chat font size** (A-with-down-arrow icon) | Shrinks message text by 1 pt per click, clamped to 8 pt minimum. |
| **Increase chat font size** (A-with-up-arrow icon) | Grows message text by 1 pt per click, clamped to 36 pt maximum. |
| **Toggle timestamps** (clock icon) | Shows/hides the per-message timestamps in the feed. |
| **Toggle Auto-Scroll** (down-arrow icon) | Enables/disables automatic scrolling to new messages. |
| **Load Chat History** (history icon) | Loads stored past messages from the database into the feed (see below). |
| **Clear Chat** (eraser icon) | Empties the on-screen list only. Nothing is deleted from the database. |
| **Delete Chat History** (database-remove icon) | After a confirmation dialog, permanently deletes stored history for connected channels from the database, then clears the view. |

**Chat appearance is real and persisted**: the font-size +/- buttons (clamped 8–36) and the timestamp toggle change the feed immediately and are saved to the `Chat.Appearance` config key, so your font size and timestamp preference survive a restart.

### Loading chat history

- History is **not** loaded when you open the chat window — this is deliberate (loading it eagerly caused a large memory spike during development).
- Click **Load Chat History** to pull up to **200 messages per platform** (Kick, Twitch, and YouTube) from the database and insert them, in chronological order, above the live messages. The log line reports the per-platform counts it loaded.
- The button's tooltip says "50 messages" — that is stale text; the actual limit is 200 per platform.
- **Discord history is persisted but not reloaded** — Discord rows are written to the `ChatMessages` table live, but this button does not yet merge them back into the feed (see [Discord](../platforms/discord.md)).
- The load runs once per session; the button will not re-load or duplicate already-loaded messages.

### What the chat window does NOT have

Being honest about the current build:

- **No per-message moderation in the feed** — there is no banned-word filter and no timeout/ban/purge buttons on individual messages. Moderation lives in two dedicated places: Discord's [built-in moderation commands](../platforms/discord.md#built-in-moderation-commands) and the tier-gated [mod system](../architecture/moderator-guide.md) with its private `#vyral-mods` channel and Moderator dashboard (reachable from the Chat Hub's **Moderator** chip). The only moderation you will see *in the feed itself* is a passive red "X was banned by Y" system message when Kick sends a moderation webhook.
- **No emote rendering** — messages display as plain text, even on Twitch.
- **No mention/highlight detection** — mentions are not highlighted (usernames *are* clickable now — they open [viewer profiles](../chat-features/viewer-profiles.md)).
- **No message search**.
- **No popout window** — chat lives only inside the main window.
- **No TikTok messages** — TikTok has no integration yet; its header button and settings exist but nothing flows through them. (Discord messages do arrive — see [Discord](../platforms/discord.md).) See [TikTok & OBS](../platforms/tiktok-discord-obs.md).

## How It Works (Developer)

- The page is [ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml) (~1390 lines) + [ChatView.xaml.cs](../../src/VyralBot.UI/Views/ChatView.xaml.cs), bound to the singleton [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) (~7200 lines).
- `ChatViewModel` is registered as a **DI singleton** and is created during profile load *before* platform auto-connect, specifically so its EventBus subscriptions exist before any message can arrive ([MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) `OnProfileLoadedAsync`).
- `InitializeAsync()` guards with `_isInitialized` and subscribes (via `IEventBus.SubscribeAsync<T>`) to roughly 20 event types (ChatViewModel.cs:2151+, the subscription block): the four per-platform chat message events (Twitch, Kick, YouTube, Discord), the generic `ChatMessageReceivedEvent`, routing/connection change events, 7 Kick event types, and 6 Twitch event types.
- Incoming per-platform handlers (`OnTwitchChatMessageReceivedAsync` ChatViewModel.cs:3070, `OnKickChatMessageReceivedAsync` :5653, `OnYouTubeChatMessageReceivedAsync` :5553, `OnDiscordChatMessageReceivedAsync` :5743) each: check the platform display-filter flag, check **echo suppression** (Discord's handler skips the bot's own account outright — the UI already adds outgoing bot messages locally), then append a `ChatMessage` to `Messages` and trim to 500. Discord messages are also persisted through the chat store here. Discord **direct messages** are diverted before rendering: instead of the main feed they are routed into the DM inbox (grouped per user, with unread badges), and a reply sent from a DM thread goes out through the same bot instance that received the DM.
- **Echo suppression**: before the VM sends anything, it records the lowercased text in `_recentTwitchMessages` / `_recentKickMessages` / `_recentYoutubeMessages` / `_recentDiscordMessages`. Incoming messages whose text matches an entry newer than 10 seconds (15 seconds for YouTube, because of polling) **and that come from one of the bot's own sending accounts** are dropped, so the bot does not render its own outgoing messages twice. The account scoping was tightened on 2026-08-15 — a viewer typing identical text is no longer filtered. (This replaced an older bot-username filter that blocked *all* of the streamer's own messages, even ones typed on the platform website.)
- **Display filters**: `TwitchFilterEnabled`, `KickFilterEnabled`, `YoutubeFilterEnabled`, `TiktokFilterEnabled`, `DiscordFilterEnabled` drop incoming messages per platform. They update live from `PlatformChatRoutingChangedEvent` (`OnPlatformRoutingChangedAsync`, ChatViewModel.cs:4297). Kick's flag is bootstrapped from the Kick settings VM and Discord's from the `Platform` row's `DiscordChatEnabled` (:2243); Twitch's toggle now persists `TwitchChatEnabled` and publishes the routing event like the others (fixed 2026-08-15).
- **Auto-scroll engine** lives in the view's code-behind: it locates the inner `ScrollViewer` in the visual tree, uses `ScrollChanged` to detect whether the user is within 20 px of the bottom, and toggles `_vm.AutoScroll`. The `Messages.CollectionChanged` handler either scrolls to end or increments `_unreadCount` and shows the "N new messages" banner (ChatView.xaml.cs:109-185). Collection-changed subscription is attached/detached on Loaded/Unloaded/DataContextChanged to avoid double-subscribe.
- **Badges**: `ChatMessage.ComputeBadgeImages()` ([ChatMessage.cs](../../src/VyralBot.UI/ViewModels/ChatMessage.cs), its own file) maps badge keys case-insensitively (broadcaster/moderator/vip/subscriber/verified) to Twitch CDN URLs (`static-cdn.jtvnw.net`) for *both* Twitch and Kick messages; unknown badge keys render nothing. Images are cached in a static `ConcurrentDictionary`; `LoadBadgeImage` supports `data:image/...;base64` URIs and HTTP downloads with a browser User-Agent and hardcoded Kick referer/origin headers. Display priority: broadcaster < moderator < vip < subscriber < verified. A much richer [BadgesToImagesConverter.cs](../../src/VyralBot.UI/Converters/BadgesToImagesConverter.cs) (50+ Twitch badges, Kick CDN, local Vyral badge) exists but the message template binds `BadgeImages` directly, so the converter is effectively unused.
- **Lifecycle**: `Cleanup()` (ChatViewModel.cs:7172) unsubscribes all ~20 subscriptions, resets IDs and `_isInitialized`, and clears state — required so the singleton can re-subscribe after a profile switch. Called from [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) on shutdown and from MainViewModel on profile switches.
- **Quick-settings popups** are hosted in [LazyPopupContent.cs](../../src/VyralBot.UI/Controls/LazyPopupContent.cs), which defers constructing the popup's view until it is first opened (a memory optimization) — see [Chat Settings](chat-settings.md).

### How messages arrive

```
Twitch IRC WebSocket ──┐
Kick Pusher WebSocket ─┼→ typed event → EventBus → ChatViewModel → filter flag → echo check → render
YouTube polling loop ──┤                                    └→ IChatMessageStore → SQLite (Kick/Twitch/YouTube/Discord)
Discord.Net gateway ───┘                                    └→ ChatMessageReceivedEvent → command pipeline
```

Transports: Twitch uses the custom [LightweightTwitchIrc.cs](../../src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs); Kick uses KickLib's Pusher client in [KickLibChatService.cs](../../src/VyralBot.Integrations/Kick/KickLibChatService.cs); YouTube polls the Live Chat API in [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs); Discord uses the [DiscordService.cs](../../src/VyralBot.Integrations/Discord/DiscordService.cs) gateway client. Details in [Twitch](../platforms/twitch.md), [Kick](../platforms/kick.md), [YouTube](../platforms/youtube.md), and [Discord](../platforms/discord.md).

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
| [DataChatMessageStore.cs](../../src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) | DB-backed history store (Kick, Twitch, YouTube, and Discord) |
| [TwitchChatMessageEvent.cs](../../src/VyralBot.Integrations/Twitch/Events/TwitchChatMessageEvent.cs) | Twitch chat event payload |
| [KickChatMessageEvent.cs](../../src/VyralBot.Integrations/Kick/Events/KickChatMessageEvent.cs) | Kick chat event payload |
| [YouTubeChatMessageEvent.cs](../../src/VyralBot.Integrations/YouTube/Events/YouTubeChatMessageEvent.cs) | YouTube chat event payload |
| [DiscordChatMessageEvent.cs](../../src/VyralBot.Integrations/Discord/Events/DiscordChatMessageEvent.cs) | Discord chat event payload (guild/channel names, mod/broadcaster flags) |
| [ChatMessage.cs](../../src/VyralBot.Data/Entities/ChatMessage.cs) | Persisted history row entity |

The message row template (ChatView.xaml, `ListBox.ItemTemplate`) — timestamp, platform logo (style with per-platform DataTriggers), badge images, colored username, wrapped text:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

    <!-- Timestamp (visibility follows the ShowTimestamps header toggle) -->
    <TextBlock Grid.Column="0" Text="{Binding Timestamp, StringFormat='h:mm tt'}"
               FontSize="10" Foreground="{StaticResource TextSecondaryBrush}"
               Visibility="{Binding DataContext.ShowTimestamps, RelativeSource={RelativeSource AncestorType=UserControl},
                            Converter={StaticResource BooleanToVisibilityConverter}}"/>

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
| Persisted chat history | `ChatMessages` table (per-profile SQLite) | Written/read by `DataChatMessageStore`; Kick + Twitch + YouTube rows are also reloaded by Load Chat History — Discord rows are written but not yet reloaded (see [Discord](../platforms/discord.md)) |
| Persisted DM history | `DiscordDmMessages` table (per-profile SQLite) | Unlimited, never trimmed automatically; per-thread Clear / inbox Clear all delete it deliberately |
| `ChatMessage` entity | Platform, Channel, MessageId, UserId, Username, DisplayName, Content, Timestamp, Color, IsModerator/IsSubscriber/IsOwner, BadgesJson, IsSystemMessage, CreatedAt | Badges serialized to JSON on save |
| Platform display flags | `Platform` entity: `TwitchChatEnabled`, `KickChatEnabled`, `YoutubeChatEnabled`, `TiktokChatEnabled`, `DiscordChatEnabled` | Drive the per-platform display filters; see [Chat Settings](chat-settings.md) |
| Echo-suppression keys | In-memory dictionaries per platform | 10 s window (15 s YouTube); content-keyed, matched only against the bot's own sending accounts; never persisted |

The in-UI `ChatMessage` model ([ChatMessage.cs](../../src/VyralBot.UI/ViewModels/ChatMessage.cs), its own file) is a plain `INotifyPropertyChanged` class distinct from the DB entity: Platform, Username, Message, Timestamp, Color, IsModerator/IsSubscriber/IsVip/IsBroadcaster, IsSystemMessage, a `Badges` dictionary, and computed `BadgeImages`.

## Status & Known Issues

- **Works**: merged live feed for Twitch/Kick/YouTube/Discord, badges, colors, timestamps (toggleable), chat font size +/- (8–36, persisted with the timestamp preference via `Chat.Appearance`), auto-scroll + unread banner, on-demand history (Kick/Twitch/YouTube), clear/delete history, quick-settings popups, the Discord DM inbox (per-user conversations with previews and unread badges, persistent unlimited history with per-thread and inbox-wide Load/Clear, New DM by Discord user ID, a unified reply box with DM-mode gradient send button that replies through the bot that received the DM, and the Combined side-by-side layout), clickable usernames opening [viewer profiles](../chat-features/viewer-profiles.md), event system messages, and — since 2026-08-15 — the full chat command pipeline (commands, currency, giveaways, polls, watchtime) driven by `ChatMessageReceivedEvent`, on Discord as well. See [Commands](../chat-features/commands.md) and the [known issues ledger](../reference/known-issues.md) (KI-1 resolved).
- **Discord history is not reloaded** — Discord messages persist to the `ChatMessages` table, but Load Chat History merges only Kick/Twitch/YouTube rows back into the feed. Tracked in the [known issues ledger](../reference/known-issues.md) (KI-34).
- **Twitch messages can be filtered out**: the Twitch "Chat Enabled" toggle now persists `TwitchChatEnabled` and publishes the routing event (fixed 2026-08-15), so all five platform filters behave the same.
- **All-Platforms popup buttons are placeholders**: Start Giveaway / Start Poll / Send Announcement / Trigger Event have no command bindings (ChatView.xaml:94-111).
- **Echo suppression is content + account based**: only messages from the bot's own sending accounts are filtered within the 10/15-second window; viewers typing identical text are no longer affected (tightened 2026-08-15).
- **Tooltip drift**: Load History says "50 messages", loads up to 200 per platform.
- **No moderation, emotes, search, mentions, or popout** (see above).
