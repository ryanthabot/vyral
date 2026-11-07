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

### Seam (next wave)

`WorkflowTriggerService` does not call these yet. Wire the delete/disable
paths to `CancelWorkflowRuns(workflow.Id)` so a workflow deleted mid-run
stops instead of running to its step cap. Second part of the seam: nodes
observe `ctx.CancellationToken`, which is get-only on `ExecutionContext` —
registry cancellation stops the interpreter **between** nodes but cannot
interrupt an in-flight node await (`wait_for_response`, `http_request`).
Surfacing the linked token to nodes needs an `ExecutionContext` change
(mutable/linked token) together with the trigger-service call site.
