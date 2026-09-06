# Facebook Gaming

> Facebook Gaming support is planned but not implemented yet. The Platforms page shows an informational **Facebook** card that explains this, and account linking is already compatible with Facebook — so viewer accounts can be linked ahead of the integration.

## Overview

VyralBot does not connect to Facebook Gaming today. The **Facebook** card on the **Platforms** page is a placeholder: clicking it (here or in Settings) sets a status message — "Facebook Gaming support is coming soon — account linking is already compatible." There is no setup wizard, no stored credentials, and no chat connection for Facebook yet.

In the startup list under **Settings → Platforms**, Facebook appears as **Not available yet** with no connect/disconnect controls, mirroring the TikTok placeholder.

## What is planned

- **Chat:** reading and posting in Facebook Gaming live chat once the integration lands, feeding the same event pipeline (chat messages, follows, stream status) the other platforms use.
- **Account linking:** the `!link` system already accepts `"Facebook"` as a platform identity, so viewers can link their Facebook account now and the link will light up automatically once chat support ships.

## Status

- Platforms page: informational card only ("Coming Soon"), always shown as **Not Connected**.
- Settings → Platforms: **Not available yet** — no startup or connection toggles.
- No `Platform` database row is created for Facebook; nothing to configure until the integration ships.

## Related

- [Platforms](overview.md) — the other platform integrations.
- [Kick](kick.md) — an example of a fully implemented platform integration.
