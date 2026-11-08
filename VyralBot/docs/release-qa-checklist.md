# Release QA Checklist — VyralBot community builds

What the maintainer (or a volunteer tester) runs before shipping a community
build of Vyral.exe + the VyralDeck APK. Work top to bottom; every item lists
exact steps and the expected result. Record failures with the log file
(`logs\vyralbot-*.txt` next to Vyral.exe) and a screenshot.

Prerequisites: Windows 10/11 x64, .NET 10 SDK, a Twitch test account, a Discord
test server you administer, and a phone on the same LAN for VyralDeck.

---

## 1. Build gates

1. From `VyralBot/src/`: `dotnet build VyralBot.sln -c Debug --nologo -v q`
   — **Expected:** 0 errors (warnings tolerable).
2. From `VyralBot/src/`: `dotnet test`
   — **Expected:** all tests pass, **771 passed** (update this number here
   when the suite grows — it is the release baseline).
3. From `VyralBot/mobile/vyral_deck/` (with `JAVA_HOME`/`ANDROID_HOME` set):
   `"$LOCALAPPDATA/flutter/bin/flutter.bat" analyze` then `flutter.bat test`
   — **Expected:** analyze clean (no issues), tests pass.
4. Close Vyral, then from repo root:
   `powershell -ExecutionPolicy Bypass -File publish-exe.ps1`
   — **Expected:** script prints `Profiles backed up to ...\profiles-backup-<timestamp>.zip`
   (when a live profiles folder exists) and ends with
   `Done. Launch it with: <Desktop>\Vyral\Vyral.exe`;
   `Desktop\Vyral\Vyral.exe` exists with a fresh timestamp.

## 2. First-run

1. Launch `Desktop\Vyral\Vyral.exe`.
   — **Expected:** the profile selector opens ("Select Your Profile"); with no
   profiles it offers **Create New Profile**.
2. Create a profile: fill **Profile Name** (letters/numbers/underscore/hyphen
   only), optional Display Name, optional **PIN Code (4-6 digits)**.
   — **Expected:** selector closes, profile is created under
   `appdata/profiles/<name>/` with `data/vyralbot.db`.
3. Home page renders.
   — **Expected:** the Home dashboard shows its cards (no blank page, no
   crash dialog); sidebar sections (Platforms, Chat, Commands, …) all navigate.
4. Check `<Desktop>\Vyral\logs\vyralbot-*.txt`.
   — **Expected:** today's log exists; **no first-chance exceptions** and no
   stack traces during startup/idle (info-level lines only).

## 3. Theme switching (Settings → Theme)

Historically-broken surfaces: Chat, Commands, Flow canvas, Dashboard cards —
check all four after every change.

1. Sidebar → **Settings** → category **Theme** → **Built-in Themes**: click
   three cards, e.g. **Dark**, then **Crimson Forge**, then **Azure Depths**.
   — **Expected:** the whole window (sidebar, cards, inputs, buttons) recolors
   instantly with no restart; selection persists after app restart
   (`appdata/ui-settings.json` → `themeId`).
2. **Custom Theme**: pick a base (Dark/Light), pick an accent (swatch or
   `#RRGGBB`), click **Apply Custom Theme**.
   — **Expected:** accent-derived hover/pressed shades appear; persists across
   restart.
3. Open **Chat**, **Commands** (open a command in the editor), the **flow
   canvas** (Commands → EDIT) and **Home**.
   — **Expected:** all four surfaces adopt the new palette — chat backgrounds/
   badges, Commands grid rows, canvas surface/text colors (node-type accents
   like trigger-amber stay fixed **by design**), dashboard cards. No leftover
   white/teal patches, no unreadable text.

## 4. Platform connect smoke

1. **Twitch:** Platforms → Twitch card → open the setup wizard. Run all 4
   steps end-to-end with the test account: **Step 1: Sign In to Twitch**
   (Sign In), **Step 2: Create Application** (dev console app with OAuth
   redirect `http://localhost:3000`, then save Client ID + Secret), **Step 3:
   Authorize Account** (Get OAuth Access Token → Authorize; after the
   browser's "This site can't be reached" page, paste the FULL callback URL
   back into the wizard), **Step 4: Test Connection** (send the test message).
   — **Expected:** success screen shows the authorized channel; Platforms
   card shows **Connected** with **RECONNECT**/**DISCONNECT** buttons.
2. **Discord:** Integrations → Discord → **Discord Bots** → **ADD BOT** (or
   **RUN FULL WIZARD**), open the bot's detail page → **START**, then on the
   **Welcome & Auto-Role** card set Welcome channel + Welcome message →
   **SAVE WELCOME SETTINGS**.
   — **Expected:** bot goes online in the test server, settings save without
   error; a member join posts the welcome card to the chosen channel.
3. **Kick:** Platforms → Kick wizard: Step 1 sign in, Step 2 create the app
   with redirect `http://localhost:3000/callback`, save the Client ID/Secret,
   authorize, paste the callback URL.
   — **Expected:** Kick shows **Connected**; the Kick settings page
   ("Enable Kick Chat", **SAVE CHANGES**) persists its toggles.
4. **YouTube:** Platforms → YouTube wizard: **SIGN IN WITH GOOGLE** →
   **OPEN DEVELOPER CONSOLE** → create a Web-application credential (use
   **📋 COPY REDIRECT URI**) → **SAVE CLIENT ID** / **SAVE CLIENT SECRET** →
   **AUTHORIZE WITH GOOGLE** → **SEND TEST MESSAGE** → **FINISH SETUP**.
   — **Expected:** wizard completes; YouTube card shows **Connected**.
5. **TikTok:** open the TikTok card.
   — **Expected:** badge reads **Coming Soon** (its setup view shows the same
   notice) and there is no connect wizard offered anywhere for TikTok.

## 5. Chat + commands (two platforms connected, e.g. Twitch + Discord)

1. Type a message in the Chat page input and press **Enter**.
   — **Expected:** the bot's message appears in the platform chat and in the
   Chat page list.
2. From a second (viewer) account, send a message on each connected platform.
   — **Expected:** both messages render in the Chat page within seconds.
3. `!points` (aliases `!balance`, `!currency`) as the viewer.
   — **Expected:** bot replies with the viewer's balance on the **originating**
   platform.
4. Custom command: Commands → **NEW COMMAND** (opens the visual flow editor
   overlay) → build trigger + send_message action → save.
   — **Expected:** typing the command in chat gets the response; the command
   appears in the CUSTOM tab grid.
5. Per-platform response variant: in the flow editor command, fill a
   different per-platform response (Twitch / Kick / YouTube / Discord fields).
   — **Expected:** each platform receives its own variant text.
6. Quotes: as a moderator `!quote add <text>`, then `!quote` and `!quote <n>`
   as a viewer.
   — **Expected:** add confirms; `!quote` returns a quote; `!quote <n>`
   returns that number ("not found (+ total)" on a bad number); the Commands →
   QUOTES tab lists the row.

## 6. Auto-mod

1. Moderator page → Auto-Moderation card → master ON, add a banned word,
   action **Warn**, save.
2. **Test box first:** use the card's sample-message test.
   — **Expected:** verdict shows the rule hit — nothing is executed.
3. Live: the viewer sends a message containing the banned word.
   — **Expected:** warn notice fires and a row lands in the dashboard Action
   Log (actor AutoMod); repeat offenses escalate per the strike ladder.
4. Send the same banned word as a **moderator**.
   — **Expected:** no action — moderators are exempt (broadcaster always
   exempt too).
5. On the Discord test server: trigger a warn-level violation and check the
   parity card.
   — **Expected:** parity card shows **Discord — Full**; Discord enforcement
   actually executes (not just logged).

## 7. Workflows

1. Automation page → **TEMPLATES** (header) → **STARTER TEMPLATES** panel →
   per-row **IMPORT** on the follower-thanks template.
   — **Expected:** workflow **"Template: Follower Thanks"** appears in the
   list, description carries `[imported from template]`, **Enabled = off**
   (the panel says so), and an IMPORTED badge shows on the row.
2. Flip its **Enabled** checkbox on.
3. Event Testing page → set **TestUsername** → fire the Twitch **follow**
   event (mind the safety banner — this runs the real pipeline).
   — **Expected:** `LastFireStatus` green; the workflow sends its thank-you
   message to Twitch chat; **Recent Fires** shows the row; Statistics /
   **Event History** gets a matching row.

## 8. Currency

1. Watchtime: with Twitch chat connected, have the viewer send one message
   (activates them), then wait ~60s.
   — **Expected:** the Watchtime page / viewer record gains a minute — the
   background service increments active viewers every 1 minute.
2. As moderator: `!give @viewer 100`.
   — **Expected:** usage is `!give @username amount`; success message and the
   viewer's `!points` reflects +100. As a non-mod, `!give` is refused.
3. Redemptions: have the viewer redeem a reward (or fire "rewardredeemed" in
   Event Testing) → Currency → **📋 Redemptions** tab → select the row →
   **✅ Approve**, then repeat another redemption and **❌ Reject** /
   **✔️ Complete**.
   — **Expected:** row states update; points are charged/refunded accordingly.
4. Bank cooldown: Currency → **🏦 Bank & Decay** → set **Withdrawal
   Cooldown (minutes)** (e.g. 10) and save → viewer deposits, runs
   `!withdraw <amount>`, then `!withdraw` again immediately.
   — **Expected:** second call replies
   `🏦 Withdrawal on cooldown — try again in ~X min.`

## 9. Alerts / Sounds

1. Events & Alerts → **Visual Alerts** → click **Test Alert** — then click it
   **again immediately while the first alert is still on screen** (queue-stall
   regression).
   — **Expected:** alert 1 plays; alert 2 queues and shows as soon as alert 1
   closes (log: "Alert shown from queue") — the queue never deadlocks.
2. **Sounds Management** → upload a sound (**Upload Sound**) → assign it to an
   event (row **Assign**), set that row's **Volume** slider, hit row **Test**.
   — **Expected:** the per-event sound plays at the chosen volume; the
   **Master Volume** slider scales everything; Assign/Clear round-trips.

## 10. Restream

1. Restream page → **Engine — FFmpeg**: click **Download FFmpeg (~110 MB)**.
   — **Expected:** download from gyan.dev (pinned `ffmpeg-9.0.2-essentials_build.zip`),
   SHA-256 verified, extracted to `appdata/bin/ffmpeg/ffmpeg.exe`; a checksum
   mismatch fails terminally (nothing extracted). (Skipping the download and
   using **Browse…** to a local ffmpeg.exe is an acceptable alternative.)
2. Add **two destinations** (+ Twitch, + YouTube / + Kick / + Custom) with
   valid keys/RTMP URLs.
   — **Expected:** both rows valid (no inline errors, keys never echoed); the
   uplink-budget hint reacts to the combined bitrates.
3. Start the session (point OBS at the ingest per the on-screen instructions),
   watch the live card, then **Stop Restream**.
   — **Expected:** UPTIME / BITRATE / FPS / SPEED / DROPPED populate; both
   destinations show Active; stop cleanly ends FFmpeg (no orphan process).
4. Auto-disable path: sabotage one destination's key, start, let it fail
   twice.
   — **Expected:** the row flips to AutoDisabled and the banner "A destination
   was auto-disabled after repeated failures…" appears with a working
   **Restart & Re-enable** button.

## 11. OBS embed + replay

1. OBS page (embedded host).
   — **Expected:** if OBS isn't installed the page offers **Install OBS
     Studio**; with it installed the obs64 window is embedded ("hosted") in
     the tab, and **Open in own window** / **Put back into Vyral** round-trip.
2. Click **Relaunch**.
   — **Expected:** obs64 is killed and restarted, the window re-embeds, the
   remote-control websocket reconnects; the **Bot Studio** tab keeps working.
3. Bot Studio tab → replay buffer button.
   — **Expected:** idle label **Start Replay Buffer** → click → transitions
     through starting to **Replay Buffer ACTIVE…** with **Save Replay**
     enabled; **Save Replay** writes the clip and shows the result text
     (`ReplaySavedText`), clearing on the next save.

## 12. VyralDeck (mobile)

1. Install the VyralDeck **APK 1.1.0** release build on the phone (same LAN).
2. Desktop: StreamDeck page (Restream group) → **START** the server.
   — **Expected:** status shows the LAN address; a 6-digit pairing PIN is
     displayed (valid 10 minutes, single-use).
3. Phone: app → connect screen (auto-discovery or manual IP) → enter the PIN.
   — **Expected:** pairing succeeds, the deck grid loads with the default
     layout, and the desktop page lists the paired device.
4. Tap a button (e.g. ChatSend).
   — **Expected:** the action fires on the PC (chat message appears) and the
     phone shows the press result.
5. Desktop → **UNPAIR** the device.
   — **Expected:** the phone's WebSocket closes immediately (app drops to
     disconnected); the device disappears from paired devices; its token no
     longer authenticates.

## 13. Profiles

1. Create a second profile (with a PIN), then Settings → Profile → **Switch**
   to it mid-session.
   — **Expected:** PIN prompt for protected profiles; platforms disconnect and
   reconnect for the new profile; currency balances and sound assignments
   follow the selected profile (per-profile database), not the old one.
2. **Export** the profile.
   — **Expected:** `{Documents}\<profile>_backup.zip` is written; the export
   surface carries the DPAPI warning (Twitch tokens are DPAPI-wrapped and
   machine/user-bound — the backup is not portable to another machine's
   decrypted tokens).
3. **Delete** a non-active profile.
   — **Expected:** "Delete Profile?" confirmation warns it cannot be undone;
   after confirm the profile directory is gone. The currently active profile
   cannot be deleted (message, no dialog).

## 14. Stability soak (2 hours)

1. Leave Vyral running idle for 2h with Twitch + Discord connected (no
   streaming).
   — **Expected:** memory in Task Manager stays roughly flat (no monotonic
   climb); reconnects in the log are sporadic, not a reconnect storm; no
   unhandled-exception entries in `logs\vyralbot-*.txt` at the end.
2. Log growth.
   — **Expected:** each rolling log file stays under **25 MB** (info-level
   logging only).

---

Ship only when every item above passes, or each failure is a known, accepted
issue noted in the release notes.
