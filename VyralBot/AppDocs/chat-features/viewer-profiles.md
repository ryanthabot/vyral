# Viewer Profiles

> Click any username in the chat window and Vyral opens a full viewer profile — identity, linked accounts (with per-account unlink), sub/follow status, points and economy, watchtime and streaks, completed goals, giveaway entries, active perks, and recent events — in one scrollable window. For linked viewers it is the whole cross-platform person; for everyone else it is the one account you clicked.

## Overview

Every chat message's username is now a clickable button (look for the hand cursor and the "View viewer profile" tooltip). Clicking it opens the **viewer profile window** for that `(username, platform)` pair. If the viewer has [linked accounts](account-linking.md), the window resolves their internal viewer identity and aggregates **every** linked account — so clicking `SameName` on Twitch shows the same person's Kick balance, sub status, watchtime streak, and goal completions too.

The window is host-facing: it is a moderation and support tool first. Its most load-bearing job is **removing accounts from a link group when the viewer cannot or will not do it themselves** — the canonical case being a banned user (see [Unlinking a banned user](#unlinking-a-banned-user)).

The same per-account unlink buttons also live in **Settings → Viewer Hub → Linked Viewers**, so you can manage link groups without having someone in chat to click on.

## User Guide

### Opening a profile

- **From chat** — click the username on any message in the [chat window](../chat/chat-window.md). The window opens on top of the main app; clicking the same username again while its window is already open brings the existing window to the front instead of stacking a duplicate.
- **From Viewer Hub** — Settings → Viewer Hub → **Linked Viewers** lists every link group; each entry opens the same profile view (see [Settings overview](../settings/overview.md)).

The window is titled `Viewer — {username} ({platform})`, resizable, and everything below sits in scrollable sections so nothing depends on window height.

### What each section shows

| Section | What you see | Why it matters |
|---|---|---|
| **Identity** | The viewer's internal ID and every linked account, one row per `(username, platform)`, with the account you clicked marked as current. A viewer with no links shows just the raw account and a "not linked" note. | Tells you instantly whether you are looking at one platform or a whole [link group](account-linking.md). |
| **Linked accounts** | Per-account rows, each with its own **Unlink** button ("Remove this account from the link group"). | The banned-user override — removes one account from the group without a `!delink` code. |
| **Sub / follow status** | Per-account follow state, sub tier, sub months, and last-seen from the platform status rows. | Feeds giveaway [odds boosts](../stream-tools/giveaways.md#odds-boosts) and [Vyral Goals](../stream-tools/vyral-goals.md); shows you *why* someone has a perk. |
| **Points & economy** | Per-account balance, total earned, total spent, plus the most recent [currency](../stream-tools/currency.md) transactions. | Support questions ("where did my points go?") get answered without opening the database. |
| **Watchtime & streaks** | Total minutes, per-stream session minutes, first/last seen, active flag, and [viewer streaks](../stream-tools/watchtime.md). | Distinguishes lurkers from regulars at a glance. |
| **Goals completed** | Every [Vyral Goal](../stream-tools/vyral-goals.md) this identity has completed, with reward. | Verify a claimed reward before re-issuing anything. |
| **Giveaway entries** | [Giveaway](../stream-tools/giveaways.md) entries for each account, including winner flags. | Catch multi-account entry attempts or confirm a past win. |
| **Perks** | Active perks per account (badges, multipliers, bonus modes). | See everything the identity currently benefits from in one list. |
| **Recent events** | Event statistics and highlights for the username (total events, notable types). | Recent history without digging through the [event log](event-history.md). |

For linked viewers the sections aggregate across the whole link group — economy, watchtime, goals, and giveaways are shown per linked account so nothing gets silently summed in a way you cannot audit.

### Unlinking a banned user

The [`!delink`](account-linking.md#removing-an-account-the-delink-flow) chat flow is deliberately two-sided: the account being removed has to confirm with a code. A banned user will not cooperate, so the streamer path skips the code entirely:

1. Open the banned user's profile — click their username in chat (or find the account in Settings → Viewer Hub → Linked Viewers).
2. In **Linked accounts**, find the specific account (the row is labeled with the username and platform) and click its **Unlink** button.
3. The account is removed from the link group immediately — no confirmation code, no typing from the banned account. The rest of the group (and its aggregates) keeps working.

This is per-account, not per-group: unlinking the banned Twitch account leaves the viewer's Kick account linked. If you want the whole person gone from a group, unlink each account row.

> Unlink removes the account from the *link group* — it is not a ban, a points wipe, or a platform disconnect. The account can re-link later via the normal [`!link`](account-linking.md) flow.

## How It Works (Developer)

- `ViewerProfileService.GetProfileAsync(username, platform)` resolves the `ViewerId` via `ILinkedAccountService`, then assembles an immutable `ViewerProfileSnapshot` of plain entity rows: `LinkedAccount` rows (flagged `IsCurrent`), `ViewerPlatformStatus` (sub/follow), `UserCurrency` + transactions, `UserWatchtime` + `ViewerStreak`, goal completions joined with goal names, giveaway entries with giveaway names/winner flags, active perks, and event statistics. A `null` viewer ID yields a valid "not linked" snapshot rather than an error, so unlinked chatters still get a (thin) profile.
- `ViewerProfileViewModel.InitializeAsync(username, platform)` loads the snapshot on a background thread and marshals results to the UI dispatcher; sections are `[ObservableProperty]` collections the window binds directly. The per-row unlink command calls `ILinkCodeService.UnlinkAsync(username, platform)` and refreshes the snapshot.
- `ViewerProfileWindow` is launched by `ChatViewModel`'s `OpenViewerProfileCommand` (wired to the username buttons in `ChatView.xaml`), resolving the VM/view pair through the app's DI container. Both the click wiring and the window follow the app's existing patterns (DynamicResource theme brushes, dispatcher marshaling, owner-set dialog).

## Files & Code

| File | Role |
|---|---|
| [ViewerProfileService.cs](../../src/VyralBot.Core/Services/Linking/ViewerProfileService.cs) | Aggregates the `ViewerProfileSnapshot` from the per-profile database. |
| [IViewerProfileService.cs](../../src/VyralBot.Core/Services/Linking/IViewerProfileService.cs) | Service contract (`GetProfileAsync`). |
| [ViewerProfileViewModel.cs](../../src/VyralBot.UI/ViewModels/ViewerProfileViewModel.cs) | Section state, background load + dispatcher marshaling, per-account unlink command. |
| [ViewerProfileWindow.xaml](../../src/VyralBot.UI/Views/ViewerProfileWindow.xaml) | The scrollable card-based window (theme-brush driven, resizable). |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Username click → `OpenViewerProfileCommand` → window (with duplicate-window guard). |
| [ViewerHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ViewerHubViewModel.cs) | Linked Viewers section with per-account unlink buttons (Settings → Viewer Hub). |
| [LinkCodeService.cs](../../src/VyralBot.Core/Services/Linking/LinkCodeService.cs) | `UnlinkAsync` — the actual per-account removal shared by both UIs. |

## Gotchas / Status & Known Issues

- **Unlink is permanent for the group membership** — there is no undo button; the viewer re-links with a fresh `!link` code if it was a mistake. The account's points/watchtime rows are *not* deleted by an unlink.
- **The streamer unlink bypasses the two-sided check on purpose** — the [`!delink`](account-linking.md#removing-an-account-the-delink-flow) chat flow exists to stop viewers unilaterally removing *other* people's accounts; the host UI is the trusted override for exactly that case (bans, disputes, cleanup).
- **Profile data is per-profile** — everything shown comes from the currently loaded profile's database (see [Profiles](../getting-started/profiles.md)); switching profiles switches the whole viewer universe.
- **Platforms without a live integration** — a TikTok/Facebook/Rumble account inside a link group shows up as a linked row and can be unlinked like any other, even though it never sent a chat message to click on (reach those via Viewer Hub).
