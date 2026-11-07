# Quick Start

> Zero to "bot connected and my first command works" in one sitting — the exact path a brand-new Vyral user takes through the current app, with the honest prerequisites at each step. Every step links to the full page for its feature.

## What the first launch actually looks like

There is no onboarding wizard, welcome carousel, or theme prompt on first run. Vyral opens the main window and immediately covers it with the **Select Your Profile** overlay — creating your first profile is the only way forward. The whole first-run experience is: **profile → connect a platform → chat**. Nothing else is forced on you.

## Step 1 — Create your profile

On the **Select Your Profile** overlay:

1. Click **New Profile**.
2. Enter a **Profile Name** (letters, numbers, underscores, hyphens only — it becomes the folder name under `appdata/profiles/`).
3. Optionally set a **Display Name** and a **PIN Code** (4–6 digits — the PIN is asked every time this profile is selected; leave it empty on a private PC).
4. Click **Create** — the profile loads and the overlay drops you on the **Home** dashboard.

Everything the bot will ever know (connections, commands, currency, chat history) lives inside that one profile folder, portable and PIN-lockable. Full story: [Profiles](profiles.md).

> **Optional now or later:** the app ships with a dark theme, but Settings → **Theme** is a full picker — nine built-in themes, a custom accent color builder, and brightness sliders, all applied live with no restart. There is deliberately no first-run theme prompt; nothing needs it.

## Step 2 — Connect your first platform

Open **Platforms** in the sidebar (the Home dashboard's connection cards navigate to the same place). Each card opens that platform's wizard:

| Platform | Setup style | Time | Fine print |
|---|---|---|---|
| **Twitch** (recommended first) | Sign in → register a dev app → authorize | ~5 min | The fullest integration: chat in/out, follows/subs/raids/cheers/**channel points** events, native moderation. No ngrok needed. [Walkthrough](../platforms/twitch.md). |
| **Discord** | Create a bot application, paste its **bot token**, invite it | ~5 min | No OAuth; supports any number of bots. Remember the **Message Content Intent** toggle in the Developer Portal. [Walkthrough](../platforms/discord.md). |
| **Kick** | Sign in → dev app → authorize | ~5 min + ngrok | Chat works immediately; **events (follows/subs) need ngrok** — run the ngrok setup in Settings → API & Webhooks first. [Walkthrough](../platforms/kick.md). |
| **YouTube** | Google Cloud project → OAuth consent → authorize | 10–15 min | Chat in/out **only while you are live** (polling). [Walkthrough](../platforms/youtube.md). |
| TikTok | — | — | Not built — the card says "coming soon" honestly. See [TikTok](../platforms/tiktok.md). |

The wizard walks each step with embedded-browser deep links and copy buttons; every platform's full walkthrough (screens described step by step, plus what can go wrong) is on its platform page above.

## Step 3 — Confirm the bot is live

When the wizard finishes, two things tell you it worked without guessing:

- The **Chat window** (sidebar **Chat**) posts a gold System line — e.g. *"Twitch chat connected — listening to #yourchannel"* — and your viewers' messages start appearing with platform badges and colors.
- The platform's card on the **Platforms** page flips to its connection view (your username, RECONNECT/DISCONNECT), and Twitch/Kick additionally get a sidebar **dashboard** with a live stream preview.

For Discord, pick the channel the bot should talk in first: **Discord → OPEN** on the bot's card → **SET ACTIVE CHANNEL** — until then the bot listens but has nowhere to speak.

## Step 4 — Run your first command

The bot ships with 21 built-in commands ([Command Catalog](../reference/command-catalog.md)) — no setup needed. From your own chat (or any moderator/viewer) type:

```
!points
```

You should get a reply with your currency balance within a second — the same pipeline runs `!leaderboard`, `!gamble`, giveaway entries, `!poll` votes, and `!watchtime`. Prefixes `!`, `?`, and `.` are interchangeable. Replies always go back to the platform the command was typed on.

You can also drive it from inside the app: type in the Chat window's send box and pick the platform under the input.

## Step 5 — Make a command of your own

1. Open **Chat Features → Commands** and click **NEW COMMAND**.
2. The **visual flow editor** opens in a page overlay with a command trigger seeded. Give it a name (say `!discord`), drag in what it should do (a **📨 Send Message** block is enough for plain text — templates like `{username}` work), and press **SAVE**.

That is the whole editing model now: *every* command — built-in or custom — opens in the same editor, and SAVE adopts it. See [Commands](../chat-features/commands.md) and the editor itself in [Automation](../chat-features/automation.md#visual-flow-editor).

## Where to go next

- **Make follows/subs loud** — assign [sounds](../stream-tools/sounds.md) and [visual alerts](../stream-tools/visual-alerts.md) to events; try them safely from [Event Testing](../chat-features/event-testing.md).
- **Borrow commands** — browse the community [Marketplace](../chat-features/marketplace.md) from the Commands page and import a ready-made flow.
- **Link your viewers across platforms** — one `!link` code merges a viewer's Twitch/Kick/YouTube/Discord wallets for bonuses and goals: [Account Linking](../chat-features/account-linking.md).
- **Go deeper** — the honest per-platform capability sheet is [Multi-Platform Parity](../platforms/multi-platform.md); everything else starts at the [Overview](../index.md).

## Status & Known Issues

- This page describes onboarding as the app ships it — deliberately minimal (profile selector → Platforms hub). There is no first-run theme picker, demo wizard prompt, or guided tour; the [Demo Wizard](../home/demo-wizard.md) exists as a separate opt-in tool, not a first-run gate.
- Twitch disconnect deletes its saved credentials (full wizard to reconnect), and YouTube needs a Google Cloud project — both are covered on their platform pages.
