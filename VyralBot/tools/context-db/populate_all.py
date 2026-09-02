"""
VyralBot Context Database — Full Population Script
=====================================================
Populates ALL tables with comprehensive project context.
Run after init_schema.py has created the database.
"""

import sys
import os

# Add parent to path so we can import populate helpers
sys.path.insert(0, os.path.dirname(__file__))
from populate import *


def main():
    print("=" * 60)
    print("VyralBot Context Database — Full Population")
    print("=" * 60)

    # ============================================================
    # 1. PROJECT METADATA
    # ============================================================
    print("\n[1/9] Inserting project metadata...")
    insert_project_metadata("project_name", "VyralBot")
    insert_project_metadata("version", "1.0.0")
    insert_project_metadata("framework", ".NET 10.0 (net10.0-windows)")
    insert_project_metadata("ui_framework", "WPF (MVVM with CommunityToolkit.Mvvm)")
    insert_project_metadata("database", "SQLite + Dapper 2.1.35")
    insert_project_metadata("logging", "Serilog 4.1.0")
    insert_project_metadata("di_container", "Microsoft.Extensions.DependencyInjection 9.0.0")
    insert_project_metadata("hosting", "Microsoft.Extensions.Hosting 9.0.0")
    insert_project_metadata("description", "Open-source, localhost-first streaming automation platform. Fastest, leanest alternative to StreamerBot and Mix It Up.")
    insert_project_metadata("target_memory", "40-80 MB RAM")
    insert_project_metadata("target_cpu_idle", "<1%")
    insert_project_metadata("target_cpu_active", "2-3%")
    insert_project_metadata("target_threads", "8-12 managed")
    insert_project_metadata("child_process_policy", "Zero child processes (strict)")
    insert_project_metadata("gc_mode", "Server GC, low-latency, tiered compilation")
    insert_project_metadata("platforms_supported", "Twitch, Kick, YouTube, TikTok, Discord")
    insert_project_metadata("solution_file", "src/VyralBot.sln")
    insert_project_metadata("projects", "VyralBot.UI, VyralBot.Core, VyralBot.Data, VyralBot.Integrations, VyralBot.Api, VyralBot.Plugins, VyralBot.Scripting")

    # ============================================================
    # 2. FEATURES
    # ============================================================
    print("[2/9] Inserting features...")

    # -- Core infrastructure --
    f_eventbus = insert_feature(
        "eventbus", "EventBus", "core",
        "In-memory pub/sub event system using System.Threading.Channels. Routes ALL events between modules. Bounded channel (500 capacity), max 4 concurrent handlers, DropOldest backpressure. Shared singleton between WPF UI and Kestrel API.",
        None, "complete",
        None,
        "Central hub — all cross-module communication flows through EventBus. Never call services directly across modules."
    )
    f_triggers = insert_feature(
        "triggers", "Trigger Engine", "core",
        "Evaluates event conditions and determines which actions to execute. Subscribes to EventBus events, matches against trigger rules (event type, platform, conditions, cooldowns), then dispatches action sequences via ActionDispatcher.",
        "Chat Features", "complete",
        ["eventbus", "actions"],
        "Triggers and Actions are the automation backbone. Supported EventTypes: ChatMessage, Follow, Subscribe, GiftSub, Raid, ChannelPoints, Cheer, Reward, Custom. Supported Platforms: Twitch, Kick, YouTube, Discord, OBS, Manual."
    )
    f_actions = insert_feature(
        "actions", "Action Dispatcher", "core",
        "Executes individual or sequences of actions with timeout and cancellation support. Action types include: SendMessage, Delay, HttpRequest, RunScript, SetVariable, AwardCurrency, and more. Each action has JSON configuration.",
        "Chat Features", "complete",
        ["eventbus"],
        "Actions are stored in the BotActions table. ActionDispatcher supports sequential execution with per-action timeout and retry logic."
    )
    f_profiles = insert_feature(
        "profiles", "Profile System", "core",
        "Multi-profile system allowing different stream configurations. Each profile has its own SQLite database, settings, and platform connections. Supports PIN protection. ProfileSwitchedEvent published on change.",
        None, "complete",
        None,
        "Profile is loaded FIRST during startup — all other services depend on the active profile's database path. ProfileAwareDataService dynamically switches the DB connection."
    )
    f_process_mgmt = insert_feature(
        "process-management", "Process Manager", "core",
        "Tracks all spawned external processes and ensures cleanup on exit. Strict zero-child-process policy — VyralBot.exe should be the only process. ProcessManager tracks any necessary external processes and terminates them on shutdown.",
        None, "complete",
        None,
        "CRITICAL RULE: Zero child processes. Never spawn background processes without ProcessManager tracking."
    )
    f_performance = insert_feature(
        "performance", "Performance Monitor", "core",
        "Background hosted service monitoring GC pressure, memory usage, thread count. Enforces performance budgets: <80MB RAM, <1% idle CPU. Uses Server GC, low-latency mode, tiered compilation.",
        None, "complete",
        None,
        "ArrayPool<byte> for buffers, pre-allocated collections, object pooling. EventBus bounded to 500 events. UI event log capped at 50 items."
    )
    f_rules = insert_feature(
        "rules-engine", "Rules / Conditions Engine", "core",
        "Evaluates conditional rules for triggers and auto-replies. Supports value comparisons, status checks, time-based rules, count-based rules. Used by TriggerEngine and AutoReplyService.",
        None, "complete",
        ["triggers", "auto-reply"],
        None
    )

    # -- Platform integrations --
    f_twitch = insert_feature(
        "twitch", "Twitch Integration", "platform",
        "Full Twitch integration: IRC chat (TwitchLib), EventSub WebSocket (follows, subs, raids, channel points, cheers), OAuth token management with DPAPI encryption. Supports auto-connect on startup.",
        "Platforms", "complete",
        ["eventbus"],
        "Two connection types: IRC for chat messages, EventSub WebSocket for events. LightweightTwitchIrc is an optional low-level IRC client for memory efficiency."
    )
    f_kick = insert_feature(
        "kick", "Kick Integration", "platform",
        "Kick platform integration using KickLib library. Chat via WebSocket, OAuth token management. Webhook subscriptions for events (follows, subs, gifts, rewards). Auto-enable webhooks on startup.",
        "Platforms", "complete",
        ["eventbus", "webserver"],
        "Kick webhooks require a public URL — uses ngrok tunnel. KickWebhookSubscriptionService manages subscription lifecycle. KickWebhookValidationMiddleware validates incoming webhooks."
    )
    f_youtube = insert_feature(
        "youtube", "YouTube Integration", "platform",
        "YouTube Live Chat integration via YouTube Data API v3. Polling-based chat message retrieval. Supports memberships, Super Chat, Super Thanks events.",
        "Platforms", "partial",
        ["eventbus"],
        "Polling-based (not WebSocket). Rate limited by YouTube API quotas."
    )
    f_tiktok = insert_feature(
        "tiktok", "TikTok Integration", "platform",
        "TikTok platform connection. OAuth setup view exists. Chat settings view exists. Full implementation scope TBD.",
        "Platforms", "planned",
        ["eventbus"],
        "Setup UI exists but full integration is not yet implemented."
    )
    f_discord = insert_feature(
        "discord", "Discord Integration", "platform",
        "Discord platform connection. OAuth setup view exists. Chat settings view exists. Full implementation scope TBD.",
        "Platforms", "planned",
        ["eventbus"],
        "Setup UI exists but full integration is not yet implemented."
    )

    # -- Chat features --
    f_chat = insert_feature(
        "chat", "Chat System", "chat",
        "Unified chat display showing messages from all connected platforms. Message persistence to SQLite. Platform-specific message formatting with badges, colors, subscriber/moderator indicators. Real-time message display in UI.",
        "Chat Features", "complete",
        ["eventbus", "twitch", "kick", "youtube"],
        "DataChatMessageStore (in UI services) writes to DB. InMemoryChatMessageStore is for dev/test. ChatViewModel subscribes to EventBus BEFORE platforms connect to avoid missing messages."
    )
    f_commands = insert_feature(
        "commands", "Commands System", "chat",
        "Custom chat commands with cooldowns (global and per-user), permissions (everyone/subscriber/moderator/broadcaster), enable/disable per platform. Built-in commands seeded on first run. Usage tracking with count and last-used timestamp.",
        "Chat Features", "complete",
        ["chat", "eventbus"],
        "Commands are case-insensitive, prefixed with '!'. CommandService handles parsing, permission checking, cooldown enforcement. Built-in commands: !commands, !uptime, etc."
    )
    f_auto_reply = insert_feature(
        "auto-reply", "Auto-Reply & Conditional Rules", "chat",
        "Template-based auto-reply system with conditional rules. Per-event configuration (follow, subscribe, raid, cheer, etc.) with custom reply templates. Rate limiting per user/platform/event type. Priority-based rule matching.",
        "Chat Features", "complete",
        ["chat", "eventbus", "rules-engine", "variables"],
        "AutoReplyRateLimiter prevents spam. ConditionalRulesViewModel provides the rule editor UI. ChatEventSettings control per-event visibility and auto-reply behavior."
    )
    f_variables = insert_feature(
        "variables", "Variables System", "chat",
        "User-defined and system variables for use in templates and messages. Variables use {variable-name} syntax in text. Supports nested variables, platform filtering, categories (Global, Twitch, Kick, Custom). VariableReplacementService processes templates.",
        "Utilities", "complete",
        None,
        "System variables are auto-updating (e.g., {viewer-count}, {uptime}). Custom variables are user-created. Variables have kebab-case names. MaxLength enforcement. VariablePickerViewModel provides a selection dialog."
    )

    # -- Stream tools --
    f_currency = insert_feature(
        "currency", "Currency System", "stream-tools",
        "Virtual currency economy for viewers. Earn via watchtime, chat activity, follows, subs, gift subs, raids. Spend via redeemable rewards. Leaderboard ranking. Transaction history. Configurable currency name and symbol.",
        "Stream Tools", "complete",
        ["eventbus", "chat"],
        "Three services: CurrencyService (balances), CurrencyRewardsService (rewards/redemptions), CurrencyCommandService (chat commands like !balance, !redeem). WatchtimeBackgroundService awards currency per watchtime interval."
    )
    f_giveaway = insert_feature(
        "giveaway", "Giveaway System", "stream-tools",
        "Create and manage giveaways with keyword entry. Supports: subscriber bonus entries, minimum watchtime requirement, followers-only, subscribers-only, multi-platform, scheduled end time, multiple winners. Status: Pending → Active → Completed/Cancelled.",
        "Stream Tools", "complete",
        ["eventbus", "chat"],
        "GiveawayService handles creation, entry validation, random winner drawing. GiveawayCommandService handles !enter command. GiveawayEntry tracks per-user entries."
    )
    f_poll = insert_feature(
        "poll", "Poll System", "stream-tools",
        "Create polls with multiple options. Supports: scheduled end time, allow/disallow multiple votes, followers-only, subscribers-only, multi-platform, minimum watchtime. Vote results stored as JSON. Status: Pending → Active → Completed/Cancelled.",
        "Stream Tools", "complete",
        ["eventbus", "chat"],
        "PollService handles creation, voting, results aggregation. PollCommandService handles !vote command. PollVote tracks individual votes."
    )
    f_watchtime = insert_feature(
        "watchtime", "Watchtime Tracking", "stream-tools",
        "Track total and per-session viewer watchtime across platforms. Award currency bonuses per interval. Leaderboard display. WatchtimeBackgroundService runs as hosted service polling active viewers.",
        "Stream Tools", "complete",
        ["eventbus", "currency"],
        "WatchtimeService tracks minutes. WatchtimeCommandService handles !watchtime. WatchtimeBackgroundService periodically awards currency based on CurrencySettings intervals."
    )

    # -- Audio & Alerts --
    f_audio = insert_feature(
        "audio", "Audio & Sounds", "audio-alerts",
        "Sound effect playback on events. Sound file management. Browser source audio via WebSocket. AudioWebSocketBroadcaster distributes audio events to connected OBS browser sources.",
        "Audio & Alerts", "complete",
        ["eventbus"],
        "SoundService plays local sound files. AudioWebSocketServer/Broadcaster handle browser source distribution. ChatEventSettings control per-event sound enable and file/volume."
    )
    f_alerts = insert_feature(
        "alerts", "Alert Overlays", "audio-alerts",
        "On-screen alert overlays for stream events (follows, subs, donations, etc.). AlertOverlayWindow displays alerts. AlertService manages the overlay lifecycle and alert queue.",
        "Audio & Alerts", "complete",
        ["eventbus"],
        "AlertService is in VyralBot.UI/Services. AlertOverlayWindow is a separate WPF window."
    )

    # -- Monitoring --
    f_statistics = insert_feature(
        "statistics", "Statistics & Analytics", "monitoring",
        "Event statistics tracking and display. Records event type, platform, username, event data, timestamp. Analytics aggregation for dashboard display. Graphs and event counts.",
        "Monitoring", "complete",
        ["eventbus"],
        "StatisticsService records EventStatistic entries. StatisticsViewModel displays aggregated data."
    )
    f_event_history = insert_feature(
        "event-history", "Event History", "monitoring",
        "Event log viewer showing chronological history of all events processed by the system. EventLog entries with platform, event type, message, severity, timestamp.",
        "Monitoring", "complete",
        None,
        "EventHistoryViewModel loads from EventLogs table. Capped display for performance."
    )
    f_event_testing = insert_feature(
        "event-testing", "Event Testing", "monitoring",
        "Debug tool for manually firing events into the EventBus. Allows testing triggers, auto-replies, and event chains without live platform connections.",
        "Monitoring", "complete",
        ["eventbus"],
        "EventTestingViewModel provides controls to fire test events of each type."
    )

    # -- Settings & Config --
    f_settings = insert_feature(
        "settings", "Global Settings", "settings",
        "Application-wide settings including theme, behavior toggles, startup options. Stored in Config table as key-value pairs with optional category grouping.",
        "Settings", "complete",
        None,
        "SettingsViewModel exposes all global settings. Config table is a generic key-value store."
    )
    f_webserver = insert_feature(
        "webserver", "Web Server & Webhooks", "settings",
        "Embedded Kestrel HTTP/WebSocket server for external communication. Receives platform webhooks (Twitch, Kick). ngrok tunnel for public URL exposure. WebSocket distribution for browser sources. REST API for external action triggers.",
        "Settings", "complete",
        ["eventbus"],
        "WebServerService manages Kestrel lifecycle. NgrokService manages tunnel. WebhookCoordinatorService orchestrates webhook registrations. KickWebhookSubscriptionService handles Kick-specific webhook management. WebServerDependencyTracker tracks dependent services."
    )

    # -- Utility --
    f_dashboard = insert_feature(
        "dashboard", "Dashboard", "utility",
        "Home dashboard showing quick stats, platform connection status, and system health overview. First view shown after profile selection.",
        "Home", "complete",
        None,
        "DashboardViewModel aggregates data from multiple services for the overview display."
    )
    f_platforms_hub = insert_feature(
        "platforms-hub", "Platforms Hub", "utility",
        "Platform selection and management hub. Shows all available platforms with connection status. Entry point for platform setup wizards (OAuth flows) and per-platform dashboards and settings.",
        "Platforms", "complete",
        ["twitch", "kick", "youtube", "tiktok", "discord"],
        "PlatformsViewModel manages the hub. Each platform has setup wizard → dashboard → settings views."
    )

    print(f"  Inserted {25} features")

    # ============================================================
    # 3. FILES (all source files with summaries)
    # ============================================================
    print("[3/9] Inserting files...")

    # --- VyralBot.Data ---
    fid_dapper = insert_file("src/VyralBot.Data/DapperDataService.cs", "cs", "data", "Primary data access layer. Implements IDataService using Dapper ORM against SQLite. Contains ALL CREATE TABLE statements, all CRUD operations, GuidTypeHandler for SQLite compatibility. 2000+ lines.", f_profiles)
    fid_idataservice = insert_file("src/VyralBot.Data/IDataService.cs", "cs", "data", "Interface defining all data operations across the application. Platform, chat, currency, commands, triggers, actions, giveaways, polls, watchtime, variables, config, webserver, event logging.", f_profiles)
    fid_data_csproj = insert_file("src/VyralBot.Data/VyralBot.Data.csproj", "csproj", "build", "Data layer project. Dependencies: Dapper 2.1.35, Microsoft.Data.Sqlite 9.0.0.", f_profiles)

    # Data entities
    fid_ent_platform = insert_file("src/VyralBot.Data/Entities/Platform.cs", "cs", "entity", "Platform entity: Id, Name, AccessToken, RefreshToken, Username, UserId, ChannelId, ChatroomId, IsConnected, per-platform chat enable/autostart flags.", f_platforms_hub)
    fid_ent_command = insert_file("src/VyralBot.Data/Entities/Command.cs", "cs", "entity", "Command entity: Id, Name, Response, Permission, GlobalCooldown, UserCooldown, IsEnabled, IsBuiltIn, EnabledOnKick/Twitch, UsageCount, LastUsedAt.", f_commands)
    fid_ent_cooldown = insert_file("src/VyralBot.Data/Entities/CommandCooldown.cs", "cs", "entity", "CommandCooldown entity: CommandId, Username, Platform, ExpiresAt. Tracks per-user and global cooldown expiration.", f_commands)
    fid_ent_trigger = insert_file("src/VyralBot.Data/Entities/Trigger.cs", "cs", "entity", "Trigger entity: Id, Name, EventType, Platform, Condition, CooldownSeconds, LastFiredAt, FireCount, Permission, ActionSequence (JSON array of action IDs).", f_triggers)
    fid_ent_action = insert_file("src/VyralBot.Data/Entities/BotAction.cs", "cs", "entity", "BotAction entity: Id, Name, ActionType, Configuration (JSON), TimeoutSeconds, RetryOnFailure, MaxRetries.", f_actions)
    fid_ent_chatmsg = insert_file("src/VyralBot.Data/Entities/ChatMessage.cs", "cs", "entity", "ChatMessage entity: Id, Platform, Channel, MessageId, UserId, Username, DisplayName, Content, Timestamp, Color, IsModerator, IsSubscriber, IsOwner, Badges (JSON).", f_chat)
    fid_ent_chatsetting = insert_file("src/VyralBot.Data/Entities/ChatEventSetting.cs", "cs", "entity", "ChatEventSetting entity: Platform, EventType, ShowInChat, AutoReplyEnabled, AutoReplyMessage, SoundEnabled, SoundFile, SoundVolume.", f_auto_reply)
    fid_ent_autorule = insert_file("src/VyralBot.Data/Entities/AutoReplyRule.cs", "cs", "entity", "AutoReplyRule entity: Name, Platform, EventType, IsEnabled, Priority, ConditionJson, ReplyTemplate, TriggerCount, LastTriggeredAt.", f_auto_reply)
    fid_ent_eventstat = insert_file("src/VyralBot.Data/Entities/EventStatistic.cs", "cs", "entity", "EventStatistic entity: Platform, EventType, Username, EventData (JSON), Timestamp, WasAutoReplied.", f_statistics)
    fid_ent_eventlog = insert_file("src/VyralBot.Data/Entities/EventLog.cs", "cs", "entity", "EventLog entity: Platform, EventType, Message, Severity, Timestamp.", f_event_history)
    fid_ent_variable = insert_file("src/VyralBot.Data/Entities/Variable.cs", "cs", "entity", "Variable entity: Id, Name (kebab-case), DisplayName, Category, Value, MaxLength, IsEditable, IsSystem, IsAutoUpdating, ContainsNestedVariables, Platforms, Description.", f_variables)
    fid_ent_config = insert_file("src/VyralBot.Data/Entities/Config.cs", "cs", "entity", "Config entity: Key, Value, Category. Generic key-value configuration store.", f_settings)
    fid_ent_currency_setting = insert_file("src/VyralBot.Data/Entities/CurrencySetting.cs", "cs", "entity", "CurrencySetting entity: CurrencyName, Symbol, WatchTime/ChatActivity/Follow/Sub/GiftSub/Raid bonus enabled flags and amounts.", f_currency)
    fid_ent_user_currency = insert_file("src/VyralBot.Data/Entities/UserCurrency.cs", "cs", "entity", "UserCurrency entity: Username, Platform, Balance, LastUpdated.", f_currency)
    fid_ent_currency_tx = insert_file("src/VyralBot.Data/Entities/CurrencyTransaction.cs", "cs", "entity", "CurrencyTransaction entity: Username, Platform, Amount, TransactionType (Earn/Spend/Admin), Reason, Timestamp.", f_currency)
    fid_ent_currency_reward = insert_file("src/VyralBot.Data/Entities/CurrencyReward.cs", "cs", "entity", "CurrencyReward entity: Name, Cost, Description, Type.", f_currency)
    fid_ent_currency_redeem = insert_file("src/VyralBot.Data/Entities/CurrencyRedemption.cs", "cs", "entity", "CurrencyRedemption entity: Username, RewardId, Status (Pending/Completed/Rejected), CreatedAt.", f_currency)
    fid_ent_giveaway = insert_file("src/VyralBot.Data/Entities/Giveaway.cs", "cs", "entity", "Giveaway entity: Name, EntryKeyword, Status, StartedAt, EndedAt, ScheduledEndTime, WinnerCount, SubscriberBonusEnabled, MinWatchtimeMinutes, AllowedPlatforms, FollowersOnly, SubscribersOnly, Winners, TotalEntries, UniqueParticipants.", f_giveaway)
    fid_ent_giveaway_entry = insert_file("src/VyralBot.Data/Entities/GiveawayEntry.cs", "cs", "entity", "GiveawayEntry entity: GiveawayId, Username, Platform, EnteredAt.", f_giveaway)
    fid_ent_poll = insert_file("src/VyralBot.Data/Entities/Poll.cs", "cs", "entity", "Poll entity: Question, Options (JSON), Status, CreatedAt, StartedAt, EndedAt, ScheduledEndTime, CreatorUsername, TotalVotes, AllowMultipleVotes, AllowedPlatforms, VoteResults (JSON).", f_poll)
    fid_ent_pollvote = insert_file("src/VyralBot.Data/Entities/PollVote.cs", "cs", "entity", "PollVote entity: PollId, Username, Platform, SelectedOption, VotedAt.", f_poll)
    fid_ent_watchtime = insert_file("src/VyralBot.Data/Entities/UserWatchtime.cs", "cs", "entity", "UserWatchtime entity: Username, Platform, TotalMinutes, LastSeenAt, FirstSeenAt, StreamSessionMinutes, LastStreamId, IsActive.", f_watchtime)
    fid_ent_webserver = insert_file("src/VyralBot.Data/Entities/WebServerEntities.cs", "cs", "entity", "WebServerSettings + WebhookSubscription entities. Server port, ngrok config, webhook registration tracking.", f_webserver)

    # --- VyralBot.Core ---
    fid_core_csproj = insert_file("src/VyralBot.Core/VyralBot.Core.csproj", "csproj", "build", "Core layer project. Dependencies: Serilog, Microsoft.Extensions.Hosting/DI/Logging, System.Threading.Channels.", f_eventbus)
    fid_core_usings = insert_file("src/VyralBot.Core/Usings.cs", "cs", "config", "Global using directives for VyralBot.Core namespace.", f_eventbus)

    # Core services - EventBus
    fid_ievent = insert_file("src/VyralBot.Core/Services/EventBus/IEvent.cs", "cs", "service", "IEvent interface + EventBase record. Core event contract: EventId, Timestamp, Source, Metadata.", f_eventbus)
    fid_ieventbus = insert_file("src/VyralBot.Core/Services/EventBus/IEventBus.cs", "cs", "service", "IEventBus interface: PublishAsync, SubscribeAsync, Unsubscribe, SubscriptionCount.", f_eventbus)
    fid_eventbus = insert_file("src/VyralBot.Core/Services/EventBus/EventBus.cs", "cs", "service", "EventBus implementation. Bounded Channel<T> (500 capacity, DropOldest). SemaphoreSlim throttling (max 4 concurrent). Background processing task. ConcurrentDictionary subscriptions.", f_eventbus)
    fid_profile_event = insert_file("src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs", "cs", "service", "ProfileSwitchedEvent: Published when user switches profiles. Contains old/new profile names.", f_profiles)

    # Core services - TriggerEngine
    fid_itrigger = insert_file("src/VyralBot.Core/Services/TriggerEngine/ITriggerEngine.cs", "cs", "service", "ITriggerEngine interface: StartAsync, StopAsync, FireTriggerAsync, ReloadTriggersAsync, ActiveTriggerCount.", f_triggers)
    fid_trigger_engine = insert_file("src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs", "cs", "service", "TriggerEngine implementation. Subscribes to EventBus events, matches against stored triggers, fires action sequences. Handles cooldowns and permissions.", f_triggers)

    # Core services - ActionDispatcher
    fid_iaction = insert_file("src/VyralBot.Core/Services/ActionDispatcher/IActionDispatcher.cs", "cs", "service", "IActionDispatcher interface: ExecuteActionAsync, ExecuteSequenceAsync, CancelTriggerActions, ActiveActionCount.", f_actions)
    fid_action_dispatch = insert_file("src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs", "cs", "service", "ActionDispatcher implementation. Executes actions with timeout/cancellation. Supports sequential execution with per-action retry logic.", f_actions)

    # Core services - Chat
    fid_ichatstore = insert_file("src/VyralBot.Core/Services/Chat/IChatMessageStore.cs", "cs", "service", "IChatMessageStore interface: SaveChatMessageAsync, GetChatMessagesAsync.", f_chat)
    fid_inmem_chatstore = insert_file("src/VyralBot.Core/Services/Chat/InMemoryChatMessageStore.cs", "cs", "service", "In-memory chat message store for dev/testing. Not used in production.", f_chat)
    fid_ichatevtsettings = insert_file("src/VyralBot.Core/Services/Chat/IChatEventSettingsService.cs", "cs", "service", "IChatEventSettingsService interface: GetSettingAsync, SaveSettingAsync, GetAllSettingsAsync.", f_auto_reply)
    fid_chatevtsettings = insert_file("src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs", "cs", "service", "ChatEventSettingsService implementation. Manages per-event settings for chat visibility, auto-reply, sound.", f_auto_reply)
    fid_iauto_reply = insert_file("src/VyralBot.Core/Services/Chat/IAutoReplyService.cs", "cs", "service", "IAutoReplyService interface: GetAutoReplyAsync, ShouldSendReply.", f_auto_reply)
    fid_auto_reply = insert_file("src/VyralBot.Core/Services/Chat/AutoReplyService.cs", "cs", "service", "AutoReplyService implementation. Conditional rule evaluation, template variable replacement, rate limit checking.", f_auto_reply)
    fid_rate_limiter = insert_file("src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs", "cs", "service", "Rate limiter for auto-replies. Prevents spam by tracking per user/platform/event type cooldowns.", f_auto_reply)

    # Core services - Commands
    fid_icmd = insert_file("src/VyralBot.Core/Services/Commands/ICommandService.cs", "cs", "service", "ICommandService interface: ParseAndExecuteAsync, GetCommandAsync, GetCommandsAsync, SaveCommandAsync, DeleteCommandAsync.", f_commands)
    fid_cmd = insert_file("src/VyralBot.Core/Services/Commands/CommandService.cs", "cs", "service", "CommandService implementation. Parses chat messages for !commands, checks permissions and cooldowns, executes command responses with variable replacement.", f_commands)

    # Core services - Currency
    fid_icurrency = insert_file("src/VyralBot.Core/Services/Currency/ICurrencyService.cs", "cs", "service", "ICurrencyService interface: GetBalanceAsync, AddCurrencyAsync, RemoveCurrencyAsync, GetLeaderboardAsync.", f_currency)
    fid_currency = insert_file("src/VyralBot.Core/Services/Currency/CurrencyService.cs", "cs", "service", "CurrencyService implementation. Balance management, currency earning from events, leaderboard calculation.", f_currency)
    fid_icurrency_rewards = insert_file("src/VyralBot.Core/Services/Currency/ICurrencyRewardsService.cs", "cs", "service", "ICurrencyRewardsService interface: GetRewardsAsync, RedeemRewardAsync, CreateRewardAsync.", f_currency)
    fid_currency_rewards = insert_file("src/VyralBot.Core/Services/Currency/CurrencyRewardsService.cs", "cs", "service", "CurrencyRewardsService implementation. Reward management, redemption processing.", f_currency)
    fid_icurrency_cmd = insert_file("src/VyralBot.Core/Services/Currency/ICurrencyCommandService.cs", "cs", "service", "ICurrencyCommandService interface: HandleCurrencyCommandAsync.", f_currency)
    fid_currency_cmd = insert_file("src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs", "cs", "service", "CurrencyCommandService implementation. Handles !balance, !redeem, !leaderboard chat commands.", f_currency)

    # Core services - Giveaway/Poll/Watchtime
    fid_giveaway_svc = insert_file("src/VyralBot.Core/Services/GiveawayService.cs", "cs", "service", "GiveawayService: Create, start, draw winners, end giveaways. Entry validation (watchtime, subscriber, platform).", f_giveaway)
    fid_giveaway_cmd = insert_file("src/VyralBot.Core/Services/GiveawayCommandService.cs", "cs", "service", "GiveawayCommandService: Handles !enter command for giveaway participation.", f_giveaway)
    fid_poll_svc = insert_file("src/VyralBot.Core/Services/PollService.cs", "cs", "service", "PollService: Create polls, record votes, aggregate results, end polls.", f_poll)
    fid_poll_cmd = insert_file("src/VyralBot.Core/Services/PollCommandService.cs", "cs", "service", "PollCommandService: Handles !vote command for poll participation.", f_poll)
    fid_watchtime_svc = insert_file("src/VyralBot.Core/Services/WatchtimeService.cs", "cs", "service", "WatchtimeService: Track viewer minutes, update session and total watchtime.", f_watchtime)
    fid_watchtime_cmd = insert_file("src/VyralBot.Core/Services/WatchtimeCommandService.cs", "cs", "service", "WatchtimeCommandService: Handles !watchtime chat command.", f_watchtime)
    fid_watchtime_bg = insert_file("src/VyralBot.Core/Services/WatchtimeBackgroundService.cs", "cs", "service", "WatchtimeBackgroundService: Hosted service that periodically awards currency based on active viewer watchtime.", f_watchtime)

    # Core services - Variables
    fid_ivar = insert_file("src/VyralBot.Core/Services/Variables/IVariableReplacementService.cs", "cs", "service", "IVariableReplacementService interface: ReplaceVariablesAsync for processing {variable} syntax in text.", f_variables)
    fid_var = insert_file("src/VyralBot.Core/Services/Variables/VariableReplacementService.cs", "cs", "service", "VariableReplacementService implementation. Resolves {variable-name} in strings. Supports nested variables, platform filtering, auto-updating system variables.", f_variables)

    # Core services - Audio
    fid_isound = insert_file("src/VyralBot.Core/Services/Audio/ISoundService.cs", "cs", "service", "ISoundService interface: PlaySoundAsync, StopAll.", f_audio)
    fid_sound = insert_file("src/VyralBot.Core/Services/Audio/SoundService.cs", "cs", "service", "SoundService implementation. Plays local audio files using system audio.", f_audio)
    fid_iaudio_ws = insert_file("src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs", "cs", "service", "IAudioWebSocketServer interface for browser source audio distribution.", f_audio)
    fid_audio_ws = insert_file("src/VyralBot.Core/Services/Audio/AudioWebSocketServer.cs", "cs", "service", "AudioWebSocketServer: WebSocket server for OBS browser source audio events.", f_audio)
    fid_audio_bc = insert_file("src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs", "cs", "service", "AudioWebSocketBroadcaster in Core: Broadcasts audio events to connected browser sources.", f_audio)

    # Core services - Other
    fid_rule_eval = insert_file("src/VyralBot.Core/Services/Rules/RuleEvaluator.cs", "cs", "service", "RuleEvaluator: Evaluates conditional rules. Value comparisons, status checks, time-based rules, count-based rules.", f_rules)
    fid_stats_svc = insert_file("src/VyralBot.Core/Services/Statistics/StatisticsService.cs", "cs", "service", "StatisticsService: Records EventStatistic entries, provides aggregated analytics data.", f_statistics)
    fid_iprocess = insert_file("src/VyralBot.Core/Services/ProcessManagement/IProcessManager.cs", "cs", "service", "IProcessManager interface: TrackProcess, StopAll, RunningProcessCount.", f_process_mgmt)
    fid_process = insert_file("src/VyralBot.Core/Services/ProcessManagement/ProcessManager.cs", "cs", "service", "ProcessManager implementation. Tracks all external processes, ensures cleanup on app exit.", f_process_mgmt)
    fid_iprofile = insert_file("src/VyralBot.Core/Services/Profiles/IProfileService.cs", "cs", "service", "IProfileService interface: GetCurrentProfileAsync, GetProfilesAsync, CreateProfileAsync, SwitchProfileAsync, ValidateProfilePinAsync.", f_profiles)
    fid_profile_svc = insert_file("src/VyralBot.Core/Services/Profiles/ProfileService.cs", "cs", "service", "ProfileService implementation. Profile-specific database paths, metadata, PIN verification, ProfileSwitchedEvent publishing.", f_profiles)
    fid_perf_monitor = insert_file("src/VyralBot.Core/Services/Performance/PerformanceMonitor.cs", "cs", "service", "PerformanceMonitor: Background hosted service monitoring GC pressure, memory, threads.", f_performance)
    fid_event_pool = insert_file("src/VyralBot.Core/Services/Performance/EventPool.cs", "cs", "service", "EventPool: Object pooling for event instances to reduce GC pressure.", f_performance)
    fid_pooled_buf = insert_file("src/VyralBot.Core/Services/Performance/PooledBuffer.cs", "cs", "service", "PooledBuffer: ArrayPool<byte> wrapper for buffer reuse.", f_performance)
    fid_ws_dep_tracker = insert_file("src/VyralBot.Core/Services/WebServer/WebServerDependencyTracker.cs", "cs", "service", "WebServerDependencyTracker: Tracks services that depend on the web server being running.", f_webserver)

    # Core models
    fid_action_ctx = insert_file("src/VyralBot.Core/Models/ActionContext.cs", "cs", "model", "ActionContext: Context passed to action execution — event data, variables, platform, username, etc.", f_actions)
    fid_profile_model = insert_file("src/VyralBot.Core/Models/Profile.cs", "cs", "model", "Profile model: Name, DisplayName, Pin, DatabasePath, CreatedAt.", f_profiles)
    fid_rule_condition = insert_file("src/VyralBot.Core/Models/Rules/RuleCondition.cs", "cs", "model", "RuleCondition model: Field, Operator, Value, LogicalOperator for rule evaluation.", f_rules)

    # --- VyralBot.Integrations ---
    fid_integ_csproj = insert_file("src/VyralBot.Integrations/VyralBot.Integrations.csproj", "csproj", "build", "Integrations layer. Dependencies: TwitchLib.Api, TwitchLib.EventSub.Websockets, KickLib 1.8.0.", f_twitch)

    # Twitch
    fid_itwitch = insert_file("src/VyralBot.Integrations/Twitch/ITwitchService.cs", "cs", "service", "ITwitchService interface: ConnectAsync, DisconnectAsync, SendMessageAsync, IsConnected, Channel, BotUsername.", f_twitch)
    fid_twitch_svc = insert_file("src/VyralBot.Integrations/Twitch/TwitchService.cs", "cs", "service", "TwitchService: IRC chat client using TwitchLib. Publishes TwitchChatMessageEvent to EventBus. Auto-reconnection.", f_twitch)
    fid_itwitch_eventsub = insert_file("src/VyralBot.Integrations/Twitch/ITwitchEventSubService.cs", "cs", "service", "ITwitchEventSubService interface: ConnectAsync, DisconnectAsync, subscription management.", f_twitch)
    fid_twitch_eventsub = insert_file("src/VyralBot.Integrations/Twitch/TwitchEventSubService.cs", "cs", "service", "TwitchEventSubService: EventSub WebSocket handler. Subscribes to follows, subs, raids, channel points, cheers. Publishes typed events.", f_twitch)
    fid_twitch_oauth = insert_file("src/VyralBot.Integrations/Twitch/TwitchOAuthService.cs", "cs", "service", "TwitchOAuthService: OAuth token management with DPAPI encryption. Token refresh logic.", f_twitch)
    fid_twitch_irc = insert_file("src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs", "cs", "service", "LightweightTwitchIrc: Low-level IRC protocol implementation for minimal memory footprint.", f_twitch)

    # Twitch events
    fid_tw_chat_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchChatMessageEvent.cs", "cs", "model", "TwitchChatMessageEvent: Chat message from Twitch IRC — username, message, badges, subscriber/mod status, color.", f_twitch)
    fid_tw_follow_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchFollowEvent.cs", "cs", "model", "TwitchFollowEvent: User followed the channel.", f_twitch)
    fid_tw_sub_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchSubscribeEvent.cs", "cs", "model", "TwitchSubscribeEvent: User subscribed to the channel.", f_twitch)
    fid_tw_resub_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchResubEvent.cs", "cs", "model", "TwitchResubEvent: User resubscribed.", f_twitch)
    fid_tw_giftsub_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchGiftSubEvent.cs", "cs", "model", "TwitchGiftSubEvent: Gift subscription received.", f_twitch)
    fid_tw_raid_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchRaidEvent.cs", "cs", "model", "TwitchRaidEvent: Channel was raided — raider username, viewer count.", f_twitch)
    fid_tw_cheer_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchCheerEvent.cs", "cs", "model", "TwitchCheerEvent: Bits cheered — username, bits amount, message.", f_twitch)
    fid_tw_cp_evt = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchChannelPointsEvent.cs", "cs", "model", "TwitchChannelPointsEvent: Channel points redeemed via EventSub.", f_twitch)
    fid_tw_cp_redeem = insert_file("src/VyralBot.Integrations/Twitch/Events/TwitchChannelPointsRedeemEvent.cs", "cs", "model", "TwitchChannelPointsRedeemEvent: Custom channel points redemption.", f_twitch)

    # Kick
    fid_ikick = insert_file("src/VyralBot.Integrations/Kick/IKickService.cs", "cs", "service", "IKickService interface: ConnectAsync (with broadcasterUserId), DisconnectAsync, SendMessageAsync, IsConnected, Channel, BotUsername.", f_kick)
    fid_kick_svc = insert_file("src/VyralBot.Integrations/Kick/KickLibChatService.cs", "cs", "service", "KickLibChatService: Kick chat client using KickLib. Publishes KickChatMessageEvent to EventBus. Chat reconnection handling.", f_kick)
    fid_kick_oauth = insert_file("src/VyralBot.Integrations/Kick/KickLibOAuthService.cs", "cs", "service", "KickLibOAuthService: OAuth & token management for Kick platform. KickLib API integration.", f_kick)

    # Kick events
    fid_k_chat_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickChatMessageEvent.cs", "cs", "model", "KickChatMessageEvent: Chat message from Kick WebSocket — username, message, badges.", f_kick)
    fid_k_follow_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickFollowEvent.cs", "cs", "model", "KickFollowEvent: User followed the Kick channel.", f_kick)
    fid_k_sub_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickSubscribeEvent.cs", "cs", "model", "KickSubscribeEvent: User subscribed on Kick.", f_kick)
    fid_k_giftsub_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickGiftSubEvent.cs", "cs", "model", "KickGiftSubEvent: Gift subscription on Kick.", f_kick)
    fid_k_reward_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickRewardRedeemedEvent.cs", "cs", "model", "KickRewardRedeemedEvent: Kick reward redeemed by user.", f_kick)
    fid_k_kicks_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickKicksGiftedEvent.cs", "cs", "model", "KickKicksGiftedEvent: Kicks (platform currency) gifted.", f_kick)
    fid_k_host_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickHostEvent.cs", "cs", "model", "KickHostEvent: Channel hosted on Kick.", f_kick)
    fid_k_stream_evt = insert_file("src/VyralBot.Integrations/Kick/Events/KickStreamStatusEvent.cs", "cs", "model", "KickStreamStatusEvent: Stream started/ended on Kick.", f_kick)

    # YouTube
    fid_iyt = insert_file("src/VyralBot.Integrations/YouTube/IYouTubeService.cs", "cs", "service", "IYouTubeService interface: ConnectAsync, DisconnectAsync, SendMessageAsync, IsConnected.", f_youtube)
    fid_yt_svc = insert_file("src/VyralBot.Integrations/YouTube/YouTubeChatService.cs", "cs", "service", "YouTubeChatService: Polls YouTube Data API v3 for live chat messages. Publishes YouTubeChatMessageEvent.", f_youtube)
    fid_yt_chat_evt = insert_file("src/VyralBot.Integrations/YouTube/Events/YouTubeChatMessageEvent.cs", "cs", "model", "YouTubeChatMessageEvent: Chat message from YouTube live chat.", f_youtube)

    # --- VyralBot.Api ---
    fid_api_csproj = insert_file("src/VyralBot.Api/VyralBot.Api.csproj", "csproj", "build", "API layer project. Embedded Kestrel web server, webhook controllers, ngrok integration.", f_webserver)
    fid_api_program = insert_file("src/VyralBot.Api/Program.cs", "cs", "config", "API Program.cs: Kestrel server configuration, middleware pipeline, controller registration.", f_webserver)
    fid_webhooks_ctrl = insert_file("src/VyralBot.Api/Controllers/WebhooksController.cs", "cs", "controller", "WebhooksController: Receives webhooks from Twitch, Kick, YouTube. Validates signatures, publishes events to EventBus.", f_webserver)
    fid_kick_wh_middleware = insert_file("src/VyralBot.Api/Middleware/KickWebhookValidationMiddleware.cs", "cs", "service", "KickWebhookValidationMiddleware: Validates incoming Kick webhook signatures for security.", f_kick)
    fid_ws_svc = insert_file("src/VyralBot.Api/Services/WebServerService.cs", "cs", "service", "WebServerService: Manages embedded Kestrel HTTP server lifecycle (start/stop/restart).", f_webserver)
    fid_ngrok_svc = insert_file("src/VyralBot.Api/Services/NgrokService.cs", "cs", "service", "NgrokService: Manages ngrok tunnel for public URL exposure. Required by Kick webhooks.", f_webserver)
    fid_kick_wh_svc = insert_file("src/VyralBot.Api/Services/KickWebhookSubscriptionService.cs", "cs", "service", "KickWebhookSubscriptionService: Manages Kick webhook subscription lifecycle — register, refresh, unregister.", f_kick)
    fid_wh_coord = insert_file("src/VyralBot.Api/Services/WebhookCoordinatorService.cs", "cs", "service", "WebhookCoordinatorService: Orchestrates webhook registrations across platforms. Manages subscription lifecycle.", f_webserver)
    fid_audio_bc_api = insert_file("src/VyralBot.Api/Services/AudioWebSocketBroadcaster.cs", "cs", "service", "AudioWebSocketBroadcaster in Api: API-side audio distribution to browser sources via WebSocket.", f_audio)
    fid_audio_html = insert_file("src/VyralBot.Api/wwwroot/audio.html", "html", "web-asset", "OBS Browser Source for audio. Connects to WebSocket server, receives audio events, plays sounds.", f_audio)
    fid_api_appsettings = insert_file("src/VyralBot.Api/appsettings.json", "json", "config", "API appsettings.json: Default Kestrel configuration.", f_webserver)
    fid_api_appsettings_dev = insert_file("src/VyralBot.Api/appsettings.Development.json", "json", "config", "API development appsettings: Dev-specific overrides.", f_webserver)
    fid_api_launch = insert_file("src/VyralBot.Api/Properties/launchSettings.json", "json", "config", "API launch settings for local development.", f_webserver)

    # --- VyralBot.UI ---
    fid_ui_csproj = insert_file("src/VyralBot.UI/VyralBot.UI.csproj", "csproj", "build", "UI layer project. WPF + CommunityToolkit.Mvvm. Dependencies: Microsoft.Web.WebView2, System.Windows.Extensions.", f_dashboard)
    fid_app_xaml = insert_file("src/VyralBot.UI/App.xaml", "xaml", "config", "App.xaml: Application resources, theme references, merged resource dictionaries.", f_dashboard)
    fid_app_xaml_cs = insert_file("src/VyralBot.UI/App.xaml.cs", "cs", "config", "App.xaml.cs: DI container setup. Registers ALL services, ViewModels, Views. Database seeding. Startup/shutdown lifecycle.", f_dashboard)
    fid_assembly_info = insert_file("src/VyralBot.UI/AssemblyInfo.cs", "cs", "config", "Assembly metadata and WPF theme info.", f_dashboard)

    # UI Main Window
    fid_main_window = insert_file("src/VyralBot.UI/MainWindow.xaml", "xaml", "view", "MainWindow.xaml: Application shell with sidebar navigation, content area, title bar, status indicators.", f_dashboard)
    fid_main_window_cs = insert_file("src/VyralBot.UI/MainWindow.xaml.cs", "cs", "view", "MainWindow.xaml.cs: Code-behind for main window. Window chrome, drag, minimize/maximize/close.", f_dashboard)

    # UI Services
    fid_alert_svc = insert_file("src/VyralBot.UI/Services/AlertService.cs", "cs", "service", "AlertService: Manages alert overlay lifecycle and alert queue display.", f_alerts)
    fid_data_chatstore = insert_file("src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs", "cs", "service", "DataChatMessageStore: Database-backed chat message persistence. Production IChatMessageStore.", f_chat)
    fid_profile_aware_ds = insert_file("src/VyralBot.UI/Services/ProfileAwareDataService.cs", "cs", "service", "ProfileAwareDataService: Dynamically switches database connection per active profile.", f_profiles)
    fid_profile_settings = insert_file("src/VyralBot.UI/Services/ProfileSettingsLoader.cs", "cs", "service", "ProfileSettingsLoader: Loads all feature settings from DB on profile load. Initializes ChatEventSettings, web server, subscriptions.", f_profiles)
    fid_webview2_mgr = insert_file("src/VyralBot.UI/Services/WebView2EnvironmentManager.cs", "cs", "service", "WebView2EnvironmentManager: Manages WebView2 runtime for OAuth setup wizards.", f_platforms_hub)

    # UI ViewModels
    fid_vm_base = insert_file("src/VyralBot.UI/ViewModels/ViewModelBase.cs", "cs", "viewmodel", "ViewModelBase: Base class for all ViewModels. Inherits ObservableObject from CommunityToolkit.Mvvm.", f_dashboard)
    fid_vm_main = insert_file("src/VyralBot.UI/ViewModels/MainViewModel.cs", "cs", "viewmodel", "MainViewModel: Main window logic. ALL navigation commands, platform status, profile management, view lifecycle, sidebar state.", f_dashboard)
    fid_vm_dashboard = insert_file("src/VyralBot.UI/ViewModels/DashboardViewModel.cs", "cs", "viewmodel", "DashboardViewModel: Home dashboard with quick stats and platform connection overview.", f_dashboard)
    fid_vm_platforms = insert_file("src/VyralBot.UI/ViewModels/PlatformsViewModel.cs", "cs", "viewmodel", "PlatformsViewModel: Platform hub — connection management, status display, setup wizard entry points.", f_platforms_hub)
    fid_vm_chat = insert_file("src/VyralBot.UI/ViewModels/ChatViewModel.cs", "cs", "viewmodel", "ChatViewModel: Main chat display. Subscribes to EventBus for messages. Message filtering, platform selection. Singleton (persisted across nav).", f_chat)
    fid_vm_commands = insert_file("src/VyralBot.UI/ViewModels/CommandsViewModel.cs", "cs", "viewmodel", "CommandsViewModel: Command CRUD. Create/edit/delete custom commands. Permission and cooldown configuration.", f_commands)
    fid_vm_currency = insert_file("src/VyralBot.UI/ViewModels/CurrencyViewModel.cs", "cs", "viewmodel", "CurrencyViewModel: Currency balances, rewards management, leaderboard display, transaction history.", f_currency)
    fid_vm_giveaway = insert_file("src/VyralBot.UI/ViewModels/GiveawayViewModel.cs", "cs", "viewmodel", "GiveawayViewModel: Giveaway creation, entry management, winner drawing and display.", f_giveaway)
    fid_vm_poll = insert_file("src/VyralBot.UI/ViewModels/PollViewModel.cs", "cs", "viewmodel", "PollViewModel: Poll creation, vote tracking, result display.", f_poll)
    fid_vm_watchtime = insert_file("src/VyralBot.UI/ViewModels/WatchtimeViewModel.cs", "cs", "viewmodel", "WatchtimeViewModel: Watchtime leaderboard display and settings.", f_watchtime)
    fid_vm_variables = insert_file("src/VyralBot.UI/ViewModels/VariablesViewModel.cs", "cs", "viewmodel", "VariablesViewModel: Variable CRUD. Create/edit/delete custom variables. System variable display.", f_variables)
    fid_vm_varpicker = insert_file("src/VyralBot.UI/ViewModels/VariablePickerViewModel.cs", "cs", "viewmodel", "VariablePickerViewModel: Variable selection dialog for template editing.", f_variables)
    fid_vm_settings = insert_file("src/VyralBot.UI/ViewModels/SettingsViewModel.cs", "cs", "viewmodel", "SettingsViewModel: Global application settings UI.", f_settings)
    fid_vm_audio = insert_file("src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs", "cs", "viewmodel", "AudioSettingsViewModel: Audio output configuration.", f_audio)
    fid_vm_sounds = insert_file("src/VyralBot.UI/ViewModels/SoundsViewModel.cs", "cs", "viewmodel", "SoundsViewModel: Sound file management — add, remove, preview sounds.", f_audio)
    fid_vm_alerts = insert_file("src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs", "cs", "viewmodel", "AlertSettingsViewModel: Alert overlay configuration — styles, duration, position.", f_alerts)
    fid_vm_stats = insert_file("src/VyralBot.UI/ViewModels/StatisticsViewModel.cs", "cs", "viewmodel", "StatisticsViewModel: Statistics display with graphs and event counts.", f_statistics)
    fid_vm_evthist = insert_file("src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs", "cs", "viewmodel", "EventHistoryViewModel: Event log viewer — chronological history of all processed events.", f_event_history)
    fid_vm_evttest = insert_file("src/VyralBot.UI/ViewModels/EventTestingViewModel.cs", "cs", "viewmodel", "EventTestingViewModel: Debug tool for manually firing events.", f_event_testing)
    fid_vm_rules = insert_file("src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs", "cs", "viewmodel", "ConditionalRulesViewModel: Auto-reply rule editor. Create/edit conditional rules with priority.", f_auto_reply)
    fid_vm_webserver = insert_file("src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs", "cs", "viewmodel", "WebServerSettingsViewModel: Web server startup/shutdown, port config, ngrok settings.", f_webserver)
    fid_vm_profile_sel = insert_file("src/VyralBot.UI/ViewModels/ProfileSelectorViewModel.cs", "cs", "viewmodel", "ProfileSelectorViewModel: Profile selection and creation dialog logic.", f_profiles)

    # Platform-specific ViewModels
    fid_vm_tw_setup = insert_file("src/VyralBot.UI/ViewModels/TwitchWebSetupViewModel.cs", "cs", "viewmodel", "TwitchWebSetupViewModel: Twitch OAuth setup wizard.", f_twitch)
    fid_vm_tw_dash = insert_file("src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs", "cs", "viewmodel", "TwitchDashboardViewModel: Twitch connection status, chat controls, event monitoring.", f_twitch)
    fid_vm_tw_settings = insert_file("src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs", "cs", "viewmodel", "TwitchSettingsViewModel: Twitch-specific settings management.", f_twitch)
    fid_vm_tw_chat = insert_file("src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs", "cs", "viewmodel", "TwitchChatSettingsViewModel: Twitch chat event configuration — per-event auto-reply, sounds.", f_twitch)
    fid_vm_k_setup = insert_file("src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs", "cs", "viewmodel", "KickWebSetupViewModel: Kick OAuth setup wizard.", f_kick)
    fid_vm_k_dash = insert_file("src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs", "cs", "viewmodel", "KickDashboardViewModel: Kick connection status, chat controls, webhook status.", f_kick)
    fid_vm_k_settings = insert_file("src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs", "cs", "viewmodel", "KickSettingsViewModel: Kick-specific settings management.", f_kick)
    fid_vm_k_chat = insert_file("src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs", "cs", "viewmodel", "KickChatSettingsViewModel: Kick chat event configuration — per-event auto-reply, sounds.", f_kick)
    fid_vm_yt_setup = insert_file("src/VyralBot.UI/ViewModels/YouTubeWebSetupViewModel.cs", "cs", "viewmodel", "YouTubeWebSetupViewModel: YouTube OAuth setup wizard.", f_youtube)
    fid_vm_yt_chat = insert_file("src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs", "cs", "viewmodel", "YoutubeChatSettingsViewModel: YouTube chat integration settings.", f_youtube)
    fid_vm_tt_setup = insert_file("src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs", "cs", "viewmodel", "TikTokWebSetupViewModel: TikTok OAuth setup wizard.", f_tiktok)
    fid_vm_tt_chat = insert_file("src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs", "cs", "viewmodel", "TiktokChatSettingsViewModel: TikTok chat settings.", f_tiktok)
    fid_vm_dc_setup = insert_file("src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs", "cs", "viewmodel", "DiscordWebSetupViewModel: Discord OAuth setup wizard.", f_discord)
    fid_vm_dc_chat = insert_file("src/VyralBot.UI/ViewModels/DiscordChatSettingsViewModel.cs", "cs", "viewmodel", "DiscordChatSettingsViewModel: Discord chat settings.", f_discord)
    fid_vm_base_oauth = insert_file("src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs", "cs", "viewmodel", "BaseOAuthSetupViewModel: Base class for all OAuth setup wizard ViewModels.", f_platforms_hub)
    fid_vm_base_web = insert_file("src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs", "cs", "viewmodel", "BaseWebSetupViewModel: Base class for web-based setup ViewModels.", f_platforms_hub)
    fid_vm_demo = insert_file("src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs", "cs", "viewmodel", "DemoSetupViewModel: Demo/example setup wizard.", f_platforms_hub)
    fid_vm_gdemo = insert_file("src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs", "cs", "viewmodel", "GoogleDemoSetupViewModel: Google demo setup wizard.", f_platforms_hub)
    fid_vm_ngrok = insert_file("src/VyralBot.UI/ViewModels/NgrokSetupViewModel.cs", "cs", "viewmodel", "NgrokSetupViewModel: ngrok configuration (legacy).", f_webserver)
    fid_vm_ngrok_wiz = insert_file("src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs", "cs", "viewmodel", "NgrokSetupWizardViewModel: ngrok setup wizard with step-by-step guidance.", f_webserver)

    # UI Views (XAML + code-behind) — abbreviated, these all follow same pattern
    view_files = [
        ("src/VyralBot.UI/Views/AlertOverlayWindow.xaml", "xaml", "view", "Alert notification overlay window.", f_alerts),
        ("src/VyralBot.UI/Views/AlertOverlayWindow.xaml.cs", "cs", "view", "Alert overlay code-behind.", f_alerts),
        ("src/VyralBot.UI/Views/AlertSettingsView.xaml", "xaml", "view", "Alert settings configuration UI.", f_alerts),
        ("src/VyralBot.UI/Views/AlertSettingsView.xaml.cs", "cs", "view", "Alert settings code-behind.", f_alerts),
        ("src/VyralBot.UI/Views/AudioSettingsView.xaml", "xaml", "view", "Audio output settings UI.", f_audio),
        ("src/VyralBot.UI/Views/AudioSettingsView.xaml.cs", "cs", "view", "Audio settings code-behind.", f_audio),
        ("src/VyralBot.UI/Views/ChatView.xaml", "xaml", "view", "Main chat message display. Shows messages from all platforms with badges and colors.", f_chat),
        ("src/VyralBot.UI/Views/ChatView.xaml.cs", "cs", "view", "Chat view code-behind. Auto-scroll, platform filtering.", f_chat),
        ("src/VyralBot.UI/Views/CommandsView.xaml", "xaml", "view", "Command editor UI — list, create, edit, delete commands.", f_commands),
        ("src/VyralBot.UI/Views/CommandsView.xaml.cs", "cs", "view", "Commands view code-behind.", f_commands),
        ("src/VyralBot.UI/Views/ConditionalRulesView.xaml", "xaml", "view", "Conditional auto-reply rule editor UI.", f_auto_reply),
        ("src/VyralBot.UI/Views/ConditionalRulesView.xaml.cs", "cs", "view", "Conditional rules code-behind.", f_auto_reply),
        ("src/VyralBot.UI/Views/ConfirmDialog.xaml", "xaml", "view", "Generic confirmation dialog.", f_dashboard),
        ("src/VyralBot.UI/Views/ConfirmDialog.xaml.cs", "cs", "view", "Confirm dialog code-behind.", f_dashboard),
        ("src/VyralBot.UI/Views/CurrencyView.xaml", "xaml", "view", "Currency system UI — balances, rewards, leaderboard.", f_currency),
        ("src/VyralBot.UI/Views/CurrencyView.xaml.cs", "cs", "view", "Currency view code-behind.", f_currency),
        ("src/VyralBot.UI/Views/CustomDialog.xaml", "xaml", "view", "Generic custom dialog.", f_dashboard),
        ("src/VyralBot.UI/Views/CustomDialog.xaml.cs", "cs", "view", "Custom dialog code-behind.", f_dashboard),
        ("src/VyralBot.UI/Views/DemoSetupView.xaml", "xaml", "view", "Demo setup wizard UI.", f_platforms_hub),
        ("src/VyralBot.UI/Views/DemoSetupView.xaml.cs", "cs", "view", "Demo setup code-behind.", f_platforms_hub),
        ("src/VyralBot.UI/Views/DiscordChatSettingsView.xaml", "xaml", "view", "Discord chat settings UI.", f_discord),
        ("src/VyralBot.UI/Views/DiscordChatSettingsView.xaml.cs", "cs", "view", "Discord chat settings code-behind.", f_discord),
        ("src/VyralBot.UI/Views/DiscordWebSetupView.xaml", "xaml", "view", "Discord OAuth setup wizard UI.", f_discord),
        ("src/VyralBot.UI/Views/DiscordWebSetupView.xaml.cs", "cs", "view", "Discord setup code-behind.", f_discord),
        ("src/VyralBot.UI/Views/ErrorDialog.xaml", "xaml", "view", "Error message dialog.", f_dashboard),
        ("src/VyralBot.UI/Views/ErrorDialog.xaml.cs", "cs", "view", "Error dialog code-behind.", f_dashboard),
        ("src/VyralBot.UI/Views/EventHistoryView.xaml", "xaml", "view", "Event log viewer UI.", f_event_history),
        ("src/VyralBot.UI/Views/EventHistoryView.xaml.cs", "cs", "view", "Event history code-behind.", f_event_history),
        ("src/VyralBot.UI/Views/EventTestingView.xaml", "xaml", "view", "Manual event testing UI.", f_event_testing),
        ("src/VyralBot.UI/Views/EventTestingView.xaml.cs", "cs", "view", "Event testing code-behind.", f_event_testing),
        ("src/VyralBot.UI/Views/GenericWebSetupView.xaml", "xaml", "view", "Generic OAuth handler UI.", f_platforms_hub),
        ("src/VyralBot.UI/Views/GenericWebSetupView.xaml.cs", "cs", "view", "Generic setup code-behind.", f_platforms_hub),
        ("src/VyralBot.UI/Views/GiveawayView.xaml", "xaml", "view", "Giveaway creation and management UI.", f_giveaway),
        ("src/VyralBot.UI/Views/GiveawayView.xaml.cs", "cs", "view", "Giveaway view code-behind.", f_giveaway),
        ("src/VyralBot.UI/Views/GoogleDemoSetupView.xaml", "xaml", "view", "Google demo setup wizard UI.", f_platforms_hub),
        ("src/VyralBot.UI/Views/GoogleDemoSetupView.xaml.cs", "cs", "view", "Google demo setup code-behind.", f_platforms_hub),
        ("src/VyralBot.UI/Views/KickChatSettingsView.xaml", "xaml", "view", "Kick chat event settings UI.", f_kick),
        ("src/VyralBot.UI/Views/KickChatSettingsView.xaml.cs", "cs", "view", "Kick chat settings code-behind.", f_kick),
        ("src/VyralBot.UI/Views/KickDashboardView.xaml", "xaml", "view", "Kick connection dashboard — status, chat, webhooks.", f_kick),
        ("src/VyralBot.UI/Views/KickDashboardView.xaml.cs", "cs", "view", "Kick dashboard code-behind.", f_kick),
        ("src/VyralBot.UI/Views/KickSettingsView.xaml", "xaml", "view", "Kick platform-specific settings UI.", f_kick),
        ("src/VyralBot.UI/Views/KickSettingsView.xaml.cs", "cs", "view", "Kick settings code-behind.", f_kick),
        ("src/VyralBot.UI/Views/KickWebSetupView.xaml", "xaml", "view", "Kick OAuth setup wizard UI.", f_kick),
        ("src/VyralBot.UI/Views/KickWebSetupView.xaml.cs", "cs", "view", "Kick setup code-behind.", f_kick),
        ("src/VyralBot.UI/Views/NgrokSetupView.xaml", "xaml", "view", "ngrok configuration UI (legacy).", f_webserver),
        ("src/VyralBot.UI/Views/NgrokSetupView.xaml.cs", "cs", "view", "ngrok setup code-behind.", f_webserver),
        ("src/VyralBot.UI/Views/NgrokSetupWizardView.xaml", "xaml", "view", "ngrok step-by-step wizard UI.", f_webserver),
        ("src/VyralBot.UI/Views/NgrokSetupWizardView.xaml.cs", "cs", "view", "ngrok wizard code-behind.", f_webserver),
        ("src/VyralBot.UI/Views/PlatformsView.xaml", "xaml", "view", "Platform selection hub UI — shows all platforms with connect status.", f_platforms_hub),
        ("src/VyralBot.UI/Views/PlatformsView.xaml.cs", "cs", "view", "Platforms view code-behind.", f_platforms_hub),
        ("src/VyralBot.UI/Views/PollView.xaml", "xaml", "view", "Poll creation and voting UI.", f_poll),
        ("src/VyralBot.UI/Views/PollView.xaml.cs", "cs", "view", "Poll view code-behind.", f_poll),
        ("src/VyralBot.UI/Views/ProfileSelectorWindow.xaml", "xaml", "view", "Profile selection dialog.", f_profiles),
        ("src/VyralBot.UI/Views/ProfileSelectorWindow.xaml.cs", "cs", "view", "Profile selector code-behind.", f_profiles),
        ("src/VyralBot.UI/Views/SettingsView.xaml", "xaml", "view", "Global application settings UI.", f_settings),
        ("src/VyralBot.UI/Views/SettingsView.xaml.cs", "cs", "view", "Settings view code-behind.", f_settings),
        ("src/VyralBot.UI/Views/SoundsView.xaml", "xaml", "view", "Sound file management UI.", f_audio),
        ("src/VyralBot.UI/Views/SoundsView.xaml.cs", "cs", "view", "Sounds view code-behind.", f_audio),
        ("src/VyralBot.UI/Views/StatisticsView.xaml", "xaml", "view", "Statistics and analytics UI.", f_statistics),
        ("src/VyralBot.UI/Views/StatisticsView.xaml.cs", "cs", "view", "Statistics view code-behind.", f_statistics),
        ("src/VyralBot.UI/Views/TiktokChatSettingsView.xaml", "xaml", "view", "TikTok chat settings UI.", f_tiktok),
        ("src/VyralBot.UI/Views/TiktokChatSettingsView.xaml.cs", "cs", "view", "TikTok chat settings code-behind.", f_tiktok),
        ("src/VyralBot.UI/Views/TikTokWebSetupView.xaml", "xaml", "view", "TikTok OAuth setup wizard UI.", f_tiktok),
        ("src/VyralBot.UI/Views/TikTokWebSetupView.xaml.cs", "cs", "view", "TikTok setup code-behind.", f_tiktok),
        ("src/VyralBot.UI/Views/TwitchChatSettingsView.xaml", "xaml", "view", "Twitch chat event settings UI.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchChatSettingsView.xaml.cs", "cs", "view", "Twitch chat settings code-behind.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchDashboardView.xaml", "xaml", "view", "Twitch connection dashboard — status, chat controls, EventSub.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchDashboardView.xaml.cs", "cs", "view", "Twitch dashboard code-behind.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchSettingsView.xaml", "xaml", "view", "Twitch platform-specific settings UI.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchSettingsView.xaml.cs", "cs", "view", "Twitch settings code-behind.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchWebSetupView.xaml", "xaml", "view", "Twitch OAuth setup wizard UI with WebView2.", f_twitch),
        ("src/VyralBot.UI/Views/TwitchWebSetupView.xaml.cs", "cs", "view", "Twitch setup code-behind.", f_twitch),
        ("src/VyralBot.UI/Views/VariablesView.xaml", "xaml", "view", "Variable manager UI — create, edit, delete variables.", f_variables),
        ("src/VyralBot.UI/Views/VariablesView.xaml.cs", "cs", "view", "Variables view code-behind.", f_variables),
        ("src/VyralBot.UI/Views/WatchtimeView.xaml", "xaml", "view", "Watchtime tracking UI — leaderboard, settings.", f_watchtime),
        ("src/VyralBot.UI/Views/WatchtimeView.xaml.cs", "cs", "view", "Watchtime view code-behind.", f_watchtime),
        ("src/VyralBot.UI/Views/WebServerSettingsView.xaml", "xaml", "view", "Web server and ngrok settings UI.", f_webserver),
        ("src/VyralBot.UI/Views/WebServerSettingsView.xaml.cs", "cs", "view", "Web server settings code-behind.", f_webserver),
        ("src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml", "xaml", "view", "YouTube chat settings UI.", f_youtube),
        ("src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml.cs", "cs", "view", "YouTube chat settings code-behind.", f_youtube),
        ("src/VyralBot.UI/Views/YouTubeWebSetupView.xaml", "xaml", "view", "YouTube OAuth setup wizard UI.", f_youtube),
        ("src/VyralBot.UI/Views/YouTubeWebSetupView.xaml.cs", "cs", "view", "YouTube setup code-behind.", f_youtube),
    ]
    for path, ftype, layer, summary, feat_id in view_files:
        insert_file(path, ftype, layer, summary, feat_id)

    # UI Controls, Converters, Behaviors, Theme
    insert_file("src/VyralBot.UI/Controls/VariablePicker.xaml", "xaml", "view", "Variable picker reusable control for template editing.", f_variables)
    insert_file("src/VyralBot.UI/Controls/VariablePicker.xaml.cs", "cs", "view", "Variable picker control code-behind.", f_variables)
    insert_file("src/VyralBot.UI/Controls/LazyPopupContent.cs", "cs", "view", "LazyPopupContent: Deferred loading popup control for performance.", f_dashboard)
    insert_file("src/VyralBot.UI/Behaviors/TextBoxVariableBehavior.cs", "cs", "view", "TextBoxVariableBehavior: Attached behavior for variable insertion in text boxes.", f_variables)
    insert_file("src/VyralBot.UI/Resources/Themes/RedSlate.xaml", "xaml", "config", "RedSlate theme: Custom color scheme and control styles.", f_settings)

    # Converters
    converter_files = [
        ("src/VyralBot.UI/Converters/BadgesToImagesConverter.cs", "Converts badge JSON to badge images for chat display."),
        ("src/VyralBot.UI/Converters/BoolToExpandIconConverter.cs", "Converts bool to expand/collapse icon."),
        ("src/VyralBot.UI/Converters/BoolToStatusConverter.cs", "Converts bool to status text (Connected/Disconnected)."),
        ("src/VyralBot.UI/Converters/BoolToStringConverter.cs", "Generic bool to string converter."),
        ("src/VyralBot.UI/Converters/CommandEditTitleConverter.cs", "Converts command edit state to dialog title."),
        ("src/VyralBot.UI/Converters/CurrencyConverters.cs", "Currency-related display converters."),
        ("src/VyralBot.UI/Converters/EqualsToVisibilityConverter.cs", "Value equality to Visibility converter."),
        ("src/VyralBot.UI/Converters/EventHistoryConverters.cs", "Event history display converters."),
        ("src/VyralBot.UI/Converters/InvertBooleanConverter.cs", "Inverts boolean value."),
        ("src/VyralBot.UI/Converters/InvertBooleanToVisibilityConverter.cs", "Inverted bool to Visibility."),
        ("src/VyralBot.UI/Converters/InvertedBooleanToVisibilityConverter.cs", "Alternate inverted bool to Visibility."),
        ("src/VyralBot.UI/Converters/NullOrEmptyToVisibilityConverter.cs", "Null/empty string to Visibility."),
        ("src/VyralBot.UI/Converters/PlatformToBrushConverter.cs", "Platform name to color brush for chat messages."),
        ("src/VyralBot.UI/Converters/PlatformToImageConverter.cs", "Platform name to platform icon image."),
        ("src/VyralBot.UI/Converters/ProfileConverters.cs", "Profile-related display converters."),
        ("src/VyralBot.UI/Converters/SetupWizardConverters.cs", "OAuth setup wizard step converters."),
        ("src/VyralBot.UI/Converters/StatisticsConverters.cs", "Statistics display converters."),
        ("src/VyralBot.UI/Converters/StringNullOrEmptyConverter.cs", "String null/empty check converter."),
        ("src/VyralBot.UI/Converters/WatchtimeFormatConverter.cs", "Formats watchtime minutes to human-readable string."),
    ]
    for path, summary in converter_files:
        insert_file(path, "cs", "view", summary, f_dashboard)

    # Other project files
    fid_plugins_csproj = insert_file("src/VyralBot.Plugins/VyralBot.Plugins.csproj", "csproj", "build", "Plugin contracts project. Defines IPlugin, IHostServices interfaces for plugin isolation.", None)
    fid_scripting_csproj = insert_file("src/VyralBot.Scripting/VyralBot.Scripting.csproj", "csproj", "build", "Scripting engine project. C# (Roslyn), JavaScript (Jint), Python (planned).", None)
    fid_sln = insert_file("src/VyralBot.sln", "sln", "build", "Solution file referencing all projects: UI, Core, Data, Integrations, Api, Plugins, Scripting.", None)

    # Root documentation files
    fid_dir_build = insert_file("Directory.Build.props", "props", "build", "Central build configuration: version, authors, company, nullable, implicit usings, analyzer enforcement.", None)

    file_count = len(view_files) + len(converter_files) + 120  # approx
    print(f"  Inserted ~{file_count} files")

    # ============================================================
    # 4. DB TABLES (the app's SQLite schema)
    # ============================================================
    print("[4/9] Inserting database schema...")

    # Platforms
    tid_platforms = insert_db_table("Platforms", f_platforms_hub,
        "CREATE TABLE IF NOT EXISTS Platforms (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, AccessToken TEXT, RefreshToken TEXT, Username TEXT, UserId TEXT, ChannelId TEXT, ChatroomId TEXT, IsConnected INTEGER DEFAULT 0, TwitchChatEnabled INTEGER DEFAULT 0, TwitchChatAutoStart INTEGER DEFAULT 0, KickChatEnabled INTEGER DEFAULT 0, KickChatAutoStart INTEGER DEFAULT 0, YouTubeChatEnabled INTEGER DEFAULT 0, YouTubeChatAutoStart INTEGER DEFAULT 0, TikTokChatEnabled INTEGER DEFAULT 0, TikTokChatAutoStart INTEGER DEFAULT 0, DiscordChatEnabled INTEGER DEFAULT 0, DiscordChatAutoStart INTEGER DEFAULT 0)",
        "Stores connected streaming platform accounts with credentials and per-platform chat settings.")
    for col in [
        ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
        ("Name", "TEXT", False, None, False, False, None, "Platform name: Twitch, Kick, YouTube, TikTok, Discord"),
        ("AccessToken", "TEXT", True, None, False, False, None, "OAuth access token (encrypted for Twitch)"),
        ("RefreshToken", "TEXT", True, None, False, False, None, "OAuth refresh token"),
        ("Username", "TEXT", True, None, False, False, None, "Bot username on this platform"),
        ("UserId", "TEXT", True, None, False, False, None, "Platform-specific user ID"),
        ("ChannelId", "TEXT", True, None, False, False, None, "Target channel ID"),
        ("ChatroomId", "TEXT", True, None, False, False, None, "Kick chatroom ID (Kick-specific)"),
        ("IsConnected", "INTEGER", False, "0", False, False, None, "Connection status flag"),
        ("TwitchChatEnabled", "INTEGER", False, "0", False, False, None, "Enable Twitch chat"),
        ("TwitchChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-connect Twitch on startup"),
        ("KickChatEnabled", "INTEGER", False, "0", False, False, None, "Enable Kick chat"),
        ("KickChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-connect Kick on startup"),
    ]:
        insert_db_column(tid_platforms, *col)

    # Commands
    tid_commands = insert_db_table("Commands", f_commands,
        "CREATE TABLE IF NOT EXISTS Commands (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Response TEXT, Permission TEXT DEFAULT 'Everyone', GlobalCooldown INTEGER DEFAULT 0, UserCooldown INTEGER DEFAULT 0, IsEnabled INTEGER DEFAULT 1, IsBuiltIn INTEGER DEFAULT 0, EnabledOnKick INTEGER DEFAULT 1, EnabledOnTwitch INTEGER DEFAULT 1, UsageCount INTEGER DEFAULT 0, LastUsedAt TEXT)",
        "Custom chat commands with cooldowns, permissions, and per-platform enable flags.")
    for col in [
        ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
        ("Name", "TEXT", False, None, False, False, None, "Command name (without ! prefix)"),
        ("Response", "TEXT", True, None, False, False, None, "Response template with {variable} support"),
        ("Permission", "TEXT", False, "'Everyone'", False, False, None, "Everyone, Subscriber, Moderator, Broadcaster"),
        ("GlobalCooldown", "INTEGER", False, "0", False, False, None, "Global cooldown in seconds"),
        ("UserCooldown", "INTEGER", False, "0", False, False, None, "Per-user cooldown in seconds"),
        ("IsEnabled", "INTEGER", False, "1", False, False, None, "Command enable flag"),
        ("IsBuiltIn", "INTEGER", False, "0", False, False, None, "Built-in command (cannot be deleted)"),
        ("EnabledOnKick", "INTEGER", False, "1", False, False, None, "Enable on Kick platform"),
        ("EnabledOnTwitch", "INTEGER", False, "1", False, False, None, "Enable on Twitch platform"),
        ("UsageCount", "INTEGER", False, "0", False, False, None, "Total usage count"),
        ("LastUsedAt", "TEXT", True, None, False, False, None, "ISO 8601 last used timestamp"),
    ]:
        insert_db_column(tid_commands, *col)

    # Triggers
    tid_triggers = insert_db_table("Triggers", f_triggers,
        "CREATE TABLE IF NOT EXISTS Triggers (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, EventType TEXT NOT NULL, Platform TEXT, Condition TEXT, CooldownSeconds INTEGER DEFAULT 0, LastFiredAt TEXT, FireCount INTEGER DEFAULT 0, Permission TEXT DEFAULT 'Everyone', ActionSequence TEXT, IsEnabled INTEGER DEFAULT 1)",
        "Event-based automation triggers. Match events by type/platform/condition, then fire action sequences.")
    for col in [
        ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
        ("Name", "TEXT", False, None, False, False, None, "Trigger display name"),
        ("EventType", "TEXT", False, None, False, False, None, "ChatMessage, Follow, Subscribe, GiftSub, Raid, ChannelPoints, Cheer, Reward, Custom"),
        ("Platform", "TEXT", True, None, False, False, None, "Twitch, Kick, YouTube, Discord, OBS, Manual"),
        ("Condition", "TEXT", True, None, False, False, None, "JSON condition expression"),
        ("CooldownSeconds", "INTEGER", False, "0", False, False, None, "Cooldown between fires"),
        ("ActionSequence", "TEXT", True, None, False, False, None, "JSON array of BotAction IDs to execute"),
        ("IsEnabled", "INTEGER", False, "1", False, False, None, "Enable flag"),
    ]:
        insert_db_column(tid_triggers, *col)

    # Actions
    tid_actions = insert_db_table("Actions", f_actions,
        "CREATE TABLE IF NOT EXISTS Actions (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, ActionType TEXT NOT NULL, Configuration TEXT, TimeoutSeconds INTEGER DEFAULT 30, RetryOnFailure INTEGER DEFAULT 0, MaxRetries INTEGER DEFAULT 0)",
        "Bot actions that can be executed by triggers. Each has a type and JSON configuration.")
    for col in [
        ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
        ("Name", "TEXT", False, None, False, False, None, "Action display name"),
        ("ActionType", "TEXT", False, None, False, False, None, "SendMessage, Delay, HttpRequest, RunScript, SetVariable, AwardCurrency, etc."),
        ("Configuration", "TEXT", True, None, False, False, None, "JSON configuration for this action type"),
        ("TimeoutSeconds", "INTEGER", False, "30", False, False, None, "Execution timeout"),
        ("RetryOnFailure", "INTEGER", False, "0", False, False, None, "Retry on error"),
        ("MaxRetries", "INTEGER", False, "0", False, False, None, "Max retry count"),
    ]:
        insert_db_column(tid_actions, *col)

    # ChatMessages
    tid_chatmsg = insert_db_table("ChatMessages", f_chat,
        "CREATE TABLE IF NOT EXISTS ChatMessages (Id TEXT PRIMARY KEY, Platform TEXT NOT NULL, Channel TEXT, MessageId TEXT, UserId TEXT, Username TEXT NOT NULL, DisplayName TEXT, Content TEXT NOT NULL, Timestamp TEXT NOT NULL, Color TEXT, IsModerator INTEGER DEFAULT 0, IsSubscriber INTEGER DEFAULT 0, IsOwner INTEGER DEFAULT 0, Badges TEXT, CreatedAt TEXT)",
        "Persisted chat messages from all platforms with user metadata and badges.")

    # ChatEventSettings
    tid_chatevtsettings = insert_db_table("ChatEventSettings", f_auto_reply,
        "CREATE TABLE IF NOT EXISTS ChatEventSettings (Platform TEXT NOT NULL, EventType TEXT NOT NULL, ShowInChat INTEGER DEFAULT 1, AutoReplyEnabled INTEGER DEFAULT 0, AutoReplyMessage TEXT, SoundEnabled INTEGER DEFAULT 0, SoundFile TEXT, SoundVolume REAL DEFAULT 1.0, PRIMARY KEY (Platform, EventType))",
        "Per-platform, per-event configuration for chat visibility, auto-reply messages, and sound effects.")

    # AutoReplyRules
    tid_autorules = insert_db_table("AutoReplyRules", f_auto_reply,
        "CREATE TABLE IF NOT EXISTS AutoReplyRules (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Platform TEXT, EventType TEXT, IsEnabled INTEGER DEFAULT 1, Priority INTEGER DEFAULT 0, ConditionJson TEXT, ReplyTemplate TEXT, TriggerCount INTEGER DEFAULT 0, LastTriggeredAt TEXT)",
        "Conditional auto-reply rules with priority ordering and template-based responses.")

    # Currency tables
    tid_currency_settings = insert_db_table("CurrencySettings", f_currency,
        "CREATE TABLE IF NOT EXISTS CurrencySettings (Id INTEGER PRIMARY KEY, CurrencyName TEXT DEFAULT 'Coins', Symbol TEXT DEFAULT '🪙', WatchTimeEnabled INTEGER DEFAULT 1, WatchTimeInterval INTEGER DEFAULT 5, WatchTimeAmount INTEGER DEFAULT 10, ChatActivityEnabled INTEGER DEFAULT 1, ChatActivityCooldown INTEGER DEFAULT 60, ChatActivityAmount INTEGER DEFAULT 5, FollowBonusEnabled INTEGER DEFAULT 1, FollowBonusAmount INTEGER DEFAULT 50, SubBonusEnabled INTEGER DEFAULT 1, SubBonusAmount INTEGER DEFAULT 200, GiftSubBonusEnabled INTEGER DEFAULT 1, GiftSubBonusAmount INTEGER DEFAULT 100, RaidBonusEnabled INTEGER DEFAULT 1, RaidBonusAmount INTEGER DEFAULT 50)",
        "Global currency system configuration — earning rates, bonus amounts, feature toggles.")

    tid_user_currency = insert_db_table("UserCurrency", f_currency,
        "CREATE TABLE IF NOT EXISTS UserCurrency (Username TEXT NOT NULL, Platform TEXT NOT NULL, Balance INTEGER DEFAULT 0, LastUpdated TEXT, PRIMARY KEY (Username, Platform))",
        "Per-user currency balances across platforms.")

    tid_currency_tx = insert_db_table("CurrencyTransactions", f_currency,
        "CREATE TABLE IF NOT EXISTS CurrencyTransactions (Id TEXT PRIMARY KEY, Username TEXT NOT NULL, Platform TEXT NOT NULL, Amount INTEGER NOT NULL, TransactionType TEXT NOT NULL, Reason TEXT, Timestamp TEXT NOT NULL)",
        "Currency transaction log — earn, spend, admin adjustments with reasons.")

    tid_currency_rewards = insert_db_table("CurrencyRewards", f_currency,
        "CREATE TABLE IF NOT EXISTS CurrencyRewards (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Cost INTEGER NOT NULL, Description TEXT, Type TEXT)",
        "Redeemable currency rewards with cost and type.")

    tid_currency_redemptions = insert_db_table("CurrencyRedemptions", f_currency,
        "CREATE TABLE IF NOT EXISTS CurrencyRedemptions (Id TEXT PRIMARY KEY, Username TEXT NOT NULL, RewardId TEXT NOT NULL, Status TEXT DEFAULT 'Pending', CreatedAt TEXT NOT NULL)",
        "Reward redemption requests with status tracking (Pending/Completed/Rejected).")

    # Giveaway tables
    tid_giveaways = insert_db_table("Giveaways", f_giveaway,
        "CREATE TABLE IF NOT EXISTS Giveaways (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, EntryKeyword TEXT, Status TEXT DEFAULT 'Pending', StartedAt TEXT, EndedAt TEXT, ScheduledEndTime TEXT, WinnerCount INTEGER DEFAULT 1, SubscriberBonusEnabled INTEGER DEFAULT 0, MinWatchtimeMinutes INTEGER DEFAULT 0, AllowedPlatforms TEXT, FollowersOnly INTEGER DEFAULT 0, SubscribersOnly INTEGER DEFAULT 0, Winners TEXT, TotalEntries INTEGER DEFAULT 0, UniqueParticipants INTEGER DEFAULT 0)",
        "Giveaway events with entry requirements, scheduling, and winner tracking.")

    tid_giveaway_entries = insert_db_table("GiveawayEntries", f_giveaway,
        "CREATE TABLE IF NOT EXISTS GiveawayEntries (Id TEXT PRIMARY KEY, GiveawayId TEXT NOT NULL, Username TEXT NOT NULL, Platform TEXT NOT NULL, EnteredAt TEXT NOT NULL)",
        "Individual giveaway entries per user per platform.")

    # Poll tables
    tid_polls = insert_db_table("Polls", f_poll,
        "CREATE TABLE IF NOT EXISTS Polls (Id TEXT PRIMARY KEY, Question TEXT NOT NULL, Options TEXT NOT NULL, Status TEXT DEFAULT 'Pending', CreatedAt TEXT, StartedAt TEXT, EndedAt TEXT, ScheduledEndTime TEXT, CreatorUsername TEXT, TotalVotes INTEGER DEFAULT 0, AllowMultipleVotes INTEGER DEFAULT 0, AllowedPlatforms TEXT, FollowersOnly INTEGER DEFAULT 0, SubscribersOnly INTEGER DEFAULT 0, MinWatchtimeMinutes INTEGER DEFAULT 0, VoteResults TEXT)",
        "Poll events with multiple options, voting rules, and result tracking.")

    tid_pollvotes = insert_db_table("PollVotes", f_poll,
        "CREATE TABLE IF NOT EXISTS PollVotes (Id TEXT PRIMARY KEY, PollId TEXT NOT NULL, Username TEXT NOT NULL, Platform TEXT NOT NULL, SelectedOption TEXT NOT NULL, VotedAt TEXT NOT NULL)",
        "Individual poll votes per user.")

    # Watchtime
    tid_watchtime = insert_db_table("UserWatchtime", f_watchtime,
        "CREATE TABLE IF NOT EXISTS UserWatchtime (Username TEXT NOT NULL, Platform TEXT NOT NULL, TotalMinutes INTEGER DEFAULT 0, LastSeenAt TEXT, FirstSeenAt TEXT, StreamSessionMinutes INTEGER DEFAULT 0, LastStreamId TEXT, IsActive INTEGER DEFAULT 0, PRIMARY KEY (Username, Platform))",
        "Per-user watchtime tracking across platforms with session and total minutes.")

    # Variables
    tid_variables = insert_db_table("Variables", f_variables,
        "CREATE TABLE IF NOT EXISTS Variables (Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, DisplayName TEXT, Category TEXT DEFAULT 'Custom', Value TEXT, MaxLength INTEGER, IsEditable INTEGER DEFAULT 1, IsSystem INTEGER DEFAULT 0, IsAutoUpdating INTEGER DEFAULT 0, ContainsNestedVariables INTEGER DEFAULT 0, Platforms TEXT, Description TEXT)",
        "User-defined and system variables for template replacement in chat messages and auto-replies.")

    # Config
    tid_config = insert_db_table("Configs", f_settings,
        "CREATE TABLE IF NOT EXISTS Configs (Key TEXT PRIMARY KEY, Value TEXT, Category TEXT)",
        "Generic key-value configuration store with optional category grouping.")

    # EventLogs
    tid_eventlogs = insert_db_table("EventLogs", f_event_history,
        "CREATE TABLE IF NOT EXISTS EventLogs (Id TEXT PRIMARY KEY, Platform TEXT, EventType TEXT, Message TEXT, Severity TEXT, Timestamp TEXT)",
        "Event log entries for monitoring and debugging.")

    # EventStatistics
    tid_eventstats = insert_db_table("EventStatistics", f_statistics,
        "CREATE TABLE IF NOT EXISTS EventStatistics (Id TEXT PRIMARY KEY, Platform TEXT, EventType TEXT, Username TEXT, EventData TEXT, Timestamp TEXT, WasAutoReplied INTEGER DEFAULT 0)",
        "Event statistics for analytics — tracks every event with optional auto-reply flag.")

    # WebServer tables
    tid_ws_settings = insert_db_table("WebServerSettings", f_webserver,
        "CREATE TABLE IF NOT EXISTS WebServerSettings (Id INTEGER PRIMARY KEY, Port INTEGER DEFAULT 7777, IsEnabled INTEGER DEFAULT 0, NgrokEnabled INTEGER DEFAULT 0, NgrokAuthToken TEXT, NgrokRegion TEXT DEFAULT 'us', PublicUrl TEXT)",
        "Embedded web server configuration — port, ngrok tunnel settings.")

    tid_wh_subs = insert_db_table("WebhookSubscriptions", f_webserver,
        "CREATE TABLE IF NOT EXISTS WebhookSubscriptions (Id TEXT PRIMARY KEY, Platform TEXT NOT NULL, EventType TEXT, SubscriptionId TEXT, Status TEXT, CallbackUrl TEXT, CreatedAt TEXT, ExpiresAt TEXT)",
        "Platform webhook registrations — tracks active subscriptions for Twitch, Kick, etc.")

    # CommandCooldowns
    tid_cooldowns = insert_db_table("CommandCooldowns", f_commands,
        "CREATE TABLE IF NOT EXISTS CommandCooldowns (CommandId TEXT NOT NULL, Username TEXT, Platform TEXT, ExpiresAt TEXT NOT NULL)",
        "Active command cooldown entries. Checked before command execution.")

    print(f"  Inserted {22} tables with columns")

    # ============================================================
    # 5. EVENTS
    # ============================================================
    print("[5/9] Inserting events and event flows...")

    # Twitch events
    eid_tw_chat = insert_event("TwitchChatMessageEvent", "Twitch", f_twitch, fid_tw_chat_evt,
        ["Username", "DisplayName", "Message", "Color", "Badges", "IsModerator", "IsSubscriber", "IsOwner", "Channel", "MessageId"],
        "Chat message received from Twitch IRC. Published by TwitchService on incoming IRC messages.")
    eid_tw_follow = insert_event("TwitchFollowEvent", "Twitch", f_twitch, fid_tw_follow_evt,
        ["Username", "UserId", "FollowedAt"],
        "User followed the Twitch channel. Published by TwitchEventSubService via EventSub WebSocket.")
    eid_tw_sub = insert_event("TwitchSubscribeEvent", "Twitch", f_twitch, fid_tw_sub_evt,
        ["Username", "Tier", "IsGift"],
        "User subscribed to the Twitch channel. Published by TwitchEventSubService.")
    eid_tw_resub = insert_event("TwitchResubEvent", "Twitch", f_twitch, fid_tw_resub_evt,
        ["Username", "Tier", "Months", "Message"],
        "User resubscribed. Published by TwitchEventSubService.")
    eid_tw_giftsub = insert_event("TwitchGiftSubEvent", "Twitch", f_twitch, fid_tw_giftsub_evt,
        ["GifterUsername", "RecipientUsername", "Tier", "TotalGifted"],
        "Gift subscription received. Published by TwitchEventSubService.")
    eid_tw_raid = insert_event("TwitchRaidEvent", "Twitch", f_twitch, fid_tw_raid_evt,
        ["RaiderUsername", "ViewerCount"],
        "Channel was raided. Published by TwitchEventSubService.")
    eid_tw_cheer = insert_event("TwitchCheerEvent", "Twitch", f_twitch, fid_tw_cheer_evt,
        ["Username", "Bits", "Message"],
        "Bits cheered in channel. Published by TwitchEventSubService.")
    eid_tw_cp = insert_event("TwitchChannelPointsEvent", "Twitch", f_twitch, fid_tw_cp_evt,
        ["Username", "RewardTitle", "RewardId", "UserInput"],
        "Channel points redeemed. Published by TwitchEventSubService.")
    eid_tw_cp_redeem = insert_event("TwitchChannelPointsRedeemEvent", "Twitch", f_twitch, fid_tw_cp_redeem,
        ["Username", "RewardTitle", "RewardId", "UserInput", "Status"],
        "Custom channel points redemption. Published by TwitchEventSubService.")

    # Kick events
    eid_k_chat = insert_event("KickChatMessageEvent", "Kick", f_kick, fid_k_chat_evt,
        ["Username", "Message", "Badges", "ChatroomId", "SenderId"],
        "Chat message received from Kick WebSocket. Published by KickLibChatService.")
    eid_k_follow = insert_event("KickFollowEvent", "Kick", f_kick, fid_k_follow_evt,
        ["Username", "UserId", "FollowedAt"],
        "User followed the Kick channel. Published via webhook.")
    eid_k_sub = insert_event("KickSubscribeEvent", "Kick", f_kick, fid_k_sub_evt,
        ["Username", "Months"],
        "User subscribed on Kick. Published via webhook.")
    eid_k_giftsub = insert_event("KickGiftSubEvent", "Kick", f_kick, fid_k_giftsub_evt,
        ["GifterUsername", "RecipientUsername", "GiftCount"],
        "Gift subscription on Kick. Published via webhook.")
    eid_k_reward = insert_event("KickRewardRedeemedEvent", "Kick", f_kick, fid_k_reward_evt,
        ["Username", "RewardName", "RewardId"],
        "Kick reward redeemed by viewer.")
    eid_k_kicks = insert_event("KickKicksGiftedEvent", "Kick", f_kick, fid_k_kicks_evt,
        ["GifterUsername", "Amount"],
        "Kicks (platform currency) gifted to the channel.")
    eid_k_host = insert_event("KickHostEvent", "Kick", f_kick, fid_k_host_evt,
        ["HostUsername", "ViewerCount"],
        "Channel hosted on Kick.")
    eid_k_stream = insert_event("KickStreamStatusEvent", "Kick", f_kick, fid_k_stream_evt,
        ["IsLive", "Title", "Category"],
        "Stream started or ended on Kick.")

    # YouTube events
    eid_yt_chat = insert_event("YouTubeChatMessageEvent", "YouTube", f_youtube, fid_yt_chat_evt,
        ["Username", "Message", "IsMember", "IsModerator"],
        "Chat message from YouTube Live Chat. Published by YouTubeChatService via API polling.")

    # Core events
    eid_profile_sw = insert_event("ProfileSwitchedEvent", "Core", f_profiles, fid_profile_event,
        ["OldProfileName", "NewProfileName"],
        "Published when user switches between profiles. Triggers reloading of all settings and service reinitialization.")

    # Event flows — who publishes and who subscribes
    insert_event_flow(eid_tw_chat, None, None, fid_twitch_svc, fid_vm_chat, "TwitchService publishes → ChatViewModel receives and displays message")
    insert_event_flow(eid_tw_chat, None, None, fid_twitch_svc, fid_chatevtsettings, "TwitchService publishes → ChatEventSettingsService processes auto-reply/sound")
    insert_event_flow(eid_tw_chat, None, None, fid_twitch_svc, fid_cmd, "TwitchService publishes → CommandService checks if message is a command")
    insert_event_flow(eid_tw_chat, None, None, fid_twitch_svc, fid_trigger_engine, "TwitchService publishes → TriggerEngine evaluates ChatMessage triggers")
    insert_event_flow(eid_tw_chat, None, None, fid_twitch_svc, fid_stats_svc, "TwitchService publishes → StatisticsService records the event")

    insert_event_flow(eid_tw_follow, None, None, fid_twitch_eventsub, fid_trigger_engine, "TwitchEventSubService publishes → TriggerEngine evaluates Follow triggers")
    insert_event_flow(eid_tw_follow, None, None, fid_twitch_eventsub, fid_auto_reply, "TwitchEventSubService publishes → AutoReplyService generates follow reply")
    insert_event_flow(eid_tw_follow, None, None, fid_twitch_eventsub, fid_currency, "TwitchEventSubService publishes → CurrencyService awards follow bonus")

    insert_event_flow(eid_tw_sub, None, None, fid_twitch_eventsub, fid_trigger_engine, "TwitchEventSubService publishes → TriggerEngine evaluates Subscribe triggers")
    insert_event_flow(eid_tw_sub, None, None, fid_twitch_eventsub, fid_currency, "TwitchEventSubService publishes → CurrencyService awards sub bonus")
    insert_event_flow(eid_tw_raid, None, None, fid_twitch_eventsub, fid_trigger_engine, "TwitchEventSubService publishes → TriggerEngine evaluates Raid triggers")

    insert_event_flow(eid_k_chat, None, None, fid_kick_svc, fid_vm_chat, "KickLibChatService publishes → ChatViewModel receives and displays")
    insert_event_flow(eid_k_chat, None, None, fid_kick_svc, fid_cmd, "KickLibChatService publishes → CommandService checks for commands")
    insert_event_flow(eid_k_chat, None, None, fid_kick_svc, fid_trigger_engine, "KickLibChatService publishes → TriggerEngine evaluates triggers")

    insert_event_flow(eid_k_follow, None, None, fid_webhooks_ctrl, fid_trigger_engine, "WebhooksController receives Kick webhook → publishes → TriggerEngine")
    insert_event_flow(eid_k_sub, None, None, fid_webhooks_ctrl, fid_trigger_engine, "WebhooksController receives Kick sub webhook → publishes → TriggerEngine")

    insert_event_flow(eid_yt_chat, None, None, fid_yt_svc, fid_vm_chat, "YouTubeChatService polls API → publishes → ChatViewModel displays")
    insert_event_flow(eid_profile_sw, None, None, fid_profile_svc, fid_app_xaml_cs, "ProfileService publishes → App.xaml.cs reinitializes all services")

    print(f"  Inserted {19} events and {18} event flows")

    # ============================================================
    # 6. DI REGISTRATIONS
    # ============================================================
    print("[6/9] Inserting DI registrations...")

    # Core singletons
    insert_di_registration("IEventBus", "EventBus", "singleton", f_eventbus, fid_app_xaml_cs, None, "CRITICAL: Shared between WPF UI and Kestrel API. Must be singleton.")
    insert_di_registration("IChatMessageStore", "DataChatMessageStore", "singleton", f_chat, fid_app_xaml_cs, None, "Production implementation. InMemoryChatMessageStore for dev/test.")
    insert_di_registration("IChatEventSettingsService", "ChatEventSettingsService", "singleton", f_auto_reply, fid_app_xaml_cs)
    insert_di_registration("RuleEvaluator", "RuleEvaluator", "singleton", f_rules, fid_app_xaml_cs)
    insert_di_registration("IAutoReplyService", "AutoReplyService", "singleton", f_auto_reply, fid_app_xaml_cs)
    insert_di_registration("IActionDispatcher", "ActionDispatcher", "singleton", f_actions, fid_app_xaml_cs)
    insert_di_registration("ITriggerEngine", "TriggerEngine", "singleton", f_triggers, fid_app_xaml_cs)
    insert_di_registration("IProcessManager", "ProcessManager", "singleton", f_process_mgmt, fid_app_xaml_cs)
    insert_di_registration("IProfileService", "ProfileService", "singleton", f_profiles, fid_app_xaml_cs)

    # Feature singletons
    insert_di_registration("ICurrencyService", "CurrencyService", "singleton", f_currency, fid_app_xaml_cs)
    insert_di_registration("ICurrencyRewardsService", "CurrencyRewardsService", "singleton", f_currency, fid_app_xaml_cs)
    insert_di_registration("ICurrencyCommandService", "CurrencyCommandService", "singleton", f_currency, fid_app_xaml_cs)
    insert_di_registration("ICommandService", "CommandService", "singleton", f_commands, fid_app_xaml_cs)
    insert_di_registration("IVariableReplacementService", "VariableReplacementService", "singleton", f_variables, fid_app_xaml_cs)
    insert_di_registration("ISoundService", "SoundService", "singleton", f_audio, fid_app_xaml_cs)
    insert_di_registration("IStatisticsService", "StatisticsService", "singleton", f_statistics, fid_app_xaml_cs)
    insert_di_registration("IAlertService", "AlertService", "singleton", f_alerts, fid_app_xaml_cs)

    # Platform singletons
    insert_di_registration("ITwitchService", "TwitchService", "singleton", f_twitch, fid_app_xaml_cs, None, "IRC chat client")
    insert_di_registration("ITwitchEventSubService", "TwitchEventSubService", "singleton", f_twitch, fid_app_xaml_cs, None, "EventSub WebSocket for events")
    insert_di_registration("TwitchOAuthService", "TwitchOAuthService", "singleton", f_twitch, fid_app_xaml_cs)
    insert_di_registration("IKickService", "KickLibChatService", "singleton", f_kick, fid_app_xaml_cs, None, "Using KickLib library")
    insert_di_registration("KickLibOAuthService", "KickLibOAuthService", "singleton", f_kick, fid_app_xaml_cs)
    insert_di_registration("IYouTubeService", "YouTubeChatService", "singleton", f_youtube, fid_app_xaml_cs)

    # Web/Webhook singletons
    insert_di_registration("WebServerService", "WebServerService", "singleton", f_webserver, fid_app_xaml_cs)
    insert_di_registration("NgrokService", "NgrokService", "singleton", f_webserver, fid_app_xaml_cs)
    insert_di_registration("KickWebhookSubscriptionService", "KickWebhookSubscriptionService", "singleton", f_kick, fid_app_xaml_cs)
    insert_di_registration("WebhookCoordinatorService", "WebhookCoordinatorService", "singleton", f_webserver, fid_app_xaml_cs)
    insert_di_registration("WebServerDependencyTracker", "WebServerDependencyTracker", "singleton", f_webserver, fid_app_xaml_cs)

    # Services
    insert_di_registration("GiveawayService", "GiveawayService", "singleton", f_giveaway, fid_app_xaml_cs)
    insert_di_registration("GiveawayCommandService", "GiveawayCommandService", "singleton", f_giveaway, fid_app_xaml_cs)
    insert_di_registration("PollService", "PollService", "singleton", f_poll, fid_app_xaml_cs)
    insert_di_registration("PollCommandService", "PollCommandService", "singleton", f_poll, fid_app_xaml_cs)
    insert_di_registration("WatchtimeService", "WatchtimeService", "singleton", f_watchtime, fid_app_xaml_cs)
    insert_di_registration("WatchtimeCommandService", "WatchtimeCommandService", "singleton", f_watchtime, fid_app_xaml_cs)

    # Hosted services
    insert_di_registration("IHostedService", "WatchtimeBackgroundService", "singleton", f_watchtime, fid_app_xaml_cs, None, "Background service awarding watchtime currency")
    insert_di_registration("IHostedService", "PerformanceMonitor", "singleton", f_performance, fid_app_xaml_cs, None, "Background service monitoring GC/memory/threads")

    # UI services
    insert_di_registration("ProfileAwareDataService", "ProfileAwareDataService", "singleton", f_profiles, fid_app_xaml_cs, None, "Wraps IDataService with profile-aware DB switching")
    insert_di_registration("ProfileSettingsLoader", "ProfileSettingsLoader", "singleton", f_profiles, fid_app_xaml_cs)

    # ViewModels (all singletons to prevent memory leaks)
    vm_registrations = [
        ("DashboardViewModel", f_dashboard),
        ("MainViewModel", f_dashboard),
        ("PlatformsViewModel", f_platforms_hub),
        ("ChatViewModel", f_chat),
        ("CommandsViewModel", f_commands),
        ("CurrencyViewModel", f_currency),
        ("GiveawayViewModel", f_giveaway),
        ("PollViewModel", f_poll),
        ("WatchtimeViewModel", f_watchtime),
        ("VariablesViewModel", f_variables),
        ("SettingsViewModel", f_settings),
        ("AudioSettingsViewModel", f_audio),
        ("SoundsViewModel", f_audio),
        ("AlertSettingsViewModel", f_alerts),
        ("StatisticsViewModel", f_statistics),
        ("EventHistoryViewModel", f_event_history),
        ("EventTestingViewModel", f_event_testing),
        ("ConditionalRulesViewModel", f_auto_reply),
        ("WebServerSettingsViewModel", f_webserver),
        ("TwitchWebSetupViewModel", f_twitch),
        ("TwitchDashboardViewModel", f_twitch),
        ("TwitchSettingsViewModel", f_twitch),
        ("TwitchChatSettingsViewModel", f_twitch),
        ("KickWebSetupViewModel", f_kick),
        ("KickDashboardViewModel", f_kick),
        ("KickSettingsViewModel", f_kick),
        ("KickChatSettingsViewModel", f_kick),
        ("YouTubeWebSetupViewModel", f_youtube),
        ("YoutubeChatSettingsViewModel", f_youtube),
        ("TikTokWebSetupViewModel", f_tiktok),
        ("TiktokChatSettingsViewModel", f_tiktok),
        ("DiscordWebSetupViewModel", f_discord),
        ("DiscordChatSettingsViewModel", f_discord),
        ("ProfileSelectorViewModel", f_profiles),
        ("NgrokSetupWizardViewModel", f_webserver),
    ]
    for vm_name, feat_id in vm_registrations:
        insert_di_registration(vm_name, vm_name, "singleton", feat_id, fid_app_xaml_cs, None, "ViewModel — registered as singleton to prevent memory leaks")

    print(f"  Inserted {len(vm_registrations) + 38} DI registrations")

    # ============================================================
    # 7. NAVIGATION MAP
    # ============================================================
    print("[7/9] Inserting navigation map...")

    # Get file IDs for views and viewmodels
    conn = get_conn()

    def get_file_id(path_fragment):
        row = conn.execute("SELECT id FROM files WHERE path LIKE ?", (f"%{path_fragment}%",)).fetchone()
        return row[0] if row else None

    nav_items = [
        ("Home", "Dashboard", f_dashboard, "DashboardViewModel.cs", None, "NavigateToHomeCommand"),
        ("Platforms", "Platforms Hub", f_platforms_hub, "PlatformsViewModel.cs", "PlatformsView.xaml", "NavigateToPlatformsCommand"),
        ("Platforms", "Twitch Dashboard", f_twitch, "TwitchDashboardViewModel.cs", "TwitchDashboardView.xaml", "NavigateToTwitchSettingsCommand"),
        ("Platforms", "Kick Dashboard", f_kick, "KickDashboardViewModel.cs", "KickDashboardView.xaml", "NavigateToKickSettingsCommand"),
        ("Chat Features", "Chat", f_chat, "ChatViewModel.cs", "ChatView.xaml", "NavigateToChatCommand"),
        ("Chat Features", "Commands", f_commands, "CommandsViewModel.cs", "CommandsView.xaml", "NavigateToCommandsCommand"),
        ("Chat Features", "Conditional Rules", f_auto_reply, "ConditionalRulesViewModel.cs", "ConditionalRulesView.xaml", "NavigateToConditionalRulesCommand"),
        ("Stream Tools", "Currency", f_currency, "CurrencyViewModel.cs", "CurrencyView.xaml", "NavigateToCurrencyCommand"),
        ("Stream Tools", "Giveaways", f_giveaway, "GiveawayViewModel.cs", "GiveawayView.xaml", "NavigateToGiveawaysCommand"),
        ("Stream Tools", "Polls", f_poll, "PollViewModel.cs", "PollView.xaml", "NavigateToPollsCommand"),
        ("Stream Tools", "Watchtime", f_watchtime, "WatchtimeViewModel.cs", "WatchtimeView.xaml", "NavigateToWatchtimeCommand"),
        ("Audio & Alerts", "Audio Settings", f_audio, "AudioSettingsViewModel.cs", "AudioSettingsView.xaml", "NavigateToAudioSettingsCommand"),
        ("Audio & Alerts", "Sounds", f_audio, "SoundsViewModel.cs", "SoundsView.xaml", "NavigateToSoundsCommand"),
        ("Audio & Alerts", "Alert Settings", f_alerts, "AlertSettingsViewModel.cs", "AlertSettingsView.xaml", "NavigateToAlertSettingsCommand"),
        ("Monitoring", "Statistics", f_statistics, "StatisticsViewModel.cs", "StatisticsView.xaml", "NavigateToStatisticsCommand"),
        ("Monitoring", "Event History", f_event_history, "EventHistoryViewModel.cs", "EventHistoryView.xaml", "NavigateToEventHistoryCommand"),
        ("Monitoring", "Event Testing", f_event_testing, "EventTestingViewModel.cs", "EventTestingView.xaml", "NavigateToEventTestingCommand"),
        ("Settings", "Global Settings", f_settings, "SettingsViewModel.cs", "SettingsView.xaml", "NavigateToSettingsCommand"),
        ("Settings", "Web Server", f_webserver, "WebServerSettingsViewModel.cs", "WebServerSettingsView.xaml", "NavigateToWebServerSettingsCommand"),
        ("Utilities", "Variables", f_variables, "VariablesViewModel.cs", "VariablesView.xaml", "NavigateToVariablesCommand"),
        ("Utilities", "Demo Wizard", f_platforms_hub, "GoogleDemoSetupViewModel.cs", "GoogleDemoSetupView.xaml", "NavigateToDemoWizardCommand"),
        ("Utilities", "ngrok Setup", f_webserver, "NgrokSetupWizardViewModel.cs", "NgrokSetupWizardView.xaml", "NavigateToNgrokSetupWizardCommand"),
    ]

    for section, label, feat_id, vm_file, view_file, cmd in nav_items:
        vm_fid = get_file_id(vm_file)
        v_fid = get_file_id(view_file) if view_file else None
        insert_navigation_item(section, label, feat_id, v_fid, vm_fid, cmd)

    conn.close()
    print(f"  Inserted {len(nav_items)} navigation items")

    # ============================================================
    # 8. ARCHITECTURE RULES
    # ============================================================
    print("[8/9] Inserting architecture rules...")

    # Performance rules
    insert_architecture_rule("performance", "EventBus must use bounded Channel<T> with capacity 500 and DropOldest backpressure. Max 4 concurrent handlers.", "critical",
        ["all"], "Channel.CreateBounded<T>(new BoundedChannelOptions(500) { FullMode = DropOldest })", "Unbounded channels or unlimited concurrency",
        "CONTEXT_MASTER.md")
    insert_architecture_rule("performance", "Use ArrayPool<byte> for all buffer allocations. Never allocate byte[] directly in hot paths.", "critical",
        ["all"], "var buffer = ArrayPool<byte>.Shared.Rent(4096); try { ... } finally { ArrayPool<byte>.Shared.Return(buffer); }",
        "new byte[4096] in loops or frequently-called methods", "PERFORMANCE.md")
    insert_architecture_rule("performance", "Pre-allocate collections with known capacity. Use List<T>(capacity), Dictionary<K,V>(capacity).", "warning",
        ["all"], "new List<string>(expectedCount)", "new List<string>() in hot paths", "PERFORMANCE.md")
    insert_architecture_rule("performance", "Target memory usage: 40-80 MB. CPU idle: <1%. CPU active: 2-3%. Threads: 8-12.", "critical",
        ["all"], None, None, "CONTEXT_MASTER.md")
    insert_architecture_rule("performance", "UI event log capped at 50 items. Use ObservableCollection efficiently — batch updates when possible.", "warning",
        ["dashboard", "event-history", "statistics"], None, None, "OPTIMIZATIONS.md")
    insert_architecture_rule("performance", "Server GC mode enabled. Low-latency GC. Tiered compilation. These are set in .csproj and must not be changed.", "critical",
        ["all"], None, None, "PERFORMANCE.md")

    # Process management rules
    insert_architecture_rule("required", "Zero child processes. VyralBot.exe must be the ONLY running process. Any external process MUST be tracked by IProcessManager.", "critical",
        ["all"], "processManager.TrackProcess(process)", "Process.Start() without ProcessManager tracking",
        "PROCESS_MANAGEMENT.md")
    insert_architecture_rule("required", "All tracked processes must be terminated on application shutdown via ProcessManager.StopAll().", "critical",
        ["all"], None, None, "PROCESS_MANAGEMENT.md")

    # Architecture rules
    insert_architecture_rule("required", "All cross-module communication MUST go through EventBus. Never call services directly across module boundaries.", "critical",
        ["all"], "await _eventBus.PublishAsync(new TwitchFollowEvent { ... })", "directService.HandleFollow() from another module",
        "CONTEXT_MASTER.md")
    insert_architecture_rule("required", "Strict layer dependency: UI → Core → Integrations → Data. No reverse dependencies. Lower layers never reference upper layers.", "critical",
        ["all"], None, "VyralBot.Core referencing VyralBot.UI", "CONTEXT_MASTER.md")
    insert_architecture_rule("required", "All services must be registered via DI in App.xaml.cs. No static service locator or direct instantiation.", "critical",
        ["all"], "services.AddSingleton<IMyService, MyService>()", "new MyService() or ServiceLocator.Get<IMyService>()",
        "CONTEXT_MASTER.md")
    insert_architecture_rule("required", "All ViewModels must be registered as SINGLETONS to prevent memory leaks during navigation.", "critical",
        ["all"], "services.AddSingleton<MyViewModel>()", "services.AddTransient<MyViewModel>()",
        "CONTEXT_MASTER.md")
    insert_architecture_rule("required", "Profile is loaded FIRST during startup. All services depend on the active profile's database path via ProfileAwareDataService.", "critical",
        ["profiles"], None, None, "CONTEXT_MASTER.md")
    insert_architecture_rule("required", "ChatViewModel must subscribe to EventBus BEFORE platforms connect to avoid missing messages.", "critical",
        ["chat"], None, None, "CONTEXT_MASTER.md")

    # Security rules
    insert_architecture_rule("security", "OAuth tokens must be encrypted using Windows DPAPI (ProtectedData) before storage. Never store plain-text tokens.", "critical",
        ["twitch", "kick", "youtube"], "ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser)",
        "Storing AccessToken as plain text in database", "IMPLEMENTATION.md")
    insert_architecture_rule("security", "Webhook signatures must be validated before processing. Use platform-specific validation middleware.", "critical",
        ["webserver", "kick"], "KickWebhookValidationMiddleware validates HMAC signatures", "Processing webhooks without signature verification",
        "IMPLEMENTATION.md")
    insert_architecture_rule("security", "Localhost-first: All services bind to 127.0.0.1 by default. External exposure only via ngrok tunnel.", "critical",
        ["webserver"], None, "Binding to 0.0.0.0 without explicit user consent", "IMPLEMENTATION.md")
    insert_architecture_rule("security", "Use parameterized SQL queries via Dapper. Never concatenate user input into SQL strings.", "critical",
        ["all"], 'conn.QueryAsync<T>("SELECT * FROM X WHERE Id = @Id", new { Id = id })',
        'conn.QueryAsync<T>($"SELECT * FROM X WHERE Id = {id}")', "IMPLEMENTATION.md")

    # Style rules
    insert_architecture_rule("style", "Use async/await throughout. No blocking calls (.Result, .Wait(), .GetAwaiter().GetResult()).", "critical",
        ["all"], "await service.DoWorkAsync()", "service.DoWorkAsync().Result",
        "IMPLEMENTATION.md")
    insert_architecture_rule("style", "Use CancellationToken propagation on all async methods. Accept and pass tokens.", "warning",
        ["all"], "async Task DoWorkAsync(CancellationToken ct)", "async Task DoWorkAsync() without cancellation support",
        "IMPLEMENTATION.md")
    insert_architecture_rule("style", "Interfaces for all services. Program to interfaces, not implementations.", "warning",
        ["all"], "IMyService → MyService registered via DI", "Using concrete MyService type directly",
        "IMPLEMENTATION.md")
    insert_architecture_rule("style", "Use Serilog structured logging. Log at Information level in production. No Debug-level logging in production builds.", "warning",
        ["all"], '_logger.Information("User {Username} connected to {Platform}", user, platform)',
        '_logger.Debug("Processing...")', "IMPLEMENTATION.md")

    # Forbidden patterns
    insert_architecture_rule("forbidden", "NEVER use Entity Framework. Use Dapper only for data access (memory efficiency).", "critical",
        ["all"], "Dapper conn.QueryAsync<T>(sql, params)", "DbContext, DbSet<T>, LINQ-to-SQL",
        "CONTEXT_MASTER.md")
    insert_architecture_rule("forbidden", "NEVER spawn WebView2 child processes for anything other than OAuth wizard. WebView2EnvironmentManager controls the single instance.", "critical",
        ["all"], None, "Creating multiple WebView2 instances", "PROCESS_MANAGEMENT.md")
    insert_architecture_rule("forbidden", "NEVER use Thread.Sleep() or Task.Delay() for synchronization. Use proper async patterns (SemaphoreSlim, channels).", "critical",
        ["all"], "await _semaphore.WaitAsync(ct)", "Thread.Sleep(1000)", "IMPLEMENTATION.md")
    insert_architecture_rule("forbidden", "NEVER store secrets in appsettings.json or environment variables. Use DPAPI encryption via database storage.", "critical",
        ["all"], None, 'appsettings.json: {"TwitchToken": "abc123"}', "IMPLEMENTATION.md")

    print(f"  Inserted {25} architecture rules")

    # ============================================================
    # 9. FTS5 SEARCH INDEX
    # ============================================================
    print("[9/9] Building FTS5 search index...")
    rebuild_fts_index()

    print("\n" + "=" * 60)
    print("POPULATION COMPLETE!")
    print("=" * 60)

    # Print summary
    conn = get_conn()
    tables = ["features", "files", "code_entities", "db_tables", "db_columns", "data_operations",
              "events", "event_flows", "di_registrations", "navigation_map", "architecture_rules",
              "project_metadata", "search_index"]
    for t in tables:
        count = conn.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0]
        print(f"  {t}: {count} rows")
    conn.close()


if __name__ == "__main__":
    main()
