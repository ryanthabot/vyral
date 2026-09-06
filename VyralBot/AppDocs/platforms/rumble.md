# Rumble

> Rumble support is planned but not implemented yet. The Platforms page shows an informational **Rumble** card that explains this, and account linking is already compatible with Rumble — so viewer accounts can be linked ahead of the integration.

## Overview

VyralBot does not connect to Rumble today. The **Rumble** card on the **Platforms** page is a placeholder: clicking it (here or in Settings) sets a status message — "Rumble support is coming soon — account linking is already compatible." There is no setup wizard, no stored credentials, and no chat connection for Rumble yet.

In the startup list under **Settings → Platforms**, Rumble appears as **Not available yet** with no connect/disconnect controls, mirroring the TikTok placeholder.

## What is planned

- **Chat:** reading and posting in Rumble live chat (Rumble offers an IRC-style chat API plus a pusher-based live chat feed) once the integration lands.
- **Account linking:** the `!link` system already accepts `"Rumble"` as a platform identity, so viewers can link their Rumble account now and the link will light up automatically once chat support ships.

## Status

- Platforms page: informational card only ("Coming Soon"), always shown as **Not Connected**.
- Settings → Platforms: **Not available yet** — no startup or connection toggles.
- No `Platform` database row is created for Rumble; nothing to configure until the integration ships.

## Related

- [Platforms](overview.md) — the other platform integrations.
- [Kick](kick.md) — an example of a fully implemented platform integration.
