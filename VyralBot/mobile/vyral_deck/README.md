# VyralDeck — Mobile Companion (Flutter/Dart)

Companion app for VyralBot's **StreamDeck** page. Press your stream deck buttons
and edit them from your phone — iOS and Android — over **your local Wi-Fi only**
(no cloud, no accounts, nothing leaves your network).

## What it does

- **Connect** to your PC running VyralBot (StreamDeck → Mobile Companion Server):
  UDP scan for servers on the network, or manual `IP:port` entry.
- **Pair** once with the 6-digit PIN shown on the PC. The pairing token is stored
  on the phone; afterwards you connect with one tap.
- **Deck tab**: your pages and buttons exactly as configured on the PC. Tap a
  button to fire it (OBS scene switch, Twitch clip, chat message, sounds,
  workflows, lights, …). Live indicators for Twitch/Kick/YouTube update in real time.
- **Edit mode**: tap a button to customize label, emoji icon, color and action
  (with per-action parameters). Changes save to the PC and sync to every
  connected device instantly.
- **Tools tab**: one-tap tools (clip, marker, clear chat, emote-only/shield
  toggles, lights) and a chat-send box for Twitch/Kick/YouTube.

## Building

The `android/` and `ios/` platform folders are generated on first setup (they
are machine-generated and not committed):

```bash
cd mobile/vyral_deck
flutter create . --platforms=ios,android --org com.vyralbot --project-name vyral_deck
flutter pub get
flutter run          # device/emulator on the same Wi-Fi as the PC
```

Requirements: Flutter 3.22+ (Dart 3.4+). No other plugins beyond
`shared_preferences` — networking is plain `dart:io` (TCP WebSocket + UDP broadcast).

## Desktop side

In VyralBot: **Restream → StreamDeck → Mobile Companion Server → START**, note
the address (`e.g. 192.168.1.20:5199`) and the pairing PIN. Allow VyralBot
through the Windows firewall when prompted (private networks).

## Protocol

See [PROTOCOL.md](PROTOCOL.md) — JSON over an authenticated WebSocket.
