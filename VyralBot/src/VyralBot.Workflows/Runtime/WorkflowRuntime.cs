using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Data;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Runtime;

/// <summary>Timing of one executed node.</summary>
public readonly record struct NodeTiming(string NodeId, string NodeType, long DurationMs);

/// <summary>Outcome of a single workflow run.</summary>
public class WorkflowExecutionResult
{
    /// <summary>"Completed", "Failed" or "Cancelled".</summary>
    public string Status { get; set; } = "Failed";

    /// <summary>Visited node types/IDs in execution order ("type:id" entries).</summary>
    public List<string> Path { get; } = new();

    /// <summary>Per-node wall-clock timings in execution order.</summary>
    public List<NodeTiming> NodeTimings { get; } = new();

    /// <summary>Total run duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Error message when <see cref="Status"/> is "Failed".</summary>
    public string? Error { get; set; }

    public bool IsCompleted => string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Graph interpreter for workflows: executes the node list starting at the entry
/// node (the workflow's explicit <see cref="Workflow.EntryNodeId"/> when set and
/// present, else the first node no other node routes to), routing via Next /
/// BranchTrue-BranchFalse (condition nodes output "true"/"false"), Branches
/// ("switch" nodes, plus multi-rule "comparison" nodes outputting
/// "case:{i}"/"else" — the editor wires those to pass-through attachment cards)
/// and BranchError (failed nodes). A failing node with no error
/// branch of its own hands over to the workflow's Error Handler chain
/// (<see cref="Workflow.ErrorHandlerEntryId"/>) as a reaction — the run still
/// ends with the original failure. "loop" nodes are interpreted natively
/// (bounded iteration count; a "stop_loop" node breaks out) before generic
/// routing. A hard step limit guards against cycles. Fire-and-forget safe:
/// <see cref="ExecuteAsync"/> never throws — failures are reported through the
/// result and logged.
/// </summary>
public class WorkflowRuntime
{
    /// <summary>Hard cap on executed nodes per run; guards against cycles and runaway graphs.</summary>
    public const int MaxSteps = 200;

    /// <summary>Hard cap on iterations per "loop" node; body nodes also count toward <see cref="MaxSteps"/>.</summary>
    public const int MaxLoopIterations = 25;

    /// <summary>Variable a "stop_loop" node sets to request breaking out of the active loop.</summary>
    public const string StopLoopVariable = "__stop_loop";

    /// <summary>
    /// Output prefix a multi-rule "comparison" node emits for its matching rule's
    /// case key ("case:0", "case:1", ...) — routed through the node's
    /// Branches["case:{i}"] entry (the editor wires those to "comparison_case"
    /// attachment cards).
    /// </summary>
    public const string CaseOutputPrefix = "case:";

    /// <summary>
    /// Output a multi-rule "comparison" node emits when no rule matched — routed
    /// through Branches["else"] (the "comparison_else" attachment card) with a
    /// BranchFalse/Next fallback.
    /// </summary>
    public const string ElseOutputValue = "else";

    private readonly NodeRegistry _registry;
    private readonly ILogger<WorkflowRuntime> _logger;

    /// <summary>
    /// ACTIVE-RUN REGISTRY (per-workflow cancellation): workflow id → the
    /// linked CTS of every in-flight <see cref="ExecuteAsync"/> run. A run
    /// registers after its entry node resolves and unregisters in its
    /// <c>finally</c>; <see cref="CancelWorkflowRuns"/> flips every CTS of one
    /// workflow so the interpreter stops at the next between-node check (the
    /// run ends as "Cancelled"). The runtime is a DI SINGLETON, so the registry
    /// is shared with every caller (WorkflowTriggerService, manual runs).
    /// WorkflowTriggerService.LoadWorkflowsAsync cancels vanished ids on every
    /// cache reload (delete/disable paths). SEAM: nodes observe
    /// <c>ctx.CancellationToken</c>, which is get-only on <see cref="ExecutionContext"/> —
    /// registry cancellation stops the interpreter BETWEEN nodes but cannot
    /// interrupt an in-flight node await (wait/http); surfacing the linked
    /// token to nodes needs an ExecutionContext change.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, CancellationTokenSource>> _activeRuns = new(StringComparer.Ordinal);

    /// <summary>
    /// Platform send hook installed by the UI layer at startup (the UI owns the
    /// platform senders; the engine has no platform dependencies). Receives the
    /// execution context and the fully substituted message text; returns true when
    /// delivered. When null, "send_message" nodes fail.
    /// </summary>
    public Func<ExecutionContext, string, Task<bool>>? MessageSender { get; set; }

    public WorkflowRuntime(NodeRegistry registry, ILogger<WorkflowRuntime>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? NullLogger<WorkflowRuntime>.Instance;
    }

    /// <summary>
    /// Cancels every in-flight run of one workflow (e.g. the workflow is being
    /// deleted or disabled mid-run). Each registered run's linked token flips,
    /// so its interpreter stops at the next between-node check and the run
    /// ends with Status "Cancelled". Returns the number of runs signalled
    /// (0 when the workflow is not currently running). Called by
    /// WorkflowTriggerService.LoadWorkflowsAsync for ids that vanished across
    /// a cache reload (workflow deleted or disabled); in-node awaits still
    /// need the ExecutionContext token change noted on <see cref="_activeRuns"/>.
    /// </summary>
    public int CancelWorkflowRuns(string? workflowId)
    {
        if (string.IsNullOrEmpty(workflowId) ||
            !_activeRuns.TryGetValue(workflowId, out var runs) ||
            runs.IsEmpty)
        {
            return 0;
        }

        int signalled = 0;
        foreach (var pair in runs)
        {
            try
            {
                pair.Value.Cancel();
                signalled++;
            }
            catch (ObjectDisposedException)
            {
                // The run finished concurrently with this call — nothing to signal.
            }
        }

        _logger.LogInformation("Cancellation requested for {Count} active run(s) of workflow {WorkflowId}", signalled, workflowId);
        return signalled;
    }

    /// <summary>True when at least one run of the workflow is currently in flight.</summary>
    public bool HasActiveRuns(string? workflowId) =>
        !string.IsNullOrEmpty(workflowId) &&
        _activeRuns.TryGetValue(workflowId, out var runs) &&
        !runs.IsEmpty;

    /// <summary>Total number of in-flight runs across all workflows (diagnostics).</summary>
    public int ActiveRunCount
    {
        get
        {
            var total = 0;
            foreach (var pair in _activeRuns)
                total += pair.Value.Count;
            return total;
        }
    }

    /// <summary>
    /// Executes a workflow. Never throws; all exceptions become a "Failed" result.
    /// A WorkflowExecutions history row is persisted when an IDataService is
    /// reachable from <paramref name="ctx"/>.Services.
    /// </summary>
    public async Task<WorkflowExecutionResult> ExecuteAsync(Workflow workflow, ExecutionContext ctx)
    {
        if (ctx is null)
            return new WorkflowExecutionResult { Status = "Failed", Error = "Execution context was null" };

        var result = new WorkflowExecutionResult();
        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTime.UtcNow;
        var workflowName = "(unknown)";

        try
        {
            if (workflow is null)
            {
                result.Error = "Workflow was null";
                return result;
            }

            workflowName = workflow.Name;
            ctx.WorkflowId = workflow.Id;
            _logger.LogInformation("Workflow '{WorkflowName}' ({WorkflowId}) starting, {NodeCount} nodes",
                workflow.Name, workflow.Id, workflow.Nodes?.Count ?? 0);

            if (workflow.Nodes is null || workflow.Nodes.Count == 0)
            {
                result.Error = "Workflow has no nodes";
                return result;
            }

            // Node lookup by ID (first occurrence wins on duplicates).
            var nodesById = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
            foreach (var node in workflow.Nodes)
            {
                if (node is not null && !string.IsNullOrEmpty(node.Id))
                    nodesById.TryAdd(node.Id, node);
            }

            // Entry node: the explicit trigger-wire target when valid, else detection.
            var current = ResolveEntryNode(workflow, nodesById);
            if (current is null)
            {
                result.Error = "Workflow has no valid nodes";
                return result;
            }

            var budget = new StepBudget();

            // Register the run for per-workflow cancellation (CancelWorkflowRuns):
            // the linked token carries the caller's ctx token AND this run's own
            // cancel signal; the interpreter checks it between nodes. Deliberately
            // NOT disposed in finally — a concurrent CancelWorkflowRuns could hit a
            // disposed CTS, and a token-less CTS holds no timer resources.
            var runCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            var runKey = Guid.NewGuid();
            var workflowRuns = _activeRuns.GetOrAdd(workflow.Id, _ => new ConcurrentDictionary<Guid, CancellationTokenSource>());
            workflowRuns[runKey] = runCts;

            ChainOutcome outcome;
            try
            {
                outcome = await RunChainAsync(
                    current, nodesById, workflow, ctx, result, budget,
                    runCts.Token, allowGlobalErrorHandler: true).ConfigureAwait(false);
            }
            finally
            {
                workflowRuns.TryRemove(runKey, out _);
                if (workflowRuns.IsEmpty)
                    _activeRuns.TryRemove(workflow.Id, out _);
            }

            // A chain that simply walked off the graph completes the run; every
            // other outcome (failure, cancellation, dangling reference) keeps the
            // status/error the chain recorded.
            if (outcome == ChainOutcome.ChainEnded && result.Error is null)
                result.Status = "Completed";

            return result;
        }
        catch (Exception ex)
        {
            // Fire-and-forget safety: never let a workflow execution throw.
            _logger.LogError(ex, "Workflow '{WorkflowName}' execution crashed", workflowName);
            result.Status = "Failed";
            result.Error = ex.Message;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            result.DurationMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation("Workflow '{WorkflowName}' {Status} in {DurationMs}ms ({Steps} steps): {Path}",
                workflowName, result.Status, result.DurationMs, result.Path.Count, string.Join(" -> ", result.Path));

            // Persist execution history (best effort; never fails the caller).
            try
            {
                var dataService = ctx.Services.GetService<IDataService>();
                if (dataService is not null)
                {
                    await dataService.SaveWorkflowExecutionAsync(new WorkflowExecution
                    {
                        Id = ctx.ExecutionId,
                        WorkflowId = workflow?.Id,
                        WorkflowName = workflowName,
                        TriggerSummary = SummarizeTrigger(workflow?.Trigger),
                        Status = result.Status,
                        StartedAt = startedAt,
                        CompletedAt = DateTime.UtcNow,
                        DurationMs = result.DurationMs,
                        NodePath = string.Join(" -> ", result.Path),
                        Error = result.Error
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist execution history for workflow '{WorkflowName}'", workflowName);
            }
        }
    }

    /// <summary>Mutable step counter shared by the main chain, loop bodies and the error handler chain.</summary>
    private sealed class StepBudget
    {
        public int Steps;
    }

    /// <summary>How a <see cref="RunChainAsync"/> walk ended.</summary>
    private enum ChainOutcome
    {
        /// <summary>The chain finished: walked off the graph, reached its stop node, or ended benignly in a non-fatal context.</summary>
        ChainEnded,

        /// <summary>A "stop_loop" request was observed inside a loop body (the flag was consumed).</summary>
        StopLoopRequested,

        /// <summary>The run was cancelled (result.Status is "Cancelled").</summary>
        Cancelled,

        /// <summary>Fatal failure in a main-flow context (result.Error carries the message).</summary>
        RunFailed
    }

    /// <summary>
    /// Walks a node chain starting at <paramref name="current"/>, executing every
    /// node with the shared semantics: step budget, cancellation, error routing
    /// via BranchError, output-driven routing (conditions, switch, Next) and
    /// native "loop" interpretation. Used for the main flow, loop bodies and the
    /// Error Handler chain alike.
    /// </summary>
    /// <param name="allowGlobalErrorHandler">
    /// True for main-flow chains (and loop bodies within them): a failing node
    /// with no error branch runs the workflow's Error Handler chain (when
    /// configured) before the run fails. False inside the error handler chain
    /// itself: a failing chain node just ends the chain, and the local flag is
    /// what keeps the handler from recursing into itself.
    /// </param>
    /// <param name="stopAtNodeId">When set, reaching this node id ends the walk (a loop body routing back into its loop node).</param>
    private async Task<ChainOutcome> RunChainAsync(
        WorkflowNode? current,
        Dictionary<string, WorkflowNode> nodesById,
        Workflow workflow,
        ExecutionContext ctx,
        WorkflowExecutionResult result,
        StepBudget budget,
        CancellationToken runToken,
        bool allowGlobalErrorHandler,
        string? stopAtNodeId = null)
    {
        while (current is not null)
        {
            // A loop body ends when it routes back into the loop node itself.
            if (stopAtNodeId is not null && string.Equals(current.Id, stopAtNodeId, StringComparison.Ordinal))
                return ChainOutcome.ChainEnded;

            // runToken is the run's linked token: the caller's ctx token (app
            // shutdown) PLUS this run's per-workflow cancel signal.
            if (runToken.IsCancellationRequested)
            {
                result.Status = "Cancelled";
                result.Error = "Execution cancelled";
                return ChainOutcome.Cancelled;
            }

            if (++budget.Steps > MaxSteps)
            {
                if (!allowGlobalErrorHandler)
                    return ChainOutcome.ChainEnded; // budget exhausted mid-chain: just stop the chain

                result.Error = $"Maximum step count ({MaxSteps}) exceeded — possible node cycle";
                return ChainOutcome.RunFailed;
            }

            var node = current;

            // "loop" nodes are interpreted by the runtime, before generic routing.
            if (string.Equals(node.Type, "loop", StringComparison.OrdinalIgnoreCase))
            {
                return await RunLoopAsync(
                    node, nodesById, workflow, ctx, result, budget, runToken,
                    allowGlobalErrorHandler).ConfigureAwait(false);
            }

            var nodeStopwatch = Stopwatch.StartNew();

            var implementation = _registry.Get(node.Type);
            if (implementation is null)
            {
                if (!allowGlobalErrorHandler)
                    return ChainOutcome.ChainEnded;

                result.Error = $"No node registered for type '{node.Type}'";
                return ChainOutcome.RunFailed;
            }

            NodeResult nodeResult;
            try
            {
                nodeResult = await implementation.ExecuteAsync(node, ctx).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                nodeStopwatch.Stop();
                result.Status = "Cancelled";
                result.Error = $"Node '{node.Type}' was cancelled";
                RecordStep(result, node, nodeStopwatch.ElapsedMilliseconds);
                return ChainOutcome.Cancelled;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Node '{NodeType}' ({NodeId}) threw in workflow '{WorkflowName}'",
                    node.Type, node.Id, workflow.Name);
                nodeResult = NodeResult.Fail(ex.Message);
            }

            nodeStopwatch.Stop();
            RecordStep(result, node, nodeStopwatch.ElapsedMilliseconds);

            if (!nodeResult.Succeeded)
            {
                // Error routing: continue on the node's error branch when wired;
                // only escalate when there is no error branch to take.
                var errorTarget = FindNode(nodesById, node.BranchError);
                if (errorTarget is not null)
                {
                    _logger.LogWarning(
                        "Node '{NodeType}' ({NodeId}) failed in workflow '{WorkflowName}': {Error} — continuing via error branch",
                        node.Type, node.Id, workflow.Name, nodeResult.Error ?? "unknown error");
                    current = errorTarget;
                    continue;
                }

                if (!string.IsNullOrEmpty(node.BranchError))
                {
                    _logger.LogWarning(
                        "Node '{NodeType}' ({NodeId}) failed in workflow '{WorkflowName}': {Error} — error branch '{BranchError}' does not exist",
                        node.Type, node.Id, workflow.Name, nodeResult.Error ?? "unknown error", node.BranchError);
                }

                // A failing node of the Error Handler chain just ends the chain
                // (the local flag keeps the handler from recursing into itself).
                if (!allowGlobalErrorHandler)
                    return ChainOutcome.ChainEnded;

                // Workflow-level Error Handler: run the handler chain as a
                // reaction when the failing node has no error branch of its own.
                // The run still ends with the original failure below — the
                // handler reacting does not make the run succeed.
                var handlerEntry = FindNode(nodesById, workflow.ErrorHandlerEntryId);
                if (handlerEntry is not null)
                {
                    var originalError = $"Node '{node.Type}' ({node.Id}) failed: {nodeResult.Error ?? "unknown error"}";

                    _logger.LogWarning(
                        "Node '{NodeType}' ({NodeId}) failed in workflow '{WorkflowName}': {Error} — running error handler chain",
                        node.Type, node.Id, workflow.Name, nodeResult.Error ?? "unknown error");

                    var handlerOutcome = await RunChainAsync(
                        handlerEntry, nodesById, workflow, ctx, result, budget, runToken,
                        allowGlobalErrorHandler: false).ConfigureAwait(false);

                    if (handlerOutcome == ChainOutcome.Cancelled)
                        return ChainOutcome.Cancelled;

                    result.Error = originalError;
                    result.Status = "Failed";
                    return ChainOutcome.RunFailed;
                }

                result.Error = $"Node '{node.Type}' ({node.Id}) failed: {nodeResult.Error ?? "unknown error"}";
                return ChainOutcome.RunFailed;
            }

            // A "stop_loop" inside a loop body ends every remaining iteration.
            if (stopAtNodeId is not null && ConsumeStopLoopRequest(ctx))
                return ChainOutcome.StopLoopRequested;

            // Route on the node's output: condition nodes output "true"/"false",
            // switch nodes route through Branches, everything else follows Next.
            // A missing branch falls back to Next so partially wired graphs still flow.
            current = ChooseNext(nodesById, ResolveNextId(node, nodeResult.OutputValue), result);

            if (current is null && result.Error is not null)
                return allowGlobalErrorHandler ? ChainOutcome.RunFailed : ChainOutcome.ChainEnded;
        }

        return ChainOutcome.ChainEnded;
    }

    /// <summary>
    /// Interprets a "loop" node: runs its body chain (the Branches["body"] port,
    /// wired from the "Loop times" output) once per iteration, then routes via
    /// node.Next ("After loop"). Invalid or non-positive counts fall back to a
    /// single iteration; the count is capped at <see cref="MaxLoopIterations"/>.
    /// A "stop_loop" node sets <see cref="StopLoopVariable"/>, which ends every
    /// remaining iteration. Unset body port = zero iterations, straight to Next.
    /// </summary>
    private async Task<ChainOutcome> RunLoopAsync(
        WorkflowNode node,
        Dictionary<string, WorkflowNode> nodesById,
        Workflow workflow,
        ExecutionContext ctx,
        WorkflowExecutionResult result,
        StepBudget budget,
        CancellationToken runToken,
        bool allowGlobalErrorHandler)
    {
        var loopStopwatch = Stopwatch.StartNew();

        var countRaw = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, "1", "Count"));
        if (!int.TryParse(countRaw, System.Globalization.CultureInfo.InvariantCulture, out var count) || count <= 0)
            count = 1;
        if (count > MaxLoopIterations)
            count = MaxLoopIterations;

        // Start clean: a stale flag from an earlier stop_loop outside this loop is ignored.
        ctx.SetVariable(StopLoopVariable, null);

        // Body entry = the "Loop times" port ("case:body" → Branches["body"]).
        var bodyEntry = FindNode(nodesById, GetBranchTarget(node, "body"));

        for (var iteration = 0; iteration < count && bodyEntry is not null; iteration++)
        {
            var bodyOutcome = await RunChainAsync(
                bodyEntry, nodesById, workflow, ctx, result, budget, runToken,
                allowGlobalErrorHandler, stopAtNodeId: node.Id).ConfigureAwait(false);

            if (bodyOutcome == ChainOutcome.StopLoopRequested)
                break; // "__stop_loop" observed (and consumed): stop every remaining iteration

            if (bodyOutcome != ChainOutcome.ChainEnded)
            {
                // Cancelled or fatal failure inside the body: record the loop
                // node itself, then propagate (result already carries the state).
                loopStopwatch.Stop();
                RecordStep(result, node, loopStopwatch.ElapsedMilliseconds);
                return bodyOutcome;
            }
            // ChainEnded: the body walked off the graph or routed back into the
            // loop node — end of this iteration, continue with the next.
        }

        loopStopwatch.Stop();
        RecordStep(result, node, loopStopwatch.ElapsedMilliseconds);

        // After loop → the "After loop" port (node.Next).
        var next = ChooseNext(nodesById, node.Next, result);
        if (next is null)
            return allowGlobalErrorHandler && result.Error is not null ? ChainOutcome.RunFailed : ChainOutcome.ChainEnded;

        return await RunChainAsync(
            next, nodesById, workflow, ctx, result, budget, runToken,
            allowGlobalErrorHandler).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines the entry node: the workflow's explicit
    /// <see cref="Workflow.EntryNodeId"/> (the trigger wire target) when set and
    /// present; otherwise the first node in the list that no other node routes to
    /// (see <see cref="FindEntryNode"/>).
    /// </summary>
    private static WorkflowNode? ResolveEntryNode(Workflow workflow, Dictionary<string, WorkflowNode> nodesById)
    {
        if (!string.IsNullOrEmpty(workflow.EntryNodeId) &&
            nodesById.TryGetValue(workflow.EntryNodeId, out var entry))
            return entry;

        return FindEntryNode(workflow);
    }

    /// <summary>Looks a per-case branch target up (exact first, then case-insensitive); null when unset.</summary>
    private static string? GetBranchTarget(WorkflowNode node, string caseKey)
    {
        if (node.Branches is null)
            return null;

        if (node.Branches.TryGetValue(caseKey, out var target))
            return target;

        foreach (var pair in node.Branches)
        {
            if (string.Equals(pair.Key, caseKey, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    /// <summary>
    /// Checks (and clears) the <see cref="StopLoopVariable"/> request set by a
    /// "stop_loop" node. True when a break was requested.
    /// </summary>
    private static bool ConsumeStopLoopRequest(ExecutionContext ctx)
    {
        if (!ctx.Variables.TryGetValue(StopLoopVariable, out var value) ||
            !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        ctx.SetVariable(StopLoopVariable, null);
        return true;
    }

    /// <summary>
    /// Determines the entry node: the first node in the list that no other node
    /// routes to (via Next/BranchTrue/BranchFalse/BranchError/Branches). Falls back
    /// to the first node in the list (also covers fully cyclic graphs).
    /// </summary>
    private static WorkflowNode? FindEntryNode(Workflow workflow)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in workflow.Nodes)
        {
            if (node is null)
                continue;

            AddReference(referenced, node.Next);
            AddReference(referenced, node.BranchTrue);
            AddReference(referenced, node.BranchFalse);
            AddReference(referenced, node.BranchError);

            if (node.Branches is not null)
            {
                foreach (var target in node.Branches.Values)
                    AddReference(referenced, target);
            }
        }

        foreach (var node in workflow.Nodes)
        {
            if (node is not null && (string.IsNullOrEmpty(node.Id) || !referenced.Contains(node.Id)))
                return node;
        }

        return workflow.Nodes.Count > 0 ? workflow.Nodes[0] : null;
    }

    /// <summary>Adds a referenced node id to the set (null/empty ids are ignored).</summary>
    private static void AddReference(HashSet<string> referenced, string? nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
            referenced.Add(nodeId);
    }

    /// <summary>
    /// Picks the outgoing route for a successful node, in precedence order:
    /// "switch" nodes look their output up in <see cref="WorkflowNode.Branches"/>
    /// (exact match first, then a case-insensitive scan) falling back to
    /// BranchFalse/Next — their output is the matched case VALUE, never
    /// "case:"-prefixed, so this path is unchanged; "case:{i}" and "else"
    /// outputs (multi-rule comparison nodes) route through Branches first, with
    /// an unmatched "else" falling back to BranchFalse/Next and an unmatched
    /// "case:x" falling back to Next; condition outputs "true"/"false" take
    /// BranchTrue/BranchFalse falling back to Next; anything else follows Next.
    /// </summary>
    private static string? ResolveNextId(WorkflowNode node, string? outputValue)
    {
        if (string.Equals(node.Type, "switch", StringComparison.OrdinalIgnoreCase))
        {
            if (outputValue is not null && node.Branches is not null)
            {
                if (node.Branches.TryGetValue(outputValue, out var exactTarget))
                    return exactTarget;

                foreach (var pair in node.Branches)
                {
                    if (string.Equals(pair.Key, outputValue, StringComparison.OrdinalIgnoreCase))
                        return pair.Value;
                }
            }

            return node.BranchFalse ?? node.Next;
        }

        // Multi-case branchers (multi-rule "comparison"): the output is the
        // matched rule's case key ("case:{i}") or "else" — route through the
        // matching Branches entry when wired. Checked after the switch handling
        // (whose outputs are raw case values), so existing switch flows route
        // exactly as before.
        if (outputValue is not null &&
            (outputValue.StartsWith(CaseOutputPrefix, StringComparison.Ordinal) ||
             outputValue.Equals(ElseOutputValue, StringComparison.OrdinalIgnoreCase)))
        {
            if (node.Branches is not null)
            {
                if (node.Branches.TryGetValue(outputValue, out var exactCaseTarget))
                    return exactCaseTarget;

                foreach (var pair in node.Branches)
                {
                    if (string.Equals(pair.Key, outputValue, StringComparison.OrdinalIgnoreCase))
                        return pair.Value;
                }
            }

            // Unwired else falls back to the default port, then Next; an unwired
            // "case:{i}" simply continues the chain.
            if (outputValue.Equals(ElseOutputValue, StringComparison.OrdinalIgnoreCase))
                return node.BranchFalse ?? node.Next;

            return node.Next;
        }

        if (string.Equals(outputValue, "true", StringComparison.OrdinalIgnoreCase))
            return node.BranchTrue ?? node.Next;

        if (string.Equals(outputValue, "false", StringComparison.OrdinalIgnoreCase))
            return node.BranchFalse ?? node.Next;

        return node.Next;
    }

    /// <summary>Looks a node id up (null-safe); null when the id is null or unknown.</summary>
    private static WorkflowNode? FindNode(Dictionary<string, WorkflowNode> nodesById, string? nodeId) =>
        nodeId is not null && nodesById.TryGetValue(nodeId, out var node) ? node : null;

    private static WorkflowNode? ChooseNext(Dictionary<string, WorkflowNode> nodesById, string? nextId, WorkflowExecutionResult result)
    {
        if (nextId is null)
            return null; // end of workflow

        if (nodesById.TryGetValue(nextId, out var next))
            return next;

        result.Error = $"Next node '{nextId}' not found";
        return null;
    }

    private static void RecordStep(WorkflowExecutionResult result, WorkflowNode node, long durationMs)
    {
        result.Path.Add($"{node.Type}:{node.Id}");
        result.NodeTimings.Add(new NodeTiming(node.Id, node.Type, durationMs));
    }

    /// <summary>Human-readable one-line trigger summary for the execution history.</summary>
    public static string SummarizeTrigger(WorkflowTrigger? trigger)
    {
        if (trigger is null)
            return "unknown";

        return trigger.Type.ToLowerInvariant() switch
        {
            WorkflowTriggerTypes.Command => $"command {(string.IsNullOrWhiteSpace(trigger.CommandPrefix) ? "!" : trigger.CommandPrefix)}{trigger.CommandName}",
            WorkflowTriggerTypes.Timer => $"timer every {trigger.IntervalMinutes ?? 0}m",
            WorkflowTriggerTypes.Event => $"event {trigger.Platform ?? "Any"}.{trigger.EventType ?? "?"}",
            _ => "manual run"
        };
    }
}
