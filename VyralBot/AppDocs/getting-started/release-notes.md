# What's new in Vyral 1.1.0

> Released 2026-11-07. A big stability, security and feature update — auto-moderation that actually enforces, Channel Points and Super Chats, quotes, Discord welcome messages, and your secrets encrypted on disk. This page distills the user-facing highlights; the full engineering changelog lives in `VyralBot/docs/CHANGELOG.md`.

## New features

- **Auto-moderation that acts, not just warns** — a built-in moderation engine (six rule types, escalation, exemptions) that now enforces on every live platform: Twitch, Kick and YouTube issue real timeouts, bans and message deletions, and Discord moderates natively. Kick needs one re-auth to grant the new moderation permission; YouTube's API has no public ban, so bans become a warning there.
- **Quotes** — `!quote`, `!quote add/del/search/count` on Twitch, Kick, YouTube and Discord (moderator-gated), plus a Quotes page with search and management. See [Quotes](../chat-features/quotes.md).
- **Twitch Channel Points** — custom reward redemptions trigger alerts, stats, auto-replies, chat lines and workflows.
- **YouTube Super Chats** — amounts show in chat cheer-style, fire alerts and sounds, count toward stats and currency, and can start workflows.
- **Discord welcome & auto-role** — greet new members by name (`{user}` variables) and hand them a role automatically.
- **Discord dashboard** — live servers, commands, moderation actions and workflow runs for every bot you run, refreshed automatically.
- **Timers & scheduled messages** — recurring commands (optionally only while chat is active) and per-bot Discord announcements on a schedule with `{time}`/`{date}` variables.
- **Event Testing page** — fire any of 17 test events (follow, sub, raid, Super Chat, redemption…) to try alerts, sounds and workflows before going live. See [Event Testing](../chat-features/event-testing.md).
- **Easier first run** — a dismissible three-step "Get started" strip on Home and eight ready-made workflow templates (each imports disabled so you can edit first). See [Quick Start](quick-start.md) and [Automation](../chat-features/automation.md).
- **Mod & stream tools** — `!so` shoutouts on every platform, plus `!clip`, `!raid`, `!title`, `!game`, `!commercial` and `!followage` on Twitch — driven from chat by you or your mods. See the [Command Catalog](../reference/command-catalog.md#mod--stream-tools-commands).
- **Text-to-speech** — Vyral speaks: alert popups can say "New Follower, Bob", viewers get a `!tts` command (rate-limited, optional mods-only), and the voice/rate/volume are configured on the Alert Settings page. See [Visual Alerts](../stream-tools/visual-alerts.md#text-to-speech-card).
- **Death counter** — a big counter card on the Tools page, the `!death` chat family, and a matching OBS overlay widget — one number, three surfaces. See [Tools Hub](../home/tools-hub.md).
- **Leaderboard overlay** — show your top viewers on stream: an OBS browser-source widget for the points leaderboard, URL built for you on the Currency page. See [Currency & Rewards](../stream-tools/currency.md#leaderboard-overlay).
- **Streaming workflow blocks** — flows can switch OBS scenes, start/stop the OBS stream, save replay-buffer highlights, and clip the Twitch stream. See [Automation](../chat-features/automation.md).
- **VyralDeck status chips** — the phone app now shows now-playing, stream uptime and the death count alongside the live dots. See [StreamDeck & VyralDeck](../stream-tools/vyraldeck.md).

## Improvements

- **Workflows can play sounds** — a new **Play Sound** block plays anything in your Sounds library at a chosen volume; starter templates use it for sub celebrations.
- **Do-not-disturb** — one switch in Settings → Notifications mutes every event sound, alert overlay **and all text-to-speech** instantly (no restart), with a preview button to check your setup. See [Settings Overview](../settings/overview.md).
- **Sounds upgrades** — master-volume slider (covers the browser source too), per-sound preview play, and drag-and-drop import for `.wav`/`.mp3`/`.ogg`/`.m4a`. See [Sounds](../stream-tools/sounds.md).
- **Commands page** — duplicate a command, search, per-platform columns and sortable usage / last-used.
- **CSV export** — Statistics and Event History export the current filtered view to an Excel-friendly file.
- **Restream guardrail** — warns when your destinations' combined bitrate tops 80% of your uplink budget, plus a dropped-frames column per destination. See [Restream](../stream-tools/restream.md).
- **One consistent theme** — every page follows the same dark palette (custom themes apply app-wide), and startup is 1–3 seconds faster.

## Fixes worth knowing

- **Alerts fire every time** — a stuck queue could silently skip them before.
- **Profile switching is clean** — currency, sounds and restream no longer carry over from the previous profile.
- **Long streams no longer eat memory** — chat history and logs are capped, and eight leaks were fixed.
- The Auto-Replies toggle now works on Kick, YouTube, TikTok and Discord, not just Twitch.

## Security & privacy

- **Secrets are encrypted on disk** — login secrets, bot tokens and lights/webhook keys are wrapped with Windows DPAPI (existing files re-encrypt automatically), and stream keys, tokens and API keys are masked in the UI.
- **Crash reports are scrubbed** — if Vyral ever crashes, the report strips tokens and keys before writing anything.
- Deleting a PIN-protected profile now asks for the PIN first.

*Found something that does not match? The [Known Issues ledger](../reference/known-issues.md) tracks every known defect honestly.*
