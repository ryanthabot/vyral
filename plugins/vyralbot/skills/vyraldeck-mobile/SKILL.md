---
name: vyraldeck-mobile
description: Work on the VyralDeck Flutter/Dart mobile app at VyralBot/mobile/vyral_deck — analyze, test, run, or build the APK with this machine's exact toolchain (Flutter at %LOCALAPPDATA%\\flutter, not on PATH). Use for any mobile app change, protocol-client work, or mobile builds.
---

# VyralDeck mobile (Flutter/Dart)

App root: `VyralBot/mobile/vyral_deck/`. LAN-only companion for VyralBot's
StreamDeck (WebSocket + UDP discovery, plain `dart:io`; no cloud, no extra
network packages).

## Toolchain (this machine)

```bash
export JAVA_HOME="C:\Program Files\Java\jdk-21"
export ANDROID_HOME="$LOCALAPPDATA/Android/Sdk"
FLUTTER="$LOCALAPPDATA/flutter/bin/flutter.bat"   # NOT on PATH
```

## Verification gates (run in this order from the app root)

```bash
"$FLUTTER" pub get        # after dependency edits
"$FLUTTER" analyze        # gate: "No issues found!"
"$FLUTTER" test           # gate: all tests pass
"$FLUTTER" build apk --release
# → build/app/outputs/flutter-apk/app-release.apk  (first build downloads Gradle — allow time)
```

## Code map

- `lib/main.dart` — dark-teal Material 3 theme matching the desktop app.
- `lib/core/models.dart` — DeckLayout/DeckPage/DeckButton JSON mirror of the C#
  model (12 slots per page invariant lives in `DeckPageData.fromJson`).
- `lib/core/action_catalog.dart` — local fallback action catalog; the server's
  `welcome.catalog` wins at runtime. Keep in sync with
  `VyralBot/src/VyralBot.UI/Services/VyralDeck/DeckModels.cs`.
- `lib/services/deck_connection.dart` — WebSocket client, typed events
  (`WelcomeEvent`, `ConfigEvent`, `StateEvent`, `PressResultEvent`, …),
  bad-token detection → `ConnectResult.rejected`.
- `lib/services/discovery.dart` — UDP scan on port 5198 (manual IP entry is the
  always-works fallback).
- `lib/services/pairing.dart` — one-time PIN pairing over HTTP POST.
- `lib/screens/` — connect / deck (owns the connection) / tools.
- Protocol contract: `PROTOCOL.md` in the app root; desktop side:
  `VyralBot/src/VyralBot.UI/Services/VyralDeck/DeckServer.cs`.

## Rules

- Changing a protocol message means changing BOTH sides in one commit.
- `DeckScreen` owns `DeckConnection` (connect in `initState`, dispose in
  `dispose`); after `pushReplacement` the previous screen's State is dead —
  late callbacks must use a captured root `Navigator`.
- Keep the dependency list minimal (`shared_preferences` only); networking is
  `dart:io`.
- Version-sensitive widget APIs: verify against `flutter analyze` on the
  installed SDK (3.47.x) before switching (e.g. `initialValue` vs `value` on
  `DropdownButtonFormField`).
- APKs are release artifacts: place in `releases\` + `Desktop\Vyral\`, never commit.
