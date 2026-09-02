using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using VyralBot.Core.Services.EventBus;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.YouTube;
using VyralBot.Integrations.YouTube.Events;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Audio;

namespace VyralBot.UI.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private readonly ILogger<ChatViewModel> _logger;
    private readonly IEventBus _eventBus;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IYouTubeService _youtubeService;
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
    private bool _isInitialized = false;

    // Track recently sent Kick messages to filter out WebSocket echoes
    private readonly Dictionary<string, DateTime> _recentKickMessages = new();
    // Track recently sent Twitch messages to filter out IRC echoes
    private readonly Dictionary<string, DateTime> _recentTwitchMessages = new();
    // Track recently sent YouTube messages to filter out poll echoes
    private readonly Dictionary<string, DateTime> _recentYoutubeMessages = new();
    private readonly object _recentMessagesLock = new();

    // Track loaded message IDs to prevent duplicates when loading history
    private readonly HashSet<string> _loadedMessageIds = new();
    private int _historyLoadedCount = 0;
    private const int MAX_HISTORY_MESSAGES = 200;

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
        KickSettingsViewModel kickSettings,
        VyralBot.Core.Services.Chat.IChatMessageStore chatStore,
        IChatEventSettingsService eventSettingsService,
        IAutoReplyService autoReplyService,
        VyralBot.Core.Services.Currency.ICurrencyService? currencyService = null,
        VyralBot.Core.Services.Currency.ICurrencyCommandService? currencyCommandService = null,
        ICommandService? commandService = null,
        ISoundService? soundService = null,
        UI.Services.IAlertService? alertService = null,
        VyralBot.Core.Services.GiveawayCommandService? giveawayCommandService = null,
        VyralBot.Core.Services.PollCommandService? pollCommandService = null,
        VyralBot.Core.Services.WatchtimeService? watchtimeService = null,
        VyralBot.Core.Services.WatchtimeCommandService? watchtimeCommandService = null,
        VyralBot.Core.Services.Variables.IVariableReplacementService? variableReplacementService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _youtubeService = youtubeService ?? throw new ArgumentNullException(nameof(youtubeService));
        _kickSettings = kickSettings ?? throw new ArgumentNullException(nameof(kickSettings));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _eventSettingsService = eventSettingsService ?? throw new ArgumentNullException(nameof(eventSettingsService));
        _autoReplyService = autoReplyService ?? throw new ArgumentNullException(nameof(autoReplyService));
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
                    _logger.LogInformation("✅ Added {Added} history messages ({Kick} Kick, {Twitch} Twitch). Total: {Total}/{Max}",
                        allHistoryMessages.Count,
                        allHistoryMessages.Count(m => m.Platform == "Kick"),
                        allHistoryMessages.Count(m => m.Platform == "Twitch"),
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

    public void UpdateConnectedPlatforms()
    {
        ConnectedPlatforms.Clear();

        _logger.LogInformation("Updating connected platforms. Twitch connected: {IsConnected}, Channel: {Channel}",
            _twitchService.IsConnected, _twitchService.Channel ?? "(none)");

        // Add "All Platforms" option first if any platform is connected
        bool hasAnyConnection = _twitchService.IsConnected || _kickService.IsConnected || _youtubeService.IsConnected;
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

        // TODO: Add YouTube, TikTok, Discord when implemented
        // For now set their connected flags to false (will be set when services exist)
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

        IsTiktokConnected = false;
        IsDiscordConnected = false;

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

            // Check if this is an echo of a message we just sent from the app
            // (replaces the old bot-username filter which blocked ALL messages from the user's own account,
            //  including ones typed on the Twitch website)
            // Key uses message-only (no username) so it matches regardless of BotUsername being null/different
            var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
            lock (_recentMessagesLock)
            {
                if (_recentTwitchMessages.TryGetValue(messageKey, out var sentTime))
                {
                    var age = DateTime.Now - sentTime;
                    if (age.TotalSeconds < 10) // Echo window: 10 seconds
                    {
                        _logger.LogInformation("🔇 Ignoring echo of recently sent Twitch message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
                        _recentTwitchMessages.Remove(messageKey);
                        return;
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
        // Filter by platform-specific enable flags
        if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase) && !TwitchFilterEnabled) return;
        if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase) && !KickFilterEnabled) return;
        if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase) && !YoutubeFilterEnabled) return;
        if (string.Equals(evt.Platform, "TikTok", StringComparison.OrdinalIgnoreCase) && !TiktokFilterEnabled) return;
        if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase) && !DiscordFilterEnabled) return;

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
                    Channel = evt.Platform == "Twitch" ? _twitchService.Channel ?? "" : _kickSettings.Username ?? "",
                    IsModerator = evt.IsModerator,
                    IsSubscriber = evt.IsSubscriber,
                    IsVIP = evt.IsVip,
                    IsBroadcaster = false, // TODO: Detect broadcaster from event
                    CurrencyBalance = currencyBalance ?? 0,
                    CustomVariables = new Dictionary<string, string>()
                };

                // Execute command
                var response = await _commandService.ExecuteAsync(evt.Message, context);

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

                    _logger.LogInformation("Processed currency command: {Command} -> {Response}", evt.Message, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing currency command: {Command}", evt.Message);
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
                    false, // isFollower - can be tracked in future
                    0); // watchtimeMinutes - can be tracked in future

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
                    false, // isFollower - can be tracked in future
                    0); // watchtimeMinutes - can be tracked in future

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

        // Add message to UI thread
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var message = new ChatMessage
            {
                Platform = evt.Platform,
                Username = evt.Username,
                Message = evt.Message ?? string.Empty,
                Timestamp = evt.Timestamp,
                Color = evt.Color ?? "#FFFFFF",
                IsModerator = evt.IsModerator,
                IsSubscriber = evt.IsSubscriber,
                IsVip = evt.IsVip
            };
            message.InitializeBadges();
            Messages.Add(message);

            // Keep only last 500 messages
            while (Messages.Count > 500)
            {
                Messages.RemoveAt(0);
            }
        });
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

                // TODO: Add TikTok, Discord, etc.
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

                            // TODO: Add TikTok, Discord, etc.
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

        // Check if this is an echo of a message we just sent from the app
        var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
        lock (_recentMessagesLock)
        {
            if (_recentYoutubeMessages.TryGetValue(messageKey, out var sentTime))
            {
                var age = DateTime.Now - sentTime;
                if (age.TotalSeconds < 15) // YouTube polling can be slow, use wider echo window
                {
                    _logger.LogInformation("🔇 Ignoring echo of recently sent YouTube message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
                    _recentYoutubeMessages.Remove(messageKey);
                    return;
                }
                else
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

        // Check if this is an echo of a message we just sent from the app
        // (replaces the old bot-username filter which blocked ALL messages from the user's own account,
        //  including ones typed on the Kick website)
        // Key uses message-only (no username) so it matches regardless of BotUsername being null/different
        var messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
        lock (_recentMessagesLock)
        {
            if (_recentKickMessages.TryGetValue(messageKey, out var sentTime))
            {
                var age = DateTime.Now - sentTime;
                if (age.TotalSeconds < 10) // Echo window: 10 seconds
                {
                    _logger.LogInformation("🔇 Ignoring echo of recently sent message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
                    _recentKickMessages.Remove(messageKey);
                    return;
                }
                else
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

    // Kick Event Handlers
    private async Task OnKickFollowAsync(KickFollowEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "Follow");
        if (settings == null) return;

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

        // Auto-reply if enabled
        _logger.LogInformation("🔍 Checking auto-reply: Enabled={Enabled}, Message={Message}",
            settings.AutoReplyEnabled, settings.AutoReplyMessage);

        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            var shouldSend = _autoReplyService.ShouldSendReply(evt.Username, "Kick", "Follow");
            _logger.LogInformation("🔍 Rate limit check: ShouldSend={ShouldSend}", shouldSend);

            if (shouldSend)
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "platform", "Kick" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);
                _logger.LogInformation("📤 Attempting to send auto-reply: {Reply}", reply);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("✅ Auto-reply sent for Kick follow: {Reply}", reply);
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
    }

    private async Task OnKickSubscribeAsync(KickSubscribeEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "Subscribe");
        if (settings == null) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "Subscribe"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "months", evt.MonthsSubscribed.ToString() },
                    { "message", evt.Message ?? "" },
                    { "platform", "Kick" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick subscribe: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick subscribe");
                }
            }
        }
    }

    private async Task OnKickGiftSubAsync(KickGiftSubEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "GiftSub");
        if (settings?.ShowInChat != true) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.GifterUsername, "Kick", "GiftSub"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "gifter", evt.GifterUsername },
                    { "count", evt.Count.ToString() },
                    { "platform", "Kick" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick gift sub: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick gift sub");
                }
            }
        }
    }

    private async Task OnKickHostAsync(KickHostEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "Host");
        if (settings?.ShowInChat != true) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.HostUsername, "Kick", "Host"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "hoster", evt.HostUsername },
                    { "username", evt.HostUsername },
                    { "viewers", evt.ViewerCount.ToString() },
                    { "platform", "Kick" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick host: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick host");
                }
            }
        }
    }

    private async Task OnKickKicksGiftedAsync(KickKicksGiftedEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "KicksGifted");
        if (settings?.ShowInChat != true) return;

        var message = $"💰 {evt.SenderUsername} sent {evt.Amount} kicks!";
        if (!string.IsNullOrEmpty(evt.Message))
            message += $" - {evt.Message}";

        await AddSystemMessageAsync("Kick", message);

        // Show visual alert
        await ShowEventAlertAsync("Kicks Gifted!", evt.SenderUsername, $"{evt.Amount} kicks!", "Kick");

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.SenderUsername, "Kick", "KicksGifted"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "sender", evt.SenderUsername },
                    { "username", evt.SenderUsername },
                    { "amount", evt.Amount.ToString() },
                    { "message", evt.Message ?? "" },
                    { "platform", "Kick" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick kicks gifted: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick kicks gifted");
                }
            }
        }
    }

    private async Task OnKickRewardRedeemedAsync(KickRewardRedeemedEvent evt)
    {
        if (!KickFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Kick", "RewardRedeemed");
        if (settings?.ShowInChat != true) return;

        var message = $"🎯 {evt.Username} redeemed \"{evt.RewardName}\" ({evt.Cost} points)";
        if (!string.IsNullOrEmpty(evt.UserInput))
            message += $" - {evt.UserInput}";

        await AddSystemMessageAsync("Kick", message);

        // Show visual alert
        await ShowEventAlertAsync("Reward Redeemed!", evt.Username, evt.RewardName, "Kick");

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "RewardRedeemed"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "reward", evt.RewardName },
                    { "cost", evt.Cost.ToString() },
                    { "input", evt.UserInput ?? "" },
                    { "platform", "Kick" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _kickService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Kick reward redeemed: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Kick reward redeemed");
                }
            }
        }
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
    }

    // Twitch Event Handlers
    private async Task OnTwitchFollowAsync(TwitchFollowEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Follow");
        if (settings == null) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Follow"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "displayname", evt.DisplayName },
                    { "platform", "Twitch" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch follow: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch follow");
                }
            }
        }
    }

    private async Task OnTwitchSubscribeAsync(TwitchSubscribeEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Subscribe");
        if (settings == null) return;

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

        // Auto-reply if enabled (only for non-gift subs)
        if (!evt.IsGift && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Subscribe"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "displayname", evt.DisplayName },
                    { "tier", evt.Tier },
                    { "platform", "Twitch" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch subscribe: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch subscribe");
                }
            }
        }
    }

    private async Task OnTwitchResubAsync(TwitchResubEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Resub");
        if (settings == null) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Resub"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "tier", evt.Tier },
                    { "months", evt.CumulativeMonths.ToString() },
                    { "streak", evt.StreakMonths.ToString() },
                    { "platform", "Twitch" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch resub: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch resub");
                }
            }
        }
    }

    private async Task OnTwitchGiftSubAsync(TwitchGiftSubEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "GiftSub");
        if (settings?.ShowInChat != true) return;

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
    }

    private async Task OnTwitchRaidAsync(TwitchRaidEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Raid");
        if (settings == null) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.FromBroadcasterUsername, "Twitch", "Raid"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.FromBroadcasterUsername },
                    { "viewers", evt.Viewers.ToString() },
                    { "platform", "Twitch" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch raid: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch raid");
                }
            }
        }
    }

    private async Task OnTwitchCheerAsync(TwitchCheerEvent evt)
    {
        if (!TwitchFilterEnabled) return;

        var settings = await _eventSettingsService.GetSettingAsync("Twitch", "Cheer");
        if (settings == null) return;

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

        // Auto-reply if enabled
        if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
        {
            if (_autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Cheer"))
            {
                var variables = new Dictionary<string, string>
                {
                    { "username", evt.Username },
                    { "bits", evt.Bits.ToString() },
                    { "platform", "Twitch" }
                };

                var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);

                try
                {
                    await _twitchService.SendMessageAsync(reply);
                    _logger.LogInformation("Auto-reply sent for Twitch cheer: {Reply}", reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send auto-reply for Twitch cheer");
                }
            }
        }
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

// Event for chat messages
public record ChatMessageReceivedEvent : EventBase
{
    public string Platform { get; init; } = "";
    public string Username { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Color { get; init; }
    public bool IsModerator { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsVip { get; init; }
}
