# WorkflowRuntime (graph interpreter + run registry)

`src/VyralBot.Workflows/Runtime/WorkflowRuntime.cs` — DI **singleton**
(`App.xaml.cs`), interprets a `Workflow` graph from its entry node with
Next/Branch routing, native loop interpretation, error branches, the
workflow-level Error Handler chain, a hard step budget (`MaxSteps` 200) and
fire-and-forget safety (`ExecuteAsync` never throws; results persist to the
WorkflowExecutions history table).

## Per-workflow run cancellation (2026-11)

`ExecuteAsync` registers each run in an internal registry
(`_activeRuns`: workflow id → run key → linked `CancellationTokenSource`).
The linked token carries the caller's `ctx.CancellationToken` **plus** the
run's own cancel signal, and is threaded through the interpreter
(`RunChainAsync`/`RunLoopAsync` check it between nodes; a cancelled run ends
with Status `"Cancelled"`). The run unregisters in a `finally`.

Public API for hosts (the runtime is a singleton, so every caller shares the
registry):

| Member | Purpose |
|---|---|
| `CancelWorkflowRuns(workflowId)` | Signals every in-flight run of one workflow; returns the count signalled |
| `HasActiveRuns(workflowId)` | True while at least one run is in flight |
| `ActiveRunCount` | Total in-flight runs (diagnostics) |

### Delete/disable wiring (2026-11-07)

`WorkflowTriggerService.LoadWorkflowsAsync` snapshots the cached workflow ids
before each reload and calls `CancelWorkflowRuns(id)` for every id that
vanished after the reload. All delete paths (automation page, flow editor side
panel, component orphan sweep, commands page) and the enable/disable toggle
call `ReloadAsync` right after the row changes, so a workflow deleted or
disabled mid-run stops at its next between-node check and its history row
records Status `"Cancelled"`. Remaining seam: nodes observe
`ctx.CancellationToken`, which is get-only on `ExecutionContext` — registry
cancellation stops the interpreter **between** nodes but cannot interrupt an
in-flight node await (`wait_for_response`, `http_request`). Surfacing the
linked token to nodes needs an `ExecutionContext` change (mutable/linked
token).

## Stream-control blocks + IStreamControlWorkflowBridge (2026-11-07)

Four "Streaming" section blocks (`Nodes/BuiltIn/StreamControlNodes.cs`,
catalog entries in `BlockCatalog`) let chat/workflow triggers control the
stream: `obs_scene` (param `SceneName`, variable-substituted), `obs_stream`
(param `Action` = Start/Stop), `obs_replay` (no params) and `twitch_clip`
(no params).

The Workflows engine keeps its no-Integration-dependencies rule
(`VyralBot.Workflows.csproj` references Core + Data only), so these nodes use
the same seam as `IDiscordWorkflowBridge`: they resolve
`Abstractions.IStreamControlWorkflowBridge` (new file
`Abstractions/StreamControlWorkflowBridge.cs`) from `ctx.Services` and fail
the node — never throw — when it is not registered. The **UI layer** must
register an implementation adapting over the OBS remote bridge
(`UI/Services/VyralDeck/ObsRemoteBridge` / obs-websocket client — scene,
stream start/stop, SaveReplayBuffer) and the Twitch Helix clip call
(`IStreamActionsService.TwitchCreateClipAsync` / `ITwitchService.CreateClipAsync`).

Failure semantics (all four): bridge outcomes map `Ok=false` onto
`NodeResult.Fail(message)` — routable through the node's error branch like
`play_sound`; helpful messages for OBS offline / remote control disabled /
replay buffer not running / Twitch disconnected or offline. Run-level
cancellation is rethrown (WaitNode pattern) so the run records `"Cancelled"`.
Both success and failure info-log through `ctx.Logger`.
