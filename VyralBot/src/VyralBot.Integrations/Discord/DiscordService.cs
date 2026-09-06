using System.Collections.Concurrent;
using System.Globalization;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord.Events;
using VyralBot.Workflows.Models;

namespace VyralBot.Integrations.Discord;

/// <summary>
/// Discord.Net-based chat service. Connects a bot account over the Discord
/// gateway, mirrors guild/channel state into <see cref="ConnectedGuilds"/>,
/// publishes chat events to the EventBus and sends messages to a selected
/// channel. Persistence (token, selected channel, status) goes through the
/// Platform database row with Name="Discord".
/// </summary>
public class DiscordService : IDiscordService, IDisposable
{
    private const string PlatformName = "Discord";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<DiscordService> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly object _stateSync = new();

    // Process-wide registry of LIVE gateway sessions, keyed by bot token
    // (trimmed, ordinal). Discord invalidates a session the moment a SECOND
    // gateway IDENTIFYs with the same token — the older session is killed with
    // close 4003 "Not authenticated", which surfaced as "random" disconnects
    // ~a minute after startup. At most one DiscordService may therefore hold a
    // gateway per token, no matter who starts it (DiscordBotManager, setup
    // wizard, Discord dashboard, ...): ConnectAsync refuses a second session
    // for an already-live token. Registration happens right after a successful
    // LoginAsync (BEFORE the gateway IDENTIFY in StartAsync) and is released
    // on DisconnectAsync, failed connects and Dispose.
    private static readonly ConcurrentDictionary<string, DiscordService> LiveTokenSessions =
        new(StringComparer.Ordinal);

    private DiscordSocketClient? _client;

    // Client that was stopped + logged out by DisconnectAsync and is awaiting
    // disposal on the next ConnectAsync (or on Dispose). It is NEVER disposed
    // at disconnect time: disposing a live client while gateway work and other
    // components' state queries are still in flight abruptly killed the app.
    private DiscordSocketClient? _stoppedClient;

    private Platform? _platform;
    private string? _selectedChannelId;
    private string? _botUsername;
    private string? _connectedToken;
    private bool _isConnected;

    // Auto-detected identity (gateway READY, V3): retained after disconnect so
    // the manager can re-read the last known values for persistence/backfill.
    private string? _detectedBotUsername;
    private string? _detectedApplicationId;
    private string? _detectedAvatarUrl;

    /// <summary>The client whose identity already raised <see cref="BotIdentityDetected"/> (duplicate-raise guard).</summary>
    private DiscordSocketClient? _identityRaisedFor;

    private IReadOnlyList<DiscordGuildInfo> _connectedGuilds = Array.Empty<DiscordGuildInfo>();
    private bool _disposed;

    // --- Bot detection & command capture state ---
    // (fed by the raw gateway message handler BEFORE its MessageSource.User
    // filter; see the "Bot detection & command capture" region near the bottom)

    /// <summary>
    /// One entry per distinct external bot author observed on the gateway,
    /// keyed by Discord user id. Live messages are the primary source; the
    /// guild member cache seeds it best-effort (partial, because the client
    /// runs without the GuildMembers intent).
    /// </summary>
    private readonly ConcurrentDictionary<ulong, DiscordSeenBot> _seenBots = new();

    /// <summary>
    /// In-flight <see cref="SendAndCaptureReplyAsync"/> waits, keyed by the
    /// target bot's user id. At most one per author; a newer call completes
    /// the previous wait with null and takes over.
    /// </summary>
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<string>> _pendingCaptures = new();

    /// <summary>Serializes registration/removal/takeover of pending captures.</summary>
    private readonly object _captureSync = new();

    /// <summary>Serializes the once-per-client seeding of <see cref="_seenBots"/>.</summary>
    private readonly object _seenBotsSeedSync = new();

    /// <summary>The client instance whose guild cache already seeded <see cref="_seenBots"/>.</summary>
    private DiscordSocketClient? _seenBotsSeededFrom;

    // --- Workflow execution surface (node editor) state ---

    /// <summary>
    /// Channel id → id of this bot's most recent successfully sent message
    /// there (single entry per channel, always pruned to the latest). Powers
    /// the workflow "last_bot" scope (edit / react / thread / delete).
    /// </summary>
    private readonly ConcurrentDictionary<ulong, ulong> _lastSentByChannel = new();

    /// <summary>
    /// Most recent message/interaction context this service saw ("trigger"
    /// scope source). Updated on every non-self gateway message and on every
    /// workflow slash/component interaction (interactions win because they
    /// arrive after the message that started the run). Guarded by _stateSync.
    /// </summary>
    private WorkflowOrigin? _workflowOrigin;

    /// <summary>Registered workflow slash commands: normalized name → description (re-registered on reconnect).</summary>
    private readonly ConcurrentDictionary<string, string> _workflowSlashCommands = new(StringComparer.Ordinal);

    /// <summary>
    /// Typed options per registered workflow slash command: normalized command
    /// name → authored parameters (entry absent/null = the legacy "args" text
    /// option). Kept alongside <see cref="_workflowSlashCommands"/> so the
    /// Ready/GuildAvailable re-registration restores the typed options.
    /// </summary>
    private readonly ConcurrentDictionary<string, IReadOnlyList<WorkflowParameter>?> _workflowSlashCommandOptions = new(StringComparer.Ordinal);

    /// <summary>Guilds whose workflow slash commands were synced this session (cleared on Ready/disconnect).</summary>
    private readonly ConcurrentDictionary<ulong, byte> _workflowSlashSyncedGuilds = new();

    /// <summary>Namespace prefix for workflow component custom ids ("vyralwf:{workflowId}(:{extra})?").</summary>
    internal const string WorkflowComponentPrefix = "vyralwf:";

    /// <inheritdoc />
    public event EventHandler<DiscordSlashInvocation>? SlashCommandInvoked;

    /// <inheritdoc />
    public event EventHandler<DiscordComponentInteraction>? ComponentInteractionInvoked;

    /// <inheritdoc />
    public event Action<string>? BotIdentityDetected;

    /// <summary>
    /// Identity of this bot instance: the DiscordBotManager assigns each managed
    /// instance its DiscordBots row id right after construction; the setup
    /// wizard's DI-singleton keeps the "primary" default. Published on
    /// <see cref="DiscordChatMessageEvent"/> so replies route back to the bot
    /// that received the message.
    /// </summary>
    public string BotId { get; set; } = "primary";

    public bool IsConnected
    {
        get { lock (_stateSync) { return _isConnected; } }
    }

    public string? BotUsername
    {
        get { lock (_stateSync) { return _botUsername; } }
    }

    /// <summary>
    /// Token of the session this service is currently connected with, null
    /// when disconnected. Exposed for the bot manager's Home-connection
    /// adoption: it must know WHICH token the legacy DI-singleton serves
    /// without trusting the Platform row (the wizard can rewrite that row's
    /// AccessToken while an old session is still live).
    /// </summary>
    public string? ConnectedToken
    {
        get { lock (_stateSync) { return _connectedToken; } }
    }

    /// <inheritdoc />
    public string? DetectedBotUsername
    {
        get { lock (_stateSync) { return _detectedBotUsername; } }
    }

    /// <inheritdoc />
    public string? DetectedApplicationId
    {
        get { lock (_stateSync) { return _detectedApplicationId; } }
    }

    /// <inheritdoc />
    public string? DetectedAvatarUrl
    {
        get { lock (_stateSync) { return _detectedAvatarUrl; } }
    }

    public string? SelectedChannelId
    {
        get { lock (_stateSync) { return _selectedChannelId; } }
    }

    public IReadOnlyList<DiscordGuildInfo> ConnectedGuilds
    {
        get { lock (_stateSync) { return _connectedGuilds; } }
    }

    public DiscordService(
        ILogger<DiscordService> logger,
        IDataService dataService,
        IEventBus eventBus)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    /// <inheritdoc />
    /// <summary>Fetches the bot's username directly from the Discord API (login-only
    /// clients don't populate CurrentUser in this Discord.Net version).</summary>
    private static async Task<string?> FetchBotUsernameAsync(string botToken)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", botToken.Trim());
            var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("username", out var un) ? un.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ValidateTokenAsync(string botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning("❌ Discord token validation failed: token is empty");
            return false;
        }

        try
        {
            // Direct REST validation against Discord's own API — deliberately NOT
            // Discord.Net: its REST client's user state is uninitialized in
            // login-only mode (NullReferenceException from GetCurrentUserAsync).
            // GET /users/@me with the bot token: 200 = valid, 401 = invalid.
            using var http = new System.Net.Http.HttpClient();
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", botToken.Trim());

            var response = await http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var user = System.Text.Json.JsonDocument.Parse(payload);
                var username = user.RootElement.TryGetProperty("username", out var un) ? un.GetString() : null;
                var userId = user.RootElement.TryGetProperty("id", out var uid) ? uid.GetString() : null;
                _logger.LogInformation("✅ Discord bot token is valid for {Username} (id {UserId})", username, userId);
                return true;
            }

            _logger.LogWarning("❌ Discord rejected the bot token: HTTP {Status} {Reason}",
                (int)response.StatusCode, response.ReasonPhrase);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Discord token validation failed");
            return false;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// True when <paramref name="owner"/> still holds (or is re-establishing) a
    /// gateway session: either the service-level connected flag is up, or the
    /// underlying client is connected/connecting. The client check covers the
    /// brief window of a Discord.Net auto-reconnect, where <see cref="IsConnected"/>
    /// is already false again but the session comes right back — during that
    /// window a second connect with the same token must still be refused.
    /// </summary>
    private static bool OwnsLiveGateway(DiscordService owner)
    {
        if (owner.IsConnected)
        {
            return true;
        }

        var client = owner._client;
        return client != null
               && (client.ConnectionState == ConnectionState.Connected
                   || client.ConnectionState == ConnectionState.Connecting);
    }

    /// <summary>
    /// Releases this instance's live-session registration for a token. No-op
    /// unless THIS instance owns the entry (never removes a slot held by a
    /// live session elsewhere).
    /// </summary>
    private void ReleaseLiveTokenSession(string? tokenKey)
    {
        if (string.IsNullOrEmpty(tokenKey))
        {
            return;
        }

        if (LiveTokenSessions.TryGetValue(tokenKey, out var owner) && ReferenceEquals(owner, this))
        {
            LiveTokenSessions.TryRemove(tokenKey, out _);
        }
    }

    public async Task<bool> ConnectAsync(string botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogError("❌ Cannot connect to Discord: bot token is empty");
            return false;
        }

        string? tokenKey = null;

        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Replace any stale client left over from a previous disconnect:
            // it is already stopped and logged out, so disposing it now is safe.
            // The live client (_client) is never disposed on this path.
            var staleClient = _stoppedClient;
            if (staleClient != null)
            {
                _stoppedClient = null;
                await TryDisposeClientAsync(staleClient).ConfigureAwait(false);
            }

            if (IsConnected)
            {
                _logger.LogInformation("ℹ️ Discord client is already connected as {Username}", BotUsername);
                return true;
            }

            // Single-session-per-token guard: when ANOTHER service instance
            // already holds a live gateway for this token, refuse to open a
            // second one. Discord kills the OLDER session with close 4003
            // ("Not authenticated") the instant the second IDENTIFY lands, so
            // the racing session used to look like a random disconnect. The
            // DiscordBotManager's adoption machinery routes UI/Home connects
            // through the live session instead of duplicating it.
            tokenKey = botToken.Trim();
            if (LiveTokenSessions.TryGetValue(tokenKey, out var liveOwner)
                && !ReferenceEquals(liveOwner, this)
                && OwnsLiveGateway(liveOwner))
            {
                _logger.LogWarning(
                    "🚫 Refusing to open a second Discord gateway: this bot token is already live on another session (bot '{Username}'). Stop that bot first or reconnect it through the bot manager.",
                    liveOwner.BotUsername ?? "(unknown)");
                return false;
            }

            _logger.LogInformation("🚀 Connecting to Discord...");

            // Load (or create) the platform row and restore the saved channel selection
            _platform = await _dataService.GetPlatformByNameAsync(PlatformName).ConfigureAwait(false);
            if (_platform == null)
            {
                _platform = new Platform { Name = PlatformName, Enabled = true };
                _logger.LogInformation("🆕 No Discord platform row found in database; a new one will be created");
            }

            lock (_stateSync)
            {
                _selectedChannelId = _platform.ChannelId;
            }

            var client = CreateClient();
            HookEvents(client);

            // Promote immediately so gateway events fired during login/start and
            // any failure path below can reach the client for cleanup
            _client = client;

            // One-shot Ready waiter so ConnectAsync only returns once guild data is on its way
            var readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task> readyHandler = () =>
            {
                readyTcs.TrySetResult();
                return Task.CompletedTask;
            };
            client.Ready += readyHandler;

            try
            {
                await client.LoginAsync(TokenType.Bot, botToken).ConfigureAwait(false);

                // Claim the token's gateway slot BEFORE the IDENTIFY (StartAsync)
                // so a concurrent second connect with the same token cannot slip
                // past the single-session guard above.
                LiveTokenSessions[tokenKey] = this;

                // CurrentUser is available right after login; capture it before the
                // gateway connect so early connection events carry the username
                lock (_stateSync)
                {
                    _botUsername = client.CurrentUser?.Username;
                }

                await client.StartAsync().ConfigureAwait(false);

                var completed = await Task.WhenAny(readyTcs.Task, Task.Delay(ReadyTimeout)).ConfigureAwait(false);
                if (completed != readyTcs.Task)
                {
                    // Not fatal: Discord.Net keeps the session alive and Ready will fire later
                    _logger.LogWarning("⏳ Timed out waiting for the Discord gateway READY payload; continuing connect");
                }

                // Re-capture after READY: CurrentUser is NOT populated by LoginAsync
                // alone in this Discord.Net version (same root cause as the old
                // token-validation bug). Fall back to a direct REST fetch if needed.
                var username = client.CurrentUser?.Username;
                if (string.IsNullOrEmpty(username))
                {
                    username = await FetchBotUsernameAsync(botToken).ConfigureAwait(false);
                }

                lock (_stateSync)
                {
                    if (!string.IsNullOrEmpty(username))
                    {
                        _botUsername = username;
                    }

                    // Remember which token the live session was authenticated
                    // with (see ConnectedToken) — the manager's adoption logic
                    // compares it against the Platform row's current token.
                    _connectedToken = botToken;
                }
            }
            finally
            {
                client.Ready -= readyHandler;
            }

            RefreshGuildSnapshot();
            SetConnectionState(connected: true);

            // Persist token + connected status
            _platform.AccessToken = botToken;
            _platform.Username = client.CurrentUser?.Username;
            _platform.UserId = client.CurrentUser?.Id.ToString();
            _platform.Status = "Connected";
            _platform.ConnectedAt = DateTime.UtcNow;
            _platform.UpdatedAt = DateTime.UtcNow;
            _platform.LastError = null;
            await _dataService.SavePlatformAsync(_platform).ConfigureAwait(false);

            // V3: capture the gateway-verified identity (username / application
            // id / avatar) and notify the bot manager so it persists it on the
            // bot's DiscordBots row. Best-effort — never fails the connect.
            CaptureBotIdentity(client);

            _logger.LogInformation("✅ Connected to Discord as {Username} ({GuildCount} guild(s))",
                BotUsername, ConnectedGuilds.Count);

            return true;
        }
        catch (Exception ex)
        {
            // The token slot may already be claimed (the failure can happen
            // after LoginAsync) — release it so the token can be retried.
            ReleaseLiveTokenSession(tokenKey);

            _logger.LogError(ex, "❌ Failed to connect to Discord");

            // Tear down the partially connected client — unhook its handlers
            // first so the stop/logout inside disposal cannot reenter them
            var partialClient = _client;
            _client = null;
            if (partialClient != null)
            {
                UnhookEvents(partialClient);
            }

            await TryDisposeClientAsync(partialClient).ConfigureAwait(false);
            SetConnectionState(connected: false);

            try
            {
                if (_platform != null)
                {
                    _platform.Status = "Error";
                    _platform.LastError = ex.Message;
                    _platform.UpdatedAt = DateTime.UtcNow;
                    await _dataService.SavePlatformAsync(_platform).ConfigureAwait(false);
                }
            }
            catch (Exception persistEx)
            {
                _logger.LogWarning(persistEx, "⚠️ Failed to persist Discord error status");
            }

            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Captures the gateway-verified identity (username, application/user
    /// snowflake id, PNG avatar URL) into the <c>Detected*</c> properties and
    /// raises <see cref="BotIdentityDetected"/> with this instance's
    /// <see cref="BotId"/>. Called from the connect path right after READY was
    /// observed (CurrentUser is only fully populated then). Raised once per
    /// connection: duplicate raises for the same client are suppressed.
    /// Subscriber exceptions and capture failures are swallowed — identity
    /// detection must never fail (or slow down) the connect.
    /// </summary>
    private void CaptureBotIdentity(DiscordSocketClient client)
    {
        try
        {
            var user = client.CurrentUser;
            if (user == null)
            {
                return;
            }

            string? username;
            string? applicationId;
            lock (_stateSync)
            {
                _detectedBotUsername = user.Username;
                _detectedApplicationId = user.Id.ToString();
                _detectedAvatarUrl = user.GetAvatarUrl(ImageFormat.Png, 128)?.ToString();
                username = _detectedBotUsername;
                applicationId = _detectedApplicationId;
            }

            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(applicationId))
            {
                return;
            }

            // Once per connection: guard duplicate raises (READY can be
            // delivered more than once per session in reconnect scenarios).
            if (ReferenceEquals(_identityRaisedFor, client))
            {
                return;
            }
            _identityRaisedFor = client;

            var handlers = BotIdentityDetected;
            if (handlers == null)
            {
                return;
            }

            foreach (Action<string> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(BotId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ BotIdentityDetected subscriber threw an exception");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to capture the Discord bot identity");
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _logger.LogInformation("🔌 Disconnecting from Discord...");

            // Capture before the state teardown clears it: the token slot in
            // the process-wide live-session registry must be released too.
            var ownedToken = ConnectedToken;

            var client = _client;
            _client = null;

            if (client != null)
            {
                // 1) Unhook ALL event handlers BEFORE stopping, so the manual
                //    stop/logout below cannot reenter our callbacks (and so no
                //    gateway event fires after state teardown begins).
                UnhookEvents(client);

                // 2) Graceful stop + logout. Each step is best-effort and logged;
                //    the client is NOT disposed here — disposing it while other
                //    components still query service state and gateway work may be
                //    draining is what abruptly killed the app.
                try
                {
                    await client.StopAsync().ConfigureAwait(false);
                }
                catch (Exception stopEx)
                {
                    _logger.LogWarning(stopEx, "⚠️ Error stopping the Discord gateway client");
                }

                try
                {
                    await client.LogoutAsync().ConfigureAwait(false);
                }
                catch (Exception logoutEx)
                {
                    _logger.LogWarning(logoutEx, "⚠️ Error logging out the Discord gateway client");
                }

                // 3) Park the fully stopped client; it is disposed lazily by the
                //    next ConnectAsync (or by Dispose).
                _stoppedClient = client;
            }

            lock (_stateSync)
            {
                _isConnected = false;
                _botUsername = null;
                _connectedToken = null;
                _selectedChannelId = null;
                _connectedGuilds = Array.Empty<DiscordGuildInfo>();

                // Workflow state is per-session: clear the trigger context and
                // force slash command re-sync on the next connect.
                _workflowOrigin = null;
                _workflowSlashSyncedGuilds.Clear();
            }

            // Free the token's gateway slot so a later connect (this service or
            // a manager-owned one) can claim it.
            ReleaseLiveTokenSession(ownedToken?.Trim());

            try
            {
                _platform = await _dataService.GetPlatformByNameAsync(PlatformName).ConfigureAwait(false) ?? _platform;
                if (_platform != null)
                {
                    _platform.Status = "Disconnected";
                    _platform.UpdatedAt = DateTime.UtcNow;
                    await _dataService.SavePlatformAsync(_platform).ConfigureAwait(false);
                }
            }
            catch (Exception persistEx)
            {
                _logger.LogWarning(persistEx, "⚠️ Failed to persist Discord disconnected status");
            }

            // Publish LAST: at this point all state is cleared and the client
            // is fully stopped, so subscribers refreshing from us see a stable
            // snapshot instead of racing a disposed client.
            PublishConnectionChanged(connected: false);
            _logger.LogInformation("✅ Disconnected from Discord");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReconnectAsync()
    {
        try
        {
            // Load the saved bot token from the Discord Platform row
            string? savedToken = null;
            try
            {
                var platform = await _dataService.GetPlatformByNameAsync(PlatformName).ConfigureAwait(false);
                savedToken = platform?.AccessToken?.Trim();
            }
            catch (Exception loadEx)
            {
                _logger.LogWarning(loadEx, "⚠️ Failed to load the saved Discord bot token");
            }

            if (string.IsNullOrWhiteSpace(savedToken))
            {
                _logger.LogWarning("❌ Cannot reconnect to Discord: no bot token is saved");
                return false;
            }

            if (IsConnected)
            {
                await DisconnectAsync().ConfigureAwait(false);
            }

            return await ConnectAsync(savedToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Discord reconnect failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task SetSelectedChannelAsync(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            throw new ArgumentException("Channel ID must not be empty", nameof(channelId));
        }

        lock (_stateSync)
        {
            _selectedChannelId = channelId;
        }

        try
        {
            var platform = await _dataService.GetPlatformByNameAsync(PlatformName).ConfigureAwait(false)
                ?? _platform
                ?? new Platform { Name = PlatformName, Enabled = true };

            platform.ChannelId = channelId;
            platform.UpdatedAt = DateTime.UtcNow;
            await _dataService.SavePlatformAsync(platform).ConfigureAwait(false);
            _platform = platform;

            _logger.LogInformation("📢 Discord selected channel set to {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to persist Discord channel selection {ChannelId}", channelId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendMessageAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("❌ Cannot send Discord message: message is empty");
            return false;
        }

        var client = _client;
        if (client == null || !IsConnected)
        {
            _logger.LogWarning("❌ Cannot send Discord message: client is not connected");
            return false;
        }

        var channelId = SelectedChannelId;
        if (string.IsNullOrEmpty(channelId) || !ulong.TryParse(channelId, out var channelSnowflake))
        {
            _logger.LogWarning("❌ Cannot send Discord message: no valid channel selected (SelectedChannelId: {ChannelId})", channelId ?? "null");
            return false;
        }

        try
        {
            var channel = client.GetChannel(channelSnowflake) as IMessageChannel;
            if (channel == null)
            {
                _logger.LogWarning("❌ Discord channel {ChannelId} not found or is not a message channel", channelId);
                return false;
            }

            var sent = await channel.SendMessageAsync(message).ConfigureAwait(false);

            // Workflow "last_bot" scope: remember the newest sent message per channel
            RememberLastSentMessage(channel.Id, sent?.Id ?? 0);

            _logger.LogInformation("✅ Sent Discord message to channel {ChannelId}", channelId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send Discord message to channel {ChannelId}", channelId);
            return false;
        }
    }

    public async Task<string> SendOwnerTestMessageAsync()
    {
        var client = _client;
        if (client == null || !IsConnected || client.CurrentUser == null)
            return "Not connected — connect the bot first (Step 3), then test.";

        var guilds = client.Guilds;
        if (guilds.Count == 0)
            return "The bot is not in any server yet — invite it with the link in Step 3, then test again.";

        var botId = client.CurrentUser.Id;
        var results = new List<string>();

        foreach (var guild in guilds)
        {
            try
            {
                // Reuse a previously created test channel so repeated tests don't multiply channels
                global::Discord.ITextChannel? channel = guild.TextChannels.FirstOrDefault(c =>
                    c.Name.Equals("vyral-test", StringComparison.OrdinalIgnoreCase));

                if (channel is null)
                {
                    channel = await guild.CreateTextChannelAsync("vyral-test", props =>
                    {
                        // Private: @everyone denied; server owner + the bot allowed.
                        props.PermissionOverwrites = new[]
                        {
                            new Overwrite(guild.Id, PermissionTarget.Role,
                                new OverwritePermissions(viewChannel: PermValue.Deny)),
                            new Overwrite(guild.OwnerId, PermissionTarget.User,
                                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)),
                            new Overwrite(botId, PermissionTarget.User,
                                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)),
                        };
                    }).ConfigureAwait(false);
                    _logger.LogInformation("Created private test channel #{Channel} in {Guild}", channel.Name, guild.Name);
                }

                await channel.SendMessageAsync(
                    $"<@{guild.OwnerId}> ✅ Vyral is connected! This private test channel is visible only to you and the bot.")
                    .ConfigureAwait(false);
                results.Add($"'{guild.Name}': pinged the owner in #{channel.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Owner test message failed for guild {Guild}", guild.Name);
                results.Add($"'{guild.Name}': failed ({ex.Message}). If this mentions permissions, re-invite the bot with the admin invite link from Step 3.");
            }
        }

        return string.Join(" | ", results);
    }

    #region Moderation

    /// <summary>
    /// Timeout length limits imposed by Discord: 1 minute to 28 days, in minutes.
    /// </summary>
    private const int MaxTimeoutMinutes = 28 * 24 * 60;

    private const int MaxSlowmodeSeconds = 21600; // 6 hours, Discord's cap

    private const int MaxBanDeleteMessageDays = 7; // Discord's prune-day cap for bans

    /// <inheritdoc />
    public async Task<string> KickMemberAsync(string guildId, string userId, string? reason)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(guildId, "server", out var guildSnowflake, out var error)
            || !TryParseSnowflake(userId, "user", out var userSnowflake, out error))
        {
            return error;
        }

        var guild = client.GetGuild(guildSnowflake);
        if (guild == null)
        {
            return $"❌ I'm not in that server (ID {guildId}).";
        }

        var permissionError = GetMissingBotPermission(guild, GuildPermission.KickMembers, "Kick Members");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            var member = await GetGuildMemberAsync(guild, userSnowflake).ConfigureAwait(false);
            if (member == null)
            {
                return $"❌ User {userId} is not a member of {guild.Name}.";
            }

            if (client.CurrentUser != null && member.Id == client.CurrentUser.Id)
            {
                return "❌ I can't kick myself.";
            }

            await member.KickAsync(reason).ConfigureAwait(false);

            var reasonSuffix = BuildReasonSuffix(reason);
            return $"✅ Kicked @{member.Username} from {guild.Name}{reasonSuffix}";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Kick Members");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to kick user {userId}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task<string> BanMemberAsync(string guildId, string userId, string? reason, int deleteMessageDays = 0)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(guildId, "server", out var guildSnowflake, out var error)
            || !TryParseSnowflake(userId, "user", out var userSnowflake, out error))
        {
            return error;
        }

        if (deleteMessageDays is < 0 or > MaxBanDeleteMessageDays)
        {
            return $"❌ Ban message deletion must be between 0 and {MaxBanDeleteMessageDays} days.";
        }

        var guild = client.GetGuild(guildSnowflake);
        if (guild == null)
        {
            return $"❌ I'm not in that server (ID {guildId}).";
        }

        var permissionError = GetMissingBotPermission(guild, GuildPermission.BanMembers, "Ban Members");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            // Resolve a display name: prefer the live member, fall back to a REST
            // user lookup (Discord lets you ban users who already left).
            var member = await GetGuildMemberAsync(guild, userSnowflake).ConfigureAwait(false);
            string displayName;
            if (member != null)
            {
                displayName = member.Username;
            }
            else
            {
                var restUser = await TryGetRestUserAsync(client, userSnowflake).ConfigureAwait(false);
                displayName = restUser?.Username ?? userId;
            }

            await guild.AddBanAsync(userSnowflake, deleteMessageDays, reason).ConfigureAwait(false);

            var reasonSuffix = BuildReasonSuffix(reason);
            return $"✅ Banned @{displayName} from {guild.Name}{reasonSuffix}";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Ban Members");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to ban user {userId}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task<string> UnbanMemberAsync(string guildId, string userId)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(guildId, "server", out var guildSnowflake, out var error)
            || !TryParseSnowflake(userId, "user", out var userSnowflake, out error))
        {
            return error;
        }

        var guild = client.GetGuild(guildSnowflake);
        if (guild == null)
        {
            return $"❌ I'm not in that server (ID {guildId}).";
        }

        var permissionError = GetMissingBotPermission(guild, GuildPermission.BanMembers, "Ban Members");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            var ban = await guild.GetBanAsync(userSnowflake).ConfigureAwait(false);
            if (ban == null)
            {
                return $"❌ User {userId} is not banned in {guild.Name}.";
            }

            await guild.RemoveBanAsync(userSnowflake).ConfigureAwait(false);

            var displayName = ban.User?.Username ?? userId;
            return $"✅ Unbanned @{displayName} in {guild.Name}";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Ban Members");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to unban user {userId}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public Task<string> GrantRoleAsync(string guildId, string userId, string roleId)
        => ModifyMemberRoleAsync(guildId, userId, roleId, grant: true);

    /// <inheritdoc />
    public Task<string> RemoveRoleAsync(string guildId, string userId, string roleId)
        => ModifyMemberRoleAsync(guildId, userId, roleId, grant: false);

    /// <summary>
    /// Shared grant/remove role core. <paramref name="userId"/> may be a
    /// snowflake or a username (command action sequences only carry names —
    /// see <see cref="ResolveGuildMemberAsync"/>). Uses the Discord.Net 3.x
    /// member-level <c>IGuildUser.AddRoleAsync/RemoveRoleAsync</c> API.
    /// </summary>
    private async Task<string> ModifyMemberRoleAsync(string guildId, string userId, string roleId, bool grant)
    {
        var verb = grant ? "grant" : "remove";
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(guildId, "server", out var guildSnowflake, out var error)
            || !TryParseSnowflake(roleId, "role", out var roleSnowflake, out error))
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return $"❌ '{userId ?? "(empty)"}' is not a valid user ID or name.";
        }

        var guild = client.GetGuild(guildSnowflake);
        if (guild == null)
        {
            return $"❌ I'm not in that server (ID {guildId}).";
        }

        var role = guild.GetRole(roleSnowflake);
        if (role == null)
        {
            return $"❌ Role {roleId} does not exist in {guild.Name}.";
        }

        var permissionError = GetMissingBotPermission(guild, GuildPermission.ManageRoles, "Manage Roles");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            var member = await ResolveGuildMemberAsync(guild, userId).ConfigureAwait(false);
            if (member == null)
            {
                return $"❌ User '{userId}' is not a member of {guild.Name}.";
            }

            if (grant)
            {
                await member.AddRoleAsync(role).ConfigureAwait(false);
                return $"✅ Granted '{role.Name}' to @{member.Username} in {guild.Name}";
            }

            await member.RemoveRoleAsync(role).ConfigureAwait(false);
            return $"✅ Removed '{role.Name}' from @{member.Username} in {guild.Name}";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Manage Roles");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to {verb} role {roleId}: {ex.Message}";
        }
    }

    /// <summary>
    /// Resolves a guild member from a snowflake ID or a username: the gateway
    /// cache first (partial — the client runs without the GuildMembers
    /// intent), then a REST user search. Null when the user cannot be found.
    /// </summary>
    private static async Task<IGuildUser?> ResolveGuildMemberAsync(SocketGuild guild, string userIdOrName)
    {
        var query = userIdOrName.Trim();

        if (ulong.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snowflake)
            && snowflake > 0)
        {
            return await GetGuildMemberAsync(guild, snowflake).ConfigureAwait(false);
        }

        var cached = guild.Users.FirstOrDefault(u =>
            string.Equals(u.Username, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(u.DisplayName, query, StringComparison.OrdinalIgnoreCase));
        if (cached != null)
        {
            return cached;
        }

        try
        {
            IGuild guildApi = guild;
            var matches = await guildApi.SearchUsersAsync(query, limit: 5).ConfigureAwait(false);
            return matches?.FirstOrDefault(u =>
                string.Equals(u.Username, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(u.DisplayName, query, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string> TimeoutMemberAsync(string guildId, string userId, int minutes, string? reason)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(guildId, "server", out var guildSnowflake, out var error)
            || !TryParseSnowflake(userId, "user", out var userSnowflake, out error))
        {
            return error;
        }

        if (minutes is < 1 or > MaxTimeoutMinutes)
        {
            return $"❌ Timeout length must be between 1 and {MaxTimeoutMinutes} minutes (28 days).";
        }

        var guild = client.GetGuild(guildSnowflake);
        if (guild == null)
        {
            return $"❌ I'm not in that server (ID {guildId}).";
        }

        var permissionError = GetMissingBotPermission(guild, GuildPermission.ModerateMembers, "Moderate Members");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            var member = await GetGuildMemberAsync(guild, userSnowflake).ConfigureAwait(false);
            if (member == null)
            {
                return $"❌ User {userId} is not a member of {guild.Name}.";
            }

            if (client.CurrentUser != null && member.Id == client.CurrentUser.Id)
            {
                return "❌ I can't time out myself.";
            }

            // Discord.Net 3.20.1: timeout is IGuildUser.SetTimeOutAsync(TimeSpan)
            // (no audit-log reason parameter exists on the timeout endpoint).
            await member.SetTimeOutAsync(TimeSpan.FromMinutes(minutes)).ConfigureAwait(false);

            var reasonSuffix = BuildReasonSuffix(reason);
            return $"✅ Timed out @{member.Username} for {minutes} minute(s) in {guild.Name}{reasonSuffix}";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Moderate Members");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to time out user {userId}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task<string> RemoveTimeoutAsync(string guildId, string userId)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(guildId, "server", out var guildSnowflake, out var error)
            || !TryParseSnowflake(userId, "user", out var userSnowflake, out error))
        {
            return error;
        }

        var guild = client.GetGuild(guildSnowflake);
        if (guild == null)
        {
            return $"❌ I'm not in that server (ID {guildId}).";
        }

        var permissionError = GetMissingBotPermission(guild, GuildPermission.ModerateMembers, "Moderate Members");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            var member = await GetGuildMemberAsync(guild, userSnowflake).ConfigureAwait(false);
            if (member == null)
            {
                return $"❌ User {userId} is not a member of {guild.Name}.";
            }

            if (member.TimedOutUntil == null)
            {
                return $"ℹ️ @{member.Username} is not timed out in {guild.Name}.";
            }

            // Discord.Net 3.20.1: IGuildUser.RemoveTimeOutAsync()
            await member.RemoveTimeOutAsync().ConfigureAwait(false);

            return $"✅ Removed the timeout for @{member.Username} in {guild.Name}";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Moderate Members");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to remove the timeout for user {userId}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task<string> SetSlowmodeAsync(string channelId, int seconds)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(channelId, "channel", out var channelSnowflake, out var error))
        {
            return error;
        }

        if (seconds is < 0 or > MaxSlowmodeSeconds)
        {
            return $"❌ Slowmode must be between 0 (off) and {MaxSlowmodeSeconds} seconds.";
        }

        if (client.GetChannel(channelSnowflake) is not SocketTextChannel channel)
        {
            return $"❌ Channel {channelId} was not found or is not a text channel.";
        }

        var permissionError = GetMissingBotPermission(channel.Guild, GuildPermission.ManageChannels, "Manage Channels");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            await channel.ModifyAsync(props => props.SlowModeInterval = seconds).ConfigureAwait(false);

            return seconds == 0
                ? $"✅ Disabled slowmode in #{channel.Name}."
                : $"✅ Set slowmode to {seconds} second(s) in #{channel.Name}.";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Manage Channels");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to set slowmode in channel {channelId}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task<string> PurgeMessagesAsync(string channelId, int count)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        if (!TryParseSnowflake(channelId, "channel", out var channelSnowflake, out var error))
        {
            return error;
        }

        if (count is < 1 or > 100)
        {
            return "❌ Purge count must be between 1 and 100 messages.";
        }

        if (client.GetChannel(channelSnowflake) is not SocketTextChannel channel)
        {
            return $"❌ Channel {channelId} was not found or is not a text channel.";
        }

        var permissionError = GetMissingBotPermission(channel.Guild, GuildPermission.ManageMessages, "Manage Messages");
        if (permissionError != null)
        {
            return permissionError;
        }

        try
        {
            // GetMessagesAsync returns a paged IAsyncEnumerable; FlattenAsync is
            // Discord.Net's helper that drains it into one flat collection.
            var messages = (await channel.GetMessagesAsync(count).FlattenAsync().ConfigureAwait(false)).ToList();

            if (messages.Count == 0)
            {
                return $"ℹ️ No messages found to delete in #{channel.Name}.";
            }

            if (messages.Count == 1)
            {
                // Discord's bulk-delete endpoint requires at least 2 messages.
                await channel.DeleteMessageAsync(messages[0]).ConfigureAwait(false);
            }
            else
            {
                await channel.DeleteMessagesAsync(messages).ConfigureAwait(false);
            }

            return $"✅ Deleted {messages.Count} message(s) in #{channel.Name}.";
        }
        catch (HttpException ex)
        {
            return DescribeDiscordError(ex, "Manage Messages");
        }
        catch (Exception ex)
        {
            return $"❌ Failed to purge messages in channel {channelId}: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns the live gateway client while connected, otherwise null (callers
    /// translate that into "Bot is not connected").
    /// </summary>
    private DiscordSocketClient? GetConnectedClient()
    {
        var client = _client;
        if (client == null || !IsConnected)
        {
            return null;
        }

        return client;
    }

    /// <summary>
    /// Resolves a guild member: gateway cache first, then a REST download
    /// (needed because the client runs without the GuildMembers intent).
    /// </summary>
    private static async Task<IGuildUser?> GetGuildMemberAsync(SocketGuild guild, ulong userId)
    {
        IGuild guildApi = guild;
        return await guildApi.GetUserAsync(userId, CacheMode.AllowDownload).ConfigureAwait(false);
    }

    /// <summary>Best-effort REST user lookup; null on any failure.</summary>
    private static async Task<IUser?> TryGetRestUserAsync(DiscordSocketClient client, ulong userId)
    {
        try
        {
            return await client.Rest.GetUserAsync(userId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Pre-flight bot permission check. Returns a ready-to-send error string when
    /// the bot's cached guild profile definitely lacks the permission, null when
    /// the check passes (or the profile isn't cached yet — then Discord decides).
    /// </summary>
    private static string? GetMissingBotPermission(SocketGuild? guild, GuildPermission permission, string permissionName)
    {
        var current = guild?.CurrentUser;
        if (current == null || current.GuildPermissions.Has(permission))
        {
            return null;
        }

        return $"❌ The bot lacks the {permissionName} permission.";
    }

    /// <summary>Maps Discord API rejections to human-readable chat messages.</summary>
    private static string DescribeDiscordError(HttpException ex, string permissionName)
    {
        if (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            return $"❌ The bot lacks the {permissionName} permission (or the target outranks the bot).";
        }

        if (ex.DiscordCode == DiscordErrorCode.UnknownMember || ex.DiscordCode == DiscordErrorCode.UnknownUser)
        {
            return "❌ That user could not be found.";
        }

        var detail = string.IsNullOrWhiteSpace(ex.Reason) ? ex.Message : ex.Reason;
        return $"❌ Discord rejected the request: {detail}";
    }

    /// <summary>Validates a snowflake-style ID string ("123456789012345678").</summary>
    private static bool TryParseSnowflake(string? value, string kind, out ulong snowflake, out string error)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && ulong.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out snowflake)
            && snowflake > 0)
        {
            error = string.Empty;
            return true;
        }

        snowflake = 0;
        error = $"❌ '{value ?? "(empty)"}' is not a valid {kind} ID.";
        return false;
    }

    private static string BuildReasonSuffix(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? string.Empty : $" — reason: {reason.Trim()}";
    }

    #endregion

    #region Gateway client setup

    private static DiscordSocketClient CreateClient() => new(new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.Guilds
                       | GatewayIntents.GuildMessages
                       | GatewayIntents.MessageContent
                       | GatewayIntents.DirectMessages
    });

    private void HookEvents(DiscordSocketClient client)
    {
        client.Log += OnClientLogAsync;
        client.Ready += OnReadyAsync;
        client.Connected += OnGatewayConnectedAsync;
        client.Disconnected += OnGatewayDisconnectedAsync;
        client.GuildAvailable += OnGuildListChangedAsync;
        client.JoinedGuild += OnGuildListChangedAsync;
        client.LeftGuild += OnGuildListChangedAsync;
        client.MessageReceived += OnMessageReceivedAsync;

        // Workflow surface: slash commands, workflow component interactions and
        // per-guild slash command re-registration on (re)connect.
        client.SlashCommandExecuted += OnWorkflowSlashCommandExecutedAsync;
        client.ButtonExecuted += OnWorkflowButtonExecutedAsync;
        client.SelectMenuExecuted += OnWorkflowSelectMenuExecutedAsync;
        client.Ready += OnWorkflowReadyAsync;
        client.GuildAvailable += OnWorkflowGuildAvailableAsync;
    }

    private void UnhookEvents(DiscordSocketClient client)
    {
        client.Log -= OnClientLogAsync;
        client.Ready -= OnReadyAsync;
        client.Connected -= OnGatewayConnectedAsync;
        client.Disconnected -= OnGatewayDisconnectedAsync;
        client.GuildAvailable -= OnGuildListChangedAsync;
        client.JoinedGuild -= OnGuildListChangedAsync;
        client.LeftGuild -= OnGuildListChangedAsync;
        client.MessageReceived -= OnMessageReceivedAsync;

        client.SlashCommandExecuted -= OnWorkflowSlashCommandExecutedAsync;
        client.ButtonExecuted -= OnWorkflowButtonExecutedAsync;
        client.SelectMenuExecuted -= OnWorkflowSelectMenuExecutedAsync;
        client.Ready -= OnWorkflowReadyAsync;
        client.GuildAvailable -= OnWorkflowGuildAvailableAsync;
    }

    private async Task TryDisposeClientAsync(DiscordSocketClient? client)
    {
        if (client == null)
        {
            return;
        }

        try
        {
            if (client.ConnectionState != ConnectionState.Disconnected)
            {
                try
                {
                    await client.StopAsync().ConfigureAwait(false);
                }
                catch (Exception stopEx)
                {
                    _logger.LogWarning(stopEx, "⚠️ Error stopping the Discord client during disposal");
                }
            }

            await client.LogoutAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error logging out the Discord client during disposal");
        }
        finally
        {
            try
            {
                client.Dispose();
            }
            catch (Exception disposeEx)
            {
                _logger.LogWarning(disposeEx, "⚠️ Error disposing the Discord client");
            }
        }
    }

    #endregion

    #region Gateway event handlers

    private Task OnClientLogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Debug => LogLevel.Debug,
            LogSeverity.Verbose => LogLevel.Trace,
            _ => LogLevel.Information
        };

        if (message.Exception != null)
        {
            _logger.Log(level, message.Exception, "Discord.Net: {Message}", message.Message ?? message.Exception.Message);
        }
        else
        {
            _logger.Log(level, "Discord.Net [{Source}]: {Message}", message.Source, message.Message);
        }

        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        var client = _client;
        if (client == null)
        {
            return Task.CompletedTask;
        }

        lock (_stateSync)
        {
            _botUsername = client.CurrentUser?.Username;
        }

        RefreshGuildSnapshot();
        SetConnectionState(connected: true);
        _logger.LogInformation("✅ Discord gateway ready as {Username} in {GuildCount} guild(s)",
            BotUsername, ConnectedGuilds.Count);
        return Task.CompletedTask;
    }

    private Task OnGatewayConnectedAsync()
    {
        // Fires on the initial connect and on every Discord.Net auto-reconnect
        _logger.LogInformation("🔌 Discord gateway connected");
        SetConnectionState(connected: true);
        return Task.CompletedTask;
    }

    private Task OnGatewayDisconnectedAsync(Exception ex)
    {
        // Discord.Net auto-reconnects by default; just surface the drop
        if (ex != null)
        {
            _logger.LogWarning(ex, "⚠️ Discord gateway disconnected ({Reason}); Discord.Net will attempt to auto-reconnect",
                ex.Message);
        }
        else
        {
            _logger.LogInformation("🔌 Discord gateway disconnected; Discord.Net will attempt to auto-reconnect");
        }

        SetConnectionState(connected: false);
        return Task.CompletedTask;
    }

    private Task OnGuildListChangedAsync(SocketGuild guild)
    {
        RefreshGuildSnapshot();
        _logger.LogDebug("📋 Discord guild state updated: {GuildName} ({MemberCount} members)",
            guild.Name, guild.MemberCount);
        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            var client = _client;
            if (client == null)
            {
                return Task.CompletedTask;
            }

            // Skip our own messages and system/bot-spam (system messages, other
            // bots and webhooks all report a non-User MessageSource)
            if (message.Author != null && client.CurrentUser != null && message.Author.Id == client.CurrentUser.Id)
            {
                return Task.CompletedTask;
            }

            // Bot detection + reply capture (see the "Bot detection & command
            // capture" region): runs BEFORE the Source filter below, because
            // messages authored by other bots never reach the event pipeline.
            // Records the author in the seen-bots map and completes any pending
            // reply capture for it. Publishes nothing, persists nothing.
            TrackBotAuthor(client, message);

            // Workflow "trigger" scope: remember the most recent non-self
            // message context (self-messages already returned above) so scope
            // operations can find the message that started a workflow run.
            TrackWorkflowOrigin(message);

            if (message.Source != MessageSource.User)
            {
                return Task.CompletedTask;
            }

            // DM channels have no name of their own: Discord.Net reports
            // IChannel.Name as "@recipient" for a SocketDMChannel, which would
            // label every DM with the recipient's handle. Detect the DM channel
            // FIRST and use a stable "DirectMessage" name instead.
            var channelName = message.Channel is IDMChannel
                ? "DirectMessage"
                : message.Channel?.Name;
            if (string.IsNullOrEmpty(channelName))
            {
                channelName = "DirectMessage";
            }

            var channelId = message.Channel?.Id.ToString();
            var guild = (message.Channel as SocketGuildChannel)?.Guild;
            var guildName = guild?.Name;

            var isModerator = false;
            var isBroadcaster = false;
            if (message.Author is IGuildUser guildUser)
            {
                var permissions = guildUser.GuildPermissions;
                isModerator = permissions.Has(GuildPermission.ManageMessages);
                isBroadcaster = guildUser.Id == guildUser.Guild.OwnerId
                                || permissions.Has(GuildPermission.Administrator);
            }

            var username = message.Author?.Username ?? "Unknown";
            var content = message.Content ?? string.Empty;
            var messageId = $"discord:{message.Id}";

            // Receipt evidence: log DMs BEFORE publishing so a lost publish
            // downstream can be told apart from a DM that never reached the
            // gateway pipeline at all.
            if (message.Channel is IDMChannel receivedDm)
            {
                // Remember the LIVE channel object: Discord.Net's GetChannel
                // lookup frequently misses DM channels (they are not part of
                // the guild cache and READY only stocks pre-existing DMs), so
                // replies must go through the channel instance the message
                // actually arrived on.
                _knownDmChannels[receivedDm.Id] = (receivedDm, message.Author?.Id ?? 0);

                var dmPreview = content.Length > 80 ? content[..80] : content;
                _logger.LogInformation("✅ Discord DM received from {Username} (bot {BotId}, channel {ChannelId}): \"{Content}\"",
                    username, BotId, channelId, dmPreview);
            }

            // Discord-specific event for the chat UI
            var discordEvent = new DiscordChatMessageEvent
            {
                Source = "Discord",
                BotId = BotId,
                GuildName = guildName,
                Channel = channelName,
                ChannelId = channelId,
                Username = username,
                Message = content,
                IsModerator = isModerator,
                IsBroadcaster = isBroadcaster,
                MessageId = messageId
            };

            // Fire-and-forget so the gateway thread never blocks (matches Kick/Twitch services)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _eventBus.PublishAsync(discordEvent).ConfigureAwait(false);
                }
                catch (Exception publishEx)
                {
                    _logger.LogError(publishEx, "❌ Failed to publish DiscordChatMessageEvent for {Username}", username);
                }
            });

            // Platform-agnostic event for unified pipelines (commands, currency, ...)
            var genericEvent = new ChatMessageReceivedEvent
            {
                Source = "Discord",
                Platform = "Discord",
                Channel = channelName,
                Username = username,
                Message = content,
                IsModerator = isModerator,
                IsSubscriber = false,
                IsVip = false,
                IsBroadcaster = isBroadcaster,
                MessageId = messageId
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await _eventBus.PublishAsync(genericEvent).ConfigureAwait(false);
                }
                catch (Exception publishEx)
                {
                    _logger.LogError(publishEx, "❌ Failed to publish ChatMessageReceivedEvent for Discord user {Username}", username);
                }
            });

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling Discord message received");
            return Task.CompletedTask;
        }
    }

    #endregion

    #region State management

    private void RefreshGuildSnapshot()
    {
        var client = _client;
        IReadOnlyList<DiscordGuildInfo> snapshot = Array.Empty<DiscordGuildInfo>();
        if (client != null)
        {
            snapshot = client.Guilds
                .Select(g => new DiscordGuildInfo(
                    g.Id.ToString(),
                    g.Name,
                    g.MemberCount,
                    g.TextChannels
                        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(c => new DiscordChannelInfo(c.Id.ToString(), c.Name))
                        .ToList()))
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        lock (_stateSync)
        {
            _connectedGuilds = snapshot;
        }
    }

    private void SetConnectionState(bool connected)
    {
        lock (_stateSync)
        {
            if (_isConnected == connected)
            {
                return;
            }

            _isConnected = connected;
        }

        PublishConnectionChanged(connected);
    }

    private void PublishConnectionChanged(bool connected)
    {
        var connectionEvent = new PlatformConnectionChangedEvent
        {
            Source = "DiscordService",
            Platform = "Discord",
            Connected = connected,
            Channel = SelectedChannelId,
            BotUsername = BotUsername
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _eventBus.PublishAsync(connectionEvent).ConfigureAwait(false);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx, "❌ Failed to publish PlatformConnectionChangedEvent for Discord");
            }
        });
    }

    #endregion

    #region Bot detection & command capture

    /// <inheritdoc />
    public IReadOnlyList<DiscordSeenBot> SeenBots
    {
        get
        {
            // Best-effort seed from the guild member cache (partial: the
            // GuildMembers intent is NOT requested, so Discord only sent us a
            // subset of members — bots that never spoke may be missing until
            // they do). Messages remain the primary source.
            SeedSeenBotsFromGuilds();

            return _seenBots.Values
                .OrderByDescending(bot => bot.LastSeenUtc)
                .ToList();
        }
    }

    /// <inheritdoc />
    public async Task<string?> SendAndCaptureReplyAsync(string channelId, string message, string authorBotId, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(channelId)
            || string.IsNullOrWhiteSpace(message)
            || string.IsNullOrWhiteSpace(authorBotId)
            || timeout <= TimeSpan.Zero
            || !ulong.TryParse(authorBotId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var authorId)
            || !ulong.TryParse(channelId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelSnowflake))
        {
            _logger.LogWarning("❌ Cannot capture a bot reply: invalid channel, author bot id, message or timeout");
            return null;
        }

        var client = _client;
        if (client == null)
        {
            // The session is gone entirely: free every in-flight waiter so it
            // does not sit out its full timeout. (Awaiters are always bounded
            // by their own timeout, so this sweep is a courtesy, not a fix.)
            FailAllPendingCaptures();
            return null;
        }

        if (!IsConnected)
        {
            // Transient gateway drop (Discord.Net auto-reconnects): leave
            // in-flight captures alone — the reply may still arrive.
            return null;
        }

        // Same channel resolution SendMessageAsync uses, but for an explicit channel id
        if (client.GetChannel(channelSnowflake) is not IMessageChannel channel)
        {
            _logger.LogWarning("❌ Cannot capture a bot reply: Discord channel {ChannelId} not found or is not a message channel", channelId);
            return null;
        }

        // Register BEFORE sending so a lightning-fast bot reply can never slip
        // past the capture. Only one capture per author at a time: a concurrent
        // call for the same bot completes the previous wait with null and
        // takes over.
        var capture = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_captureSync)
        {
            if (_pendingCaptures.TryGetValue(authorId, out var previous))
            {
                _pendingCaptures.Remove(authorId, out _);
                previous.TrySetResult(null!);
            }

            _pendingCaptures[authorId] = capture;
        }

        try
        {
            await channel.SendMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Failed to send the capture trigger to Discord channel {ChannelId}", channelId);
            RemovePendingCapture(authorId, capture);
            return null;
        }

        try
        {
            // Raw content of the next message authored by that bot (any
            // channel); an empty string means the reply was embed-only, null
            // means superseded by a newer capture for the same bot.
            return await capture.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("⏳ Timed out after {Seconds:N0}s waiting for bot {BotId} to reply in channel {ChannelId}",
                timeout.TotalSeconds, authorBotId, channelId);
            RemovePendingCapture(authorId, capture);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Waiting for a reply from Discord bot {BotId} failed", authorBotId);
            RemovePendingCapture(authorId, capture);
            return null;
        }
    }

    /// <summary>
    /// Runs inside the raw gateway message handler BEFORE the
    /// <see cref="MessageSource"/>.User filter for every message authored by
    /// another bot: records the author in <see cref="_seenBots"/> and
    /// completes any pending reply capture for it. Publishes no events and
    /// persists nothing. No-op for non-bot authors and for our own account.
    /// </summary>
    private void TrackBotAuthor(DiscordSocketClient client, SocketMessage message)
    {
        try
        {
            var author = message.Author;
            if (author == null || !author.IsBot)
            {
                return;
            }

            var selfId = client.CurrentUser?.Id;
            if (selfId.HasValue && author.Id == selfId.Value)
            {
                return; // never track ourselves
            }

            RecordSeenBot(author, (message.Channel as SocketGuildChannel)?.Guild?.Name);
            CompletePendingCapture(author.Id, message.Content ?? string.Empty);
        }
        catch (Exception ex)
        {
            // Never let best-effort bot tracking break the message pipeline
            _logger.LogDebug(ex, "Discord bot tracking skipped for author {AuthorId}", message.Author?.Id);
        }
    }

    /// <summary>Creates or refreshes the seen-bots entry for one bot author.</summary>
    private void RecordSeenBot(IUser author, string? guildName)
    {
        var now = DateTime.UtcNow;
        var username = string.IsNullOrEmpty(author.Username) ? "Unknown" : author.Username;
        var displayName = (author as IGuildUser)?.DisplayName;

        _seenBots.AddOrUpdate(
            author.Id,
            _ => new DiscordSeenBot(author.Id.ToString(), username, displayName, now, guildName),
            (_, existing) => existing with
            {
                Username = username,
                DisplayName = displayName ?? existing.DisplayName,
                LastSeenUtc = now,
                GuildName = guildName ?? existing.GuildName
            });
    }

    /// <summary>
    /// Best-effort seed of <see cref="_seenBots"/> from the guild member cache
    /// (once per client instance, triggered by the first <see cref="SeenBots"/>
    /// read after a connect). Partial by design: without the GuildMembers
    /// intent Discord only sends a subset of members, so bots that never spoke
    /// may be missing until they do. Excludes ourselves.
    /// </summary>
    private void SeedSeenBotsFromGuilds()
    {
        var client = _client;
        if (client == null)
        {
            return; // retried on the next SeenBots access once connected
        }

        lock (_seenBotsSeedSync)
        {
            if (ReferenceEquals(_seenBotsSeededFrom, client))
            {
                return;
            }
        }

        try
        {
            foreach (var guild in client.Guilds)
            {
                var guildName = guild.Name;
                var selfId = client.CurrentUser?.Id;
                foreach (var user in guild.Users)
                {
                    if (!user.IsBot)
                    {
                        continue;
                    }

                    if (selfId.HasValue && user.Id == selfId.Value)
                    {
                        continue; // exclude ourselves
                    }

                    RecordSeenBot(user, guildName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Best-effort seeding of seen Discord bots from the guild cache failed");
        }
        finally
        {
            lock (_seenBotsSeedSync)
            {
                _seenBotsSeededFrom = client;
            }
        }
    }

    /// <summary>Completes and removes the pending capture for an author, if any.</summary>
    private void CompletePendingCapture(ulong authorId, string content)
    {
        TaskCompletionSource<string>? pending = null;
        lock (_captureSync)
        {
            if (_pendingCaptures.TryGetValue(authorId, out var capture))
            {
                _pendingCaptures.Remove(authorId, out _);
                pending = capture;
            }
        }

        // Complete outside the lock (RunContinuationsAsynchronously already
        // avoids inline continuation runs on the gateway thread).
        pending?.TrySetResult(content);
    }

    /// <summary>
    /// Removes a capture registration, but only when this exact waiter still
    /// owns the slot (a takeover by a newer capture must survive).
    /// </summary>
    private void RemovePendingCapture(ulong authorId, TaskCompletionSource<string> capture)
    {
        lock (_captureSync)
        {
            if (_pendingCaptures.TryGetValue(authorId, out var pending)
                && ReferenceEquals(pending, capture))
            {
                _pendingCaptures.Remove(authorId, out _);
            }
        }
    }

    /// <summary>Completes every in-flight capture with null and clears them.</summary>
    private void FailAllPendingCaptures()
    {
        List<TaskCompletionSource<string>> orphaned = new();
        lock (_captureSync)
        {
            foreach (var pair in _pendingCaptures)
            {
                if (_pendingCaptures.Remove(pair.Key, out var capture))
                {
                    orphaned.Add(capture);
                }
            }
        }

        foreach (var capture in orphaned)
        {
            capture.TrySetResult(null!);
        }
    }

    #endregion

    #region Workflow execution surface

    /// <summary>
    /// Most recent message/interaction context this service saw ("trigger"
    /// scope source): channel + message id, author and guild. The message id
    /// is 0 for slash-command interactions (Discord creates the response
    /// message lazily); scope ops fall back to the newest non-self message in
    /// the channel when it is 0.
    /// </summary>
    private sealed record WorkflowOrigin(
        ulong ChannelId,
        ulong MessageId,
        string? AuthorUsername,
        ulong? GuildId,
        string? GuildName);

    /// <summary>Stores the newest sent message id per channel (one entry per channel = always the latest).</summary>
    private void RememberLastSentMessage(ulong channelId, ulong messageId)
    {
        if (channelId == 0 || messageId == 0)
        {
            return;
        }

        _lastSentByChannel[channelId] = messageId;
    }

    /// <summary>
    /// Records a gateway message as the current workflow origin. Never runs
    /// for self-messages (the caller filters those out first), so "trigger"
    /// scope can never delete the bot's own output.
    /// </summary>
    private void TrackWorkflowOrigin(SocketMessage message)
    {
        try
        {
            if (message?.Channel == null)
            {
                return;
            }

            var guild = (message.Channel as SocketGuildChannel)?.Guild;
            lock (_stateSync)
            {
                _workflowOrigin = new WorkflowOrigin(
                    message.Channel.Id,
                    message.Id,
                    message.Author?.Username,
                    guild?.Id,
                    guild?.Name);
            }
        }
        catch (Exception ex)
        {
            // Never let best-effort origin tracking break the message pipeline
            _logger.LogDebug(ex, "Workflow origin tracking skipped for message {MessageId}", message?.Id);
        }
    }

    /// <summary>
    /// Overwrites the workflow origin from a slash command / component
    /// interaction: interactions always win over plain messages because a
    /// workflow fired from an interaction must scope to its channel, not to
    /// the last chat message.
    /// </summary>
    private void TrackWorkflowOriginFromInteraction(
        ISocketMessageChannel? channel, ulong messageId, string? username, SocketGuild? guild)
    {
        try
        {
            if (channel == null)
            {
                return;
            }

            lock (_stateSync)
            {
                _workflowOrigin = new WorkflowOrigin(channel.Id, messageId, username, guild?.Id, guild?.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Workflow origin tracking skipped for an interaction in channel {ChannelId}", channel?.Id);
        }
    }

    /// <summary>Current workflow origin snapshot (null until something was seen this session).</summary>
    private WorkflowOrigin? GetWorkflowOrigin()
    {
        lock (_stateSync)
        {
            return _workflowOrigin;
        }
    }

    /// <summary>
    /// Resolves the guild for workflow operations: an explicit guild name
    /// first, then the origin guild, then the selected channel's guild, then
    /// the bot's first guild. Null when the bot is in no guild.
    /// </summary>
    private SocketGuild? ResolveWorkflowGuild(DiscordSocketClient client, string? guildName)
    {
        var query = guildName?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            var byName = client.Guilds.FirstOrDefault(g =>
                string.Equals(g.Name, query, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                return byName;
            }
        }

        var origin = GetWorkflowOrigin();
        if (origin?.GuildId is > 0)
        {
            var byId = client.GetGuild(origin.GuildId.Value);
            if (byId != null)
            {
                return byId;
            }
        }

        var selectedSnowflake = ParseSnowflakeOrNull(SelectedChannelId);
        if (selectedSnowflake is > 0
            && (client.GetChannel(selectedSnowflake.Value) as SocketGuildChannel)?.Guild is SocketGuild selectedGuild)
        {
            return selectedGuild;
        }

        return client.Guilds.FirstOrDefault();
    }

    /// <summary>
    /// Resolves a message channel for workflow operations, mirroring
    /// <see cref="SendMessageAsync"/>'s convention: empty → the origin channel,
    /// else the selected channel. "#name"/"name" is searched inside
    /// <paramref name="guildName"/> when given, otherwise across all guilds;
    /// a numeric string is treated as a snowflake id first (with the name
    /// search as a fallback so an unresolvable id never hides a matching
    /// channel). Outputs the guild that owns the resolved channel.
    /// </summary>
    private IMessageChannel? ResolveWorkflowChannel(
        DiscordSocketClient client, string? channelNameOrId, string? guildName, out SocketGuild? guild)
    {
        guild = null;

        var query = channelNameOrId?.Trim();
        var searchName = string.IsNullOrEmpty(query)
            ? string.Empty
            : (query.StartsWith('#') ? query[1..].Trim() : query);

        // 1) Raw snowflake id
        if (searchName.Length > 0
            && ulong.TryParse(searchName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snowflake)
            && client.GetChannel(snowflake) is IMessageChannel byId)
        {
            guild = (byId as SocketGuildChannel)?.Guild;
            return byId;
        }

        // 2) Name search ("#general" / "general")
        if (searchName.Length > 0)
        {
            var scopedGuilds = string.IsNullOrWhiteSpace(guildName)
                ? client.Guilds
                : client.Guilds.Where(g => string.Equals(g.Name, guildName.Trim(), StringComparison.OrdinalIgnoreCase));

            foreach (var candidateGuild in scopedGuilds)
            {
                var channel = candidateGuild.TextChannels.FirstOrDefault(c =>
                    string.Equals(c.Name, searchName, StringComparison.OrdinalIgnoreCase));
                if (channel != null)
                {
                    guild = candidateGuild;
                    return channel;
                }
            }
        }

        // 3) Origin channel, then the selected channel (SendMessageAsync convention)
        var origin = GetWorkflowOrigin();
        if (origin is { ChannelId: > 0 } && client.GetChannel(origin.ChannelId) is IMessageChannel originChannel)
        {
            guild = (originChannel as SocketGuildChannel)?.Guild;
            return originChannel;
        }

        var selectedChannelId = ParseSnowflakeOrNull(SelectedChannelId);
        if (selectedChannelId is > 0 && client.GetChannel(selectedChannelId.Value) is IMessageChannel selected)
        {
            guild = (selected as SocketGuildChannel)?.Guild;
            return selected;
        }

        return null;
    }

    /// <summary>Parses a snowflake string; null when empty/invalid.</summary>
    private static ulong? ParseSnowflakeOrNull(string? value)
    {
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var snowflake) && snowflake > 0
            ? snowflake
            : null;
    }

    /// <inheritdoc />
    public async Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow message: bot {BotId} is not connected", BotId);
            return false;
        }

        var embed = TryBuildEmbedFromJson(embedJson);
        if (string.IsNullOrWhiteSpace(content) && embed == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow message: content and embed are both empty");
            return false;
        }

        var channel = ResolveWorkflowChannel(client, channelNameOrId, guildName, out _);
        if (channel == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow message: channel '{Channel}' was not found for bot {BotId}",
                channelNameOrId, BotId);
            return false;
        }

        try
        {
            var sent = await channel.SendMessageAsync(content ?? string.Empty, embed: embed).ConfigureAwait(false);
            RememberLastSentMessage(channel.Id, sent?.Id ?? 0);
            _logger.LogInformation("✅ Sent a workflow message to Discord channel {ChannelId}", channel.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send a workflow message to Discord channel {ChannelId}", channel.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendToChannelWithComponentsAsync(
        string channelNameOrId,
        string content,
        string? embedJson,
        string? buttonsJson,
        string? menuJson,
        string? componentPrefix,
        string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow message with components: bot {BotId} is not connected", BotId);
            return false;
        }

        // Interactive components NEED a "vyralwf:{workflowId}:{nodeId}" prefix:
        // without it their clicks could never dispatch back into a workflow, so
        // unusable parts are dropped (logged) instead of sent dead.
        var components = TryBuildWorkflowComponents(buttonsJson, menuJson, componentPrefix, out var componentsRequested);
        if (componentsRequested && components == null)
        {
            _logger.LogWarning(
                "❌ Workflow components dropped (missing '{Prefix}{{workflowId}}:{{nodeId}}' prefix or unparseable JSON) — sending without them",
                WorkflowComponentPrefix);
        }

        var embed = TryBuildEmbedFromJson(embedJson);
        if (string.IsNullOrWhiteSpace(content) && embed == null && components == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow message: content, embed and components are all empty");
            return false;
        }

        var channel = ResolveWorkflowChannel(client, channelNameOrId, guildName, out _);
        if (channel == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow message: channel '{Channel}' was not found for bot {BotId}",
                channelNameOrId, BotId);
            return false;
        }

        try
        {
            var sent = await channel.SendMessageAsync(content ?? string.Empty, embed: embed, components: components).ConfigureAwait(false);
            RememberLastSentMessage(channel.Id, sent?.Id ?? 0);
            _logger.LogInformation("✅ Sent a workflow message{ComponentsSuffix} to Discord channel {ChannelId}",
                components != null ? " with components" : string.Empty, channel.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send a workflow message to Discord channel {ChannelId}", channel.Id);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Workflow component (button / select menu) JSON → Discord components.
    // Schemas (node Config strings, tolerant of malformed JSON — bad parts
    // are skipped with a log, a send never fails over decoration):
    //   Buttons: [{"label":"Yes","style":"Success","emoji":"✅"}]
    //            style ∈ Primary|Secondary|Success|Danger|Link (needs "url")
    //   Menu:    {"placeholder":"Choose...","options":[{"label":"A","value":"a"}]}
    // Custom ids: "{prefix}:btn{i}" / "{prefix}:menu" where prefix is the
    // caller-supplied "vyralwf:{workflowId}:{nodeId}".
    // ------------------------------------------------------------------

    /// <summary>Discord caps one message's interactive buttons at 5.</summary>
    private const int MaxWorkflowButtons = 5;

    /// <summary>Discord caps select menus at 25 options.</summary>
    private const int MaxWorkflowMenuOptions = 25;

    /// <summary>Discord caps every component custom id at 100 characters.</summary>
    private const int MaxWorkflowCustomIdLength = 100;

    private const int MaxWorkflowButtonLabelLength = 80;
    private const int MaxWorkflowMenuPlaceholderLength = 100;
    private const int MaxWorkflowMenuOptionLabelLength = 100;
    private const int MaxWorkflowMenuOptionValueLength = 100;

    /// <summary>
    /// Builds message components from tolerant workflow JSON. Returns null when
    /// nothing usable was built — either nothing was requested or every part
    /// was dropped (unparseable JSON, missing prefix, oversized custom ids).
    /// </summary>
    private MessageComponent? TryBuildWorkflowComponents(string? buttonsJson, string? menuJson, string? componentPrefix, out bool componentsRequested)
    {
        componentsRequested = !string.IsNullOrWhiteSpace(buttonsJson) || !string.IsNullOrWhiteSpace(menuJson);
        if (!componentsRequested)
        {
            return null;
        }

        try
        {
            var builder = new ComponentBuilder();

            // Each part parses independently: malformed Buttons JSON must not
            // also cost the Menu (and vice versa).
            if (!string.IsNullOrWhiteSpace(buttonsJson))
            {
                try
                {
                    AddWorkflowButtons(builder, buttonsJson, componentPrefix);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Workflow buttons JSON could not be parsed — buttons are not sent");
                }
            }

            if (!string.IsNullOrWhiteSpace(menuJson))
            {
                try
                {
                    AddWorkflowSelectMenu(builder, menuJson, componentPrefix);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Workflow menu JSON could not be parsed — the menu is not sent");
                }
            }

            var components = builder.Build();
            return components.Components.Count > 0 ? components : null;
        }
        catch (Exception ex)
        {
            // Components are decoration: malformed JSON degrades to a plain send
            _logger.LogDebug(ex, "Workflow component JSON could not be parsed — sending without components");
            return null;
        }
    }

    /// <summary>Parses the Buttons JSON into up to 5 interactive buttons with custom ids "{prefix}:btn{i}".</summary>
    private void AddWorkflowButtons(ComponentBuilder builder, string buttonsJson, string? componentPrefix)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(buttonsJson);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (index >= MaxWorkflowButtons)
            {
                _logger.LogDebug("Workflow buttons JSON declares more than {Limit} buttons — the rest are not sent", MaxWorkflowButtons);
                break;
            }

            if (element.ValueKind != System.Text.Json.JsonValueKind.Object
                || !TryGetEmbedString(element, "label", out var label))
            {
                continue;
            }

            var style = ParseWorkflowButtonStyle(TryGetEmbedString(element, "style", out var styleName) ? styleName : null);

            var button = new ButtonBuilder()
                .WithLabel(ClampEmbedLength(label, MaxWorkflowButtonLabelLength));

            if (style == ButtonStyle.Link && TryGetEmbedString(element, "url", out var url))
            {
                // Link buttons navigate instead of interacting (no custom id)
                builder.WithButton(button.WithStyle(ButtonStyle.Link).WithUrl(url));
                index++;
                continue;
            }

            if (style == ButtonStyle.Link)
            {
                style = ButtonStyle.Secondary; // Discord rejects a Link button without a URL — degrade to Secondary
            }

            var customId = BuildWorkflowComponentCustomId(componentPrefix, $"btn{index}");
            if (customId == null)
            {
                _logger.LogDebug("Workflow button '{Label}' skipped: no usable custom id (missing/oversized prefix)", label);
                continue;
            }

            if (TryGetEmbedString(element, "emoji", out var emoji))
            {
                TryApplyComponentEmoji(button, emoji);
            }

            builder.WithButton(button.WithStyle(style).WithCustomId(customId));
            index++;
        }
    }

    /// <summary>
    /// Parses the Menu JSON into one select menu with the custom id
    /// "{prefix}:menu"; a bare options array (without the wrapper object) also works.
    /// </summary>
    private void AddWorkflowSelectMenu(ComponentBuilder builder, string menuJson, string? componentPrefix)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(menuJson);
        var root = doc.RootElement;

        string? placeholder = null;
        System.Text.Json.JsonElement optionsElement = default;
        var hasOptions = false;
        if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (TryGetEmbedString(root, "placeholder", out var placeholderText))
            {
                placeholder = placeholderText;
            }

            hasOptions = root.TryGetProperty("options", out optionsElement)
                         && optionsElement.ValueKind == System.Text.Json.JsonValueKind.Array;
        }
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            optionsElement = root;
            hasOptions = true;
        }

        if (!hasOptions)
        {
            return;
        }

        var customId = BuildWorkflowComponentCustomId(componentPrefix, "menu");
        if (customId == null)
        {
            _logger.LogDebug("Workflow select menu skipped: no usable custom id (missing/oversized prefix)");
            return;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId(customId)
            .WithMinValues(1)
            .WithMaxValues(1);
        if (placeholder != null)
        {
            menu.WithPlaceholder(ClampEmbedLength(placeholder, MaxWorkflowMenuPlaceholderLength));
        }

        var optionCount = 0;
        foreach (var option in optionsElement.EnumerateArray())
        {
            if (optionCount >= MaxWorkflowMenuOptions)
            {
                _logger.LogDebug("Workflow menu JSON declares more than {Limit} options — the rest are not sent", MaxWorkflowMenuOptions);
                break;
            }

            if (option.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            TryGetEmbedString(option, "label", out var optionLabel);
            TryGetEmbedString(option, "value", out var optionValue);
            if (optionLabel.Length == 0 && optionValue.Length == 0)
            {
                continue;
            }

            if (optionLabel.Length == 0)
            {
                optionLabel = optionValue; // a value-only option still needs a visible label
            }
            else if (optionValue.Length == 0)
            {
                optionValue = optionLabel;
            }

            menu.AddOption(
                ClampEmbedLength(optionLabel, MaxWorkflowMenuOptionLabelLength),
                ClampEmbedLength(optionValue, MaxWorkflowMenuOptionValueLength));
            optionCount++;
        }

        if (optionCount > 0)
        {
            builder.WithSelectMenu(menu);
        }
    }

    /// <summary>Maps a workflow button style name to Discord's styles (default Primary).</summary>
    private static ButtonStyle ParseWorkflowButtonStyle(string? styleName)
    {
        return (styleName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "secondary" or "grey" or "gray" => ButtonStyle.Secondary,
            "success" or "green" => ButtonStyle.Success,
            "danger" or "red" => ButtonStyle.Danger,
            "link" => ButtonStyle.Link,
            _ => ButtonStyle.Primary
        };
    }

    /// <summary>
    /// Custom id for a workflow component: "{prefix}:{suffix}" where prefix is
    /// the "vyralwf:{workflowId}:{nodeId}" stem the engine supplies. A prefix
    /// missing the "vyralwf:" namespace gets it prepended so the component
    /// gateway filter accepts the clicks. Null when there is no usable prefix
    /// or the result would exceed Discord's 100-character custom-id limit.
    /// </summary>
    private static string? BuildWorkflowComponentCustomId(string? componentPrefix, string suffix)
    {
        var prefix = (componentPrefix ?? string.Empty).Trim();
        if (prefix.Length == 0)
        {
            return null;
        }

        if (!prefix.StartsWith(WorkflowComponentPrefix, StringComparison.Ordinal))
        {
            prefix = WorkflowComponentPrefix + prefix;
        }

        var customId = $"{prefix}:{suffix}";
        return customId.Length <= MaxWorkflowCustomIdLength ? customId : null;
    }

    /// <summary>Applies a unicode or custom ("name:id") emoji to a button (best-effort; unusable emoji never blocks the send).</summary>
    private static void TryApplyComponentEmoji(ButtonBuilder button, string emoji)
    {
        try
        {
            if (Emote.TryParse(emoji.Trim(), out var custom) && custom != null)
            {
                button.WithEmote(custom);
            }
            else
            {
                button.WithEmote(new Emoji(emoji.Trim()));
            }
        }
        catch (Exception)
        {
            // Skip — the emoji is optional decoration
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow DM: bot {BotId} is not connected", BotId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("❌ Cannot send a workflow DM: username is empty");
            return false;
        }

        var embed = TryBuildEmbedFromJson(embedJson);
        if (string.IsNullOrWhiteSpace(content) && embed == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow DM: content and embed are both empty");
            return false;
        }

        var user = await ResolveDmUserAsync(client, username.Trim(), guildName).ConfigureAwait(false);
        if (user == null)
        {
            _logger.LogWarning("❌ Cannot send a workflow DM: user '{Username}' was not found for bot {BotId}", username, BotId);
            return false;
        }

        try
        {
            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
            if (dmChannel == null)
            {
                _logger.LogWarning("❌ Cannot send a workflow DM: no DM channel for {Username}", username);
                return false;
            }

            var sent = await dmChannel.SendMessageAsync(content ?? string.Empty, embed: embed).ConfigureAwait(false);
            RememberLastSentMessage(dmChannel.Id, sent?.Id ?? 0);
            _logger.LogInformation("✅ Sent a workflow DM to {Username}", username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send a workflow DM to {Username}", username);
            return false;
        }
    }

    /// <summary>
    /// Resolves a DM target by name: guild members across the bot's guilds
    /// (the explicit guild first), then the seen-bots cache with a REST user
    /// fetch (external bot accounts are often missing from the partial member
    /// cache). Null when nobody matches.
    /// </summary>
    private async Task<IUser?> ResolveDmUserAsync(DiscordSocketClient client, string username, string? guildName)
    {
        var scopedGuilds = string.IsNullOrWhiteSpace(guildName)
            ? client.Guilds
            : client.Guilds.Where(g => string.Equals(g.Name, guildName.Trim(), StringComparison.OrdinalIgnoreCase));

        foreach (var guild in scopedGuilds)
        {
            var member = await ResolveGuildMemberAsync(guild, username).ConfigureAwait(false);
            if (member != null)
            {
                return member;
            }
        }

        var seen = SeenBots.FirstOrDefault(b =>
            string.Equals(b.Username, username, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.DisplayName, username, StringComparison.OrdinalIgnoreCase));
        if (seen != null
            && ulong.TryParse(seen.BotId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seenId))
        {
            return await TryGetRestUserAsync(client, seenId).ConfigureAwait(false);
        }

        return null;
    }

    /// <inheritdoc />
    /// <summary>
    /// DM channels this bot has actually received a message on, keyed by
    /// channel snowflake. Discord.Net's GetChannel lookup misses DM channels,
    /// so replies reuse the live channel instance captured on receipt.
    /// </summary>
    private readonly ConcurrentDictionary<ulong, (IMessageChannel Channel, ulong RecipientUserId)> _knownDmChannels = new();

    public async Task<bool> SendDmToChannelAsync(string channelId, string content, string? embedJson = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot send a DM by channel id: bot {BotId} is not connected", BotId);
            return false;
        }

        var channelSnowflake = ParseSnowflakeOrNull(channelId);
        if (channelSnowflake is not > 0)
        {
            _logger.LogWarning("❌ Cannot send a DM by channel id: '{ChannelId}' is not a valid snowflake", channelId);
            return false;
        }

        // Discord.Net's DM cache is unreliable for lookup by id, so prefer the
        // live channel object remembered when this bot last RECEIVED a DM on
        // this channel, then the gateway cache, then create a fresh DM channel
        // from the remembered recipient user id.
        if (_knownDmChannels.TryGetValue(channelSnowflake.Value, out var known)
            && await SendViaChannelAsync(known.Channel, channelId, content, embedJson).ConfigureAwait(false))
        {
            return true;
        }

        IMessageChannel? channel = client.GetChannel(channelSnowflake.Value) as IMessageChannel;
        if (channel == null)
        {
            var recipientId = _knownDmChannels.TryGetValue(channelSnowflake.Value, out var remembered)
                ? remembered.RecipientUserId
                : 0;
            if (recipientId > 0)
            {
                try
                {
                    global::Discord.IUser? user = client.GetUser(recipientId);
                    if (user == null)
                    {
                        user = await client.Rest.GetUserAsync(recipientId).ConfigureAwait(false);
                    }
                    if (user is global::Discord.Rest.RestUser restUser)
                    {
                        var created = await restUser.CreateDMChannelAsync().ConfigureAwait(false);
                        if (created is IMessageChannel createdChannel)
                        {
                            _knownDmChannels[channelSnowflake.Value] = (createdChannel, recipientId);
                            channel = createdChannel;
                            _logger.LogInformation("ℹ️ DM channel {ChannelId} recreated for user {UserId} on bot {BotId}",
                                channelId, recipientId, BotId);
                        }
                    }
                    else if (user is global::Discord.WebSocket.SocketUser socketUser)
                    {
                        var created = await socketUser.CreateDMChannelAsync().ConfigureAwait(false);
                        if (created is IMessageChannel createdChannel)
                        {
                            _knownDmChannels[channelSnowflake.Value] = (createdChannel, recipientId);
                            channel = createdChannel;
                            _logger.LogInformation("ℹ️ DM channel {ChannelId} recreated for user {UserId} on bot {BotId}",
                                channelId, recipientId, BotId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ Could not recreate the DM channel {ChannelId} for bot {BotId}",
                        channelId, BotId);
                }
            }
        }

        if (channel == null)
        {
            _logger.LogWarning("❌ Cannot send a DM by channel id: channel {ChannelId} was not found (or is not a message channel) for bot {BotId}",
                channelSnowflake.Value, BotId);
            return false;
        }

        return await SendViaChannelAsync(channel, channelId, content, embedJson).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends through a resolved DM channel, validating content/embed and
    /// translating failures into the boolean contract used by DM routing.
    /// </summary>
    private async Task<bool> SendViaChannelAsync(IMessageChannel channel, string channelId, string? content, string? embedJson)
    {
        var embed = TryBuildEmbedFromJson(embedJson);
        if (string.IsNullOrWhiteSpace(content) && embed == null)
        {
            _logger.LogWarning("❌ Cannot send a DM by channel id: content and embed are both empty");
            return false;
        }

        try
        {
            await channel.SendMessageAsync(content ?? string.Empty, embed: embed).ConfigureAwait(false);
            _logger.LogInformation("✅ Sent a DM to Discord channel {ChannelId}", channelId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Failed to send a DM to Discord channel {ChannelId}", channelId);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
        => ModifyWorkflowUserRoleAsync(username, roleIdOrName, grant: true, reason, guildName);

    /// <inheritdoc />
    public Task<bool> RemoveRoleFromUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
        => ModifyWorkflowUserRoleAsync(username, roleIdOrName, grant: false, reason, guildName);

    /// <summary>Shared workflow grant/remove role core (name-based, no guild ids required).</summary>
    private async Task<bool> ModifyWorkflowUserRoleAsync(
        string username, string roleIdOrName, bool grant, string? reason, string? guildName)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot {Verb} a workflow role: bot {BotId} is not connected", grant ? "grant" : "remove", BotId);
            return false;
        }

        var guild = ResolveWorkflowGuild(client, guildName);
        if (guild == null || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(roleIdOrName))
        {
            _logger.LogWarning("❌ Cannot {Verb} a workflow role: no guild or an empty user/role name", grant ? "grant" : "remove");
            return false;
        }

        var role = ResolveWorkflowRole(guild, roleIdOrName);
        if (role == null)
        {
            _logger.LogWarning("❌ Cannot {Verb} a workflow role: role '{Role}' was not found in {Guild}",
                grant ? "grant" : "remove", roleIdOrName, guild.Name);
            return false;
        }

        var member = await ResolveGuildMemberAsync(guild, username.Trim()).ConfigureAwait(false);
        if (member == null)
        {
            _logger.LogWarning("❌ Cannot {Verb} a workflow role: user '{Username}' was not found in {Guild}",
                grant ? "grant" : "remove", username, guild.Name);
            return false;
        }

        try
        {
            var options = string.IsNullOrWhiteSpace(reason)
                ? null
                : new RequestOptions { AuditLogReason = reason.Trim() };

            if (grant)
            {
                await member.AddRoleAsync(role, options).ConfigureAwait(false);
            }
            else
            {
                await member.RemoveRoleAsync(role, options).ConfigureAwait(false);
            }

            _logger.LogInformation("✅ {Verb} role '{Role}' {Direction} @{User} in {Guild}",
                grant ? "Granted" : "Removed", role.Name, grant ? "to" : "from", member.Username, guild.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to {Verb} role '{Role}' for @{User} in {Guild}",
                grant ? "grant" : "remove", role.Name, member.Username, guild.Name);
            return false;
        }
    }

    /// <summary>Resolves a role by snowflake id or (case-insensitive) name.</summary>
    private static SocketRole? ResolveWorkflowRole(SocketGuild guild, string roleIdOrName)
    {
        var query = roleIdOrName.Trim();
        return guild.Roles.FirstOrDefault(r => r.Id.ToString() == query)
               ?? guild.Roles.FirstOrDefault(r => string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<bool> CreateWorkflowChannelAsync(string name, string channelType, string? category, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot create a workflow channel: bot {BotId} is not connected", BotId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("❌ Cannot create a workflow channel: name is empty");
            return false;
        }

        var guild = ResolveWorkflowGuild(client, guildName);
        if (guild == null)
        {
            _logger.LogWarning("❌ Cannot create a workflow channel: bot {BotId} is in no guild", BotId);
            return false;
        }

        var kind = (channelType ?? "Text").Trim().ToLowerInvariant();
        var categoryId = ResolveWorkflowCategory(guild, category)?.Id;

        try
        {
            switch (kind)
            {
                case "text":
                    await guild.CreateTextChannelAsync(name, props => props.CategoryId = categoryId).ConfigureAwait(false);
                    break;
                case "voice":
                    await guild.CreateVoiceChannelAsync(name, props => props.CategoryId = categoryId).ConfigureAwait(false);
                    break;
                case "forum":
                    await guild.CreateForumChannelAsync(name, props => props.CategoryId = categoryId).ConfigureAwait(false);
                    break;
                default:
                    _logger.LogWarning("❌ Cannot create a workflow channel: unknown channel type '{Type}' (Text|Voice|Forum)", channelType);
                    return false;
            }

            _logger.LogInformation("✅ Created a {Type} channel '{Name}' in {Guild}", channelType, name, guild.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create a {Type} channel '{Name}' in {Guild}", channelType, name, guild.Name);
            return false;
        }
    }

    /// <summary>Resolves a category by snowflake id or (case-insensitive) name; null when absent/unknown.</summary>
    private static SocketCategoryChannel? ResolveWorkflowCategory(SocketGuild guild, string? category)
    {
        var query = category?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        return guild.CategoryChannels.FirstOrDefault(c => c.Id.ToString() == query)
               ?? guild.CategoryChannels.FirstOrDefault(c => string.Equals(c.Name, query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the message a workflow scope operation targets ("trigger" /
    /// "last_bot"). Returns null (logged) for unknown scopes or when nothing
    /// resolvable exists. "trigger" prefers the interaction/message that most
    /// recently started a run and falls back to the newest message authored by
    /// somebody else in the origin/selected channel.
    /// </summary>
    private async Task<(IMessageChannel Channel, IMessage Message)?> ResolveScopedMessageAsync(
        DiscordSocketClient client, string scope)
    {
        var normalizedScope = (scope ?? string.Empty).Trim().ToLowerInvariant();
        var origin = GetWorkflowOrigin();

        if (normalizedScope == "last_bot")
        {
            var channelId = origin is { ChannelId: > 0 }
                ? origin.ChannelId
                : ParseSnowflakeOrNull(SelectedChannelId);
            if (channelId is not ulong lastBotChannelId
                || !_lastSentByChannel.TryGetValue(lastBotChannelId, out var lastSentId))
            {
                _logger.LogWarning("❌ Workflow scope '{Scope}': no sent message is tracked for bot {BotId}", normalizedScope, BotId);
                return null;
            }

            if (client.GetChannel(lastBotChannelId) is not IMessageChannel channel
                || await channel.GetMessageAsync(lastSentId).ConfigureAwait(false) is not IMessage message)
            {
                _logger.LogWarning("❌ Workflow scope '{Scope}': tracked message {MessageId} is gone", normalizedScope, lastSentId);
                return null;
            }

            return (channel, message);
        }

        if (normalizedScope == "trigger")
        {
            // 1) The interaction/message that most recently started a run
            //    (component interactions carry the component's message id)
            if (origin is { MessageId: > 0 } && client.GetChannel(origin.ChannelId) is IMessageChannel originChannel)
            {
                var tracked = await originChannel.GetMessageAsync(origin.MessageId).ConfigureAwait(false);
                if (tracked != null
                    && (client.CurrentUser == null || tracked.Author?.Id != client.CurrentUser.Id))
                {
                    return (originChannel, tracked);
                }
            }

            // 2) Fallback: the newest non-self message in the origin/selected channel
            var channelId = origin is { ChannelId: > 0 }
                ? origin.ChannelId
                : ParseSnowflakeOrNull(SelectedChannelId);
            if (channelId is ulong fallbackChannelId
                && client.GetChannel(fallbackChannelId) is IMessageChannel fallbackChannel)
            {
                var messages = await fallbackChannel.GetMessagesAsync(10).FlattenAsync().ConfigureAwait(false);
                var newest = messages.FirstOrDefault(m =>
                    client.CurrentUser == null || m.Author?.Id != client.CurrentUser.Id);
                if (newest != null)
                {
                    return (fallbackChannel, newest);
                }
            }

            _logger.LogWarning("❌ Workflow scope '{Scope}': no trigger message is tracked for bot {BotId}", normalizedScope, BotId);
            return null;
        }

        _logger.LogWarning("❌ Unknown workflow message scope '{Scope}' (trigger|last_bot)", scope);
        return null;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteMessageByScopeAsync(string scope, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot delete a workflow message: bot {BotId} is not connected", BotId);
            return false;
        }

        try
        {
            var scoped = await ResolveScopedMessageAsync(client, scope).ConfigureAwait(false);
            if (scoped == null)
            {
                return false;
            }

            await scoped.Value.Message.DeleteAsync().ConfigureAwait(false);
            _logger.LogInformation("✅ Deleted workflow message {MessageId} (scope: {Scope})", scoped.Value.Message.Id, scope);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete a workflow message (scope: {Scope})", scope);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> EditLastBotMessageAsync(string newContent, string? embedJson, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot edit the last workflow message: bot {BotId} is not connected", BotId);
            return false;
        }

        var embed = TryBuildEmbedFromJson(embedJson);
        var text = newContent ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) && embed == null)
        {
            _logger.LogWarning("❌ Cannot edit the last workflow message: content and embed are both empty");
            return false;
        }

        var origin = GetWorkflowOrigin();
        var channelId = origin is { ChannelId: > 0 }
            ? origin.ChannelId
            : ParseSnowflakeOrNull(SelectedChannelId);
        if (channelId is not ulong editChannelId
            || !_lastSentByChannel.TryGetValue(editChannelId, out var lastSentId)
            || client.GetChannel(editChannelId) is not IMessageChannel channel)
        {
            _logger.LogWarning("❌ Cannot edit the last workflow message: no sent message is tracked for bot {BotId}", BotId);
            return false;
        }

        try
        {
            if (await channel.GetMessageAsync(lastSentId).ConfigureAwait(false) is not IUserMessage message)
            {
                _logger.LogWarning("❌ Cannot edit the last workflow message: tracked message {MessageId} is gone", lastSentId);
                return false;
            }

            await message.ModifyAsync(props =>
            {
                props.Content = text;
                props.Embed = embed;
            }).ConfigureAwait(false);

            _logger.LogInformation("✅ Edited the last workflow message {MessageId}", lastSentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to edit the last workflow message {MessageId}", lastSentId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReactToMessageAsync(string emoji, string scope, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot react to a workflow message: bot {BotId} is not connected", BotId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(emoji))
        {
            _logger.LogWarning("❌ Cannot react to a workflow message: emoji is empty");
            return false;
        }

        try
        {
            var scoped = await ResolveScopedMessageAsync(client, scope).ConfigureAwait(false);
            if (scoped == null)
            {
                return false;
            }

            // Custom emotes ("name:id") need Emote; unicode emoji goes through Emoji.
            IEmote emote = Emote.TryParse(emoji.Trim(), out var custom) && custom != null
                ? custom
                : new Emoji(emoji.Trim());

            await scoped.Value.Message.AddReactionAsync(emote).ConfigureAwait(false);
            _logger.LogInformation("✅ Reacted to workflow message {MessageId} (scope: {Scope})", scoped.Value.Message.Id, scope);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to react to a workflow message (scope: {Scope})", scope);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CreateThreadFromLastAsync(string threadName, int autoArchiveMinutes, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot create a workflow thread: bot {BotId} is not connected", BotId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(threadName))
        {
            _logger.LogWarning("❌ Cannot create a workflow thread: name is empty");
            return false;
        }

        var origin = GetWorkflowOrigin();
        var channelId = origin is { ChannelId: > 0 }
            ? origin.ChannelId
            : ParseSnowflakeOrNull(SelectedChannelId);
        if (channelId is not ulong threadChannelId
            || !_lastSentByChannel.TryGetValue(threadChannelId, out var lastSentId)
            || client.GetChannel(threadChannelId) is not SocketTextChannel channel)
        {
            _logger.LogWarning("❌ Cannot create a workflow thread: no sent message is tracked in a text channel for bot {BotId}", BotId);
            return false;
        }

        var lastMessage = await channel.GetMessageAsync(lastSentId).ConfigureAwait(false);
        if (lastMessage is null)
        {
            _logger.LogWarning("❌ Cannot create a workflow thread: tracked message {MessageId} no longer exists", lastSentId);
            return false;
        }

        try
        {
            await channel.CreateThreadAsync(
                threadName.Trim(),
                ThreadType.PublicThread,
                ClampArchiveDuration(autoArchiveMinutes),
                lastMessage).ConfigureAwait(false);

            _logger.LogInformation("✅ Created thread '{Thread}' from message {MessageId}", threadName, lastSentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create thread '{Thread}' from message {MessageId}", threadName, lastSentId);
            return false;
        }
    }

    /// <summary>Clamps minutes to Discord's thread archive durations (60 / 1440 / 4320 / 10080).</summary>
    private static ThreadArchiveDuration ClampArchiveDuration(int minutes)
    {
        if (minutes <= 60)
        {
            return ThreadArchiveDuration.OneHour;
        }

        if (minutes <= 1440)
        {
            return ThreadArchiveDuration.OneDay;
        }

        if (minutes <= 4320)
        {
            return ThreadArchiveDuration.ThreeDays;
        }

        return ThreadArchiveDuration.OneWeek;
    }

    /// <inheritdoc />
    public async Task<bool> SetChannelLockAsync(bool locked, string? reason, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            _logger.LogWarning("❌ Cannot {State} the workflow channel: bot {BotId} is not connected",
                locked ? "lock" : "unlock", BotId);
            return false;
        }

        var channel = ResolveWorkflowChannel(client, null, guildName, out var guild);
        if (channel is not SocketGuildChannel guildChannel || guild == null)
        {
            _logger.LogWarning("❌ Cannot {State} the workflow channel: no guild channel is selected for bot {BotId}",
                locked ? "lock" : "unlock", BotId);
            return false;
        }

        try
        {
            // Overwrite @everyone's SendMessages on the origin/selected channel,
            // preserving every other permission the overwrite already carries.
            var everyone = guild.EveryoneRole;
            var existing = guildChannel.PermissionOverwrites.FirstOrDefault(o =>
                o.TargetId == everyone.Id && o.TargetType == PermissionTarget.Role);
            var hasExisting = existing.TargetId == everyone.Id && existing.TargetType == PermissionTarget.Role;
            var permissions = (hasExisting
                    ? existing.Permissions
                    : OverwritePermissions.AllowAll(guildChannel))
                .Modify(sendMessages: locked ? PermValue.Deny : PermValue.Inherit);

            var options = string.IsNullOrWhiteSpace(reason)
                ? null
                : new RequestOptions { AuditLogReason = reason.Trim() };

            await guildChannel.AddPermissionOverwriteAsync(everyone, permissions, options).ConfigureAwait(false);

            _logger.LogInformation("✅ {State} SendMessages for @everyone in #{Channel} ({Guild})",
                locked ? "Denied" : "Restored", guildChannel.Name, guild.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to {State} the workflow channel #{Channel}",
                locked ? "lock" : "unlock", guildChannel.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UserHasRoleAsync(string username, string roleIdOrName, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return false;
        }

        var guild = ResolveWorkflowGuild(client, guildName);
        if (guild == null || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(roleIdOrName))
        {
            return false;
        }

        var role = ResolveWorkflowRole(guild, roleIdOrName);
        if (role == null)
        {
            return false;
        }

        try
        {
            var member = await ResolveGuildMemberAsync(guild, username.Trim()).ConfigureAwait(false);
            return member != null && member.RoleIds.Contains(role.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Failed to check the role '{Role}' for user '{Username}' in {Guild}",
                roleIdOrName, username, guild.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UserHasPermissionAsync(string username, string permission, string? guildName = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return false;
        }

        var guild = ResolveWorkflowGuild(client, guildName);
        if (guild == null || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        if (!Enum.TryParse<GuildPermission>(permission.Trim(), ignoreCase: true, out var permissionValue))
        {
            _logger.LogWarning("❌ Unknown Discord permission '{Permission}' requested by a workflow", permission);
            return false;
        }

        try
        {
            var member = await ResolveGuildMemberAsync(guild, username.Trim()).ConfigureAwait(false);
            return member != null && member.GuildPermissions.Has(permissionValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Failed to check the permission '{Permission}' for user '{Username}' in {Guild}",
                permission, username, guild.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetWorkflowRoleNamesAsync(string? guildName = null)
    {
        IReadOnlyList<string> names = Array.Empty<string>();
        var client = GetConnectedClient();
        var guild = client != null ? ResolveWorkflowGuild(client, guildName) : null;
        if (guild != null)
        {
            try
            {
                // Managed roles (integrations) and @everyone are not pickable
                names = guild.Roles
                    .Where(r => !r.IsManaged
                                && r.Id != guild.EveryoneRole.Id
                                && !string.Equals(r.Name, "@everyone", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ Failed to list roles for the workflow editor (bot {BotId})", BotId);
                names = Array.Empty<string>();
            }
        }

        return Task.FromResult(names);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetWorkflowChannelNamesAsync(string? guildName = null)
    {
        IReadOnlyList<string> names = Array.Empty<string>();
        var client = GetConnectedClient();
        var guild = client != null ? ResolveWorkflowGuild(client, guildName) : null;
        if (guild != null)
        {
            try
            {
                names = guild.TextChannels
                    .Select(c => c.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ Failed to list channels for the workflow editor (bot {BotId})", BotId);
                names = Array.Empty<string>();
            }
        }

        return Task.FromResult(names);
    }

    // ------------------------------------------------------------------
    // Name-based moderation for the workflow engine (additive surface).
    // Each method reuses the existing snowflake-based moderation methods —
    // including their permission checks and error descriptions — by resolving
    // the guild/user/channel first, then delegating. A call is successful when
    // the delegated result reports success ("✅") or an informational no-op
    // ("ℹ️", e.g. purging an already-empty channel); everything else logs and
    // returns false. Never throws.
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<bool> KickMemberByNameAsync(string username, string? reason, string? guildName = null)
        => await RunModerationAsync(
            $"kick '{username}'", async () =>
            {
                var (client, guild) = ResolveModerationGuildOrWarn(guildName, "kick");
                if (guild is null)
                {
                    return "❌ No server available.";
                }

                var member = await ResolveGuildMemberAsync(guild, username?.Trim() ?? string.Empty).ConfigureAwait(false);
                if (member is null)
                {
                    return $"❌ User '{username}' is not a member of {guild.Name}.";
                }

                return await KickMemberAsync(guild.Id.ToString(CultureInfo.InvariantCulture), member.Id.ToString(CultureInfo.InvariantCulture), reason).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> BanMemberByNameAsync(string username, string? reason, string? guildName = null)
        => await RunModerationAsync(
            $"ban '{username}'", async () =>
            {
                var (client, guild) = ResolveModerationGuildOrWarn(guildName, "ban");
                if (guild is null)
                {
                    return "❌ No server available.";
                }

                var member = await ResolveGuildMemberAsync(guild, username?.Trim() ?? string.Empty).ConfigureAwait(false);
                if (member is null)
                {
                    return $"❌ User '{username}' was not found in {guild.Name}.";
                }

                return await BanMemberAsync(guild.Id.ToString(CultureInfo.InvariantCulture), member.Id.ToString(CultureInfo.InvariantCulture), reason).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> UnbanMemberByNameAsync(string username, string? guildName = null)
        => await RunModerationAsync(
            $"unban '{username}'", async () =>
            {
                var (client, guild) = ResolveModerationGuildOrWarn(guildName, "unban");
                if (guild is null || client is null)
                {
                    return "❌ No server available.";
                }

                // Banned users are no longer guild members, so the member cache
                // cannot see them — reuse the DM resolution (members across the
                // bot's guilds + the seen-bots REST fallback) instead.
                var user = await ResolveDmUserAsync(client, username?.Trim() ?? string.Empty, guildName).ConfigureAwait(false);
                if (user is null)
                {
                    return $"❌ User '{username}' could not be resolved.";
                }

                return await UnbanMemberAsync(guild.Id.ToString(CultureInfo.InvariantCulture), user.Id.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> TimeoutMemberByNameAsync(string username, int minutes, string? reason, string? guildName = null)
        => await RunModerationAsync(
            $"timeout '{username}'", async () =>
            {
                var (client, guild) = ResolveModerationGuildOrWarn(guildName, "timeout");
                if (guild is null)
                {
                    return "❌ No server available.";
                }

                var member = await ResolveGuildMemberAsync(guild, username?.Trim() ?? string.Empty).ConfigureAwait(false);
                if (member is null)
                {
                    return $"❌ User '{username}' is not a member of {guild.Name}.";
                }

                return await TimeoutMemberAsync(guild.Id.ToString(CultureInfo.InvariantCulture), member.Id.ToString(CultureInfo.InvariantCulture), minutes, reason).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> RemoveTimeoutMemberByNameAsync(string username, string? guildName = null)
        => await RunModerationAsync(
            $"remove timeout for '{username}'", async () =>
            {
                var (client, guild) = ResolveModerationGuildOrWarn(guildName, "remove timeout");
                if (guild is null)
                {
                    return "❌ No server available.";
                }

                var member = await ResolveGuildMemberAsync(guild, username?.Trim() ?? string.Empty).ConfigureAwait(false);
                if (member is null)
                {
                    return $"❌ User '{username}' is not a member of {guild.Name}.";
                }

                return await RemoveTimeoutAsync(guild.Id.ToString(CultureInfo.InvariantCulture), member.Id.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> PurgeOriginChannelAsync(int count, string? guildName = null)
        => await RunModerationAsync(
            $"purge {count} message(s)", async () =>
            {
                var client = GetConnectedClient();
                var channel = client != null ? ResolveWorkflowChannel(client, null, guildName, out _) : null;
                if (channel is not SocketTextChannel textChannel)
                {
                    _logger.LogWarning("❌ Cannot purge: no origin/selected text channel for bot {BotId}", BotId);
                    return "❌ No text channel available.";
                }

                return await PurgeMessagesAsync(textChannel.Id.ToString(CultureInfo.InvariantCulture), count).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> SetSlowmodeOnOriginChannelAsync(int seconds, string? guildName = null)
        => await RunModerationAsync(
            $"set slowmode to {seconds}s", async () =>
            {
                var client = GetConnectedClient();
                var channel = client != null ? ResolveWorkflowChannel(client, null, guildName, out _) : null;
                if (channel is not SocketTextChannel textChannel)
                {
                    _logger.LogWarning("❌ Cannot set slowmode: no origin/selected text channel for bot {BotId}", BotId);
                    return "❌ No text channel available.";
                }

                return await SetSlowmodeAsync(textChannel.Id.ToString(CultureInfo.InvariantCulture), seconds).ConfigureAwait(false);
            }).ConfigureAwait(false);

    /// <summary>
    /// Guild for a name-based moderation call (the workflow guild resolution),
    /// with a warning when the bot is in no server. The client is echoed back
    /// because the unban path needs it for cross-guild user resolution.
    /// </summary>
    private (DiscordSocketClient? Client, SocketGuild? Guild) ResolveModerationGuildOrWarn(string? guildName, string operation)
    {
        var client = GetConnectedClient();
        var guild = client != null ? ResolveWorkflowGuild(client, guildName) : null;
        if (guild is null)
        {
            _logger.LogWarning("❌ Cannot {Operation} via the workflow bridge: bot {BotId} is not connected or in no server",
                operation, BotId);
        }

        return (client, guild);
    }

    /// <summary>
    /// Runs one delegated moderation call, turning its human-readable result
    /// into the workflow surface's bool: "✅" (success) or "ℹ️" (informational
    /// no-op) count as success; anything else is logged and reported as false.
    /// </summary>
    private async Task<bool> RunModerationAsync(string operation, Func<Task<string>> call)
    {
        try
        {
            var result = await call().ConfigureAwait(false);
            if (result.StartsWith("✅", StringComparison.Ordinal) || result.StartsWith("ℹ️", StringComparison.Ordinal))
            {
                _logger.LogInformation("✅ Workflow moderation ({Operation}): {Result}", operation, result);
                return true;
            }

            _logger.LogWarning("❌ Workflow moderation ({Operation}) failed: {Result}", operation, result);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Workflow moderation ({Operation}) threw", operation);
            return false;
        }
    }

    /// <summary>
    /// Builds a Discord embed from tolerant workflow JSON:
    /// {title, description, color:"#5865F2", footer, footerIconUrl, imageUrl,
    /// thumbnailUrl, authorName, authorUrl, authorIconUrl,
    /// fields:[{name, value, inline}]}. Missing fields are skipped; malformed
    /// JSON or an embed with no usable content returns null (a plain-text send
    /// is then still possible).
    /// </summary>
    private static Embed? TryBuildEmbedFromJson(string? embedJson)
    {
        if (string.IsNullOrWhiteSpace(embedJson))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(embedJson);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            var builder = new EmbedBuilder();

            if (TryGetEmbedString(root, "title", out var title))
            {
                builder.Title = ClampEmbedLength(title, EmbedBuilder.MaxTitleLength);
            }

            if (TryGetEmbedString(root, "description", out var description))
            {
                builder.Description = ClampEmbedLength(description, EmbedBuilder.MaxDescriptionLength);
            }

            if (TryGetEmbedString(root, "color", out var color) && TryParseEmbedColor(color, out var discordColor))
            {
                builder.Color = discordColor;
            }

            if (TryGetEmbedString(root, "footer", out var footer))
            {
                builder.Footer ??= new EmbedFooterBuilder();
                builder.Footer.Text = ClampEmbedLength(footer, EmbedFooterBuilder.MaxFooterTextLength);
                if (TryGetEmbedString(root, "footerIconUrl", out var footerIconUrl))
                {
                    builder.Footer.IconUrl = footerIconUrl;
                }
            }

            if (TryGetEmbedString(root, "imageUrl", out var imageUrl))
            {
                builder.ImageUrl = imageUrl;
            }

            if (TryGetEmbedString(root, "thumbnailUrl", out var thumbnailUrl))
            {
                builder.ThumbnailUrl = thumbnailUrl;
            }

            if (TryGetEmbedString(root, "authorName", out var authorName))
            {
                var author = new EmbedAuthorBuilder
                {
                    Name = ClampEmbedLength(authorName, EmbedAuthorBuilder.MaxAuthorNameLength)
                };
                if (TryGetEmbedString(root, "authorUrl", out var authorUrl))
                {
                    author.Url = authorUrl;
                }

                if (TryGetEmbedString(root, "authorIconUrl", out var authorIconUrl))
                {
                    author.IconUrl = authorIconUrl;
                }

                builder.Author = author;
            }

            if (root.TryGetProperty("fields", out var fields)
                && fields.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var field in fields.EnumerateArray())
                {
                    if (builder.Fields.Count >= EmbedBuilder.MaxFieldCount)
                    {
                        break;
                    }

                    if (field.ValueKind != System.Text.Json.JsonValueKind.Object
                        || !TryGetEmbedString(field, "name", out var fieldName))
                    {
                        continue;
                    }

                    TryGetEmbedString(field, "value", out var fieldValue);
                    builder.AddField(new EmbedFieldBuilder()
                        .WithName(ClampEmbedLength(fieldName, EmbedFieldBuilder.MaxFieldNameLength))
                        .WithValue(ClampEmbedLength(fieldValue ?? string.Empty, EmbedFieldBuilder.MaxFieldValueLength))
                        .WithIsInline(IsInlineField(field)));
                }
            }

            // An embed with nothing set is not buildable (Discord requires at
            // least one element) — treat it as "no embed" so plain text still sends.
            if (builder.Title == null
                && builder.Description == null
                && builder.Footer == null
                && builder.Author == null
                && builder.ImageUrl == null
                && builder.ThumbnailUrl == null
                && builder.Fields.Count == 0)
            {
                return null;
            }

            return builder.Build();
        }
        catch (Exception)
        {
            // The embed is optional decoration: tolerate any malformed JSON.
            return null;
        }
    }

    /// <summary>Tolerant JSON string reader (skips missing/non-string/whitespace properties).</summary>
    private static bool TryGetEmbedString(System.Text.Json.JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    /// <summary>Reads a field's "inline" flag (default false for anything else).</summary>
    private static bool IsInlineField(System.Text.Json.JsonElement field)
    {
        return field.ValueKind == System.Text.Json.JsonValueKind.Object
               && field.TryGetProperty("inline", out var inline)
               && inline.ValueKind == System.Text.Json.JsonValueKind.True;
    }

    /// <summary>Parses "#5865F2" / "5865F2" / decimal number embed colors.</summary>
    private static bool TryParseEmbedColor(string text, out Color color)
    {
        color = default;
        var trimmed = text.Trim().TrimStart('#');
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
            && hex <= 0xFFFFFF)
        {
            color = new Color(hex);
            return true;
        }

        if (uint.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
            && numeric <= 0xFFFFFF)
        {
            color = new Color(numeric);
            return true;
        }

        return false;
    }

    /// <summary>Defensive length clamp for embed content (Discord hard-limits each part).</summary>
    private static string ClampEmbedLength(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength];

    #endregion

    #region Workflow slash commands & interactions

    /// <inheritdoc />
    public async Task<string?> RegisterWorkflowSlashCommandAsync(string name, string description, IReadOnlyList<WorkflowParameter>? parameters = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return "Bot is not connected";
        }

        var normalized = NormalizeWorkflowCommandName(name);
        if (normalized == null)
        {
            return $"'{name}' is not a valid slash command name (letters, digits, '-' and '_' only).";
        }

        var descriptionText = string.IsNullOrWhiteSpace(description) ? "Vyral workflow command" : description.Trim();

        // Remember the mapping so Ready/GuildAvailable re-register after
        // reconnects — with the same typed options (absent/null = the legacy
        // "args" text option).
        _workflowSlashCommands[normalized] = descriptionText;
        if (parameters is { Count: > 0 })
        {
            _workflowSlashCommandOptions[normalized] = parameters;
        }
        else
        {
            _workflowSlashCommandOptions.TryRemove(normalized, out _);
        }

        var errors = new List<string>();
        foreach (var guild in client.Guilds.ToList())
        {
            var error = await RegisterWorkflowSlashCommandInGuildAsync(client, guild, normalized, descriptionText, parameters).ConfigureAwait(false);
            if (error == null)
            {
                _workflowSlashSyncedGuilds[guild.Id] = 1;
            }
            else
            {
                errors.Add($"{guild.Name}: {error}");
            }
        }

        if (errors.Count == 0)
        {
            _logger.LogInformation("✅ Registered workflow slash command '/{Command}' in {Count} guild(s)",
                normalized, client.Guilds.Count);
            return null;
        }

        _logger.LogWarning("❌ Workflow slash command '/{Command}' registration failed for {Count} guild(s)",
            normalized, errors.Count);
        return string.Join(" | ", errors);
    }

    /// <inheritdoc />
    public async Task<string?> UnregisterWorkflowSlashCommandAsync(string name)
    {
        var normalized = NormalizeWorkflowCommandName(name);
        if (normalized == null)
        {
            return null;
        }

        // Forget first: even when no client is connected (or a guild delete
        // fails) the command must not come back on the next Ready/GuildAvailable.
        _workflowSlashCommands.TryRemove(normalized, out _);
        _workflowSlashCommandOptions.TryRemove(normalized, out _);

        var client = GetConnectedClient();
        if (client == null)
        {
            return null;
        }

        var errors = new List<string>();
        var deleted = 0;
        foreach (var guild in client.Guilds.ToList())
        {
            try
            {
                var existing = await client.Rest.GetGuildApplicationCommands(guild.Id).ConfigureAwait(false);
                foreach (var command in existing.Where(c => string.Equals(c.Name, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    await command.DeleteAsync().ConfigureAwait(false);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ Failed to unregister workflow slash command '/{Command}' in {Guild}",
                    normalized, guild.Name);
                errors.Add($"{guild.Name}: {ex.Message}");
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("🗑️ Unregistered workflow slash command '/{Command}' from {Count} guild command(s)",
                normalized, deleted);
        }

        return errors.Count == 0 ? null : string.Join(" | ", errors);
    }

    /// <summary>
    /// Normalizes a workflow command name for Discord: lowercase, ASCII
    /// letters/digits/'-'/'_' only (spaces → dashes), trimmed to 1-32 chars.
    /// Null when nothing usable remains.
    /// </summary>
    private static string? NormalizeWorkflowCommandName(string? name)
    {
        var cleaned = (name ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
        var builder = new System.Text.StringBuilder(cleaned.Length);
        foreach (var character in cleaned)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim('-');
        if (result.Length > 32)
        {
            result = result[..32].Trim('-');
        }

        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// Registers (or replaces — delete + create, the clean overwrite path for
    /// guild commands in Discord.Net 3.x) one workflow slash command in one
    /// guild. Guild commands apply instantly, unlike global ones (up to an
    /// hour). The REST lookup avoids depending on the socket-side application
    /// command cache. Returns null on success, otherwise an error string.
    /// </summary>
    private async Task<string?> RegisterWorkflowSlashCommandInGuildAsync(
        DiscordSocketClient client,
        SocketGuild guild,
        string normalized,
        string description,
        IReadOnlyList<WorkflowParameter>? parameters = null)
    {
        try
        {
            var properties = BuildWorkflowSlashCommandProperties(normalized, description, parameters);

            var existing = await client.Rest.GetGuildApplicationCommands(guild.Id).ConfigureAwait(false);
            var duplicate = existing.FirstOrDefault(c =>
                string.Equals(c.Name, normalized, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                await duplicate.DeleteAsync().ConfigureAwait(false);
            }

            await guild.CreateApplicationCommandAsync(properties).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Failed to register workflow slash command '/{Command}' in {Guild}",
                normalized, guild.Name);
            return ex.Message;
        }
    }

    /// <summary>Discord caps a slash command at 25 options.</summary>
    private const int MaxWorkflowSlashOptions = 25;

    /// <summary>Discord caps option descriptions at 100 characters.</summary>
    private const int MaxWorkflowOptionDescriptionLength = 100;

    /// <summary>
    /// Builds one workflow slash command: the legacy single "args" text option
    /// when no typed parameters are authored, otherwise one Discord option per
    /// parameter (capped at Discord's 25). Option names follow Discord's rules
    /// (lowercase [a-z0-9_-], 1-32 chars — the same normalization as the
    /// command name); parameters whose name cannot be normalized are skipped.
    /// </summary>
    private SlashCommandProperties BuildWorkflowSlashCommandProperties(
        string normalized, string description, IReadOnlyList<WorkflowParameter>? parameters)
    {
        var builder = new SlashCommandBuilder()
            .WithName(normalized)
            .WithDescription(description);

        if (parameters == null || parameters.Count == 0)
        {
            builder.AddOption(new SlashCommandOptionBuilder()
                .WithName("args")
                .WithDescription("Arguments")
                .WithType(ApplicationCommandOptionType.String)
                .WithRequired(false));
            return builder.Build();
        }

        var optionCount = 0;
        foreach (var parameter in parameters)
        {
            if (optionCount >= MaxWorkflowSlashOptions)
            {
                _logger.LogWarning("Workflow slash command '/{Command}' declares more than {Limit} parameters — the rest are not registered",
                    normalized, MaxWorkflowSlashOptions);
                break;
            }

            if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
            {
                continue;
            }

            // Option names obey the same Discord rules as command names
            var optionName = NormalizeWorkflowCommandName(parameter.Name);
            if (optionName == null)
            {
                _logger.LogWarning("Workflow slash command '/{Command}': parameter '{Name}' has no usable option name — skipped",
                    normalized, parameter.Name);
                continue;
            }

            builder.AddOption(new SlashCommandOptionBuilder()
                .WithName(optionName)
                .WithDescription(BuildWorkflowOptionDescription(normalized, parameter))
                .WithType(MapWorkflowParameterType(parameter.Type))
                .WithRequired(parameter.Required));
            optionCount++;
        }

        return builder.Build();
    }

    /// <summary>
    /// Maps a workflow parameter type to a Discord option type:
    /// text→String, number→Integer, user→User, channel→Channel, role→Role,
    /// choice→Boolean (the editor's Choice is true/false), attachment→Attachment;
    /// anything else falls back to String.
    /// </summary>
    private static ApplicationCommandOptionType MapWorkflowParameterType(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "number" => ApplicationCommandOptionType.Integer,
            "user" => ApplicationCommandOptionType.User,
            "channel" => ApplicationCommandOptionType.Channel,
            "role" => ApplicationCommandOptionType.Role,
            "choice" => ApplicationCommandOptionType.Boolean,
            "attachment" => ApplicationCommandOptionType.Attachment,
            _ => ApplicationCommandOptionType.String
        };
    }

    /// <summary>
    /// Discord-safe option description: the authored description (falling back
    /// to "Option {name}"), newlines flattened, clamped to 100 characters,
    /// never empty (Discord rejects empty descriptions).
    /// </summary>
    private string BuildWorkflowOptionDescription(string commandName, WorkflowParameter parameter)
    {
        var raw = string.IsNullOrWhiteSpace(parameter.Description)
            ? $"Option {parameter.Name}"
            : parameter.Description.Trim();

        var sanitized = string.Join(' ', raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim();
        if (sanitized.Length > MaxWorkflowOptionDescriptionLength)
        {
            sanitized = sanitized[..MaxWorkflowOptionDescriptionLength].TrimEnd();
        }

        if (sanitized.Length == 0)
        {
            _logger.LogWarning("Workflow slash command '/{Command}': parameter '{Name}' has an unusable description — using a fallback",
                commandName, parameter.Name);
            sanitized = "Workflow option";
        }

        return sanitized;
    }

    private Task OnWorkflowReadyAsync()
    {
        // Re-sync after (re)connect: guild commands persist server-side, but
        // the per-session sync tracking starts empty and a resumed session
        // should not depend on it. Fire-and-forget, never throws.
        if (_workflowSlashCommands.IsEmpty)
        {
            return Task.CompletedTask;
        }

        _workflowSlashSyncedGuilds.Clear();
        _ = ResyncWorkflowSlashCommandsAsync();
        return Task.CompletedTask;
    }

    private Task OnWorkflowGuildAvailableAsync(SocketGuild guild)
    {
        // A guild that became available after the initial sync (late join or
        // READY ordering) still needs the workflow commands.
        if (_workflowSlashCommands.IsEmpty || _workflowSlashSyncedGuilds.ContainsKey(guild.Id))
        {
            return Task.CompletedTask;
        }

        _ = ResyncWorkflowSlashCommandsAsync(new[] { guild });
        return Task.CompletedTask;
    }

    /// <summary>Best-effort re-registration of every tracked workflow command (never throws).</summary>
    private async Task ResyncWorkflowSlashCommandsAsync(IReadOnlyList<SocketGuild>? guilds = null)
    {
        var client = GetConnectedClient();
        if (client == null)
        {
            return;
        }

        try
        {
            var targets = guilds ?? client.Guilds.ToList();
            foreach (var (name, description) in _workflowSlashCommands)
            {
                _workflowSlashCommandOptions.TryGetValue(name, out var parameters);
                foreach (var guild in targets)
                {
                    var error = await RegisterWorkflowSlashCommandInGuildAsync(client, guild, name, description, parameters).ConfigureAwait(false);
                    if (error == null)
                    {
                        _workflowSlashSyncedGuilds[guild.Id] = 1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Workflow slash command re-registration skipped");
        }
    }

    private Task OnWorkflowSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        _ = HandleWorkflowSlashInvocationAsync(command);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Defers the interaction (ephemeral) so Discord never shows "interaction
    /// failed" even when the workflow engine runs longer than Discord's
    /// 3-second response window, updates the workflow origin to the
    /// interaction, and raises <see cref="SlashCommandInvoked"/> for EVERY
    /// slash command — subscribers gate on their registered workflow names.
    /// Never throws.
    /// </summary>
    private async Task HandleWorkflowSlashInvocationAsync(SocketSlashCommand? command)
    {
        try
        {
            try
            {
                await command.DeferAsync(ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception deferEx)
            {
                // A lost ack only costs the "thinking…" indicator, never the app
                _logger.LogDebug(deferEx, "Workflow slash ack (defer) failed for '/{Command}'", command?.CommandName);
            }

            var guild = (command?.Channel as SocketGuildChannel)?.Guild;
            TrackWorkflowOriginFromInteraction(
                command?.Channel,
                messageId: 0,
                username: command?.User?.Username,
                guild: guild);

            var invocation = new DiscordSlashInvocation
            {
                BotId = BotId,
                CommandName = command?.CommandName ?? string.Empty,
                Arguments = ExtractSlashArguments(command),
                OptionValues = BuildSlashOptionValues(command),
                Username = command?.User?.Username ?? "Unknown",
                ChannelName = ResolveWorkflowChannelName(command?.Channel),
                GuildName = guild?.Name,
                Respond = BuildInteractionResponder(command)
            };

            SlashCommandInvoked?.Invoke(this, invocation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling the workflow slash command '{Command}'", command?.CommandName);
        }
    }

    private Task OnWorkflowButtonExecutedAsync(SocketMessageComponent component)
        => HandleWorkflowComponentInteractionAsync(component, "button");

    private Task OnWorkflowSelectMenuExecutedAsync(SocketMessageComponent component)
        => HandleWorkflowComponentInteractionAsync(component, "select menu");

    /// <summary>
    /// Raises <see cref="ComponentInteractionInvoked"/> ONLY for workflow
    /// components (custom ids starting with "vyralwf:") — other features own
    /// their own component ids. Defers ephemeral (no "interaction failed"),
    /// updates the workflow origin to the component's message and never throws.
    /// </summary>
    private async Task HandleWorkflowComponentInteractionAsync(SocketMessageComponent? component, string kind)
    {
        try
        {
            if (component?.Data?.CustomId == null
                || !component.Data.CustomId.StartsWith(WorkflowComponentPrefix, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                await component.DeferAsync(ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception deferEx)
            {
                _logger.LogDebug(deferEx, "Workflow component ack (defer) failed for '{CustomId}'", component.Data.CustomId);
            }

            var guild = (component.Channel as SocketGuildChannel)?.Guild;
            TrackWorkflowOriginFromInteraction(
                component.Channel,
                messageId: component.Message?.Id ?? 0,
                username: component.User?.Username,
                guild: guild);

            var invocation = new DiscordComponentInteraction
            {
                BotId = BotId,
                CustomId = component.Data.CustomId,
                Username = component.User?.Username ?? "Unknown",
                ChannelName = ResolveWorkflowChannelName(component.Channel),
                GuildName = guild?.Name,
                Values = component.Data.Values != null
                    ? component.Data.Values.ToArray()
                    : Array.Empty<string>(),
                Respond = BuildInteractionResponder(component)
            };

            ComponentInteractionInvoked?.Invoke(this, invocation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling a workflow {Kind} interaction", kind);
        }
    }

    /// <summary>
    /// Slash arguments for the workflow payload: the registered command has
    /// one optional string option named "args"; unknown option shapes have
    /// their string values joined with spaces (defensive).
    /// </summary>
    private static string ExtractSlashArguments(SocketSlashCommand? command)
    {
        var options = command?.Data?.Options;
        if (options == null || options.Count == 0)
        {
            return string.Empty;
        }

        var argsOption = options.FirstOrDefault(o =>
            string.Equals(o.Name, "args", StringComparison.OrdinalIgnoreCase));
        if (argsOption != null)
        {
            return argsOption.Value?.ToString()?.Trim() ?? string.Empty;
        }

        return string.Join(' ', options
            .Where(o => o.Value is string)
            .Select(o => ((string)o.Value).Trim())
            .Where(v => v.Length > 0));
    }

    /// <summary>
    /// Per-option values for the workflow payload (typed slash commands): each
    /// Discord option is stringified tolerantly (see
    /// <see cref="DescribeSlashOptionValue"/>), and where the invoked command is
    /// one we registered with typed parameters every value is ALSO mirrored
    /// under the parameter's original authored name (Discord only knows the
    /// normalized option names, so "Max Users" → registered as "max-users" →
    /// available as both variables). Never throws.
    /// </summary>
    private Dictionary<string, string> BuildSlashOptionValues(SocketSlashCommand? command)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var options = command?.Data?.Options;
            if (options == null)
            {
                return values;
            }

            foreach (var option in options)
            {
                if (option == null || string.IsNullOrEmpty(option.Name))
                {
                    continue;
                }

                values[option.Name] = DescribeSlashOptionValue(option.Value);
            }

            var normalizedCommand = NormalizeWorkflowCommandName(command?.CommandName);
            if (normalizedCommand != null
                && _workflowSlashCommandOptions.TryGetValue(normalizedCommand, out var parameters)
                && parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        continue;
                    }

                    var optionKey = NormalizeWorkflowCommandName(parameter.Name);
                    if (optionKey != null
                        && values.TryGetValue(optionKey, out var optionValue)
                        && !values.ContainsKey(parameter.Name))
                    {
                        values[parameter.Name] = optionValue;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Option extraction must never break the invocation event
            _logger.LogDebug(ex, "Workflow slash option extraction failed for '/{Command}'", command?.CommandName);
        }

        return values;
    }

    /// <summary>
    /// Tolerant stringification of one slash option value: User→username,
    /// Channel→channel name, Role→role name, Boolean→"true"/"false",
    /// Attachment→file name, everything else invariant ToString.
    /// </summary>
    private static string DescribeSlashOptionValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        try
        {
            return value switch
            {
                SocketUser user => string.IsNullOrEmpty(user.Username) ? value.ToString() ?? string.Empty : user.Username,
                IChannel channel => string.IsNullOrEmpty(channel.Name) ? value.ToString() ?? string.Empty : channel.Name,
                IRole role => string.IsNullOrEmpty(role.Name) ? value.ToString() ?? string.Empty : role.Name,
                Attachment attachment => attachment.Filename ?? string.Empty,
                bool flag => flag ? "true" : "false",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
        }
        catch (Exception)
        {
            return value.ToString() ?? string.Empty;
        }
    }

    /// <summary>Channel name for interaction payloads ("DirectMessage" matches the chat-event convention).</summary>
    private static string ResolveWorkflowChannelName(ISocketMessageChannel? channel)
    {
        var name = channel?.Name;
        return string.IsNullOrEmpty(name) ? "DirectMessage" : name;
    }

    /// <summary>
    /// Reply channel for interaction payloads: the interaction is already
    /// deferred, so replies go out as ephemeral followups (with a
    /// RespondAsync fallback for the race where the ack failed). Best-effort —
    /// never throws, so a failing reply can never break a workflow node.
    /// </summary>
    private Func<string, Task> BuildInteractionResponder(SocketInteraction? interaction)
    {
        return async message =>
        {
            try
            {
                if (interaction == null || string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                if (!interaction.HasResponded)
                {
                    await interaction.RespondAsync(message, ephemeral: true).ConfigureAwait(false);
                }
                else
                {
                    await interaction.FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Workflow interaction reply failed");
            }
        };
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_connectionLock.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("⚠️ DiscordService dispose skipped: a connection operation is still in flight");
            return;
        }

        try
        {
            _disposed = true;

            // Unhook the live client's handlers before disposing it so disposal
            // cannot reenter our callbacks
            var client = _client;
            _client = null;
            if (client != null)
            {
                UnhookEvents(client);
            }

            TryDisposeClientAsync(client).GetAwaiter().GetResult();

            // Also dispose any client parked by a previous DisconnectAsync
            var stoppedClient = _stoppedClient;
            _stoppedClient = null;
            if (stoppedClient != null)
            {
                TryDisposeClientAsync(stoppedClient).GetAwaiter().GetResult();
            }

            // Release the token's gateway slot AFTER the client teardown so a
            // concurrent connect cannot claim the token while this session is
            // still draining. Not releasing here would block the token forever
            // (Dispose nulls the client but nothing else clears the registry).
            ReleaseLiveTokenSession(ConnectedToken?.Trim());

            _logger.LogInformation("🧹 DiscordService disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error disposing DiscordService");
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }
}
