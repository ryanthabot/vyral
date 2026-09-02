using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;

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
/// Each DiscordService still persists the legacy shared Platform row Name="Discord"
/// on connect/disconnect (last writer wins). That row is intentionally ignored by
/// the manager — the DiscordBots table is the source of truth for multi-bot
/// tokens/status/selected channel.
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
    private readonly object _sync = new();
    private readonly Dictionary<string, ManagedBot> _running = new(StringComparer.Ordinal);
    private bool _schemaEnsured;
    private bool _disposed;

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
            SelectedChannelId = bot.SelectedChannelId
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
                RaiseServiceEvent(ServiceStopped, running.Service);
                await ShutdownBotAsync(running).ConfigureAwait(false);
            }

            await _dataService.DeleteDiscordBotAsync(botId).ConfigureAwait(false);

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

            lock (_sync)
            {
                if (_running.TryGetValue(row.Id, out var existing) && existing.Service.IsConnected)
                {
                    _logger.LogInformation("ℹ️ Discord bot '{Name}' is already connected", row.Name);
                    return true;
                }

                // Evict this bot's own stale (stopped) entry so it can never match
                // itself in the duplicate-token guard below.
                _running.Remove(row.Id);
            }

            // Guard against connecting the same token twice inside the manager:
            // two gateway sessions for one token duplicate every incoming event.
            // (A bot never conflicts with itself — its stale entry was evicted above.)
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
                    return true;
                }

                // Manual START for a bot whose token is served by the not-yet-adopted
                // legacy singleton: adopt that session instead of failing.
                if (_legacyService is { IsConnected: true }
                    && LegacyTokenMatches(row.Token).GetAwaiter().GetResult())
                {
                    lock (_sync)
                    {
                        if (!_running.ContainsKey(row.Id))
                        {
                            _legacyService.BotId = row.Id;
                            _running[row.Id] = new ManagedBot { Config = ToConfig(row), Service = _legacyService };
                        }
                    }
                    RaiseServiceEvent(ServiceStarted, _legacyService);
                    BotsChanged?.Invoke();
                    return true;
                }
            }

            var service = new DiscordService(
                _loggerFactory.CreateLogger<DiscordService>(),
                _dataService,
                _eventBus);

            // Tag the instance with its bot row id BEFORE connecting so every
            // event it publishes (including ones fired during login/READY)
            // carries the correct origin for per-bot reply routing.
            service.BotId = row.Id;

            _logger.LogInformation("🚀 Starting Discord bot '{Name}' ({BotId})...", row.Name, row.Id);
            var connected = await service.ConnectAsync(row.Token!).ConfigureAwait(false);
            if (!connected)
            {
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

            if (isLegacyBot && _legacyService is { IsConnected: true })
            {
                // ADOPT the already-running legacy gateway session as this bot's
                // managed instance: one session, correct dashboard status, working
                // START/STOP. Without this, the bot shows offline while running.
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

        try
        {
            bot.Service.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Dispose failed for Discord bot '{Name}'", bot.Config.Name);
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
        row.SelectedChannelId);

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

        List<ManagedBot> bots;
        lock (_sync)
        {
            bots = _running.Values.ToList();
            _running.Clear();
        }

        foreach (var bot in bots)
        {
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

        // Note: _operationGate is intentionally NOT disposed — an in-flight
        // start/stop would then throw ObjectDisposedException in its finally block.
        _logger.LogInformation("🧹 DiscordBotManager disposed ({BotCount} bot(s) shut down)", bots.Count);
    }
}
