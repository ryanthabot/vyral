using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord.Events;

namespace VyralBot.Integrations.Discord;

/// <summary>
/// Default <see cref="IDiscordBotManager"/>. Owns N independent
/// <see cref="DiscordService"/> instances (one per row in the DiscordBots table),
/// each created with (ILogger&lt;DiscordService&gt;, IDataService, IEventBus) and
/// connected with its own explicitly passed token — never via ReconnectAsync or
/// the service-internal legacy Platform row. All start/stop work is serialized
/// through a semaphore; dictionary access is guarded by a lock.
/// </summary>
/// <remarks>
/// The legacy DI-singleton DiscordService (the setup wizard's "primary"
/// service) is folded into this world via adoption: every connect with the
/// Platform row's token — whether started by this manager's adoption flow or
/// by a legacy path (wizard / Platforms page / dashboard) — ends up owned by
/// the manager (see <see cref="AdoptPlatformTokenAsync"/>), so exactly one
/// gateway session runs per token.
/// </remarks>
/// <remarks>
/// Each DiscordService still persists the legacy shared Platform row Name="Discord"
/// on connect/disconnect (last writer wins). That row remains the CONFIG store
/// for the Home connection; the DiscordBots table is the source of truth for
/// multi-bot tokens/status/selected channel.
/// </remarks>
public class DiscordBotManager : IDiscordBotManager, IDisposable
{
    private const string LegacyPlatformName = "Discord";
    private const string StatusConnected = "Connected";
    private const string StatusDisconnected = "Disconnected";
    private const string StatusError = "Error";

    private sealed class ManagedBot
    {
        public required DiscordBotConfig Config { get; init; }
        public required DiscordService Service { get; init; }
    }

    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<DiscordBotManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    // Serializes Home-connection adoptions (AdoptPlatformTokenAsync). Kept
    // SEPARATE from _operationGate: adoption orchestrates Start/StopBotAsync
    // (which take the operation gate) and must never hold it itself.
    private readonly SemaphoreSlim _adoptionGate = new(1, 1);

    private readonly object _sync = new();
    private readonly Dictionary<string, ManagedBot> _running = new(StringComparer.Ordinal);
    private bool _schemaEnsured;
    private bool _disposed;

    // The bot row (and its token) currently serving "the Discord connection
    // from Home" (legacy Platform row). In-memory on purpose: gateway sessions
    // do not survive a restart, so retirement of a previous Home session is a
    // within-process concern. Guarded by _adoptionGate (written) / _sync-safe
    // reads via the PlatformAdoptedBotId getter.
    private string? _platformAdoptedBotId;
    private string? _platformAdoptedToken;

    // Bus subscription for the per-bot welcome/auto-role-on-join feature.
    // Assigned asynchronously (SubscribeAsync) right after construction.
    private Guid? _memberJoinedSubscriptionId;

    // Scheduled messages: ONE manager-wide timer (self-rearming one-shot, so
    // ticks never overlap) evaluating every running bot's due schedules.
    private readonly System.Threading.Timer? _scheduledMessagesTimer;
    private int _scheduledMessagesTickInProgress;
    private static readonly TimeSpan ScheduledMessagesTickInterval = TimeSpan.FromSeconds(30);

    public event Action? BotsChanged;

    /// <summary>
    /// Raised after a bot's <see cref="DiscordService"/> entered the running
    /// set — including the adopted legacy session. Lets event subscribers (the
    /// workflow bridge) hook the service's interaction events as bots start.
    /// Raised defensively: one throwing subscriber cannot break the others.
    /// </summary>
    public event EventHandler<DiscordService>? ServiceStarted;

    /// <summary>
    /// Raised after a running bot's service left the running set but BEFORE it
    /// is shut down, so event subscribers can unhook cleanly.
    /// </summary>
    public event EventHandler<DiscordService>? ServiceStopped;

    private readonly DiscordService? _legacyService;

    public DiscordBotManager(
        IDataService dataService,
        IEventBus eventBus,
        ILogger<DiscordBotManager> logger,
        ILoggerFactory loggerFactory,
        IDiscordService? legacyService = null)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _legacyService = legacyService as DiscordService;

        if (_legacyService != null)
        {
            // Auto-adoption trigger for the legacy connect paths this manager
            // does not own (setup wizard, Platforms page auto-connect,
            // dashboard refresh): whenever the singleton completes a connect,
            // its session is claimed as a managed bot so the legacy world and
            // the manager world can never diverge.
            _legacyService.BotIdentityDetected += OnLegacyServiceIdentityDetected;
        }

        // Welcome/auto-role on member join: the owning DiscordService of every
        // join publishes DiscordMemberJoinedEvent on the shared bus, so ONE
        // manager-wide subscription dispatches to the right bot. Fire-and-forget
        // init (a constructor cannot await); failures are logged, never thrown.
        _ = SubscribeToMemberJoinsAsync();

        // Recurring announcements: a single 30-second one-shot timer (re-armed
        // after each evaluation) checks every running bot's schedules. Bots'
        // schedules are read fresh from the store, so detail-page saves apply
        // to the very next tick.
        _scheduledMessagesTimer = new System.Threading.Timer(
            _ => _ = RunScheduledMessagesTickAsync(),
            null,
            ScheduledMessagesTickInterval,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Registers the manager's <see cref="DiscordMemberJoinedEvent"/> handler.
    /// </summary>
    private async Task SubscribeToMemberJoinsAsync()
    {
        try
        {
            _memberJoinedSubscriptionId = await _eventBus
                .SubscribeAsync<DiscordMemberJoinedEvent>(OnDiscordMemberJoinedAsync)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Could not subscribe to Discord member-join events — welcome/auto-role disabled");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IDiscordService> Connected
    {
        get
        {
            lock (_sync)
            {
                return _running.Values
                    .Where(b => b.Service.IsConnected)
                    .Select(b => (IDiscordService)b.Service)
                    .ToList();
            }
        }
    }

    /// <inheritdoc />
    public IDiscordService? Primary => Connected.FirstOrDefault();

    /// <inheritdoc />
    public string? PlatformAdoptedBotId
    {
        get
        {
            var id = _platformAdoptedBotId;
            if (id == null)
            {
                return null;
            }

            lock (_sync)
            {
                // Only while the tracked row actually runs: a stale id after a
                // Stop must not make the Home disconnect flow "stop" a ghost.
                return _running.ContainsKey(id) ? id : null;
            }
        }
    }


    private async Task<bool> LegacyTokenMatches(string token)
    {
        try
        {
            var legacy = await _dataService.GetPlatformByNameAsync(LegacyPlatformName).ConfigureAwait(false);
            return legacy != null
                   && !string.IsNullOrWhiteSpace(legacy.AccessToken)
                   && string.Equals(legacy.AccessToken, token, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscordBotConfig>> LoadBotsAsync()
    {
        await EnsureSchemaAsync().ConfigureAwait(false);

        var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
        var bots = rows.Select(ToConfig).ToList();

        // First run: no bots saved yet — migrate the legacy single-bot Platform
        // row into a "Primary" bot so the old setup keeps working.
        if (bots.Count == 0)
        {
            var migrated = await TryMigrateLegacyBotAsync().ConfigureAwait(false);
            if (migrated != null)
            {
                bots.Add(migrated);
            }
        }

        return bots;
    }

    /// <inheritdoc />
    public async Task<bool> AddBotAsync(DiscordBotConfig bot)
    {
        if (bot == null || string.IsNullOrWhiteSpace(bot.Name) || string.IsNullOrWhiteSpace(bot.Token))
        {
            _logger.LogWarning("❌ Cannot add Discord bot: name and token are required");
            return false;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
        if (rows.Any(r => string.Equals(r.Name, bot.Name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("❌ Cannot add Discord bot '{Name}': a bot with this name already exists", bot.Name);
            return false;
        }

        var row = new DiscordBot
        {
            Id = string.IsNullOrWhiteSpace(bot.Id) ? Guid.NewGuid().ToString("N") : bot.Id.Trim(),
            Name = bot.Name.Trim(),
            Token = bot.Token,
            Status = StatusDisconnected,
            SelectedChannelId = bot.SelectedChannelId,
            ApplicationId = bot.ApplicationId,
            BotUsername = bot.BotUsername,
            AvatarUrl = bot.AvatarUrl,
            RoleLinking = bot.RoleLinking,
            RoleGiveaway = bot.RoleGiveaway,
            ModChannelId = bot.ModChannelId,
            ModChannelEnabled = bot.ModChannelEnabled,
            AssignedChannelIds = bot.AssignedChannelIds
        };

        await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
        _logger.LogInformation("✅ Added Discord bot '{Name}' ({BotId})", row.Name, row.Id);
        RaiseBotsChanged();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateBotAsync(DiscordBotConfig bot)
    {
        if (bot == null || string.IsNullOrWhiteSpace(bot.Id))
        {
            return false;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
        var row = rows.FirstOrDefault(r => string.Equals(r.Id, bot.Id, StringComparison.Ordinal));
        if (row == null)
        {
            _logger.LogWarning("❌ Cannot update Discord bot {BotId}: not found", bot.Id);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bot.Name)
            && !string.Equals(bot.Name, row.Name, StringComparison.OrdinalIgnoreCase)
            && rows.Any(r => !ReferenceEquals(r, row) && string.Equals(r.Name, bot.Name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("❌ Cannot rename Discord bot to '{Name}': another bot already uses that name", bot.Name);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bot.Name))
        {
            row.Name = bot.Name.Trim();
        }

        // Null/empty keeps the stored value so rename-only updates (config loaded
        // via `with { Name = ... }` still carries the token, but partial configs
        // must not wipe credentials).
        var tokenChanged = false;
        if (!string.IsNullOrWhiteSpace(bot.Token) && !string.Equals(bot.Token, row.Token, StringComparison.Ordinal))
        {
            row.Token = bot.Token;
            tokenChanged = true;
        }

        var channelChanged = false;
        if (!string.IsNullOrWhiteSpace(bot.SelectedChannelId)
            && !string.Equals(bot.SelectedChannelId, row.SelectedChannelId, StringComparison.Ordinal))
        {
            row.SelectedChannelId = bot.SelectedChannelId;
            channelChanged = true;
        }

        // Per-bot auto-start flips take effect on the next app launch; nothing
        // live to apply here.
        var autoStartChanged = bot.AutoStart != row.AutoStart;
        row.AutoStart = bot.AutoStart;

        await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
        _logger.LogInformation("✅ Updated Discord bot '{Name}' ({BotId})", row.Name, row.Id);

        // Apply live where possible: channel swap on the running instance, token
        // change requires a reconnect. Capture decisions under the lock, then act
        // outside it (the actions are async fire-and-forget).
        DiscordService? channelTarget = null;
        var shouldRestart = false;
        lock (_sync)
        {
            if (_running.TryGetValue(row.Id, out var running))
            {
                if (channelChanged && !string.IsNullOrWhiteSpace(row.SelectedChannelId))
                {
                    channelTarget = running.Service;
                }

                shouldRestart = tokenChanged;
            }
        }

        if (channelTarget != null)
        {
            _ = ApplySelectedChannelAsync(channelTarget, row.SelectedChannelId!);
        }

        if (shouldRestart)
        {
            _logger.LogInformation("🔁 Token changed for '{Name}' — restarting the bot", row.Name);
            _ = RestartAsync(row.Id);
        }

        RaiseBotsChanged();
        return true;
    }

    /// <inheritdoc />
    public async Task RemoveBotAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ManagedBot? running;
            lock (_sync)
            {
                _running.TryGetValue(botId, out running);
                _running.Remove(botId);
            }

            if (running != null)
            {
                // Let lifecycle subscribers unhook BEFORE the service is torn down
                UnhookServiceEvents(running.Service);
                RaiseServiceEvent(ServiceStopped, running.Service);
                await ShutdownBotAsync(running).ConfigureAwait(false);
            }

            await _dataService.DeleteDiscordBotAsync(botId).ConfigureAwait(false);

            await CleanupBotScopedDataAsync(botId).ConfigureAwait(false);

            // If the removed bot was the migrated legacy bot, retire the legacy row
            // too — otherwise the next start-up would migrate/resurrect it.
            try
            {
                var legacy = await _dataService.GetPlatformByNameAsync(LegacyPlatformName).ConfigureAwait(false);
                var removedToken = running?.Config.Token;
                if (legacy != null
                    && !string.IsNullOrWhiteSpace(legacy.AccessToken)
                    && (removedToken == null || string.Equals(legacy.AccessToken, removedToken, StringComparison.Ordinal)))
                {
                    legacy.Status = StatusDisconnected;
                    legacy.AccessToken = null;
                    legacy.UpdatedAt = DateTime.UtcNow;
                    await _dataService.SavePlatformAsync(legacy).ConfigureAwait(false);
                    _logger.LogInformation("🧹 Retired the legacy Discord row alongside the removed bot");
                }
            }
            catch (Exception legacyEx)
            {
                _logger.LogWarning(legacyEx, "⚠️ Could not retire the legacy Discord row during removal");
            }

            _logger.LogInformation("🗑️ Removed Discord bot {BotId}", botId);
        }
        finally
        {
            _operationGate.Release();
        }

        RaiseBotsChanged();
    }

    /// <summary>
    /// Deletes the removed bot's scoped data so it does not linger as orphans:
    /// Command rows pinned to the bot (they can never fire again — no bot row,
    /// no runtime), Command timers pinned to it, its Discord role→tier
    /// mappings, its monitored connected channels and its per-bot Configs rows
    /// ("DiscordBot.{botId}.Integrations" / ".DisabledBuiltIns"). Each group is
    /// best-effort: one failing group is logged and never blocks the others
    /// or the removal itself.
    /// </summary>
    private async Task CleanupBotScopedDataAsync(string botId)
    {
        // Commands pinned to this bot ("this bot only" scope).
        try
        {
            int deleted = 0;
            foreach (Command command in await _dataService.GetCommandsAsync().ConfigureAwait(false))
            {
                if (string.Equals(command.BotId, botId, StringComparison.Ordinal))
                {
                    await _dataService.DeleteCommandAsync(command.Id).ConfigureAwait(false);
                    deleted++;
                }
            }

            if (deleted > 0)
            {
                _logger.LogInformation("🧹 Deleted {Count} command(s) scoped to removed bot {BotId}", deleted, botId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up commands scoped to removed bot {BotId}", botId);
        }

        // Command timers pinned to this bot.
        try
        {
            int deleted = 0;
            foreach (CommandTimer timer in await _dataService.GetCommandTimersAsync().ConfigureAwait(false))
            {
                if (string.Equals(timer.BotId, botId, StringComparison.Ordinal))
                {
                    await _dataService.DeleteCommandTimerAsync(timer.Id).ConfigureAwait(false);
                    deleted++;
                }
            }

            if (deleted > 0)
            {
                _logger.LogInformation("🧹 Deleted {Count} command timer(s) scoped to removed bot {BotId}", deleted, botId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up timers scoped to removed bot {BotId}", botId);
        }

        // Discord role → mod tier mappings authored for this bot.
        try
        {
            var assignments = await _dataService.GetRoleTierAssignmentsAsync(botId).ConfigureAwait(false);
            foreach (var assignment in assignments)
            {
                await _dataService.DeleteRoleTierAssignmentAsync(botId, assignment.DiscordRoleId).ConfigureAwait(false);
            }

            if (assignments.Count > 0)
            {
                _logger.LogInformation("🧹 Deleted {Count} role-tier assignment(s) for removed bot {BotId}", assignments.Count, botId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up role-tier assignments for removed bot {BotId}", botId);
        }

        // Monitored channels tagged to this bot.
        try
        {
            var channels = await _dataService.GetConnectedChannelsAsync(botId).ConfigureAwait(false);
            foreach (var channel in channels)
            {
                await _dataService.DeleteConnectedChannelAsync(botId, channel.ChannelId).ConfigureAwait(false);
            }

            if (channels.Count > 0)
            {
                _logger.LogInformation("🧹 Deleted {Count} connected channel(s) for removed bot {BotId}", channels.Count, botId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up connected channels for removed bot {BotId}", botId);
        }

        // Per-bot Configs rows ("DiscordBot.{botId}.*"): the integrations list
        // and disabled-built-ins written by the bot's integrations page and by
        // flow saves, plus the welcome/auto-role settings, allowed channels
        // and scheduled messages of the detail page. Without this the keys
        // linger as orphans forever.
        try
        {
            var configKeys = new[]
            {
                $"DiscordBot.{botId}.Integrations",
                $"DiscordBot.{botId}.DisabledBuiltIns",
                DiscordBotWelcomeSettings.ConfigKey(botId),
                DiscordBotAllowedChannels.ConfigKey(botId),
                DiscordBotScheduledMessage.ConfigKey(botId)
            };

            int deleted = 0;
            foreach (string key in configKeys)
            {
                if (await _dataService.DeleteConfigValueAsync(key).ConfigureAwait(false))
                {
                    deleted++;
                }
            }

            if (deleted > 0)
            {
                _logger.LogInformation("🧹 Deleted {Count} config key(s) scoped to removed bot {BotId}", deleted, botId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up config keys scoped to removed bot {BotId}", botId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> StartBotAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return false;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
        var row = rows.FirstOrDefault(r => string.Equals(r.Id, botId, StringComparison.Ordinal));
        if (row == null)
        {
            _logger.LogWarning("❌ Cannot start Discord bot {BotId}: not found", botId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(row.Token))
        {
            _logger.LogWarning("❌ Cannot start Discord bot '{Name}': no token saved", row.Name);
            await SetStatusAsync(row, StatusError).ConfigureAwait(false);
            RaiseBotsChanged();
            return false;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return false;
            }

            // Pre-compute the Platform-row token match for the legacy-adoption
            // check inside the lock below: LegacyTokenMatches is a DATABASE
            // round-trip, and blocking on it under the lock
            // (GetAwaiter().GetResult()) stalled every manager dictionary
            // access on I/O.
            var legacyRowTokenMatches = _legacyService is { IsConnected: true }
                && await LegacyTokenMatches(row.Token!).ConfigureAwait(false);

            ManagedBot? evicted = null;
            lock (_sync)
            {
                if (_running.TryGetValue(row.Id, out var existing) && existing.Service.IsConnected)
                {
                    _logger.LogInformation("ℹ️ Discord bot '{Name}' is already connected", row.Name);
                    return true;
                }

                // Evict this bot's own stale entry so it can never match
                // itself in the duplicate-token guard below.
                if (_running.Remove(row.Id, out var stale))
                {
                    evicted = stale;
                }
            }

            if (evicted != null)
            {
                // The evicted service's IsConnected flag is only false while
                // its client is stopped OR mid-auto-reconnect. In the
                // reconnect case an abandoned entry would leave an ORPHANED
                // live gateway: Discord.Net brings the client back by itself,
                // it keeps the token's live-session slot claimed, and the
                // replacement ConnectAsync below then refuses the token — the
                // bot card would show Offline forever while the invisible
                // session keeps publishing events. Tear the stale service
                // down first so exactly one session survives this start.
                UnhookServiceEvents(evicted.Service);
                RaiseServiceEvent(ServiceStopped, evicted.Service);
                await ShutdownBotAsync(evicted).ConfigureAwait(false);
            }

            // Guard against connecting the same token twice inside the manager:
            // two gateway sessions for one token duplicate every incoming event.
            // (A bot never conflicts with itself — its stale entry was evicted above.)
            Task statusUpdateTask = Task.CompletedTask;
            var adoptedLegacy = false;
            lock (_sync)
            {
                var duplicate = _running.Values.FirstOrDefault(b =>
                    b.Config.Id != row.Id
                    && string.Equals(b.Config.Token, row.Token, StringComparison.Ordinal));
                if (duplicate != null)
                {
                    _logger.LogWarning(
                        "⚠️ Discord bot '{Name}' shares a token with running bot '{Other}' — skipping start to avoid duplicate events",
                        row.Name, duplicate.Config.Name);
                    // Report failure so the UI does not claim the bot "is up and
                    // running" while its card stays Offline.
                    return false;
                }

                // Manual START for a bot whose token is served by the not-yet-adopted
                // legacy singleton: adopt that session instead of failing. The LIVE
                // ConnectedToken is the ground truth (the wizard can rewrite the
                // Platform row's AccessToken while the old session is still up);
                // the Platform-row match (pre-computed above, outside the lock)
                // stays as a fallback so a session whose token was persisted
                // normally keeps adopting as before.
                if (_legacyService is { IsConnected: true }
                    && (string.Equals(_legacyService.ConnectedToken?.Trim(), row.Token, StringComparison.Ordinal)
                        || legacyRowTokenMatches))
                {
                    if (!_running.ContainsKey(row.Id))
                    {
                        _legacyService.BotId = row.Id;
                        _running[row.Id] = new ManagedBot { Config = ToConfig(row), Service = _legacyService };
                    }

                    // The adoption check proved the legacy session serves this
                    // row's token (live ConnectedToken or the Platform row),
                    // so this adopted session IS the Home session — track it
                    // so a later token change retires exactly this row.
                    _platformAdoptedBotId = row.Id;
                    _platformAdoptedToken = row.Token;

                    // Identity: subscribe and backfill whatever the adopted
                    // session already detected (its connect predates us).
                    HookServiceEvents(_legacyService);
                    _ = PersistIdentityAsync(row.Id);

                    // Keep the row status honest (the bot IS online). The task
                    // starts here but is awaited outside the lock — C# forbids
                    // await inside a lock statement.
                    statusUpdateTask = UpdateRowStatusAsync(row.Id, StatusConnected);
                    adoptedLegacy = true;
                }
            }

            if (adoptedLegacy)
            {
                await statusUpdateTask.ConfigureAwait(false);
                BotsChanged?.Invoke();
                return true;
            }

            var service = new DiscordService(
                _loggerFactory.CreateLogger<DiscordService>(),
                _dataService,
                _eventBus);

            // Tag the instance with its bot row id BEFORE connecting so every
            // event it publishes (including ones fired during login/READY)
            // carries the correct origin for per-bot reply routing.
            service.BotId = row.Id;

            // Identity persistence: subscribe BEFORE connecting so the
            // BotIdentityDetected raise during ConnectAsync/READY is caught;
            // the subscription also covers later reconnects of this client.
            HookServiceEvents(service);

            _logger.LogInformation("🚀 Starting Discord bot '{Name}' ({BotId})...", row.Name, row.Id);
            var connected = await service.ConnectAsync(row.Token!).ConfigureAwait(false);
            if (!connected)
            {
                UnhookServiceEvents(service);
                service.Dispose();
                await SetStatusAsync(row, StatusError).ConfigureAwait(false);
                _logger.LogError("❌ Discord bot '{Name}' failed to connect", row.Name);
                RaiseBotsChanged();
                return false;
            }

            // ConnectAsync restores the shared legacy Platform row's channel;
            // override it with this bot's own saved selection when present.
            if (!string.IsNullOrWhiteSpace(row.SelectedChannelId))
            {
                await ApplySelectedChannelAsync(service, row.SelectedChannelId).ConfigureAwait(false);
            }

            lock (_sync)
            {
                _running[row.Id] = new ManagedBot { Config = ToConfig(row), Service = service };
            }

            // Notify lifecycle subscribers (outside the lock so a slow
            // subscriber never blocks the manager's dictionary).
            RaiseServiceEvent(ServiceStarted, service);

            // Backfill: the identity event can fire during ConnectAsync — i.e.
            // BEFORE the running entry above existed — so persist whatever the
            // service already detected now that it is in the running set.
            // No-op when nothing was detected or nothing changed.
            _ = PersistIdentityAsync(row.Id);

            await SetStatusAsync(row, StatusConnected).ConfigureAwait(false);
            _logger.LogInformation("✅ Discord bot '{Name}' connected as {Username}", row.Name, service.BotUsername);
            RaiseBotsChanged();
            return true;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopBotAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ManagedBot? running;
            lock (_sync)
            {
                _running.TryGetValue(botId, out running);
                _running.Remove(botId);
            }

            if (running == null)
            {
                // Not running — still normalize the persisted status.
                await UpdateRowStatusAsync(botId, StatusDisconnected).ConfigureAwait(false);
                return;
            }

            // Let lifecycle subscribers unhook BEFORE the service is torn down
            UnhookServiceEvents(running.Service);
            RaiseServiceEvent(ServiceStopped, running.Service);
            await ShutdownBotAsync(running).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }

        RaiseBotsChanged();
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        List<string> ids;
        lock (_sync)
        {
            ids = _running.Keys.ToList();
        }

        foreach (var id in ids)
        {
            try
            {
                await StopBotAsync(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to stop Discord bot {BotId} during StopAll", id);
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAllSavedAsync()
    {
        var bots = await LoadBotsAsync().ConfigureAwait(false);

        // The legacy single-bot auto-connect (MainViewModel -> the DI-singleton
        // DiscordService) may already be serving the migrated token. Connecting
        // it again here would open a second gateway session and duplicate every
        // incoming event, so skip tokens the legacy setup bot already runs.
        string? legacyToken = null;
        var legacyConnected = false;
        try
        {
            var legacy = await _dataService.GetPlatformByNameAsync(LegacyPlatformName).ConfigureAwait(false);
            if (legacy != null)
            {
                legacyToken = legacy.AccessToken;
                legacyConnected = string.Equals(legacy.Status, StatusConnected, StringComparison.OrdinalIgnoreCase)
                                  && !string.IsNullOrWhiteSpace(legacy.AccessToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Could not read the legacy Discord Platform row; skipping the duplicate-token guard");
        }

        // Reconcile the migrated 'Primary' bot with the legacy row: the setup wizard
        // saves re-issued tokens ONLY to the legacy Platform row, so after a Discord
        // "Reset Token" the bot row can hold a stale token while the legacy row is
        // live. The live legacy row is authoritative — sync the bot row to it.
        if (legacyConnected && !string.IsNullOrWhiteSpace(legacyToken))
        {
            var migrated = bots.FirstOrDefault(b => b.Name == "Primary")
                           ?? (bots.Count == 1 ? bots[0] : null);
            if (migrated != null && !string.Equals(migrated.Token, legacyToken, StringComparison.Ordinal))
            {
                try
                {
                    await _dataService.SaveDiscordBotAsync(new DiscordBot
                    {
                        Id = migrated.Id,
                        Name = migrated.Name,
                        Token = legacyToken,
                        Status = StatusConnected,
                        SelectedChannelId = migrated.SelectedChannelId,
                        ApplicationId = migrated.ApplicationId,
                        BotUsername = migrated.BotUsername,
                        AvatarUrl = migrated.AvatarUrl,
                        RoleLinking = migrated.RoleLinking,
                        RoleGiveaway = migrated.RoleGiveaway,
                        ModChannelId = migrated.ModChannelId,
                        ModChannelEnabled = migrated.ModChannelEnabled,
                        AssignedChannelIds = migrated.AssignedChannelIds,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }).ConfigureAwait(false);
                    _logger.LogInformation(
                        "🔄 Synced bot '{Name}' to the re-issued token saved by the setup wizard", migrated.Name);
                    bots = (await LoadBotsAsync().ConfigureAwait(false)).ToList();
                }
                catch (Exception syncEx)
                {
                    _logger.LogWarning(syncEx, "⚠️ Could not sync the re-issued token to the bot row");
                }
            }
        }

        foreach (var bot in bots.Where(b => !string.IsNullOrWhiteSpace(b.Token)))
        {
            var wantsStart = string.Equals(bot.Status, StatusConnected, StringComparison.OrdinalIgnoreCase);
            var isLegacyBot = legacyConnected && string.Equals(bot.Token, legacyToken, StringComparison.Ordinal);

            if (isLegacyBot
                && _legacyService is { IsConnected: true }
                && string.Equals(_legacyService.ConnectedToken?.Trim(), bot.Token, StringComparison.Ordinal))
            {
                // ADOPT the already-running legacy gateway session as this bot's
                // managed instance: one session, correct dashboard status, working
                // START/STOP. Without this, the bot shows offline while running.
                // The ConnectedToken check (not just the Platform row) guarantees
                // the live session REALLY serves this row's token before it is
                // claimed here — a stale-row match would track the wrong session.
                lock (_sync)
                {
                    if (!_running.ContainsKey(bot.Id))
                    {
                        _legacyService.BotId = bot.Id;
                        _running[bot.Id] = new ManagedBot { Config = bot, Service = _legacyService };
                        _logger.LogInformation(
                            "🤝 Adopted the running legacy session as Discord bot '{Name}' ({BotId})",
                            bot.Name, bot.Id);
                    }
                }
                // Keep the row status in sync with reality (the bot IS online)
                try
                {
                    await UpdateRowStatusAsync(bot.Id, StatusConnected).ConfigureAwait(false);
                }
                catch
                {
                    // status sync is best-effort
                }

                // bot.Token == legacyToken (isLegacyBot above): the adopted
                // session is the Home session — track it for later token-change
                // retirement.
                _platformAdoptedBotId = bot.Id;
                _platformAdoptedToken = bot.Token;

                // Identity: subscribe and backfill whatever the adopted session
                // already detected (its connect predates the manager's hook).
                HookServiceEvents(_legacyService);
                _ = PersistIdentityAsync(bot.Id);

                RaiseServiceEvent(ServiceStarted, _legacyService);
                BotsChanged?.Invoke();
                continue;
            }

            if (!wantsStart && !isLegacyBot)
            {
                continue;
            }

            try
            {
                await StartBotAsync(bot.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Auto-start failed for Discord bot '{Name}' ({BotId})", bot.Name, bot.Id);
            }
        }
    }

    /// <inheritdoc />
    public IDiscordService? Get(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return null;
        }

        lock (_sync)
        {
            return _running.TryGetValue(botId, out var bot) ? bot.Service : null;
        }
    }

    // ==================================================================
    // Welcome + auto-role on member join (per bot)
    //
    // Every running DiscordService publishes DiscordMemberJoinedEvent (with
    // its BotId) on the shared EventBus; the manager's single subscription
    // loads the joining bot's settings fresh on EVERY join (no cache to
    // invalidate — saves from the detail page apply immediately) and acts on
    // them through that bot's own service instance.
    // ==================================================================

    /// <inheritdoc />
    public async Task<DiscordBotWelcomeSettings> GetWelcomeSettingsAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return DiscordBotWelcomeSettings.Default;
        }

        try
        {
            var json = await _dataService
                .GetConfigValueAsync(DiscordBotWelcomeSettings.ConfigKey(botId))
                .ConfigureAwait(false);
            return DiscordBotWelcomeSettings.FromJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Could not load welcome settings for bot {BotId}; using defaults", botId);
            return DiscordBotWelcomeSettings.Default;
        }
    }

    /// <inheritdoc />
    public async Task SaveWelcomeSettingsAsync(string botId, DiscordBotWelcomeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(botId) || settings == null)
        {
            return;
        }

        await _dataService
            .SetConfigValueAsync(DiscordBotWelcomeSettings.ConfigKey(botId), settings.ToJson())
            .ConfigureAwait(false);
        _logger.LogInformation("✅ Saved welcome settings for Discord bot {BotId} (enabled: {Enabled})",
            botId, settings.Enabled);
    }

    /// <summary>
    /// Bus handler for member joins: for a bot with the feature enabled, posts
    /// the templated welcome message to the configured channel and grants the
    /// configured role. Settings are only acted on when the join's guild
    /// matches <see cref="DiscordBotWelcomeSettings.GuildId"/> (channel and
    /// role snowflakes are guild-scoped). Every failure is logged and
    /// swallowed — the handler must never crash the bus pipeline.
    /// </summary>
    private async Task OnDiscordMemberJoinedAsync(DiscordMemberJoinedEvent evt)
    {
        try
        {
            if (_disposed || string.IsNullOrWhiteSpace(evt?.BotId))
            {
                return;
            }

            var settings = await GetWelcomeSettingsAsync(evt.BotId!).ConfigureAwait(false);
            if (!settings.Enabled)
            {
                return;
            }

            // Channel/role ids only mean something inside the guild they were
            // picked from; joins of any other guild are left alone.
            if (string.IsNullOrWhiteSpace(settings.GuildId)
                || !string.Equals(settings.GuildId, evt.GuildId, StringComparison.Ordinal))
            {
                return;
            }

            if (Get(evt.BotId!) is not { IsConnected: true } service)
            {
                _logger.LogInformation("ℹ️ Welcome skipped for bot {BotId}: not connected", evt.BotId);
                return;
            }

            // 1) Welcome message (independent of the role grant — one failing
            //    half must not stop the other).
            if (!string.IsNullOrWhiteSpace(settings.Message)
                && !string.IsNullOrWhiteSpace(settings.ChannelId))
            {
                try
                {
                    var text = DiscordBotWelcomeSettings.FormatMessage(
                        settings.Message, evt.Username, evt.DisplayName, evt.UserId, evt.GuildName);
                    var sent = await service
                        .SendToChannelAsync(settings.ChannelId!, text, null, evt.GuildName)
                        .ConfigureAwait(false);
                    if (sent)
                    {
                        _logger.LogInformation("👋 Sent the welcome message for {Username} in {Guild}",
                            evt.Username, evt.GuildName ?? settings.GuildId);
                    }
                    else
                    {
                        _logger.LogInformation("ℹ️ Welcome message for {Username} was not sent (channel '{ChannelId}' unresolvable)",
                            evt.Username, settings.ChannelId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "ℹ️ Welcome message failed for {Username} — continuing to the role grant",
                        evt.Username);
                }
            }

            // 2) Auto-role grant.
            if (!string.IsNullOrWhiteSpace(settings.AutoRoleId)
                && !string.IsNullOrWhiteSpace(evt.UserId)
                && !string.IsNullOrWhiteSpace(evt.GuildId))
            {
                try
                {
                    var result = await service
                        .GrantRoleAsync(evt.GuildId!, evt.UserId!, settings.AutoRoleId!)
                        .ConfigureAwait(false);
                    _logger.LogInformation("🎖️ Auto-role on join for {Username}: {Result}", evt.Username, result);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "ℹ️ Auto-role grant failed for {Username} on join", evt.Username);
                }
            }
        }
        catch (Exception ex)
        {
            // Defensive: the EventBus also guards handlers, but the welcome
            // feature must never surface as a bus error either way.
            _logger.LogWarning(ex, "⚠️ Welcome/auto-role handling failed for a Discord member join");
        }
    }

    // ==================================================================
    // Per-bot ALLOWED CHANNELS + SCHEDULED MESSAGES
    //
    // Both live in the per-bot Configs store ("Discord.{botId}.*" siblings of
    // the welcome settings). Allowed channels are ENFORCED where commands are
    // matched — CommandService reads the same key live (30s cache) — while the
    // scheduled messages are acted on here by the manager-wide timer.
    // ==================================================================

    /// <inheritdoc />
    public async Task<DiscordBotAllowedChannels> GetAllowedChannelsAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return DiscordBotAllowedChannels.Default;
        }

        try
        {
            var json = await _dataService
                .GetConfigValueAsync(DiscordBotAllowedChannels.ConfigKey(botId))
                .ConfigureAwait(false);
            return DiscordBotAllowedChannels.FromJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Could not load allowed channels for bot {BotId}; treating it as unrestricted", botId);
            return DiscordBotAllowedChannels.Default;
        }
    }

    /// <inheritdoc />
    public async Task SaveAllowedChannelsAsync(string botId, DiscordBotAllowedChannels allowed)
    {
        if (string.IsNullOrWhiteSpace(botId) || allowed == null)
        {
            return;
        }

        await _dataService
            .SetConfigValueAsync(DiscordBotAllowedChannels.ConfigKey(botId), allowed.ToJson())
            .ConfigureAwait(false);
        _logger.LogInformation("✅ Saved allowed channels for Discord bot {BotId} ({Count} channel(s) picked)",
            botId, allowed.ChannelIds.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscordBotScheduledMessage>> GetScheduledMessagesAsync(string botId)
    {
        if (string.IsNullOrWhiteSpace(botId))
        {
            return [];
        }

        try
        {
            var json = await _dataService
                .GetConfigValueAsync(DiscordBotScheduledMessage.ConfigKey(botId))
                .ConfigureAwait(false);
            return DiscordBotScheduledMessage.ListFromJson(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Could not load scheduled messages for bot {BotId}; treating it as none", botId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SaveScheduledMessagesAsync(string botId, IReadOnlyList<DiscordBotScheduledMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(botId) || messages == null)
        {
            return;
        }

        // Bounded list + reset every entry's next-run clock: a null NextRunUtc
        // makes the timer schedule the first run one full interval out, so an
        // edit never replays stale due times from before the save.
        var bounded = messages
            .Take(DiscordBotScheduledMessage.MaxPerBot)
            .Select(m => m with { NextRunUtc = null })
            .ToList();

        await _dataService
            .SetConfigValueAsync(DiscordBotScheduledMessage.ConfigKey(botId), DiscordBotScheduledMessage.ListToJson(bounded))
            .ConfigureAwait(false);
        _logger.LogInformation("✅ Saved {Count} scheduled message(s) for Discord bot {BotId}", bounded.Count, botId);
    }

    /// <summary>
    /// One 30-second tick: evaluates every running bot's due schedules.
    /// Overlap-guarded (a single evaluation in flight); re-arms the one-shot
    /// timer in the finally block so a slow tick DELAYS the next instead of
    /// stacking, and stops re-arming once the manager is disposed.
    /// </summary>
    private async Task RunScheduledMessagesTickAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _scheduledMessagesTickInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await EvaluateScheduledMessagesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defensive: the tick runs as a fire-and-forget task and must
            // never surface as an unobserved task exception.
            _logger.LogWarning(ex, "⚠️ Scheduled-message evaluation failed");
        }
        finally
        {
            Interlocked.Exchange(ref _scheduledMessagesTickInProgress, 0);

            if (!_disposed && _scheduledMessagesTimer != null)
            {
                try
                {
                    _scheduledMessagesTimer.Change(ScheduledMessagesTickInterval, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                    // Manager disposed mid-tick — nothing left to re-arm.
                }
            }
        }
    }

    private async Task EvaluateScheduledMessagesAsync()
    {
        List<string> botIds;
        lock (_sync)
        {
            botIds = _running.Keys.ToList();
        }

        foreach (var botId in botIds)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await EvaluateBotScheduledMessagesAsync(botId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // One bot's failure must never stop the other bots' schedules.
                _logger.LogInformation(ex, "ℹ️ Scheduled messages skipped for bot {BotId} this tick", botId);
            }
        }
    }

    /// <summary>
    /// Fires one bot's due schedules through its own service instance. A due
    /// entry fires ONCE and its next run is reset a full interval out — ticks
    /// missed while the bot/app was offline are never caught up. Persistence
    /// happens once per tick, only when a next-run clock actually moved.
    /// </summary>
    private async Task EvaluateBotScheduledMessagesAsync(string botId)
    {
        if (Get(botId) is not { IsConnected: true } service)
        {
            return;
        }

        string? json;
        try
        {
            json = await _dataService
                .GetConfigValueAsync(DiscordBotScheduledMessage.ConfigKey(botId))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "ℹ️ Could not read scheduled messages for bot {BotId} this tick", botId);
            return;
        }

        var schedules = DiscordBotScheduledMessage.ListFromJson(json);
        if (schedules.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var changed = false;

        for (var i = 0; i < schedules.Count; i++)
        {
            var schedule = schedules[i];
            if (!schedule.Enabled
                || string.IsNullOrWhiteSpace(schedule.ChannelId)
                || string.IsNullOrWhiteSpace(schedule.Message))
            {
                continue;
            }

            // Freshly saved entry (NextRunUtc null): schedule its first run
            // one interval out instead of firing the moment it is saved.
            if (schedule.NextRunUtc is not { } dueAt)
            {
                schedules[i] = schedule with { NextRunUtc = now.AddMinutes(schedule.EffectiveIntervalMinutes) };
                changed = true;
                continue;
            }

            if (dueAt > now)
            {
                continue;
            }

            schedules[i] = schedule with { NextRunUtc = now.AddMinutes(schedule.EffectiveIntervalMinutes) };
            changed = true;

            try
            {
                var text = DiscordBotScheduledMessage.FormatMessage(schedule.Message);
                var sent = await service
                    .SendToChannelAsync(schedule.ChannelId!, text, null)
                    .ConfigureAwait(false);
                if (sent)
                {
                    _logger.LogInformation("📅 Sent a scheduled message for bot {BotId} to channel {ChannelId}",
                        botId, schedule.ChannelId);
                }
                else
                {
                    _logger.LogInformation(
                        "ℹ️ Scheduled message for bot {BotId} was not sent (channel '{ChannelId}' unresolvable — deleted?)",
                        botId, schedule.ChannelId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "ℹ️ Scheduled message send failed for bot {BotId} in channel {ChannelId}",
                    botId, schedule.ChannelId);
            }
        }

        if (changed)
        {
            await _dataService
                .SetConfigValueAsync(DiscordBotScheduledMessage.ConfigKey(botId), DiscordBotScheduledMessage.ListToJson(schedules))
                .ConfigureAwait(false);
        }
    }

    // ==================================================================
    // Home-connection adoption (single source of truth)
    //
    // The legacy Platform row (Name="Discord") is the CONFIG store for "the
    // Discord connection from Home"; the RUNNING world is this manager.
    // AdoptPlatformTokenAsync is the one funnel every Home connect goes
    // through — PlatformConnectionService calls it directly, and legacy
    // connect paths that predate the manager (setup wizard, Platforms page
    // auto-connect, dashboard refresh) land here via the legacy singleton's
    // BotIdentityDetected hook. Result: exactly one gateway session per
    // token, every event tagged with a DiscordBots row id.
    // ==================================================================

    /// <inheritdoc />
    public async Task<DiscordAdoptionResult> AdoptPlatformTokenAsync(string platformToken, string? preferredName = null)
    {
        if (string.IsNullOrWhiteSpace(platformToken))
        {
            return new DiscordAdoptionResult(false, Error: "No Discord bot token is saved.");
        }

        platformToken = platformToken.Trim();

        await _adoptionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await EnsureSchemaAsync().ConfigureAwait(false);

            // 1) Token changed since the last adoption: retire the row that
            //    served the PREVIOUS platform token — and only that row.
            //    Unrelated multi-bot rows are never stopped here.
            var previousToken = _platformAdoptedToken;
            var previousBotId = _platformAdoptedBotId;
            if (previousToken != null && previousBotId != null
                && !string.Equals(previousToken, platformToken, StringComparison.Ordinal))
            {
                _platformAdoptedBotId = null;
                _platformAdoptedToken = null;

                var previousRows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
                var previousRow = previousRows.FirstOrDefault(r => string.Equals(r.Id, previousBotId, StringComparison.Ordinal));
                if (previousRow != null
                    && string.Equals(previousRow.Token, previousToken, StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "🔁 The Home Discord token changed — stopping bot '{Name}' which served the previous token",
                        previousRow.Name);
                    await StopBotAsync(previousBotId).ConfigureAwait(false);
                }
            }

            // 2) Point the legacy singleton (the session every legacy UI path
            //    keys off) at the Home token. A session already serving the
            //    token is kept as-is; a stale one is replaced. A failed connect
            //    falls through to a manager-owned session in step 4.
            //
            //    Skip the reconnect entirely when a manager-owned service
            //    already serves this token: a second legacy gateway for the
            //    same token is neither needed nor allowed (DiscordService's
            //    single-session guard would refuse it, and a racing second
            //    IDENTIFY is exactly what kills live sessions with close
            //    4003). The row machinery below adopts the manager session.
            bool managerAlreadyServing;
            lock (_sync)
            {
                managerAlreadyServing = _running.Values.Any(b =>
                    !ReferenceEquals(b.Service, _legacyService)
                    && b.Service.IsConnected
                    && string.Equals(b.Service.ConnectedToken?.Trim(), platformToken, StringComparison.Ordinal));
            }

            if (_legacyService != null && !managerAlreadyServing)
            {
                var legacyServing = _legacyService.IsConnected
                    && string.Equals(_legacyService.ConnectedToken?.Trim(), platformToken, StringComparison.Ordinal);
                if (!legacyServing)
                {
                    if (_legacyService.IsConnected)
                    {
                        // Still serving the previous Home token: retire that
                        // session before opening the new one (two gateways for
                        // one token would duplicate every incoming event).
                        await _legacyService.DisconnectAsync().ConfigureAwait(false);
                    }

                    var legacyConnected = await _legacyService.ConnectAsync(platformToken).ConfigureAwait(false);
                    if (legacyConnected)
                    {
                        _logger.LogInformation(
                            "🤝 Home Discord session is up as {Username} — adopting it as a managed bot",
                            _legacyService.BotUsername ?? "(unknown)");
                    }
                    else
                    {
                        _logger.LogWarning(
                            "⚠️ Home Discord connect failed — falling back to a manager-owned session");
                    }
                }
            }

            // 3) Find or create the DiscordBots row for this token.
            var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
            var row = rows.FirstOrDefault(r => string.Equals(r.Token, platformToken, StringComparison.Ordinal));
            var createdRow = false;
            if (row == null)
            {
                var liveUsername = _legacyService is { IsConnected: true }
                    && string.Equals(_legacyService.ConnectedToken?.Trim(), platformToken, StringComparison.Ordinal)
                        ? (_legacyService.DetectedBotUsername ?? _legacyService.BotUsername)
                        : null;
                var desiredName = !string.IsNullOrWhiteSpace(liveUsername)
                    ? liveUsername
                    : (string.IsNullOrWhiteSpace(preferredName) ? null : preferredName.Trim());

                var config = new DiscordBotConfig(
                    Id: Guid.NewGuid().ToString("N"),
                    Name: UniqueBotName(desiredName, rows),
                    Token: platformToken,
                    Status: StatusDisconnected);

                if (!await AddBotAsync(config).ConfigureAwait(false))
                {
                    return new DiscordAdoptionResult(false, Error: "Could not save the Discord bot row for the Home connection.");
                }

                createdRow = true;
                rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
                row = rows.FirstOrDefault(r => string.Equals(r.Token, platformToken, StringComparison.Ordinal));
                if (row == null)
                {
                    return new DiscordAdoptionResult(false, Error: "The Discord bot row for the Home connection could not be loaded.");
                }

                _logger.LogInformation(
                    "➕ Created Discord bot '{Name}' ({BotId}) for the Home connection",
                    row.Name, row.Id);
            }

            // 4) Ensure the row runs through the manager. StartBotAsync adopts
            //    the legacy singleton's session when it serves this token —
            //    the exact machinery StartAllSavedAsync uses at startup.
            ManagedBot? running;
            lock (_sync)
            {
                _running.TryGetValue(row.Id, out running);
            }

            if (running == null || !running.Service.IsConnected)
            {
                if (!await StartBotAsync(row.Id).ConfigureAwait(false))
                {
                    return new DiscordAdoptionResult(
                        false,
                        row.Id,
                        Error: "Discord bot failed to connect — check the bot token.");
                }
            }

            // 5) Duplicate-session healing: a legacy path connected the
            //    singleton with the Home token while a manager-created service
            //    already runs the same row (two gateways for one token would
            //    duplicate every event). Keep the legacy session — all legacy
            //    UI state keys off it — by re-running the start so the
            //    adoption branch claims the row.
            if (_legacyService is { IsConnected: true }
                && string.Equals(_legacyService.ConnectedToken?.Trim(), platformToken, StringComparison.Ordinal))
            {
                lock (_sync)
                {
                    _running.TryGetValue(row.Id, out running);
                }

                if (running != null && !ReferenceEquals(running.Service, _legacyService))
                {
                    _logger.LogInformation(
                        "♻️ Two live sessions for the Home Discord token — collapsing onto the adopted Home session");
                    await StopBotAsync(row.Id).ConfigureAwait(false);
                    await StartBotAsync(row.Id).ConfigureAwait(false);
                }
            }

            // 6) Post-adoption sync: row status, display name of freshly
            //    created rows, per-bot channel preference, identity backfill,
            //    Home tracking.
            rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
            row = rows.FirstOrDefault(r => string.Equals(r.Id, row.Id, StringComparison.Ordinal)) ?? row;

            await UpdateRowStatusAsync(row.Id, StatusConnected).ConfigureAwait(false);

            if (createdRow)
            {
                // Prefer the detected username as the display name of a freshly
                // created row once the identity is known ("prefer the detected
                // username once connected").
                var adoptedService = Get(row.Id);
                var detected = adoptedService?.DetectedBotUsername;
                if (!string.IsNullOrWhiteSpace(detected)
                    && !string.Equals(detected, row.Name, StringComparison.OrdinalIgnoreCase))
                {
                    row.Name = UniqueBotName(
                        detected,
                        rows.Where(r => !string.Equals(r.Id, row.Id, StringComparison.Ordinal)));
                    await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
                }
            }

            if (!string.IsNullOrWhiteSpace(row.SelectedChannelId))
            {
                lock (_sync)
                {
                    _running.TryGetValue(row.Id, out running);
                }

                if (running != null
                    && !string.Equals(running.Service.SelectedChannelId, row.SelectedChannelId, StringComparison.Ordinal))
                {
                    // The adopted Home session restored the Platform row's
                    // channel; this bot's own saved selection wins.
                    await ApplySelectedChannelAsync(running.Service, row.SelectedChannelId).ConfigureAwait(false);
                }
            }

            _platformAdoptedBotId = row.Id;
            _platformAdoptedToken = platformToken;

            // Identity backfill: an adopted session's connect (and its
            // identity raise) predate the manager's subscription.
            _ = PersistIdentityAsync(row.Id);

            string? botUsername;
            lock (_sync)
            {
                botUsername = _running.TryGetValue(row.Id, out running) ? running.Service.BotUsername : null;
            }
            botUsername ??= row.BotUsername;

            RaiseBotsChanged();

            _logger.LogInformation(
                "✅ Home Discord connection adopted as managed bot '{Name}' ({BotId}) as {Username}",
                row.Name, row.Id, botUsername ?? "(unknown)");

            return new DiscordAdoptionResult(true, row.Id, botUsername);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Adopting the Home Discord connection failed");
            return new DiscordAdoptionResult(false, Error: $"Connecting the Discord bot failed: {ex.Message}");
        }
        finally
        {
            _adoptionGate.Release();
        }
    }

    /// <summary>
    /// Auto-adoption trigger for the legacy connect paths this manager does
    /// not own: the legacy singleton completed a connect, so claim its session
    /// (idempotent — a no-op once the token's row already runs via adoption).
    /// </summary>
    private void OnLegacyServiceIdentityDetected(string botId)
    {
        // Fire-and-forget: the gateway thread must never block on adoption
        // (which connects/stops services and writes the database). The
        // adoption gate serializes with any concurrent adoption.
        _ = AdoptLegacySessionAsync();
    }

    private async Task AdoptLegacySessionAsync()
    {
        try
        {
            if (_legacyService is not { IsConnected: true })
            {
                return;
            }

            // The singleton persists its token to the Platform row on connect,
            // so that row's AccessToken is the token the live session uses.
            var platform = await _dataService.GetPlatformByNameAsync(LegacyPlatformName).ConfigureAwait(false);
            var token = platform?.AccessToken?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            await AdoptPlatformTokenAsync(
                token,
                _legacyService.DetectedBotUsername ?? _legacyService.BotUsername).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Auto-adopting the legacy Discord session failed");
        }
    }

    /// <summary>
    /// First bot-row display name derived from <paramref name="desired"/> that
    /// no existing row uses ("Discord Bot" placeholder when empty), with a
    /// numeric suffix when the base name is taken.
    /// </summary>
    private string UniqueBotName(string? desired, IEnumerable<DiscordBot> existingRows)
    {
        var baseName = string.IsNullOrWhiteSpace(desired) ? "Discord Bot" : desired.Trim();
        var taken = new HashSet<string>(
            existingRows.Select(r => r.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        var suffix = 2;
        while (taken.Contains($"{baseName} {suffix}"))
        {
            suffix++;
        }

        return $"{baseName} {suffix}";
    }

    // ==================================================================
    // Per-bot roles (V3): single-assignment flags on the DiscordBots rows.
    // AssignRoleAsync(assigned: true) gives the role to ONE bot and clears it
    // on every other row; assigned: false clears just that bot;
    // ClearRoleAsync unassigns everywhere. Role resolution is case-sensitive
    // on the DiscordBotRoles constants.
    // ==================================================================

    /// <inheritdoc />
    public async Task<string?> GetBotIdForRoleAsync(string role)
    {
        if (!DiscordBotRoles.All.Contains(role))
        {
            _logger.LogWarning("❌ Unknown Discord bot role '{Role}'", role ?? "(null)");
            return null;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
        return rows.FirstOrDefault(r => HasRoleFlag(r, role))?.Id;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveBotIdForRoleAsync(string role)
    {
        var assigned = await GetBotIdForRoleAsync(role).ConfigureAwait(false);
        return assigned ?? Primary?.BotId;
    }

    /// <inheritdoc />
    public async Task AssignRoleAsync(string botId, string role, bool assigned)
    {
        if (string.IsNullOrWhiteSpace(botId) || !DiscordBotRoles.All.Contains(role))
        {
            _logger.LogWarning("❌ Cannot assign Discord bot role '{Role}' to {BotId}: unknown role or empty bot id",
                role ?? "(null)", botId ?? "(null)");
            return;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        var rows = (await _dataService.GetDiscordBotsAsync().ConfigureAwait(false)).ToList();
        var target = rows.FirstOrDefault(r => string.Equals(r.Id, botId, StringComparison.Ordinal));
        if (target == null)
        {
            _logger.LogWarning("❌ Cannot assign Discord bot role '{Role}': bot {BotId} not found", role, botId);
            return;
        }

        var changed = false;
        foreach (var row in rows)
        {
            // assigned:true touches every row (flag the target, clear the rest);
            // assigned:false touches just the target row.
            var isTarget = string.Equals(row.Id, botId, StringComparison.Ordinal);
            if (!assigned && !isTarget)
            {
                continue;
            }

            if (SetRoleFlag(row, role, assigned && isTarget))
            {
                await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation("✅ {Verb} Discord bot role '{Role}' {Prep} bot '{Name}' ({BotId})",
                assigned ? "Assigned" : "Unassigned", role, assigned ? "to" : "from", target.Name, botId);
            RaiseBotsChanged();
        }
    }

    /// <inheritdoc />
    public async Task ClearRoleAsync(string role)
    {
        if (!DiscordBotRoles.All.Contains(role))
        {
            _logger.LogWarning("❌ Cannot clear unknown Discord bot role '{Role}'", role ?? "(null)");
            return;
        }

        await EnsureSchemaAsync().ConfigureAwait(false);

        var changed = false;
        var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (SetRoleFlag(row, role, false))
            {
                await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation("✅ Cleared Discord bot role '{Role}' from every bot", role);
            RaiseBotsChanged();
        }
    }

    private static bool HasRoleFlag(DiscordBot row, string role) => role switch
    {
        DiscordBotRoles.Linking => row.RoleLinking,
        DiscordBotRoles.Giveaway => row.RoleGiveaway,
        _ => false
    };

    /// <summary>
    /// Writes the role flag when it differs from the current value. Returns
    /// true when the row changed (and therefore needs persisting).
    /// </summary>
    private static bool SetRoleFlag(DiscordBot row, string role, bool value)
    {
        switch (role)
        {
            case DiscordBotRoles.Linking:
                if (row.RoleLinking == value) return false;
                row.RoleLinking = value;
                return true;
            case DiscordBotRoles.Giveaway:
                if (row.RoleGiveaway == value) return false;
                row.RoleGiveaway = value;
                return true;
            default:
                return false;
        }
    }

    // ==================================================================
    // Bot identity persistence (V3): DiscordService raises
    // BotIdentityDetected once per connection; the manager persists ONLY the
    // three identity fields on that bot's row (UpdatedAt is stamped by
    // SaveDiscordBotAsync) — never Name/Token/Status/SelectedChannelId/roles —
    // and raises BotsChanged so open UI refreshes.
    // ==================================================================

    private void HookServiceEvents(DiscordService service)
    {
        // "-=" then "+=" keeps adoption of the same instance (the legacy
        // service can be adopted repeatedly) a single subscription.
        service.BotIdentityDetected -= OnBotIdentityDetected;
        service.BotIdentityDetected += OnBotIdentityDetected;
        service.ConnectionStateChanged -= OnServiceConnectionStateChanged;
        service.ConnectionStateChanged += OnServiceConnectionStateChanged;
    }

    private void UnhookServiceEvents(DiscordService service)
    {
        service.BotIdentityDetected -= OnBotIdentityDetected;
        service.ConnectionStateChanged -= OnServiceConnectionStateChanged;
    }

    /// <summary>
    /// A running bot's live connection flag flipped: gateway drop (network
    /// loss), Discord.Net auto-reconnect, or permanent loss. The row's
    /// persisted Status is deliberately NOT rewritten here (Status is the
    /// auto-restore hint for the next launch), but open UI must see the live
    /// truth immediately — fan out <see cref="BotsChanged"/> so the bots list
    /// and the per-bot dashboard re-read the service's IsConnected flag.
    /// </summary>
    private void OnServiceConnectionStateChanged(bool connected)
    {
        _logger.LogInformation(
            connected
                ? "🔌 Discord gateway connection restored for a running bot"
                : "⚠️ Discord gateway connection lost for a running bot (auto-reconnect pending)");
        RaiseBotsChanged();
    }

    private void OnBotIdentityDetected(string botId)
    {
        // Fire-and-forget: the DB write must not block the gateway thread and
        // an Action<string> handler cannot await. PersistIdentityAsync never
        // throws (all failures are logged inside).
        _ = PersistIdentityAsync(botId);
    }

    /// <summary>
    /// Persists the detected identity of a RUNNING bot onto its row. Loads the
    /// row fresh from the database, mutates only ApplicationId/BotUsername/
    /// AvatarUrl (UpdatedAt is stamped by SaveDiscordBotAsync) and raises
    /// <see cref="BotsChanged"/> when anything changed. Safe to call
    /// redundantly — a no-op when nothing was detected or nothing changed.
    /// </summary>
    private async Task PersistIdentityAsync(string botId)
    {
        try
        {
            IDiscordService? service;
            lock (_sync)
            {
                service = _running.TryGetValue(botId, out var running) ? running.Service : null;
            }

            if (service == null)
            {
                // The bot left the running set before the event was handled.
                return;
            }

            var username = service.DetectedBotUsername;
            var applicationId = service.DetectedApplicationId;
            var avatarUrl = service.DetectedAvatarUrl;
            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(applicationId) && string.IsNullOrEmpty(avatarUrl))
            {
                return; // nothing detected yet
            }

            await EnsureSchemaAsync().ConfigureAwait(false);

            var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
            var row = rows.FirstOrDefault(r => string.Equals(r.Id, botId, StringComparison.Ordinal));
            if (row == null)
            {
                return;
            }

            var changed = false;
            if (!string.Equals(row.ApplicationId, applicationId, StringComparison.Ordinal))
            {
                row.ApplicationId = applicationId;
                changed = true;
            }
            if (!string.Equals(row.BotUsername, username, StringComparison.Ordinal))
            {
                row.BotUsername = username;
                changed = true;
            }
            if (!string.Equals(row.AvatarUrl, avatarUrl, StringComparison.Ordinal))
            {
                row.AvatarUrl = avatarUrl;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
            _logger.LogInformation(
                "🪪 Persisted auto-detected identity for Discord bot '{Name}' ({BotId}): {Username} / {ApplicationId}",
                row.Name, botId, username ?? "(none)", applicationId ?? "(none)");
            RaiseBotsChanged();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to persist the detected identity for Discord bot {BotId}", botId);
        }
    }

    private async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
        {
            return;
        }

        // Idempotent (CREATE TABLE IF NOT EXISTS); creates the DiscordBots table
        // and the Commands.BotId column on first use.
        await _dataService.EnsureDatabaseCreatedAsync().ConfigureAwait(false);
        _schemaEnsured = true;
    }

    private async Task<DiscordBotConfig?> TryMigrateLegacyBotAsync()
    {
        try
        {
            var legacy = await _dataService.GetPlatformByNameAsync(LegacyPlatformName).ConfigureAwait(false);
            if (legacy == null || string.IsNullOrWhiteSpace(legacy.AccessToken))
            {
                return null;
            }

            var row = new DiscordBot
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Primary",
                Token = legacy.AccessToken,
                Status = string.Equals(legacy.Status, StatusConnected, StringComparison.OrdinalIgnoreCase)
                    ? StatusConnected
                    : StatusDisconnected,
                SelectedChannelId = legacy.ChannelId
            };

            await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
            _logger.LogInformation(
                "🔄 Migrated the legacy Discord Platform row into multi-bot 'Primary' (username: {Username})",
                legacy.Username ?? "(unknown)");
            return ToConfig(row);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Legacy Discord bot migration failed; starting with an empty bot list");
            return null;
        }
    }

    private async Task ApplySelectedChannelAsync(DiscordService service, string channelId)
    {
        try
        {
            if (!string.Equals(service.SelectedChannelId, channelId, StringComparison.Ordinal))
            {
                await service.SetSelectedChannelAsync(channelId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to apply selected channel {ChannelId}", channelId);
        }
    }

    private async Task ShutdownBotAsync(ManagedBot bot)
    {
        try
        {
            await bot.Service.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Disconnect failed for Discord bot '{Name}'", bot.Config.Name);
        }

        if (ReferenceEquals(bot.Service, _legacyService))
        {
            // The adopted Home session is the long-lived DI singleton that the
            // setup wizard and every legacy connect path reuse. DiscordService.
            // Dispose permanently kills its connection lock, which would turn
            // any later Home connect into an ObjectDisposedException — so only
            // disconnect it and restore the wizard's default BotId tag.
            bot.Service.BotId = "primary";
        }
        else
        {
            try
            {
                bot.Service.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Dispose failed for Discord bot '{Name}'", bot.Config.Name);
            }
        }

        await UpdateRowStatusAsync(bot.Config.Id, StatusDisconnected).ConfigureAwait(false);
    }

    private async Task RestartAsync(string botId)
    {
        try
        {
            await StopBotAsync(botId).ConfigureAwait(false);
            await StartBotAsync(botId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Restart failed for Discord bot {BotId}", botId);
        }
    }

    private async Task SetStatusAsync(DiscordBot row, string status)
    {
        row.Status = status;
        try
        {
            await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to persist status '{Status}' for Discord bot '{Name}'", status, row.Name);
        }
    }

    private async Task UpdateRowStatusAsync(string botId, string status)
    {
        try
        {
            var rows = await _dataService.GetDiscordBotsAsync().ConfigureAwait(false);
            var row = rows.FirstOrDefault(r => string.Equals(r.Id, botId, StringComparison.Ordinal));
            if (row != null)
            {
                row.Status = status;
                await _dataService.SaveDiscordBotAsync(row).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to persist status '{Status}' for Discord bot {BotId}", status, botId);
        }
    }

    private static DiscordBotConfig ToConfig(DiscordBot row) => new(
        row.Id,
        row.Name,
        row.Token,
        row.Status,
        row.SelectedChannelId,
        row.ApplicationId,
        row.BotUsername,
        row.AvatarUrl,
        row.RoleLinking,
        row.RoleGiveaway,
        row.ModChannelId,
        row.ModChannelEnabled,
        row.AssignedChannelIds)
    {
        AutoStart = row.AutoStart,
    };

    /// <summary>
    /// Raises <see cref="BotsChanged"/> defensively: one throwing subscriber must
    /// not break the others or the manager. Handlers run on the caller's thread.
    /// </summary>
    private void RaiseBotsChanged()
    {
        var handlers = BotsChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ BotsChanged subscriber threw an exception");
            }
        }
    }

    /// <summary>
    /// Raises a service lifecycle event (<see cref="ServiceStarted"/> /
    /// <see cref="ServiceStopped"/>) defensively: one throwing subscriber must
    /// not break the others or the manager. Handlers run on the caller's thread.
    /// </summary>
    private void RaiseServiceEvent(EventHandler<DiscordService>? handlers, DiscordService? service)
    {
        if (handlers == null || service == null)
        {
            return;
        }

        foreach (EventHandler<DiscordService> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, service);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Discord service lifecycle subscriber threw an exception");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop the scheduled-message tick FIRST so a callback can never race
        // the service teardown below (the tick's overlap guard + _disposed
        // check make a mid-flight tick a safe no-op after this point).
        _scheduledMessagesTimer?.Dispose();

        List<ManagedBot> bots;
        lock (_sync)
        {
            bots = _running.Values.ToList();
            _running.Clear();
        }

        foreach (var bot in bots)
        {
            // Unhook identity persistence BEFORE disposing the service so a
            // late gateway event cannot reach a disposed manager path.
            UnhookServiceEvents(bot.Service);

            if (ReferenceEquals(bot.Service, _legacyService))
            {
                // The DI container owns the legacy singleton and disposes it at
                // shutdown itself; the manager must never Dispose it (mirrors
                // the ShutdownBotAsync guard that keeps the wizard service
                // reusable).
                continue;
            }

            try
            {
                // DiscordService.Dispose tears the gateway client down synchronously.
                bot.Service.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Dispose failed for Discord bot '{Name}'", bot.Config.Name);
            }
        }

        // The legacy singleton may still be subscribed if it was adopted and
        // the manager is being torn down without a StopBot first ("-=" on a
        // non-subscribed handler is a no-op).
        if (_legacyService != null)
        {
            UnhookServiceEvents(_legacyService);
            _legacyService.BotIdentityDetected -= OnLegacyServiceIdentityDetected;
        }

        // Drop the member-join bus subscription so a disposed manager can
        // never receive another welcome/auto-role dispatch.
        if (_memberJoinedSubscriptionId is { } subscriptionId)
        {
            _memberJoinedSubscriptionId = null;
            try
            {
                _eventBus.Unsubscribe(subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Could not unsubscribe from Discord member-join events");
            }
        }

        // Note: _operationGate is intentionally NOT disposed — an in-flight
        // start/stop would then throw ObjectDisposedException in its finally block.
        _logger.LogInformation("🧹 DiscordBotManager disposed ({BotCount} bot(s) shut down)", bots.Count);
    }
}
