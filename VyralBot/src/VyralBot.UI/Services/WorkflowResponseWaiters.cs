using Microsoft.Extensions.Logging;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Services;

/// <summary>
/// Thread-safe implementation of <see cref="VyralBot.Workflows.Abstractions.IResponseWaiterRegistry"/>
/// backing the "wait_for_response" workflow node
/// (<see cref="VyralBot.Workflows.Nodes.WaitResponseNode"/>).
///
/// CONTRACT: the node suspends a workflow run until the TRIGGERING USER posts
/// another message on the same platform/channel — the received text is stored
/// in the execution's <c>{response}</c> variable, and a timeout (or run
/// cancellation) produces null so the node fails and the runtime's error
/// branch can take over. The node resolves the registry from
/// ExecutionContext.Services; a missing registration is a failed node result,
/// never a throw.
///
/// FEEDING: ChatViewModel calls <see cref="Feed"/> once per inbound USER
/// message — after echo filtering and dedup, so the bot's own outgoing text
/// never satisfies a waiter. Feed is a cheap no-op when nobody is waiting.
///
/// KEYING: waiters are keyed by "platform|username|channel" (lowercased) —
/// exactly the ctx.Platform / ctx.Username / ctx.Channel triple the node
/// waits on, so a workflow triggered in "#general" only ever sees replies
/// posted in "#general" by the same user.
///
/// LIMITS: at most <see cref="MaxPendingWaiters"/> concurrent pending waiters;
/// beyond that the OLDEST pending waiters fail fast (as if timed out) so a
/// runaway flow cannot pin memory. All matching waiters are completed by a
/// single Feed (fan-out). Fully thread-safe: registering, feeding and removal
/// share one lock — contention is irrelevant at these scales.
/// </summary>
public class WorkflowResponseWaiters : IResponseWaiterRegistry
{
    /// <summary>Maximum number of concurrently pending waiters (oldest fail fast beyond this).</summary>
    private const int MaxPendingWaiters = 100;

    private readonly ILogger<WorkflowResponseWaiters>? _logger;
    private readonly object _sync = new();
    // normalized "platform|username|channel" → pending waiters for that origin
    private readonly Dictionary<string, List<PendingWaiter>> _pending = new(StringComparer.Ordinal);
    // global registration order across all keys, for the oldest-fail-fast cap
    private readonly Queue<PendingWaiter> _registrationOrder = new();

    private sealed class PendingWaiter
    {
        public PendingWaiter(string key, DateTime expiresAtUtc)
        {
            Key = key;
            ExpiresAtUtc = expiresAtUtc;
            Completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string Key { get; }
        public DateTime ExpiresAtUtc { get; }
        public TaskCompletionSource<string?> Completion { get; }
    }

    public WorkflowResponseWaiters(ILogger<WorkflowResponseWaiters>? logger = null)
    {
        _logger = logger; // optional — the registry works silently without one
    }

    /// <inheritdoc />
    public async Task<string?> WaitForResponseAsync(
        string platform,
        string username,
        string channel,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(platform, username, channel);
        if (key.Length == 0)
        {
            return null; // nothing addressable to wait on
        }

        if (timeout < TimeSpan.Zero)
        {
            timeout = TimeSpan.Zero;
        }

        var waiter = new PendingWaiter(key, DateTime.UtcNow + timeout);
        lock (_sync)
        {
            if (!_pending.TryGetValue(key, out var list))
            {
                list = new List<PendingWaiter>();
                _pending[key] = list;
            }

            // Drop waiters that already expired but whose Task.Delay has not
            // fired yet (they must never be completed by a late Feed).
            var now = DateTime.UtcNow;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].ExpiresAtUtc <= now)
                {
                    var expired = list[i];
                    list.RemoveAt(i);
                    SafeSetResult(expired, null);
                }
            }

            list.Add(waiter);
            _registrationOrder.Enqueue(waiter);

            // Global cap: fail the OLDEST pending waiters first so a runaway
            // flow cannot accumulate unbounded waiters.
            while (_registrationOrder.Count > MaxPendingWaiters)
            {
                var oldest = _registrationOrder.Dequeue();
                if (RemoveCore(oldest))
                {
                    SafeSetResult(oldest, null); // fail fast, indistinguishable from a timeout
                    _logger?.LogDebug("Response waiter for {Key} dropped: pending-waiter cap ({Cap}) reached",
                        oldest.Key, MaxPendingWaiters);
                }
                // Entries already completed/removed elsewhere dequeue as no-ops.
            }
        }

        try
        {
            var delayTask = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(waiter.Completion.Task, delayTask).ConfigureAwait(false);
            if (completed != waiter.Completion.Task)
            {
                return null; // timeout or cancellation
            }

            return await waiter.Completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null; // run cancelled — contract says null, never a throw
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Response waiter await failed for {Key}", key);
            return null;
        }
        finally
        {
            lock (_sync)
            {
                // Always deregister. When WE removed it here nobody else has
                // completed it — settle it as a timeout so no awaiter leaks.
                if (RemoveCore(waiter))
                {
                    SafeSetResult(waiter, null);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Feed(string platform, string username, string channel, string message)
    {
        var key = BuildKey(platform, username, channel);
        if (key.Length == 0)
        {
            return;
        }

        List<PendingWaiter> toComplete;
        lock (_sync)
        {
            if (_pending.Count == 0 || !_pending.TryGetValue(key, out var list))
            {
                return; // cheap no-op when nobody is waiting on this origin
            }

            toComplete = new List<PendingWaiter>(list.Count);
            var now = DateTime.UtcNow;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].ExpiresAtUtc > now)
                {
                    toComplete.Add(list[i]);
                }

                // Expired waiters are dropped too — their own Task.Delay
                // settles them; a late Feed must not resurrect them.
                list.RemoveAt(i);
            }

            _pending.Remove(key);
        }

        // Complete OUTSIDE the lock: continuations may run inline.
        foreach (var waiter in toComplete)
        {
            SafeSetResult(waiter, message);
        }

        if (toComplete.Count > 0)
        {
            _logger?.LogDebug("Response waiter(s) for {Key} fed ({Count} waiter(s))", key, toComplete.Count);
        }
    }

    /// <summary>
    /// Removes a waiter from its per-key list (and leaves the global order
    /// queue to drain lazily via the cap loop). True when the waiter was still
    /// registered — i.e. the caller is the one settling its completion.
    /// </summary>
    private bool RemoveCore(PendingWaiter waiter)
    {
        if (!_pending.TryGetValue(waiter.Key, out var list))
        {
            return false;
        }

        if (!list.Remove(waiter))
        {
            return false;
        }

        if (list.Count == 0 && ReferenceEquals(_pending[waiter.Key], list))
        {
            _pending.Remove(waiter.Key);
        }

        return true;
    }

    /// <summary>Settles a waiter, tolerating already-completed sources. Never throws.</summary>
    private void SafeSetResult(PendingWaiter waiter, string? result)
    {
        try
        {
            waiter.Completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to settle a response waiter for {Key}", waiter.Key);
        }
    }

    /// <summary>Normalized waiter key ("platform|username|channel", lowercased); empty when nothing usable.</summary>
    private static string BuildKey(string? platform, string? username, string? channel)
    {
        var p = (platform ?? string.Empty).Trim();
        var u = (username ?? string.Empty).Trim();
        var c = (channel ?? string.Empty).Trim();
        if (p.Length == 0 || u.Length == 0)
        {
            return string.Empty; // channel may legitimately be empty (e.g. DMs)
        }

        return $"{p}|{u}|{c}".ToLowerInvariant();
    }
}
