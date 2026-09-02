using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.YouTube;
using VyralBot.Integrations.YouTube.Events;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Discord.Events;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Audio;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private readonly ILogger<ChatViewModel> _logger;
    private readonly IEventBus _eventBus;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IYouTubeService _youtubeService;
    private readonly IDiscordService _discordService;
    // Multi-bot Discord manager (V1 routing: sends go through manager.Primary,
    // falling back to the wizard's single setup bot when no manager bot runs)
    private readonly IDiscordBotManager _discordBotManager;
    // Discord built-in moderation commands ("/" prefix in the app chat, "!"
    // prefix inside Discord itself). Optional: when not registered, "/" answers
    // with a "not available" system message and the in-Discord "!" handling
    // is skipped, leaving every other behavior untouched.
    private readonly IDiscordCommandHandler? _discordCommandHandler;
    private readonly KickSettingsViewModel _kickSettings;
    private readonly VyralBot.Core.Services.Chat.IChatMessageStore _chatStore;
    private readonly IChatEventSettingsService _eventSettingsService;
    private readonly IAutoReplyService _autoReplyService;
    private readonly VyralBot.Core.Services.Currency.ICurrencyService? _currencyService;
    private readonly VyralBot.Core.Services.Currency.ICurrencyCommandService? _currencyCommandService;
    private readonly ICommandService? _commandService;
    private readonly ISoundService? _soundService;
    private readonly UI.Services.IAlertService? _alertService;
    private readonly VyralBot.Core.Services.GiveawayCommandService? _giveawayCommandService;
    private readonly VyralBot.Core.Services.PollCommandService? _pollCommandService;
    private readonly VyralBot.Core.Services.WatchtimeService? _watchtimeService;
    private readonly VyralBot.Core.Services.WatchtimeCommandService? _watchtimeCommandService;
    private readonly VyralBot.Core.Services.Variables.IVariableReplacementService? _variableReplacementService;
    private readonly IDataService? _dataService;
    // Action-sequence executor for custom commands (Core returns the
    // $ACTIONS$ marker; this runner owns the platform senders the steps need)
    private readonly UI.Services.CommandActionRunner? _commandActionRunner;
    // Workflow command triggers (Visual Automation): gets the first shot at
    // every generic chat message BEFORE the CommandService lookup. Optional —
    // when not registered, chat handling is exactly as before.
    private readonly UI.Services.WorkflowTriggerService? _workflowTriggerService;
    // Response waiters for the workflow engine's "wait_for_response" node:
    // fed once per inbound user message (after echo/dedup filtering) so a
    // suspended workflow resumes on the triggering user's next message.
    // Optional — when not registered, chat handling is exactly as before.
    private readonly VyralBot.Workflows.Abstractions.IResponseWaiterRegistry? _responseWaiters;
    private Guid _twitchSubscriptionId;
    private Guid _kickSubscriptionId;
    private Guid _genericSubscriptionId;
    private Guid _routingSubscriptionId;
    private Guid _connectionSubscriptionId;
    private Guid _kickFollowSubscriptionId;
    private Guid _kickSubscribeSubscriptionId;
    private Guid _kickGiftSubSubscriptionId;
    private Guid _kickHostSubscriptionId;
    private Guid _kickKicksGiftedSubscriptionId;
    private Guid _kickRewardRedeemedSubscriptionId;
    private Guid _kickStreamStatusSubscriptionId;
    private Guid _twitchFollowSubscriptionId;
    private Guid _twitchSubscribeSubscriptionId;
    private Guid _twitchResubSubscriptionId;
    private Guid _twitchGiftSubSubscriptionId;
    private Guid _twitchRaidSubscriptionId;
    private Guid _twitchCheerSubscriptionId;
    private Guid _youtubeSubscriptionId;
    private Guid _discordSubscriptionId;
    private bool _isInitialized = false;

    // Track recently sent Kick messages to filter out WebSocket echoes
    private readonly Dictionary<string, DateTime> _recentKickMessages = new();
    // Track recently sent Twitch messages to filter out IRC echoes
    private readonly Dictionary<string, DateTime> _recentTwitchMessages = new();
    // Track recently sent YouTube messages to filter out poll echoes
    private readonly Dictionary<string, DateTime> _recentYoutubeMessages = new();
    // Track recently sent Discord messages to filter out gateway echoes
    private readonly Dictionary<string, DateTime> _recentDiscordMessages = new();
    private readonly object _recentMessagesLock = new();

    // Track loaded message IDs to prevent duplicates when loading history
    private readonly HashSet<string> _loadedMessageIds = new();
    private int _historyLoadedCount = 0;
    private const int MAX_HISTORY_MESSAGES = 200;

    // Deduplicate generic chat events: the same message can arrive through more
    // than one transport (e.g. Kick Pusher plus webhooks). Bounded LRU-style set.
    private readonly HashSet<string> _processedMessageIds = new();
    private readonly Queue<string> _processedMessageIdOrder = new();
    private readonly object _processedMessageIdsLock = new();
    private const int MAX_PROCESSED_MESSAGE_IDS = 1000;

    // Discord per-bot origin: MessageId ("discord:{snowflake}") -> bot id, as
    // tagged on DiscordChatMessageEvent by each bot instance. The generic
    // pipeline (ChatMessageReceivedEvent carries no bot field) looks entries up
    // so replies and command filtering route back to the bot that saw the
    // message. Bounded LRU-style, mirroring the dedup set above.
    private readonly Dictionary<string, string> _discordMessageBotIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _discordMessageBotIdOrder = new();
    private readonly object _discordMessageBotIdsLock = new();
    private const int MAX_DISCORD_MESSAGE_BOT_IDS = 500;

    // Cached platform rows (account the bot sends as; Twitch broadcaster id for follower checks)
    private Platform? _twitchPlatformRow;
    private Platform? _kickPlatformRow;
    private Platform? _youtubePlatformRow;
    private DateTime _platformRowsLoadedAt = DateTime.MinValue;
    private readonly object _platformRowsLock = new();
    private static readonly TimeSpan PlatformRowCacheTtl = TimeSpan.FromMinutes(5);

    // Best-effort follower cache for giveaway/poll eligibility (Twitch Helix lookups)
    private readonly Dictionary<string, (bool IsFollower, DateTime ExpiresAt)> _followerCache = new();
    private readonly object _followerCacheLock = new();
    private static readonly TimeSpan FollowerCacheTtl = TimeSpan.FromMinutes(5);

    // HttpClient for Twitch Helix eligibility lookups (separate from the badge image client)
    private static readonly HttpClient _helixHttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private string _messageInput = "";

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private ObservableCollection<PlatformOption> _connectedPlatforms = new();

    [ObservableProperty]
    private PlatformOption? _selectedPlatform;

    [ObservableProperty]
    private bool _isPlatformSelectorOpen;

    [ObservableProperty]
    private bool _isAllPlatformsSettingsOpen;

    [ObservableProperty]
    private bool _isTwitchSettingsOpen;

    [ObservableProperty]
    private bool _isKickSettingsOpen;

    [ObservableProperty]
    private bool _isYoutubeSettingsOpen;

    [ObservableProperty]
    private bool _isTiktokSettingsOpen;

    [ObservableProperty]
    private bool _isDiscordSettingsOpen;

    [ObservableProperty]
    private bool _isTwitchConnected;

    [ObservableProperty]
    private bool _isKickConnected;

    [ObservableProperty]
    private bool _isKickConfigured;

    [ObservableProperty]
    private bool _isYoutubeConnected;

    [ObservableProperty]
    private bool _isTiktokConnected;

    [ObservableProperty]
    private bool _isDiscordConnected;

    [ObservableProperty]
    private bool _twitchFilterEnabled = true;

    [ObservableProperty]
    private bool _kickFilterEnabled = true;

    [ObservableProperty]
    private bool _youtubeFilterEnabled = true;

    [ObservableProperty]
    private bool _tiktokFilterEnabled = true;

    [ObservableProperty]
    private bool _discordFilterEnabled = true;

    public ChatViewModel(
        ILogger<ChatViewModel> logger,
        IEventBus eventBus,
        ITwitchService twitchService,
        IKickService kickService,
        IYouTubeService youtubeService,
        IDiscordService discordService,
        IDiscordBotManager discordBotManager,
        KickSettingsViewModel kickSettings,
        VyralBot.Core.Services.Chat.IChatMessageStore chatStore,
        IChatEventSettingsService eventSettingsService,
        IAutoReplyService autoReplyService,
        // Discord "/" moderation command handler (optional dep, like the ones
        // below; must follow the required parameters, so it sits at the head
        // of the optional block rather than directly after IDiscordBotManager)
        IDiscordCommandHandler? discordCommandHandler = null,
        VyralBot.Core.Services.Currency.ICurrencyService? currencyService = null,
        VyralBot.Core.Services.Currency.ICurrencyCommandService? currencyCommandService = null,
        ICommandService? commandService = null,
        ISoundService? soundService = null,
        UI.Services.IAlertService? alertService = null,
        VyralBot.Core.Services.GiveawayCommandService? giveawayCommandService = null,
        VyralBot.Core.Services.PollCommandService? pollCommandService = null,
        VyralBot.Core.Services.WatchtimeService? watchtimeService = null,
        VyralBot.Core.Services.WatchtimeCommandService? watchtimeCommandService = null,
        VyralBot.Core.Services.Variables.IVariableReplacementService? variableReplacementService = null,
        IDataService? dataService = null,
        UI.Services.CommandActionRunner? commandActionRunner = null,
        UI.Services.WorkflowTriggerService? workflowTriggerService = null,
        VyralBot.Workflows.Abstractions.IResponseWaiterRegistry? responseWaiters = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _youtubeService = youtubeService ?? throw new ArgumentNullException(nameof(youtubeService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _kickSettings = kickSettings ?? throw new ArgumentNullException(nameof(kickSettings));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _eventSettingsService = eventSettingsService ?? throw new ArgumentNullException(nameof(eventSettingsService));
        _autoReplyService = autoReplyService ?? throw new ArgumentNullException(nameof(autoReplyService));
        _discordCommandHandler = discordCommandHandler; // Optional - Discord moderation commands
        _currencyService = currencyService;
        _currencyCommandService = currencyCommandService;
        _commandService = commandService;
        _soundService = soundService;
        _alertService = alertService;
        _giveawayCommandService = giveawayCommandService;
        _pollCommandService = pollCommandService;
        _watchtimeService = watchtimeService;
        _watchtimeCommandService = watchtimeCommandService;
        _variableReplacementService = variableReplacementService;
        _dataService = dataService; // Optional - used for event statistics and eligibility checks
        _commandActionRunner = commandActionRunner; // Optional - runs custom-command action sequences
        _workflowTriggerService = workflowTriggerService; // Optional - workflow command triggers
        _responseWaiters = responseWaiters; // Optional - workflow wait_for_response registry
        _currencyService = currencyService; // Optional - currency system may not be used
        _currencyCommandService = currencyCommandService; // Optional
        _commandService = commandService; // Optional
        _soundService = soundService; // Optional
        _alertService = alertService; // Optional - alerts may not be configured

        // Set static logger reference for ChatMessage badge loading
        ChatMessage._logger = _logger;
    }

    public async Task InitializeAsync()
    {
        // Prevent duplicate subscriptions (now that this is a Singleton)
        if (_isInitialized)
        {
            _logger.LogInformation("ChatViewModel already initialized, skipping duplicate subscription");
            UpdateConnectedPlatforms();
            return;
        }

        _logger.LogInformation("🚀 Initializing unified chat view with EventBus subscriptions...");

        // Subscribe to Twitch chat events
        _twitchSubscriptionId = await _eventBus.SubscribeAsync<TwitchChatMessageEvent>(OnTwitchChatMessageReceivedAsync);
        _logger.LogInformation("✅ Subscribed to TwitchChatMessageEvent (ID: {SubscriptionId})", _twitchSubscriptionId);

        // Subscribe to Kick chat events - CRITICAL for webhook message receiving!
        _kickSubscriptionId = await _eventBus.SubscribeAsync<KickChatMessageEvent>(OnKickChatMessageReceivedAsync);
        _logger.LogInformation("✅ Subscribed to KickChatMessageEvent (ID: {SubscriptionId})", _kickSubscriptionId);
        _logger.LogInformation("   📡 Ready to receive Kick webhook events from WebhooksController!");

        // Subscribe to YouTube chat events
        _youtubeSubscriptionId = await _eventBus.SubscribeAsync<YouTubeChatMessageEvent>(OnYouTubeChatMessageReceivedAsync);
        _logger.LogInformation("✅ Subscribed to YouTubeChatMessageEvent (ID: {SubscriptionId})", _youtubeSubscriptionId);

        // Subscribe to Discord chat events
        _discordSubscriptionId = await _eventBus.SubscribeAsync<DiscordChatMessageEvent>(OnDiscordChatMessageReceivedAsync);
        _logger.LogInformation("✅ Subscribed to DiscordChatMessageEvent (ID: {SubscriptionId})", _discordSubscriptionId);

        // Subscribe to generic chat events from all platforms
        _genericSubscriptionId = await _eventBus.SubscribeAsync<ChatMessageReceivedEvent>(OnChatMessageReceivedAsync);
        _logger.LogInformation("✅ Subscribed to ChatMessageReceivedEvent (ID: {SubscriptionId})", _genericSubscriptionId);

        // Subscribe to runtime routing change events so UI toggles take effect immediately
        _routingSubscriptionId = await _eventBus.SubscribeAsync<PlatformChatRoutingChangedEvent>(OnPlatformRoutingChangedAsync);
        _logger.LogInformation("✅ Subscribed to PlatformChatRoutingChangedEvent (ID: {SubscriptionId})", _routingSubscriptionId);

        // Subscribe to platform connection change events
        _connectionSubscriptionId = await _eventBus.SubscribeAsync<PlatformConnectionChangedEvent>(OnPlatformConnectionChangedAsync);
        _logger.LogInformation("✅ Subscribed to PlatformConnectionChangedEvent (ID: {SubscriptionId})", _connectionSubscriptionId);

        // Subscribe to Kick event types
        _kickFollowSubscriptionId = await _eventBus.SubscribeAsync<KickFollowEvent>(OnKickFollowAsync);
        _logger.LogInformation("✅ Subscribed to KickFollowEvent (ID: {SubscriptionId})", _kickFollowSubscriptionId);

        _kickSubscribeSubscriptionId = await _eventBus.SubscribeAsync<KickSubscribeEvent>(OnKickSubscribeAsync);
        _logger.LogInformation("✅ Subscribed to KickSubscribeEvent (ID: {SubscriptionId})", _kickSubscribeSubscriptionId);

        _kickGiftSubSubscriptionId = await _eventBus.SubscribeAsync<KickGiftSubEvent>(OnKickGiftSubAsync);
        _logger.LogInformation("✅ Subscribed to KickGiftSubEvent (ID: {SubscriptionId})", _kickGiftSubSubscriptionId);

        _kickHostSubscriptionId = await _eventBus.SubscribeAsync<KickHostEvent>(OnKickHostAsync);
        _logger.LogInformation("✅ Subscribed to KickHostEvent (ID: {SubscriptionId})", _kickHostSubscriptionId);

        _kickKicksGiftedSubscriptionId = await _eventBus.SubscribeAsync<KickKicksGiftedEvent>(OnKickKicksGiftedAsync);
        _logger.LogInformation("✅ Subscribed to KickKicksGiftedEvent (ID: {SubscriptionId})", _kickKicksGiftedSubscriptionId);

        _kickRewardRedeemedSubscriptionId = await _eventBus.SubscribeAsync<KickRewardRedeemedEvent>(OnKickRewardRedeemedAsync);
        _logger.LogInformation("✅ Subscribed to KickRewardRedeemedEvent (ID: {SubscriptionId})", _kickRewardRedeemedSubscriptionId);

        _kickStreamStatusSubscriptionId = await _eventBus.SubscribeAsync<KickStreamStatusEvent>(OnKickStreamStatusAsync);
        _logger.LogInformation("✅ Subscribed to KickStreamStatusEvent (ID: {SubscriptionId})", _kickStreamStatusSubscriptionId);

        // Subscribe to Twitch event types
        _twitchFollowSubscriptionId = await _eventBus.SubscribeAsync<TwitchFollowEvent>(OnTwitchFollowAsync);
        _logger.LogInformation("✅ Subscribed to TwitchFollowEvent (ID: {SubscriptionId})", _twitchFollowSubscriptionId);

        _twitchSubscribeSubscriptionId = await _eventBus.SubscribeAsync<TwitchSubscribeEvent>(OnTwitchSubscribeAsync);
        _logger.LogInformation("✅ Subscribed to TwitchSubscribeEvent (ID: {SubscriptionId})", _twitchSubscribeSubscriptionId);

        _twitchResubSubscriptionId = await _eventBus.SubscribeAsync<TwitchResubEvent>(OnTwitchResubAsync);
        _logger.LogInformation("✅ Subscribed to TwitchResubEvent (ID: {SubscriptionId})", _twitchResubSubscriptionId);

        _twitchGiftSubSubscriptionId = await _eventBus.SubscribeAsync<TwitchGiftSubEvent>(OnTwitchGiftSubAsync);
        _logger.LogInformation("✅ Subscribed to TwitchGiftSubEvent (ID: {SubscriptionId})", _twitchGiftSubSubscriptionId);

        _twitchRaidSubscriptionId = await _eventBus.SubscribeAsync<TwitchRaidEvent>(OnTwitchRaidAsync);
        _logger.LogInformation("✅ Subscribed to TwitchRaidEvent (ID: {SubscriptionId})", _twitchRaidSubscriptionId);

        _twitchCheerSubscriptionId = await _eventBus.SubscribeAsync<TwitchCheerEvent>(OnTwitchCheerAsync);
        _logger.LogInformation("✅ Subscribed to TwitchCheerEvent (ID: {SubscriptionId})", _twitchCheerSubscriptionId);

        // Populate connected platforms
        UpdateConnectedPlatforms();
        // Initialize filter flags from persisted quick-settings where available
        try
        {
            KickFilterEnabled = _kickSettings.IsChatEnabled;
            _logger.LogInformation("Initialized KickFilterEnabled from KickSettings: {Enabled}", KickFilterEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize KickFilterEnabled from KickSettings");
        }

        // Initialize the Discord filter from the persisted Platform row (mirrors
        // how the Kick flag bootstraps from KickSettings). Runtime toggles keep
        // flowing in through PlatformChatRoutingChangedEvent's "Discord" case.
        try
        {
            if (_dataService != null)
            {
                var discordPlatformRow = await _dataService.GetPlatformByNameAsync("Discord");
                DiscordFilterEnabled = discordPlatformRow?.DiscordChatEnabled ?? true;
                _logger.LogInformation("Initialized DiscordFilterEnabled from Platform row: {Enabled}", DiscordFilterEnabled);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize DiscordFilterEnabled from Platform row");
        }

        _isInitialized = true;
        _logger.LogInformation("🎯 ChatViewModel initialization complete - all event handlers active!");
        _logger.LogInformation("💾 Chat history available but not loaded yet (saves memory - load on-demand if needed)");

        // DON'T load historical messages immediately - causes 46MB memory spike!
        // Messages will start appearing as new ones arrive via webhooks.
        // If you want to see history, you can manually load it with a command.
        // This keeps memory usage low until actually needed.
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        try
        {
            // Check if we've already loaded history (only load once)
            if (_historyLoadedCount > 0)
            {
                _logger.LogInformation("📜 History already loaded ({Count} messages). Clear chat to reload.", _historyLoadedCount);
                return;
            }

            _logger.LogInformation("📜 Loading chat history on-demand...");

            var allHistoryMessages = new List<ChatMessage>();

            // Load Kick history
            if (_kickService.IsConnected && !string.IsNullOrEmpty(_kickService.Channel))
            {
                // Get up to 200 messages from database
                var stored = await _chatStore.GetRecentAsync("Kick", _kickService.Channel, MAX_HISTORY_MESSAGES);
                if (stored != null && stored.Count > 0)
                {
                    _logger.LogInformation("Fetched {Count} stored Kick messages from database", stored.Count);
                    foreach (var evt in stored)
                    {
                        if (evt is VyralBot.Integrations.Kick.Events.KickChatMessageEvent k)
                        {
                            // Create a unique ID for deduplication
                            var messageId = $"Kick:{k.Username}:{k.Timestamp.Ticks}:{k.Message}";

                            // Skip if already loaded
                            if (_loadedMessageIds.Contains(messageId)) continue;

                            var historyMessage = new ChatMessage
                            {
                                Platform = "Kick",
                                Username = k.DisplayName ?? k.Username,
                                Message = k.Message,
                                Timestamp = k.CreatedAt ?? k.Timestamp,
                                Color = k.Color ?? "#53FC18",
                                IsModerator = k.IsModerator,
                                IsSubscriber = k.IsSubscriber,
                                IsVip = false,
                                IsBroadcaster = k.IsOwner,
                                Badges = k.Badges ?? new Dictionary<string, string>()
                            };
                            historyMessage.InitializeBadges();
                            allHistoryMessages.Add(historyMessage);
                            _loadedMessageIds.Add(messageId);
                        }
                    }
                }
            }

            // Load Twitch history
            if (_twitchService.IsConnected && !string.IsNullOrEmpty(_twitchService.Channel))
            {
                var stored = await _chatStore.GetRecentAsync("Twitch", _twitchService.Channel, MAX_HISTORY_MESSAGES);
                if (stored != null && stored.Count > 0)
                {
                    _logger.LogInformation("Fetched {Count} stored Twitch messages from database", stored.Count);
                    foreach (var evt in stored)
                    {
                        if (evt is VyralBot.Integrations.Twitch.Events.TwitchChatMessageEvent t)
                        {
                            var messageId = $"Twitch:{t.Username}:{t.Timestamp.Ticks}:{t.Message}";

                            if (_loadedMessageIds.Contains(messageId)) continue;

                            var historyMessage = new ChatMessage
                            {
                                Platform = "Twitch",
                                Username = t.DisplayName ?? t.Username,
                                Message = t.Message,
                                Timestamp = t.Timestamp.ToLocalTime(),
                                Color = t.Color ?? "#FFFFFF",
                                IsModerator = t.IsModerator,
                                IsSubscriber = t.IsSubscriber,
                                IsVip = t.IsVip,
                                IsBroadcaster = t.IsBroadcaster,
                                Badges = t.Badges ?? new Dictionary<string, string>()
                            };
                            historyMessage.InitializeBadges();
                            allHistoryMessages.Add(historyMessage);
                            _loadedMessageIds.Add(messageId);
                        }
                    }
                }
            }

            // Load YouTube history
            if (_youtubeService.IsConnected && !string.IsNullOrEmpty(_youtubeService.Channel))
            {
                var stored = await _chatStore.GetRecentAsync("YouTube", _youtubeService.Channel, MAX_HISTORY_MESSAGES);
                if (stored != null && stored.Count > 0)
                {
                    _logger.LogInformation("Fetched {Count} stored YouTube messages from database", stored.Count);
                    foreach (var evt in stored)
                    {
                        if (evt is VyralBot.Integrations.YouTube.Events.YouTubeChatMessageEvent y)
                        {
                            var stamp = y.PublishedAt ?? y.Timestamp;
                            var messageId = $"YouTube:{y.Username}:{stamp.Ticks}:{y.Message}";

                            if (_loadedMessageIds.Contains(messageId)) continue;

                            var historyMessage = new ChatMessage
                            {
                                Platform = "YouTube",
                                Username = y.DisplayName,
                                Message = y.Message,
                                Timestamp = stamp.ToLocalTime(),
                                Color = y.Color ?? "#FF0000",
                                IsModerator = y.IsModerator,
                                IsSubscriber = y.IsMember,
                                IsBroadcaster = y.IsOwner,
                                Badges = new Dictionary<string, string>()
                            };
                            historyMessage.InitializeBadges();
                            allHistoryMessages.Add(historyMessage);
                            _loadedMessageIds.Add(messageId);
                        }
                    }
                }
            }

            // Load Discord history (the store keys rows by channel name — resolve it from
            // the guild list of the Primary bot, falling back to the setup bot)
            var discordHistoryService = _discordBotManager.Primary ?? _discordService;
            if (discordHistoryService.IsConnected && !string.IsNullOrEmpty(discordHistoryService.SelectedChannelId))
            {
                var discordChannelKey = discordHistoryService.SelectedChannelId;
                foreach (var guild in discordHistoryService.ConnectedGuilds)
                {
                    foreach (var channel in guild.TextChannels)
                    {
                        if (channel.Id == discordHistoryService.SelectedChannelId)
                        {
                            discordChannelKey = channel.Name;
                            break;
                        }
                    }
                    if (discordChannelKey != discordHistoryService.SelectedChannelId) break;
                }

                var stored = await _chatStore.GetRecentAsync("Discord", discordChannelKey, MAX_HISTORY_MESSAGES);
                if (stored != null && stored.Count > 0)
                {
                    _logger.LogInformation("Fetched {Count} stored Discord messages from database", stored.Count);
                    foreach (var evt in stored)
                    {
                        if (evt is VyralBot.Integrations.Discord.Events.DiscordChatMessageEvent d)
                        {
                            var messageId = $"Discord:{d.Username}:{d.Timestamp.Ticks}:{d.Message}";

                            if (_loadedMessageIds.Contains(messageId)) continue;

                            var badges = new Dictionary<string, string>();
                            if (d.IsBroadcaster) badges["broadcaster"] = "1";
                            if (d.IsModerator) badges["moderator"] = "1";

                            var historyMessage = new ChatMessage
                            {
                                Platform = "Discord",
                                Username = d.Username,
                                Message = d.Message,
                                Timestamp = d.Timestamp.ToLocalTime(),
                                Color = "#5865F2",
                                IsModerator = d.IsModerator,
                                IsBroadcaster = d.IsBroadcaster,
                                Badges = badges
                            };
                            historyMessage.InitializeBadges();
                            allHistoryMessages.Add(historyMessage);
                            _loadedMessageIds.Add(messageId);
                        }
                    }
                }
            }

            if (allHistoryMessages.Count > 0)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Insert at beginning in chronological order
                    foreach (var msg in allHistoryMessages.OrderBy(m => m.Timestamp))
                    {
                        Messages.Insert(0, msg);
                    }

                    _historyLoadedCount += allHistoryMessages.Count;
                    _logger.LogInformation("✅ Added {Added} history messages ({Kick} Kick, {Twitch} Twitch, {YouTube} YouTube). Total: {Total}/{Max}",
                        allHistoryMessages.Count,
                        allHistoryMessages.Count(m => m.Platform == "Kick"),
                        allHistoryMessages.Count(m => m.Platform == "Twitch"),
                        allHistoryMessages.Count(m => m.Platform == "YouTube"),
                        _historyLoadedCount, MAX_HISTORY_MESSAGES);

                    // Trim to last 500 total messages (history + live)
                    while (Messages.Count > 500) Messages.RemoveAt(Messages.Count - 1);
                });
            }
            else
            {
                _logger.LogInformation("No history messages available from any platform");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load chat history");
        }
    }

    /// <summary>
    /// Discord service used for outgoing messages: the multi-bot manager's
    /// Primary bot (first connected) with a fallback to the wizard's single
    /// setup bot. Used when no per-bot origin is known (manual sends,
    /// send-to-all, messages from untagged instances).
    /// </summary>
    private IDiscordService? DiscordSendTarget =>
        _discordBotManager.Primary ?? (_discordService.IsConnected ? _discordService : null);

    /// <summary>
    /// Resolves the Discord service a reply should go through for a message
    /// that originated from <paramref name="botId"/>: the originating bot's own
    /// manager instance first, then the primary manager bot, then the wizard's
    /// setup bot. The "primary" tag belongs to the setup wizard's singleton, so
    /// it maps back to that instance before any manager fallback.
    /// </summary>
    private IDiscordService? ResolveDiscordSendTarget(string? botId)
    {
        // Managed bot instance that published the event (managed bots carry
        // their DiscordBots row id as BotId).
        if (!string.IsNullOrWhiteSpace(botId) && !string.Equals(botId, "primary", StringComparison.Ordinal))
        {
            var byId = _discordBotManager.Get(botId);
            if (byId != null && byId.IsConnected)
            {
                return byId;
            }
        }

        // Wizard setup bot: its events carry the default "primary" tag.
        if (string.Equals(botId, "primary", StringComparison.Ordinal) && _discordService.IsConnected)
        {
            return _discordService;
        }

        // Origin unknown, disconnected, or untagged: first connected manager
        // bot, else the setup bot.
        return DiscordSendTarget;
    }

    /// <summary>True when any manager bot or the setup bot is connected.</summary>
    private bool IsAnyDiscordConnected =>
        _discordBotManager.Connected.Count > 0 || _discordService.IsConnected;

    /// <summary>
    /// Runs a custom command's ACTION SEQUENCE: parses the command id out of
    /// the <see cref="ICommandService.ActionsMarkerPrefix"/> marker Core
    /// returned, loads the Command row and executes its steps via
    /// CommandActionRunner. Returns the run summary for the reply, or null
    /// (the sequence's own send steps already spoke for the command).
    /// </summary>
    private async Task<string?> RunCommandActionsAsync(string marker, CommandContext context)
    {
        if (_commandActionRunner == null || _dataService == null)
        {
            _logger.LogWarning("Action-sequence command skipped: runner or data service unavailable");
            return null;
        }

        var commandId = marker[ICommandService.ActionsMarkerPrefix.Length..];
        try
        {
            var command = await _dataService.GetCommandAsync(commandId);
            if (command == null)
            {
                _logger.LogWarning("Action-sequence command {CommandId} not found", commandId);
                return null;
            }

            return await _commandActionRunner.RunAsync(command, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run action sequence for command {CommandId}", commandId);
            return null;
        }
    }

    /// <summary>
    /// Sends a Discord reply back through the bot the message came from
    /// (<paramref name="botId"/>), falling back to Primary and then the setup bot.
    /// </summary>
    private async Task SendDiscordReplyAsync(string response, string? botId = null)
    {
        var target = ResolveDiscordSendTarget(botId);
        if (target != null)
        {
            await target.SendMessageAsync(response);
        }
    }

    /// <summary>
    /// Executes a Discord-scoped "/" command typed into the app's unified chat
    /// input (e.g. "/kick @user", "/slowmode 10", "/purge 25"): run by the
    /// Discord bot through <see cref="IDiscordCommandHandler"/> and answered
    /// in the APP CHAT ONLY as a gold system message — nothing is ever posted
    /// to Discord from here. The caller guarantees this runs instead of the
    /// normal send path (no echo tracking, no send-to-all).
    /// </summary>
    private async Task HandleDiscordSlashCommandAsync(string commandLine)
    {
        if (_discordCommandHandler == null)
        {
            await AddSystemMessageAsync("Discord", "Discord moderation commands are not available.");
            return;
        }

        // Target bot: the selected platform's bot when a Discord entry is
        // selected, else the default (Primary) target. The platform list only
        // ever carries a single "Discord" entry whose send target IS
        // ResolveDiscordSendTarget(null) (see the "Discord" case in
        // SendMessageAsync), so one resolution covers both branches.
        var bot = ResolveDiscordSendTarget(null);
        if (bot == null || !bot.IsConnected)
        {
            await AddSystemMessageAsync("Discord", "No Discord bot is connected.");
            return;
        }

        try
        {
            // The app user is the bot owner: full moderator + owner rights.
            // Strip the leading "/": the handler expects the bare command line.
            var bareCommand = commandLine.TrimStart('/').Trim();
            var result = await _discordCommandHandler.HandleAsync(
                bot, bareCommand, callerIsModerator: true, callerIsOwner: true, bot.SelectedChannelId);

            if (result.Handled)
            {
                // System message in the app chat only (never sent to Discord)
                await AddSystemMessageAsync("Discord", $"Discord: {result.Response}");
            }
            else
            {
                await AddSystemMessageAsync("Discord", "Unknown Discord command. Try /help for the built-in moderation commands.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Discord slash command: {Command}", commandLine);
            await AddSystemMessageAsync("Discord", "Discord command failed. Check the logs for details.");
        }
        finally
        {
            MessageInput = "";
        }
    }

    /// <summary>
    /// In-Discord "!" moderation commands (kick/ban/unban/mute/unmute/slowmode/
    /// purge/clear/help): server members with moderator/owner permissions type
    /// them directly in a Discord channel and the bot replies in that channel.
    /// Returns true when the command was handled (the reply is routed back
    /// through the originating bot via <see cref="SendDiscordReplyAsync"/>).
    /// </summary>
    /// <remarks>
    /// The generic pipeline (OnChatMessageReceivedAsync) receives this same
    /// message separately as a ChatMessageReceivedEvent and cannot be
    /// cancelled from here — that fallthrough is harmless and accepted: the
    /// moderation command names do not collide with any seeded unified
    /// command (CommandService returns null for unknown names), and each
    /// fallback service (currency/giveaway/poll/watchtime) only replies when
    /// its own command name matches. The moderation reply is therefore never
    /// doubled.
    /// </remarks>
    private async Task<bool> TryHandleDiscordModCommandAsync(DiscordChatMessageEvent evt)
    {
        if (_discordCommandHandler == null || string.IsNullOrEmpty(evt.Message) || !evt.Message.StartsWith("!"))
        {
            return false;
        }

        try
        {
            // Origin routing: the bot that received the message executes the
            // command (manager instance by BotId, falling back to Primary,
            // then the wizard's setup bot) and the reply goes back through
            // that same bot, so multi-bot setups answer in the right channel.
            var originBot = ResolveDiscordSendTarget(evt.BotId);
            if (originBot == null || !originBot.IsConnected)
            {
                return false;
            }

            // Built-ins registered as disabled for this bot (Integrations card,
            // Configs key "DiscordBot.{botId}.DisabledBuiltIns") are skipped so an
            // external service (BotGhost/MEE6/etc.) can own that command instead —
            // avoiding dual-hosting conflicts.
            try
            {
                if (_dataService != null)
                {
                    var disabledJson = await _dataService.GetConfigValueAsync($"DiscordBot.{evt.BotId}.DisabledBuiltIns");
                    if (!string.IsNullOrEmpty(disabledJson))
                    {
                        var disabled = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(disabledJson);
                        var firstToken = evt.Message.Trim().TrimStart('!', '.').Split(' ')[0].ToLowerInvariant();
                        if (disabled != null && disabled.Contains(firstToken))
                        {
                            _logger.LogInformation("Skipped disabled built-in '{Command}' for bot {BotId} (owned by an external integration)", firstToken, evt.BotId);
                            return false;
                        }
                    }
                }
            }
            catch (Exception disableEx)
            {
                _logger.LogDebug(disableEx, "Could not check disabled built-ins for bot {BotId}", evt.BotId);
            }

            var result = await _discordCommandHandler.HandleAsync(
                originBot, evt.Message, evt.IsModerator, evt.IsBroadcaster, evt.ChannelId);

            if (result.Handled)
            {
                await SendDiscordReplyAsync(result.Response, evt.BotId);
                _logger.LogInformation("Processed in-Discord moderation command from {Username}: {Command} -> {Response}",
                    evt.Username, evt.Message, result.Response);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing in-Discord moderation command: {Command}", evt.Message);
        }

        return false;
    }

    /// <summary>
    /// Records which Discord bot published a message, keyed by MessageId
    /// ("discord:{snowflake}") so the generic pipeline can recover the origin.
    /// Bounded: oldest entries drop off after MAX_DISCORD_MESSAGE_BOT_IDS.
    /// </summary>
    private void RememberDiscordBotOrigin(string? messageId, string? botId)
    {
        if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(botId))
        {
            return;
        }

        lock (_discordMessageBotIdsLock)
        {
            // Each message id is tagged once in practice; the Contains check
            // just guards against double-enqueue on a re-tag.
            if (!_discordMessageBotIds.ContainsKey(messageId))
            {
                _discordMessageBotIdOrder.Enqueue(messageId);
            }

            _discordMessageBotIds[messageId] = botId;

            while (_discordMessageBotIdOrder.Count > MAX_DISCORD_MESSAGE_BOT_IDS)
            {
                var oldest = _discordMessageBotIdOrder.Dequeue();
                // Only evict when the id was not re-tagged later in the queue.
                if (!_discordMessageBotIdOrder.Contains(oldest))
                {
                    _discordMessageBotIds.Remove(oldest);
                }
            }
        }
    }

    /// <summary>
    /// Recovers the Discord bot origin for a generic chat event: the recorded
    /// origin for its MessageId, or null when unknown/already evicted (callers
    /// then fall back to the primary routing target). Discord events are also
    /// published untagged by the generic pipeline for non-Discord platforms,
    /// which simply never hit this lookup.
    /// </summary>
    private string? ResolveDiscordBotOrigin(ChatMessageReceivedEvent evt)
    {
        if (!string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(evt.MessageId))
        {
            return null;
        }

        lock (_discordMessageBotIdsLock)
        {
            return _discordMessageBotIds.TryGetValue(evt.MessageId, out var botId) ? botId : null;
        }
    }

    public void UpdateConnectedPlatforms()
    {
        ConnectedPlatforms.Clear();

        _logger.LogInformation("Updating connected platforms. Twitch connected: {IsConnected}, Channel: {Channel}",
            _twitchService.IsConnected, _twitchService.Channel ?? "(none)");

        // Add "All Platforms" option first if any platform is connected
        bool hasAnyConnection = _twitchService.IsConnected || _kickService.IsConnected || _youtubeService.IsConnected || IsAnyDiscordConnected;
        if (hasAnyConnection)
        {
            ConnectedPlatforms.Add(new PlatformOption
            {
                Name = "All Platforms",
                Icon = "VyralLogo",
                Color = "#26A69A",
                IsAllPlatforms = true
            });
            _logger.LogInformation("Added 'All Platforms' option");
        }

        // Update per-platform connected flags and add Twitch if connected
        IsTwitchConnected = _twitchService.IsConnected;
        if (_twitchService.IsConnected)
        {
            _logger.LogInformation("Adding Twitch to connected platforms. Channel: {Channel}, Bot: {Bot}",
                _twitchService.Channel, _twitchService.BotUsername);
            ConnectedPlatforms.Add(new PlatformOption
            {
                Name = "Twitch",
                Icon = "Twitch",
                Color = "#9146FF"
            });
        }
        else
        {
            _logger.LogWarning("Twitch is NOT connected. Cannot add to platforms list.");
        }

        // Add Kick if connected
        IsKickConnected = _kickService.IsConnected;
        IsKickConfigured = _kickService.IsConnected || !string.IsNullOrEmpty(_kickService.Channel);
        if (_kickService.IsConnected)
        {
            _logger.LogInformation("Adding Kick to connected platforms");
            ConnectedPlatforms.Add(new PlatformOption
            {
                Name = "Kick",
                Icon = "Kickstarter",
                Color = "#53FC18"
            });
        }

        // Add YouTube if connected
        IsYoutubeConnected = _youtubeService.IsConnected;
        if (_youtubeService.IsConnected)
        {
            _logger.LogInformation("Adding YouTube to connected platforms");
            ConnectedPlatforms.Add(new PlatformOption
            {
                Name = "YouTube",
                Icon = "Youtube",
                Color = "#FF0000"
            });
        }

        // TODO: Add TikTok when implemented
        IsTiktokConnected = false;

        // Add Discord if connected (multi-bot: any manager bot or the setup bot)
        IsDiscordConnected = IsAnyDiscordConnected;
        if (IsDiscordConnected)
        {
            // V1 routing target: the Primary (first connected) manager bot; the
            // label surfaces its username so multi-bot setups are distinguishable.
            var primaryDiscord = _discordBotManager.Primary;
            var discordBotName = primaryDiscord?.BotUsername ?? _discordService.BotUsername;
            _logger.LogInformation("Adding Discord to connected platforms. Primary bot: {Bot} (manager bots: {ManagedCount}), ChannelId: {ChannelId}",
                discordBotName, _discordBotManager.Connected.Count,
                primaryDiscord?.SelectedChannelId ?? _discordService.SelectedChannelId);
            ConnectedPlatforms.Add(new PlatformOption
            {
                Name = "Discord", // must stay "Discord" — send paths switch on it
                Icon = "Discord",
                Color = "#5865F2" // Discord Blurple (PlatformToBrushConverter also maps this)
            });
        }

        _logger.LogInformation("Total connected platforms: {Count}", ConnectedPlatforms.Count);

        // Select "All Platforms" by default (first in list)
        if (ConnectedPlatforms.Count > 0 && SelectedPlatform == null)
        {
            SelectedPlatform = ConnectedPlatforms[0];
            _logger.LogInformation("Auto-selected platform: {Platform}", SelectedPlatform?.Name ?? string.Empty);
        }
        else if (ConnectedPlatforms.Count == 0)
        {
            _logger.LogWarning("No platforms connected!");
            SelectedPlatform = null;
        }
    }

    private async Task OnTwitchChatMessageReceivedAsync(TwitchChatMessageEvent evt)
    {
        try
        {
            _logger.LogInformation("📨 ChatViewModel received Twitch message from {Username}: {Message}", evt.Username, evt.Message);

            // Check if this is an echo of a message we just sent from the app.
            // Restricted to messages from the account the bot sends as (the IRC
            // login nick) so a viewer typing identical text is not filtered.
            // The tracking entry is intentionally kept on match so the generic
            // command pipeline (subscribed to a separate event) also filters it.
            var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
            lock (_recentMessagesLock)
            {
                if (_recentTwitchMessages.TryGetValue(messageKey, out var sentTime))
                {
                    var age = DateTime.Now - sentTime;
                    if (age.TotalSeconds < 10) // Echo window: 10 seconds
                    {
                        if (IsOwnTwitchAccount(evt.Username))
                        {
                            _logger.LogInformation("🔇 Ignoring echo of recently sent Twitch message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
                            return;
                        }
                    }
                    else
                    {
                        _recentTwitchMessages.Remove(messageKey);
                    }
                }

                // Cleanup old entries (> 10 seconds)
                var expiredKeys = _recentTwitchMessages
                    .Where(kvp => (DateTime.Now - kvp.Value).TotalSeconds > 10)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in expiredKeys)
                {
                    _recentTwitchMessages.Remove(key);
                }
            }

            if (!TwitchFilterEnabled)
            {
                _logger.LogDebug("Twitch messages filtered out (TwitchFilterEnabled=false)");
                return;
            }

            // If platforms list is empty, refresh it (connection might have happened after initialization)
            if (ConnectedPlatforms.Count == 0)
            {
                _logger.LogInformation("Platforms list is empty, refreshing...");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    UpdateConnectedPlatforms();
                });
            }

            // Add Twitch message to UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var message = new ChatMessage
                {
                    Platform = "Twitch",
                    Username = evt.DisplayName ?? evt.Username ?? string.Empty,
                    Message = evt.Message ?? string.Empty,
                    Timestamp = evt.Timestamp.ToLocalTime(),
                    Color = evt.Color ?? "#FFFFFF",
                    IsModerator = evt.IsModerator,
                    IsSubscriber = evt.IsSubscriber,
                    IsVip = evt.IsVip,
                    IsBroadcaster = evt.IsBroadcaster,
                    Badges = evt.Badges ?? new Dictionary<string, string>()
                };
                message.InitializeBadges();
                Messages.Add(message);

                _logger.LogInformation("✅ Twitch message added to UI. Total messages: {Count}", Messages.Count);

                // Keep only last 500 messages
                while (Messages.Count > 500)
                {
                    Messages.RemoveAt(0);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling Twitch chat message from {Username}", evt?.Username ?? "unknown");
        }
    }

    private async Task OnChatMessageReceivedAsync(ChatMessageReceivedEvent evt)
    {
        if (evt == null) return;

        // Filter by platform-specific enable flags
        if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase) && !TwitchFilterEnabled) return;
        if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase) && !KickFilterEnabled) return;
        if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase) && !YoutubeFilterEnabled) return;
        if (string.Equals(evt.Platform, "TikTok", StringComparison.OrdinalIgnoreCase) && !TiktokFilterEnabled) return;
        if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase) && !DiscordFilterEnabled) return;

        // Skip echoes of messages the bot itself just sent, so the bot never
        // executes its own outgoing text as a viewer command. The per-platform
        // handlers apply the same account-restricted check for display.
        if (await IsEchoOfOwnRecentMessageAsync(evt)) return;

        // Deduplicate: the same message can be delivered through more than one transport
        if (!TryMarkMessageProcessed(evt)) return;

        // Resolve the channel name for this platform (used for broadcaster
        // detection and command variable replacement)
        var channel = ResolveChannelName(evt);

        // Resolve the Discord bot this message came from (per-origin routing:
        // replies go back through the same bot, and command execution filters
        // by bot scope). Null for every other platform and for untagged events.
        var discordBotId = ResolveDiscordBotOrigin(evt);

        // RESPONSE WAITERS (workflow "wait_for_response" node): every inbound
        // user message is fed to the registry exactly once, AFTER echo
        // filtering and dedup so the bot's own outgoing messages never satisfy
        // a waiter and duplicate transports never double-resume a workflow.
        // Cheap no-op when nobody is waiting; Feed never throws (guarded anyway).
        try
        {
            _responseWaiters?.Feed(
                evt.Platform ?? string.Empty,
                evt.Username ?? string.Empty,
                channel ?? string.Empty,
                evt.Message ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Response waiter feed failed for {Platform}/{Username}", evt.Platform, evt.Username);
        }

        // WORKFLOW COMMAND TRIGGERS (Visual Automation, BotGhostLikeIntegration
        // phases 3/25): workflow command triggers get the first shot at this
        // message, BEFORE the CommandService lookup. When a workflow command
        // matches, the workflow runs fire-and-forget and fully consumes the
        // message — CommandService, currency, giveaways, polls and watchtime
        // never see it. When nothing matches, everything below flows exactly
        // as before. Matching only intercepts exact registered workflow
        // command names, so existing commands are untouched.
        if (_workflowTriggerService != null
            && _workflowTriggerService.TryHandleCommand(
                evt.Platform,
                evt.Username,
                evt.Message ?? string.Empty,
                channel ?? string.Empty,
                discordBotId,
                async response =>
                {
                    // Direct-reply callback handed to the workflow execution:
                    // same routing the CommandService response path uses below.
                    if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        await _twitchService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
                    {
                        await _kickService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordReplyAsync(response, discordBotId);
                    }
                },
                // Identity seeding for the workflow permission option: the
                // generic event carries badge flags (not raw role lists or
                // platform user ids), so roles are badge-derived and the id
                // stays null where unavailable.
                BuildWorkflowRoleList(evt.IsModerator, evt.IsBroadcaster, evt.IsVip, evt.IsSubscriber),
                userId: null,
                isBot: false))
        {
            return; // handled by a workflow command trigger
        }

        // Process unified commands first (if enabled)
        if (_commandService != null && !string.IsNullOrEmpty(evt.Message) && _commandService.IsCommand(evt.Message))
        {
            try
            {
                // Get currency balance for command context
                int? currencyBalance = null;
                if (_currencyService != null)
                {
                    try
                    {
                        currencyBalance = await _currencyService.GetBalanceAsync(evt.Username, evt.Platform);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get currency balance for command context");
                    }
                }

                // Build command context
                var context = new CommandContext
                {
                    Username = evt.Username,
                    Platform = evt.Platform,
                    Channel = channel,
                    IsModerator = evt.IsModerator,
                    IsSubscriber = evt.IsSubscriber,
                    IsVIP = evt.IsVip,
                    IsBroadcaster = evt.IsBroadcaster ||
                        string.Equals(evt.Username, channel, StringComparison.OrdinalIgnoreCase),
                    CurrencyBalance = currencyBalance ?? 0,
                    CustomVariables = new Dictionary<string, string>(),
                    DiscordBotId = discordBotId
                };

                // Execute command (per-bot scope filter applies on Discord)
                var response = await _commandService.ExecuteAsync(evt.Message, context, discordBotId);

                // Action-sequence commands: Core returned the $ACTIONS$<id>
                // marker instead of a reply — run the steps here (this layer
                // owns every platform sender) and use the run summary as the
                // chat reply below.
                if (response is not null
                    && response.StartsWith(ICommandService.ActionsMarkerPrefix, StringComparison.Ordinal))
                {
                    response = await RunCommandActionsAsync(response, context);
                }

                if (response != null)
                {
                    // Send command response to chat
                    if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        await _twitchService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
                    {
                        await _kickService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordReplyAsync(response, discordBotId);
                    }

                    _logger.LogInformation("Processed unified command: {Command} -> {Response}", evt.Message, response);
                    return; // Don't process as regular chat or currency command
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing unified command: {Command}", evt.Message);
            }
        }

        // Process currency commands if message starts with ! (fallback for currency-specific commands)
        if (_currencyCommandService != null && evt.Message.StartsWith("!"))
        {
            try
            {
                var response = await _currencyCommandService.ProcessCommandAsync(
                    evt.Message, evt.Username, evt.Platform, evt.IsModerator);

                if (response != null)
                {
                    // Send command response to chat
                    if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        await _twitchService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
                    {
                        await _kickService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordReplyAsync(response, discordBotId);
                    }

                    _logger.LogInformation("Processed currency command: {Command} -> {Response}", evt.Message, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing currency command: {Command}", evt.Message);
            }
        }

        // Resolve entry eligibility data shared by the giveaway and poll command
        // fallbacks below (KI-5): watchtime from the watchtime service and a
        // best-effort follower lookup.
        bool isFollower = false;
        int watchtimeMinutes = 0;
        if (evt.Message.StartsWith("!"))
        {
            if (_watchtimeService != null)
            {
                try
                {
                    var watchtime = await _watchtimeService.GetUserWatchtimeAsync(evt.Username, evt.Platform);
                    watchtimeMinutes = watchtime?.TotalMinutes ?? 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to look up watchtime for {Username} ({Platform})", evt.Username, evt.Platform);
                }
            }

            try
            {
                isFollower = await IsFollowerForEligibilityAsync(evt.Platform, evt.Username);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve follower status for {Username} ({Platform})", evt.Username, evt.Platform);
            }
        }

        // Process giveaway commands (including custom entry keywords)
        if (_giveawayCommandService != null && evt.Message.StartsWith("!"))
        {
            try
            {
                var response = await _giveawayCommandService.ProcessCommandAsync(
                    evt.Message,
                    evt.Username,
                    evt.Platform,
                    evt.IsModerator,
                    evt.IsSubscriber,
                    isFollower,
                    watchtimeMinutes);

                if (response != null)
                {
                    // Send command response to chat
                    if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        await _twitchService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
                    {
                        await _kickService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordReplyAsync(response, discordBotId);
                    }

                    _logger.LogInformation("Processed giveaway command: {Command} -> {Response}", evt.Message, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing giveaway command: {Command}", evt.Message);
            }
        }

        // Process poll commands
        if (_pollCommandService != null && evt.Message.StartsWith("!poll"))
        {
            try
            {
                var response = await _pollCommandService.ProcessCommandAsync(
                    evt.Message,
                    evt.Username,
                    evt.Platform,
                    evt.IsModerator,
                    evt.IsSubscriber,
                    isFollower,
                    watchtimeMinutes);

                if (response != null)
                {
                    // Send command response to chat
                    if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        await _twitchService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
                    {
                        await _kickService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordReplyAsync(response, discordBotId);
                    }

                    _logger.LogInformation("Processed poll command: {Command} -> {Response}", evt.Message, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing poll command: {Command}", evt.Message);
            }
        }

        // Process watchtime commands
        if (_watchtimeCommandService != null && evt.Message.StartsWith("!watchtime"))
        {
            try
            {
                var response = await _watchtimeCommandService.ProcessCommandAsync(
                    evt.Message,
                    evt.Username,
                    evt.Platform);

                if (response != null)
                {
                    // Send command response to chat
                    if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
                    {
                        await _twitchService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
                    {
                        await _kickService.SendMessageAsync(response);
                    }
                    else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendDiscordReplyAsync(response, discordBotId);
                    }

                    _logger.LogInformation("Processed watchtime command: {Command} -> {Response}", evt.Message, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing watchtime command: {Command}", evt.Message);
            }
        }

        // Track user watchtime activity
        if (_watchtimeService != null)
        {
            try
            {
                await _watchtimeService.RecordUserActivityAsync(evt.Username, evt.Platform);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error recording watchtime for {Username}", evt.Username);
            }
        }

        // Award chat activity currency (if not a command)
        if (_currencyService != null && !evt.Message.StartsWith("!"))
        {
            try
            {
                await _currencyService.AwardChatActivityAsync(evt.Username, evt.Platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award chat activity currency to {Username}", evt.Username);
            }
        }

        // Award the one-time first-chat bonus. Dedupe semantics verified: the
        // service only pays out when the viewer has no currency record yet, so
        // calling this for every incoming message is safe.
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardFirstChatBonusAsync(evt.Username, evt.Platform);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to award first-chat bonus to {Username}", evt.Username);
            }
        }

        // NOTE: UI rendering is intentionally NOT done here. The per-platform
        // handlers (OnTwitchChatMessageReceivedAsync / OnKickChatMessageReceivedAsync /
        // OnYouTubeChatMessageReceivedAsync) already add the message to the feed,
        // so rendering here would duplicate every line.
    }

    /// <summary>
    /// Badge-derived role list for workflow identity seeding ("moderator",
    /// "broadcaster", "vip", "subscriber" — only the badges the sender holds).
    /// Empty string when none apply, which the trigger service treats as an
    /// unseeded (fail-open) permission context. Used by the generic pipeline's
    /// TryHandleCommand call, where the platform event carries badge flags but
    /// no raw role names or platform user ids.
    /// </summary>
    private static string BuildWorkflowRoleList(bool isModerator, bool isBroadcaster, bool isVip, bool isSubscriber)
    {
        var roles = new List<string>(4);
        if (isBroadcaster) roles.Add("broadcaster");
        if (isModerator) roles.Add("moderator");
        if (isVip) roles.Add("vip");
        if (isSubscriber) roles.Add("subscriber");
        return string.Join(",", roles);
    }

    /// <summary>
    /// Resolves the channel name for a generic chat event, preferring the live
    /// service connection state and falling back to the event's channel field.
    /// </summary>
    private string ResolveChannelName(ChatMessageReceivedEvent evt)
    {
        if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
            return _twitchService.Channel ?? evt.Channel;
        if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
            return _kickService.Channel ?? evt.Channel;
        if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
            return _youtubeService.Channel ?? evt.Channel;
        return evt.Channel;
    }

    /// <summary>
    /// Returns true when this event is an echo of a message the bot itself sent
    /// recently (same content within the echo window AND sent from the account
    /// the bot sends as on that platform).
    /// </summary>
    private async Task<bool> IsEchoOfOwnRecentMessageAsync(ChatMessageReceivedEvent evt)
    {
        var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
        if (messageKey.Length == 0) return false;

        bool isOwnAccount;
        int echoWindowSeconds;

        if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
        {
            isOwnAccount = IsOwnTwitchAccount(evt.Username);
            echoWindowSeconds = 10;
        }
        else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
        {
            isOwnAccount = await IsOwnKickAccountAsync(evt.Username);
            echoWindowSeconds = 10;
        }
        else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
        {
            isOwnAccount = await IsOwnYouTubeAccountAsync(evt.Username);
            echoWindowSeconds = 15; // YouTube polling can be slow, wider echo window
        }
        else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            isOwnAccount = IsOwnDiscordAccount(evt.Username);
            echoWindowSeconds = 10;
        }
        else
        {
            return false;
        }

        if (!isOwnAccount) return false;

        lock (_recentMessagesLock)
        {
            Dictionary<string, DateTime> recent = evt.Platform.ToLowerInvariant() switch
            {
                "twitch" => _recentTwitchMessages,
                "kick" => _recentKickMessages,
                "youtube" => _recentYoutubeMessages,
                "discord" => _recentDiscordMessages,
                _ => _recentTwitchMessages
            };

            if (recent.TryGetValue(messageKey, out var sentTime))
            {
                return (DateTime.Now - sentTime).TotalSeconds < echoWindowSeconds;
            }
        }

        return false;
    }

    /// <summary>
    /// Twitch sends as the nick the IRC client logged in with.
    /// </summary>
    private bool IsOwnTwitchAccount(string? username)
    {
        return !string.IsNullOrEmpty(username)
            && !string.IsNullOrEmpty(_twitchService.BotUsername)
            && string.Equals(username, _twitchService.BotUsername, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Kick sends through the broadcaster's account (KickLib uses the
    /// broadcaster token), so the echo source is the Platform row's Username.
    /// </summary>
    private async Task<bool> IsOwnKickAccountAsync(string? username)
    {
        if (string.IsNullOrEmpty(username)) return false;

        var platform = await GetCachedPlatformRowAsync("Kick");
        var senderName = platform?.Username ?? _kickService.BotUsername;
        return !string.IsNullOrEmpty(senderName)
            && string.Equals(username, senderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// YouTube sends as the authorized channel (title stored in the Platform
    /// row's Username).
    /// </summary>
    private async Task<bool> IsOwnYouTubeAccountAsync(string? username)
    {
        if (string.IsNullOrEmpty(username)) return false;

        var platform = await GetCachedPlatformRowAsync("YouTube");
        var senderName = platform?.Username ?? _youtubeService.Channel;
        return !string.IsNullOrEmpty(senderName)
            && string.Equals(username, senderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Discord sends as the bot account the gateway logged in with. With
    /// multiple bots running, ANY of their accounts can echo our own sends.
    /// </summary>
    private bool IsOwnDiscordAccount(string? username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return false;
        }

        // Manager bots (multi-bot)
        foreach (var bot in _discordBotManager.Connected)
        {
            if (!string.IsNullOrEmpty(bot.BotUsername)
                && string.Equals(username, bot.BotUsername, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Wizard's setup bot (DI singleton)
        return !string.IsNullOrEmpty(_discordService.BotUsername)
            && string.Equals(username, _discordService.BotUsername, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads the Platform rows for echo/follower checks with a short in-memory
    /// cache to avoid hitting the database on every message.
    /// </summary>
    private async Task<Platform?> GetCachedPlatformRowAsync(string platformName)
    {
        if (_dataService == null) return null;

        lock (_platformRowsLock)
        {
            if (DateTime.UtcNow - _platformRowsLoadedAt < PlatformRowCacheTtl)
            {
                return platformName switch
                {
                    "Twitch" => _twitchPlatformRow,
                    "Kick" => _kickPlatformRow,
                    "YouTube" => _youtubePlatformRow,
                    _ => null
                };
            }
        }

        Platform? twitchRow = null;
        Platform? kickRow = null;
        Platform? youtubeRow = null;
        try
        {
            twitchRow = await _dataService.GetPlatformByNameAsync("Twitch");
            kickRow = await _dataService.GetPlatformByNameAsync("Kick");
            youtubeRow = await _dataService.GetPlatformByNameAsync("YouTube");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load platform rows for echo/follower checks");
        }

        lock (_platformRowsLock)
        {
            _twitchPlatformRow = twitchRow;
            _kickPlatformRow = kickRow;
            _youtubePlatformRow = youtubeRow;
            _platformRowsLoadedAt = DateTime.UtcNow;
            return platformName switch
            {
                "Twitch" => twitchRow,
                "Kick" => kickRow,
                "YouTube" => youtubeRow,
                _ => null
            };
        }
    }

    /// <summary>
    /// Marks a generic chat event as processed; returns false when the same
    /// message has already been handled (duplicate transport delivery).
    /// </summary>
    private bool TryMarkMessageProcessed(ChatMessageReceivedEvent evt)
    {
        var key = !string.IsNullOrEmpty(evt.MessageId)
            ? evt.MessageId!
            : $"{evt.Platform}:{evt.Username}:{evt.Timestamp.Ticks}:{evt.Message?.GetHashCode()}";

        lock (_processedMessageIdsLock)
        {
            if (!_processedMessageIds.Add(key))
            {
                _logger.LogDebug("Skipping duplicate chat message event (key: {Key})", key);
                return false;
            }

            _processedMessageIdOrder.Enqueue(key);
            while (_processedMessageIdOrder.Count > MAX_PROCESSED_MESSAGE_IDS)
            {
                var oldest = _processedMessageIdOrder.Dequeue();
                _processedMessageIds.Remove(oldest);
            }
            return true;
        }
    }

    /// <summary>
    /// Best-effort follower resolution for giveaway/poll eligibility (KI-5).
    /// Twitch: Helix channels/followers lookup (requires Platform.UserId,
    /// ClientId and a decryptable access token; results cached briefly).
    /// Kick: no follower-lookup API is exposed by KickLib 1.8.0 - TODO.
    /// YouTube: no follower concept - TODO.
    /// </summary>
    private async Task<bool> IsFollowerForEligibilityAsync(string platform, string username)
    {
        if (string.IsNullOrEmpty(username)) return false;

        if (string.Equals(platform, "Twitch", StringComparison.OrdinalIgnoreCase))
        {
            return await IsTwitchFollowerAsync(username);
        }

        // TODO (KI-5): Kick has no channel-followers lookup in KickLib 1.8.0 and
        // YouTube has no follower concept; follower-gated entries remain
        // effectively disabled on those platforms until an API is wired in.
        return false;
    }

    private async Task<bool> IsTwitchFollowerAsync(string username)
    {
        if (_dataService == null || !_twitchService.IsConnected) return false;

        var cacheKey = $"twitch:{username.ToLowerInvariant()}";
        lock (_followerCacheLock)
        {
            if (_followerCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            {
                return cached.IsFollower;
            }
        }

        var platform = await GetCachedPlatformRowAsync("Twitch");
        var broadcasterId = platform?.UserId;
        var clientId = platform?.ClientId;
        var encryptedToken = platform?.AccessToken;
        if (string.IsNullOrEmpty(broadcasterId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(encryptedToken))
        {
            // Helix needs the broadcaster's user id (see KI-2) - without it we
            // cannot resolve follower status, so keep the safe default.
            return false;
        }

        string accessToken;
        try
        {
            accessToken = TwitchOAuthService.Decrypt(encryptedToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt Twitch access token for follower lookup");
            return false;
        }

        try
        {
            // Resolve the viewer's user id by login name
            string? userId = null;
            using (var userRequest = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(username)}"))
            {
                userRequest.Headers.Add("Client-Id", clientId);
                userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var userResponse = await _helixHttpClient.SendAsync(userRequest);
                if (!userResponse.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Twitch Helix users lookup failed for {Username}: {Status}",
                        username, userResponse.StatusCode);
                    return false;
                }

                using var userDoc = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
                if (userDoc.RootElement.TryGetProperty("data", out var userData) &&
                    userData.GetArrayLength() > 0 &&
                    userData[0].TryGetProperty("id", out var idProp))
                {
                    userId = idProp.GetString();
                }
            }

            if (string.IsNullOrEmpty(userId))
            {
                // Viewer does not exist (or API shape unexpected) - not a follower
                return false;
            }

            // Check the follow relationship
            using var followRequest = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.twitch.tv/helix/channels/followers?broadcaster_id={Uri.EscapeDataString(broadcasterId)}&user_id={Uri.EscapeDataString(userId)}");
            followRequest.Headers.Add("Client-Id", clientId);
            followRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var followResponse = await _helixHttpClient.SendAsync(followRequest);
            if (!followResponse.IsSuccessStatusCode)
            {
                _logger.LogDebug("Twitch Helix followers lookup failed for {Username}: {Status} (token may lack moderator:read:followers)",
                    username, followResponse.StatusCode);
                return false;
            }

            using var followDoc = JsonDocument.Parse(await followResponse.Content.ReadAsStringAsync());
            var isFollower = followDoc.RootElement.TryGetProperty("total", out var totalProp) && totalProp.GetInt32() > 0;

            lock (_followerCacheLock)
            {
                _followerCache[cacheKey] = (isFollower, DateTime.UtcNow.Add(FollowerCacheTtl));
            }
            return isFollower;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twitch follower lookup failed for {Username}", username);
            return false;
        }
    }

    /// <summary>
    /// Persists an <see cref="EventStatistic"/> row (KI-3) for a chat event handler.
    /// </summary>
    private async Task SaveEventStatisticAsync(string platform, string eventType, string username,
        Dictionary<string, string> variables, bool wasAutoReplied)
    {
        if (_dataService == null) return;

        try
        {
            await _dataService.SaveEventStatisticAsync(new EventStatistic
            {
                Platform = platform,
                EventType = eventType,
                Username = username,
                EventData = JsonSerializer.Serialize(variables),
                Timestamp = DateTime.UtcNow,
                WasAutoReplied = wasAutoReplied
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist event statistic for {Platform}/{EventType}", platform, eventType);
        }
    }

    private Task OnPlatformRoutingChangedAsync(PlatformChatRoutingChangedEvent evt)
    {
        // Update filter flags so incoming messages are routed/blocked immediately
        switch (evt.Platform)
        {
            case "Twitch":
                TwitchFilterEnabled = evt.Enabled;
                break;
            case "Kick":
                KickFilterEnabled = evt.Enabled;
                break;
            case "YouTube":
                YoutubeFilterEnabled = evt.Enabled;
                break;
            case "TikTok":
                TiktokFilterEnabled = evt.Enabled;
                break;
            case "Discord":
                DiscordFilterEnabled = evt.Enabled;
                break;
        }

        _logger.LogInformation("Platform routing changed: {Platform} -> {Enabled}", evt.Platform, evt.Enabled);
        return Task.CompletedTask;
    }

    private async Task OnPlatformConnectionChangedAsync(PlatformConnectionChangedEvent evt)
    {
        _logger.LogInformation("Platform connection changed: {Platform} -> {Connected}", evt.Platform, evt.Connected);

        // Update the connected platforms list on UI thread
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateConnectedPlatforms();
        });

        _logger.LogInformation("✅ Connected platforms updated after {Platform} {Status}",
            evt.Platform, evt.Connected ? "connected" : "disconnected");
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        _logger.LogInformation("SendMessageAsync called. MessageInput: '{Input}'", MessageInput);

        if (string.IsNullOrWhiteSpace(MessageInput))
        {
            _logger.LogWarning("MessageInput is empty or whitespace");
            return;
        }

        // Discord-scoped "/" commands (e.g. "/kick @user", "/slowmode 10"):
        // executed by the Discord bot and answered in the app chat as a system
        // message — NEVER posted to Discord. Checked before any platform/send
        // logic (including platform selection and send-to-all) so "/" text can
        // never reach the normal send path or echo suppression. "!" remains
        // the cross-platform multichat prefix and is unaffected.
        var slashCommand = MessageInput.Trim();
        if (slashCommand.StartsWith("/", StringComparison.Ordinal) && slashCommand.Length > 1)
        {
            await HandleDiscordSlashCommandAsync(slashCommand);
            return;
        }

        if (SelectedPlatform == null)
        {
            _logger.LogWarning("No platform selected for sending message");
            System.Windows.MessageBox.Show("No platform selected. Please connect to a platform first.", "No Platform", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // If "All Platforms" is selected, send to all platforms
        if (SelectedPlatform.IsAllPlatforms)
        {
            await SendToAllPlatformsAsync();
            return;
        }

        try
        {
            _logger.LogInformation("Attempting to send message to {Platform}: {Message}", SelectedPlatform?.Name ?? string.Empty, MessageInput);

            switch (SelectedPlatform?.Name ?? string.Empty)
            {
                case "Twitch":
                    _logger.LogInformation("Twitch IsConnected: {IsConnected}, Channel: {Channel}", _twitchService.IsConnected, _twitchService.Channel);
                    if (_twitchService.IsConnected)
                    {
                        var messageText = MessageInput;

                        // Replace variables if service is available
                        if (_variableReplacementService != null && messageText.Contains('{'))
                        {
                            try
                            {
                                _logger.LogInformation("🔄 Attempting variable replacement. Service available: {Available}, Message: '{Message}'",
                                    _variableReplacementService != null, messageText);

                                var contextVars = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("📋 Context variables: {Vars}",
                                    string.Join(", ", contextVars.Select(kv => $"{kv.Key}={kv.Value}")));

                                var availableVars = await _variableReplacementService!.GetAvailableVariablesAsync(SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("📚 Available database variables: {Count} - {Names}",
                                    availableVars.Count,
                                    availableVars.Count > 0 ? string.Join(", ", availableVars.Keys) : "NONE");

                                messageText = await _variableReplacementService!.ReplaceVariablesAsync(
                                    messageText,
                                    contextVars,
                                    SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("✅ Variables replaced in message. Original: '{Original}', Replaced: '{Replaced}'",
                                    MessageInput, messageText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "❌ Error replacing variables in message, using original text");
                                // Continue with original messageText if replacement fails
                            }
                        }
                        else
                        {
                            if (_variableReplacementService == null)
                                _logger.LogWarning("⚠️ Variable replacement service is NULL");
                            else if (!messageText.Contains('{'))
                                _logger.LogDebug("No variables to replace (no '{{' found in message)");
                        }

                        _logger.LogInformation("Sending message to Twitch...");

                        // Track sent message BEFORE sending (echo arrives very fast!)
                        // Key uses message-only (no username) so it matches regardless of BotUsername
                        var twitchMessageKey = messageText.Trim().ToLowerInvariant();
                        lock (_recentMessagesLock)
                        {
                            _recentTwitchMessages[twitchMessageKey] = DateTime.Now;
                            _logger.LogInformation("🔒 Tracking sent Twitch message to filter echo: {MessageKey}", twitchMessageKey);
                        }

                        await _twitchService.SendMessageAsync(messageText);
                        _logger.LogInformation("Message sent to Twitch successfully");

                        // Add sent message to chat (echo will be filtered by _recentTwitchMessages tracking)
                        var twitchMessage = new ChatMessage
                        {
                            Platform = "Twitch",
                            Username = _twitchService.BotUsername ?? "Bot",
                            Message = messageText,
                            Timestamp = DateTime.Now,
                            Color = "#9146FF",
                            IsModerator = false,
                            IsSubscriber = false,
                            IsVip = false,
                            IsBroadcaster = true,
                            Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
                        };
                        twitchMessage.InitializeBadges();
                        Messages.Add(twitchMessage);

                        _logger.LogInformation("Added sent message to chat UI");
                    }
                    else
                    {
                        _logger.LogWarning("Twitch is not connected, cannot send message");
                        System.Windows.MessageBox.Show("Twitch connection lost. Please reconnect.", "Not Connected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                    break;

                case "Kick":
                    _logger.LogInformation("Kick IsConnected: {IsConnected}, Channel: {Channel}", _kickService.IsConnected, _kickService.Channel);
                    if (_kickService.IsConnected)
                    {
                        var messageText = MessageInput;

                        // Replace variables if service is available
                        if (_variableReplacementService != null && messageText.Contains('{'))
                        {
                            try
                            {
                                _logger.LogInformation("🔄 Attempting variable replacement. Service available: {Available}, Message: '{Message}'",
                                    _variableReplacementService != null, messageText);

                                var contextVars = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("📋 Context variables: {Vars}",
                                    string.Join(", ", contextVars.Select(kv => $"{kv.Key}={kv.Value}")));

                                var availableVars = await _variableReplacementService!.GetAvailableVariablesAsync(SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("📚 Available database variables: {Count} - {Names}",
                                    availableVars.Count,
                                    availableVars.Count > 0 ? string.Join(", ", availableVars.Keys) : "NONE");

                                messageText = await _variableReplacementService!.ReplaceVariablesAsync(
                                    messageText,
                                    contextVars,
                                    SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("✅ Variables replaced in message. Original: '{Original}', Replaced: '{Replaced}'",
                                    MessageInput, messageText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "❌ Error replacing variables in message, using original text");
                                // Continue with original messageText if replacement fails
                            }
                        }
                        else
                        {
                            if (_variableReplacementService == null)
                                _logger.LogWarning("⚠️ Variable replacement service is NULL");
                            else if (!messageText.Contains('{'))
                                _logger.LogDebug("No variables to replace (no '{{' found in message)");
                        }

                        // Track sent message BEFORE sending (echo arrives very fast!)
                        // Key uses message-only (no username) so it matches regardless of BotUsername
                        var messageKey = messageText.Trim().ToLowerInvariant();
                        lock (_recentMessagesLock)
                        {
                            _recentKickMessages[messageKey] = DateTime.Now;
                            _logger.LogInformation("🔒 Tracking sent Kick message to filter echo: {MessageKey}", messageKey);
                        }

                        _logger.LogInformation("Sending message to Kick...");
                        await _kickService.SendMessageAsync(messageText);
                        _logger.LogInformation("Message sent to Kick successfully");

                        // Add sent message to chat (will ignore echo from WebSocket)
                        var sentMessage = new ChatMessage
                        {
                            Platform = "Kick",
                            Username = _kickService.BotUsername ?? "Bot",
                            Message = messageText,
                            Timestamp = DateTime.Now,
                            Color = "#53FC18",
                            IsModerator = false,
                            IsSubscriber = false,
                            IsVip = false,
                            IsBroadcaster = true,
                            Badges = new Dictionary<string, string> { { "Broadcaster", "Broadcaster" } } // Kick uses PascalCase
                        };

                        _logger.LogInformation("📛 Sent message has {BadgeCount} badges: {Badges}", sentMessage.Badges.Count, string.Join(", ", sentMessage.Badges.Select(b => $"{b.Key}={b.Value}")));

                        sentMessage.InitializeBadges();
                        Messages.Add(sentMessage);

                        _logger.LogInformation("Added sent message to chat UI");
                    }
                    else
                    {
                        _logger.LogWarning("Kick is not connected, cannot send message");
                        System.Windows.MessageBox.Show("Kick connection lost. Please reconnect.", "Not Connected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                    break;

                case "YouTube":
                    _logger.LogInformation("YouTube IsConnected: {IsConnected}, Channel: {Channel}", _youtubeService.IsConnected, _youtubeService.Channel);
                    if (_youtubeService.IsConnected)
                    {
                        var messageText = MessageInput;

                        // Replace variables if service is available
                        if (_variableReplacementService != null && messageText.Contains('{'))
                        {
                            try
                            {
                                var contextVars = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
                                messageText = await _variableReplacementService!.ReplaceVariablesAsync(
                                    messageText,
                                    contextVars,
                                    SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("✅ Variables replaced for YouTube. Original: '{Original}', Replaced: '{Replaced}'",
                                    MessageInput, messageText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "❌ Error replacing variables in message, using original text");
                            }
                        }

                        // Track sent message to filter echo
                        var ytMessageKey = messageText.Trim().ToLowerInvariant();
                        lock (_recentMessagesLock)
                        {
                            _recentYoutubeMessages[ytMessageKey] = DateTime.Now;
                            _logger.LogInformation("🔒 Tracking sent YouTube message to filter echo: {MessageKey}", ytMessageKey);
                        }

                        _logger.LogInformation("Sending message to YouTube...");
                        await _youtubeService.SendMessageAsync(messageText);
                        _logger.LogInformation("Message sent to YouTube successfully");

                        // Add sent message to chat
                        var ytMessage = new ChatMessage
                        {
                            Platform = "YouTube",
                            Username = _youtubeService.BotUsername ?? "Bot",
                            Message = messageText,
                            Timestamp = DateTime.Now,
                            Color = "#FF0000",
                            IsModerator = false,
                            IsSubscriber = false,
                            IsVip = false,
                            IsBroadcaster = true,
                            Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
                        };
                        ytMessage.InitializeBadges();
                        Messages.Add(ytMessage);

                        _logger.LogInformation("Added sent YouTube message to chat UI");
                    }
                    else
                    {
                        _logger.LogWarning("YouTube is not connected, cannot send message");
                        System.Windows.MessageBox.Show("YouTube connection lost. Please reconnect.", "Not Connected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                    break;

                case "Discord":
                    // Manual send: no per-bot origin, so resolve without one
                    // (first connected manager bot, else the setup bot).
                    var discordSend = ResolveDiscordSendTarget(null);
                    _logger.LogInformation("Discord IsConnected: {IsConnected}, SelectedChannelId: {ChannelId}",
                        discordSend?.IsConnected, discordSend?.SelectedChannelId);
                    if (discordSend != null && discordSend.IsConnected)
                    {
                        var messageText = MessageInput;

                        // Replace variables if service is available
                        if (_variableReplacementService != null && messageText.Contains('{'))
                        {
                            try
                            {
                                var contextVars = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
                                messageText = await _variableReplacementService!.ReplaceVariablesAsync(
                                    messageText,
                                    contextVars,
                                    SelectedPlatform?.Name ?? string.Empty);
                                _logger.LogInformation("✅ Variables replaced for Discord. Original: '{Original}', Replaced: '{Replaced}'",
                                    MessageInput, messageText);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "❌ Error replacing variables in message, using original text");
                            }
                        }

                        // Track sent message BEFORE sending (echo arrives very fast!)
                        // Key uses message-only (no username) so it matches regardless of BotUsername
                        var discordMessageKey = messageText.Trim().ToLowerInvariant();
                        lock (_recentMessagesLock)
                        {
                            _recentDiscordMessages[discordMessageKey] = DateTime.Now;
                            _logger.LogInformation("🔒 Tracking sent Discord message to filter echo: {MessageKey}", discordMessageKey);
                        }

                        _logger.LogInformation("Sending message to Discord...");
                        await discordSend.SendMessageAsync(messageText);
                        _logger.LogInformation("Message sent to Discord successfully");

                        // Add sent message to chat (echo from the gateway is skipped by the
                        // own-account check in OnDiscordChatMessageReceivedAsync)
                        var discordMessage = new ChatMessage
                        {
                            Platform = "Discord",
                            Username = discordSend.BotUsername ?? "Bot",
                            Message = messageText,
                            Timestamp = DateTime.Now,
                            Color = "#5865F2",
                            IsModerator = false,
                            IsSubscriber = false,
                            IsVip = false,
                            IsBroadcaster = true,
                            Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
                        };
                        discordMessage.InitializeBadges();
                        Messages.Add(discordMessage);

                        _logger.LogInformation("Added sent Discord message to chat UI");
                    }
                    else
                    {
                        _logger.LogWarning("Discord is not connected, cannot send message");
                        System.Windows.MessageBox.Show("Discord connection lost. Please reconnect.", "Not Connected", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                    break;

                // TODO: Add TikTok, etc.
                default:
                    _logger.LogWarning("Unknown platform: {Platform}", SelectedPlatform?.Name ?? string.Empty);
                    break;
            }

            MessageInput = "";
            _logger.LogInformation("Message input cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to {Platform}", SelectedPlatform?.Name ?? string.Empty);
            System.Windows.MessageBox.Show($"Error sending message: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SendToAllPlatformsAsync()
    {
        _logger.LogInformation("SendToAllPlatformsAsync called. MessageInput: '{Input}'", MessageInput);

        if (string.IsNullOrWhiteSpace(MessageInput))
        {
            _logger.LogWarning("MessageInput is empty or whitespace");
            return;
        }

        // Same "/" interception as SendMessageAsync: this command is also bound
        // directly in ChatView.xaml (send-to-all button), so a Discord slash
        // command must never leak into the broadcast path.
        var slashCommand = MessageInput.Trim();
        if (slashCommand.StartsWith("/", StringComparison.Ordinal) && slashCommand.Length > 1)
        {
            await HandleDiscordSlashCommandAsync(slashCommand);
            return;
        }

        if (ConnectedPlatforms.Count == 0)
        {
            _logger.LogWarning("No platforms connected");
            System.Windows.MessageBox.Show("No platforms connected. Please connect to platforms first.", "No Platforms", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            var messageText = MessageInput;

            // Apply variable replacement for each platform
                if (_variableReplacementService != null)
            {
                // For send to all, we might need to handle platform-specific variables differently
                // For now, use a generic replacement or skip platform-specific
                    messageText = await _variableReplacementService!.ReplaceVariablesAsync(messageText, null);
            }

            var sendTasks = new List<Task>();
            var errors = new List<string>();

            // Track sent messages BEFORE sending to filter echoes from both platforms.
            // This ensures only the Vyral-branded message appears (no duplicate platform echoes).
            // Key uses message-only (no username) so it matches regardless of BotUsername being null/different
            var echoKey = messageText.Trim().ToLowerInvariant();
            lock (_recentMessagesLock)
            {
                if (_twitchService.IsConnected)
                {
                    _recentTwitchMessages[echoKey] = DateTime.Now;
                    _logger.LogInformation("🔒 Tracking SendToAll Twitch echo: {Key}", echoKey);
                }
                if (_kickService.IsConnected)
                {
                    _recentKickMessages[echoKey] = DateTime.Now;
                    _logger.LogInformation("🔒 Tracking SendToAll Kick echo: {Key}", echoKey);
                }
                if (_youtubeService.IsConnected)
                {
                    _recentYoutubeMessages[echoKey] = DateTime.Now;
                    _logger.LogInformation("🔒 Tracking SendToAll YouTube echo: {Key}", echoKey);
                }
                if (IsAnyDiscordConnected)
                {
                    _recentDiscordMessages[echoKey] = DateTime.Now;
                    _logger.LogInformation("🔒 Tracking SendToAll Discord echo: {Key}", echoKey);
                }
            }

            foreach (var platform in ConnectedPlatforms.Where(p => !p.IsAllPlatforms))
            {
                sendTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        switch (platform.Name)
                        {
                            case "Twitch":
                                if (_twitchService.IsConnected)
                                {
                                    await _twitchService.SendMessageAsync(messageText);
                                    _logger.LogInformation("Message sent to Twitch");
                                }
                                else
                                {
                                    _logger.LogWarning("Twitch is not connected, cannot send message");
                                    lock (errors)
                                    {
                                        errors.Add("Twitch: Not connected");
                                    }
                                }
                                break;

                            case "Kick":
                                if (_kickService.IsConnected)
                                {
                                    await _kickService.SendMessageAsync(messageText);
                                    _logger.LogInformation("Message sent to Kick");
                                }
                                else
                                {
                                    _logger.LogWarning("Kick is not connected, cannot send message");
                                    lock (errors)
                                    {
                                        errors.Add("Kick: Not connected");
                                    }
                                }
                                break;

                            case "YouTube":
                                if (_youtubeService.IsConnected)
                                {
                                    await _youtubeService.SendMessageAsync(messageText);
                                    _logger.LogInformation("Message sent to YouTube");
                                }
                                else
                                {
                                    _logger.LogWarning("YouTube is not connected, cannot send message");
                                    lock (errors)
                                    {
                                        errors.Add("YouTube: Not connected");
                                    }
                                }
                                break;

                            case "Discord":
                                // Broadcast send: no per-bot origin — Primary manager bot, else the setup bot
                                var discordAllSend = ResolveDiscordSendTarget(null);
                                if (discordAllSend != null && discordAllSend.IsConnected)
                                {
                                    await discordAllSend.SendMessageAsync(messageText);
                                    _logger.LogInformation("Message sent to Discord");
                                }
                                else
                                {
                                    _logger.LogWarning("Discord is not connected, cannot send message");
                                    lock (errors)
                                    {
                                        errors.Add("Discord: Not connected");
                                    }
                                }
                                break;

                            // TODO: Add TikTok, etc.
                            default:
                                _logger.LogWarning("Unknown platform: {Platform}", platform.Name);
                                lock (errors)
                                {
                                    errors.Add($"{platform.Name}: Not supported");
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending message to {Platform}", platform.Name);
                        lock (errors)
                        {
                            errors.Add($"{platform.Name}: {ex.Message}");
                        }
                    }
                }));
            }

            await Task.WhenAll(sendTasks);

            // Add sent message to chat with Vyral logo
            var sentMessage = new ChatMessage
            {
                Platform = "Vyral",
                Username = "Vyral",
                Message = messageText,
                Timestamp = DateTime.Now,
                Color = "#26A69A", // Vyral teal color
                IsModerator = false,
                IsSubscriber = false,
                IsVip = false,
                IsBroadcaster = true,
                Badges = new Dictionary<string, string> { { "Vyral", "Vyral" } }
            };
            sentMessage.InitializeBadges();
            Messages.Add(sentMessage);

            if (errors.Count > 0)
            {
                var errorMessage = "Some messages failed to send:\n" + string.Join("\n", errors);
                System.Windows.MessageBox.Show(errorMessage, "Partial Send Failure", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            else
            {
                _logger.LogInformation("Message sent to all platforms successfully");
            }

            MessageInput = "";
            _logger.LogInformation("Message input cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to all platforms");
            System.Windows.MessageBox.Show($"Error sending message: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private Dictionary<string, string> BuildContextVariables(string platformName)
    {
        var contextVars = new Dictionary<string, string>();

        // Platform-specific context
        switch (platformName)
        {
            case "Twitch":
                contextVars["bot-username"] = _twitchService.BotUsername ?? "Bot";
                contextVars["channel"] = _twitchService.Channel ?? "";
                break;

            case "Kick":
                contextVars["bot-username"] = _kickService.BotUsername ?? "Bot";
                contextVars["channel"] = _kickService.Channel ?? "";
                break;

            case "Discord":
                var discordContext = DiscordSendTarget; // keep context aligned with the send target
                contextVars["bot-username"] = discordContext?.BotUsername ?? "Bot";
                contextVars["channel"] = discordContext?.SelectedChannelId ?? "";
                break;

            // Add other platforms as they're implemented
            default:
                contextVars["bot-username"] = "Bot";
                contextVars["channel"] = "";
                break;
        }

        // Global context variables
        contextVars["platform"] = platformName;

        return contextVars;
    }

    [RelayCommand]
    private void TogglePlatformSelector()
    {
        IsPlatformSelectorOpen = !IsPlatformSelectorOpen;
    }

    [RelayCommand]
    private void SelectPlatform(PlatformOption platform)
    {
        SelectedPlatform = platform;
        IsPlatformSelectorOpen = false;
        _logger.LogInformation("Selected platform: {Platform}", platform.Name);
    }

    [RelayCommand]
    private void ToggleAllPlatformsSettings()
    {
        IsAllPlatformsSettingsOpen = !IsAllPlatformsSettingsOpen;
        if (IsAllPlatformsSettingsOpen)
        {
            _logger.LogInformation("All Platforms quick-settings opened");
        }
    }

    [RelayCommand]
    private void ToggleTwitchSettings()
    {
        IsTwitchSettingsOpen = !IsTwitchSettingsOpen;
        if (IsTwitchSettingsOpen)
        {
            IsKickSettingsOpen = false;
            IsYoutubeSettingsOpen = false;
            IsTiktokSettingsOpen = false;
            IsDiscordSettingsOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleKickSettings()
    {
        IsKickSettingsOpen = !IsKickSettingsOpen;
        if (IsKickSettingsOpen)
        {
            IsTwitchSettingsOpen = false;
            IsYoutubeSettingsOpen = false;
            IsTiktokSettingsOpen = false;
            IsDiscordSettingsOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleYoutubeSettings()
    {
        IsYoutubeSettingsOpen = !IsYoutubeSettingsOpen;
        if (IsYoutubeSettingsOpen)
        {
            IsTwitchSettingsOpen = false;
            IsKickSettingsOpen = false;
            IsTiktokSettingsOpen = false;
            IsDiscordSettingsOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleTiktokSettings()
    {
        IsTiktokSettingsOpen = !IsTiktokSettingsOpen;
        if (IsTiktokSettingsOpen)
        {
            IsTwitchSettingsOpen = false;
            IsKickSettingsOpen = false;
            IsYoutubeSettingsOpen = false;
            IsDiscordSettingsOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleDiscordSettings()
    {
        IsDiscordSettingsOpen = !IsDiscordSettingsOpen;
        if (IsDiscordSettingsOpen)
        {
            IsTwitchSettingsOpen = false;
            IsKickSettingsOpen = false;
            IsYoutubeSettingsOpen = false;
            IsTiktokSettingsOpen = false;
        }
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        _loadedMessageIds.Clear();
        _historyLoadedCount = 0;
        _logger.LogInformation("Chat cleared (history tracking reset)");
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        try
        {
            var confirmDialog = new Views.CustomDialog(
                "Clear History",
                "Are you sure you want to delete all chat history from the database? This cannot be undone.",
                Views.CustomDialogButtons.YesNo,
                Views.CustomDialogIcon.Warning)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (confirmDialog.ShowDialog() == true)
            {
                // Clear history for all connected platforms
                if (_kickService.IsConnected && !string.IsNullOrEmpty(_kickService.Channel))
                {
                    await _chatStore.DeleteAsync("Kick", _kickService.Channel);
                    _logger.LogInformation("🗑️ Deleted Kick chat history for channel: {Channel}", _kickService.Channel);
                }

                if (_twitchService.IsConnected && !string.IsNullOrEmpty(_twitchService.Channel))
                {
                    await _chatStore.DeleteAsync("Twitch", _twitchService.Channel);
                    _logger.LogInformation("🗑️ Deleted Twitch chat history for channel: {Channel}", _twitchService.Channel);
                }

                // Clear UI and reset tracking
                Messages.Clear();
                _loadedMessageIds.Clear();
                _historyLoadedCount = 0;

                _logger.LogInformation("✅ Chat history deleted from database");
                var successDialog = new Views.CustomDialog(
                    "History Cleared",
                    "Chat history has been deleted from the database.",
                    Views.CustomDialogButtons.OK,
                    Views.CustomDialogIcon.Success)
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                successDialog.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing chat history");
            var errorDialog = new Views.CustomDialog(
                "Error",
                $"Error clearing history: {ex.Message}",
                Views.CustomDialogButtons.OK,
                Views.CustomDialogIcon.Error)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            errorDialog.ShowDialog();
        }
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        AutoScroll = !AutoScroll;
    }

    private async Task OnYouTubeChatMessageReceivedAsync(YouTubeChatMessageEvent evt)
    {
        if (evt == null) return;

        _logger.LogInformation("📨 ChatViewModel received YouTube message from {Username}: {Message}", evt.Username, evt.Message);

        // Check if this is an echo of a message we just sent from the app.
        // Restricted to messages from the account the bot sends as (the
        // authorized channel) so a viewer typing identical text is not filtered.
        // The tracking entry is intentionally kept on match so the generic
        // command pipeline (subscribed to a separate event) also filters it.
        var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
        var isOwnYoutubeMessage = await IsOwnYouTubeAccountAsync(evt.DisplayName ?? evt.Username);
        lock (_recentMessagesLock)
        {
            if (_recentYoutubeMessages.TryGetValue(messageKey, out var sentTime))
            {
                var age = DateTime.Now - sentTime;
                if (age.TotalSeconds < 15 && isOwnYoutubeMessage) // YouTube polling can be slow, use wider echo window
                {
                    _logger.LogInformation("🔇 Ignoring echo of recently sent YouTube message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
                    return;
                }
                else if (age.TotalSeconds >= 15)
                {
                    _recentYoutubeMessages.Remove(messageKey);
                }
            }

            // Cleanup old entries (> 15 seconds)
            var expiredKeys = _recentYoutubeMessages
                .Where(kvp => (DateTime.Now - kvp.Value).TotalSeconds > 15)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in expiredKeys)
            {
                _recentYoutubeMessages.Remove(key);
            }
        }

        if (!YoutubeFilterEnabled)
        {
            _logger.LogDebug("YouTube messages filtered out (YoutubeFilterEnabled=false)");
            return;
        }

        // If platforms list is empty or doesn't include YouTube, refresh it
        if (ConnectedPlatforms.Count == 0 || !ConnectedPlatforms.Any(p => p.Name == "YouTube"))
        {
            _logger.LogInformation("YouTube not in platforms list, refreshing...");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateConnectedPlatforms());
        }

        // Add YouTube message to UI thread
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var chatMessage = new ChatMessage
            {
                Platform = "YouTube",
                Username = evt.DisplayName ?? evt.Username ?? "Unknown",
                Message = evt.Message ?? string.Empty,
                Timestamp = evt.Timestamp.ToLocalTime(),
                Color = "#FF0000",
                IsModerator = evt.IsModerator,
                IsSubscriber = evt.IsMember,
                IsVip = false,
                IsBroadcaster = evt.IsOwner,
                Badges = new Dictionary<string, string>()
            };

            // Add role badges
            if (evt.IsOwner) chatMessage.Badges["broadcaster"] = "1";
            if (evt.IsModerator) chatMessage.Badges["moderator"] = "1";
            if (evt.IsMember) chatMessage.Badges["subscriber"] = "1";

            chatMessage.InitializeBadges();
            Messages.Add(chatMessage);

            _logger.LogInformation("✅ YouTube message added to UI. Total messages: {Count}", Messages.Count);

            // Keep only last 500 messages
            while (Messages.Count > 500)
            {
                Messages.RemoveAt(0);
            }
        });
    }

    private async Task OnKickChatMessageReceivedAsync(KickChatMessageEvent evt)
    {
        // Defensive null checks
        if (evt == null) return;

        _logger.LogInformation("📨 ChatViewModel received Kick message from {Username}: {Message}", evt.Username, evt.Message);

        // Check if this is an echo of a message we just sent from the app.
        // Restricted to messages from the account the bot sends as (the Kick
        // broadcaster whose token KickLib uses) so a viewer typing identical
        // text is not filtered. The tracking entry is intentionally kept on
        // match so the generic command pipeline also filters it.
        var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
        var isOwnKickMessage = await IsOwnKickAccountAsync(evt.Username);
        lock (_recentMessagesLock)
        {
            if (_recentKickMessages.TryGetValue(messageKey, out var sentTime))
            {
                var age = DateTime.Now - sentTime;
                if (age.TotalSeconds < 10 && isOwnKickMessage) // Echo window: 10 seconds
                {
                    _logger.LogInformation("🔇 Ignoring echo of recently sent message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
                    return;
                }
                else if (age.TotalSeconds >= 10)
                {
                    // Expired tracking entry, clean it up
                    _recentKickMessages.Remove(messageKey);
                }
            }

            // Cleanup old entries (> 10 seconds)
            var expiredKeys = _recentKickMessages
                .Where(kvp => (DateTime.Now - kvp.Value).TotalSeconds > 10)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in expiredKeys)
            {
                _recentKickMessages.Remove(key);
            }
        }

        if (!KickFilterEnabled)
        {
            _logger.LogDebug("Kick messages filtered out (KickFilterEnabled=false)");
            return;
        }

        var source = !string.IsNullOrEmpty(evt.MessageId) && evt.MessageId.StartsWith("history_") ? "history" : "realtime";
        _logger.LogInformation("Kick message ({Source}) from {Username} ({DisplayName}): {Message}", source, evt.Username, evt.DisplayName, evt.Message);

        // If platforms list is empty or doesn't include Kick, refresh it
        if (ConnectedPlatforms.Count == 0 || !ConnectedPlatforms.Any(p => p.Name == "Kick"))
        {
            _logger.LogInformation("Kick not in platforms list, refreshing...");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateConnectedPlatforms());
        }

        // Add Kick message to UI thread
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var chatMessage = new ChatMessage
            {
                Platform = "Kick",
                Username = evt.DisplayName ?? evt.Username ?? "Unknown",
                Message = evt.Message ?? string.Empty,
                Timestamp = evt.Timestamp.ToLocalTime(),
                Color = evt.Color ?? "#53FC18",
                IsModerator = evt.IsModerator,
                IsSubscriber = evt.IsSubscriber,
                IsVip = false,
                IsBroadcaster = evt.IsOwner,
                Badges = evt.Badges ?? new Dictionary<string, string>()
            };

            chatMessage.InitializeBadges();

            _logger.LogInformation("✅ Kick message added to UI. Total messages: {Count}", Messages.Count + 1);

            Messages.Add(chatMessage);

            // Keep only last 500 messages
            while (Messages.Count > 500)
            {
                Messages.RemoveAt(0);
            }
        });
    }

    private async Task OnDiscordChatMessageReceivedAsync(DiscordChatMessageEvent evt)
    {
        // Defensive null checks
        if (evt == null) return;

        _logger.LogInformation("📨 ChatViewModel received Discord message from {Username}: {Message}", evt.Username, evt.Message);

        // Record the originating bot (tagged on the event by each DiscordService
        // instance) BEFORE any early-out below: the generic command pipeline
        // looks the origin up by MessageId to route replies back through the
        // same bot and to apply per-bot command filtering. Both events are
        // published concurrently, so record unconditionally.
        RememberDiscordBotOrigin(evt.MessageId, evt.BotId);

        // Skip messages from the bot itself (Username == BotUsername, case-insensitive).
        // The UI adds the bot's outgoing messages locally when sending, so the
        // gateway echo would only ever duplicate them.
        if (IsOwnDiscordAccount(evt.Username))
        {
            _logger.LogInformation("🔇 Ignoring message from the Discord bot account itself: {Message}", evt.Message);
            return;
        }

        // Echo-suppression note: sent messages are recorded in
        // _recentDiscordMessages by the send paths (SendMessageAsync /
        // SendToAllPlatformsAsync) and consumed by the account-scoped check in
        // IsEchoOfOwnRecentMessageAsync so the generic command pipeline never
        // executes the bot's outgoing text as a viewer command.

        if (!DiscordFilterEnabled)
        {
            _logger.LogDebug("Discord messages filtered out (DiscordFilterEnabled=false)");
            return;
        }

        // In-Discord "!" moderation commands (kick/ban/slowmode/purge/...):
        // moderators/owners type them in Discord and the bot replies in that
        // channel, routed back through the originating bot. The message still
        // displays and persists below like any other chat line. The generic
        // pipeline cannot be cancelled for this message — see the remarks on
        // TryHandleDiscordModCommandAsync for why its fallthrough is safe
        // (no seeded command collides, so no double reply).
        await TryHandleDiscordModCommandAsync(evt);

        // If platforms list is empty or doesn't include Discord, refresh it
        if (ConnectedPlatforms.Count == 0 || !ConnectedPlatforms.Any(p => p.Name == "Discord"))
        {
            _logger.LogInformation("Discord not in platforms list, refreshing...");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateConnectedPlatforms());
        }

        // Persist for chat history (channel name as the store key, like the other platforms)
        var channelName = !string.IsNullOrEmpty(evt.Channel) ? evt.Channel : (evt.ChannelId ?? "Unknown");
        try
        {
            await _chatStore.AddAsync("Discord", channelName, evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist Discord chat message from {Username}", evt.Username);
        }

        // Add Discord message to UI thread
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Synthesize badge keys from the event flags (Discord events carry
            // no badge dictionary, like YouTube)
            var badges = new Dictionary<string, string>();
            if (evt.IsBroadcaster) badges["broadcaster"] = "1";
            if (evt.IsModerator) badges["moderator"] = "1";

            var chatMessage = new ChatMessage
            {
                Platform = "Discord",
                Username = evt.Username ?? "Unknown",
                Message = evt.Message ?? string.Empty,
                Timestamp = evt.Timestamp.ToLocalTime(),
                Color = "#5865F2", // Discord Blurple default
                IsModerator = evt.IsModerator,
                IsSubscriber = false,
                IsVip = false,
                IsBroadcaster = evt.IsBroadcaster,
                Badges = badges
            };

            chatMessage.InitializeBadges();

            _logger.LogInformation("✅ Discord message added to UI. Total messages: {Count}", Messages.Count + 1);

            Messages.Add(chatMessage);

            // Keep only last 500 messages
            while (Messages.Count > 500)
            {
                Messages.RemoveAt(0);
            }
        });
    }

    // Kick Event Handlers
    private async Task OnKickFollowAsync(KickFollowEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "Follow");
        if (settings == null) return;

        var wasAutoReplied = false;

        if (settings.ShowInChat)
        {
            var message = $"🎉 {evt.Username} just followed! Total followers: {evt.FollowerCount}";
            // Display as system message for event notifications in chat
            await AddSystemMessageAsync("Kick", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("follow", "Kick");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Kick follow");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("New Follower!", evt.Username, $"Total followers: {evt.FollowerCount}", "Kick");

        // Award currency bonus for following
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.Username, "Kick", "follow");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Kick follow");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "platform", "Kick" }
        };

        // Auto-reply if enabled
        _logger.LogInformation("🔍 Checking auto-reply: Enabled={Enabled}, Message={Message}",
            settings.AutoReplyEnabled, settings.AutoReplyMessage);

        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            var shouldSend = _autoReplyService.ShouldSendReply(evt.Username, "Kick", "Follow");
            _logger.LogInformation("🔍 Rate limit check: ShouldSend={ShouldSend}", shouldSend);

            if (shouldSend)
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);
                _logger.LogInformation("📤 Attempting to send auto-reply: {Reply}", reply);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("✅ Auto-reply sent for Kick follow: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to send auto-reply for Kick follow");
                }
            }
            else
            {
                _logger.LogInformation("⏸️ Auto-reply blocked by rate limiter");
            }
        }
        else
        {
            _logger.LogInformation("⚙️ Auto-reply skipped: Enabled={Enabled}, HasMessage={HasMessage}",
                settings.AutoReplyEnabled, !string.IsNullOrEmpty(settings.AutoReplyMessage));
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Kick", "Follow", evt.Username, variables, wasAutoReplied);
    }

    private async Task OnKickSubscribeAsync(KickSubscribeEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "Subscribe");
        if (settings == null) return;

        var wasAutoReplied = false;

        if (settings.ShowInChat)
        {
            var monthsText = evt.MonthsSubscribed > 1 ? $" ({evt.MonthsSubscribed} months)" : "";
            var message = $"⭐ {evt.Username} subscribed{monthsText}!";
            if (!string.IsNullOrEmpty(evt.Message))
                message += $" - {evt.Message}";

            await AddSystemMessageAsync("Kick", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("subscribe", "Kick");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Kick subscribe");
            }
        }

        // Show visual alert
        var alertMessage = evt.MonthsSubscribed > 1 ? $"{evt.MonthsSubscribed} months!" : "Thanks for subscribing!";
        await ShowEventAlertAsync("New Subscriber!", evt.Username, alertMessage, "Kick");

        // Award currency bonus for subscribing
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.Username, "Kick", "subscribe");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Kick subscribe");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "months", evt.MonthsSubscribed.ToString() },
            { "message", evt.Message ?? "" },
            { "platform", "Kick" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "Subscribe"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick subscribe: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick subscribe");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Kick", "Subscribe", evt.Username, variables, wasAutoReplied);
    }

    private async Task OnKickGiftSubAsync(KickGiftSubEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "GiftSub");
        if (settings?.ShowInChat != true) return;

        var wasAutoReplied = false;

        var message = $"🎁 {evt.GifterUsername} gifted {evt.Count} subscription(s)!";
        if (evt.RecipientUsernames?.Any() == true)
            message += $" Recipients: {string.Join(", ", evt.RecipientUsernames)}";

        await AddSystemMessageAsync("Kick", message);

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("giftsub", "Kick");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Kick gift sub");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("Gift Subs!", evt.GifterUsername, $"{evt.Count} subscription(s) gifted!", "Kick");

        // Award currency bonus for gifting subs (multiplied by count)
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.GifterUsername, "Kick", "giftsub", evt.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Kick gift sub");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "gifter", evt.GifterUsername },
            { "count", evt.Count.ToString() },
            { "platform", "Kick" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.GifterUsername, "Kick", "GiftSub"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick gift sub: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick gift sub");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Kick", "GiftSub", evt.GifterUsername, variables, wasAutoReplied);
    }

    private async Task OnKickHostAsync(KickHostEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "Host");
        if (settings?.ShowInChat != true) return;

        var wasAutoReplied = false;

        var message = $"📺 {evt.HostUsername} is hosting with {evt.ViewerCount} viewers!";
        await AddSystemMessageAsync("Kick", message);

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("host", "Kick");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Kick host");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("New Host!", evt.HostUsername, $"{evt.ViewerCount} viewers!", "Kick");

        // Award currency bonus for hosting
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.HostUsername, "Kick", "host");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Kick host");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "hoster", evt.HostUsername },
            { "username", evt.HostUsername },
            { "viewers", evt.ViewerCount.ToString() },
            { "platform", "Kick" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.HostUsername, "Kick", "Host"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick host: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick host");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Kick", "Host", evt.HostUsername, variables, wasAutoReplied);
    }

    private async Task OnKickKicksGiftedAsync(KickKicksGiftedEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "KicksGifted");
        if (settings?.ShowInChat != true) return;

        var wasAutoReplied = false;

        var message = $"💰 {evt.SenderUsername} sent {evt.Amount} kicks!";
        if (!string.IsNullOrEmpty(evt.Message))
            message += $" - {evt.Message}";

        await AddSystemMessageAsync("Kick", message);

        // Show visual alert
        await ShowEventAlertAsync("Kicks Gifted!", evt.SenderUsername, $"{evt.Amount} kicks!", "Kick");

        var variables = new Dictionary<string, string>
        {
            { "sender", evt.SenderUsername },
            { "username", evt.SenderUsername },
            { "amount", evt.Amount.ToString() },
            { "message", evt.Message ?? "" },
            { "platform", "Kick" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.SenderUsername, "Kick", "KicksGifted"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick kicks gifted: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick kicks gifted");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Kick", "KicksGifted", evt.SenderUsername, variables, wasAutoReplied);
    }

    private async Task OnKickRewardRedeemedAsync(KickRewardRedeemedEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "RewardRedeemed");
        if (settings?.ShowInChat != true) return;

        var wasAutoReplied = false;

        var message = $"🎯 {evt.Username} redeemed \"{evt.RewardName}\" ({evt.Cost} points)";
        if (!string.IsNullOrEmpty(evt.UserInput))
            message += $" - {evt.UserInput}";

        await AddSystemMessageAsync("Kick", message);

        // Show visual alert
        await ShowEventAlertAsync("Reward Redeemed!", evt.Username, evt.RewardName, "Kick");

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "reward", evt.RewardName },
            { "cost", evt.Cost.ToString() },
            { "input", evt.UserInput ?? "" },
            { "platform", "Kick" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "RewardRedeemed"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick reward redeemed: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick reward redeemed");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Kick", "RewardRedeemed", evt.Username, variables, wasAutoReplied);
    }

    private async Task OnKickStreamStatusAsync(KickStreamStatusEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "StreamStatus");
        if (settings?.ShowInChat != true) return;

        var statusText = evt.IsLive ? "🔴 Stream is now LIVE" : "⚫ Stream ended";
        var message = $"{statusText}: {evt.Title}";
        if (!string.IsNullOrEmpty(evt.Category))
            message += $" ({evt.Category})";

        await AddSystemMessageAsync("Kick", message);

        // Persist the event for Statistics / Event History (KI-3) — no auto-reply exists for stream status
        var variables = new Dictionary<string, string>
        {
            { "islive", evt.IsLive.ToString() },
            { "title", evt.Title },
            { "category", evt.Category ?? "" },
            { "platform", "Kick" }
        };
        await SaveEventStatisticAsync("Kick", "StreamStatus", "System", variables, false);
    }

    // Twitch Event Handlers
    private async Task OnTwitchFollowAsync(TwitchFollowEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Follow");
        if (settings == null) return;

        var wasAutoReplied = false;

        if (settings.ShowInChat)
        {
            var message = $"🎉 {evt.DisplayName} just followed!";
            await AddSystemMessageAsync("Twitch", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("follow", "Twitch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Twitch follow");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("New Follower!", evt.DisplayName, null, "Twitch");

        // Award currency bonus for following
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.Username, "Twitch", "follow");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Twitch follow");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "displayname", evt.DisplayName },
            { "platform", "Twitch" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Follow"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch follow: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch follow");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Twitch", "Follow", evt.Username, variables, wasAutoReplied);
    }

    private async Task OnTwitchSubscribeAsync(TwitchSubscribeEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Subscribe");
        if (settings == null) return;

        var wasAutoReplied = false;

        var tierText = evt.Tier switch
        {
            "1000" => "Tier 1",
            "2000" => "Tier 2",
            "3000" => "Tier 3",
            _ => "Tier Unknown"
        };

        if (settings.ShowInChat)
        {
            var message = evt.IsGift
                ? $"⭐ {evt.DisplayName} received a {tierText} gift sub!"
                : $"⭐ {evt.DisplayName} subscribed at {tierText}!";
            await AddSystemMessageAsync("Twitch", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("subscribe", "Twitch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Twitch subscribe");
            }
        }

        // Show visual alert
        var alertTitle = evt.IsGift ? "Gift Sub!" : "New Subscriber!";
        await ShowEventAlertAsync(alertTitle, evt.DisplayName, tierText, "Twitch");

        // Award currency bonus for subscribing (not for gift recipients - gifter gets bonus)
        if (_currencyService != null && !evt.IsGift)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.Username, "Twitch", "subscribe");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Twitch subscribe");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "displayname", evt.DisplayName },
            { "tier", evt.Tier },
            { "platform", "Twitch" }
        };

        // Auto-reply if enabled (only for non-gift subs)
        if (!evt.IsGift && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Subscribe"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch subscribe: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch subscribe");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Twitch", "Subscribe", evt.Username, variables, wasAutoReplied);
    }

    private async Task OnTwitchResubAsync(TwitchResubEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Resub");
        if (settings == null) return;

        var wasAutoReplied = false;

        var tierText = evt.Tier switch
        {
            "1000" => "Tier 1",
            "2000" => "Tier 2",
            "3000" => "Tier 3",
            _ => "Tier Unknown"
        };

        if (settings.ShowInChat)
        {
            var message = $"⭐ {evt.Username} resubscribed at {tierText} ({evt.CumulativeMonths} months total)!";
            if (!string.IsNullOrEmpty(evt.Message))
                message += $" - {evt.Message}";
            await AddSystemMessageAsync("Twitch", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("resub", "Twitch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Twitch resub");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("Resubscribed!", evt.Username, $"{tierText} - {evt.CumulativeMonths} months!", "Twitch");

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "tier", evt.Tier },
            { "months", evt.CumulativeMonths.ToString() },
            { "streak", evt.StreakMonths.ToString() },
            { "platform", "Twitch" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Resub"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch resub: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch resub");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Twitch", "Resub", evt.Username, variables, wasAutoReplied);
    }

    private async Task OnTwitchGiftSubAsync(TwitchGiftSubEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "GiftSub");
        if (settings?.ShowInChat != true) return;

        var wasAutoReplied = false;

        var tierText = evt.Tier switch
        {
            "1000" => "Tier 1",
            "2000" => "Tier 2",
            "3000" => "Tier 3",
            _ => "Tier Unknown"
        };
        var message = $"🎁 {evt.GifterUsername} gifted {evt.Total} {tierText} sub{(evt.Total > 1 ? "s" : "")}!";
        await AddSystemMessageAsync("Twitch", message);

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("giftsub", "Twitch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Twitch gift sub");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("Gift Subs!", evt.GifterUsername, $"{evt.Total} {tierText} subs!", "Twitch");

        // Award currency bonus for gifting subs (multiplied by count)
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.GifterUsername, "Twitch", "giftsub", evt.Total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Twitch gift sub");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "gifter", evt.GifterUsername },
            { "username", evt.GifterUsername },
            { "count", evt.Total.ToString() },
            { "tier", evt.Tier },
            { "platform", "Twitch" }
        };

        // Auto-reply if enabled (matches the sibling handlers; rate-limited)
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.GifterUsername, "Twitch", "GiftSub"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch gift sub: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch gift sub");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Twitch", "GiftSub", evt.GifterUsername, variables, wasAutoReplied);
    }

    private async Task OnTwitchRaidAsync(TwitchRaidEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Raid");
        if (settings == null) return;

        var wasAutoReplied = false;

        if (settings.ShowInChat)
        {
            var message = $"🚀 {evt.FromBroadcasterUsername} raided with {evt.Viewers} viewer{(evt.Viewers != 1 ? "s" : "")}!";
            await AddSystemMessageAsync("Twitch", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("raid", "Twitch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Twitch raid");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("Raid!", evt.FromBroadcasterUsername, $"{evt.Viewers} viewers!", "Twitch");

        // Award currency bonus for raiding (multiplied by viewer count)
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.FromBroadcasterUsername, "Twitch", "raid", evt.Viewers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Twitch raid");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "username", evt.FromBroadcasterUsername },
            { "raider", evt.FromBroadcasterUsername }, // seeded/default templates use {raider}
            { "viewers", evt.Viewers.ToString() },
            { "platform", "Twitch" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.FromBroadcasterUsername, "Twitch", "Raid"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch raid: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch raid");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Twitch", "Raid", evt.FromBroadcasterUsername, variables, wasAutoReplied);
    }

    private async Task OnTwitchCheerAsync(TwitchCheerEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Cheer");
        if (settings == null) return;

        var wasAutoReplied = false;

        if (settings.ShowInChat)
        {
            var message = $"💎 {evt.Username} cheered {evt.Bits} bits!";
            if (!string.IsNullOrEmpty(evt.Message))
                message += $" - {evt.Message}";
            await AddSystemMessageAsync("Twitch", message);
        }

        // Play sound
        if (_soundService != null)
        {
            try
            {
                await _soundService.PlayEventSoundAsync("cheer", "Twitch");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound for Twitch cheer");
            }
        }

        // Show visual alert
        await ShowEventAlertAsync("Bits!", evt.Username, $"{evt.Bits} bits!", "Twitch");

        // Award currency bonus for cheering (multiplied by bit count)
        if (_currencyService != null)
        {
            try
            {
                await _currencyService.AwardEventBonusAsync(evt.Username, "Twitch", "cheer", evt.Bits);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to award currency bonus for Twitch cheer");
            }
        }

        var variables = new Dictionary<string, string>
        {
            { "username", evt.Username },
            { "bits", evt.Bits.ToString() },
            { "platform", "Twitch" }
        };

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Cheer"))
            {
                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch cheer: {Reply}", reply);
                    wasAutoReplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch cheer");
                }
            }
        }

        // Persist the event for Statistics / Event History (KI-3)
        await SaveEventStatisticAsync("Twitch", "Cheer", evt.Username, variables, wasAutoReplied);
    }

    private async Task AddSystemMessageAsync(string platform, string messageText)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var chatMessage = new ChatMessage
            {
                Platform = platform,
                Username = "System",
                Message = messageText,
                Timestamp = DateTime.Now,
                Color = "#FFD700", // Gold for system messages
                IsSystemMessage = true,
                IsModerator = false,
                IsSubscriber = false,
                IsVip = false,
                IsBroadcaster = false,
                Badges = new Dictionary<string, string>()
            };

            Messages.Add(chatMessage);

            // Keep only last 500 messages
            while (Messages.Count > 500)
            {
                Messages.RemoveAt(0);
            }
        });
    }

    public void Cleanup()
    {
        _logger.LogInformation("🧹 ChatViewModel.Cleanup() called - unsubscribing all events and resetting state");

        if (_twitchSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchSubscriptionId);
        }
        if (_kickSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickSubscriptionId);
        }
        if (_youtubeSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_youtubeSubscriptionId);
        }
        if (_discordSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_discordSubscriptionId);
        }
        if (_genericSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_genericSubscriptionId);
        }
        if (_routingSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_routingSubscriptionId);
        }
        if (_connectionSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_connectionSubscriptionId);
        }
        if (_kickFollowSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickFollowSubscriptionId);
        }
        if (_kickSubscribeSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickSubscribeSubscriptionId);
        }
        if (_kickGiftSubSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickGiftSubSubscriptionId);
        }
        if (_kickHostSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickHostSubscriptionId);
        }
        if (_kickKicksGiftedSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickKicksGiftedSubscriptionId);
        }
        if (_kickRewardRedeemedSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickRewardRedeemedSubscriptionId);
        }
        if (_kickStreamStatusSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_kickStreamStatusSubscriptionId);
        }
        if (_twitchFollowSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchFollowSubscriptionId);
        }
        if (_twitchSubscribeSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchSubscribeSubscriptionId);
        }
        if (_twitchResubSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchResubSubscriptionId);
        }
        if (_twitchGiftSubSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchGiftSubSubscriptionId);
        }
        if (_twitchRaidSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchRaidSubscriptionId);
        }
        if (_twitchCheerSubscriptionId != Guid.Empty)
        {
            _eventBus.Unsubscribe(_twitchCheerSubscriptionId);
        }

        // CRITICAL: Reset all subscription IDs so they can be re-created
        _twitchSubscriptionId = Guid.Empty;
        _kickSubscriptionId = Guid.Empty;
        _youtubeSubscriptionId = Guid.Empty;
        _discordSubscriptionId = Guid.Empty;
        _genericSubscriptionId = Guid.Empty;
        _routingSubscriptionId = Guid.Empty;
        _connectionSubscriptionId = Guid.Empty;
        _kickFollowSubscriptionId = Guid.Empty;
        _kickSubscribeSubscriptionId = Guid.Empty;
        _kickGiftSubSubscriptionId = Guid.Empty;
        _kickHostSubscriptionId = Guid.Empty;
        _kickKicksGiftedSubscriptionId = Guid.Empty;
        _kickRewardRedeemedSubscriptionId = Guid.Empty;
        _kickStreamStatusSubscriptionId = Guid.Empty;
        _twitchFollowSubscriptionId = Guid.Empty;
        _twitchSubscribeSubscriptionId = Guid.Empty;
        _twitchResubSubscriptionId = Guid.Empty;
        _twitchGiftSubSubscriptionId = Guid.Empty;
        _twitchRaidSubscriptionId = Guid.Empty;
        _twitchCheerSubscriptionId = Guid.Empty;

        // CRITICAL: Reset _isInitialized so InitializeAsync() will re-subscribe on next call
        // Without this, the singleton ChatViewModel permanently loses all event subscriptions after profile switch
        _isInitialized = false;

        // Clear loaded message tracking
        _loadedMessageIds.Clear();
        _historyLoadedCount = 0;

        // Clear per-profile caches (dedupe, follower lookups, cached platform rows)
        lock (_processedMessageIdsLock)
        {
            _processedMessageIds.Clear();
            _processedMessageIdOrder.Clear();
        }
        lock (_followerCacheLock)
        {
            _followerCache.Clear();
        }
        lock (_platformRowsLock)
        {
            _twitchPlatformRow = null;
            _kickPlatformRow = null;
            _youtubePlatformRow = null;
            _platformRowsLoadedAt = DateTime.MinValue;
        }

        // Clear messages on UI thread
        try
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() => Messages.Clear());
        }
        catch { /* App may be shutting down */ }

        _logger.LogInformation("✅ ChatViewModel cleanup complete - ready for re-initialization");
    }

    /// <summary>
    /// Shows a visual alert for an event if alerts are enabled
    /// </summary>
    private async Task ShowEventAlertAsync(string title, string username, string? message = null, string platform = "")
    {
        if (_alertService == null) return;

        try
        {
            // Default to TopCenter with SlideInFromTop animation
            await _alertService.ShowAlertAsync(
                title: title,
                username: username,
                message: message,
                imageUrl: null,
                animation: UI.Views.AlertAnimation.SlideInFromTop,
                position: UI.Views.AlertPosition.TopCenter,
                durationSeconds: 5);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show alert for {Title}", title);
        }
    }
}

public class BadgeInfo
{
    public string Name { get; set; } = "";
    public System.Windows.Media.Imaging.BitmapImage Image { get; set; } = null!;
}

public class ChatMessage : System.ComponentModel.INotifyPropertyChanged
{
    // Static cache of pre-loaded badge images shared by all messages
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.Imaging.BitmapImage> _badgeImageCache = new();
    internal static Microsoft.Extensions.Logging.ILogger? _logger;

    public string Platform { get; set; } = "";
    public string Username { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public bool IsModerator { get; set; }
    public bool IsSubscriber { get; set; }
    public bool IsVip { get; set; }
    public bool IsBroadcaster { get; set; }
    public bool IsSystemMessage { get; set; } = false;

    private Dictionary<string, string> _badges = new();
    public Dictionary<string, string> Badges
    {
        get => _badges;
        set
        {
            _logger?.LogInformation("[Badges setter] Setting {Count} badges: {BadgeList}", value.Count, string.Join(", ", value.Select(kvp => kvp.Key)));
            _badges = value;
            // Compute badge images immediately when badges are set
            _badgeImages = null; // Clear cache
            BadgeImages = ComputeBadgeImages(); // Compute immediately
            _logger?.LogInformation("[Badges setter] Computed {Count} badge images", BadgeImages.Count);
        }
    }

    // Badge images - computed eagerly when Badges property is set
    private List<BadgeInfo>? _badgeImages;
    public List<BadgeInfo> BadgeImages
    {
        get => _badgeImages ?? new List<BadgeInfo>();
        private set
        {
            _badgeImages = value;
            _logger?.LogInformation("[BadgeImages setter] Setting {Count} images. First image details: Width={Width}, Height={Height}",
                value.Count, value.Count > 0 ? value[0].Image.PixelWidth : 0, value.Count > 0 ? value[0].Image.PixelHeight : 0);
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BadgeImages)));
            _logger?.LogInformation("[BadgeImages setter] PropertyChanged fired for BadgeImages");
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    // Call this after object initialization to compute badge images
    public void InitializeBadges()
    {
        _logger?.LogInformation("[InitializeBadges] Called with {Count} badges, _badgeImages is {State}", _badges.Count, _badgeImages == null ? "null" : "not null");
        if (_badges.Count > 0)
        {
            _logger?.LogInformation("[InitializeBadges] Badges: {Badges}", string.Join(", ", _badges.Select(b => $"{b.Key}={b.Value}")));
        }

        if (_badgeImages == null && _badges.Count > 0)
        {
            _logger?.LogInformation("[InitializeBadges] Computing badge images...");
            BadgeImages = ComputeBadgeImages();
            _logger?.LogInformation("[InitializeBadges] Computed {Count} images", BadgeImages.Count);
        }
    }

    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    static ChatMessage()
    {
        // Add headers to avoid 403 Forbidden from CDNs
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://kick.com/");
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://kick.com");
    }

    private static System.Windows.Media.Imaging.BitmapImage LoadBadgeImage(string url)
    {
        return _badgeImageCache.GetOrAdd(url, uri =>
        {
            try
            {
                byte[] imageBytes;

                // Handle data URIs (base64 embedded images)
                if (uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("[LoadBadgeImage] Loading embedded data URI");
                    var base64Data = uri.Substring(uri.IndexOf(",") + 1);
                    imageBytes = Convert.FromBase64String(base64Data);
                    _logger?.LogInformation("[LoadBadgeImage] Decoded {Size} bytes from base64", imageBytes.Length);
                }
                else
                {
                    // Download from HTTP/HTTPS
                    _logger?.LogInformation("[LoadBadgeImage] Downloading: {Uri}", uri);
                    imageBytes = _httpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                    _logger?.LogInformation("[LoadBadgeImage] Downloaded {Size} bytes", imageBytes.Length);
                }

                // Create BitmapImage from byte array - let WPF handle sizing via XAML Width/Height
                var stream = new System.IO.MemoryStream(imageBytes);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                // Don't set DecodePixelWidth/Height - let XAML Width/Height control the size
                bitmap.EndInit();
                stream.Dispose();
                bitmap.Freeze();
                _logger?.LogInformation("[LoadBadgeImage] ✅ Successfully created BitmapImage: Width={Width}, Height={Height}, PixelWidth={PixelWidth}, PixelHeight={PixelHeight}, IsFrozen={IsFrozen}",
                    bitmap.Width, bitmap.Height, bitmap.PixelWidth, bitmap.PixelHeight, bitmap.IsFrozen);

                return bitmap;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[LoadBadgeImage] ❌ ERROR loading badge from {Uri}", uri);
                // Return null on failure - will be skipped in ComputeBadgeImages
                return null!;
            }
        });
    }

    private List<BadgeInfo> ComputeBadgeImages()
    {
        _logger?.LogInformation("[ComputeBadgeImages] Starting with {Count} badges", Badges.Count);
        var badgeInfos = new List<BadgeInfo>();

        foreach (var badge in Badges.OrderBy(b => GetBadgePriority(b.Key)))
        {
            _logger?.LogInformation("[ComputeBadgeImages] Processing badge: {Key}={Value}", badge.Key, badge.Value);
            string? url = null;
            string badgeName = badge.Key;

            // Case-insensitive badge matching - use Twitch badge URLs for both platforms
            var badgeType = badge.Key.ToLowerInvariant();

            switch (badgeType)
            {
                case "broadcaster":
                    url = "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1";
                    badgeName = "Broadcaster";
                    break;
                case "moderator":
                    url = "https://static-cdn.jtvnw.net/badges/v1/3267646d-33f0-4b17-b3df-f923a41db1d0/1";
                    badgeName = "Moderator";
                    break;
                case "vip":
                    url = "https://static-cdn.jtvnw.net/badges/v1/b817aba4-fad8-49e2-b88a-7cc744dfa6ec/1";
                    badgeName = "VIP";
                    break;
                case "subscriber":
                    url = "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/1";
                    badgeName = "Subscriber";
                    break;
                case "verified":
                    // Twitch doesn't have verified badge, use a custom one
                    url = "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/1";
                    badgeName = "Verified";
                    break;
            }

            if (url != null)
            {
                _logger?.LogInformation("[ComputeBadgeImages] Loading image from: {Url}", url);
                var bitmap = LoadBadgeImage(url);
                if (bitmap != null)
                {
                    _logger?.LogInformation("[ComputeBadgeImages] ✅ Image loaded successfully");
                    badgeInfos.Add(new BadgeInfo { Name = badgeName, Image = bitmap });
                }
                else
                {
                    _logger?.LogInformation("[ComputeBadgeImages] ❌ Image load returned null");
                }
            }
            else
            {
                _logger?.LogInformation("[ComputeBadgeImages] ⚠️ No URL mapping for badge: {Badge}", badge.Key);
            }
        }

        _logger?.LogInformation("[ComputeBadgeImages] Returning {Count} images", badgeInfos.Count);
        return badgeInfos;
    }

    private static int GetBadgePriority(string badgeType)
    {
        return badgeType.ToLowerInvariant() switch
        {
            "broadcaster" => 0,
            "moderator" => 1,
            "vip" => 2,
            "subscriber" => 3,
            "verified" => 4,
            _ => 99
        };
    }
}

public class PlatformOption
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public bool IsAllPlatforms { get; set; } = false;
}

// NOTE: ChatMessageReceivedEvent now lives in VyralBot.Core.Models
// (src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) so the chat
// integrations can publish it and the command pipeline can consume it.
