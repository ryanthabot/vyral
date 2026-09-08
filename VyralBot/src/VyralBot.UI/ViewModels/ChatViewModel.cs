using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models;
using VyralBot.Core.Services;
using VyralBot.Core.Services.Audio;
using VyralBot.Core.Services.Channels;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.EventBus;
using VyralBot.Core.Services.EventBus.Events;
using VyralBot.Core.Services.Linking;
using VyralBot.Core.Services.StreamEffects;
using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Discord.Events;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Kick.Events;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Twitch.Events;
using VyralBot.Integrations.YouTube;
using VyralBot.Integrations.YouTube.Events;
using VyralBot.UI.Services;
using VyralBot.UI.Views;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.ViewModels;

public class ChatViewModel : ViewModelBase
{
	private readonly ILogger<ChatViewModel> _logger;

	private readonly IEventBus _eventBus;

	private readonly ITwitchService _twitchService;

	private readonly IKickService _kickService;

	private readonly IYouTubeService _youtubeService;

	private readonly IDiscordService _discordService;

	private readonly IDiscordBotManager _discordBotManager;

	private readonly IDiscordCommandHandler? _discordCommandHandler;

	private readonly StreamEffectCommandRouter? _streamEffectRouter;

	private readonly IConnectedChannelService? _connectedChannels;

	private readonly KickSettingsViewModel _kickSettings;

	private readonly IChatMessageStore _chatStore;

	private readonly IChatEventSettingsService _eventSettingsService;

	private readonly IAutoReplyService _autoReplyService;

	private readonly ICurrencyService? _currencyService;

	private readonly ICurrencyCommandService? _currencyCommandService;

	private readonly ICommandService? _commandService;

	private readonly ISoundService? _soundService;

	private readonly IAlertService? _alertService;

	private readonly GiveawayCommandService? _giveawayCommandService;

	private readonly PollCommandService? _pollCommandService;

	private readonly WatchtimeService? _watchtimeService;

	private readonly WatchtimeCommandService? _watchtimeCommandService;

	private readonly IVariableReplacementService? _variableReplacementService;

	private readonly IDataService? _dataService;

	private readonly CommandActionRunner? _commandActionRunner;

	private readonly WorkflowTriggerService? _workflowTriggerService;

	private readonly ILinkCodeService? _linkCodeService;

	private readonly IViewerStatusService? _viewerStatusService;

	private readonly ISocialGoalService? _socialGoalService;

	private string? _linkingBotIdCache;

	private DateTime _linkingBotIdCacheAtUtc = DateTime.MinValue;

	private static readonly TimeSpan LinkingBotIdCacheTtl = TimeSpan.FromSeconds(30L);

	private ViewerProfileWindow? _openViewerProfileWindow;

	private string? _openViewerProfileKey;

	private bool _viewerProfileWindowOpening;

	private readonly IResponseWaiterRegistry? _responseWaiters;

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

	private Guid _twitchStreamStatusSubscriptionId;

	private Guid _giveawayAutoEndedSubscriptionId;

	private Guid _pollAutoEndedSubscriptionId;

	private Guid _youTubeMembershipStatusSubscriptionId;

	private Guid _accountsLinkedSubscriptionId;

	private Guid _viewerGoalCompletedSubscriptionId;

	private Guid _giveawayIntakeModeSubscriptionId;

	private Guid _youtubeSubscriptionId;

	private Guid _youTubeStateSubscriptionId;

	private string? _lastTwitchChatState;

	private string? _lastKickChatState;

	private string? _lastDiscordChatState;

	private Guid _discordSubscriptionId;

	private bool _isInitialized = false;

	private bool _showEventsInChat = true;

	private readonly Dictionary<string, DateTime> _recentKickMessages = new Dictionary<string, DateTime>();

	private readonly Dictionary<string, DateTime> _recentTwitchMessages = new Dictionary<string, DateTime>();

	private readonly Dictionary<string, DateTime> _recentYoutubeMessages = new Dictionary<string, DateTime>();

	private readonly Dictionary<string, DateTime> _recentDiscordMessages = new Dictionary<string, DateTime>();

	private readonly object _recentMessagesLock = new object();

	private readonly HashSet<string> _loadedMessageIds = new HashSet<string>();

	private int _historyLoadedCount = 0;

	private const int MAX_HISTORY_MESSAGES = 200;

	private readonly HashSet<string> _processedMessageIds = new HashSet<string>();

	private readonly Queue<string> _processedMessageIdOrder = new Queue<string>();

	private readonly object _processedMessageIdsLock = new object();

	private const int MAX_PROCESSED_MESSAGE_IDS = 1000;

	private readonly Dictionary<string, string> _discordMessageBotIds = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly Queue<string> _discordMessageBotIdOrder = new Queue<string>();

	private readonly object _discordMessageBotIdsLock = new object();

	private const int MAX_DISCORD_MESSAGE_BOT_IDS = 500;

	private readonly Dictionary<string, (string ChannelId, string? BotId, DateTime ReceivedAtUtc)> _lastDiscordDmByBot = new Dictionary<string, (string, string, DateTime)>(StringComparer.Ordinal);

	private readonly Queue<string> _lastDiscordDmByBotOrder = new Queue<string>();

	private readonly object _lastDiscordDmByBotLock = new object();

	private const int MAX_DISCORD_DM_ORIGINS = 200;

	private static readonly TimeSpan DiscordDmReplyWindow = TimeSpan.FromSeconds(30L);

	private const int MAX_DM_THREAD_MESSAGES = 200;

	private const int MAX_DM_CONVERSATIONS = 50;

	private Platform? _twitchPlatformRow;

	private Platform? _kickPlatformRow;

	private Platform? _youtubePlatformRow;

	private DateTime _platformRowsLoadedAt = DateTime.MinValue;

	private readonly object _platformRowsLock = new object();

	private static readonly TimeSpan PlatformRowCacheTtl = TimeSpan.FromMinutes(5L);

	private bool _twitchAutoRepliesEnabled = true;

	private readonly Dictionary<string, (bool IsFollower, DateTime ExpiresAt)> _followerCache = new Dictionary<string, (bool, DateTime)>();

	private readonly object _followerCacheLock = new object();

	private static readonly TimeSpan FollowerCacheTtl = TimeSpan.FromMinutes(5L);

	private static readonly HttpClient _helixHttpClient = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(5L)
	};

	private ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();

	private ObservableCollection<DmConversation> _dmConversations = new ObservableCollection<DmConversation>();

	private DmConversation? _selectedDmConversation;

	private string _dmReplyInput = "";

	// Unified reply box: true while a DM thread is open — the main chat input
	// becomes the DM composer (SendMessageAsync routes to the DM reply path).
	private bool _isDmReplyMode;

	// "New DM" inline input in the DM inbox header: target user snowflake, the
	// surfaced validation/API error, and whether the inline input is expanded.
	private string _newDmUserId = "";

	private string _newDmStatus = "";

	private bool _isNewDmInputOpen;

	// DM inbox search: filters the conversation list by username/user id and
	// runs a content search over saved DM history while non-empty.
	private string _dmSearchQuery = "";

	private readonly ObservableCollection<DmSearchResult> _dmSearchResults = new ObservableCollection<DmSearchResult>();

	private ChatMessage? _dmScrollTarget;

	private bool _showDmPanel;

	// Connected Channels panel (third header toggle): mutually exclusive with
	// the DM panel, corrected in both directions exactly like DM/Combined.
	private bool _showChannelsPanel;

	private bool _channelsLoading;

	private bool _combinedView;

	private string _messageInput = "";

	private bool _autoScroll = true;

	private ObservableCollection<PlatformOption> _connectedPlatforms = new ObservableCollection<PlatformOption>();

	private PlatformOption? _selectedPlatform;

	private bool _isPlatformSelectorOpen;

	private bool _isAllPlatformsSettingsOpen;

	private bool _isTwitchSettingsOpen;

	private bool _isKickSettingsOpen;

	private bool _isYoutubeSettingsOpen;

	private bool _isTiktokSettingsOpen;

	private bool _isDiscordSettingsOpen;

	private bool _isTwitchConnected;

	private bool _isKickConnected;

	private bool _isKickConfigured;

	private bool _isYoutubeConnected;

	private bool _isTiktokConnected;

	private bool _isDiscordConnected;

	private bool _twitchFilterEnabled = true;

	private bool _kickFilterEnabled = true;

	private bool _youtubeFilterEnabled = true;

	private bool _tiktokFilterEnabled = true;

	private bool _discordFilterEnabled = true;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? sendDmReplyCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? backToDmInboxCommand;

	private AsyncRelayCommand? startDmByUserIdCommand;

	private RelayCommand? toggleNewDmInputCommand;

	private AsyncRelayCommand<DmSearchResult>? openDmSearchResultCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? loadHistoryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? sendMessageCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? sendToAllPlatformsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? togglePlatformSelectorCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PlatformOption>? selectPlatformCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<object?>? openViewerProfileCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleAllPlatformsSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleTwitchSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleKickSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleYoutubeSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleTiktokSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleDiscordSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? startGiveawayCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? startPollCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? triggerEventCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? sendAnnouncementCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearChatCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? clearHistoryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleAutoScrollCommand;

	private readonly HashSet<string> _routedDmMessageIds = new HashSet<string>();
	private readonly object _routedDmMessageIdsLock = new object();

	private bool WasDmAlreadyRouted(string messageId)
	{
		lock (_routedDmMessageIdsLock)
		{
			return _routedDmMessageIds.Contains(messageId);
		}
	}

	private void RememberRoutedDm(string messageId)
	{
		lock (_routedDmMessageIdsLock)
		{
			_routedDmMessageIds.Add(messageId);
		}
	}

	private static readonly ConcurrentDictionary<string, DateTime> DmHintLastSent = new ConcurrentDictionary<string, DateTime>();

	private static readonly TimeSpan DmHintThrottle = TimeSpan.FromMinutes(5.0);

	private AsyncRelayCommand? loadDmHistoryCommand;

	private AsyncRelayCommand? clearDmHistoryCommand;

	private AsyncRelayCommand? loadAllDmHistoryCommand;

	private AsyncRelayCommand? clearAllDmHistoryCommand;

	private AsyncRelayCommand? refreshChannelsCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? toggleChannelMonitoringCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? toggleModerationPurposeCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? toggleAdminPurposeCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? toggleSupportPurposeCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? toggleLoggingPurposeCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? setAsMainChannelCommand;

	private AsyncRelayCommand<ConnectedChannelItem>? openChannelInChatCommand;

	public bool HasSelectedDmConversation => SelectedDmConversation != null;

	public bool DmOnlyView => ShowDmPanel && !CombinedView;

	public ObservableCollection<ConnectedChannelItem> ConnectedChannels { get; } = new ObservableCollection<ConnectedChannelItem>();

	private bool CanSendDmReply => SelectedDmConversation != null && !string.IsNullOrWhiteSpace(DmReplyInput);

	private IDiscordService? DiscordSendTarget => _discordBotManager.Primary ?? (_discordService.IsConnected ? _discordService : null);

	private bool IsAnyDiscordConnected => _discordBotManager.Connected.Count > 0 || _discordService.IsConnected;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<ChatMessage> Messages
	{
		get
		{
			return _messages;
		}
		[MemberNotNull("_messages")]
		set
		{
			if (!EqualityComparer<ObservableCollection<ChatMessage>>.Default.Equals(_messages, value))
			{
				OnPropertyChanging("Messages");
				_messages = value;
				OnPropertyChanged("Messages");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<DmConversation> DmConversations
	{
		get
		{
			return _dmConversations;
		}
		[MemberNotNull("_dmConversations")]
		set
		{
			if (!EqualityComparer<ObservableCollection<DmConversation>>.Default.Equals(_dmConversations, value))
			{
				OnPropertyChanging("DmConversations");
				_dmConversations = value;
				OnPropertyChanged("DmConversations");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DmConversation? SelectedDmConversation
	{
		get
		{
			return _selectedDmConversation;
		}
		set
		{
			if (!EqualityComparer<DmConversation>.Default.Equals(_selectedDmConversation, value))
			{
				OnPropertyChanging("SelectedDmConversation");
				_selectedDmConversation = value;
				OnSelectedDmConversationChanged(value);
				OnPropertyChanged("SelectedDmConversation");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DmReplyInput
	{
		get
		{
			return _dmReplyInput;
		}
		[MemberNotNull("_dmReplyInput")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_dmReplyInput, value))
			{
				OnPropertyChanging("DmReplyInput");
				_dmReplyInput = value;
				OnDmReplyInputChanged(value);
				OnPropertyChanged("DmReplyInput");
			}
		}
	}

	/// <summary>True while a DM thread is open: the main chat input acts as the DM composer (placeholder + send routing switch).</summary>
	public bool IsDmReplyMode
	{
		get
		{
			return _isDmReplyMode;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isDmReplyMode, value))
			{
				OnPropertyChanging("IsDmReplyMode");
				_isDmReplyMode = value;
				OnPropertyChanged("IsDmReplyMode");
			}
		}
	}

	public string NewDmUserId
	{
		get
		{
			return _newDmUserId;
		}
		[MemberNotNull("_newDmUserId")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newDmUserId, value))
			{
				OnPropertyChanging("NewDmUserId");
				_newDmUserId = value;
				OnPropertyChanged("NewDmUserId");
			}
		}
	}

	/// <summary>Inline validation/API error for the "New DM" input (empty = nothing to show).</summary>
	public string NewDmStatus
	{
		get
		{
			return _newDmStatus;
		}
		[MemberNotNull("_newDmStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newDmStatus, value))
			{
				OnPropertyChanging("NewDmStatus");
				_newDmStatus = value;
				OnPropertyChanged("NewDmStatus");
			}
		}
	}

	public bool IsNewDmInputOpen
	{
		get
		{
			return _isNewDmInputOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isNewDmInputOpen, value))
			{
				OnPropertyChanging("IsNewDmInputOpen");
				_isNewDmInputOpen = value;
				OnPropertyChanged("IsNewDmInputOpen");
			}
		}
	}

	/// <summary>
	/// DM inbox search text. While non-empty the inbox shows matches over
	/// conversation usernames, Discord user ids, last previews AND a saved
	/// history content search (DmSearchResults); clearing it restores the
	/// normal conversation list.
	/// </summary>
	public string DmSearchQuery
	{
		get
		{
			return _dmSearchQuery;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_dmSearchQuery, value))
			{
				OnPropertyChanging("DmSearchQuery");
				_dmSearchQuery = value;
				OnPropertyChanged("DmSearchQuery");
				OnPropertyChanged("IsDmSearching");
				OnPropertyChanged("IsDmInboxIdle");
				OnPropertyChanged("ShowDmInboxList");
				_ = ApplyDmSearchAsync();
			}
		}
	}

	/// <summary>True while a DM search query is active (inbox swaps to search results).</summary>
	public bool IsDmSearching => !string.IsNullOrWhiteSpace(DmSearchQuery);

	/// <summary>True while NO DM search query is active (normal conversation list is shown).</summary>
	public bool IsDmInboxIdle => string.IsNullOrWhiteSpace(DmSearchQuery);

	/// <summary>
	/// True when the inbox conversation list may render: no active search AND
	/// no open thread. The inbox list and the thread list share the same grid
	/// cell, so the inbox must also hide while a conversation is selected —
	/// otherwise it bleeds through the thread's transparent background and a
	/// new (empty) thread shows every other conversation's rows beneath it.
	/// </summary>
	public bool ShowDmInboxList => IsDmInboxIdle && !HasSelectedDmConversation;

	/// <summary>Search hits (username/id matches + saved-history content matches) shown while searching.</summary>
	public ObservableCollection<DmSearchResult> DmSearchResults => _dmSearchResults;

	/// <summary>True when the current search produced at least one hit.</summary>
	public bool HasDmSearchResults => _dmSearchResults.Count > 0;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ChatMessage? DmScrollTarget
	{
		get
		{
			return _dmScrollTarget;
		}
		set
		{
			if (!EqualityComparer<ChatMessage>.Default.Equals(_dmScrollTarget, value))
			{
				OnPropertyChanging("DmScrollTarget");
				_dmScrollTarget = value;
				OnPropertyChanged("DmScrollTarget");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowDmPanel
	{
		get
		{
			return _showDmPanel;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showDmPanel, value))
			{
				OnPropertyChanging("ShowDmPanel");
				_showDmPanel = value;
				OnShowDmPanelChanged(value);
				OnPropertyChanged("ShowDmPanel");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CombinedView
	{
		get
		{
			return _combinedView;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_combinedView, value))
			{
				OnPropertyChanging("CombinedView");
				_combinedView = value;
				OnCombinedViewChanged(value);
				OnPropertyChanged("CombinedView");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowChannelsPanel
	{
		get
		{
			return _showChannelsPanel;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showChannelsPanel, value))
			{
				OnPropertyChanging("ShowChannelsPanel");
				_showChannelsPanel = value;
				OnShowChannelsPanelChanged(value);
				OnPropertyChanged("ShowChannelsPanel");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ChannelsLoading
	{
		get
		{
			return _channelsLoading;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_channelsLoading, value))
			{
				OnPropertyChanging("ChannelsLoading");
				_channelsLoading = value;
				OnPropertyChanged("ChannelsLoading");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string MessageInput
	{
		get
		{
			return _messageInput;
		}
		[MemberNotNull("_messageInput")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_messageInput, value))
			{
				OnPropertyChanging("MessageInput");
				_messageInput = value;
				OnPropertyChanged("MessageInput");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AutoScroll
	{
		get
		{
			return _autoScroll;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_autoScroll, value))
			{
				OnPropertyChanging("AutoScroll");
				_autoScroll = value;
				OnPropertyChanged("AutoScroll");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<PlatformOption> ConnectedPlatforms
	{
		get
		{
			return _connectedPlatforms;
		}
		[MemberNotNull("_connectedPlatforms")]
		set
		{
			if (!EqualityComparer<ObservableCollection<PlatformOption>>.Default.Equals(_connectedPlatforms, value))
			{
				OnPropertyChanging("ConnectedPlatforms");
				_connectedPlatforms = value;
				OnPropertyChanged("ConnectedPlatforms");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public PlatformOption? SelectedPlatform
	{
		get
		{
			return _selectedPlatform;
		}
		set
		{
			if (!EqualityComparer<PlatformOption>.Default.Equals(_selectedPlatform, value))
			{
				OnPropertyChanging("SelectedPlatform");
				_selectedPlatform = value;
				OnPropertyChanged("SelectedPlatform");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsPlatformSelectorOpen
	{
		get
		{
			return _isPlatformSelectorOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isPlatformSelectorOpen, value))
			{
				OnPropertyChanging("IsPlatformSelectorOpen");
				_isPlatformSelectorOpen = value;
				OnPropertyChanged("IsPlatformSelectorOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAllPlatformsSettingsOpen
	{
		get
		{
			return _isAllPlatformsSettingsOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isAllPlatformsSettingsOpen, value))
			{
				OnPropertyChanging("IsAllPlatformsSettingsOpen");
				_isAllPlatformsSettingsOpen = value;
				OnPropertyChanged("IsAllPlatformsSettingsOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsTwitchSettingsOpen
	{
		get
		{
			return _isTwitchSettingsOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isTwitchSettingsOpen, value))
			{
				OnPropertyChanging("IsTwitchSettingsOpen");
				_isTwitchSettingsOpen = value;
				OnPropertyChanged("IsTwitchSettingsOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsKickSettingsOpen
	{
		get
		{
			return _isKickSettingsOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isKickSettingsOpen, value))
			{
				OnPropertyChanging("IsKickSettingsOpen");
				_isKickSettingsOpen = value;
				OnPropertyChanged("IsKickSettingsOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsYoutubeSettingsOpen
	{
		get
		{
			return _isYoutubeSettingsOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isYoutubeSettingsOpen, value))
			{
				OnPropertyChanging("IsYoutubeSettingsOpen");
				_isYoutubeSettingsOpen = value;
				OnPropertyChanged("IsYoutubeSettingsOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsTiktokSettingsOpen
	{
		get
		{
			return _isTiktokSettingsOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isTiktokSettingsOpen, value))
			{
				OnPropertyChanging("IsTiktokSettingsOpen");
				_isTiktokSettingsOpen = value;
				OnPropertyChanged("IsTiktokSettingsOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDiscordSettingsOpen
	{
		get
		{
			return _isDiscordSettingsOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isDiscordSettingsOpen, value))
			{
				OnPropertyChanging("IsDiscordSettingsOpen");
				_isDiscordSettingsOpen = value;
				OnPropertyChanged("IsDiscordSettingsOpen");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsTwitchConnected
	{
		get
		{
			return _isTwitchConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isTwitchConnected, value))
			{
				OnPropertyChanging("IsTwitchConnected");
				_isTwitchConnected = value;
				OnPropertyChanged("IsTwitchConnected");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsKickConnected
	{
		get
		{
			return _isKickConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isKickConnected, value))
			{
				OnPropertyChanging("IsKickConnected");
				_isKickConnected = value;
				OnPropertyChanged("IsKickConnected");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsKickConfigured
	{
		get
		{
			return _isKickConfigured;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isKickConfigured, value))
			{
				OnPropertyChanging("IsKickConfigured");
				_isKickConfigured = value;
				OnPropertyChanged("IsKickConfigured");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsYoutubeConnected
	{
		get
		{
			return _isYoutubeConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isYoutubeConnected, value))
			{
				OnPropertyChanging("IsYoutubeConnected");
				_isYoutubeConnected = value;
				OnPropertyChanged("IsYoutubeConnected");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsTiktokConnected
	{
		get
		{
			return _isTiktokConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isTiktokConnected, value))
			{
				OnPropertyChanging("IsTiktokConnected");
				_isTiktokConnected = value;
				OnPropertyChanged("IsTiktokConnected");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDiscordConnected
	{
		get
		{
			return _isDiscordConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isDiscordConnected, value))
			{
				OnPropertyChanging("IsDiscordConnected");
				_isDiscordConnected = value;
				OnPropertyChanged("IsDiscordConnected");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool TwitchFilterEnabled
	{
		get
		{
			return _twitchFilterEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_twitchFilterEnabled, value))
			{
				OnPropertyChanging("TwitchFilterEnabled");
				_twitchFilterEnabled = value;
				OnPropertyChanged("TwitchFilterEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool KickFilterEnabled
	{
		get
		{
			return _kickFilterEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_kickFilterEnabled, value))
			{
				OnPropertyChanging("KickFilterEnabled");
				_kickFilterEnabled = value;
				OnPropertyChanged("KickFilterEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool YoutubeFilterEnabled
	{
		get
		{
			return _youtubeFilterEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_youtubeFilterEnabled, value))
			{
				OnPropertyChanging("YoutubeFilterEnabled");
				_youtubeFilterEnabled = value;
				OnPropertyChanged("YoutubeFilterEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool TiktokFilterEnabled
	{
		get
		{
			return _tiktokFilterEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_tiktokFilterEnabled, value))
			{
				OnPropertyChanging("TiktokFilterEnabled");
				_tiktokFilterEnabled = value;
				OnPropertyChanged("TiktokFilterEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool DiscordFilterEnabled
	{
		get
		{
			return _discordFilterEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_discordFilterEnabled, value))
			{
				OnPropertyChanging("DiscordFilterEnabled");
				_discordFilterEnabled = value;
				OnPropertyChanged("DiscordFilterEnabled");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SendDmReplyCommand => sendDmReplyCommand ?? (sendDmReplyCommand = new AsyncRelayCommand(SendDmReplyAsync, () => CanSendDmReply));

	public IAsyncRelayCommand LoadDmHistoryCommand => loadDmHistoryCommand ?? (loadDmHistoryCommand = new AsyncRelayCommand(LoadSelectedDmHistoryAsync, () => SelectedDmConversation != null));

	public IAsyncRelayCommand ClearDmHistoryCommand => clearDmHistoryCommand ?? (clearDmHistoryCommand = new AsyncRelayCommand(ClearSelectedDmHistoryAsync, () => SelectedDmConversation != null));

	public IAsyncRelayCommand ClearAllDmHistoryCommand => clearAllDmHistoryCommand ?? (clearAllDmHistoryCommand = new AsyncRelayCommand(ClearAllDmHistoryAsync));

	public IAsyncRelayCommand LoadAllDmHistoryCommand => loadAllDmHistoryCommand ?? (loadAllDmHistoryCommand = new AsyncRelayCommand(LoadAllDmHistoryAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand BackToDmInboxCommand => backToDmInboxCommand ?? (backToDmInboxCommand = new RelayCommand(BackToDmInbox));

	public IRelayCommand ToggleNewDmInputCommand => toggleNewDmInputCommand ?? (toggleNewDmInputCommand = new RelayCommand(ToggleNewDmInput));

	public IAsyncRelayCommand StartDmByUserIdCommand => startDmByUserIdCommand ?? (startDmByUserIdCommand = new AsyncRelayCommand(StartDmByUserIdAsync));

	public IAsyncRelayCommand OpenDmSearchResultCommand => openDmSearchResultCommand ?? (openDmSearchResultCommand = new AsyncRelayCommand<DmSearchResult>(OpenDmSearchResultAsync));

	public IAsyncRelayCommand RefreshChannelsCommand => refreshChannelsCommand ?? (refreshChannelsCommand = new AsyncRelayCommand(RefreshChannelsAsync));

	public IAsyncRelayCommand ToggleChannelMonitoringCommand => toggleChannelMonitoringCommand ?? (toggleChannelMonitoringCommand = new AsyncRelayCommand<ConnectedChannelItem>(ToggleChannelMonitoringAsync));

	public IAsyncRelayCommand ToggleModerationPurposeCommand => toggleModerationPurposeCommand ?? (toggleModerationPurposeCommand = new AsyncRelayCommand<ConnectedChannelItem>(ToggleModerationPurposeAsync));

	public IAsyncRelayCommand ToggleAdminPurposeCommand => toggleAdminPurposeCommand ?? (toggleAdminPurposeCommand = new AsyncRelayCommand<ConnectedChannelItem>(ToggleAdminPurposeAsync));

	public IAsyncRelayCommand ToggleSupportPurposeCommand => toggleSupportPurposeCommand ?? (toggleSupportPurposeCommand = new AsyncRelayCommand<ConnectedChannelItem>(ToggleSupportPurposeAsync));

	public IAsyncRelayCommand ToggleLoggingPurposeCommand => toggleLoggingPurposeCommand ?? (toggleLoggingPurposeCommand = new AsyncRelayCommand<ConnectedChannelItem>(ToggleLoggingPurposeAsync));

	public IAsyncRelayCommand SetAsMainChannelCommand => setAsMainChannelCommand ?? (setAsMainChannelCommand = new AsyncRelayCommand<ConnectedChannelItem>(SetAsMainChannelAsync));

	public IAsyncRelayCommand OpenChannelInChatCommand => openChannelInChatCommand ?? (openChannelInChatCommand = new AsyncRelayCommand<ConnectedChannelItem>(OpenChannelInChatAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand LoadHistoryCommand => loadHistoryCommand ?? (loadHistoryCommand = new AsyncRelayCommand(LoadHistoryAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SendMessageCommand => sendMessageCommand ?? (sendMessageCommand = new AsyncRelayCommand(SendMessageAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SendToAllPlatformsCommand => sendToAllPlatformsCommand ?? (sendToAllPlatformsCommand = new AsyncRelayCommand(SendToAllPlatformsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand TogglePlatformSelectorCommand => togglePlatformSelectorCommand ?? (togglePlatformSelectorCommand = new RelayCommand(TogglePlatformSelector));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PlatformOption> SelectPlatformCommand => selectPlatformCommand ?? (selectPlatformCommand = new RelayCommand<PlatformOption>(SelectPlatform));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<object?> OpenViewerProfileCommand => openViewerProfileCommand ?? (openViewerProfileCommand = new AsyncRelayCommand<object>(OpenViewerProfileAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleAllPlatformsSettingsCommand => toggleAllPlatformsSettingsCommand ?? (toggleAllPlatformsSettingsCommand = new RelayCommand(ToggleAllPlatformsSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleTwitchSettingsCommand => toggleTwitchSettingsCommand ?? (toggleTwitchSettingsCommand = new RelayCommand(ToggleTwitchSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleKickSettingsCommand => toggleKickSettingsCommand ?? (toggleKickSettingsCommand = new RelayCommand(ToggleKickSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleYoutubeSettingsCommand => toggleYoutubeSettingsCommand ?? (toggleYoutubeSettingsCommand = new RelayCommand(ToggleYoutubeSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleTiktokSettingsCommand => toggleTiktokSettingsCommand ?? (toggleTiktokSettingsCommand = new RelayCommand(ToggleTiktokSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleDiscordSettingsCommand => toggleDiscordSettingsCommand ?? (toggleDiscordSettingsCommand = new RelayCommand(ToggleDiscordSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand StartGiveawayCommand => startGiveawayCommand ?? (startGiveawayCommand = new AsyncRelayCommand(StartGiveawayAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand StartPollCommand => startPollCommand ?? (startPollCommand = new AsyncRelayCommand(StartPollAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand TriggerEventCommand => triggerEventCommand ?? (triggerEventCommand = new AsyncRelayCommand(TriggerEventAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SendAnnouncementCommand => sendAnnouncementCommand ?? (sendAnnouncementCommand = new AsyncRelayCommand(SendAnnouncementAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearChatCommand => clearChatCommand ?? (clearChatCommand = new RelayCommand(ClearChat));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ClearHistoryCommand => clearHistoryCommand ?? (clearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleAutoScrollCommand => toggleAutoScrollCommand ?? (toggleAutoScrollCommand = new RelayCommand(ToggleAutoScroll));

	private async Task LoadSelectedDmHistoryAsync()
	{
		if (SelectedDmConversation != null)
		{
			SelectedDmConversation.HistoryLoaded = false;
			await LoadDmThreadHistoryAsync(SelectedDmConversation);
		}
	}

	private async Task ClearSelectedDmHistoryAsync()
	{
		if (SelectedDmConversation != null)
		{
			DmConversation conversation = SelectedDmConversation;
			await (_dataService?.ClearDiscordDmHistoryAsync(conversation.Username));
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				conversation.Messages.Clear();
				conversation.LastPreview = "";
				conversation.UnreadCount = 0;
				conversation.HistoryLoaded = true;
			});
		}
	}

	private async Task LoadAllDmHistoryAsync()
	{
		if (_dataService == null)
		{
			return;
		}
		try
		{
			foreach (var item in await _dataService.GetDiscordDmConversationsAsync())
			{
				var (username, _, _, _) = item;
				_ = item.Item2;
				_ = item.Item3;
				_ = item.Item4;
				DmConversation conversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase));
				if (conversation == null)
				{
					conversation = new DmConversation
					{
						Username = username
					};
					await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
					{
						DmConversations.Add(conversation);
					});
				}
				conversation.HistoryLoaded = false;
				await LoadDmThreadHistoryAsync(conversation);
			}
			_logger.LogInformation("Loaded ALL DM history ({Count} conversations)", DmConversations.Count);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Load all DM history failed");
		}
	}

	private async Task ClearAllDmHistoryAsync()
	{
		await (_dataService?.ClearDiscordDmHistoryAsync(null));
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			DmConversations.Clear();
			SelectedDmConversation = null;
		});
	}

	private void ScrollDmThreadToBottom()
	{
		if (AutoScroll)
		{
			DmConversation selectedDmConversation = SelectedDmConversation;
			if (selectedDmConversation != null && selectedDmConversation.Messages.Count != 0)
			{
				ObservableCollection<ChatMessage> messages = selectedDmConversation.Messages;
				DmScrollTarget = messages[messages.Count - 1];
			}
		}
	}

	private void RouteDiscordDmToConversation(DiscordChatMessageEvent evt, ChatMessage chatMessage)
	{
		string username = evt.Username ?? "Unknown";
		DmConversation dmConversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase));
		if (dmConversation == null)
		{
			dmConversation = new DmConversation
			{
				Username = username
			};
			DmConversations.Insert(0, dmConversation);
		}
		else if (DmConversations.IndexOf(dmConversation) > 0)
		{
			DmConversations.Move(DmConversations.IndexOf(dmConversation), 0);
		}
		if (string.IsNullOrEmpty(dmConversation.ChannelId) && !string.IsNullOrEmpty(evt.MessageId))
		{
			lock (_lastDiscordDmByBotLock)
			{
				if (_lastDiscordDmByBot.TryGetValue(evt.MessageId, out (string, string, DateTime) value))
				{
					dmConversation.ChannelId = value.Item1;
					dmConversation.BotId = value.Item2 ?? dmConversation.BotId;
				}
			}
		}
		// Never forget WHO is talking: adopt the sender's Discord user id the
		// first time we see it so by-id replies and id search work forever.
		if (string.IsNullOrEmpty(dmConversation.UserId) && !string.IsNullOrEmpty(evt.SenderUserId))
		{
			dmConversation.UserId = evt.SenderUserId;
		}
		// Dedupe: the same DM can arrive through more than one event path.
		if (!string.IsNullOrEmpty(evt.MessageId))
		{
			if (_routedDmMessageIds.Contains(evt.MessageId))
			{
				return;
			}
			_routedDmMessageIds.Add(evt.MessageId);
		}
		dmConversation.Messages.Add(chatMessage);
		_ = _dataService?.SaveDiscordDmMessageAsync(new DiscordDmMessage
		{
			Id = (evt.MessageId ?? Guid.NewGuid().ToString()),
			BotId = dmConversation.BotId ?? string.Empty,
			ChannelId = dmConversation.ChannelId ?? string.Empty,
			Username = username,
			UserId = dmConversation.UserId,
			Direction = "Incoming",
			Content = chatMessage.Message ?? string.Empty,
			Timestamp = DateTime.UtcNow,
		});
		dmConversation.LastPreview = chatMessage.Message;
		dmConversation.LastAt = chatMessage.Timestamp;
		if (dmConversation != SelectedDmConversation)
		{
			dmConversation.UnreadCount++;
		}
		else
		{
			ScrollDmThreadToBottom();
		}
		_logger.LogInformation("\ud83d\udce8 DM routed to inbox conversation {Username} ({MessageCount} messages, unread {Unread})", username, dmConversation.Messages.Count, dmConversation.UnreadCount);
	}

	private async Task LoadDmConversationsFromDbAsync()
	{
		if (_dataService == null)
		{
			return;
		}
		try
		{
			IEnumerable<(string Username, string? BotUsername, DateTime LastAt, int Total)> summaries = await _dataService.GetDiscordDmConversationsAsync();
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				foreach (var item4 in summaries)
				{
					string username = item4.Username;
					string item = item4.BotUsername;
					DateTime item2 = item4.LastAt;
					int item3 = item4.Total;
					DmConversation dmConversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase));
					if (dmConversation == null)
					{
						dmConversation = new DmConversation
						{
							Username = username
						};
						DmConversations.Add(dmConversation);
					}
					if (string.IsNullOrEmpty(dmConversation.BotUsername) && !string.IsNullOrEmpty(item))
					{
						dmConversation.BotUsername = item;
					}
					if (dmConversation.LastAt < item2)
					{
						dmConversation.LastAt = item2;
					}
					if (string.IsNullOrEmpty(dmConversation.LastPreview))
					{
						dmConversation.LastPreview = item3 + " saved messages";
					}
				}
			});
			_logger.LogInformation("Loaded {Count} persisted DM conversation(s) from history", DmConversations.Count);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to load persisted DM conversations");
		}
	}

	private async Task LoadDmThreadHistoryAsync(DmConversation conversation)
	{
		if (_dataService == null || conversation.HistoryLoaded)
		{
			return;
		}
		try
		{
			IEnumerable<DiscordDmMessage> history = await _dataService.GetDiscordDmHistoryAsync(conversation.Username);
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				conversation.Messages.Clear();
				foreach (DiscordDmMessage item in history)
				{
					// Backfill the participant's Discord user id from the first
					// saved row that carries one (same pattern as the ChannelId
					// backfill) so by-id replies survive restarts.
					if (string.IsNullOrEmpty(conversation.UserId) && !string.IsNullOrEmpty(item.UserId))
					{
						conversation.UserId = item.UserId;
					}
					bool flag = item.Direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase);
					ChatMessage chatMessage = new ChatMessage
					{
						Platform = (flag ? "Vyral" : "Discord"),
						Username = (flag ? (item.BotUsername ?? "Bot") : item.Username),
						Message = item.Content,
						Timestamp = item.Timestamp.ToLocalTime(),
						Color = (flag ? "#5865F2" : "#2E7D32"),
						IsModerator = false,
						IsSubscriber = false,
						IsVip = false,
						IsBroadcaster = flag,
						Badges = (flag ? new Dictionary<string, string> { { "broadcaster", "1" } } : new Dictionary<string, string>())
					};
					chatMessage.InitializeBadges();
					conversation.Messages.Add(chatMessage);
				}
				conversation.HistoryLoaded = true;
			});
			_logger.LogInformation("Loaded {Count} persisted DMs for {Username}", conversation.Messages.Count, conversation.Username);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to load DM history for {Username}", conversation.Username);
		}
	}

	private void PruneDmConversations()
	{
		while (DmConversations.Count > 50)
		{
			DmConversation dmConversation = null;
			foreach (DmConversation dmConversation2 in DmConversations)
			{
				if (dmConversation2 != SelectedDmConversation && (dmConversation == null || dmConversation2.LastAt < dmConversation.LastAt))
				{
					dmConversation = dmConversation2;
				}
			}
			if (dmConversation == null)
			{
				break;
			}
			DmConversations.Remove(dmConversation);
		}
	}

	private void MoveDmConversationToFront(DmConversation conversation)
	{
		int num = DmConversations.IndexOf(conversation);
		if (num > 0)
		{
			DmConversations.Move(num, 0);
		}
	}

	private async Task SendDmReplyAsync()
	{
		// Kept for the legacy reply-bar command (SendDmReplyCommand); the
		// unified main input reaches the same core through SendMessageAsync's
		// DM branch, using MessageInput as the text.
		if (await SendDmReplyCoreAsync(SelectedDmConversation, DmReplyInput?.Trim()))
		{
			DmReplyInput = "";
		}
	}

	/// <summary>
	/// Shared DM reply send used by the legacy reply command AND the unified
	/// main input when <see cref="IsDmReplyMode"/> is on. Returns true when the
	/// message was delivered — the caller then clears its own input property
	/// (DmReplyInput or MessageInput respectively).
	/// </summary>
	private async Task<bool> SendDmReplyCoreAsync(DmConversation? conversation, string? text)
	{
		if (conversation == null || string.IsNullOrEmpty(text))
		{
			return false;
		}
		// Reply routing fallback chain: (1) the DM channel we have on file,
		// (2) the participant's Discord user id — this rescues conversations
		// whose channel is not gateway-cached (proactive by-id DMs, restarts),
		// (3) resolving the recipient by username.
		IDiscordService? bot = ResolveDiscordDmSendTarget(conversation.BotId);
		if (bot == null || !bot.IsConnected)
		{
			bot = _discordBotManager.Connected.FirstOrDefault() ?? (_discordService.IsConnected ? _discordService : null);
		}
		if (bot == null || !bot.IsConnected)
		{
			_logger.LogWarning("DM reply to {Username} skipped: no connected Discord bot", conversation.Username);
			await InsertDmDeliveryFailureAsync(conversation, "No connected Discord bot.");
			return false;
		}
		bool delivered = false;
		string? dmError = null;
		try
		{
			if (!string.IsNullOrEmpty(conversation.ChannelId))
			{
				delivered = await bot.SendDmToChannelAsync(conversation.ChannelId, text);
			}
			if (!delivered && !string.IsNullOrEmpty(conversation.UserId))
			{
				(bool byUserId, string? userIdError) = await bot.SendDmToUserAsync(conversation.UserId, text);
				delivered = byUserId;
				if (!byUserId)
				{
					dmError = userIdError;
					_logger.LogWarning("DM reply by user id {UserId} failed: {Error}", conversation.UserId, userIdError ?? "unknown error");
				}
			}
			if (!delivered)
			{
				delivered = await bot.SendDmAsync(conversation.Username, text, null);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "DM reply to {Username} (channel {ChannelId}) threw", conversation.Username, conversation.ChannelId);
			delivered = false;
		}
		if (!delivered)
		{
			_logger.LogWarning("Failed to deliver DM reply to {Username} (channel {ChannelId})", conversation.Username, conversation.ChannelId);
			// Make the failure VISIBLE in the open thread so the streamer sees
			// exactly why Discord refused the send (privacy settings, missing
			// shared server, ...) instead of a silent drop.
			await InsertDmDeliveryFailureAsync(conversation, dmError);
			return false;
		}
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			ChatMessage chatMessage = new ChatMessage
			{
				Platform = "Vyral",
				Username = (bot.BotUsername ?? "Bot"),
				Message = text,
				Timestamp = DateTime.Now,
				Color = "#5865F2",
				IsModerator = false,
				IsSubscriber = false,
				IsVip = false,
				IsBroadcaster = true,
				Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
			};
			chatMessage.InitializeBadges();
			conversation.Messages.Add(chatMessage);
			_dataService?.SaveDiscordDmMessageAsync(new DiscordDmMessage
			{
				BotId = (conversation.BotId ?? string.Empty),
				BotUsername = (bot.BotUsername ?? "Bot"),
				ChannelId = conversation.ChannelId,
				Username = conversation.Username,
				UserId = conversation.UserId,
				Direction = "Outgoing",
				Content = text,
				Timestamp = DateTime.UtcNow
			});
			conversation.LastPreview = text;
			conversation.LastAt = chatMessage.Timestamp;
			MoveDmConversationToFront(conversation);
			ScrollDmThreadToBottom();
		});
		return true;
	}

	/// <summary>
	/// Surfaces a failed DM reply INSIDE the open conversation's thread as a
	/// red SYSTEM message ("⚠️ Couldn't deliver: ...") so the streamer sees the
	/// real Discord rejection — e.g. "can't message this user (no shared
	/// server / privacy settings)". Same styling pattern as the outgoing echo;
	/// not persisted (it is a UI note, not a chat message). In-memory only.
	/// </summary>
	private async Task InsertDmDeliveryFailureAsync(DmConversation conversation, string? error)
	{
		string errorText = string.IsNullOrWhiteSpace(error) ? "Discord refused the message." : error.Trim();
		string failureText = "⚠️ Couldn't deliver: " + errorText;
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			ChatMessage chatMessage = new ChatMessage
			{
				Platform = "Vyral",
				Username = "System",
				Message = failureText,
				Timestamp = DateTime.Now,
				Color = "#E53935",
				IsModerator = false,
				IsSubscriber = false,
				IsVip = false,
				IsBroadcaster = false,
				IsSystemMessage = true,
				Badges = new Dictionary<string, string>()
			};
			chatMessage.InitializeBadges();
			conversation.Messages.Add(chatMessage);
			ScrollDmThreadToBottom();
		});
	}

	private void BackToDmInbox()
	{
		SelectedDmConversation = null;
	}

	private void ToggleNewDmInput()
	{
		NewDmStatus = "";
		IsNewDmInputOpen = !IsNewDmInputOpen;
	}

	/// <summary>
	/// Starts a DM conversation from a raw Discord user snowflake entered in
	/// the inbox header: validates the id (17-20 digits), opens the DM channel
	/// on the primary/linked bot and selects the (new or existing) conversation
	/// so the thread opens ready to reply. Nothing is persisted here — the
	/// history row is saved with the first message.
	/// </summary>
	private async Task StartDmByUserIdAsync()
	{
		NewDmStatus = "";
		string input = (NewDmUserId ?? string.Empty).Trim();
		if (input.Length < 17 || input.Length > 20 || !input.All(char.IsDigit))
		{
			_logger.LogWarning("New DM skipped: '{UserId}' is not a 17-20 digit Discord user id", input);
			NewDmStatus = "Enter a 17-20 digit Discord user ID.";
			return;
		}
		IDiscordService? bot = ResolveDiscordSendTarget(null);
		if (bot == null || !bot.IsConnected)
		{
			_logger.LogWarning("New DM skipped: no connected Discord bot");
			NewDmStatus = "No connected Discord bot.";
			return;
		}
		(bool success, string? channelId, string? username, string? openError) = await bot.OpenDmByUserIdAsync(input);
		if (!success || string.IsNullOrEmpty(channelId))
		{
			_logger.LogWarning("New DM failed for user id {UserId} on bot {BotId}: {Error}", input, bot.BotId, openError ?? "unknown error");
			NewDmStatus = string.IsNullOrEmpty(openError)
				? "Could not open a DM with that user ID."
				: ("Could not open a DM: " + openError);
			return;
		}
		string resolvedUsername = username ?? input;
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			// Reuse an existing conversation for the same channel (or the same
			// user on the same bot) instead of duplicating the inbox row.
			DmConversation? conversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.ChannelId, channelId, StringComparison.Ordinal));
			if (conversation == null)
			{
				conversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, resolvedUsername, StringComparison.OrdinalIgnoreCase) && string.Equals(c.BotId, bot.BotId, StringComparison.Ordinal));
			}
			if (conversation != null)
			{
				// Refresh the routing info so the thread stays replyable even
				// if the saved row predates this DM channel.
				conversation.ChannelId = channelId;
				conversation.BotId = bot.BotId;
				conversation.UserId ??= input;
				MoveDmConversationToFront(conversation);
			}
			else
			{
				conversation = new DmConversation
				{
					Username = resolvedUsername,
					ChannelId = channelId,
					BotId = bot.BotId,
					UserId = input,
					Platform = "Discord",
					LastAt = DateTime.Now
				};
				DmConversations.Insert(0, conversation);
			}
			ShowDmPanel = true;
			SelectedDmConversation = conversation;
			NewDmUserId = "";
			IsNewDmInputOpen = false;
			NewDmStatus = "";
			ScrollDmThreadToBottom();
		});
		_logger.LogInformation("Opened DM conversation with {Username} (channel {ChannelId}) via bot {BotId}", resolvedUsername, channelId, bot.BotId);
	}

	/// <summary>
	/// Runs the DM inbox search for the current <see cref="DmSearchQuery"/>:
	/// in-memory matches over the loaded conversations (Username / UserId /
	/// last preview) plus a saved-history content search from the data store.
	/// Results land in <see cref="DmSearchResults"/>; an empty query restores
	/// the normal conversation list. Incoming DMs keep appending while
	/// searching — clearing the box brings the untouched inbox back.
	/// </summary>
	private async Task ApplyDmSearchAsync()
	{
		string query = (DmSearchQuery ?? string.Empty).Trim();
		if (query.Length == 0)
		{
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				_dmSearchResults.Clear();
				OnPropertyChanged("HasDmSearchResults");
			});
			return;
		}
		List<DmSearchResult> results = new List<DmSearchResult>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (DmConversation conversation in DmConversations)
		{
			bool usernameMatch = (!string.IsNullOrEmpty(conversation.Username) && conversation.Username.Contains(query, StringComparison.OrdinalIgnoreCase));
			bool idMatch = (!string.IsNullOrEmpty(conversation.UserId) && conversation.UserId.Contains(query, StringComparison.Ordinal));
			bool previewMatch = (!string.IsNullOrEmpty(conversation.LastPreview) && conversation.LastPreview.Contains(query, StringComparison.OrdinalIgnoreCase));
			if (!usernameMatch && !idMatch && !previewMatch)
			{
				continue;
			}
			string snippet = previewMatch ? conversation.LastPreview : (idMatch ? ("Discord user ID " + conversation.UserId) : conversation.LastPreview);
			string key = conversation.Username + "|" + snippet;
			if (seen.Add(key))
			{
				results.Add(new DmSearchResult
				{
					Username = conversation.Username,
					UserId = conversation.UserId,
					BotId = conversation.BotId,
					ChannelId = conversation.ChannelId,
					Snippet = snippet,
					Timestamp = conversation.LastAt
				});
			}
		}
		try
		{
			IEnumerable<DiscordDmMessage> contentHits = _dataService == null
				? Enumerable.Empty<DiscordDmMessage>()
				: await _dataService.SearchDiscordDmMessagesAsync(query);
			foreach (DiscordDmMessage hit in contentHits)
			{
				string key = hit.Username + "|" + hit.Content;
				if (!seen.Add(key))
				{
					continue;
				}
				results.Add(new DmSearchResult
				{
					Username = hit.Username,
					UserId = hit.UserId,
					BotId = hit.BotId,
					ChannelId = hit.ChannelId,
					Snippet = hit.Content,
					Timestamp = hit.Timestamp.ToLocalTime()
				});
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "DM content search failed for '{Query}'", query);
		}
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			// A newer query may have superseded this run while we were searching.
			if (!string.Equals((DmSearchQuery ?? string.Empty).Trim(), query, StringComparison.Ordinal))
			{
				return;
			}
			_dmSearchResults.Clear();
			foreach (DmSearchResult result in results)
			{
				_dmSearchResults.Add(result);
			}
			OnPropertyChanged("HasDmSearchResults");
		});
	}

	/// <summary>
	/// Opens the conversation behind a clicked search hit: creates (or reuses)
	/// the conversation row — carrying over the remembered user id — selects it
	/// (which loads the full persisted history) and clears the search so the
	/// normal inbox returns.
	/// </summary>
	private Task OpenDmSearchResultAsync(DmSearchResult? result)
	{
		if (result == null)
		{
			return Task.CompletedTask;
		}
		DmSearchQuery = "";
		DmConversation? conversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, result.Username, StringComparison.OrdinalIgnoreCase));
		if (conversation == null)
		{
			conversation = new DmConversation
			{
				Username = result.Username,
				BotId = (result.BotId ?? string.Empty),
				ChannelId = (result.ChannelId ?? string.Empty),
				UserId = result.UserId,
				Platform = "Discord",
				LastAt = ((result.Timestamp == default(DateTime)) ? DateTime.Now : result.Timestamp)
			};
			DmConversations.Insert(0, conversation);
		}
		else
		{
			conversation.UserId ??= result.UserId;
			conversation.BotId = string.IsNullOrEmpty(conversation.BotId) ? (result.BotId ?? string.Empty) : conversation.BotId;
			conversation.ChannelId = string.IsNullOrEmpty(conversation.ChannelId) ? (result.ChannelId ?? string.Empty) : conversation.ChannelId;
		}
		ShowDmPanel = true;
		SelectedDmConversation = conversation;
		MoveDmConversationToFront(conversation);
		ScrollDmThreadToBottom();
		_logger.LogInformation("DM search result opened for {Username}", result.Username);
		return Task.CompletedTask;
	}

	public ChatViewModel(ILogger<ChatViewModel> logger, IEventBus eventBus, ITwitchService twitchService, IKickService kickService, IYouTubeService youtubeService, IDiscordService discordService, IDiscordBotManager discordBotManager, KickSettingsViewModel kickSettings, IChatMessageStore chatStore, IChatEventSettingsService eventSettingsService, IAutoReplyService autoReplyService, IDiscordCommandHandler? discordCommandHandler = null, ICurrencyService? currencyService = null, ICurrencyCommandService? currencyCommandService = null, ICommandService? commandService = null, ISoundService? soundService = null, IAlertService? alertService = null, GiveawayCommandService? giveawayCommandService = null, PollCommandService? pollCommandService = null, WatchtimeService? watchtimeService = null, WatchtimeCommandService? watchtimeCommandService = null, IVariableReplacementService? variableReplacementService = null, IDataService? dataService = null, CommandActionRunner? commandActionRunner = null, WorkflowTriggerService? workflowTriggerService = null, IResponseWaiterRegistry? responseWaiters = null, ILinkCodeService? linkCodeService = null, IViewerStatusService? viewerStatusService = null, ISocialGoalService? socialGoalService = null, StreamEffectCommandRouter? streamEffectRouter = null, IConnectedChannelService? connectedChannels = null)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_eventBus = eventBus ?? throw new ArgumentNullException("eventBus");
		_twitchService = twitchService ?? throw new ArgumentNullException("twitchService");
		_kickService = kickService ?? throw new ArgumentNullException("kickService");
		_youtubeService = youtubeService ?? throw new ArgumentNullException("youtubeService");
		_discordService = discordService ?? throw new ArgumentNullException("discordService");
		_discordBotManager = discordBotManager ?? throw new ArgumentNullException("discordBotManager");
		_kickSettings = kickSettings ?? throw new ArgumentNullException("kickSettings");
		_chatStore = chatStore ?? throw new ArgumentNullException("chatStore");
		_eventSettingsService = eventSettingsService ?? throw new ArgumentNullException("eventSettingsService");
		_autoReplyService = autoReplyService ?? throw new ArgumentNullException("autoReplyService");
		_discordCommandHandler = discordCommandHandler;
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
		_dataService = dataService;
		_commandActionRunner = commandActionRunner;
		_workflowTriggerService = workflowTriggerService;
		_responseWaiters = responseWaiters;
		_linkCodeService = linkCodeService;
		_viewerStatusService = viewerStatusService;
		_socialGoalService = socialGoalService;
		_streamEffectRouter = streamEffectRouter;
		_connectedChannels = connectedChannels;
		_currencyService = currencyService;
		_currencyCommandService = currencyCommandService;
		_commandService = commandService;
		_soundService = soundService;
		_alertService = alertService;
		ChatMessage._logger = _logger;
	}

	public async Task InitializeAsync()
	{
		if (_isInitialized)
		{
			_logger.LogInformation("ChatViewModel already initialized, skipping duplicate subscription");
			UpdateConnectedPlatforms();
			return;
		}
		_logger.LogInformation("\ud83d\ude80 Initializing unified chat view with EventBus subscriptions...");
		LoadDmConversationsFromDbAsync();
		_twitchSubscriptionId = await _eventBus.SubscribeAsync<TwitchChatMessageEvent>(OnTwitchChatMessageReceivedAsync);
		_logger.LogInformation("? Subscribed to TwitchChatMessageEvent (ID: {SubscriptionId})", _twitchSubscriptionId);
		_kickSubscriptionId = await _eventBus.SubscribeAsync<KickChatMessageEvent>(OnKickChatMessageReceivedAsync);
		_logger.LogInformation("? Subscribed to KickChatMessageEvent (ID: {SubscriptionId})", _kickSubscriptionId);
		_logger.LogInformation("   \ud83d\udce1 Ready to receive Kick webhook events from WebhooksController!");
		_youtubeSubscriptionId = await _eventBus.SubscribeAsync<YouTubeChatMessageEvent>(OnYouTubeChatMessageReceivedAsync);
		_youTubeStateSubscriptionId = await _eventBus.SubscribeAsync<YouTubeLiveChatStateEvent>(OnYouTubeLiveChatStateAsync);
		_logger.LogInformation("? Subscribed to YouTubeChatMessageEvent (ID: {SubscriptionId})", _youtubeSubscriptionId);
		_discordSubscriptionId = await _eventBus.SubscribeAsync<DiscordChatMessageEvent>(OnDiscordChatMessageReceivedAsync);
		_logger.LogInformation("? Subscribed to DiscordChatMessageEvent (ID: {SubscriptionId})", _discordSubscriptionId);
		_genericSubscriptionId = await _eventBus.SubscribeAsync<ChatMessageReceivedEvent>(OnChatMessageReceivedAsync);
		_logger.LogInformation("? Subscribed to ChatMessageReceivedEvent (ID: {SubscriptionId})", _genericSubscriptionId);
		_routingSubscriptionId = await _eventBus.SubscribeAsync<PlatformChatRoutingChangedEvent>(OnPlatformRoutingChangedAsync);
		_logger.LogInformation("? Subscribed to PlatformChatRoutingChangedEvent (ID: {SubscriptionId})", _routingSubscriptionId);
		_connectionSubscriptionId = await _eventBus.SubscribeAsync<PlatformConnectionChangedEvent>(OnPlatformConnectionChangedAsync);
		_logger.LogInformation("? Subscribed to PlatformConnectionChangedEvent (ID: {SubscriptionId})", _connectionSubscriptionId);
		_kickFollowSubscriptionId = await _eventBus.SubscribeAsync<KickFollowEvent>(OnKickFollowAsync);
		_logger.LogInformation("? Subscribed to KickFollowEvent (ID: {SubscriptionId})", _kickFollowSubscriptionId);
		_kickSubscribeSubscriptionId = await _eventBus.SubscribeAsync<KickSubscribeEvent>(OnKickSubscribeAsync);
		_logger.LogInformation("? Subscribed to KickSubscribeEvent (ID: {SubscriptionId})", _kickSubscribeSubscriptionId);
		_kickGiftSubSubscriptionId = await _eventBus.SubscribeAsync<KickGiftSubEvent>(OnKickGiftSubAsync);
		_logger.LogInformation("? Subscribed to KickGiftSubEvent (ID: {SubscriptionId})", _kickGiftSubSubscriptionId);
		_kickHostSubscriptionId = await _eventBus.SubscribeAsync<KickHostEvent>(OnKickHostAsync);
		_logger.LogInformation("? Subscribed to KickHostEvent (ID: {SubscriptionId})", _kickHostSubscriptionId);
		_kickKicksGiftedSubscriptionId = await _eventBus.SubscribeAsync<KickKicksGiftedEvent>(OnKickKicksGiftedAsync);
		_logger.LogInformation("? Subscribed to KickKicksGiftedEvent (ID: {SubscriptionId})", _kickKicksGiftedSubscriptionId);
		_kickRewardRedeemedSubscriptionId = await _eventBus.SubscribeAsync<KickRewardRedeemedEvent>(OnKickRewardRedeemedAsync);
		_logger.LogInformation("? Subscribed to KickRewardRedeemedEvent (ID: {SubscriptionId})", _kickRewardRedeemedSubscriptionId);
		_kickStreamStatusSubscriptionId = await _eventBus.SubscribeAsync<KickStreamStatusEvent>(OnKickStreamStatusAsync);
		_logger.LogInformation("? Subscribed to KickStreamStatusEvent (ID: {SubscriptionId})", _kickStreamStatusSubscriptionId);
		_twitchFollowSubscriptionId = await _eventBus.SubscribeAsync<TwitchFollowEvent>(OnTwitchFollowAsync);
		_logger.LogInformation("? Subscribed to TwitchFollowEvent (ID: {SubscriptionId})", _twitchFollowSubscriptionId);
		_twitchSubscribeSubscriptionId = await _eventBus.SubscribeAsync<TwitchSubscribeEvent>(OnTwitchSubscribeAsync);
		_logger.LogInformation("? Subscribed to TwitchSubscribeEvent (ID: {SubscriptionId})", _twitchSubscribeSubscriptionId);
		_twitchResubSubscriptionId = await _eventBus.SubscribeAsync<TwitchResubEvent>(OnTwitchResubAsync);
		_logger.LogInformation("? Subscribed to TwitchResubEvent (ID: {SubscriptionId})", _twitchResubSubscriptionId);
		_twitchGiftSubSubscriptionId = await _eventBus.SubscribeAsync<TwitchGiftSubEvent>(OnTwitchGiftSubAsync);
		_logger.LogInformation("? Subscribed to TwitchGiftSubEvent (ID: {SubscriptionId})", _twitchGiftSubSubscriptionId);
		_twitchRaidSubscriptionId = await _eventBus.SubscribeAsync<TwitchRaidEvent>(OnTwitchRaidAsync);
		_logger.LogInformation("? Subscribed to TwitchRaidEvent (ID: {SubscriptionId})", _twitchRaidSubscriptionId);
		_twitchCheerSubscriptionId = await _eventBus.SubscribeAsync<TwitchCheerEvent>(OnTwitchCheerAsync);
		_logger.LogInformation("? Subscribed to TwitchCheerEvent (ID: {SubscriptionId})", _twitchCheerSubscriptionId);
		_twitchStreamStatusSubscriptionId = await _eventBus.SubscribeAsync<TwitchStreamStatusEvent>(OnTwitchStreamStatusAsync);
		_logger.LogInformation("? Subscribed to TwitchStreamStatusEvent (ID: {SubscriptionId})", _twitchStreamStatusSubscriptionId);
		_giveawayAutoEndedSubscriptionId = await _eventBus.SubscribeAsync<GiveawayAutoEndedEvent>(OnGiveawayAutoEndedAsync);
		_logger.LogInformation("? Subscribed to GiveawayAutoEndedEvent (ID: {SubscriptionId})", _giveawayAutoEndedSubscriptionId);
		_pollAutoEndedSubscriptionId = await _eventBus.SubscribeAsync<PollAutoEndedEvent>(OnPollAutoEndedAsync);
		_logger.LogInformation("? Subscribed to PollAutoEndedEvent (ID: {SubscriptionId})", _pollAutoEndedSubscriptionId);
		_youTubeMembershipStatusSubscriptionId = await _eventBus.SubscribeAsync<YouTubeMembershipEvent>(OnYouTubeMembershipStatusAsync);
		_logger.LogInformation("? Subscribed to YouTubeMembershipEvent (ID: {SubscriptionId})", _youTubeMembershipStatusSubscriptionId);
		_accountsLinkedSubscriptionId = await _eventBus.SubscribeAsync<AccountsLinkedEvent>(OnAccountsLinkedAsync);
		_logger.LogInformation("? Subscribed to AccountsLinkedEvent (ID: {SubscriptionId})", _accountsLinkedSubscriptionId);
		_viewerGoalCompletedSubscriptionId = await _eventBus.SubscribeAsync<ViewerGoalCompletedEvent>(OnViewerGoalCompletedAsync);
		_logger.LogInformation("? Subscribed to ViewerGoalCompletedEvent (ID: {SubscriptionId})", _viewerGoalCompletedSubscriptionId);
		_giveawayIntakeModeSubscriptionId = await _eventBus.SubscribeAsync<GiveawayIntakeModeChangedEvent>(OnGiveawayIntakeModeChangedAsync);
		_logger.LogInformation("? Subscribed to GiveawayIntakeModeChangedEvent (ID: {SubscriptionId})", _giveawayIntakeModeSubscriptionId);
		UpdateConnectedPlatforms();
		try
		{
			KickFilterEnabled = _kickSettings.IsChatEnabled;
			_logger.LogInformation("Initialized KickFilterEnabled from KickSettings: {Enabled}", KickFilterEnabled);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogWarning(ex4, "Failed to initialize KickFilterEnabled from KickSettings");
		}
		try
		{
			if (_dataService != null)
			{
				DiscordFilterEnabled = (await _dataService.GetPlatformByNameAsync("Discord"))?.DiscordChatEnabled ?? true;
				_logger.LogInformation("Initialized DiscordFilterEnabled from Platform row: {Enabled}", DiscordFilterEnabled);
			}
		}
		catch (Exception ex)
		{
			Exception ex5 = ex;
			Exception ex6 = ex5;
			Exception ex7 = ex6;
			_logger.LogWarning(ex7, "Failed to initialize DiscordFilterEnabled from Platform row");
		}
		try
		{
			if (_dataService != null)
			{
				TwitchFilterEnabled = (await _dataService.GetPlatformByNameAsync("Twitch"))?.TwitchChatEnabled ?? true;
				_logger.LogInformation("Initialized TwitchFilterEnabled from Platform row: {Enabled}", TwitchFilterEnabled);
			}
		}
		catch (Exception ex)
		{
			Exception ex8 = ex;
			Exception ex9 = ex8;
			Exception ex10 = ex9;
			_logger.LogWarning(ex10, "Failed to initialize TwitchFilterEnabled from Platform row");
		}
		try
		{
			if (_dataService != null)
			{
				YoutubeFilterEnabled = (await _dataService.GetPlatformByNameAsync("YouTube"))?.YoutubeChatEnabled ?? true;
				_logger.LogInformation("Initialized YoutubeFilterEnabled from Platform row: {Enabled}", YoutubeFilterEnabled);
			}
		}
		catch (Exception ex)
		{
			Exception ex11 = ex;
			Exception ex12 = ex11;
			Exception ex13 = ex12;
			_logger.LogWarning(ex13, "Failed to initialize YoutubeFilterEnabled from Platform row");
		}
		await ReloadEventDisplayToggleAsync();
		await ReloadTwitchAutoRepliesToggleAsync();
		_isInitialized = true;
		_logger.LogInformation("\ud83c\udfaf ChatViewModel initialization complete - all event handlers active!");
		_logger.LogInformation("\ud83d\udcbe Chat history available but not loaded yet (saves memory - load on-demand if needed)");
	}

	public async Task ReloadEventDisplayToggleAsync()
	{
		try
		{
			if (_dataService != null)
			{
				_showEventsInChat = !bool.TryParse(await _dataService.GetConfigValueAsync("Notifications.ShowEventsInChat"), out var enabled) || enabled;
				_logger.LogInformation("Event-message display toggle: {Enabled}", _showEventsInChat);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogWarning(ex4, "Failed to load event-message display toggle");
		}
	}

	private async Task LoadHistoryAsync()
	{
		try
		{
			if (_historyLoadedCount > 0)
			{
				_logger.LogInformation("\ud83d\udcdc History already loaded ({Count} messages). Clear chat to reload.", _historyLoadedCount);
				return;
			}
			_logger.LogInformation("\ud83d\udcdc Loading chat history on-demand...");
			List<ChatMessage> allHistoryMessages = new List<ChatMessage>();
			if (_kickService.IsConnected && !string.IsNullOrEmpty(_kickService.Channel))
			{
				IReadOnlyList<IEvent> stored = await _chatStore.GetRecentAsync("Kick", _kickService.Channel);
				if (stored != null && stored.Count > 0)
				{
					_logger.LogInformation("Fetched {Count} stored Kick messages from database", stored.Count);
					foreach (IEvent evt in stored)
					{
						if (evt is KickChatMessageEvent k)
						{
							string messageId = $"Kick:{k.Username}:{k.Timestamp.Ticks}:{k.Message}";
							if (!_loadedMessageIds.Contains(messageId))
							{
								ChatMessage historyMessage = new ChatMessage
								{
									Platform = "Kick",
									Username = (k.DisplayName ?? k.Username),
									Message = k.Message,
									Timestamp = (k.CreatedAt ?? k.Timestamp),
									Color = (k.Color ?? "#53FC18"),
									IsModerator = k.IsModerator,
									IsSubscriber = k.IsSubscriber,
									IsVip = false,
									IsBroadcaster = k.IsOwner,
									Badges = (k.Badges ?? new Dictionary<string, string>())
								};
								historyMessage.InitializeBadges();
								allHistoryMessages.Add(historyMessage);
								_loadedMessageIds.Add(messageId);
							}
						}
					}
				}
			}
			if (_twitchService.IsConnected && !string.IsNullOrEmpty(_twitchService.Channel))
			{
				IReadOnlyList<IEvent> stored2 = await _chatStore.GetRecentAsync("Twitch", _twitchService.Channel);
				if (stored2 != null && stored2.Count > 0)
				{
					_logger.LogInformation("Fetched {Count} stored Twitch messages from database", stored2.Count);
					foreach (IEvent evt2 in stored2)
					{
						if (evt2 is TwitchChatMessageEvent t)
						{
							string messageId2 = $"Twitch:{t.Username}:{t.Timestamp.Ticks}:{t.Message}";
							if (!_loadedMessageIds.Contains(messageId2))
							{
								ChatMessage historyMessage2 = new ChatMessage
								{
									Platform = "Twitch",
									Username = (t.DisplayName ?? t.Username),
									Message = t.Message,
									Timestamp = t.Timestamp.ToLocalTime(),
									Color = (t.Color ?? "#FFFFFF"),
									IsModerator = t.IsModerator,
									IsSubscriber = t.IsSubscriber,
									IsVip = t.IsVip,
									IsBroadcaster = t.IsBroadcaster,
									Badges = (t.Badges ?? new Dictionary<string, string>())
								};
								historyMessage2.InitializeBadges();
								allHistoryMessages.Add(historyMessage2);
								_loadedMessageIds.Add(messageId2);
							}
						}
					}
				}
			}
			if (_youtubeService.IsConnected && !string.IsNullOrEmpty(_youtubeService.Channel))
			{
				IReadOnlyList<IEvent> stored3 = await _chatStore.GetRecentAsync("YouTube", _youtubeService.Channel);
				if (stored3 != null && stored3.Count > 0)
				{
					_logger.LogInformation("Fetched {Count} stored YouTube messages from database", stored3.Count);
					DateTime? publishedAt = null;
					foreach (IEvent evt3 in stored3)
					{
						YouTubeChatMessageEvent y = evt3 as YouTubeChatMessageEvent;
						int num;
						if ((object)y != null)
						{
							publishedAt = y.PublishedAt;
							num = 1;
						}
						else
						{
							num = 0;
						}
						if (num != 0)
						{
							DateTime stamp = publishedAt ?? y.Timestamp;
							string messageId3 = $"YouTube:{y.Username}:{stamp.Ticks}:{y.Message}";
							if (!_loadedMessageIds.Contains(messageId3))
							{
								ChatMessage historyMessage3 = new ChatMessage
								{
									Platform = "YouTube",
									Username = y.DisplayName,
									Message = y.Message,
									Timestamp = stamp.ToLocalTime(),
									Color = (y.Color ?? "#FF0000"),
									IsModerator = y.IsModerator,
									IsSubscriber = y.IsMember,
									IsBroadcaster = y.IsOwner,
									Badges = new Dictionary<string, string>()
								};
								historyMessage3.InitializeBadges();
								allHistoryMessages.Add(historyMessage3);
								_loadedMessageIds.Add(messageId3);
							}
						}
					}
				}
			}
			IDiscordService discordHistoryService = _discordBotManager.Primary ?? _discordService;
			if (discordHistoryService.IsConnected && !string.IsNullOrEmpty(discordHistoryService.SelectedChannelId))
			{
				string discordChannelKey = discordHistoryService.SelectedChannelId;
				foreach (DiscordGuildInfo guild in discordHistoryService.ConnectedGuilds)
				{
					foreach (DiscordChannelInfo channel in guild.TextChannels)
					{
						if (channel.Id == discordHistoryService.SelectedChannelId)
						{
							discordChannelKey = channel.Name;
							break;
						}
					}
					if (discordChannelKey != discordHistoryService.SelectedChannelId)
					{
						break;
					}
				}
				IReadOnlyList<IEvent> stored4 = await _chatStore.GetRecentAsync("Discord", discordChannelKey);
				if (stored4 != null && stored4.Count > 0)
				{
					_logger.LogInformation("Fetched {Count} stored Discord messages from database", stored4.Count);
					foreach (IEvent evt4 in stored4)
					{
						if (!(evt4 is DiscordChatMessageEvent d))
						{
							continue;
						}
						string messageId4 = $"Discord:{d.Username}:{d.Timestamp.Ticks}:{d.Message}";
						if (!_loadedMessageIds.Contains(messageId4))
						{
							Dictionary<string, string> badges = new Dictionary<string, string>();
							if (d.IsBroadcaster)
							{
								badges["broadcaster"] = "1";
							}
							if (d.IsModerator)
							{
								badges["moderator"] = "1";
							}
							ChatMessage historyMessage4 = new ChatMessage
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
							historyMessage4.InitializeBadges();
							allHistoryMessages.Add(historyMessage4);
							_loadedMessageIds.Add(messageId4);
						}
					}
				}
			}
			if (allHistoryMessages.Count > 0)
			{
				await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
				{
					foreach (ChatMessage item in allHistoryMessages.OrderBy((ChatMessage m) => m.Timestamp))
					{
						Messages.Insert(0, item);
					}
					_historyLoadedCount += allHistoryMessages.Count;
					_logger.LogInformation("? Added {Added} history messages ({Kick} Kick, {Twitch} Twitch, {YouTube} YouTube). Total: {Total}/{Max}", allHistoryMessages.Count, allHistoryMessages.Count((ChatMessage m) => m.Platform == "Kick"), allHistoryMessages.Count((ChatMessage m) => m.Platform == "Twitch"), allHistoryMessages.Count((ChatMessage m) => m.Platform == "YouTube"), _historyLoadedCount, 200);
					while (Messages.Count > 500)
					{
						Messages.RemoveAt(Messages.Count - 1);
					}
				});
			}
			else
			{
				_logger.LogInformation("No history messages available from any platform");
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogWarning(ex4, "Failed to load chat history");
		}
	}

	private IDiscordService? ResolveDiscordSendTarget(string? botId)
	{
		if (!string.IsNullOrWhiteSpace(botId) && !string.Equals(botId, "primary", StringComparison.Ordinal))
		{
			IDiscordService discordService = _discordBotManager.Get(botId);
			if (discordService != null && discordService.IsConnected)
			{
				return discordService;
			}
		}
		if (string.Equals(botId, "primary", StringComparison.Ordinal) && _discordService.IsConnected)
		{
			return _discordService;
		}
		return DiscordSendTarget;
	}

	private IDiscordService? ResolveDiscordDmSendTarget(string? originBotId)
	{
		if (!string.IsNullOrWhiteSpace(originBotId) && !string.Equals(originBotId, "primary", StringComparison.OrdinalIgnoreCase))
		{
			IDiscordService discordService = _discordBotManager.Get(originBotId);
			if (discordService != null)
			{
				return discordService;
			}
		}
		if (_discordService.IsConnected)
		{
			return _discordService;
		}
		return DiscordSendTarget;
	}

	private async Task<string?> RunCommandActionsAsync(string marker, CommandContext context)
	{
		if (_commandActionRunner == null || _dataService == null)
		{
			_logger.LogWarning("Action-sequence command skipped: runner or data service unavailable");
			return null;
		}
		int length = "$ACTIONS$".Length;
		string commandId = marker.Substring(length, marker.Length - length);
		try
		{
			Command command = await _dataService.GetCommandAsync(commandId);
			if (command == null)
			{
				_logger.LogWarning("Action-sequence command {CommandId} not found", commandId);
				return null;
			}
			return await _commandActionRunner.RunAsync(command, context);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to run action sequence for command {CommandId}", commandId);
			return null;
		}
	}

	private async Task SendDiscordReplyAsync(string response, string? botId = null, string? dmChannelId = null, string? dmUsername = null)
	{
		if (!string.IsNullOrEmpty(dmChannelId))
		{
			IDiscordService dmBot = ResolveDiscordDmSendTarget(botId);
			if (dmBot?.IsConnected ?? false)
			{
				if (!(await dmBot.SendDmToChannelAsync(dmChannelId, response)))
				{
					_logger.LogWarning("Failed to deliver DM reply to {Username} (channel {ChannelId})", string.IsNullOrEmpty(dmUsername) ? "unknown" : dmUsername, dmChannelId);
				}
			}
			else
			{
				_logger.LogWarning("Discord DM reply skipped (no connected bot): channel {ChannelId}", dmChannelId);
			}
			return;
		}
		IDiscordService target = ResolveDiscordSendTarget(botId);
		if (target == null)
		{
			return;
		}
		bool sent = await target.SendMessageAsync(response);
		if (sent)
		{
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				ChatMessage chatMessage = new ChatMessage
				{
					Platform = "Vyral",
					Username = (target.BotUsername ?? "Bot"),
					Message = response,
					Timestamp = DateTime.Now,
					Color = "#5865F2",
					IsBroadcaster = true,
					Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
				};
				chatMessage.InitializeBadges();
				Messages.Add(chatMessage);
				while (Messages.Count > 500)
				{
					Messages.RemoveAt(0);
				}
			});
		}
		if (sent && response.StartsWith("\ud83d\udccb", StringComparison.Ordinal))
		{
			AutoDeleteDiscordReplyAsync(target);
		}
	}

	private async Task AutoDeleteDiscordReplyAsync(IDiscordService bot)
	{
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(30L));
			await bot.DeleteMessageByScopeAsync("last_bot");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogDebug(ex2, "Scheduled delete of the Discord help reply failed (best-effort cleanup)");
		}
	}

	private async Task HandleDiscordSlashCommandAsync(string commandLine)
	{
		if (_discordCommandHandler == null)
		{
			await AddSystemMessageAsync("Discord", "Discord moderation commands are not available.");
			return;
		}
		IDiscordService bot = ResolveDiscordSendTarget(null);
		if (bot?.IsConnected ?? false)
		{
			try
			{
				try
				{
					string bareCommand = commandLine.TrimStart('/').Trim();
					DiscordCommandResult result = await _discordCommandHandler.HandleAsync(bot, bareCommand, callerIsModerator: true, callerIsOwner: true, bot.SelectedChannelId);
					if (!result.Handled)
					{
						await AddSystemMessageAsync("Discord", "Unknown Discord command. Try /help for the built-in moderation commands.");
					}
					else
					{
						await AddSystemMessageAsync("Discord", "Discord: " + result.Response);
					}
				}
				catch (Exception exception)
				{
					_logger.LogError(exception, "Error executing Discord slash command: {Command}", commandLine);
					await AddSystemMessageAsync("Discord", "Discord command failed. Check the logs for details.");
				}
				return;
			}
			finally
			{
				MessageInput = "";
			}
		}
		await AddSystemMessageAsync("Discord", "No Discord bot is connected.");
	}

	private async Task<bool> TryHandleDiscordModCommandAsync(DiscordChatMessageEvent evt)
	{
		if (_discordCommandHandler == null || string.IsNullOrEmpty(evt.Message) || !evt.Message.StartsWith("!"))
		{
			return false;
		}
		try
		{
			IDiscordService originBot = ResolveDiscordSendTarget(evt.BotId);
			if (originBot == null || !originBot.IsConnected)
			{
				return false;
			}
			try
			{
				if (_dataService != null)
				{
					string disabledJson = await _dataService.GetConfigValueAsync("DiscordBot." + evt.BotId + ".DisabledBuiltIns");
					if (!string.IsNullOrEmpty(disabledJson))
					{
						List<string> disabled = JsonSerializer.Deserialize<List<string>>(disabledJson);
						string firstToken = evt.Message.Trim().TrimStart('!', '.').Split(' ')[0].ToLowerInvariant();
						if (disabled?.Contains(firstToken) ?? false)
						{
							_logger.LogInformation("Skipped disabled built-in '{Command}' for bot {BotId} (owned by an external integration)", firstToken, evt.BotId);
							return false;
						}
					}
				}
			}
			catch (Exception exception)
			{
				_logger.LogDebug(exception, "Could not check disabled built-ins for bot {BotId}", evt.BotId);
			}
			// Engine A channel gating: when connected channels are configured,
			// only Moderation/Admin-tagged ones run the built-in commands
			// (legacy rule: nothing configured → every channel is allowed).
			if (_connectedChannels != null &&
				!await _connectedChannels.AcceptsCommandsAsync(evt.BotId ?? "", evt.ChannelId ?? ""))
			{
				return false;
			}
			DiscordCommandResult result = await _discordCommandHandler.HandleAsync(originBot, evt.Message, evt.IsModerator, evt.IsBroadcaster, evt.ChannelId);
			if (result.Handled)
			{
				await SendDiscordReplyAsync(result.Response, evt.BotId);
				_logger.LogInformation("Processed in-Discord moderation command from {Username}: {Command} -> {Response}", evt.Username, evt.Message, result.Response);
				return true;
			}
		}
		catch (Exception exception2)
		{
			_logger.LogError(exception2, "Error processing in-Discord moderation command: {Command}", evt.Message);
		}
		return false;
	}

	/// <summary>
	/// Runs the stream-effect router ("!flashlights", "!lightson",
	/// "!lightsoff", "!streamdeck") first; a non-null reply is sent through the
	/// same Discord reply path the mod-command results use and the message is
	/// consumed (other handlers never see it).
	/// </summary>
	private async Task<bool> TryHandleStreamEffectCommandAsync(DiscordChatMessageEvent evt)
	{
		if (_streamEffectRouter == null || string.IsNullOrEmpty(evt.Message))
		{
			return false;
		}
		try
		{
			string? reply = await _streamEffectRouter.TryHandleAsync(new StreamEffectCommandContext(
				evt.BotId ?? "", evt.GuildId, evt.ChannelId ?? "", evt.SenderUserId ?? "", evt.Username, evt.Message));
			if (reply == null)
			{
				return false;
			}
			await SendDiscordReplyAsync(reply, evt.BotId);
			_logger.LogInformation("Processed in-Discord stream-effect command from {Username}: {Command} -> {Response}", evt.Username, evt.Message, reply);
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error processing in-Discord stream-effect command: {Command}", evt.Message);
		}
		return false;
	}

	private void RememberDiscordBotOrigin(string? messageId, string? botId)
	{
		if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(botId))
		{
			return;
		}
		lock (_discordMessageBotIdsLock)
		{
			if (!_discordMessageBotIds.ContainsKey(messageId))
			{
				_discordMessageBotIdOrder.Enqueue(messageId);
			}
			_discordMessageBotIds[messageId] = botId;
			while (_discordMessageBotIdOrder.Count > 500)
			{
				string text = _discordMessageBotIdOrder.Dequeue();
				if (!_discordMessageBotIdOrder.Contains(text))
				{
					_discordMessageBotIds.Remove(text);
				}
			}
		}
	}

	private string? ResolveDiscordBotOrigin(ChatMessageReceivedEvent evt)
	{
		if (!string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(evt.MessageId))
		{
			return null;
		}
		lock (_discordMessageBotIdsLock)
		{
			string value;
			return _discordMessageBotIds.TryGetValue(evt.MessageId, out value) ? value : null;
		}
	}

	private void RememberDiscordDmOrigin(string? messageId, string? channelId, string? botId)
	{
		if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(channelId))
		{
			return;
		}
		lock (_lastDiscordDmByBotLock)
		{
			if (!_lastDiscordDmByBot.ContainsKey(messageId))
			{
				_lastDiscordDmByBotOrder.Enqueue(messageId);
			}
			_lastDiscordDmByBot[messageId] = (channelId, botId, DateTime.UtcNow);
			while (_lastDiscordDmByBotOrder.Count > 200)
			{
				string text = _lastDiscordDmByBotOrder.Dequeue();
				if (!_lastDiscordDmByBotOrder.Contains(text))
				{
					_lastDiscordDmByBot.Remove(text);
				}
			}
		}
	}

	private (string ChannelId, string? BotId)? ResolveDiscordDmReplyTarget(ChatMessageReceivedEvent evt)
	{
		if (!string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(evt.MessageId))
		{
			return null;
		}
		lock (_lastDiscordDmByBotLock)
		{
			if (_lastDiscordDmByBot.TryGetValue(evt.MessageId, out (string, string, DateTime) value) && DateTime.UtcNow - value.Item3 <= DiscordDmReplyWindow)
			{
				return (value.Item1, value.Item2);
			}
		}
		return null;
	}

	public void UpdateConnectedPlatforms()
	{
		ConnectedPlatforms.Clear();
		_logger.LogInformation("Updating connected platforms. Twitch connected: {IsConnected}, Channel: {Channel}", _twitchService.IsConnected, _twitchService.Channel ?? "(none)");
		if (_twitchService.IsConnected || _kickService.IsConnected || _youtubeService.IsConnected || IsAnyDiscordConnected)
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
		IsTwitchConnected = _twitchService.IsConnected;
		if (_twitchService.IsConnected)
		{
			_logger.LogInformation("Adding Twitch to connected platforms. Channel: {Channel}, Bot: {Bot}", _twitchService.Channel, _twitchService.BotUsername);
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
		IsDiscordConnected = IsAnyDiscordConnected;
		if (IsDiscordConnected)
		{
			IDiscordService primary = _discordBotManager.Primary;
			string text = primary?.BotUsername ?? _discordService.BotUsername;
			_logger.LogInformation("Adding Discord to connected platforms. Primary bot: {Bot} (manager bots: {ManagedCount}), ChannelId: {ChannelId}", text, _discordBotManager.Connected.Count, primary?.SelectedChannelId ?? _discordService.SelectedChannelId);
			ConnectedPlatforms.Add(new PlatformOption
			{
				Name = "Discord",
				Icon = "Discord",
				Color = "#5865F2"
			});
		}
		_logger.LogInformation("Total connected platforms: {Count}", ConnectedPlatforms.Count);
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
		PostChatStateSystemMessages(_twitchService.IsConnected, _twitchService.Channel, _kickService.IsConnected, _kickService.Channel, IsDiscordConnected, !string.IsNullOrEmpty(_discordBotManager.Primary?.SelectedChannelId ?? _discordService.SelectedChannelId));
	}

	private async Task OnTwitchChatMessageReceivedAsync(TwitchChatMessageEvent evt)
	{
		try
		{
			_logger.LogInformation("\ud83d\udce8 ChatViewModel received Twitch message from {Username}: {Message}", evt.Username, evt.Message);
			string messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
			lock (_recentMessagesLock)
			{
				if (_recentTwitchMessages.TryGetValue(messageKey, out var sentTime))
				{
					TimeSpan age = DateTime.Now - sentTime;
					if (age.TotalSeconds < 10.0)
					{
						if (IsOwnTwitchAccount(evt.Username))
						{
							_logger.LogInformation("\ud83d\udd07 Ignoring echo of recently sent Twitch message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
							return;
						}
					}
					else
					{
						_recentTwitchMessages.Remove(messageKey);
					}
				}
				List<string> expiredKeys = _recentTwitchMessages.Where(delegate(KeyValuePair<string, DateTime> kvp)
				{
					DateTime now = DateTime.Now;
					KeyValuePair<string, DateTime> keyValuePair = kvp;
					return (now - keyValuePair.Value).TotalSeconds > 10.0;
				}).Select(delegate(KeyValuePair<string, DateTime> kvp)
				{
					KeyValuePair<string, DateTime> keyValuePair = kvp;
					return keyValuePair.Key;
				}).ToList();
				foreach (string key in expiredKeys)
				{
					_recentTwitchMessages.Remove(key);
				}
			}
			if (!TwitchFilterEnabled)
			{
				_logger.LogDebug("Twitch messages filtered out (TwitchFilterEnabled=false)");
				return;
			}
			if (ConnectedPlatforms.Count == 0)
			{
				_logger.LogInformation("Platforms list is empty, refreshing...");
				await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
				{
					UpdateConnectedPlatforms();
				});
			}
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				ChatMessage chatMessage = new ChatMessage
				{
					Platform = (IsOwnTwitchAccount(evt.Username) ? "Vyral" : "Twitch"),
					Username = (evt.DisplayName ?? evt.Username ?? string.Empty),
					Message = (evt.Message ?? string.Empty),
					Timestamp = evt.Timestamp.ToLocalTime(),
					Color = (evt.Color ?? "#FFFFFF"),
					IsModerator = evt.IsModerator,
					IsSubscriber = evt.IsSubscriber,
					IsVip = evt.IsVip,
					IsBroadcaster = evt.IsBroadcaster,
					Badges = (evt.Badges ?? new Dictionary<string, string>())
				};
				chatMessage.InitializeBadges();
				Messages.Add(chatMessage);
				_logger.LogInformation("? Twitch message added to UI. Total messages: {Count}", Messages.Count);
				while (Messages.Count > 500)
				{
					Messages.RemoveAt(0);
				}
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogError(ex4, "? Error handling Twitch chat message from {Username}", evt?.Username ?? "unknown");
		}
	}

	private async Task OnChatMessageReceivedAsync(ChatMessageReceivedEvent evt)
	{
		bool flag = evt == null || (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase) && !TwitchFilterEnabled) || (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase) && !KickFilterEnabled) || (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase) && !YoutubeFilterEnabled) || (string.Equals(evt.Platform, "TikTok", StringComparison.OrdinalIgnoreCase) && !TiktokFilterEnabled) || (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase) && !DiscordFilterEnabled);
		bool flag2 = flag;
		if (!flag2)
		{
			flag2 = await IsEchoOfOwnRecentMessageAsync(evt);
		}
		if (flag2 || !TryMarkMessageProcessed(evt))
		{
			return;
		}
		string channel = ResolveChannelName(evt);
		string discordBotId = ResolveDiscordBotOrigin(evt);
		string discordDmChannelId = null;
		string discordDmBotId = null;
		(string ChannelId, string? BotId)? discordDmTarget = ResolveDiscordDmReplyTarget(evt);
		if (discordDmTarget.HasValue)
		{
			discordDmChannelId = discordDmTarget.Value.ChannelId;
			discordDmBotId = discordDmTarget.Value.BotId;
		}
		bool isDiscordDmMessage = discordDmChannelId != null || (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase) && string.Equals(evt.Channel, "DirectMessage", StringComparison.OrdinalIgnoreCase));
		if (isDiscordDmMessage)
		{
			string dmEntryOriginBotId = discordDmBotId ?? discordBotId;
			_logger.LogInformation("Discord DM from {Username} via bot {OriginId}: \"{Content}\"", evt.Username, string.IsNullOrEmpty(dmEntryOriginBotId) ? "unknown" : dmEntryOriginBotId, TruncateForLog(evt.Message, 80));
		}
		try
		{
			_responseWaiters?.Feed(evt.Platform ?? string.Empty, evt.Username ?? string.Empty, channel ?? string.Empty, evt.Message ?? string.Empty);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Response waiter feed failed for {Platform}/{Username}", evt.Platform, evt.Username);
		}
		if (isDiscordDmMessage && _linkCodeService != null && IsDiscordLinkIntakeMessage(evt.Message, out string linkIntakeCode))
		{
			_logger.LogInformation("Discord DM link intake matched for {Username}: \"{Message}\" ({Kind})", evt.Username, evt.Message, (linkIntakeCode != null) ? "code redemption" : "link request");
			string linkIntakeOriginBotId = NormalizeDiscordOriginBotId(discordDmBotId ?? discordBotId);
			string linkingBotId = await GetLinkingBotIdAsync();
			if (linkingBotId == null || linkIntakeOriginBotId == null || string.Equals(linkingBotId, linkIntakeOriginBotId, StringComparison.Ordinal))
			{
				if (string.IsNullOrEmpty(discordDmChannelId))
				{
					_logger.LogWarning("Discord DM link intake for {Username} dropped: DM channel not resolved yet", evt.Username);
				}
				else
				{
					await HandleDiscordDmLinkIntakeAsync(evt, discordDmChannelId, discordDmBotId ?? discordBotId, linkIntakeCode);
				}
				return;
			}
			_logger.LogInformation("DM link intake from {Username} skipped - received by bot '{OriginId}', linking bot is '{LinkingBotId}'", evt.Username, linkIntakeOriginBotId, linkingBotId);
		}
		if (isDiscordDmMessage && _linkCodeService != null && IsDiscordDelinkIntakeMessage(evt.Message, out string delinkArg))
		{
			_logger.LogInformation("Discord DM delink intake matched for {Username}: \"{Message}\"", evt.Username, evt.Message);
			string delinkIntakeOriginBotId = NormalizeDiscordOriginBotId(discordDmBotId ?? discordBotId);
			string linkingBotId2 = await GetLinkingBotIdAsync();
			if (linkingBotId2 == null || delinkIntakeOriginBotId == null || string.Equals(linkingBotId2, delinkIntakeOriginBotId, StringComparison.Ordinal))
			{
				if (string.IsNullOrEmpty(discordDmChannelId))
				{
					_logger.LogWarning("Discord DM delink intake for {Username} dropped: DM channel not resolved yet", evt.Username);
				}
				else
				{
					await HandleDiscordDmDelinkIntakeAsync(evt, discordDmChannelId, discordDmBotId ?? discordBotId, delinkArg);
				}
				return;
			}
			_logger.LogInformation("DM delink intake from {Username} skipped - received by bot '{OriginId}', linking bot is '{LinkingBotId}'", evt.Username, delinkIntakeOriginBotId, linkingBotId2);
		}
		if (isDiscordDmMessage && _linkCodeService != null && !string.IsNullOrWhiteSpace(evt.Message) && !evt.Message.TrimStart().StartsWith("!", StringComparison.Ordinal))
		{
			string hintOriginBotId = NormalizeDiscordOriginBotId(discordDmBotId ?? discordBotId);
			string hintLinkingBotId = await GetLinkingBotIdAsync();
			if ((hintLinkingBotId == null || hintOriginBotId == null || string.Equals(hintLinkingBotId, hintOriginBotId, StringComparison.Ordinal)) && !string.IsNullOrEmpty(discordDmChannelId) && TryClaimDmHint(evt.Username))
			{
				await SendDiscordDmAsync(discordDmChannelId, discordDmBotId ?? discordBotId, "\ud83e\udd16 I connect your accounts across platforms! DM me 'link' to get a code, then type '!link <code>' in the stream chat. 'delink <platform>' removes a link, '!linked' shows your status.", evt.Username);
			}
		}
		if (_workflowTriggerService != null && _workflowTriggerService.TryHandleCommand(evt.Platform, evt.Username, evt.Message ?? string.Empty, channel ?? string.Empty, discordBotId, async delegate(string text)
		{
			if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
			{
				await _twitchService.SendMessageAsync(text);
			}
			else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
			{
				await _kickService.SendMessageAsync(text);
			}
			else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
			{
				await _youtubeService.SendMessageAsync(text);
			}
			else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
			{
				await SendDiscordReplyAsync(text, discordBotId, discordDmChannelId, evt.Username);
			}
		}, BuildWorkflowRoleList(evt.IsModerator, evt.IsBroadcaster, evt.IsVip, evt.IsSubscriber)))
		{
			return;
		}
		if (_commandService != null && !string.IsNullOrEmpty(evt.Message) && _commandService.IsCommand(evt.Message))
		{
			try
			{
				int? currencyBalance = null;
				if (_currencyService != null)
				{
					try
					{
						currencyBalance = await _currencyService.GetBalanceAsync(evt.Username, evt.Platform);
					}
					catch (Exception ex)
					{
						Exception ex2 = ex;
						Exception ex3 = ex2;
						Exception ex4 = ex3;
						_logger.LogWarning(ex4, "Failed to get currency balance for command context");
					}
				}
				CommandContext context = new CommandContext
				{
					Username = evt.Username,
					Platform = evt.Platform,
					Channel = channel,
					IsModerator = evt.IsModerator,
					IsSubscriber = evt.IsSubscriber,
					IsVIP = evt.IsVip,
					IsBroadcaster = (evt.IsBroadcaster || string.Equals(evt.Username, channel, StringComparison.OrdinalIgnoreCase)),
					CurrencyBalance = currencyBalance.GetValueOrDefault(),
					CustomVariables = new Dictionary<string, string>(),
					DiscordBotId = discordBotId
				};
				string response = await _commandService.ExecuteAsync(evt.Message, context, discordBotId);
				if (response?.StartsWith("$ACTIONS$", StringComparison.Ordinal) ?? false)
				{
					response = await RunCommandActionsAsync(response, context);
				}
				if (response != null)
				{
					if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
					{
						await _twitchService.SendMessageAsync(response);
					}
					else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
					{
						await _kickService.SendMessageAsync(response);
					}
					else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
					{
						await _youtubeService.SendMessageAsync(response);
					}
					else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						await SendDiscordReplyAsync(response, discordBotId, discordDmChannelId, evt.Username);
					}
					_logger.LogInformation("Processed unified command: {Command} -> {Response}", evt.Message, response);
					return;
				}
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Error processing unified command: {Command}", evt.Message);
			}
		}
		if (_currencyCommandService != null && evt.Message.StartsWith("!"))
		{
			try
			{
				string response2 = await _currencyCommandService.ProcessCommandAsync(evt.Message, evt.Username, evt.Platform, evt.IsModerator);
				if (response2 != null)
				{
					if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
					{
						await _twitchService.SendMessageAsync(response2);
					}
					else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
					{
						await _kickService.SendMessageAsync(response2);
					}
					else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
					{
						await _youtubeService.SendMessageAsync(response2);
					}
					else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						await SendDiscordReplyAsync(response2, discordBotId, discordDmChannelId, evt.Username);
					}
					_logger.LogInformation("Processed currency command: {Command} -> {Response}", evt.Message, response2);
				}
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Error processing currency command: {Command}", evt.Message);
			}
		}
		bool isFollower = false;
		int watchtimeMinutes = 0;
		if (evt.Message.StartsWith("!"))
		{
			if (_watchtimeService != null)
			{
				try
				{
					watchtimeMinutes = (await _watchtimeService.GetUserWatchtimeAsync(evt.Username, evt.Platform))?.TotalMinutes ?? 0;
				}
				catch (Exception ex)
				{
					Exception ex11 = ex;
					Exception ex12 = ex11;
					Exception ex13 = ex12;
					_logger.LogWarning(ex13, "Failed to look up watchtime for {Username} ({Platform})", evt.Username, evt.Platform);
				}
			}
			try
			{
				isFollower = await IsFollowerForEligibilityAsync(evt.Platform, evt.Username);
			}
			catch (Exception exception2)
			{
				_logger.LogWarning(exception2, "Failed to resolve follower status for {Username} ({Platform})", evt.Username, evt.Platform);
			}
		}
		if (_giveawayCommandService != null && evt.Message.StartsWith("!"))
		{
			try
			{
				string response3 = await _giveawayCommandService.ProcessCommandAsync(evt.Message, evt.Username, evt.Platform, evt.IsModerator, evt.IsSubscriber, isFollower, watchtimeMinutes);
				if (response3 != null)
				{
					if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
					{
						await _twitchService.SendMessageAsync(response3);
					}
					else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
					{
						await _kickService.SendMessageAsync(response3);
					}
					else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
					{
						await _youtubeService.SendMessageAsync(response3);
					}
					else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						await SendDiscordReplyAsync(response3, discordBotId, discordDmChannelId, evt.Username);
					}
					_logger.LogInformation("Processed giveaway command: {Command} -> {Response}", evt.Message, response3);
				}
			}
			catch (Exception ex)
			{
				Exception ex14 = ex;
				Exception ex15 = ex14;
				Exception ex16 = ex15;
				_logger.LogError(ex16, "Error processing giveaway command: {Command}", evt.Message);
			}
		}
		if (_pollCommandService != null && evt.Message.StartsWith("!poll"))
		{
			try
			{
				string response4 = await _pollCommandService.ProcessCommandAsync(evt.Message, evt.Username, evt.Platform, evt.IsModerator, evt.IsSubscriber, isFollower, watchtimeMinutes);
				if (response4 != null)
				{
					if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
					{
						await _twitchService.SendMessageAsync(response4);
					}
					else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
					{
						await _kickService.SendMessageAsync(response4);
					}
					else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
					{
						await _youtubeService.SendMessageAsync(response4);
					}
					else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						await SendDiscordReplyAsync(response4, discordBotId, discordDmChannelId, evt.Username);
					}
					_logger.LogInformation("Processed poll command: {Command} -> {Response}", evt.Message, response4);
				}
			}
			catch (Exception ex)
			{
				Exception ex17 = ex;
				Exception ex18 = ex17;
				Exception ex19 = ex18;
				_logger.LogError(ex19, "Error processing poll command: {Command}", evt.Message);
			}
		}
		if (_watchtimeCommandService != null && evt.Message.StartsWith("!watchtime"))
		{
			try
			{
				string response5 = await _watchtimeCommandService.ProcessCommandAsync(evt.Message, evt.Username, evt.Platform);
				if (response5 != null)
				{
					if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
					{
						await _twitchService.SendMessageAsync(response5);
					}
					else if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
					{
						await _kickService.SendMessageAsync(response5);
					}
					else if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
					{
						await _youtubeService.SendMessageAsync(response5);
					}
					else if (string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						await SendDiscordReplyAsync(response5, discordBotId, discordDmChannelId, evt.Username);
					}
					_logger.LogInformation("Processed watchtime command: {Command} -> {Response}", evt.Message, response5);
				}
			}
			catch (Exception ex)
			{
				Exception ex20 = ex;
				Exception ex21 = ex20;
				Exception ex22 = ex21;
				_logger.LogError(ex22, "Error processing watchtime command: {Command}", evt.Message);
			}
		}
		if (_watchtimeService != null)
		{
			try
			{
				await _watchtimeService.RecordUserActivityAsync(evt.Username, evt.Platform);
			}
			catch (Exception ex)
			{
				Exception ex23 = ex;
				Exception ex24 = ex23;
				Exception ex25 = ex24;
				_logger.LogWarning(ex25, "Error recording watchtime for {Username}", evt.Username);
			}
		}
		if (_currencyService != null && !evt.Message.StartsWith("!"))
		{
			try
			{
				await _currencyService.AwardChatActivityAsync(evt.Username, evt.Platform);
			}
			catch (Exception ex)
			{
				Exception ex26 = ex;
				Exception ex27 = ex26;
				Exception ex28 = ex27;
				_logger.LogError(ex28, "Failed to award chat activity currency to {Username}", evt.Username);
			}
		}
		if (_currencyService == null)
		{
			return;
		}
		try
		{
			await _currencyService.AwardFirstChatBonusAsync(evt.Username, evt.Platform);
		}
		catch (Exception exception3)
		{
			_logger.LogWarning(exception3, "Failed to award first-chat bonus to {Username}", evt.Username);
		}
	}

	private async Task<string?> GetLinkingBotIdAsync()
	{
		DateTime now = DateTime.UtcNow;
		if (now - _linkingBotIdCacheAtUtc < LinkingBotIdCacheTtl)
		{
			return _linkingBotIdCache;
		}
		string resolved = null;
		try
		{
			resolved = await _discordBotManager.ResolveBotIdForRoleAsync("Linking");
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to resolve the Discord linking bot (role {Role})", "Linking");
		}
		_linkingBotIdCache = resolved;
		_linkingBotIdCacheAtUtc = now;
		return resolved;
	}

	private string? NormalizeDiscordOriginBotId(string? originBotId)
	{
		if (string.IsNullOrWhiteSpace(originBotId))
		{
			return null;
		}
		if (string.Equals(originBotId, "primary", StringComparison.OrdinalIgnoreCase))
		{
			return _discordBotManager.Primary?.BotId;
		}
		return originBotId;
	}

	private static string TruncateForLog(string? content, int maxLength)
	{
		string text = content ?? string.Empty;
		return (text.Length <= maxLength) ? text : (text.Substring(0, maxLength) + "...");
	}

	private static bool IsDiscordLinkIntakeMessage(string? message, out string? code)
	{
		code = null;
		string text = (message ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return false;
		}
		if (text.Equals("link", StringComparison.OrdinalIgnoreCase) || text.Equals("!link", StringComparison.OrdinalIgnoreCase) || text.Equals("connect", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (text.Length == 6 && text.All(char.IsDigit))
		{
			code = text;
			return true;
		}
		return false;
	}

	private static bool IsDiscordDelinkIntakeMessage(string? message, out string arg)
	{
		arg = string.Empty;
		string text = (message ?? string.Empty).Trim();
		string text2;
		if (text.Equals("delink", StringComparison.OrdinalIgnoreCase) || text.Equals("!delink", StringComparison.OrdinalIgnoreCase))
		{
			text2 = string.Empty;
		}
		else if (text.StartsWith("!delink ", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text.Substring("!delink ".Length);
		}
		else
		{
			if (!text.StartsWith("delink ", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			text2 = text.Substring("delink ".Length);
		}
		arg = text2.Trim();
		return true;
	}

	private async Task HandleDiscordDmLinkIntakeAsync(ChatMessageReceivedEvent evt, string dmChannelId, string? botId, string? code)
	{
		try
		{
			bool flag = _dataService != null;
			bool flag2 = flag;
			bool enabled = false;
			if (flag2)
			{
				flag2 = bool.TryParse(await _dataService.GetConfigValueAsync("Linking.Enabled"), out enabled);
			}
			if (flag2 && !enabled)
			{
				await SendDiscordDmAsync(dmChannelId, botId, "\ud83d\udd17 Linking is currently disabled.", evt.Username);
				return;
			}
			string reply;
			if (code == null)
			{
				(string Code, DateTime ExpiresAt) tuple = await _linkCodeService.GenerateCodeAsync(evt.Username, "Discord");
				string generatedCode = tuple.Code;
				DateTime expiresAt = tuple.ExpiresAt;
				int minutes = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalMinutes));
				reply = $"\ud83d\udd17 Your link code: **{generatedCode}** (expires in {minutes} min). Send it from another platform:\n```\n!link {generatedCode}\n```\nin the other platform's chat.";
			}
			else
			{
				reply = (await _linkCodeService.RedeemCodeAsync(code, evt.Username, "Discord")).Message;
			}
			await SendDiscordDmAsync(dmChannelId, botId, reply, evt.Username);
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogWarning("Discord DM link intake rejected for {Username}: {Reason}", evt.Username, ex.Message);
			await SendDiscordDmAsync(dmChannelId, botId, "\ud83d\udd17 " + ex.Message, evt.Username);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed Discord DM link intake for {Username}: {Message}", evt.Username, evt.Message);
			try
			{
				await SendDiscordDmAsync(dmChannelId, botId, "?\ufe0f Couldn't process that - try again", evt.Username);
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				Exception sendEx = ex4;
				_logger.LogWarning(sendEx, "Failed to send the DM link-intake error reply");
			}
		}
	}

	private async Task HandleDiscordDmDelinkIntakeAsync(ChatMessageReceivedEvent evt, string dmChannelId, string? botId, string arg)
	{
		try
		{
			bool flag = _dataService != null;
			bool flag2 = flag;
			bool enabled = false;
			if (flag2)
			{
				flag2 = bool.TryParse(await _dataService.GetConfigValueAsync("Linking.Enabled"), out enabled);
			}
			if (flag2 && !enabled)
			{
				await SendDiscordDmAsync(dmChannelId, botId, "\ud83d\udd17 Linking is currently disabled.", evt.Username);
				return;
			}
			string reply;
			if (arg.Length == 0)
			{
				reply = "\ud83d\udd17 Usage: DM 'delink <platform>' to unlink (e.g. 'delink Twitch'), then type '!delink <code>' on that platform within the code's validity to confirm.";
			}
			else if (arg.Length == 6 && arg.All(char.IsDigit))
			{
				reply = (await _linkCodeService.RedeemDelinkCodeAsync(arg, evt.Username, "Discord")).Message;
			}
			else
			{
				(string Code, DateTime ExpiresAt) tuple = await _linkCodeService.GenerateDelinkCodeAsync(evt.Username, "Discord", arg);
				string delinkCode = tuple.Code;
				DateTime expiresAt = tuple.ExpiresAt;
				int minutes = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalMinutes));
				reply = $"\ud83d\udd17 Your delink code: {delinkCode}. Now type '!delink {delinkCode}' on {arg} within {minutes} minutes.";
			}
			await SendDiscordDmAsync(dmChannelId, botId, reply, evt.Username);
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogWarning("Discord DM delink intake rejected for {Username}: {Reason}", evt.Username, ex.Message);
			await SendDiscordDmAsync(dmChannelId, botId, "?\ufe0f " + ex.Message, evt.Username);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed Discord DM delink intake for {Username}: {Message}", evt.Username, evt.Message);
			try
			{
				await SendDiscordDmAsync(dmChannelId, botId, "?\ufe0f Couldn't process that - try again", evt.Username);
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				Exception sendEx = ex4;
				_logger.LogWarning(sendEx, "Failed to send the DM delink-intake error reply");
			}
		}
	}

	private static bool TryClaimDmHint(string? username)
	{
		if (string.IsNullOrWhiteSpace(username))
		{
			return false;
		}
		string key = username.Trim();
		DateTime utcNow = DateTime.UtcNow;
		if (DmHintLastSent.TryGetValue(key, out var value) && utcNow - value < DmHintThrottle)
		{
			return false;
		}
		DmHintLastSent[key] = utcNow;
		return true;
	}

	private async Task SendDiscordDmAsync(string dmChannelId, string? botId, string content, string? username = null)
	{
		IDiscordService bot = ResolveDiscordDmSendTarget(botId);
		if (bot == null || !bot.IsConnected)
		{
			_logger.LogWarning("Discord DM send skipped (no connected bot): channel {ChannelId}", dmChannelId);
			return;
		}
		// Same fallback chain as the inbox reply: channel id, then the
		// participant's remembered Discord user id, then username resolution.
		DmConversation? existingConversation = string.IsNullOrEmpty(username)
			? null
			: DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase));
		string? userId = existingConversation?.UserId;
		bool delivered = false;
		try
		{
			if (!string.IsNullOrEmpty(dmChannelId))
			{
				delivered = await bot.SendDmToChannelAsync(dmChannelId, content);
			}
			if (!delivered && !string.IsNullOrEmpty(userId))
			{
				(bool byUserId, string? userIdError) = await bot.SendDmToUserAsync(userId, content);
				delivered = byUserId;
				if (!byUserId)
				{
					_logger.LogWarning("DM send by user id {UserId} failed: {Error}", userId, userIdError ?? "unknown error");
				}
			}
			if (!delivered && !string.IsNullOrEmpty(username))
			{
				delivered = await bot.SendDmAsync(username, content, null);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Discord DM send threw (channel {ChannelId}, user {Username})", dmChannelId, username);
			delivered = false;
		}
		if (!delivered)
		{
			_logger.LogWarning("Failed to deliver DM reply to {Username} (channel {ChannelId})", string.IsNullOrEmpty(username) ? "unknown" : username, dmChannelId);
			return;
		}
		if (string.IsNullOrEmpty(username))
		{
			return;
		}
		_dataService?.SaveDiscordDmMessageAsync(new DiscordDmMessage
		{
			BotId = (botId ?? string.Empty),
			BotUsername = (bot.BotUsername ?? "Bot"),
			ChannelId = dmChannelId,
			Username = (username ?? "Unknown"),
			UserId = userId,
			Direction = "Outgoing",
			Content = content,
			Timestamp = DateTime.UtcNow
		});
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			DmConversation dmConversation = DmConversations.FirstOrDefault((DmConversation c) => string.Equals(c.Username, username, StringComparison.OrdinalIgnoreCase));
			if (dmConversation != null)
			{
				ChatMessage chatMessage = new ChatMessage
				{
					Platform = "Vyral",
					Username = (bot.BotUsername ?? "Bot"),
					Message = content,
					Timestamp = DateTime.Now,
					Color = "#5865F2",
					IsModerator = false,
					IsSubscriber = false,
					IsVip = false,
					IsBroadcaster = true,
					Badges = new Dictionary<string, string> { { "broadcaster", "1" } }
				};
				chatMessage.InitializeBadges();
				dmConversation.Messages.Add(chatMessage);
				dmConversation.LastPreview = content;
				dmConversation.LastAt = DateTime.Now;

			}
		});
	}

	private static string BuildWorkflowRoleList(bool isModerator, bool isBroadcaster, bool isVip, bool isSubscriber)
	{
		List<string> list = new List<string>(4);
		if (isBroadcaster)
		{
			list.Add("broadcaster");
		}
		if (isModerator)
		{
			list.Add("moderator");
		}
		if (isVip)
		{
			list.Add("vip");
		}
		if (isSubscriber)
		{
			list.Add("subscriber");
		}
		return string.Join(",", list);
	}

	private string ResolveChannelName(ChatMessageReceivedEvent evt)
	{
		if (string.Equals(evt.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
		{
			return _twitchService.Channel ?? evt.Channel;
		}
		if (string.Equals(evt.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
		{
			return _kickService.Channel ?? evt.Channel;
		}
		if (string.Equals(evt.Platform, "YouTube", StringComparison.OrdinalIgnoreCase))
		{
			return _youtubeService.Channel ?? evt.Channel;
		}
		return evt.Channel;
	}

	private async Task<bool> IsEchoOfOwnRecentMessageAsync(ChatMessageReceivedEvent evt)
	{
		string messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
		if (messageKey.Length == 0)
		{
			return false;
		}
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
			echoWindowSeconds = 15;
		}
		else
		{
			if (!string.Equals(evt.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			isOwnAccount = IsOwnDiscordAccount(evt.Username);
			echoWindowSeconds = 10;
		}
		if (!isOwnAccount)
		{
			return false;
		}
		lock (_recentMessagesLock)
		{
			string text = evt.Platform.ToLowerInvariant();
			if (1 == 0)
			{
			}
			Dictionary<string, DateTime> dictionary = text switch
			{
				"twitch" => _recentTwitchMessages, 
				"kick" => _recentKickMessages, 
				"youtube" => _recentYoutubeMessages, 
				"discord" => _recentDiscordMessages, 
				_ => _recentTwitchMessages, 
			};
			if (1 == 0)
			{
			}
			Dictionary<string, DateTime> dictionary2 = dictionary;
			Dictionary<string, DateTime> dictionary3 = dictionary2;
			Dictionary<string, DateTime> recent = dictionary3;
			if (recent.TryGetValue(messageKey, out var sentTime))
			{
				return (DateTime.Now - sentTime).TotalSeconds < (double)echoWindowSeconds;
			}
		}
		return false;
	}

	private bool IsOwnTwitchAccount(string? username)
	{
		return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(_twitchService.BotUsername) && string.Equals(username, _twitchService.BotUsername, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<bool> IsOwnKickAccountAsync(string? username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return false;
		}
		string senderName = (await GetCachedPlatformRowAsync("Kick"))?.Username ?? _kickService.BotUsername;
		return !string.IsNullOrEmpty(senderName) && string.Equals(username, senderName, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<bool> IsOwnYouTubeAccountAsync(string? username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return false;
		}
		string senderName = (await GetCachedPlatformRowAsync("YouTube"))?.Username ?? _youtubeService.Channel;
		return !string.IsNullOrEmpty(senderName) && string.Equals(username, senderName, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsOwnDiscordAccount(string? username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return false;
		}
		foreach (IDiscordService item in _discordBotManager.Connected)
		{
			if (!string.IsNullOrEmpty(item.BotUsername) && string.Equals(username, item.BotUsername, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return !string.IsNullOrEmpty(_discordService.BotUsername) && string.Equals(username, _discordService.BotUsername, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<Platform?> GetCachedPlatformRowAsync(string platformName)
	{
		if (_dataService == null)
		{
			return null;
		}
		lock (_platformRowsLock)
		{
			if (DateTime.UtcNow - _platformRowsLoadedAt < PlatformRowCacheTtl)
			{
				if (1 == 0)
				{
				}
				Platform result = platformName switch
				{
					"Twitch" => _twitchPlatformRow, 
					"Kick" => _kickPlatformRow, 
					"YouTube" => _youtubePlatformRow, 
					_ => null, 
				};
				if (1 == 0)
				{
				}
				return result;
			}
		}
		Platform twitchRow = null;
		Platform kickRow = null;
		Platform youtubeRow = null;
		try
		{
			twitchRow = await _dataService.GetPlatformByNameAsync("Twitch");
			kickRow = await _dataService.GetPlatformByNameAsync("Kick");
			youtubeRow = await _dataService.GetPlatformByNameAsync("YouTube");
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to load platform rows for echo/follower checks");
		}
		lock (_platformRowsLock)
		{
			_twitchPlatformRow = twitchRow;
			_kickPlatformRow = kickRow;
			_youtubePlatformRow = youtubeRow;
			_platformRowsLoadedAt = DateTime.UtcNow;
			if (1 == 0)
			{
			}
			Platform result = platformName switch
			{
				"Twitch" => twitchRow, 
				"Kick" => kickRow, 
				"YouTube" => youtubeRow, 
				_ => null, 
			};
			if (1 == 0)
			{
			}
			return result;
		}
	}

	private bool TryMarkMessageProcessed(ChatMessageReceivedEvent evt)
	{
		string text = ((!string.IsNullOrEmpty(evt.MessageId)) ? evt.MessageId : $"{evt.Platform}:{evt.Username}:{evt.Timestamp.Ticks}:{evt.Message?.GetHashCode()}");
		lock (_processedMessageIdsLock)
		{
			if (!_processedMessageIds.Add(text))
			{
				_logger.LogDebug("Skipping duplicate chat message event (key: {Key})", text);
				return false;
			}
			_processedMessageIdOrder.Enqueue(text);
			while (_processedMessageIdOrder.Count > 1000)
			{
				string item = _processedMessageIdOrder.Dequeue();
				_processedMessageIds.Remove(item);
			}
			return true;
		}
	}

	private async Task<bool> IsFollowerForEligibilityAsync(string platform, string username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return false;
		}
		if (string.Equals(platform, "Twitch", StringComparison.OrdinalIgnoreCase))
		{
			return await IsTwitchFollowerAsync(username);
		}
		return false;
	}

	private async Task<bool> IsTwitchFollowerAsync(string username)
	{
		if (_dataService == null || !_twitchService.IsConnected)
		{
			return false;
		}
		string cacheKey = "twitch:" + username.ToLowerInvariant();
		lock (_followerCacheLock)
		{
			if (_followerCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
			{
				return cached.IsFollower;
			}
		}
		Platform platform = await GetCachedPlatformRowAsync("Twitch");
		string broadcasterId = platform?.UserId;
		string clientId = platform?.ClientId;
		string encryptedToken = platform?.AccessToken;
		if (string.IsNullOrEmpty(broadcasterId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(encryptedToken))
		{
			return false;
		}
		string accessToken;
		try
		{
			accessToken = TwitchOAuthService.Decrypt(encryptedToken);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to decrypt Twitch access token for follower lookup");
			return false;
		}
		try
		{
			string userId = null;
			using (HttpRequestMessage userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users?login=" + Uri.EscapeDataString(username)))
			{
				userRequest.Headers.Add("Client-Id", clientId);
				userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
				using HttpResponseMessage userResponse = await _helixHttpClient.SendAsync(userRequest);
				if (!userResponse.IsSuccessStatusCode)
				{
					_logger.LogDebug("Twitch Helix users lookup failed for {Username}: {Status}", username, userResponse.StatusCode);
					return false;
				}
				using JsonDocument userDoc = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
				if (userDoc.RootElement.TryGetProperty("data", out var userData) && userData.GetArrayLength() > 0 && userData[0].TryGetProperty("id", out var idProp))
				{
					userId = idProp.GetString();
				}
			}
			if (string.IsNullOrEmpty(userId))
			{
				return false;
			}
			using HttpRequestMessage followRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/channels/followers?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) + "&user_id=" + Uri.EscapeDataString(userId));
			followRequest.Headers.Add("Client-Id", clientId);
			followRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			using HttpResponseMessage followResponse = await _helixHttpClient.SendAsync(followRequest);
			if (!followResponse.IsSuccessStatusCode)
			{
				_logger.LogDebug("Twitch Helix followers lookup failed for {Username}: {Status} (token may lack moderator:read:followers)", username, followResponse.StatusCode);
				return false;
			}
			using JsonDocument followDoc = JsonDocument.Parse(await followResponse.Content.ReadAsStringAsync());
			JsonElement totalProp;
			bool isFollower = followDoc.RootElement.TryGetProperty("total", out totalProp) && totalProp.GetInt32() > 0;
			lock (_followerCacheLock)
			{
				_followerCache[cacheKey] = (isFollower, DateTime.UtcNow.Add(FollowerCacheTtl));
			}
			return isFollower;
		}
		catch (Exception exception2)
		{
			_logger.LogWarning(exception2, "Twitch follower lookup failed for {Username}", username);
			return false;
		}
	}

	private async Task SaveEventStatisticAsync(string platform, string eventType, string username, Dictionary<string, string> variables, bool wasAutoReplied)
	{
		if (_dataService == null)
		{
			return;
		}
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
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to persist event statistic for {Platform}/{EventType}", platform, eventType);
		}
	}

	private async Task<string> BuildAutoReplyTextAsync(string platform, string eventType, string username, string template, Dictionary<string, string> variables)
	{
		string effectiveTemplate = template;
		try
		{
			Dictionary<string, object> ruleContext = variables.ToDictionary<KeyValuePair<string, string>, string, object>((KeyValuePair<string, string> kv) => kv.Key, (KeyValuePair<string, string> kv) => kv.Value, StringComparer.OrdinalIgnoreCase);
			string conditionalReply = await _autoReplyService.GetAutoReplyAsync(username, platform, eventType, ruleContext);
			if (!string.IsNullOrEmpty(conditionalReply))
			{
				effectiveTemplate = conditionalReply;
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogDebug(ex4, "Conditional auto-reply lookup failed for {Platform}/{EventType}", platform, eventType);
		}
		string reply = _autoReplyService.ProcessTemplate(effectiveTemplate, variables);
		if (_variableReplacementService != null)
		{
			try
			{
				reply = await _variableReplacementService.ReplaceVariablesAsync(reply, variables, platform);
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogWarning(ex7, "Variable replacement failed for auto-reply {Platform}/{EventType}", platform, eventType);
			}
		}
		return reply;
	}

	private Task OnPlatformRoutingChangedAsync(PlatformChatRoutingChangedEvent evt)
	{
		switch (evt.Platform)
		{
		case "Twitch":
			TwitchFilterEnabled = evt.Enabled;
			ReloadTwitchAutoRepliesToggleAsync();
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

	private async Task ReloadTwitchAutoRepliesToggleAsync()
	{
		try
		{
			if (_dataService != null)
			{
				_twitchAutoRepliesEnabled = !bool.TryParse(await _dataService.GetConfigValueAsync("Chat.Twitch.AutoRepliesEnabled"), out var enabled) || enabled;
				_logger.LogInformation("Twitch auto-replies master toggle: {Enabled}", _twitchAutoRepliesEnabled);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogWarning(ex4, "Failed to load the Twitch auto-replies master toggle");
		}
	}

	private async Task OnTwitchStreamStatusAsync(TwitchStreamStatusEvent evt)
	{
		try
		{
			if (_dataService != null)
			{
				string value = (evt.IsLive ? DateTime.UtcNow.ToString("o") : string.Empty);
				await _dataService.SetConfigValueAsync("Stream.StartedAtUtc", value);
				_logger.LogInformation("Twitch stream {State}; Stream.StartedAtUtc={Value}", evt.IsLive ? "online" : "offline", evt.IsLive ? value : "(cleared)");
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogWarning(ex4, "Failed to persist Stream.StartedAtUtc");
		}
	}

	private async Task OnGiveawayAutoEndedAsync(GiveawayAutoEndedEvent evt)
	{
		_logger.LogInformation("Giveaway auto-ended by scheduler: {Title}", evt.Title);
		await SendSchedulerAnnouncementAsync(evt.AllowedPlatforms, evt.SummaryText, routeDiscordThroughGiveawayRole: true);
	}

	private async Task OnPollAutoEndedAsync(PollAutoEndedEvent evt)
	{
		_logger.LogInformation("Poll auto-ended by scheduler: {Question}", evt.Question);
		await SendSchedulerAnnouncementAsync(evt.AllowedPlatforms, evt.SummaryText);
	}

	private async Task SendSchedulerAnnouncementAsync(string allowedPlatforms, string message, bool routeDiscordThroughGiveawayRole = false)
	{
		IEnumerable<string> enumerable2;
		if (allowedPlatforms.Equals("All", StringComparison.OrdinalIgnoreCase))
		{
			IEnumerable<string> enumerable = new string[4] { "Twitch", "Kick", "YouTube", "Discord" };
			enumerable2 = enumerable;
		}
		else
		{
			IEnumerable<string> enumerable3 = allowedPlatforms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			enumerable2 = enumerable3;
		}
		IEnumerable<string> targets = enumerable2;
		foreach (string target in targets)
		{
			try
			{
				if (string.Equals(target, "Twitch", StringComparison.OrdinalIgnoreCase))
				{
					if (_twitchService.IsConnected)
					{
						await _twitchService.SendMessageAsync(message);
					}
					else
					{
						_logger.LogWarning("Twitch is not connected, skipping scheduler announcement");
					}
				}
				else if (string.Equals(target, "Kick", StringComparison.OrdinalIgnoreCase))
				{
					if (_kickService.IsConnected)
					{
						await _kickService.SendMessageAsync(message);
					}
					else
					{
						_logger.LogWarning("Kick is not connected, skipping scheduler announcement");
					}
				}
				else if (string.Equals(target, "YouTube", StringComparison.OrdinalIgnoreCase))
				{
					if (_youtubeService.IsConnected)
					{
						await _youtubeService.SendMessageAsync(message);
					}
					else
					{
						_logger.LogWarning("YouTube is not connected, skipping scheduler announcement");
					}
				}
				else
				{
					if (!string.Equals(target, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					string giveawayBotId = null;
					if (routeDiscordThroughGiveawayRole)
					{
						try
						{
							giveawayBotId = await _discordBotManager.ResolveBotIdForRoleAsync("GiveawayAnnouncements");
						}
						catch (Exception ex)
						{
							Exception ex2 = ex;
							Exception ex3 = ex2;
							Exception resolveEx = ex3;
							_logger.LogWarning(resolveEx, "Failed to resolve the Discord giveaway bot (role {Role})", "GiveawayAnnouncements");
						}
					}
					IDiscordService discordSend = ResolveDiscordSendTarget(giveawayBotId);
					if (discordSend?.IsConnected ?? false)
					{
						await discordSend.SendMessageAsync(message);
					}
					else
					{
						_logger.LogWarning("Discord is not connected, skipping scheduler announcement");
					}
					continue;
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to send scheduler announcement to {Platform}", target);
			}
		}
	}

	private async Task SendAnnouncementToConnectedPlatformsAsync(string message, bool routeDiscordThroughGiveawayRole = false)
	{
		string[] array = new string[4] { "Twitch", "Kick", "YouTube", "Discord" };
		string[] array2 = array;
		string[] array3 = array2;
		foreach (string target in array3)
		{
			try
			{
				if (string.Equals(target, "Twitch", StringComparison.OrdinalIgnoreCase))
				{
					if (_twitchService.IsConnected)
					{
						await _twitchService.SendMessageAsync(message);
					}
				}
				else if (string.Equals(target, "Kick", StringComparison.OrdinalIgnoreCase))
				{
					if (_kickService.IsConnected)
					{
						await _kickService.SendMessageAsync(message);
					}
				}
				else if (string.Equals(target, "YouTube", StringComparison.OrdinalIgnoreCase))
				{
					if (_youtubeService.IsConnected)
					{
						await _youtubeService.SendMessageAsync(message);
					}
				}
				else
				{
					if (!string.Equals(target, "Discord", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					string giveawayBotId = null;
					if (routeDiscordThroughGiveawayRole)
					{
						try
						{
							giveawayBotId = await _discordBotManager.ResolveBotIdForRoleAsync("GiveawayAnnouncements");
						}
						catch (Exception ex)
						{
							Exception ex2 = ex;
							Exception ex3 = ex2;
							Exception resolveEx = ex3;
							_logger.LogWarning(resolveEx, "Failed to resolve the Discord giveaway bot (role {Role})", "GiveawayAnnouncements");
						}
					}
					IDiscordService discordSend = ResolveDiscordSendTarget(giveawayBotId);
					if (discordSend?.IsConnected ?? false)
					{
						await discordSend.SendMessageAsync(message);
					}
					continue;
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to send linking announcement to {Platform}", target);
			}
		}
	}

	private async Task OnYouTubeMembershipStatusAsync(YouTubeMembershipEvent evt)
	{
		if (_viewerStatusService == null)
		{
			return;
		}
		try
		{
			await _viewerStatusService.RecordSubscriptionAsync(evt.Username, "YouTube", 1, 0);
			_logger.LogInformation("Recorded YouTube membership status for {Username} (Level: {Level}, Milestone: {IsMilestone})", evt.Username, evt.Level, evt.IsMilestone);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to record YouTube membership status for {Username}", evt.Username);
		}
	}

	private async Task OnAccountsLinkedAsync(AccountsLinkedEvent evt)
	{
		_logger.LogInformation("Accounts linked: {Username} on {Platform} now shares {Platforms}", evt.Username, evt.Platform, evt.LinkedPlatformNames);
		int platformCount = evt.LinkedPlatformNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
		await SendAnnouncementToConnectedPlatformsAsync($"\ud83d\udd17 {evt.Username} just linked {evt.Platform} - now on {platformCount} platforms!");
		if (_socialGoalService != null)
		{
			try
			{
				await _socialGoalService.EvaluateViewerAsync(evt.ViewerId, evt.Username, evt.Platform);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to evaluate social goals after link for {Username} ({ViewerId})", evt.Username, evt.ViewerId);
			}
		}
	}

	private async Task OnViewerGoalCompletedAsync(ViewerGoalCompletedEvent evt)
	{
		_logger.LogInformation("Viewer goal completed: {Username} -> {GoalName}", evt.Username, evt.GoalName);
		await SendAnnouncementToConnectedPlatformsAsync($"\ud83c\udfc6 {evt.Username} completed '{evt.GoalName}'! {evt.RewardDescription}");
	}

	private async Task OnGiveawayIntakeModeChangedAsync(GiveawayIntakeModeChangedEvent evt)
	{
		_logger.LogInformation("Giveaway intake mode changed to {Mode} ({EntriesPerMinute}/min) for giveaway {GiveawayId}", evt.Mode, evt.EntriesPerMinute, evt.GiveawayId);
		string message = ((evt.Mode == "Timed") ? "? High traffic - giveaway entries are being batched every few seconds." : "? Traffic normalized - giveaway entries are live again.");
		await SendAnnouncementToConnectedPlatformsAsync(message, routeDiscordThroughGiveawayRole: true);
	}

	private void PostChatStateSystemMessages(bool twitchConnected, string? twitchChannel, bool kickConnected, string? kickChannel, bool discordConnected, bool discordChannelSelected)
	{
		try
		{
			List<(string Platform, string Message)> pending = new List<(string, string)>();
			string text = (twitchConnected ? "connected" : "disconnected");
			if (text != _lastTwitchChatState)
			{
				pending.Add(twitchConnected ? ("Twitch", "Twitch chat connected - listening to #" + (string.IsNullOrWhiteSpace(twitchChannel) ? "your channel" : twitchChannel) + ".") : ("Twitch", "Twitch chat disconnected."));
				_lastTwitchChatState = text;
			}
			string text2 = (kickConnected ? "connected" : "disconnected");
			if (text2 != _lastKickChatState)
			{
				pending.Add(kickConnected ? ("Kick", "Kick chat connected - listening to " + (string.IsNullOrWhiteSpace(kickChannel) ? "your channel" : kickChannel) + ".") : ("Kick", "Kick chat disconnected."));
				_lastKickChatState = text2;
			}
			string text3 = ((!discordConnected) ? "disconnected" : (discordChannelSelected ? "channel-selected" : "no-channel"));
			if (text3 != _lastDiscordChatState)
			{
				List<(string, string)> list = pending;
				bool flag = false;
				(string, string) item = ((text3 == "channel-selected") ? ("Discord", "Discord chat connected - listening to the selected channel.") : ((!(text3 == "no-channel")) ? ("Discord", "Discord chat disconnected.") : ("Discord", "Discord bot is connected, but no chat channel is selected - Discord messages won't appear here until you pick one on the Discord page.")));
				bool flag2 = false;
				list.Add(item);
				_lastDiscordChatState = text3;
			}
			if (pending.Count == 0)
			{
				return;
			}
			((DispatcherObject)Application.Current).Dispatcher.InvokeAsync<Task>((Func<Task>)async delegate
			{
				foreach (var (platform, message) in pending)
				{
					await AddSystemMessageAsync(platform, message);
				}
			});
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to post platform chat-state system messages");
		}
	}

	private async Task OnPlatformConnectionChangedAsync(PlatformConnectionChangedEvent evt)
	{
		_logger.LogInformation("Platform connection changed: {Platform} -> {Connected}", evt.Platform, evt.Connected);
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			UpdateConnectedPlatforms();
		});
		_logger.LogInformation("? Connected platforms updated after {Platform} {Status}", evt.Platform, evt.Connected ? "connected" : "disconnected");
	}

	private async Task SendMessageAsync()
	{
		_logger.LogInformation("SendMessageAsync called. MessageInput: '{Input}'", MessageInput);
		if (string.IsNullOrWhiteSpace(MessageInput))
		{
			_logger.LogWarning("MessageInput is empty or whitespace");
			return;
		}
		// Unified reply box: while a DM thread is open the main input is the DM
		// composer — route to the shared DM reply core instead of the platform
		// send paths (slash-command handling included).
		if (IsDmReplyMode && SelectedDmConversation != null)
		{
			if (await SendDmReplyCoreAsync(SelectedDmConversation, MessageInput?.Trim()))
			{
				MessageInput = "";
			}
			return;
		}
		string slashCommand = MessageInput.Trim();
		if (slashCommand.StartsWith("/", StringComparison.Ordinal) && slashCommand.Length > 1)
		{
			await HandleDiscordSlashCommandAsync(slashCommand);
			return;
		}
		if (SelectedPlatform == null)
		{
			_logger.LogWarning("No platform selected for sending message");
			MessageBox.Show("No platform selected. Please connect to a platform first.", "No Platform", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (!SelectedPlatform.IsAllPlatforms)
		{
			try
			{
				_logger.LogInformation("Attempting to send message to {Platform}: {Message}", SelectedPlatform?.Name ?? string.Empty, MessageInput);
				switch (SelectedPlatform?.Name ?? string.Empty)
				{
				case "Twitch":
					_logger.LogInformation("Twitch IsConnected: {IsConnected}, Channel: {Channel}", _twitchService.IsConnected, _twitchService.Channel);
					if (_twitchService.IsConnected)
					{
						string messageText4 = MessageInput;
						if (_variableReplacementService != null && messageText4.Contains('{'))
						{
							try
							{
								_logger.LogInformation("\ud83d\udd04 Attempting variable replacement. Service available: {Available}, Message: '{Message}'", _variableReplacementService != null, messageText4);
								Dictionary<string, string> contextVars4 = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("\ud83d\udccb Context variables: {Vars}", string.Join(", ", contextVars4.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
								Dictionary<string, string> availableVars2 = await _variableReplacementService.GetAvailableVariablesAsync(SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("\ud83d\udcda Available database variables: {Count} - {Names}", availableVars2.Count, (availableVars2.Count > 0) ? string.Join(", ", availableVars2.Keys) : "NONE");
								messageText4 = await _variableReplacementService.ReplaceVariablesAsync(messageText4, contextVars4, SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("? Variables replaced in message. Original: '{Original}', Replaced: '{Replaced}'", MessageInput, messageText4);
							}
							catch (Exception ex)
							{
								Exception ex11 = ex;
								Exception ex12 = ex11;
								Exception ex13 = ex12;
								_logger.LogWarning(ex13, "? Error replacing variables in message, using original text");
							}
						}
						else if (_variableReplacementService == null)
						{
							_logger.LogWarning("?\ufe0f Variable replacement service is NULL");
						}
						else if (!messageText4.Contains('{'))
						{
							_logger.LogDebug("No variables to replace (no '{{' found in message)");
						}
						_logger.LogInformation("Sending message to Twitch...");
						string twitchMessageKey = messageText4.Trim().ToLowerInvariant();
						lock (_recentMessagesLock)
						{
							_recentTwitchMessages[twitchMessageKey] = DateTime.Now;
							_logger.LogInformation("\ud83d\udd12 Tracking sent Twitch message to filter echo: {MessageKey}", twitchMessageKey);
						}
						await _twitchService.SendMessageAsync(messageText4);
						_logger.LogInformation("Message sent to Twitch successfully");
						ChatMessage twitchMessage = new ChatMessage
						{
							Platform = "Vyral",
							Username = (_twitchService.BotUsername ?? "Bot"),
							Message = messageText4,
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
						MessageInput = "";
					}
					else
					{
						_logger.LogWarning("Twitch is not connected, cannot send message");
						MessageBox.Show("Twitch connection lost. Please reconnect.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
					break;
				case "Kick":
					_logger.LogInformation("Kick IsConnected: {IsConnected}, Channel: {Channel}", _kickService.IsConnected, _kickService.Channel);
					if (_kickService.IsConnected)
					{
						string messageText2 = MessageInput;
						if (_variableReplacementService != null && messageText2.Contains('{'))
						{
							try
							{
								_logger.LogInformation("\ud83d\udd04 Attempting variable replacement. Service available: {Available}, Message: '{Message}'", _variableReplacementService != null, messageText2);
								Dictionary<string, string> contextVars2 = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("\ud83d\udccb Context variables: {Vars}", string.Join(", ", contextVars2.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
								Dictionary<string, string> availableVars = await _variableReplacementService.GetAvailableVariablesAsync(SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("\ud83d\udcda Available database variables: {Count} - {Names}", availableVars.Count, (availableVars.Count > 0) ? string.Join(", ", availableVars.Keys) : "NONE");
								messageText2 = await _variableReplacementService.ReplaceVariablesAsync(messageText2, contextVars2, SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("? Variables replaced in message. Original: '{Original}', Replaced: '{Replaced}'", MessageInput, messageText2);
							}
							catch (Exception ex)
							{
								Exception ex5 = ex;
								Exception ex6 = ex5;
								Exception ex7 = ex6;
								_logger.LogWarning(ex7, "? Error replacing variables in message, using original text");
							}
						}
						else if (_variableReplacementService == null)
						{
							_logger.LogWarning("?\ufe0f Variable replacement service is NULL");
						}
						else if (!messageText2.Contains('{'))
						{
							_logger.LogDebug("No variables to replace (no '{{' found in message)");
						}
						string messageKey = messageText2.Trim().ToLowerInvariant();
						lock (_recentMessagesLock)
						{
							_recentKickMessages[messageKey] = DateTime.Now;
							_logger.LogInformation("\ud83d\udd12 Tracking sent Kick message to filter echo: {MessageKey}", messageKey);
						}
						_logger.LogInformation("Sending message to Kick...");
						await _kickService.SendMessageAsync(messageText2);
						_logger.LogInformation("Message sent to Kick successfully");
						ChatMessage sentMessage = new ChatMessage
						{
							Platform = "Vyral",
							Username = (_kickService.BotUsername ?? "Bot"),
							Message = messageText2,
							Timestamp = DateTime.Now,
							Color = "#53FC18",
							IsModerator = false,
							IsSubscriber = false,
							IsVip = false,
							IsBroadcaster = true,
							Badges = new Dictionary<string, string> { { "Broadcaster", "Broadcaster" } }
						};
						_logger.LogInformation("\ud83d\udcdb Sent message has {BadgeCount} badges: {Badges}", sentMessage.Badges.Count, string.Join(", ", sentMessage.Badges.Select((KeyValuePair<string, string> b) => b.Key + "=" + b.Value)));
						sentMessage.InitializeBadges();
						Messages.Add(sentMessage);
						_logger.LogInformation("Added sent message to chat UI");
						MessageInput = "";
					}
					else
					{
						_logger.LogWarning("Kick is not connected, cannot send message");
						MessageBox.Show("Kick connection lost. Please reconnect.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
					break;
				case "YouTube":
					_logger.LogInformation("YouTube IsConnected: {IsConnected}, Channel: {Channel}", _youtubeService.IsConnected, _youtubeService.Channel);
					if (_youtubeService.IsConnected)
					{
						string messageText3 = MessageInput;
						if (_variableReplacementService != null && messageText3.Contains('{'))
						{
							try
							{
								Dictionary<string, string> contextVars3 = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
								messageText3 = await _variableReplacementService.ReplaceVariablesAsync(messageText3, contextVars3, SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("? Variables replaced for YouTube. Original: '{Original}', Replaced: '{Replaced}'", MessageInput, messageText3);
							}
							catch (Exception ex)
							{
								Exception ex8 = ex;
								Exception ex9 = ex8;
								Exception ex10 = ex9;
								_logger.LogWarning(ex10, "? Error replacing variables in message, using original text");
							}
						}
						string ytMessageKey = messageText3.Trim().ToLowerInvariant();
						lock (_recentMessagesLock)
						{
							_recentYoutubeMessages[ytMessageKey] = DateTime.Now;
							_logger.LogInformation("\ud83d\udd12 Tracking sent YouTube message to filter echo: {MessageKey}", ytMessageKey);
						}
						_logger.LogInformation("Sending message to YouTube...");
						await _youtubeService.SendMessageAsync(messageText3);
						_logger.LogInformation("Message sent to YouTube successfully");
						ChatMessage ytMessage = new ChatMessage
						{
							Platform = "Vyral",
							Username = (_youtubeService.BotUsername ?? "Bot"),
							Message = messageText3,
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
						MessageInput = "";
					}
					else
					{
						_logger.LogWarning("YouTube is not connected, cannot send message");
						MessageBox.Show("YouTube connection lost. Please reconnect.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
					break;
				case "Discord":
				{
					IDiscordService discordSend = ResolveDiscordSendTarget(null);
					_logger.LogInformation("Discord IsConnected: {IsConnected}, SelectedChannelId: {ChannelId}", discordSend?.IsConnected, discordSend?.SelectedChannelId);
					if (discordSend?.IsConnected ?? false)
					{
						string messageText = MessageInput;
						if (_variableReplacementService != null && messageText.Contains('{'))
						{
							try
							{
								Dictionary<string, string> contextVars = BuildContextVariables(SelectedPlatform?.Name ?? string.Empty);
								messageText = await _variableReplacementService.ReplaceVariablesAsync(messageText, contextVars, SelectedPlatform?.Name ?? string.Empty);
								_logger.LogInformation("? Variables replaced for Discord. Original: '{Original}', Replaced: '{Replaced}'", MessageInput, messageText);
							}
							catch (Exception ex)
							{
								Exception ex2 = ex;
								Exception ex3 = ex2;
								Exception ex4 = ex3;
								_logger.LogWarning(ex4, "? Error replacing variables in message, using original text");
							}
						}
						string discordMessageKey = messageText.Trim().ToLowerInvariant();
						lock (_recentMessagesLock)
						{
							_recentDiscordMessages[discordMessageKey] = DateTime.Now;
							_logger.LogInformation("\ud83d\udd12 Tracking sent Discord message to filter echo: {MessageKey}", discordMessageKey);
						}
						_logger.LogInformation("Sending message to Discord...");
						await discordSend.SendMessageAsync(messageText);
						_logger.LogInformation("Message sent to Discord successfully");
						ChatMessage discordMessage = new ChatMessage
						{
							Platform = "Vyral",
							Username = (discordSend.BotUsername ?? "Bot"),
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
						MessageInput = "";
					}
					else
					{
						_logger.LogWarning("Discord is not connected, cannot send message");
						MessageBox.Show("Discord connection lost. Please reconnect.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
					break;
				}
				default:
					_logger.LogWarning("Unknown platform: {Platform}", SelectedPlatform?.Name ?? string.Empty);
					break;
				}
				return;
			}
			catch (Exception ex14)
			{
				_logger.LogError(ex14, "Error sending message to {Platform}", SelectedPlatform?.Name ?? string.Empty);
				MessageBox.Show("Error sending message: " + ex14.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
		}
		await SendToAllPlatformsAsync();
	}

	private async Task SendToAllPlatformsAsync()
	{
		_logger.LogInformation("SendToAllPlatformsAsync called. MessageInput: '{Input}'", MessageInput);
		if (string.IsNullOrWhiteSpace(MessageInput))
		{
			_logger.LogWarning("MessageInput is empty or whitespace");
			return;
		}
		string slashCommand = MessageInput.Trim();
		if (slashCommand.StartsWith("/", StringComparison.Ordinal) && slashCommand.Length > 1)
		{
			await HandleDiscordSlashCommandAsync(slashCommand);
			return;
		}
		if (ConnectedPlatforms.Count == 0)
		{
			_logger.LogWarning("No platforms connected");
			MessageBox.Show("No platforms connected. Please connect to platforms first.", "No Platforms", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		try
		{
			string messageText = MessageInput;
			if (_variableReplacementService != null)
			{
				messageText = await _variableReplacementService.ReplaceVariablesAsync(messageText);
			}
			List<Task> sendTasks = new List<Task>();
			List<string> errors = new List<string>();
			string echoKey = messageText.Trim().ToLowerInvariant();
			lock (_recentMessagesLock)
			{
				if (_twitchService.IsConnected)
				{
					_recentTwitchMessages[echoKey] = DateTime.Now;
					_logger.LogInformation("\ud83d\udd12 Tracking SendToAll Twitch echo: {Key}", echoKey);
				}
				if (_kickService.IsConnected)
				{
					_recentKickMessages[echoKey] = DateTime.Now;
					_logger.LogInformation("\ud83d\udd12 Tracking SendToAll Kick echo: {Key}", echoKey);
				}
				if (_youtubeService.IsConnected)
				{
					_recentYoutubeMessages[echoKey] = DateTime.Now;
					_logger.LogInformation("\ud83d\udd12 Tracking SendToAll YouTube echo: {Key}", echoKey);
				}
				if (IsAnyDiscordConnected)
				{
					_recentDiscordMessages[echoKey] = DateTime.Now;
					_logger.LogInformation("\ud83d\udd12 Tracking SendToAll Discord echo: {Key}", echoKey);
				}
			}
			foreach (PlatformOption platform in ConnectedPlatforms.Where((PlatformOption p) => !p.IsAllPlatforms))
			{
				sendTasks.Add(Task.Run(async delegate
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
						{
							IDiscordService discordAllSend = ResolveDiscordSendTarget(null);
							if (discordAllSend?.IsConnected ?? false)
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
						}
						default:
							_logger.LogWarning("Unknown platform: {Platform}", platform.Name);
							lock (errors)
							{
								errors.Add(platform.Name + ": Not supported");
							}
							break;
						}
					}
					catch (Exception ex2)
					{
						Exception ex3 = ex2;
						Exception ex4 = ex3;
						Exception ex5 = ex4;
						_logger.LogError(ex5, "Error sending message to {Platform}", platform.Name);
						lock (errors)
						{
							errors.Add(platform.Name + ": " + ex5.Message);
						}
					}
				}));
			}
			await Task.WhenAll(sendTasks);
			ChatMessage sentMessage = new ChatMessage
			{
				Platform = "Vyral",
				Username = "Vyral",
				Message = messageText,
				Timestamp = DateTime.Now,
				Color = "#26A69A",
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
				string errorMessage = "Some messages failed to send:\n" + string.Join("\n", errors);
				MessageBox.Show(errorMessage, "Partial Send Failure", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
			MessageBox.Show("Error sending message: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private Dictionary<string, string> BuildContextVariables(string platformName)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		switch (platformName)
		{
		case "Twitch":
			dictionary["bot-username"] = _twitchService.BotUsername ?? "Bot";
			dictionary["channel"] = _twitchService.Channel ?? "";
			break;
		case "Kick":
			dictionary["bot-username"] = _kickService.BotUsername ?? "Bot";
			dictionary["channel"] = _kickService.Channel ?? "";
			break;
		case "Discord":
		{
			IDiscordService discordSendTarget = DiscordSendTarget;
			dictionary["bot-username"] = discordSendTarget?.BotUsername ?? "Bot";
			dictionary["channel"] = discordSendTarget?.SelectedChannelId ?? "";
			break;
		}
		default:
			dictionary["bot-username"] = "Bot";
			dictionary["channel"] = "";
			break;
		}
		dictionary["platform"] = platformName;
		return dictionary;
	}

	private void TogglePlatformSelector()
	{
		IsPlatformSelectorOpen = !IsPlatformSelectorOpen;
	}

	private void SelectPlatform(PlatformOption platform)
	{
		SelectedPlatform = platform;
		IsPlatformSelectorOpen = false;
		_logger.LogInformation("Selected platform: {Platform}", platform.Name);
	}

	private async Task OpenViewerProfileAsync(object? parameter)
	{
		ChatMessage message = parameter as ChatMessage;
		if (message == null || message.IsSystemMessage || string.IsNullOrWhiteSpace(message.Username))
		{
			return;
		}
		string platform = message.Platform;
		if (string.IsNullOrWhiteSpace(platform))
		{
			PlatformOption selected = SelectedPlatform;
			platform = ((selected != null && !selected.IsAllPlatforms) ? selected.Name : string.Empty);
		}
		if (string.IsNullOrWhiteSpace(platform))
		{
			_logger.LogDebug("Viewer profile click ignored for {Username}: no platform context", message.Username);
			return;
		}
		if (_viewerProfileWindowOpening)
		{
			_logger.LogDebug("Viewer profile click ignored for {Username}: a profile window is already opening", message.Username);
			return;
		}
		_viewerProfileWindowOpening = true;
		try
		{
			string key = message.Username + "|" + platform;
			ViewerProfileWindow existing = _openViewerProfileWindow;
			if (existing != null && existing.IsLoaded && string.Equals(_openViewerProfileKey, key, StringComparison.OrdinalIgnoreCase))
			{
				existing.Activate();
				return;
			}
			IServiceProvider provider = App.ServiceProvider;
			if (provider == null)
			{
				_logger.LogWarning("Viewer profile window skipped: service provider not yet available");
				return;
			}
			ViewerProfileViewModel viewModel = provider.GetRequiredService<ViewerProfileViewModel>();
			await viewModel.InitializeAsync(message.Username, platform);
			ViewerProfileWindow window = new ViewerProfileWindow(viewModel)
			{
				Owner = Application.Current.MainWindow
			};
			_openViewerProfileWindow = window;
			_openViewerProfileKey = key;
			window.Closed += delegate
			{
				if (_openViewerProfileWindow == window)
				{
					_openViewerProfileWindow = null;
					_openViewerProfileKey = null;
				}
			};
			window.ShowDialog();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogError(ex4, "Failed to open the viewer profile window for {Username} ({Platform})", message.Username, platform);
		}
		finally
		{
			_viewerProfileWindowOpening = false;
		}
	}

	private void ToggleAllPlatformsSettings()
	{
		IsAllPlatformsSettingsOpen = !IsAllPlatformsSettingsOpen;
		if (IsAllPlatformsSettingsOpen)
		{
			_logger.LogInformation("All Platforms quick-settings opened");
		}
	}

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

	private async Task CloseAllPlatformsPopupAndNavigateAsync(string pageKey)
	{
		IsAllPlatformsSettingsOpen = false;
		try
		{
			await _eventBus.PublishAsync(new NavigateToPageEvent
			{
				PageKey = pageKey,
				Source = "System"
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogError(ex4, "Failed to publish NavigateToPageEvent for {PageKey}", pageKey);
		}
	}

	private Task StartGiveawayAsync()
	{
		return CloseAllPlatformsPopupAndNavigateAsync("giveaways");
	}

	private Task StartPollAsync()
	{
		return CloseAllPlatformsPopupAndNavigateAsync("polls");
	}

	private Task TriggerEventAsync()
	{
		return CloseAllPlatformsPopupAndNavigateAsync("eventtesting");
	}

	private static string? ShowAnnouncementPrompt()
	{
		TextBox input = new TextBox
		{
			Margin = new Thickness(24.0, 12.0, 24.0, 0.0),
			FontSize = 13.0,
			TextWrapping = TextWrapping.Wrap
		};
		Button button = new Button
		{
			Content = "SEND",
			Width = 100.0,
			Height = 34.0,
			Margin = new Thickness(0.0, 16.0, 8.0, 0.0)
		};
		Button button2 = new Button
		{
			Content = "CANCEL",
			Width = 100.0,
			Height = 34.0,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(24.0, 0.0, 24.0, 16.0)
		};
		stackPanel.Children.Add(button);
		stackPanel.Children.Add(button2);
		StackPanel stackPanel2 = new StackPanel();
		stackPanel2.Children.Add(new TextBlock
		{
			Text = "Announcement to send to every connected platform:",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(24.0, 16.0, 24.0, 0.0)
		});
		stackPanel2.Children.Add(input);
		stackPanel2.Children.Add(stackPanel);
		Window dialog = new Window
		{
			Title = "Send Announcement",
			Owner = Application.Current.MainWindow,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			SizeToContent = SizeToContent.WidthAndHeight,
			Width = 440.0,
			ResizeMode = ResizeMode.NoResize,
			Content = stackPanel2
		};
		string result = null;
		button.Click += delegate
		{
			result = input.Text;
			dialog.DialogResult = true;
			dialog.Close();
		};
		button2.Click += delegate
		{
			dialog.DialogResult = false;
			dialog.Close();
		};
		input.Loaded += delegate
		{
			input.Focus();
		};
		return (dialog.ShowDialog() == true) ? result : null;
	}

	private async Task SendAnnouncementAsync()
	{
		try
		{
			IsAllPlatformsSettingsOpen = false;
			string text = ShowAnnouncementPrompt();
			if (!string.IsNullOrWhiteSpace(text))
			{
				MessageInput = text;
				await SendToAllPlatformsAsync();
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogError(ex4, "Failed to send the announcement from the chat quick actions");
		}
	}

	private void ClearChat()
	{
		Messages.Clear();
		_loadedMessageIds.Clear();
		_historyLoadedCount = 0;
		_logger.LogInformation("Chat cleared (history tracking reset)");
	}

	private async Task ClearHistoryAsync()
	{
		try
		{
			CustomDialog confirmDialog = new CustomDialog("Clear History", "Are you sure you want to delete all chat history from the database? This cannot be undone.", CustomDialogButtons.YesNo, CustomDialogIcon.Warning)
			{
				Owner = Application.Current.MainWindow
			};
			if (confirmDialog.ShowDialog() != true)
			{
				return;
			}
			if (_kickService.IsConnected && !string.IsNullOrEmpty(_kickService.Channel))
			{
				await _chatStore.DeleteAsync("Kick", _kickService.Channel);
				_logger.LogInformation("\ud83d\uddd1\ufe0f Deleted Kick chat history for channel: {Channel}", _kickService.Channel);
			}
			if (_twitchService.IsConnected && !string.IsNullOrEmpty(_twitchService.Channel))
			{
				await _chatStore.DeleteAsync("Twitch", _twitchService.Channel);
				_logger.LogInformation("\ud83d\uddd1\ufe0f Deleted Twitch chat history for channel: {Channel}", _twitchService.Channel);
			}
			if (_youtubeService.IsConnected && !string.IsNullOrEmpty(_youtubeService.Channel))
			{
				await _chatStore.DeleteAsync("YouTube", _youtubeService.Channel);
				_logger.LogInformation("\ud83d\uddd1\ufe0f Deleted YouTube chat history for channel: {Channel}", _youtubeService.Channel);
			}
			IDiscordService discordClearTarget = _discordBotManager.Primary ?? _discordService;
			if (discordClearTarget.IsConnected && !string.IsNullOrEmpty(discordClearTarget.SelectedChannelId))
			{
				string discordChannelKey = discordClearTarget.SelectedChannelId;
				foreach (DiscordGuildInfo guild in discordClearTarget.ConnectedGuilds)
				{
					foreach (DiscordChannelInfo channel in guild.TextChannels)
					{
						if (channel.Id == discordClearTarget.SelectedChannelId)
						{
							discordChannelKey = channel.Name;
							break;
						}
					}
					if (discordChannelKey != discordClearTarget.SelectedChannelId)
					{
						break;
					}
				}
				await _chatStore.DeleteAsync("Discord", discordChannelKey);
				_logger.LogInformation("\ud83d\uddd1\ufe0f Deleted Discord chat history for channel: {Channel}", discordChannelKey);
			}
			Messages.Clear();
			_loadedMessageIds.Clear();
			_historyLoadedCount = 0;
			_logger.LogInformation("? Chat history deleted from database");
			CustomDialog successDialog = new CustomDialog("History Cleared", "Chat history has been deleted from the database.", CustomDialogButtons.OK, CustomDialogIcon.Success)
			{
				Owner = Application.Current.MainWindow
			};
			successDialog.ShowDialog();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogError(ex4, "Error clearing chat history");
			CustomDialog errorDialog = new CustomDialog("Error", "Error clearing history: " + ex4.Message, CustomDialogButtons.OK, CustomDialogIcon.Error)
			{
				Owner = Application.Current.MainWindow
			};
			errorDialog.ShowDialog();
		}
	}

	private void ToggleAutoScroll()
	{
		AutoScroll = !AutoScroll;
	}

	private async Task OnYouTubeChatMessageReceivedAsync(YouTubeChatMessageEvent evt)
	{
		if (evt == null)
		{
			return;
		}
		_logger.LogInformation("\ud83d\udce8 ChatViewModel received YouTube message from {Username}: {Message}", evt.Username, evt.Message);
		string messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
		bool isOwnYoutubeMessage = await IsOwnYouTubeAccountAsync(evt.DisplayName ?? evt.Username);
		lock (_recentMessagesLock)
		{
			if (_recentYoutubeMessages.TryGetValue(messageKey, out var sentTime))
			{
				TimeSpan age = DateTime.Now - sentTime;
				if (age.TotalSeconds < 15.0 && isOwnYoutubeMessage)
				{
					_logger.LogInformation("\ud83d\udd07 Ignoring echo of recently sent YouTube message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
					return;
				}
				if (age.TotalSeconds >= 15.0)
				{
					_recentYoutubeMessages.Remove(messageKey);
				}
			}
			List<string> expiredKeys = _recentYoutubeMessages.Where(delegate(KeyValuePair<string, DateTime> kvp)
			{
				DateTime now = DateTime.Now;
				KeyValuePair<string, DateTime> keyValuePair = kvp;
				return (now - keyValuePair.Value).TotalSeconds > 15.0;
			}).Select(delegate(KeyValuePair<string, DateTime> kvp)
			{
				KeyValuePair<string, DateTime> keyValuePair = kvp;
				return keyValuePair.Key;
			}).ToList();
			foreach (string key in expiredKeys)
			{
				_recentYoutubeMessages.Remove(key);
			}
		}
		if (!YoutubeFilterEnabled)
		{
			_logger.LogDebug("YouTube messages filtered out (YoutubeFilterEnabled=false)");
			return;
		}
		if (ConnectedPlatforms.Count == 0 || !ConnectedPlatforms.Any((PlatformOption p) => p.Name == "YouTube"))
		{
			_logger.LogInformation("YouTube not in platforms list, refreshing...");
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				UpdateConnectedPlatforms();
			});
		}
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			ChatMessage chatMessage = new ChatMessage
			{
				Platform = (isOwnYoutubeMessage ? "Vyral" : "YouTube"),
				Username = (evt.DisplayName ?? evt.Username ?? "Unknown"),
				Message = (evt.Message ?? string.Empty),
				Timestamp = evt.Timestamp.ToLocalTime(),
				Color = "#FF0000",
				IsModerator = evt.IsModerator,
				IsSubscriber = evt.IsMember,
				IsVip = false,
				IsBroadcaster = evt.IsOwner,
				Badges = new Dictionary<string, string>()
			};
			if (evt.IsOwner)
			{
				chatMessage.Badges["broadcaster"] = "1";
			}
			if (evt.IsModerator)
			{
				chatMessage.Badges["moderator"] = "1";
			}
			if (evt.IsMember)
			{
				chatMessage.Badges["subscriber"] = "1";
			}
			chatMessage.InitializeBadges();
			Messages.Add(chatMessage);
			_logger.LogInformation("? YouTube message added to UI. Total messages: {Count}", Messages.Count);
			while (Messages.Count > 500)
			{
				Messages.RemoveAt(0);
			}
		});
	}

	private async Task OnKickChatMessageReceivedAsync(KickChatMessageEvent evt)
	{
		if (evt == null)
		{
			return;
		}
		_logger.LogInformation("\ud83d\udce8 ChatViewModel received Kick message from {Username}: {Message}", evt.Username, evt.Message);
		string messageKey = evt.Message?.Trim().ToLowerInvariant() ?? "";
		bool isOwnKickMessage = await IsOwnKickAccountAsync(evt.Username);
		lock (_recentMessagesLock)
		{
			if (_recentKickMessages.TryGetValue(messageKey, out var sentTime))
			{
				TimeSpan age = DateTime.Now - sentTime;
				if (age.TotalSeconds < 10.0 && isOwnKickMessage)
				{
					_logger.LogInformation("\ud83d\udd07 Ignoring echo of recently sent message (age: {Age:F2}s): {Message}", age.TotalSeconds, evt.Message);
					return;
				}
				if (age.TotalSeconds >= 10.0)
				{
					_recentKickMessages.Remove(messageKey);
				}
			}
			List<string> expiredKeys = _recentKickMessages.Where(delegate(KeyValuePair<string, DateTime> kvp)
			{
				DateTime now = DateTime.Now;
				KeyValuePair<string, DateTime> keyValuePair = kvp;
				return (now - keyValuePair.Value).TotalSeconds > 10.0;
			}).Select(delegate(KeyValuePair<string, DateTime> kvp)
			{
				KeyValuePair<string, DateTime> keyValuePair = kvp;
				return keyValuePair.Key;
			}).ToList();
			foreach (string key in expiredKeys)
			{
				_recentKickMessages.Remove(key);
			}
		}
		if (!KickFilterEnabled)
		{
			_logger.LogDebug("Kick messages filtered out (KickFilterEnabled=false)");
			return;
		}
		string source = ((!string.IsNullOrEmpty(evt.MessageId) && evt.MessageId.StartsWith("history_")) ? "history" : "realtime");
		_logger.LogInformation("Kick message ({Source}) from {Username} ({DisplayName}): {Message}", source, evt.Username, evt.DisplayName, evt.Message);
		if (ConnectedPlatforms.Count == 0 || !ConnectedPlatforms.Any((PlatformOption p) => p.Name == "Kick"))
		{
			_logger.LogInformation("Kick not in platforms list, refreshing...");
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				UpdateConnectedPlatforms();
			});
		}
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			ChatMessage chatMessage = new ChatMessage
			{
				Platform = (isOwnKickMessage ? "Vyral" : "Kick"),
				Username = (evt.DisplayName ?? evt.Username ?? "Unknown"),
				Message = (evt.Message ?? string.Empty),
				Timestamp = evt.Timestamp.ToLocalTime(),
				Color = (evt.Color ?? "#53FC18"),
				IsModerator = evt.IsModerator,
				IsSubscriber = evt.IsSubscriber,
				IsVip = false,
				IsBroadcaster = evt.IsOwner,
				Badges = (evt.Badges ?? new Dictionary<string, string>())
			};
			chatMessage.InitializeBadges();
			_logger.LogInformation("? Kick message added to UI. Total messages: {Count}", Messages.Count + 1);
			Messages.Add(chatMessage);
			while (Messages.Count > 500)
			{
				Messages.RemoveAt(0);
			}
		});
	}

	private async Task OnDiscordChatMessageReceivedAsync(DiscordChatMessageEvent evt)
	{
		if (evt == null)
		{
			return;
		}
		_logger.LogInformation("\ud83d\udce8 ChatViewModel received Discord message from {Username}: {Message}", evt.Username, evt.Message);
		RememberDiscordBotOrigin(evt.MessageId, evt.BotId);
		if (string.Equals(evt.Channel, "DirectMessage", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(evt.GuildName))
		{
			RememberDiscordDmOrigin(evt.MessageId, evt.ChannelId, evt.BotId);
		}
		if (IsOwnDiscordAccount(evt.Username))
		{
			_logger.LogInformation("\ud83d\udd07 Ignoring message from the Discord bot account itself: {Message}", evt.Message);
			return;
		}
		if (!DiscordFilterEnabled)
		{
			_logger.LogDebug("Discord messages filtered out (DiscordFilterEnabled=false)");
			return;
		}
		// Stream effects first: a recognized (and tier-allowed) "!flashlights" /
		// "!lightson" / "!lightsoff" / "!streamdeck" command is consumed here.
		if (await TryHandleStreamEffectCommandAsync(evt))
		{
			return;
		}
		await TryHandleDiscordModCommandAsync(evt);
		if (ConnectedPlatforms.Count == 0 || !ConnectedPlatforms.Any((PlatformOption p) => p.Name == "Discord"))
		{
			_logger.LogInformation("Discord not in platforms list, refreshing...");
			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				UpdateConnectedPlatforms();
			});
		}
		string channelName = ((!string.IsNullOrEmpty(evt.Channel)) ? evt.Channel : (evt.ChannelId ?? "Unknown"));
		try
		{
			await _chatStore.AddAsync("Discord", channelName, evt);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Failed to persist Discord chat message from {Username}", evt.Username);
		}
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			if (evt.IsBroadcaster)
			{
				dictionary["broadcaster"] = "1";
			}
			if (evt.IsModerator)
			{
				dictionary["moderator"] = "1";
			}
			ChatMessage chatMessage = new ChatMessage
			{
				Platform = "Discord",
				Username = (evt.Username ?? "Unknown"),
				Message = (evt.Message ?? string.Empty),
				Timestamp = evt.Timestamp.ToLocalTime(),
				Color = "#5865F2",
				IsModerator = evt.IsModerator,
				IsSubscriber = false,
				IsVip = false,
				IsBroadcaster = evt.IsBroadcaster,
				Badges = dictionary
			};
			chatMessage.InitializeBadges();
			if (string.Equals(evt.Channel, "DirectMessage", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(evt.GuildName))
			{
				RouteDiscordDmToConversation(evt, chatMessage);
			}
			else
			{
				_logger.LogInformation("? Discord message added to UI. Total messages: {Count}", Messages.Count + 1);
				Messages.Add(chatMessage);
				while (Messages.Count > 500)
				{
					Messages.RemoveAt(0);
				}
			}
		});
	}

	private async Task OnKickFollowAsync(KickFollowEvent evt)
	{
		if (!KickFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "Follow")) ?? new ChatEventSetting
		{
			Platform = "Kick",
			EventType = "Follow",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		if (_viewerStatusService != null)
		{
			try
			{
				await _viewerStatusService.RecordFollowAsync(evt.Username, "Kick");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogWarning(ex4, "Failed to record Kick follow status for {Username}", evt.Username);
			}
		}
		bool wasAutoReplied = false;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string message = $"\ud83c\udf89 {evt.Username} just followed! Total followers: {evt.FollowerCount}";
			await AddSystemMessageAsync("Kick", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("follow", "Kick");
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to play sound for Kick follow");
			}
		}
		await ShowEventAlertAsync("New Follower!", evt.Username, $"Total followers: {evt.FollowerCount}", "Kick");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.Username, "Kick", "follow");
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to award currency bonus for Kick follow");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{ "platform", "Kick" }
		};
		_logger.LogInformation("\ud83d\udd0d Checking auto-reply: Enabled={Enabled}, Message={Message}", settings.AutoReplyEnabled, settings.AutoReplyMessage);
		if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
		{
			bool shouldSend = _autoReplyService.ShouldSendReply(evt.Username, "Kick", "Follow");
			_logger.LogInformation("\ud83d\udd0d Rate limit check: ShouldSend={ShouldSend}", shouldSend);
			if (shouldSend)
			{
				string reply = await BuildAutoReplyTextAsync("Kick", "Follow", evt.Username, settings.AutoReplyMessage, variables);
				_logger.LogInformation("\ud83d\udce4 Attempting to send auto-reply: {Reply}", reply);
				try
				{
					await _kickService.SendMessageAsync(reply);
					_logger.LogInformation("? Auto-reply sent for Kick follow: {Reply}", reply);
					wasAutoReplied = true;
				}
				catch (Exception ex)
				{
					Exception ex11 = ex;
					Exception ex12 = ex11;
					Exception ex13 = ex12;
					_logger.LogError(ex13, "? Failed to send auto-reply for Kick follow");
				}
			}
			else
			{
				_logger.LogInformation("?\ufe0f Auto-reply blocked by rate limiter");
			}
		}
		else
		{
			_logger.LogInformation("?\ufe0f Auto-reply skipped: Enabled={Enabled}, HasMessage={HasMessage}", settings.AutoReplyEnabled, !string.IsNullOrEmpty(settings.AutoReplyMessage));
		}
		await SaveEventStatisticAsync("Kick", "Follow", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnKickSubscribeAsync(KickSubscribeEvent evt)
	{
		if (!KickFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "Subscribe")) ?? new ChatEventSetting
		{
			Platform = "Kick",
			EventType = "Subscribe",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		if (_viewerStatusService != null)
		{
			try
			{
				await _viewerStatusService.RecordSubscriptionAsync(evt.Username, "Kick", 1, evt.MonthsSubscribed);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogWarning(ex4, "Failed to record Kick subscription status for {Username}", evt.Username);
			}
		}
		bool wasAutoReplied = false;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string monthsText = ((evt.MonthsSubscribed > 1) ? $" ({evt.MonthsSubscribed} months)" : "");
			string message = $"? {evt.Username} subscribed{monthsText}!";
			if (!string.IsNullOrEmpty(evt.Message))
			{
				message = message + " - " + evt.Message;
			}
			await AddSystemMessageAsync("Kick", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("subscribe", "Kick");
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to play sound for Kick subscribe");
			}
		}
		await ShowEventAlertAsync("New Subscriber!", message: (evt.MonthsSubscribed > 1) ? $"{evt.MonthsSubscribed} months!" : "Thanks for subscribing!", username: evt.Username, platform: "Kick");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.Username, "Kick", "subscribe");
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to award currency bonus for Kick subscribe");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{
				"months",
				evt.MonthsSubscribed.ToString()
			},
			{
				"message",
				evt.Message ?? ""
			},
			{ "platform", "Kick" }
		};
		if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.Username, "Kick", "Subscribe"))
		{
			string reply = await BuildAutoReplyTextAsync("Kick", "Subscribe", evt.Username, settings.AutoReplyMessage, variables);
			try
			{
				await _kickService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Kick subscribe: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex11 = ex;
				Exception ex12 = ex11;
				Exception ex13 = ex12;
				_logger.LogError(ex13, "Failed to send auto-reply for Kick subscribe");
			}
		}
		await SaveEventStatisticAsync("Kick", "Subscribe", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnKickGiftSubAsync(KickGiftSubEvent evt)
	{
		if (!KickFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "GiftSub")) ?? new ChatEventSetting
		{
			Platform = "Kick",
			EventType = "GiftSub",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		string message = $"\ud83c\udf81 {evt.GifterUsername} gifted {evt.Count} subscription(s)!";
		if (evt.RecipientUsernames?.Any() ?? false)
		{
			message = message + " Recipients: " + string.Join(", ", evt.RecipientUsernames);
		}
		if (_showEventsInChat && settings.ShowInChat)
		{
			await AddSystemMessageAsync("Kick", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("giftsub", "Kick");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to play sound for Kick gift sub");
			}
		}
		await ShowEventAlertAsync("Gift Subs!", evt.GifterUsername, $"{evt.Count} subscription(s) gifted!", "Kick");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.GifterUsername, "Kick", "giftsub", evt.Count);
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to award currency bonus for Kick gift sub");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "gifter", evt.GifterUsername },
			{
				"count",
				evt.Count.ToString()
			},
			{ "platform", "Kick" }
		};
		if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.GifterUsername, "Kick", "GiftSub"))
		{
			string reply = await BuildAutoReplyTextAsync("Kick", "GiftSub", evt.GifterUsername, settings.AutoReplyMessage, variables);
			try
			{
				await _kickService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Kick gift sub: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to send auto-reply for Kick gift sub");
			}
		}
		await SaveEventStatisticAsync("Kick", "GiftSub", evt.GifterUsername, variables, wasAutoReplied);
	}

	private async Task OnKickHostAsync(KickHostEvent evt)
	{
		if (!KickFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "Host")) ?? new ChatEventSetting
		{
			Platform = "Kick",
			EventType = "Host",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		string message = $"\ud83d\udcfa {evt.HostUsername} is hosting with {evt.ViewerCount} viewers!";
		if (_showEventsInChat && settings.ShowInChat)
		{
			await AddSystemMessageAsync("Kick", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("host", "Kick");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to play sound for Kick host");
			}
		}
		await ShowEventAlertAsync("New Host!", evt.HostUsername, $"{evt.ViewerCount} viewers!", "Kick");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.HostUsername, "Kick", "host");
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to award currency bonus for Kick host");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "hoster", evt.HostUsername },
			{ "username", evt.HostUsername },
			{
				"viewers",
				evt.ViewerCount.ToString()
			},
			{ "platform", "Kick" }
		};
		if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.HostUsername, "Kick", "Host"))
		{
			string reply = await BuildAutoReplyTextAsync("Kick", "Host", evt.HostUsername, settings.AutoReplyMessage, variables);
			try
			{
				await _kickService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Kick host: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to send auto-reply for Kick host");
			}
		}
		await SaveEventStatisticAsync("Kick", "Host", evt.HostUsername, variables, wasAutoReplied);
	}

	private async Task OnKickKicksGiftedAsync(KickKicksGiftedEvent evt)
	{
		if (!KickFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "KicksGifted")) ?? new ChatEventSetting
		{
			Platform = "Kick",
			EventType = "KicksGifted",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		string message = $"\ud83d\udcb0 {evt.SenderUsername} sent {evt.Amount} kicks!";
		if (!string.IsNullOrEmpty(evt.Message))
		{
			message = message + " - " + evt.Message;
		}
		if (_showEventsInChat && settings.ShowInChat)
		{
			await AddSystemMessageAsync("Kick", message);
		}
		await ShowEventAlertAsync("Kicks Gifted!", evt.SenderUsername, $"{evt.Amount} kicks!", "Kick");
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "sender", evt.SenderUsername },
			{ "username", evt.SenderUsername },
			{
				"amount",
				evt.Amount.ToString()
			},
			{
				"message",
				evt.Message ?? ""
			},
			{ "platform", "Kick" }
		};
		if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.SenderUsername, "Kick", "KicksGifted"))
		{
			string reply = await BuildAutoReplyTextAsync("Kick", "KicksGifted", evt.SenderUsername, settings.AutoReplyMessage, variables);
			try
			{
				await _kickService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Kick kicks gifted: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to send auto-reply for Kick kicks gifted");
			}
		}
		await SaveEventStatisticAsync("Kick", "KicksGifted", evt.SenderUsername, variables, wasAutoReplied);
	}

	private async Task OnKickRewardRedeemedAsync(KickRewardRedeemedEvent evt)
	{
		if (!KickFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "RewardRedeemed")) ?? new ChatEventSetting
		{
			Platform = "Kick",
			EventType = "RewardRedeemed",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		string message = $"\ud83c\udfaf {evt.Username} redeemed \"{evt.RewardName}\" ({evt.Cost} points)";
		if (!string.IsNullOrEmpty(evt.UserInput))
		{
			message = message + " - " + evt.UserInput;
		}
		if (_showEventsInChat && settings.ShowInChat)
		{
			await AddSystemMessageAsync("Kick", message);
		}
		await ShowEventAlertAsync("Reward Redeemed!", evt.Username, evt.RewardName, "Kick");
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{ "reward", evt.RewardName },
			{
				"cost",
				evt.Cost.ToString()
			},
			{
				"input",
				evt.UserInput ?? ""
			},
			{ "platform", "Kick" }
		};
		if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.Username, "Kick", "RewardRedeemed"))
		{
			string reply = await BuildAutoReplyTextAsync("Kick", "RewardRedeemed", evt.Username, settings.AutoReplyMessage, variables);
			try
			{
				await _kickService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Kick reward redeemed: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to send auto-reply for Kick reward redeemed");
			}
		}
		await SaveEventStatisticAsync("Kick", "RewardRedeemed", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnKickStreamStatusAsync(KickStreamStatusEvent evt)
	{
		if (KickFilterEnabled)
		{
			ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Kick", "StreamStatus")) ?? new ChatEventSetting
			{
				Platform = "Kick",
				EventType = "StreamStatus",
				ShowInChat = true,
				AutoReplyEnabled = false,
				SoundEnabled = false
			};
			string statusText = (evt.IsLive ? "\ud83d\udd34 Stream is now LIVE" : "? Stream ended");
			string message = statusText + ": " + evt.Title;
			if (!string.IsNullOrEmpty(evt.Category))
			{
				message = message + " (" + evt.Category + ")";
			}
			if (_showEventsInChat && settings.ShowInChat)
			{
				await AddSystemMessageAsync("Kick", message);
			}
			Dictionary<string, string> variables = new Dictionary<string, string>
			{
				{
					"islive",
					evt.IsLive.ToString()
				},
				{ "title", evt.Title },
				{
					"category",
					evt.Category ?? ""
				},
				{ "platform", "Kick" }
			};
			await SaveEventStatisticAsync("Kick", "StreamStatus", "System", variables, wasAutoReplied: false);
		}
	}

	private async Task OnTwitchFollowAsync(TwitchFollowEvent evt)
	{
		if (!TwitchFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Twitch", "Follow")) ?? new ChatEventSetting
		{
			Platform = "Twitch",
			EventType = "Follow",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		if (_viewerStatusService != null)
		{
			try
			{
				await _viewerStatusService.RecordFollowAsync(evt.Username, "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogWarning(ex4, "Failed to record Twitch follow status for {Username}", evt.Username);
			}
		}
		bool wasAutoReplied = false;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string message = "\ud83c\udf89 " + evt.DisplayName + " just followed!";
			await AddSystemMessageAsync("Twitch", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("follow", "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to play sound for Twitch follow");
			}
		}
		await ShowEventAlertAsync("New Follower!", evt.DisplayName, null, "Twitch");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.Username, "Twitch", "follow");
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to award currency bonus for Twitch follow");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{ "displayname", evt.DisplayName },
			{ "platform", "Twitch" }
		};
		if (_twitchAutoRepliesEnabled && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Follow"))
		{
			string reply = await BuildAutoReplyTextAsync("Twitch", "Follow", evt.Username, settings.AutoReplyMessage, variables);
			try
			{
				await _twitchService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Twitch follow: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex11 = ex;
				Exception ex12 = ex11;
				Exception ex13 = ex12;
				_logger.LogError(ex13, "Failed to send auto-reply for Twitch follow");
			}
		}
		await SaveEventStatisticAsync("Twitch", "Follow", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnTwitchSubscribeAsync(TwitchSubscribeEvent evt)
	{
		if (!TwitchFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Twitch", "Subscribe")) ?? new ChatEventSetting
		{
			Platform = "Twitch",
			EventType = "Subscribe",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		if (_viewerStatusService != null)
		{
			try
			{
				await _viewerStatusService.RecordSubscriptionAsync(evt.Username, "Twitch", SubTierParser.Parse(evt.Tier), evt.MonthsTotal);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogWarning(ex4, "Failed to record Twitch subscription status for {Username}", evt.Username);
			}
		}
		bool wasAutoReplied = false;
		string tier = evt.Tier;
		if (1 == 0)
		{
		}
		string text = tier switch
		{
			"1000" => "Tier 1", 
			"2000" => "Tier 2", 
			"3000" => "Tier 3", 
			_ => "Tier Unknown", 
		};
		if (1 == 0)
		{
		}
		string text2 = text;
		string text3 = text2;
		string tierText = text3;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string message = (evt.IsGift ? $"? {evt.DisplayName} received a {tierText} gift sub!" : $"? {evt.DisplayName} subscribed at {tierText}!");
			await AddSystemMessageAsync("Twitch", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("subscribe", "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to play sound for Twitch subscribe");
			}
		}
		string alertTitle = (evt.IsGift ? "Gift Sub!" : "New Subscriber!");
		await ShowEventAlertAsync(alertTitle, evt.DisplayName, tierText, "Twitch");
		if (_currencyService != null && !evt.IsGift)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.Username, "Twitch", "subscribe");
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to award currency bonus for Twitch subscribe");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{ "displayname", evt.DisplayName },
			{ "tier", evt.Tier },
			{ "platform", "Twitch" }
		};
		if (_twitchAutoRepliesEnabled && !evt.IsGift && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Subscribe"))
		{
			string reply = await BuildAutoReplyTextAsync("Twitch", "Subscribe", evt.Username, settings.AutoReplyMessage, variables);
			try
			{
				await _twitchService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Twitch subscribe: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex11 = ex;
				Exception ex12 = ex11;
				Exception ex13 = ex12;
				_logger.LogError(ex13, "Failed to send auto-reply for Twitch subscribe");
			}
		}
		await SaveEventStatisticAsync("Twitch", "Subscribe", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnTwitchResubAsync(TwitchResubEvent evt)
	{
		if (!TwitchFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Twitch", "Resub")) ?? new ChatEventSetting
		{
			Platform = "Twitch",
			EventType = "Resub",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		if (_viewerStatusService != null)
		{
			try
			{
				await _viewerStatusService.RecordSubscriptionAsync(evt.Username, "Twitch", SubTierParser.Parse(evt.Tier), evt.CumulativeMonths);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogWarning(ex4, "Failed to record Twitch resub status for {Username}", evt.Username);
			}
		}
		bool wasAutoReplied = false;
		string tier = evt.Tier;
		if (1 == 0)
		{
		}
		string text = tier switch
		{
			"1000" => "Tier 1", 
			"2000" => "Tier 2", 
			"3000" => "Tier 3", 
			_ => "Tier Unknown", 
		};
		if (1 == 0)
		{
		}
		string text2 = text;
		string text3 = text2;
		string tierText = text3;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string message = $"? {evt.Username} resubscribed at {tierText} ({evt.CumulativeMonths} months total)!";
			if (!string.IsNullOrEmpty(evt.Message))
			{
				message = message + " - " + evt.Message;
			}
			await AddSystemMessageAsync("Twitch", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("resub", "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to play sound for Twitch resub");
			}
		}
		await ShowEventAlertAsync("Resubscribed!", evt.Username, $"{tierText} - {evt.CumulativeMonths} months!", "Twitch");
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{ "tier", evt.Tier },
			{
				"months",
				evt.CumulativeMonths.ToString()
			},
			{
				"streak",
				evt.StreakMonths.ToString()
			},
			{ "platform", "Twitch" }
		};
		if (_twitchAutoRepliesEnabled && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Resub"))
		{
			string reply = await BuildAutoReplyTextAsync("Twitch", "Resub", evt.Username, settings.AutoReplyMessage, variables);
			try
			{
				await _twitchService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Twitch resub: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to send auto-reply for Twitch resub");
			}
		}
		await SaveEventStatisticAsync("Twitch", "Resub", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnTwitchGiftSubAsync(TwitchGiftSubEvent evt)
	{
		if (!TwitchFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Twitch", "GiftSub")) ?? new ChatEventSetting
		{
			Platform = "Twitch",
			EventType = "GiftSub",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		string tier = evt.Tier;
		if (1 == 0)
		{
		}
		string text = tier switch
		{
			"1000" => "Tier 1", 
			"2000" => "Tier 2", 
			"3000" => "Tier 3", 
			_ => "Tier Unknown", 
		};
		if (1 == 0)
		{
		}
		string text2 = text;
		string text3 = text2;
		string tierText = text3;
		string message = $"\ud83c\udf81 {evt.GifterUsername} gifted {evt.Total} {tierText} sub{((evt.Total > 1) ? "s" : "")}!";
		if (_showEventsInChat && settings.ShowInChat)
		{
			await AddSystemMessageAsync("Twitch", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("giftsub", "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to play sound for Twitch gift sub");
			}
		}
		await ShowEventAlertAsync("Gift Subs!", evt.GifterUsername, $"{evt.Total} {tierText} subs!", "Twitch");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.GifterUsername, "Twitch", "giftsub", evt.Total);
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to award currency bonus for Twitch gift sub");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "gifter", evt.GifterUsername },
			{ "username", evt.GifterUsername },
			{
				"count",
				evt.Total.ToString()
			},
			{ "tier", evt.Tier },
			{ "platform", "Twitch" }
		};
		if (_twitchAutoRepliesEnabled && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.GifterUsername, "Twitch", "GiftSub"))
		{
			string reply = await BuildAutoReplyTextAsync("Twitch", "GiftSub", evt.GifterUsername, settings.AutoReplyMessage, variables);
			try
			{
				await _twitchService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Twitch gift sub: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to send auto-reply for Twitch gift sub");
			}
		}
		await SaveEventStatisticAsync("Twitch", "GiftSub", evt.GifterUsername, variables, wasAutoReplied);
	}

	private async Task OnTwitchRaidAsync(TwitchRaidEvent evt)
	{
		if (!TwitchFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Twitch", "Raid")) ?? new ChatEventSetting
		{
			Platform = "Twitch",
			EventType = "Raid",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string message = $"\ud83d\ude80 {evt.FromBroadcasterUsername} raided with {evt.Viewers} viewer{((evt.Viewers != 1) ? "s" : "")}!";
			await AddSystemMessageAsync("Twitch", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("raid", "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to play sound for Twitch raid");
			}
		}
		await ShowEventAlertAsync("Raid!", evt.FromBroadcasterUsername, $"{evt.Viewers} viewers!", "Twitch");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.FromBroadcasterUsername, "Twitch", "raid", evt.Viewers);
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to award currency bonus for Twitch raid");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.FromBroadcasterUsername },
			{ "raider", evt.FromBroadcasterUsername },
			{
				"viewers",
				evt.Viewers.ToString()
			},
			{ "platform", "Twitch" }
		};
		if (_twitchAutoRepliesEnabled && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.FromBroadcasterUsername, "Twitch", "Raid"))
		{
			string reply = await BuildAutoReplyTextAsync("Twitch", "Raid", evt.FromBroadcasterUsername, settings.AutoReplyMessage, variables);
			try
			{
				await _twitchService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Twitch raid: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to send auto-reply for Twitch raid");
			}
		}
		await SaveEventStatisticAsync("Twitch", "Raid", evt.FromBroadcasterUsername, variables, wasAutoReplied);
	}

	private async Task OnTwitchCheerAsync(TwitchCheerEvent evt)
	{
		if (!TwitchFilterEnabled)
		{
			return;
		}
		ChatEventSetting settings = (await _eventSettingsService.GetSettingAsync("Twitch", "Cheer")) ?? new ChatEventSetting
		{
			Platform = "Twitch",
			EventType = "Cheer",
			ShowInChat = true,
			AutoReplyEnabled = false,
			SoundEnabled = false
		};
		bool wasAutoReplied = false;
		if (_showEventsInChat && settings.ShowInChat)
		{
			string message = $"\ud83d\udc8e {evt.Username} cheered {evt.Bits} bits!";
			if (!string.IsNullOrEmpty(evt.Message))
			{
				message = message + " - " + evt.Message;
			}
			await AddSystemMessageAsync("Twitch", message);
		}
		if (_soundService != null)
		{
			try
			{
				await _soundService.PlayEventSoundAsync("cheer", "Twitch");
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				Exception ex4 = ex3;
				_logger.LogError(ex4, "Failed to play sound for Twitch cheer");
			}
		}
		await ShowEventAlertAsync("Bits!", evt.Username, $"{evt.Bits} bits!", "Twitch");
		if (_currencyService != null)
		{
			try
			{
				await _currencyService.AwardEventBonusAsync(evt.Username, "Twitch", "cheer", evt.Bits);
			}
			catch (Exception ex)
			{
				Exception ex5 = ex;
				Exception ex6 = ex5;
				Exception ex7 = ex6;
				_logger.LogError(ex7, "Failed to award currency bonus for Twitch cheer");
			}
		}
		Dictionary<string, string> variables = new Dictionary<string, string>
		{
			{ "username", evt.Username },
			{
				"bits",
				evt.Bits.ToString()
			},
			{ "platform", "Twitch" }
		};
		if (_twitchAutoRepliesEnabled && settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) && _autoReplyService.ShouldSendReply(evt.Username, "Twitch", "Cheer"))
		{
			string reply = await BuildAutoReplyTextAsync("Twitch", "Cheer", evt.Username, settings.AutoReplyMessage, variables);
			try
			{
				await _twitchService.SendMessageAsync(reply);
				_logger.LogInformation("Auto-reply sent for Twitch cheer: {Reply}", reply);
				wasAutoReplied = true;
			}
			catch (Exception ex)
			{
				Exception ex8 = ex;
				Exception ex9 = ex8;
				Exception ex10 = ex9;
				_logger.LogError(ex10, "Failed to send auto-reply for Twitch cheer");
			}
		}
		await SaveEventStatisticAsync("Twitch", "Cheer", evt.Username, variables, wasAutoReplied);
	}

	private async Task OnYouTubeLiveChatStateAsync(YouTubeLiveChatStateEvent stateEvent)
	{
		try
		{
			await AddSystemMessageAsync("YouTube", stateEvent.Message);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			Exception ex4 = ex3;
			_logger.LogError(ex4, "Failed to add the YouTube state system message");
		}
	}

	private async Task AddSystemMessageAsync(string platform, string messageText)
	{
		await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
		{
			ChatMessage item = new ChatMessage
			{
				Platform = "Vyral",
				Username = "System",
				Message = messageText,
				Timestamp = DateTime.Now,
				Color = "#FFD700",
				IsSystemMessage = true,
				IsModerator = false,
				IsSubscriber = false,
				IsVip = false,
				IsBroadcaster = false,
				Badges = new Dictionary<string, string>()
			};
			Messages.Add(item);
			while (Messages.Count > 500)
			{
				Messages.RemoveAt(0);
			}
		});
	}

	public void Cleanup()
	{
		_logger.LogInformation("\ud83e\uddf9 ChatViewModel.Cleanup() called - unsubscribing all events and resetting state");
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
			_eventBus.Unsubscribe(_youTubeStateSubscriptionId);
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
		if (_twitchStreamStatusSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_twitchStreamStatusSubscriptionId);
		}
		if (_giveawayAutoEndedSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_giveawayAutoEndedSubscriptionId);
		}
		if (_pollAutoEndedSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_pollAutoEndedSubscriptionId);
		}
		if (_youTubeMembershipStatusSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_youTubeMembershipStatusSubscriptionId);
		}
		if (_accountsLinkedSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_accountsLinkedSubscriptionId);
		}
		if (_viewerGoalCompletedSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_viewerGoalCompletedSubscriptionId);
		}
		if (_giveawayIntakeModeSubscriptionId != Guid.Empty)
		{
			_eventBus.Unsubscribe(_giveawayIntakeModeSubscriptionId);
		}
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
		_twitchStreamStatusSubscriptionId = Guid.Empty;
		_giveawayAutoEndedSubscriptionId = Guid.Empty;
		_pollAutoEndedSubscriptionId = Guid.Empty;
		_youTubeMembershipStatusSubscriptionId = Guid.Empty;
		_accountsLinkedSubscriptionId = Guid.Empty;
		_viewerGoalCompletedSubscriptionId = Guid.Empty;
		_giveawayIntakeModeSubscriptionId = Guid.Empty;
		_isInitialized = false;
		_loadedMessageIds.Clear();
		_historyLoadedCount = 0;
		lock (_processedMessageIdsLock)
		{
			_processedMessageIds.Clear();
			_processedMessageIdOrder.Clear();
		}
		lock (_discordMessageBotIdsLock)
		{
			_discordMessageBotIds.Clear();
			_discordMessageBotIdOrder.Clear();
		}
		lock (_lastDiscordDmByBotLock)
		{
			_lastDiscordDmByBot.Clear();
			_lastDiscordDmByBotOrder.Clear();
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
		try
		{
			Application current = Application.Current;
			if (current != null)
			{
				Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
				if (dispatcher != null)
				{
					dispatcher.Invoke((Action)delegate
					{
						Messages.Clear();
					});
				}
			}
		}
		catch
		{
		}
		_logger.LogInformation("? ChatViewModel cleanup complete - ready for re-initialization");
	}

	private async Task ShowEventAlertAsync(string title, string username, string? message = null, string platform = "")
	{
		if (_alertService == null)
		{
			return;
		}
		try
		{
			await _alertService.ShowAlertAsync(title, username, message);
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "Failed to show alert for {Title}", title);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedDmConversationChanged(DmConversation? value)
	{
		OnPropertyChanged("HasSelectedDmConversation");
		OnPropertyChanged("ShowDmInboxList");
		// Unified reply box: DM mode on exactly while a thread is open.
		IsDmReplyMode = value != null;
		if (value != null && value.UnreadCount > 0)
		{
			value.UnreadCount = 0;
		}
		_ = LoadDmThreadHistoryAsync(value);
		DmReplyInput = "";
		if (value != null)
		{
			LoadDmThreadHistoryAsync(value);
		}
		ScrollDmThreadToBottom();
		SendDmReplyCommand.NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDmReplyInputChanged(string value)
	{
		SendDmReplyCommand.NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnShowDmPanelChanged(bool value)
	{
		// Opening the DM panel closes the Connected Channels panel (the
		// correction back happens in OnShowChannelsPanelChanged).
		if (value && ShowChannelsPanel)
		{
			ShowChannelsPanel = false;
		}
		if (!value && CombinedView)
		{
			CombinedView = false;
		}
		if (value)
		{
			ScrollDmThreadToBottom();
		}
		else if (SelectedDmConversation != null)
		{
			// The DM panel is gone, so its thread can no longer be seen —
			// leave DM reply mode instead of leaving the main input silently
			// composing DMs for a hidden conversation.
			SelectedDmConversation = null;
		}
		OnPropertyChanged("DmOnlyView");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCombinedViewChanged(bool value)
	{
		if (value && !ShowDmPanel)
		{
			ShowDmPanel = true;
		}
		if (!value && ShowDmPanel)
		{
			ShowDmPanel = false;
		}
		OnPropertyChanged("DmOnlyView");
		ScrollDmThreadToBottom();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnShowChannelsPanelChanged(bool value)
	{
		// Exactly one of {chat, DM panel, channels panel} is visible: opening
		// the channels panel closes the DM panel (whose handler then drops
		// CombinedView, which needs the DM panel). The DM panel opening closes
		// this one back (see OnShowDmPanelChanged).
		if (value && ShowDmPanel)
		{
			ShowDmPanel = false;
		}
		if (value)
		{
			_ = LoadConnectedChannelsAsync();
		}
	}

	private async Task RefreshChannelsAsync()
	{
		await LoadConnectedChannelsAsync();
	}

	/// <summary>
	/// Rebuilds the Connected Channels rows from the primary bot's guild
	/// snapshot overlayed with the stored registry rows (the same bot the mod
	/// command gate keys on — see TryHandleDiscordModCommandAsync). Stored
	/// channels missing from the snapshot stay listed as stale rows so the
	/// user can still untick them.
	/// </summary>
	private async Task LoadConnectedChannelsAsync()
	{
		if (ChannelsLoading)
		{
			return;
		}
		ChannelsLoading = true;
		try
		{
			IDiscordService? originBot = ResolveDiscordSendTarget(null);
			string botId = originBot?.BotId ?? "";

			Dictionary<string, ConnectedChannel> storedByChannelId = new Dictionary<string, ConnectedChannel>(StringComparer.Ordinal);
			if (_connectedChannels != null)
			{
				foreach (ConnectedChannel stored in await _connectedChannels.GetChannelsAsync(botId))
				{
					storedByChannelId[stored.ChannelId] = stored;
				}
			}

			IDiscordService snapshotSource = originBot ?? _discordService;
			List<ConnectedChannelItem> rows = new List<ConnectedChannelItem>();
			HashSet<string> seenChannelIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (DiscordGuildInfo guild in snapshotSource.ConnectedGuilds)
			{
				foreach (DiscordChannelInfo channel in guild.TextChannels)
				{
					// The same channel id can appear under several guilds on
					// some backends — keep the first (snapshot order) row.
					if (!seenChannelIds.Add(channel.Id))
					{
						continue;
					}
					storedByChannelId.TryGetValue(channel.Id, out ConnectedChannel? stored);
					rows.Add(new ConnectedChannelItem
					{
						BotId = botId,
						ChannelId = channel.Id,
						ChannelName = channel.Name,
						GuildId = guild.Id,
						GuildName = guild.Name,
						ExistsInGuild = true,
						MonitoringEnabled = stored?.MonitoringEnabled ?? false,
						IsModeration = stored != null && ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Moderation),
						IsAdmin = stored != null && ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Admin),
						IsSupport = stored != null && ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Support),
						IsLogging = stored != null && ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Logging)
					});
				}
			}

			foreach (ConnectedChannel stored in storedByChannelId.Values)
			{
				if (seenChannelIds.Contains(stored.ChannelId))
				{
					continue;
				}
				rows.Add(new ConnectedChannelItem
				{
					BotId = botId,
					ChannelId = stored.ChannelId,
					ChannelName = stored.ChannelName,
					GuildId = stored.GuildId,
					GuildName = stored.GuildName,
					ExistsInGuild = false,
					MonitoringEnabled = stored.MonitoringEnabled,
					IsModeration = ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Moderation),
					IsAdmin = ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Admin),
					IsSupport = ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Support),
					IsLogging = ChannelPurposes.HasPurpose(stored.Purposes, ChannelPurposes.Logging)
				});
			}

			rows.Sort((a, b) =>
			{
				int byMonitoring = b.MonitoringEnabled.CompareTo(a.MonitoringEnabled);
				return byMonitoring != 0 ? byMonitoring : string.Compare(a.ChannelName, b.ChannelName, StringComparison.OrdinalIgnoreCase);
			});

			await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				ConnectedChannels.Clear();
				foreach (ConnectedChannelItem row in rows)
				{
					ConnectedChannels.Add(row);
				}
			});
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to load connected channels");
		}
		finally
		{
			ChannelsLoading = false;
		}
	}

	// The CheckBox flips its bound flag through the TwoWay binding BEFORE the
	// command runs, so these handlers only refresh the summary and persist the
	// new state — never flip the bool again here or the UI desyncs (same
	// trust-the-binding approach as DiscordBotDetailViewModel's toggles).
	private Task ToggleChannelMonitoringAsync(ConnectedChannelItem? item) => PersistChannelChangeAsync(item);

	private Task ToggleModerationPurposeAsync(ConnectedChannelItem? item) => PersistChannelChangeAsync(item);

	private Task ToggleAdminPurposeAsync(ConnectedChannelItem? item) => PersistChannelChangeAsync(item);

	private Task ToggleSupportPurposeAsync(ConnectedChannelItem? item) => PersistChannelChangeAsync(item);

	private Task ToggleLoggingPurposeAsync(ConnectedChannelItem? item) => PersistChannelChangeAsync(item);

	private async Task PersistChannelChangeAsync(ConnectedChannelItem? item)
	{
		if (item == null)
		{
			return;
		}
		item.RefreshPurposeSummary();
		await PersistChannelAsync(item);
	}

	/// <summary>
	/// Upserts the row while anything is configured; a row left with no state
	/// is deleted so the registry returns to "zero rows → legacy mode"
	/// (commands then work in every channel again).
	/// </summary>
	private async Task PersistChannelAsync(ConnectedChannelItem item)
	{
		if (_connectedChannels == null)
		{
			return;
		}
		try
		{
			if (item.MonitoringEnabled || item.PurposeCsv.Length > 0)
			{
				await _connectedChannels.UpsertAsync(new ConnectedChannel
				{
					BotId = item.BotId,
					ChannelId = item.ChannelId,
					ChannelName = item.ChannelName,
					GuildId = item.GuildId,
					GuildName = item.GuildName,
					Purposes = item.PurposeCsv,
					MonitoringEnabled = item.MonitoringEnabled
				});
			}
			else
			{
				await _connectedChannels.DeleteAsync(item.BotId, item.ChannelId);
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to persist connected channel {ChannelId}", item.ChannelId);
		}
	}

	private async Task SetAsMainChannelAsync(ConnectedChannelItem? item)
	{
		if (item == null)
		{
			return;
		}
		IDiscordService? bot = ResolveDiscordSendTarget(null);
		if (bot == null)
		{
			await AddSystemMessageAsync("Discord", "No Discord bot is connected.");
			return;
		}
		try
		{
			await bot.SetSelectedChannelAsync(item.ChannelId);
			_logger.LogInformation("Discord active channel set to {ChannelId} from the connected-channels panel", item.ChannelId);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to set the Discord active channel to {ChannelId}", item.ChannelId);
			await AddSystemMessageAsync("Discord", $"Couldn't set #{item.ChannelName} as the active channel: {exception.Message}");
		}
	}

	/// <summary>
	/// Closes the panel and points the bot's active channel at the row so the
	/// Discord feed streams it; the chat has no per-channel Discord filter to
	/// select, so a system note makes the switch visible instead.
	/// </summary>
	private async Task OpenChannelInChatAsync(ConnectedChannelItem? item)
	{
		if (item == null)
		{
			return;
		}
		ShowChannelsPanel = false;
		IDiscordService? bot = ResolveDiscordSendTarget(null);
		if (bot == null)
		{
			await AddSystemMessageAsync("Discord", "No Discord bot is connected.");
			return;
		}
		try
		{
			await bot.SetSelectedChannelAsync(item.ChannelId);
			await AddSystemMessageAsync("Discord", $"Now viewing #{item.ChannelName} in {item.GuildName}.");
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to open Discord channel {ChannelId} in chat", item.ChannelId);
		}
	}
}
