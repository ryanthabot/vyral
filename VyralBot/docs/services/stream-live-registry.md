# Stream Live Registry

## Overview
Singleton registry of per-platform live status, and (since 1.1.0) the source
of the **stream live/offline transition events** that drive
`streamlive`/`streamoffline` workflow triggers. Lives in
`src/VyralBot.Integrations/StreamStatus/StreamLiveRegistry.cs` because only
Integrations can reference the platform event types; Core owns the
`IStreamLiveRegistry` contract that gates/UI consume.

## Inputs (DI ctor, EventBus subscriptions)
- `TwitchStreamStatusEvent.IsLive` → `SetLive("Twitch", IsLive)`
- `KickStreamStatusEvent.IsLive` → `SetLive("Kick", IsLive)`
- `YouTubeLiveChatStateEvent.State == "Listening"` → `SetLive("YouTube", …)`
  (the only state polling an active live chat; Connecting/WaitingForBroadcast/
  ReauthRequired are not live)

Discord/TikTok are never registered — `IsLive` is always false there. A
parameterless ctor exists for unit tests only (no bus subscriptions).

## Outputs
- `IsLive(platform)` — case-insensitive lookup; false for blank/unknown.
- `StreamLiveChanged` .NET event (platform string) — legacy seam.
- **`StreamLiveChangedEvent` on the EventBus** (`Core/Services/EventBus/Events/StreamStatusEvents.cs`,
  fields `Platform` + `IsLive`) — published fire-and-forget on **actual
  transitions only**: platforms start not-live, so a first `SetLive(false)`
  is a no-op and repeat pings never re-fire.

## Consumers
- `WorkflowTriggerService` subscribes `StreamLiveChangedEvent` and dispatches
  the canonical key `streamlive` (IsLive=true) / `streamoffline`
  (IsLive=false). `{username}` is seeded with the platform's channel name
  (falls back to the platform name), plus `{platform}` and `{isLive}`
  variables. Authored aliases: streamstart/online/golive → `streamlive`;
  streamend/offline → `streamoffline`.
- `TestEventPublisher` ids `streamlive.fire` / `streamoffline.fire` publish
  the same event directly (registry bypassed) for Event Testing.

## Lifecycle
`UnsubscribeAll()` releases every bus subscription (idempotent; host
shutdown). Tests: `Wave17StreamLiveTriggerTests` (transitions-only publish,
dispatch key by IsLive) and `Wave17bStreamLiveTriggerTests` (platform-event
wiring, YouTube Listening mapping, IsLive query semantics, end-to-end
once-per-transition, channel seeding).
