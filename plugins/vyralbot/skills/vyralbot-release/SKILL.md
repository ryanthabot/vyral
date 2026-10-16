---
name: vyralbot-release
description: Produce a full VyralBot release — desktop self-contained Vyral.exe via publish-exe.ps1 plus the VyralDeck APK — placing artifacts in Desktop/Vyral and releases/ without committing binaries. Use when asked for "a new release", "build the exe", "ship it", or updated installable artifacts to test.
---

# VyralBot release procedure

## 0. Preconditions

- **Vyral.exe must be closed** (`tasklist | grep -i vyral`); obs64 running is
  fine and must not be killed.
- Gates first: desktop `dotnet build` + `dotnet test` clean (see the
  `vyralbot-build` skill); mobile `flutter analyze` + `flutter test` clean (see
  the `vyraldeck-mobile` skill). Do not ship red builds.

## 1. Desktop

```powershell
# from the repo root
powershell -ExecutionPolicy Bypass -File publish-exe.ps1
```

The script: publishes self-contained single-file to a staging dir → backs up
`Desktop\Vyral\profiles` to a timestamped zip → copies app files → renames to
`Vyral.exe`. It never deletes user data.

Result: `Desktop\Vyral\Vyral.exe`. Then copy it to `releases\Vyral.exe`.

## 2. Mobile APK

From `VyralBot/mobile/vyral_deck/` (toolchain is NOT on PATH):

```bash
export JAVA_HOME="C:\Program Files\Java\jdk-21"
export ANDROID_HOME="$LOCALAPPDATA/Android/Sdk"
"$LOCALAPPDATA/flutter/bin/flutter.bat" build apk --release
```

Result: `build/app/outputs/flutter-apk/app-release.apk`. Copy as
`releases/VyralDeck-<version>.apk` (version from `pubspec.yaml`) and to
`Desktop\Vyral\` so it sits next to the exe.

## 3. Placement rule (standing decision, 2026-09-24)

- Artifacts go to BOTH `Desktop\Vyral\` and `releases\`.
- **NEVER `git add` them** — the ~236 MB exe and ~51 MB APK would bloat the
  OneDrive-synced git repo. Leave them as untracked/modified working-tree files
  and tell the user; they decide whether to track them.

## 4. Report

- Every artifact path + size.
- Gate results (build/test/analyze) verbatim.
- Which binaries are intentionally uncommitted.
