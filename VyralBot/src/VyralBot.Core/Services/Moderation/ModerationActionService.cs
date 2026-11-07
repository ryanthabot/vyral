using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// The per-platform enforcement surface the auto-moderator drives. One adapter
/// per connected platform, registered by the composition root (or a chat
/// pipeline) when that platform gains moderation APIs. Every method is
/// best-effort: false / exceptions never crash the chat pipeline.
/// </summary>
public interface IModerationPlatformAdapter
{
    /// <summary>Platform name as used in <see cref="ModerationIncomingMessage.Platform"/>.</summary>
    string Platform { get; }

    /// <summary>Whether the platform can delete a single chat message.</summary>
    bool CanDeleteMessage { get; }

    /// <summary>Whether the platform can time a viewer out.</summary>
    bool CanTimeout { get; }

    /// <summary>Whether the platform can ban a viewer.</summary>
    bool CanBan { get; }

    /// <summary>Deletes the message from <paramref name="message"/> (by id or author).</summary>
    Task<bool> DeleteMessageAsync(ModerationIncomingMessage message);

    /// <summary>Times the viewer out for <paramref name="seconds"/>.</summary>
    Task<bool> TimeoutAsync(ModerationIncomingMessage message, int seconds, string reason);

    /// <summary>Bans the viewer.</summary>
    Task<bool> BanAsync(ModerationIncomingMessage message, string reason);

    /// <summary>Posts a public chat notice (used for warnings and fallbacks).</summary>
    Task<bool> SendNoticeAsync(string message);
}

/// <summary>
/// Executes auto-moderation verdicts against the registered platform adapters
/// with GRACEFUL DEGRADATION: a platform without a ban API falls back
/// timeout → delete → warn; a platform with no adapter at all still gets the
/// action logged (and a warning notice when some chat send path exists via an
/// adapter). NEVER throws. Every executed — or degraded, or logged-only —
/// action is written to the persistent mod-action log
/// (<see cref="IDataService.SaveModActionAsync"/>), so it survives restarts and
/// shows up in the Moderator dashboard's Action Log.
/// </summary>
public sealed class ModerationActionService
{
    private const string AutoModActor = "AutoMod";
    private const int MaxTimeoutSeconds = 604800; // 7 days

    private readonly object _adapterGate = new();
    private readonly Dictionary<string, IModerationPlatformAdapter> _adapters =
        new(StringComparer.OrdinalIgnoreCase);

    // Non-readonly: the Shared instance gets these late via Configure() from
    // the Moderator dashboard's page init. Plain fields are sufficient —
    // Configure runs once, before any chat-pipeline ExecuteAsync calls.
    private IDataService? _dataService;
    private ILogger? _logger;

    /// <summary>
    /// Process-wide default instance. The Moderator dashboard configures it
    /// with the profile's IDataService on page init; the chat pipelines call
    /// <see cref="ExecuteAsync"/> on it. DI composition may also construct a
    /// dedicated instance later (the class is stateless besides the registry).
    /// </summary>
    public static ModerationActionService Shared { get; } = new();

    public ModerationActionService()
    {
    }

    public ModerationActionService(IDataService dataService, ILogger? logger = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger;
    }

    /// <summary>Late binding of persistence + logging (used for the Shared instance).</summary>
    public void Configure(IDataService dataService, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataService);
        _dataService = dataService;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // adapter registry
    // ------------------------------------------------------------------

    public void RegisterAdapter(IModerationPlatformAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        lock (_adapterGate)
        {
            _adapters[adapter.Platform] = adapter;
        }
    }

    public void UnregisterAdapter(string platform)
    {
        lock (_adapterGate)
        {
            _adapters.Remove(platform);
        }
    }

    public IModerationPlatformAdapter? GetAdapter(string platform)
    {
        lock (_adapterGate)
        {
            return _adapters.TryGetValue(platform, out var adapter) ? adapter : null;
        }
    }

    /// <summary>Snapshot of the registered adapters (UI parity display).</summary>
    public IReadOnlyList<IModerationPlatformAdapter> GetAdapters()
    {
        lock (_adapterGate)
        {
            return _adapters.Values.ToList();
        }
    }

    // ------------------------------------------------------------------
    // execution + degradation
    // ------------------------------------------------------------------

    /// <summary>
    /// Executes (or degrades) one verdict and logs it. Returns the action kind
    /// that was actually applied — check the returned value against
    /// <paramref name="verdict"/>.Action to detect degradation.
    /// </summary>
    public async Task<ModerationActionKind> ExecuteAsync(
        ModerationIncomingMessage message,
        ModerationVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(verdict);

        var adapter = GetAdapter(message.Platform);
        var desired = verdict.Action;
        var executed = desired;
        var degraded = false;
        var notice = $"@{message.Username}: please follow the chat rules ({verdict.Reason}).";

        try
        {
            switch (desired)
            {
                case ModerationActionKind.Ban:
                    if (adapter?.CanBan == true && await adapter.BanAsync(message, verdict.Reason).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Ban;
                    }
                    else if (adapter?.CanTimeout == true &&
                             await adapter.TimeoutAsync(message, Math.Min(verdict.TimeoutSeconds, MaxTimeoutSeconds), verdict.Reason).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Timeout;
                        degraded = true;
                    }
                    else if (adapter?.CanDeleteMessage == true &&
                             await adapter.DeleteMessageAsync(message).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Delete;
                        degraded = true;
                    }
                    else if (await TryNotifyAsync(adapter, notice).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Warn;
                        degraded = true;
                    }
                    else
                    {
                        // No adapter at all (e.g. TikTok / Kick today): the
                        // action still lands in the mod log so mods see it.
                        executed = ModerationActionKind.Warn;
                        degraded = true;
                    }

                    break;

                case ModerationActionKind.Timeout:
                    if (adapter?.CanTimeout == true &&
                        await adapter.TimeoutAsync(message, Math.Min(verdict.TimeoutSeconds, MaxTimeoutSeconds), verdict.Reason).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Timeout;
                    }
                    else if (adapter?.CanDeleteMessage == true &&
                             await adapter.DeleteMessageAsync(message).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Delete;
                        degraded = true;
                    }
                    else if (await TryNotifyAsync(adapter, notice).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Warn;
                        degraded = true;
                    }
                    else
                    {
                        executed = ModerationActionKind.Warn;
                        degraded = true;
                    }

                    break;

                case ModerationActionKind.Delete:
                    if (adapter?.CanDeleteMessage == true &&
                        await adapter.DeleteMessageAsync(message).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Delete;
                    }
                    else if (await TryNotifyAsync(adapter, notice).ConfigureAwait(false))
                    {
                        executed = ModerationActionKind.Warn;
                        degraded = true;
                    }
                    else
                    {
                        executed = ModerationActionKind.Warn;
                        degraded = true;
                    }

                    break;

                case ModerationActionKind.Warn:
                    await TryNotifyAsync(adapter, notice).ConfigureAwait(false);
                    executed = ModerationActionKind.Warn;
                    break;
            }
        }
        catch (Exception ex)
        {
            // Adapter contract says "never throw" but the chat pipeline must
            // survive even a misbehaving adapter.
            _logger?.LogWarning(ex,
                "Auto-mod action {Action} on {Platform}/{Username} threw — logged as degraded",
                desired, message.Platform, message.Username);
            executed = desired;
            degraded = true;
        }

        await LogAsync(message, verdict, executed, degraded, adapter is null).ConfigureAwait(false);
        return executed;
    }

    private static async Task<bool> TryNotifyAsync(IModerationPlatformAdapter? adapter, string notice)
    {
        if (adapter is null)
        {
            return false;
        }

        try
        {
            return await adapter.SendNoticeAsync(notice).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private async Task LogAsync(
        ModerationIncomingMessage message,
        ModerationVerdict verdict,
        ModerationActionKind executed,
        bool degraded,
        bool noAdapter)
    {
        var details = $"{verdict.Rule}: {verdict.Reason} (strike {verdict.Strikes})" +
                      (noAdapter ? " — no platform adapter, logged only" :
                       degraded ? $" — degraded from {verdict.Action}" : "");
        if (executed == ModerationActionKind.Timeout)
        {
            details += $" [{Math.Min(verdict.TimeoutSeconds, MaxTimeoutSeconds)}s]";
        }

        try
        {
            var dataService = _dataService;
            if (dataService is null)
            {
                _logger?.LogDebug(
                    "Auto-mod action not persisted (no DataService configured): {Details}", details);
                return;
            }

            await dataService.SaveModActionAsync(new ModAction
            {
                ModUsername = AutoModActor,
                ModUserId = "",
                TargetUsername = message.Username,
                TargetUserId = message.UserId ?? "",
                Action = executed.ToString(),
                Details = $"{message.Platform}: {details}"
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist the auto-mod action for {Username}", message.Username);
        }
    }
}

/// <summary>Coarse enforcement level a platform offers the auto-moderator.</summary>
public enum ModerationEnforcementLevel
{
    /// <summary>No moderation surface at all — verdicts are logged only.</summary>
    None,

    /// <summary>Adapter posts warning notices; no native per-viewer actions.</summary>
    NoticeOnly,

    /// <summary>Ban / timeout / delete (or the platform equivalent) enforced natively.</summary>
    Full
}

/// <summary>
/// One platform's parity row for the Moderator dashboard's enforcement card —
/// the audit level from <see cref="ModerationPlatformParity.Table"/> merged
/// with live adapter registration.
/// </summary>
public sealed record ModerationPlatformParityRow(
    string Platform,
    ModerationEnforcementLevel Level,
    string Capabilities,
    string Note,
    bool AdapterRegistered)
{
    /// <summary>Short badge text ("Full" / "Notice-only" / "None").</summary>
    public string LevelText => Level switch
    {
        ModerationEnforcementLevel.Full => "Full",
        ModerationEnforcementLevel.NoticeOnly => "Notice-only",
        _ => "None"
    };

    /// <summary>Live registration state of the platform adapter.</summary>
    public string Registration => AdapterRegistered ? "adapter registered" : "no adapter registered";

    /// <summary>One-line detail under the platform name + level badge.</summary>
    public string Detail => $"{Registration} — {Capabilities}. {Note}";
}

/// <summary>
/// Static per-platform moderation parity table — the audit-truth for what each
/// integration can enforce TODAY (see the service interfaces in
/// VyralBot.Integrations and the adapters in App.xaml.cs). The Moderator
/// dashboard's parity card renders <see cref="Snapshot"/> rows so its display
/// can never drift from this table.
/// </summary>
public static class ModerationPlatformParity
{
    /// <summary>Platform, enforcement level, native capability summary, audit note.</summary>
    public static readonly (string Platform, ModerationEnforcementLevel Level, string BuiltIn, string Note)[] Table =
    {
        ("Twitch", ModerationEnforcementLevel.Full,
            "ban / timeout / delete via Helix (POST /moderation/bans, DELETE /moderation/chat)",
            "TwitchModerationAdapter enforces every verdict natively; the chat pipeline does not parse IRC message ids yet, so a Delete without a message id degrades to a 1-second timeout — a timeout purges the sender's messages, which still removes the offending line"),
        ("Discord", ModerationEnforcementLevel.Full,
            "ban / timeout by username (minute-granular)",
            "DiscordModerationAdapter enforces ban + timeout natively; no delete-by-id/by-author API is exposed, so Delete verdicts degrade to a warning notice"),
        ("Kick", ModerationEnforcementLevel.NoticeOnly,
            "connect/send only",
            "KickModerationAdapter posts warn notices; moderation webhooks (WebhooksController) are inbound notifications, not outbound actions — ban/timeout/delete degrade to warn"),
        ("YouTube", ModerationEnforcementLevel.NoticeOnly,
            "connect/send + stream info only",
            "YouTubeModerationAdapter posts warn notices; IYouTubeService has no per-viewer action API — ban/timeout/delete degrade to warn"),
        ("TikTok", ModerationEnforcementLevel.None,
            "none",
            "No chat service integration exists (web setup only) — auto-mod verdicts are logged only")
    };

    /// <summary>
    /// Structured rows for the Moderator dashboard's parity card: one per
    /// known platform (Table order) plus any registered adapter the Table
    /// doesn't cover yet, each carrying the Table's enforcement level merged
    /// with live adapter registration.
    /// </summary>
    public static IReadOnlyList<ModerationPlatformParityRow> Snapshot(ModerationActionService? service)
    {
        var adapters = service?.GetAdapters() ??
            (IReadOnlyList<IModerationPlatformAdapter>)Array.Empty<IModerationPlatformAdapter>();
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            registered.Add(adapter.Platform);
        }

        var rows = new List<ModerationPlatformParityRow>(Table.Length);
        foreach (var (platform, level, builtIn, note) in Table)
        {
            rows.Add(new ModerationPlatformParityRow(
                platform, level, builtIn, note, registered.Remove(platform)));
        }

        // Adapters for platforms not yet audited into the Table (future
        // integrations): derive the level from the advertised capabilities so
        // they still appear on the card instead of being invisible.
        foreach (var adapter in adapters)
        {
            if (registered.Remove(adapter.Platform))
            {
                rows.Add(new ModerationPlatformParityRow(
                    adapter.Platform,
                    LevelForCapabilities(adapter),
                    DescribeCapabilities(adapter),
                    "Platform not yet in the parity audit table — level derived from the adapter's capability flags",
                    true));
            }
        }

        return rows;
    }

    /// <summary>
    /// One status line per known platform (text form of <see cref="Snapshot"/>),
    /// e.g. for logs: enforcement level, adapter registration, degradation.
    /// </summary>
    public static IEnumerable<string> Describe(ModerationActionService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        foreach (var row in Snapshot(service))
        {
            yield return row.Level switch
            {
                ModerationEnforcementLevel.Full =>
                    $"{row.Platform} — FULL enforcement ({row.Registration}; {row.Capabilities}) — {row.Note}",
                ModerationEnforcementLevel.NoticeOnly =>
                    $"{row.Platform} — NOTICE-ONLY ({row.Registration}; {row.Capabilities}) — ban/timeout/delete degrade to warn — {row.Note}",
                _ =>
                    $"{row.Platform} — NO enforcement surface ({row.Registration}) — actions are logged only — {row.Note}"
            };
        }
    }

    private static ModerationEnforcementLevel LevelForCapabilities(IModerationPlatformAdapter adapter)
        => adapter.CanBan || adapter.CanTimeout || adapter.CanDeleteMessage
            ? ModerationEnforcementLevel.Full
            : ModerationEnforcementLevel.NoticeOnly;

    private static string DescribeCapabilities(IModerationPlatformAdapter adapter)
    {
        var parts = new List<string>(3);
        if (adapter.CanDeleteMessage) parts.Add("delete");
        if (adapter.CanTimeout) parts.Add("timeout");
        if (adapter.CanBan) parts.Add("ban");
        return parts.Count == 0 ? "notice-only" : string.Join("/", parts);
    }
}
