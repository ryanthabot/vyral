# Chat Hub (Chat Management)

## Overview
Unified per-platform chat configuration surface. Five platform tabs (Twitch,
Kick, YouTube, TikTok, Discord) over a shared General card, per-platform
event/display cards, and a per-tab Save button. Also hosts the Moderator
shortcut chip (publishes `NavigateToPageEvent`).

## Source Files
- **XAML:** `src/VyralBot.UI/Views/ChatHubView.xaml`
- **ViewModel:** `src/VyralBot.UI/ViewModels/ChatHubViewModel.cs`
- **Per-platform VMs:** `TwitchChatSettingsViewModel`, `KickChatSettingsViewModel`,
  `YoutubeChatSettingsViewModel`, `TiktokChatSettingsViewModel`, `DiscordChatSettingsViewModel`
- **Quick-settings popups (ChatView):** `Views/{Twitch,Kick,Youtube,Tiktok,Discord}ChatSettingsView.xaml`

## General card (all platforms)
| Control | Persistence |
|---|---|
| Chat Enabled | Platform row `*ChatEnabled` + `PlatformChatRoutingChangedEvent` |
| Auto-Start Chat | Platform row `*ChatAutoStart` |
| Auto-Replies | Config `Chat.Twitch.AutoRepliesEnabled` / `Kick.AutoRepliesEnabled` / `Chat.YouTube.AutoRepliesEnabled` / `Chat.Discord.AutoRepliesEnabled` (all read by `ChatViewModel` master toggles; TikTok gated off) |

## Tab parity matrix (events each platform actually raises)
| Tab | Sections | Backing |
|---|---|---|
| Twitch | Follow, Subscribe, Resub, Gift Sub, Raid, Cheer, Channel Point Redemption | `ChatEventSetting` rows `Twitch/{Follow,Subscribe,Resub,GiftSub,Raid,Cheer,RewardRedeemed}`; sound mirror keys `EventSound_Twitch_*` (`redemption` for rewards) |
| Kick | Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed, Stream Status (show-in-chat only) | rows `Kick/{Follow,Subscribe,GiftSub,Host,KicksGifted,RewardRedeemed,StreamStatus}`; sound keys incl. `kicks_gifted`, `redemption` |
| YouTube | Membership, Super Chat (auto-reply + template only — chat lines always render via the synthetic publisher message) | rows `YouTube/{Subscribe,Cheer}` (shared vocabulary so conditional rules match across platforms) |
| Discord | Member Join, Server Boost (show-in-chat + auto-reply) | rows `Discord/{Follow,Subscribe}` |
| TikTok | Coming-soon gate (whole card disabled); display prefs stored for later | none — no chat pipeline exists |

## Display Options (all five tabs)
`ShowBadges` / `ShowTimestamps` / `CompactMode` checkboxes persisted under
`Chat.Twitch.*`, `Kick.*`, `Chat.YouTube.*`, `Chat.TikTok.*`, `Chat.Discord.*`
config keys; mirrored in each platform's ChatView quick-settings popup.

## Save behavior
Per-tab Save covers every visible control: platform flags, master toggles,
display prefs and all `ChatEventSetting` rows for that platform (YouTube
membership/SuperChat force `ShowInChat = true` — the synthetic chat line from
`YouTubeChatService` always renders, so a false value would be a dead flag).

## Not raised by these platforms (do not add sections)
Twitch stream status is internal bookkeeping (`Stream.StartedAtUtc`), not a
chat event; YouTube has no follow/raid; Discord has no follow/sub/cheer rows.
