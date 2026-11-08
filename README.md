# VyralBot

VyralBot is a free Windows app that runs your whole stream from one window:
a multi-platform chat bot, alerts and overlays, an embedded OBS, multi-platform
restreaming, currency, giveaways, auto-moderation, a visual workflow editor
and a mobile StreamDeck companion — all in a single process, no subscriptions.

## Platforms

| Platform | Chat bot | Events / alerts | Notes |
|---|---|---|---|
| Twitch | Yes | Yes | IRC chat, Helix moderation, Channel Points redemptions |
| Kick | Yes | Yes | Webhooks with replay protection |
| YouTube | Yes | Yes | Live chat, Super Chats, memberships |
| Discord | Yes | Yes | Multi-bot, welcome messages, auto-role, dashboard |
| TikTok | Coming soon | Coming soon | Shown as "Coming Soon" in the app — not yet usable |

## Requirements

- Windows 10 or 11, 64-bit
- That's it — the release build is self-contained (no .NET install needed).
  If the Microsoft WebView2 runtime is missing, VyralBot offers to install it
  on first launch (~2 MB, from Microsoft).

## Download and run

1. Get `Vyral.exe` from the project's release channel. It is a single
   self-contained file — no installer.
2. Double-click it. On first launch you create a **profile** (optionally
   protected with a PIN). A profile holds your platform connections, settings,
   currency and workflows — you can keep separate profiles per channel.
3. Open **Platforms**, connect Twitch / Kick / YouTube / Discord, and you are
   live.

Your data (profiles, database, logs, sounds) lives in
`%LOCALAPPDATA%\VyralBot` — safe across app updates, never next to the exe.

## Building from source

```powershell
# build (from repo root)
dotnet build VyralBot/src/VyralBot.sln -c Debug

# run the test suite
dotnet test VyralBot/src/VyralBot.sln

# produce a self-contained Desktop\Vyral\Vyral.exe (close Vyral first)
powershell -ExecutionPolicy Bypass -File publish-exe.ps1
```

Requires the .NET 10 SDK (Windows, x64). The VyralDeck mobile companion is
built separately with Flutter from `VyralBot/mobile/vyral_deck/`.

## Documentation

- Full docs: [`VyralBot/docs/`](VyralBot/docs/README.md) — architecture,
  services, per-feature guides.
- In-app docs: open **Documentation** in the app's sidebar for guides on
  every feature.
- Release notes: [`VyralBot/docs/CHANGELOG.md`](VyralBot/docs/CHANGELOG.md).
- Community command marketplace: shared flow-editor commands are published to
  the public GitHub store [`ryanthabot/vyral`](https://github.com/ryanthabot/vyral)
  and can be imported by sharecode from the app's Marketplace tab.

## License

GPL-2.0 — see [LICENSE](LICENSE).
