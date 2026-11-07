using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.ProcessManagement;

namespace VyralBot.Integrations.Restream;

/// <summary>
/// Runs one FFmpeg restream session: a listen-mode RTMP ingest (one publisher —
/// OBS on this PC or a gaming PC over the LAN) fanned out to N platform
/// outputs. The engine supervises the process: it restarts FFmpeg whenever the
/// publisher disconnects (OBS auto-reconnects on its own), backs off on
/// crash-looping, permanently fails on a busy ingest port, and auto-disables a
/// destination that repeatedly kills the session so the healthy outputs keep
/// streaming. Runs entirely in the background — navigating away from the page
/// never stops a live session; only StopAsync (user stop / app exit) does.
/// </summary>
public sealed class RestreamEngine : IDisposable
{
    /// <summary>A run shorter than this counts as a crash, not a normal disconnect.</summary>
    private static readonly TimeSpan QuickFailureWindow = TimeSpan.FromSeconds(10);

    /// <summary>Backoff ladder for consecutive quick failures (index = streak-1, last repeats).</summary>
    private static readonly int[] BackoffMs = { 2000, 5000, 10000, 30000 };

    private const int MaxConsecutiveUnattributedFailures = 3;

    private readonly IProcessManager _processManager;
    private readonly ILogger<RestreamEngine> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _supervision;
    private RestreamSessionConfig? _config;

    private RestreamEngineStatus _status = RestreamEngineStatus.Idle;

    public RestreamEngine(IProcessManager processManager, ILogger<RestreamEngine> logger)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Latest immutable snapshot. Safe to poll; also raised via <see cref="StatusChanged"/>.</summary>
    public RestreamEngineStatus Status => _status;

    /// <summary>Raised on every state/stat change from background threads — marshal on the consumer side.</summary>
    public event EventHandler<RestreamEngineStatus>? StatusChanged;

    /// <summary>Masked FFmpeg log lines (bounded by the producer, not the engine).</summary>
    public event EventHandler<string>? LogLine;

    public bool IsRunning => _supervision is not null && !_supervision.IsCompleted;

    /// <summary>Stops any running session, then starts a fresh one with the given configuration.</summary>
    public async Task StartAsync(RestreamSessionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync("restarted").ConfigureAwait(false);

            _config = config;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetStatus(new RestreamEngineStatus(
                RestreamSessionState.WaitingForSource,
                "waiting for OBS to connect…",
                null,
                RuntimeFor(config.Destinations, excluded: null),
                null));

            _supervision = Task.Run(() => SuperviseAsync(config, token), token);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>Stops the session (kills FFmpeg's process tree) and returns to Idle.</summary>
    public async Task StopAsync()
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync("stopped by user").ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task StopCoreAsync(string reason)
    {
        _cts?.Cancel();
        if (_supervision is not null)
        {
            try
            {
                await _supervision.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Restream supervision loop ended with an error");
            }
        }

        _supervision = null;
        if (_cts is not null)
        {
            _cts.Dispose();
            _cts = null;
        }

        _config = null;
        if (_status.State != RestreamSessionState.Faulted)
        {
            SetStatus(RestreamEngineStatus.Idle with { Message = reason });
        }
    }

    private async Task SuperviseAsync(RestreamSessionConfig config, CancellationToken ct)
    {
        try
        {
            await SuperviseCoreAsync(config, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        catch (Exception ex)
        {
            // The supervision task is only awaited on stop — without this a
            // crash would leave the status frozen on "streaming".
            _logger.LogError(ex, "Restream supervision loop crashed");
            SetStatus(new RestreamEngineStatus(
                RestreamSessionState.Faulted,
                $"engine error: {ex.Message}",
                null,
                _status.Destinations,
                null));
        }
    }

    private async Task SuperviseCoreAsync(RestreamSessionConfig config, CancellationToken ct)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var quickFailureStreak = 0;
        var attributedStreak = 0;
        var lastImplicatedId = (string?)null;
        var unattributedStreak = 0;

        while (!ct.IsCancellationRequested)
        {
            var active = config.Destinations.Where(d => d.Enabled && !excluded.Contains(d.Id)).ToList();
            if (active.Count == 0)
            {
                SetStatus(new RestreamEngineStatus(
                    RestreamSessionState.Faulted,
                    excluded.Count > 0
                        ? "every destination failed repeatedly — stopped. Check the stream keys/URLs and start again."
                        : "no enabled destinations — add one before starting.",
                    null,
                    RuntimeFor(config.Destinations, excluded),
                    null));
                return;
            }

            var commandLine = RestreamArgumentBuilder.BuildCommandLine(config.Source, active);
            RaiseLog(RestreamArgumentBuilder.Mask($"ffmpeg {commandLine}", config.Destinations));

            var startInfo = new ProcessStartInfo(config.FfmpegPath)
            {
                Arguments = commandLine,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            var startedAt = DateTimeOffset.UtcNow;
            Process? process = null;
            var runLog = new List<string>(256);
            var sawPublisher = false;
            var implicatedId = (string?)null;
            var implicatedDetail = (string?)null;
            var portInUse = false;

            try
            {
                process = await _processManager.StartProcessAsync(startInfo, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus(new RestreamEngineStatus(
                    RestreamSessionState.Faulted,
                    $"could not start FFmpeg ({RestreamArgumentBuilder.Mask(ex.Message, config.Destinations)})",
                    null,
                    RuntimeFor(config.Destinations, excluded),
                    null));
                return;
            }

            SetStatus(new RestreamEngineStatus(
                RestreamSessionState.WaitingForSource,
                "waiting for OBS to connect…",
                null,
                RuntimeFor(config.Destinations, excluded),
                null));

            // Exit notification via our own handler: the shared ProcessManager
            // disposes the Process object as soon as it exits, so relying on
            // WaitForExitAsync after the fact races with that disposal. The
            // WhenAny races exit against cancellation — a stop during a live
            // stream must kill FFmpeg instead of waiting for it to finish.
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnExited(object? sender, EventArgs args) => exited.TrySetResult();
            try
            {
                // Subscribe FIRST, then probe: if the process died between a
                // HasExited check and the subscription the Exited event fires
                // with no listener and never fires again, so the session would
                // wait forever. After subscribing, a late probe only races a
                // redundant TrySetResult.
                process.Exited += OnExited;
                if (process.HasExited)
                {
                    exited.TrySetResult();
                }
            }
            catch (Exception)
            {
                // ProcessManager disposed it (it already exited) — treat as exited.
                exited.TrySetResult();
            }

            // Both pipes must be drained to EOF or the child blocks on a full
            // pipe buffer (the gap the ngrok launcher has).
            var stderrPump = PumpAsync(process.StandardError, line =>
            {
                runLog.Add(line);
                if (line.StartsWith("Input #", StringComparison.Ordinal))
                {
                    sawPublisher = true;
                    SetStatus(new RestreamEngineStatus(
                        RestreamSessionState.Streaming,
                        "live",
                        _status.Stats,
                        ActiveRuntime(config.Destinations, excluded),
                        DateTimeOffset.UtcNow));
                }

                if (implicatedId is null && LooksLikeError(line))
                {
                    var match = FindImplicatedDestination(line, active);
                    if (match is not null)
                    {
                        implicatedId = match.Id;
                        implicatedDetail = RestreamArgumentBuilder.Mask(line, config.Destinations);
                    }
                }

                if (line.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase))
                {
                    portInUse = true;
                }

                RaiseLog(RestreamArgumentBuilder.Mask(line, config.Destinations));
            });

            var progressParser = new ProgressParser();
            var stdoutPump = PumpAsync(process.StandardOutput, line =>
            {
                if (progressParser.Feed(line) is { } stats)
                {
                    if (!sawPublisher)
                    {
                        sawPublisher = true;
                    }

                    SetStatus(new RestreamEngineStatus(
                        RestreamSessionState.Streaming,
                        "live",
                        stats,
                        ActiveRuntime(config.Destinations, excluded),
                        _status.StreamingSince ?? DateTimeOffset.UtcNow));
                }
            });

            await Task.WhenAny(exited.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                // StopAsync path: kill the tree so the pumps drain and the
                // supervision task can complete.
                try
                {
                    await _processManager.KillProcessAsync(process.Id).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Already gone — nothing to do.
                }
            }

            var ranFor = DateTimeOffset.UtcNow - startedAt;
            int? exitCode = null;
            try
            {
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception)
            {
                // ProcessManager already disposed it — treat as unknown exit.
            }

            try { process.Exited -= OnExited; } catch { }

            await Task.WhenAll(stderrPump, stdoutPump).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (portInUse)
            {
                SetStatus(new RestreamEngineStatus(
                    RestreamSessionState.Faulted,
                    $"port {config.Source.IngestPort} is already in use — change the ingest port or stop the other app using it.",
                    null,
                    RuntimeFor(config.Destinations, excluded),
                    null));
                return;
            }

            if (implicatedId is not null)
            {
                SetDestinationError(implicatedId, implicatedDetail);
            }

            // A clean exit after a real session is just the publisher stopping:
            // re-listen immediately; OBS's auto-reconnect republishes. The
            // exit code is often UNKNOWN on this path — the ProcessManager
            // disposes the Process in its own Exited handler before this read,
            // so ExitCode throws and reads as null — so a post-publisher exit
            // with no implicated destination and no port conflict is treated
            // as a publisher stop whenever the run was not quick; a sub-10s
            // run is a crash even if the publisher had connected (that one
            // keeps the backoff ladder and the unattributed-failure budget).
            // Without this, three healthy OBS reconnect cycles would fault
            // the session as "FFmpeg keeps exiting".
            if (sawPublisher && implicatedId is null && !portInUse && exitCode is 0 or null && ranFor >= QuickFailureWindow)
            {
                quickFailureStreak = 0;
                unattributedStreak = 0;
                lastImplicatedId = null;
                SetStatus(new RestreamEngineStatus(
                    RestreamSessionState.WaitingForSource,
                    "OBS disconnected — waiting for it to reconnect…",
                    null,
                    RuntimeFor(config.Destinations, excluded),
                    null));
                continue;
            }

            // Everything else is a crash (bad key, unreachable ingest, arg
            // error, publisher that never finished handshaking…). The quick
            // streak only drives the backoff ladder; attribution drives the
            // auto-disable/stop decisions and must work for slow failures too
            // (platforms that accept the connection and then reject the key
            // kill the session well past the quick window).
            var wasQuick = ranFor < QuickFailureWindow;
            quickFailureStreak = wasQuick ? quickFailureStreak + 1 : 0;

            attributedStreak = implicatedId is null
                ? 0
                : implicatedId == lastImplicatedId ? attributedStreak + 1 : 1;

            if (implicatedId is not null && attributedStreak >= 2)
            {
                // Same destination killed the session twice in a row: drop it so
                // the healthy outputs survive, and keep going.
                excluded.Add(implicatedId);
                var dropped = active.First(d => d.Id == implicatedId);
                RaiseLog($"destination '{dropped.Name}' failed twice — auto-disabled for this session");
                attributedStreak = 0;
                unattributedStreak = 0;
                lastImplicatedId = null;
                continue;
            }

            lastImplicatedId = implicatedId;
            unattributedStreak = implicatedId is null ? unattributedStreak + 1 : 0;

            if (unattributedStreak >= MaxConsecutiveUnattributedFailures)
            {
                var tail = string.Join(Environment.NewLine, runLog.TakeLast(5));
                SetStatus(new RestreamEngineStatus(
                    RestreamSessionState.Faulted,
                    $"FFmpeg keeps exiting ({RestreamArgumentBuilder.Mask(tail, config.Destinations)})",
                    null,
                    RuntimeFor(config.Destinations, excluded),
                    null));
                return;
            }

            var delay = BackoffMs[Math.Clamp(quickFailureStreak, 1, BackoffMs.Length) - 1];
            SetStatus(new RestreamEngineStatus(
                RestreamSessionState.WaitingForSource,
                $"FFmpeg exited (code {exitCode?.ToString() ?? "?"}) — retrying in {delay / 1000.0:0.#}s…",
                null,
                RuntimeFor(config.Destinations, excluded),
                null));

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    private static bool LooksLikeError(string line) =>
        line.Contains("failed", StringComparison.OrdinalIgnoreCase)
        || line.Contains("error", StringComparison.OrdinalIgnoreCase)
        || line.Contains("refused", StringComparison.OrdinalIgnoreCase)
        || line.Contains("timed out", StringComparison.OrdinalIgnoreCase);

    private static RestreamDestination? FindImplicatedDestination(string line, IReadOnlyList<RestreamDestination> active)
    {
        // Full publish URL first — it embeds the key, so it uniquely names one
        // destination even when several rows share a server (Twitch main+backup).
        foreach (var destination in active)
        {
            if (string.IsNullOrWhiteSpace(destination.StreamKey))
            {
                continue;
            }

            if (line.Contains(RestreamArgumentBuilder.DestinationUrl(destination), StringComparison.OrdinalIgnoreCase))
            {
                return destination;
            }
        }

        // Fallback for error lines that name only the server host.
        foreach (var destination in active)
        {
            if (!string.IsNullOrWhiteSpace(destination.ServerUrl)
                && line.Contains(destination.ServerUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return destination;
            }
        }

        return null;
    }

    private async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                onLine(line);
            }
        }
        catch (Exception)
        {
            // Pipe torn down (process killed / disposed) — the pump's job is done.
        }
    }

    private void SetDestinationError(string destinationId, string? detail)
    {
        var destinations = _status.Destinations.ToList();
        var index = destinations.FindIndex(d => d.Id == destinationId);
        if (index >= 0)
        {
            destinations[index] = destinations[index] with { Health = RestreamDestinationHealth.Error, Detail = detail };
            SetStatus(_status with { Destinations = destinations });
        }
    }

    private IReadOnlyList<RestreamDestinationRuntime> RuntimeFor(
        IReadOnlyList<RestreamDestination> destinations,
        HashSet<string>? excluded)
    {
        var existing = _status.Destinations.ToDictionary(d => d.Id, StringComparer.Ordinal);
        return destinations
            .Where(d => d.Enabled)
            .Select(d =>
            {
                if (excluded is not null && excluded.Contains(d.Id))
                {
                    return new RestreamDestinationRuntime(d.Id, RestreamDestinationHealth.AutoDisabled, "auto-disabled after repeated failures");
                }

                // A destination error stays visible through the wait/backoff
                // window; the Streaming transition (ActiveRuntime) clears it.
                if (existing.TryGetValue(d.Id, out var runtime)
                    && runtime.Health == RestreamDestinationHealth.Error)
                {
                    return runtime;
                }

                return new RestreamDestinationRuntime(d.Id, RestreamDestinationHealth.Pending, null);
            })
            .ToList();
    }

    private IReadOnlyList<RestreamDestinationRuntime> ActiveRuntime(
        IReadOnlyList<RestreamDestination> destinations,
        HashSet<string> excluded)
    {
        return destinations
            .Where(d => d.Enabled)
            .Select(d => excluded.Contains(d.Id)
                ? new RestreamDestinationRuntime(d.Id, RestreamDestinationHealth.AutoDisabled, "auto-disabled after repeated failures")
                : new RestreamDestinationRuntime(d.Id, RestreamDestinationHealth.Active, null))
            .ToList();
    }

    private void SetStatus(RestreamEngineStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void RaiseLog(string line) => LogLine?.Invoke(this, line);

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping the restream engine during dispose");
        }

        _mutex.Dispose();
    }

    /// <summary>Parses FFmpeg's <c>-progress pipe:1</c> key=value blocks into stats snapshots.</summary>
    private sealed class ProgressParser
    {
        private readonly Dictionary<string, string> _current = new(StringComparer.Ordinal);

        /// <summary>Feed one line; returns stats when a block completes ("progress=…").</summary>
        public RestreamStats? Feed(string line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return null;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            _current[key] = value;

            if (key != "progress")
            {
                return null;
            }

            var stats = new RestreamStats(
                ParseDouble(_current.GetValueOrDefault("fps")) ?? 0,
                ParseBitrate(_current.GetValueOrDefault("bitrate")),
                ParseSpeed(_current.GetValueOrDefault("speed")),
                _current.TryGetValue("drop_frames", out var drop) && long.TryParse(drop, out var dropped) ? dropped : 0);

            _current.Clear();
            return stats;
        }

        private static double? ParseDouble(string? text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static double ParseBitrate(string? text)
        {
            // Format: " 6000.1kbits/s" (or "N/A").
            if (string.IsNullOrEmpty(text) || text.Contains("N/A", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var suffix = text.IndexOf("kbits/s", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(suffix > 0 ? text[..suffix] : text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0;
        }

        private static double ParseSpeed(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.Contains("N/A", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return double.TryParse(text.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }
    }
}
