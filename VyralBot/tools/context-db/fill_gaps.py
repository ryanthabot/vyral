"""
VyralBot Context Database — Gap Filler
========================================
Populates the remaining empty tables:
  - db_columns (correct schema for ALL 24 tables)
  - code_entities (interfaces, classes, key methods)
  - data_operations (all SQL CRUD mapped to methods/tables)
Then rebuilds the FTS index.
"""

import sys, os
sys.path.insert(0, os.path.dirname(__file__))
from populate import *


def get_feature_id(name):
    conn = get_conn()
    row = conn.execute("SELECT id FROM features WHERE name = ?", (name,)).fetchone()
    conn.close()
    return row[0] if row else None


def get_file_id(path):
    conn = get_conn()
    row = conn.execute("SELECT id FROM files WHERE path = ?", (path,)).fetchone()
    conn.close()
    return row[0] if row else None


def get_table_id(name):
    conn = get_conn()
    row = conn.execute("SELECT id FROM db_tables WHERE name = ?", (name,)).fetchone()
    conn.close()
    return row[0] if row else None


# ============================================================
# 1. FIX & COMPLETE db_columns
# ============================================================
def populate_db_columns():
    print("\n[1/4] Fixing and completing db_columns for ALL 24 tables...")

    # Clear old inaccurate data
    conn = get_conn()
    conn.execute("DELETE FROM db_columns")
    conn.commit()
    conn.close()
    print("  Cleared old db_columns")

    # --- Schema definitions extracted from DapperDataService.cs lines 1648-2260 ---
    # Format: (table_name, [(col_name, col_type, nullable, default, is_pk, is_fk, fk_target, description), ...])

    tables = {
        "Platforms": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Platform name (Twitch, Kick, etc.)"),
            ("Enabled", "INTEGER", False, "1", False, False, None, "Whether platform is enabled"),
            ("AccessToken", "TEXT", True, None, False, False, None, "OAuth access token"),
            ("RefreshToken", "TEXT", True, None, False, False, None, "OAuth refresh token"),
            ("TokenExpiresAt", "TEXT", True, None, False, False, None, "Token expiration timestamp"),
            ("Username", "TEXT", True, None, False, False, None, "Authenticated username"),
            ("UserId", "TEXT", True, None, False, False, None, "Platform user ID"),
            ("ChannelId", "TEXT", True, None, False, False, None, "Kick channel ID"),
            ("ChatroomId", "TEXT", True, None, False, False, None, "Kick chatroom ID"),
            ("ClientId", "TEXT", True, None, False, False, None, "OAuth client ID"),
            ("ClientSecret", "TEXT", True, None, False, False, None, "OAuth client secret"),
            ("Status", "TEXT", False, "'Disconnected'", False, False, None, "Connection status"),
            ("LastError", "TEXT", True, None, False, False, None, "Last error message"),
            ("ConnectedAt", "TEXT", True, None, False, False, None, "Last connection timestamp"),
            ("KickChatEnabled", "INTEGER", False, "0", False, False, None, "Kick chat integration enabled"),
            ("KickChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-start Kick chat on launch"),
            ("TwitchChatEnabled", "INTEGER", False, "0", False, False, None, "Twitch chat integration enabled"),
            ("TwitchChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-start Twitch chat on launch"),
            ("YoutubeChatEnabled", "INTEGER", False, "0", False, False, None, "YouTube chat integration enabled"),
            ("YoutubeChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-start YouTube chat on launch"),
            ("TiktokChatEnabled", "INTEGER", False, "0", False, False, None, "TikTok chat integration enabled"),
            ("TiktokChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-start TikTok chat on launch"),
            ("DiscordChatEnabled", "INTEGER", False, "0", False, False, None, "Discord chat integration enabled"),
            ("DiscordChatAutoStart", "INTEGER", False, "0", False, False, None, "Auto-start Discord chat on launch"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "Configs": [
            ("Key", "TEXT", False, None, True, False, None, "Configuration key (primary key)"),
            ("Value", "TEXT", False, None, False, False, None, "Configuration value"),
            ("Category", "TEXT", True, None, False, False, None, "Config category for grouping"),
            ("IsEncrypted", "INTEGER", False, "0", False, False, None, "Whether value is encrypted"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "Triggers": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Trigger display name"),
            ("Description", "TEXT", True, None, False, False, None, "Trigger description"),
            ("Enabled", "INTEGER", False, "1", False, False, None, "Whether trigger is active"),
            ("EventType", "TEXT", False, None, False, False, None, "Event type that fires this trigger"),
            ("Platform", "TEXT", True, None, False, False, None, "Platform filter (null = all)"),
            ("Condition", "TEXT", True, None, False, False, None, "JSON condition expression"),
            ("CooldownSeconds", "INTEGER", False, "0", False, False, None, "Cooldown between firings"),
            ("LastFiredAt", "TEXT", True, None, False, False, None, "Last fire timestamp"),
            ("FireCount", "INTEGER", False, "0", False, False, None, "Total times fired"),
            ("Permission", "TEXT", False, "'Everyone'", False, False, None, "Required permission level"),
            ("ActionSequence", "TEXT", False, "'[]'", False, False, None, "JSON array of action IDs to execute"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "Actions": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Action display name"),
            ("Description", "TEXT", True, None, False, False, None, "Action description"),
            ("ActionType", "TEXT", False, None, False, False, None, "Type of action (SendChat, RunCommand, etc.)"),
            ("Configuration", "TEXT", False, None, False, False, None, "JSON configuration blob"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "EventLogs": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Platform", "TEXT", False, None, False, False, None, "Source platform"),
            ("EventType", "TEXT", False, None, False, False, None, "Event type name"),
            ("Data", "TEXT", True, None, False, False, None, "JSON event payload"),
            ("TriggerId", "TEXT", True, None, False, True, "Triggers", "Associated trigger ID"),
            ("Timestamp", "TEXT", False, None, False, False, None, "Event timestamp"),
        ],
        "WebServerSettings": [
            ("Id", "INTEGER", False, None, True, False, None, "Singleton row ID (always 1)"),
            ("IsEnabled", "INTEGER", False, "0", False, False, None, "Whether web server is enabled"),
            ("Port", "INTEGER", False, "5000", False, False, None, "HTTP listen port"),
            ("NgrokRegion", "TEXT", False, "'us'", False, False, None, "Ngrok tunnel region"),
            ("CurrentTunnelUrl", "TEXT", True, None, False, False, None, "Active ngrok tunnel URL"),
            ("AutoStartOnLaunch", "INTEGER", False, "1", False, False, None, "Start server on app launch"),
            ("LastStarted", "TEXT", True, None, False, False, None, "Last server start time"),
            ("LastStopped", "TEXT", True, None, False, False, None, "Last server stop time"),
        ],
        "WebhookSubscriptions": [
            ("Id", "TEXT", False, None, True, False, None, "Subscription ID from platform"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform name"),
            ("EventType", "TEXT", False, None, False, False, None, "Webhook event type"),
            ("EventVersion", "INTEGER", False, "1", False, False, None, "Event schema version"),
            ("BroadcasterUserId", "TEXT", True, None, False, False, None, "Platform broadcaster user ID"),
            ("WebhookUrl", "TEXT", False, None, False, False, None, "Callback URL for webhook"),
            ("IsActive", "INTEGER", False, "1", False, False, None, "Whether subscription is active"),
            ("LastEventReceived", "TEXT", True, None, False, False, None, "Timestamp of last received event"),
            ("CreatedAt", "TEXT", False, "CURRENT_TIMESTAMP", False, False, None, "Record creation timestamp"),
        ],
        "ChatMessages": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Platform", "TEXT", False, None, False, False, None, "Source platform"),
            ("Channel", "TEXT", False, None, False, False, None, "Channel name"),
            ("MessageId", "TEXT", True, None, False, False, None, "Platform message ID for dedup"),
            ("UserId", "TEXT", True, None, False, False, None, "Platform user ID"),
            ("Username", "TEXT", True, None, False, False, None, "Sender username"),
            ("DisplayName", "TEXT", True, None, False, False, None, "Sender display name"),
            ("Content", "TEXT", True, None, False, False, None, "Message text content"),
            ("Timestamp", "TEXT", False, None, False, False, None, "Message timestamp"),
            ("Color", "TEXT", True, None, False, False, None, "Username color hex"),
            ("IsModerator", "INTEGER", False, "0", False, False, None, "Sender is moderator"),
            ("IsSubscriber", "INTEGER", False, "0", False, False, None, "Sender is subscriber"),
            ("IsOwner", "INTEGER", False, "0", False, False, None, "Sender is channel owner"),
            ("BadgesJson", "TEXT", True, None, False, False, None, "JSON array of chat badges"),
            ("IsSystemMessage", "INTEGER", False, "0", False, False, None, "Whether this is a system message"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
        ],
        "ChatEventSettings": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform name"),
            ("EventType", "TEXT", False, None, False, False, None, "Event type"),
            ("ShowInChat", "INTEGER", False, "1", False, False, None, "Show event in chat overlay"),
            ("AutoReplyEnabled", "INTEGER", False, "0", False, False, None, "Enable auto-reply for this event"),
            ("AutoReplyMessage", "TEXT", True, None, False, False, None, "Auto-reply message template"),
            ("SoundEnabled", "INTEGER", False, "1", False, False, None, "Play sound on event"),
            ("SoundFile", "TEXT", True, None, False, False, None, "Path to sound file"),
            ("SoundVolume", "INTEGER", False, "100", False, False, None, "Sound volume (0-100)"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "AutoReplyRules": [
            ("Id", "INTEGER", False, None, True, False, None, "Auto-increment primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Rule display name"),
            ("Platform", "TEXT", False, None, False, False, None, "Target platform"),
            ("EventType", "TEXT", False, None, False, False, None, "Event type to match"),
            ("IsEnabled", "INTEGER", False, "1", False, False, None, "Whether rule is active"),
            ("Priority", "INTEGER", False, "0", False, False, None, "Execution priority (lower = first)"),
            ("ConditionJson", "TEXT", False, "'{}'", False, False, None, "JSON condition for matching"),
            ("ReplyTemplate", "TEXT", False, None, False, False, None, "Reply template with variables"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
            ("TriggerCount", "INTEGER", False, "0", False, False, None, "Times this rule has fired"),
            ("LastTriggeredAt", "TEXT", True, None, False, False, None, "Last trigger timestamp"),
        ],
        "EventStatistics": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Platform", "TEXT", False, None, False, False, None, "Source platform"),
            ("EventType", "TEXT", False, None, False, False, None, "Event type"),
            ("Username", "TEXT", False, None, False, False, None, "Associated username"),
            ("EventData", "TEXT", True, None, False, False, None, "JSON event data"),
            ("Timestamp", "TEXT", False, None, False, False, None, "Event timestamp"),
            ("WasAutoReplied", "INTEGER", False, "0", False, False, None, "Whether auto-reply was sent"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
        ],
        "CurrencySettings": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key (singleton)"),
            ("CurrencyName", "TEXT", False, "'Points'", False, False, None, "Display name for currency"),
            ("CurrencySymbol", "TEXT", False, "'⭐'", False, False, None, "Currency symbol/emoji"),
            ("WatchTimeEnabled", "INTEGER", False, "1", False, False, None, "Earn currency by watching"),
            ("WatchTimeInterval", "INTEGER", False, "60", False, False, None, "Seconds between watchtime payouts"),
            ("WatchTimeAmount", "INTEGER", False, "10", False, False, None, "Amount per watchtime interval"),
            ("ChatActivityEnabled", "INTEGER", False, "1", False, False, None, "Earn currency by chatting"),
            ("ChatActivityCooldown", "INTEGER", False, "120", False, False, None, "Seconds between chat payouts"),
            ("ChatActivityAmount", "INTEGER", False, "5", False, False, None, "Amount per chat activity"),
            ("FollowBonusEnabled", "INTEGER", False, "1", False, False, None, "Bonus for new follows"),
            ("FollowBonusAmount", "INTEGER", False, "100", False, False, None, "Follow bonus amount"),
            ("SubBonusEnabled", "INTEGER", False, "1", False, False, None, "Bonus for subscriptions"),
            ("SubBonusAmount", "INTEGER", False, "500", False, False, None, "Subscription bonus amount"),
            ("GiftSubBonusEnabled", "INTEGER", False, "1", False, False, None, "Bonus for gifted subs"),
            ("GiftSubBonusAmountPerSub", "INTEGER", False, "50", False, False, None, "Amount per gifted sub"),
            ("RaidBonusEnabled", "INTEGER", False, "1", False, False, None, "Bonus for raids"),
            ("RaidBonusAmountPerViewer", "INTEGER", False, "10", False, False, None, "Amount per raid viewer"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "UserCurrency": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Username", "TEXT", False, None, False, False, None, "Username"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform"),
            ("Balance", "INTEGER", False, "0", False, False, None, "Current balance"),
            ("TotalEarned", "INTEGER", False, "0", False, False, None, "Lifetime earned"),
            ("TotalSpent", "INTEGER", False, "0", False, False, None, "Lifetime spent"),
            ("LastWatchTimeUpdate", "TEXT", True, None, False, False, None, "Last watchtime payout"),
            ("LastChatActivityUpdate", "TEXT", True, None, False, False, None, "Last chat activity payout"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "CurrencyTransactions": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Username", "TEXT", False, None, False, False, None, "Username"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform"),
            ("Amount", "INTEGER", False, None, False, False, None, "Transaction amount (+/-)"),
            ("TransactionType", "TEXT", False, None, False, False, None, "Type (earn, spend, admin, etc.)"),
            ("Reason", "TEXT", True, None, False, False, None, "Reason description"),
            ("BalanceBefore", "INTEGER", False, None, False, False, None, "Balance before transaction"),
            ("BalanceAfter", "INTEGER", False, None, False, False, None, "Balance after transaction"),
            ("Timestamp", "TEXT", False, None, False, False, None, "Transaction timestamp"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
        ],
        "CurrencyRewards": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Reward display name"),
            ("Description", "TEXT", True, None, False, False, None, "Reward description"),
            ("Cost", "INTEGER", False, None, False, False, None, "Cost in currency"),
            ("IsEnabled", "INTEGER", False, "1", False, False, None, "Whether reward is available"),
            ("RequiresApproval", "INTEGER", False, "0", False, False, None, "Needs mod approval"),
            ("Cooldown", "INTEGER", False, "0", False, False, None, "Cooldown seconds between redemptions"),
            ("MaxRedemptionsPerUser", "INTEGER", False, "0", False, False, None, "Max per user (0=unlimited)"),
            ("MaxRedemptionsTotal", "INTEGER", False, "0", False, False, None, "Max total (0=unlimited)"),
            ("CurrentRedemptions", "INTEGER", False, "0", False, False, None, "Current redemption count"),
            ("Category", "TEXT", True, None, False, False, None, "Reward category"),
            ("Prompt", "TEXT", True, None, False, False, None, "User input prompt text"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "CurrencyRedemptions": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("RewardId", "TEXT", False, None, False, True, "CurrencyRewards", "FK to CurrencyRewards.Id"),
            ("RewardName", "TEXT", False, None, False, False, None, "Snapshot of reward name"),
            ("Username", "TEXT", False, None, False, False, None, "Redeeming username"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform"),
            ("Cost", "INTEGER", False, None, False, False, None, "Cost at time of redemption"),
            ("UserInput", "TEXT", True, None, False, False, None, "User-provided input text"),
            ("Status", "TEXT", False, "'Pending'", False, False, None, "Pending/Approved/Rejected/Cancelled"),
            ("RedeemedAt", "TEXT", False, None, False, False, None, "Redemption timestamp"),
            ("ProcessedAt", "TEXT", True, None, False, False, None, "When processed by mod"),
            ("ProcessedBy", "TEXT", True, None, False, False, None, "Mod who processed"),
            ("Notes", "TEXT", True, None, False, False, None, "Mod notes"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
        ],
        "Giveaways": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Giveaway title"),
            ("Description", "TEXT", True, None, False, False, None, "Giveaway description"),
            ("EntryKeyword", "TEXT", False, None, False, False, None, "Chat keyword to enter"),
            ("Status", "INTEGER", False, "0", False, False, None, "0=Created,1=Active,2=Ended,3=Cancelled"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("StartedAt", "TEXT", True, None, False, False, None, "When giveaway started"),
            ("EndedAt", "TEXT", True, None, False, False, None, "When giveaway ended"),
            ("ScheduledEndTime", "TEXT", True, None, False, False, None, "Auto-end time"),
            ("MaxEntries", "INTEGER", True, None, False, False, None, "Max entries (null=unlimited)"),
            ("WinnerCount", "INTEGER", False, "1", False, False, None, "Number of winners to pick"),
            ("SubscriberBonusEnabled", "INTEGER", False, "0", False, False, None, "Extra entries for subs"),
            ("SubscriberBonusEntries", "INTEGER", False, "1", False, False, None, "Bonus entry count for subs"),
            ("MinWatchtimeMinutes", "INTEGER", False, "0", False, False, None, "Min watchtime required"),
            ("AllowedPlatforms", "TEXT", False, "'All'", False, False, None, "Comma-separated platform list"),
            ("FollowersOnly", "INTEGER", False, "0", False, False, None, "Restrict to followers"),
            ("SubscribersOnly", "INTEGER", False, "0", False, False, None, "Restrict to subscribers"),
            ("Winners", "TEXT", True, None, False, False, None, "JSON array of winner names"),
            ("TotalEntries", "INTEGER", False, "0", False, False, None, "Total entry count"),
            ("UniqueParticipants", "INTEGER", False, "0", False, False, None, "Unique participant count"),
            ("Metadata", "TEXT", True, None, False, False, None, "Extra JSON metadata"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "GiveawayEntries": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("GiveawayId", "TEXT", False, None, False, True, "Giveaways", "FK to Giveaways.Id"),
            ("Username", "TEXT", False, None, False, False, None, "Entrant username"),
            ("Platform", "TEXT", False, None, False, False, None, "Entrant platform"),
            ("PlatformUserId", "TEXT", True, None, False, False, None, "Platform-specific user ID"),
            ("EnteredAt", "TEXT", False, None, False, False, None, "Entry timestamp"),
            ("EntryCount", "INTEGER", False, "1", False, False, None, "Number of entries (bonus)"),
            ("IsSubscriber", "INTEGER", False, "0", False, False, None, "Was subscriber at entry"),
            ("IsFollower", "INTEGER", False, "0", False, False, None, "Was follower at entry"),
            ("WatchtimeMinutes", "INTEGER", False, "0", False, False, None, "Watchtime at entry"),
            ("Message", "TEXT", True, None, False, False, None, "Entry chat message"),
            ("IsWinner", "INTEGER", False, "0", False, False, None, "Whether this entry won"),
            ("WonAt", "TEXT", True, None, False, False, None, "Win timestamp"),
        ],
        "Polls": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Question", "TEXT", False, None, False, False, None, "Poll question text"),
            ("Options", "TEXT", False, None, False, False, None, "JSON array of option strings"),
            ("Status", "INTEGER", False, "0", False, False, None, "0=Created,1=Active,2=Ended,3=Cancelled"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("StartedAt", "TEXT", True, None, False, False, None, "When poll started"),
            ("EndedAt", "TEXT", True, None, False, False, None, "When poll ended"),
            ("ScheduledEndTime", "TEXT", True, None, False, False, None, "Auto-end time"),
            ("CreatorUsername", "TEXT", False, None, False, False, None, "Who created the poll"),
            ("TotalVotes", "INTEGER", False, "0", False, False, None, "Total vote count"),
            ("AllowMultipleVotes", "INTEGER", False, "0", False, False, None, "Allow voting for multiple options"),
            ("AllowedPlatforms", "TEXT", False, "'All'", False, False, None, "Comma-separated platforms"),
            ("FollowersOnly", "INTEGER", False, "0", False, False, None, "Restrict to followers"),
            ("SubscribersOnly", "INTEGER", False, "0", False, False, None, "Restrict to subscribers"),
            ("MinWatchtimeMinutes", "INTEGER", False, "0", False, False, None, "Min watchtime required"),
            ("VoteResults", "TEXT", True, None, False, False, None, "JSON vote results cache"),
            ("Metadata", "TEXT", True, None, False, False, None, "Extra JSON metadata"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "PollVotes": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("PollId", "TEXT", False, None, False, True, "Polls", "FK to Polls.Id"),
            ("Username", "TEXT", False, None, False, False, None, "Voter username"),
            ("Platform", "TEXT", False, None, False, False, None, "Voter platform"),
            ("PlatformUserId", "TEXT", True, None, False, False, None, "Platform-specific user ID"),
            ("OptionIndex", "INTEGER", False, None, False, False, None, "Index of selected option"),
            ("VotedAt", "TEXT", False, None, False, False, None, "Vote timestamp"),
            ("IsSubscriber", "INTEGER", False, "0", False, False, None, "Was subscriber at vote time"),
            ("IsFollower", "INTEGER", False, "0", False, False, None, "Was follower at vote time"),
            ("WatchtimeMinutes", "INTEGER", False, "0", False, False, None, "Watchtime at vote time"),
        ],
        "UserWatchtime": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Username", "TEXT", False, None, False, False, None, "Username"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform"),
            ("TotalMinutes", "INTEGER", False, "0", False, False, None, "Total watchtime minutes"),
            ("LastSeenAt", "TEXT", False, None, False, False, None, "Last activity timestamp"),
            ("FirstSeenAt", "TEXT", False, None, False, False, None, "First seen timestamp"),
            ("StreamSessionMinutes", "INTEGER", False, "0", False, False, None, "Minutes in current stream"),
            ("LastStreamId", "TEXT", True, None, False, False, None, "Current stream session ID"),
            ("IsActive", "INTEGER", False, "0", False, False, None, "Currently active viewer"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "Variables": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Variable name (unique, case-insensitive)"),
            ("DisplayName", "TEXT", False, None, False, False, None, "Display name for UI"),
            ("Category", "TEXT", False, None, False, False, None, "Variable category"),
            ("Prefix", "TEXT", True, None, False, False, None, "Variable prefix (e.g., 'twitch.')"),
            ("Value", "TEXT", True, None, False, False, None, "Current variable value"),
            ("MaxLength", "INTEGER", True, "2000", False, False, None, "Max value length"),
            ("IsEditable", "INTEGER", True, "0", False, False, None, "User can edit value"),
            ("IsSystem", "INTEGER", True, "0", False, False, None, "System-managed variable"),
            ("IsAutoUpdating", "INTEGER", True, "0", False, False, None, "Auto-refreshes from API"),
            ("ContainsNestedVariables", "INTEGER", True, "0", False, False, None, "Value contains {{var}} refs"),
            ("Platforms", "TEXT", True, None, False, False, None, "Applicable platforms"),
            ("Description", "TEXT", True, None, False, False, None, "Variable description"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
            ("LastSyncedAt", "TEXT", True, None, False, False, None, "Last API sync timestamp"),
        ],
        "Commands": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("Name", "TEXT", False, None, False, False, None, "Command name (e.g., !hello)"),
            ("Response", "TEXT", False, None, False, False, None, "Response template with variables"),
            ("Permission", "TEXT", False, "'Everyone'", False, False, None, "Required permission level"),
            ("GlobalCooldown", "INTEGER", False, "0", False, False, None, "Global cooldown seconds"),
            ("UserCooldown", "INTEGER", False, "0", False, False, None, "Per-user cooldown seconds"),
            ("IsEnabled", "INTEGER", False, "1", False, False, None, "Whether command is active"),
            ("IsBuiltIn", "INTEGER", False, "0", False, False, None, "System built-in (non-deletable)"),
            ("EnabledOnKick", "INTEGER", False, "1", False, False, None, "Active on Kick platform"),
            ("EnabledOnTwitch", "INTEGER", False, "1", False, False, None, "Active on Twitch platform"),
            ("UsageCount", "INTEGER", False, "0", False, False, None, "Total times executed"),
            ("LastUsedAt", "TEXT", True, None, False, False, None, "Last execution timestamp"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
            ("UpdatedAt", "TEXT", False, None, False, False, None, "Last update timestamp"),
        ],
        "CommandCooldowns": [
            ("Id", "TEXT", False, None, True, False, None, "GUID primary key"),
            ("CommandId", "TEXT", False, None, False, True, "Commands", "FK to Commands.Id"),
            ("CommandName", "TEXT", False, None, False, False, None, "Command name for fast lookup"),
            ("Username", "TEXT", True, None, False, False, None, "User (null = global cooldown)"),
            ("Platform", "TEXT", False, None, False, False, None, "Platform"),
            ("ExpiresAt", "TEXT", False, None, False, False, None, "Cooldown expiry timestamp"),
            ("CreatedAt", "TEXT", False, None, False, False, None, "Record creation timestamp"),
        ],
    }

    total_cols = 0
    for table_name, columns in tables.items():
        tid = get_table_id(table_name)
        if tid is None:
            print(f"  WARNING: Table '{table_name}' not found in db_tables — skipping")
            continue
        for col in columns:
            col_name, col_type, nullable, default, is_pk, is_fk, fk_target, desc = col
            insert_db_column(tid, col_name, col_type, nullable, default, is_pk, is_fk, fk_target, desc)
            total_cols += 1

    print(f"  Inserted {total_cols} columns across {len(tables)} tables")


# ============================================================
# 2. CODE ENTITIES
# ============================================================
def populate_code_entities():
    print("\n[2/4] Inserting code entities...")

    # Clear old data
    conn = get_conn()
    conn.execute("DELETE FROM code_entities")
    conn.commit()
    conn.close()

    # Lookup file and feature IDs
    f_data_service = get_file_id("src/VyralBot.Data/DapperDataService.cs")
    f_idata_service = get_file_id("src/VyralBot.Data/IDataService.cs")
    f_event_bus = get_file_id("src/VyralBot.Core/Events/IEventBus.cs")
    f_event_bus_impl = get_file_id("src/VyralBot.Core/Events/EventBus.cs")
    f_trigger_engine = get_file_id("src/VyralBot.Core/Triggers/TriggerEngine.cs")
    f_itrigger_engine = get_file_id("src/VyralBot.Core/Triggers/ITriggerEngine.cs")
    f_action_exec = get_file_id("src/VyralBot.Core/Actions/ActionExecutor.cs")
    f_iaction_exec = get_file_id("src/VyralBot.Core/Actions/IActionExecutor.cs")
    f_ichat_service = get_file_id("src/VyralBot.Core/Chat/IChatService.cs")
    f_kick_chat = get_file_id("src/VyralBot.Integrations/Kick/KickChatService.cs")
    f_twitch_chat = get_file_id("src/VyralBot.Integrations/Twitch/TwitchChatService.cs")
    f_main_vm = get_file_id("src/VyralBot.UI/ViewModels/MainViewModel.cs")
    f_app_host = get_file_id("src/VyralBot.UI/AppHost.cs")
    f_ivar_service = get_file_id("src/VyralBot.Core/Variables/IVariableService.cs")
    f_var_service = get_file_id("src/VyralBot.Core/Variables/VariableService.cs")
    f_icurrency_service = get_file_id("src/VyralBot.Core/Currency/ICurrencyService.cs")
    f_currency_service = get_file_id("src/VyralBot.Core/Currency/CurrencyService.cs")
    f_igiveaway_service = get_file_id("src/VyralBot.Core/Giveaways/IGiveawayService.cs")
    f_giveaway_service = get_file_id("src/VyralBot.Core/Giveaways/GiveawayService.cs")
    f_ipoll_service = get_file_id("src/VyralBot.Core/Polls/IPollService.cs")
    f_poll_service = get_file_id("src/VyralBot.Core/Polls/PollService.cs")
    f_iwatchtime_service = get_file_id("src/VyralBot.Core/Watchtime/IWatchtimeService.cs")
    f_watchtime_service = get_file_id("src/VyralBot.Core/Watchtime/WatchtimeService.cs")
    f_icommand_service = get_file_id("src/VyralBot.Core/Commands/ICommandService.cs")
    f_command_service = get_file_id("src/VyralBot.Core/Commands/CommandService.cs")
    f_web_server = get_file_id("src/VyralBot.Api/WebServer.cs")
    f_kick_webhook = get_file_id("src/VyralBot.Integrations/Kick/KickWebhookService.cs")

    feat_data = get_feature_id("data-service")
    feat_eventbus = get_feature_id("event-bus")
    feat_triggers = get_feature_id("trigger-engine")
    feat_actions = get_feature_id("action-executor")
    feat_chat = get_feature_id("chat-system")
    feat_currency = get_feature_id("currency-system")
    feat_giveaway = get_feature_id("giveaway-system")
    feat_polls = get_feature_id("poll-system")
    feat_watchtime = get_feature_id("watchtime-tracker")
    feat_commands = get_feature_id("command-system")
    feat_variables = get_feature_id("variable-system")
    feat_kick = get_feature_id("kick-integration")
    feat_twitch = get_feature_id("twitch-integration")
    feat_webserver = get_feature_id("web-server")

    count = 0

    # --- Interfaces ---
    def add_interface(file_id, feat_id, name, ns, summary, methods):
        nonlocal count
        eid = insert_code_entity(file_id, feat_id, "interface", name, namespace=ns, summary=summary)
        count += 1
        for m_name, m_sig, m_desc in methods:
            insert_code_entity(file_id, feat_id, "method", m_name, namespace=ns, parent_entity_id=eid, signature=m_sig, summary=m_desc)
            count += 1

    # IDataService
    add_interface(f_idata_service, feat_data, "IDataService", "VyralBot.Data",
        "Lightweight data access interface using Dapper. Replaces EF Core DbContext.",
        [
            ("GetPlatformsAsync", "Task<IEnumerable<Platform>> GetPlatformsAsync()", "Get all platforms"),
            ("GetPlatformByNameAsync", "Task<Platform?> GetPlatformByNameAsync(string name)", "Get platform by name"),
            ("GetPlatformByIdAsync", "Task<Platform?> GetPlatformByIdAsync(Guid id)", "Get platform by ID"),
            ("SavePlatformAsync", "Task SavePlatformAsync(Platform platform)", "Upsert platform"),
            ("DeletePlatformAsync", "Task DeletePlatformAsync(Guid id)", "Delete platform by ID"),
            ("GetConfigValueAsync", "Task<string?> GetConfigValueAsync(string key)", "Get config value by key"),
            ("SetConfigValueAsync", "Task SetConfigValueAsync(string key, string value, string? category)", "Upsert config value"),
            ("GetConfigsByCategoryAsync", "Task<IEnumerable<Config>> GetConfigsByCategoryAsync(string category)", "Get configs by category"),
            ("GetTriggersAsync", "Task<IEnumerable<Trigger>> GetTriggersAsync(bool? enabledOnly)", "Get triggers with optional filter"),
            ("GetTriggerByIdAsync", "Task<Trigger?> GetTriggerByIdAsync(Guid id)", "Get trigger by ID"),
            ("SaveTriggerAsync", "Task SaveTriggerAsync(Trigger trigger)", "Upsert trigger"),
            ("DeleteTriggerAsync", "Task DeleteTriggerAsync(Guid id)", "Delete trigger by ID"),
            ("GetActionsAsync", "Task<IEnumerable<BotAction>> GetActionsAsync()", "Get all actions"),
            ("GetActionByIdAsync", "Task<BotAction?> GetActionByIdAsync(Guid id)", "Get action by ID"),
            ("SaveActionAsync", "Task SaveActionAsync(BotAction action)", "Upsert action"),
            ("DeleteActionAsync", "Task DeleteActionAsync(Guid id)", "Delete action by ID"),
            ("GetEventLogsAsync", "Task<IEnumerable<EventLog>> GetEventLogsAsync(int limit)", "Get recent event logs"),
            ("LogEventAsync", "Task LogEventAsync(EventLog log)", "Insert event log"),
            ("GetWebServerSettingsAsync", "Task<WebServerSettings> GetWebServerSettingsAsync()", "Get web server settings (singleton)"),
            ("SaveWebServerSettingsAsync", "Task SaveWebServerSettingsAsync(WebServerSettings settings)", "Upsert web server settings"),
            ("GetWebhookSubscriptionsAsync", "Task<IEnumerable<WebhookSubscription>> GetWebhookSubscriptionsAsync(string? platform)", "Get webhook subscriptions"),
            ("SaveWebhookSubscriptionAsync", "Task SaveWebhookSubscriptionAsync(WebhookSubscription sub)", "Upsert webhook subscription"),
            ("DeleteWebhookSubscriptionAsync", "Task DeleteWebhookSubscriptionAsync(string id)", "Delete webhook subscription"),
            ("DeleteWebhookSubscriptionsByPlatformAsync", "Task DeleteWebhookSubscriptionsByPlatformAsync(string platform)", "Delete all platform webhook subs"),
            ("EnsureDatabaseCreatedAsync", "Task EnsureDatabaseCreatedAsync()", "Create all tables and run migrations"),
            ("SaveChatMessageAsync", "Task<bool> SaveChatMessageAsync(ChatMessage message)", "Save chat message with dedup"),
            ("GetChatMessagesAsync", "Task<IEnumerable<ChatMessage>> GetChatMessagesAsync(string platform, string channel, int limit)", "Get chat history"),
            ("DeleteChatMessagesAsync", "Task DeleteChatMessagesAsync(string platform, string channel)", "Delete chat messages"),
            ("GetChatEventSettingAsync", "Task<ChatEventSetting?> GetChatEventSettingAsync(string platform, string eventType)", "Get event setting"),
            ("GetAllChatEventSettingsAsync", "Task<IEnumerable<ChatEventSetting>> GetAllChatEventSettingsAsync(string? platform)", "Get all event settings"),
            ("SaveChatEventSettingAsync", "Task SaveChatEventSettingAsync(ChatEventSetting setting)", "Upsert event setting"),
            ("DeleteChatEventSettingAsync", "Task DeleteChatEventSettingAsync(Guid id)", "Delete event setting"),
            ("SaveEventStatisticAsync", "Task SaveEventStatisticAsync(EventStatistic stat)", "Insert event statistic"),
            ("GetEventStatisticsAsync", "Task<IEnumerable<EventStatistic>> GetEventStatisticsAsync(string? platform, string? eventType, int limit)", "Query event stats"),
            ("GetEventStatisticsByUsernameAsync", "Task<IEnumerable<EventStatistic>> GetEventStatisticsByUsernameAsync(string username, string? platform)", "Get stats for user"),
            ("GetCurrencySettingsAsync", "Task<CurrencySetting?> GetCurrencySettingsAsync()", "Get currency config (singleton)"),
            ("SaveCurrencySettingsAsync", "Task SaveCurrencySettingsAsync(CurrencySetting settings)", "Upsert currency config"),
            ("GetUserCurrencyAsync", "Task<UserCurrency?> GetUserCurrencyAsync(string username, string platform)", "Get user balance"),
            ("GetUserCurrenciesAsync", "Task<IEnumerable<UserCurrency>> GetUserCurrenciesAsync(string username)", "Get user balances across platforms"),
            ("GetCurrencyLeaderboardAsync", "Task<IEnumerable<UserCurrency>> GetCurrencyLeaderboardAsync(int limit, string? platform)", "Get currency leaderboard"),
            ("SaveUserCurrencyAsync", "Task SaveUserCurrencyAsync(UserCurrency currency)", "Upsert user currency"),
            ("SaveCurrencyTransactionAsync", "Task SaveCurrencyTransactionAsync(CurrencyTransaction txn)", "Insert currency transaction"),
            ("GetCurrencyTransactionsAsync", "Task<IEnumerable<CurrencyTransaction>> GetCurrencyTransactionsAsync(string? username, string? platform, int limit)", "Query transactions"),
            ("GetCurrencyRewardAsync", "Task<CurrencyReward?> GetCurrencyRewardAsync(string id)", "Get reward by ID"),
            ("GetCurrencyRewardsAsync", "Task<IEnumerable<CurrencyReward>> GetCurrencyRewardsAsync(bool? enabledOnly)", "Get all rewards"),
            ("SaveCurrencyRewardAsync", "Task SaveCurrencyRewardAsync(CurrencyReward reward)", "Upsert reward"),
            ("DeleteCurrencyRewardAsync", "Task DeleteCurrencyRewardAsync(string id)", "Delete reward"),
            ("GetCurrencyRedemptionAsync", "Task<CurrencyRedemption?> GetCurrencyRedemptionAsync(string id)", "Get redemption by ID"),
            ("GetCurrencyRedemptionsAsync", "Task<IEnumerable<CurrencyRedemption>> GetCurrencyRedemptionsAsync(string? status, string? username)", "Query redemptions"),
            ("SaveCurrencyRedemptionAsync", "Task SaveCurrencyRedemptionAsync(CurrencyRedemption redemption)", "Upsert redemption"),
            ("DeleteCurrencyRedemptionAsync", "Task DeleteCurrencyRedemptionAsync(string id)", "Delete redemption"),
            ("GetCommandAsync", "Task<Command?> GetCommandAsync(string id)", "Get command by ID"),
            ("GetCommandByNameAsync", "Task<Command?> GetCommandByNameAsync(string name)", "Get command by name (case-insensitive)"),
            ("GetCommandsAsync", "Task<IEnumerable<Command>> GetCommandsAsync(bool? enabledOnly, bool? builtInOnly)", "Get commands with filters"),
            ("SaveCommandAsync", "Task SaveCommandAsync(Command command)", "Upsert command"),
            ("DeleteCommandAsync", "Task DeleteCommandAsync(string id)", "Delete command (non-built-in only)"),
            ("IncrementCommandUsageAsync", "Task IncrementCommandUsageAsync(string id)", "Increment command usage counter"),
            ("GetCommandCooldownAsync", "Task<CommandCooldown?> GetCommandCooldownAsync(string commandId, string? username, string platform)", "Get active cooldown"),
            ("SaveCommandCooldownAsync", "Task SaveCommandCooldownAsync(CommandCooldown cooldown)", "Insert cooldown entry"),
            ("DeleteExpiredCooldownsAsync", "Task DeleteExpiredCooldownsAsync()", "Clean up expired cooldowns"),
            ("GetAutoReplyRulesAsync", "Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesAsync()", "Get all auto-reply rules"),
            ("GetAutoReplyRulesByPlatformAndEventAsync", "Task<IEnumerable<AutoReplyRule>> GetAutoReplyRulesByPlatformAndEventAsync(string platform, string eventType)", "Get matching rules"),
            ("GetAutoReplyRuleAsync", "Task<AutoReplyRule?> GetAutoReplyRuleAsync(int id)", "Get rule by ID"),
            ("SaveAutoReplyRuleAsync", "Task<int> SaveAutoReplyRuleAsync(AutoReplyRule rule)", "Upsert rule, returns ID"),
            ("DeleteAutoReplyRuleAsync", "Task DeleteAutoReplyRuleAsync(int id)", "Delete rule"),
            ("IncrementRuleTriggerCountAsync", "Task IncrementRuleTriggerCountAsync(int id)", "Increment rule trigger counter"),
            ("GetGiveawaysAsync", "Task<IEnumerable<Giveaway>> GetGiveawaysAsync()", "Get all giveaways"),
            ("GetGiveawayAsync", "Task<Giveaway?> GetGiveawayAsync(Guid id)", "Get giveaway by ID"),
            ("GetActiveGiveawayAsync", "Task<Giveaway?> GetActiveGiveawayAsync()", "Get currently active giveaway"),
            ("SaveGiveawayAsync", "Task SaveGiveawayAsync(Giveaway giveaway)", "Upsert giveaway"),
            ("DeleteGiveawayAsync", "Task DeleteGiveawayAsync(Guid id)", "Delete giveaway and entries"),
            ("GetGiveawayEntriesAsync", "Task<IEnumerable<GiveawayEntry>> GetGiveawayEntriesAsync(Guid giveawayId)", "Get entries for giveaway"),
            ("GetGiveawayEntryAsync", "Task<GiveawayEntry?> GetGiveawayEntryAsync(Guid giveawayId, string username, string platform)", "Get specific entry"),
            ("SaveGiveawayEntryAsync", "Task SaveGiveawayEntryAsync(GiveawayEntry entry)", "Upsert giveaway entry"),
            ("GetGiveawayEntryCountAsync", "Task<int> GetGiveawayEntryCountAsync(Guid giveawayId)", "Get total entry count"),
            ("GetGiveawayUniqueParticipantsAsync", "Task<int> GetGiveawayUniqueParticipantsAsync(Guid giveawayId)", "Get unique participant count"),
            ("GetPollsAsync", "Task<IEnumerable<Poll>> GetPollsAsync()", "Get all polls"),
            ("GetPollAsync", "Task<Poll?> GetPollAsync(Guid id)", "Get poll by ID"),
            ("GetActivePollAsync", "Task<Poll?> GetActivePollAsync()", "Get currently active poll"),
            ("SavePollAsync", "Task SavePollAsync(Poll poll)", "Upsert poll"),
            ("DeletePollAsync", "Task DeletePollAsync(Guid id)", "Delete poll and votes"),
            ("GetPollVotesAsync", "Task<IEnumerable<PollVote>> GetPollVotesAsync(Guid pollId)", "Get votes for poll"),
            ("GetPollVoteAsync", "Task<PollVote?> GetPollVoteAsync(Guid pollId, string username, string platform)", "Get specific vote"),
            ("SavePollVoteAsync", "Task SavePollVoteAsync(PollVote vote)", "Upsert poll vote"),
            ("GetPollVoteCountAsync", "Task<int> GetPollVoteCountAsync(Guid pollId)", "Get total vote count"),
            ("GetPollVoteResultsAsync", "Task<Dictionary<int,int>> GetPollVoteResultsAsync(Guid pollId)", "Get vote results by option"),
            ("GetUserWatchtimeAsync", "Task<UserWatchtime?> GetUserWatchtimeAsync(string username, string platform)", "Get user watchtime"),
            ("GetAllUserWatchtimeAsync", "Task<IEnumerable<UserWatchtime>> GetAllUserWatchtimeAsync()", "Get all watchtime records"),
            ("GetTopWatchtimeUsersAsync", "Task<IEnumerable<UserWatchtime>> GetTopWatchtimeUsersAsync(int limit)", "Get watchtime leaderboard"),
            ("GetActiveViewersAsync", "Task<IEnumerable<UserWatchtime>> GetActiveViewersAsync()", "Get currently active viewers"),
            ("GetTotalActiveViewersCountAsync", "Task<int> GetTotalActiveViewersCountAsync()", "Get active viewer count"),
            ("SaveUserWatchtimeAsync", "Task SaveUserWatchtimeAsync(UserWatchtime watchtime)", "Upsert watchtime record"),
            ("DeactivateInactiveViewersAsync", "Task DeactivateInactiveViewersAsync(DateTime inactiveSince)", "Mark inactive viewers"),
            ("ResetStreamSessionMinutesAsync", "Task ResetStreamSessionMinutesAsync()", "Reset stream session counters"),
            ("GetVariableAsync", "Task<Variable?> GetVariableAsync(string id)", "Get variable by ID"),
            ("GetVariableByNameAsync", "Task<Variable?> GetVariableByNameAsync(string name)", "Get variable by name"),
            ("GetVariablesAsync", "Task<IEnumerable<Variable>> GetVariablesAsync()", "Get all variables"),
            ("GetVariablesByCategoryAsync", "Task<IEnumerable<Variable>> GetVariablesByCategoryAsync(string category)", "Get variables by category"),
            ("GetVariableCategoriesAsync", "Task<IEnumerable<string>> GetVariableCategoriesAsync()", "Get distinct variable categories"),
            ("SaveVariableAsync", "Task SaveVariableAsync(Variable variable)", "Upsert variable"),
            ("DeleteVariableAsync", "Task DeleteVariableAsync(string id)", "Delete variable (non-system only)"),
            ("UpdateVariableValueAsync", "Task UpdateVariableValueAsync(string name, string? value)", "Update variable value by name"),
        ]
    )

    # DapperDataService class
    insert_code_entity(f_data_service, feat_data, "class", "DapperDataService",
        namespace="VyralBot.Data",
        summary="Dapper-based implementation of IDataService. All SQLite CRUD operations, table creation, and migrations.",
        is_entry_point=False)
    count += 1

    # GuidTypeHandler class
    insert_code_entity(f_data_service, feat_data, "class", "GuidTypeHandler",
        namespace="VyralBot.Data",
        summary="SqlMapper.TypeHandler<Guid> for SQLite TEXT-based GUID storage.")
    count += 1

    # IEventBus interface
    if f_event_bus:
        add_interface(f_event_bus, feat_eventbus, "IEventBus", "VyralBot.Core.Events",
            "Publish/subscribe event bus for decoupled cross-feature communication.",
            [
                ("PublishAsync", "Task PublishAsync<TEvent>(TEvent evt) where TEvent : class", "Publish event to all subscribers"),
                ("Subscribe", "IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class", "Subscribe to event type"),
            ]
        )

    # EventBus class
    if f_event_bus_impl:
        insert_code_entity(f_event_bus_impl, feat_eventbus, "class", "EventBus",
            namespace="VyralBot.Core.Events",
            summary="In-memory pub/sub event bus implementation. Thread-safe, async handlers.")
        count += 1

    # ITriggerEngine
    if f_itrigger_engine:
        add_interface(f_itrigger_engine, feat_triggers, "ITriggerEngine", "VyralBot.Core.Triggers",
            "Evaluates triggers against incoming events and fires matched actions.",
            [
                ("EvaluateAsync", "Task EvaluateAsync(string eventType, string platform, object? eventData)", "Evaluate triggers for an event"),
                ("ReloadTriggersAsync", "Task ReloadTriggersAsync()", "Reload triggers from database"),
            ]
        )

    # TriggerEngine class
    if f_trigger_engine:
        insert_code_entity(f_trigger_engine, feat_triggers, "class", "TriggerEngine",
            namespace="VyralBot.Core.Triggers",
            summary="Evaluates trigger conditions, manages cooldowns, fires action sequences.")
        count += 1

    # IActionExecutor
    if f_iaction_exec:
        add_interface(f_iaction_exec, feat_actions, "IActionExecutor", "VyralBot.Core.Actions",
            "Executes bot actions (SendChat, Delay, API call, etc.).",
            [
                ("ExecuteAsync", "Task ExecuteAsync(BotAction action, IDictionary<string,object> context)", "Execute a single action"),
                ("ExecuteSequenceAsync", "Task ExecuteSequenceAsync(IEnumerable<BotAction> actions, IDictionary<string,object> context)", "Execute action sequence"),
            ]
        )

    # IChatService
    if f_ichat_service:
        add_interface(f_ichat_service, feat_chat, "IChatService", "VyralBot.Core.Chat",
            "Platform-agnostic chat service interface for sending/receiving messages.",
            [
                ("ConnectAsync", "Task ConnectAsync()", "Connect to chat"),
                ("DisconnectAsync", "Task DisconnectAsync()", "Disconnect from chat"),
                ("SendMessageAsync", "Task SendMessageAsync(string channel, string message)", "Send a chat message"),
            ]
        )

    # KickChatService
    if f_kick_chat:
        insert_code_entity(f_kick_chat, feat_kick, "class", "KickChatService",
            namespace="VyralBot.Integrations.Kick",
            summary="Kick platform chat via WebSocket (Pusher protocol). Implements IChatService.")
        count += 1

    # TwitchChatService
    if f_twitch_chat:
        insert_code_entity(f_twitch_chat, feat_twitch, "class", "TwitchChatService",
            namespace="VyralBot.Integrations.Twitch",
            summary="Twitch chat via TwitchLib EventSub WebSocket. Implements IChatService.")
        count += 1

    # ICurrencyService
    if f_icurrency_service:
        add_interface(f_icurrency_service, feat_currency, "ICurrencyService", "VyralBot.Core.Currency",
            "Currency system: balances, transactions, rewards, and redemptions.",
            [
                ("GetBalanceAsync", "Task<int> GetBalanceAsync(string username, string platform)", "Get user balance"),
                ("AddCurrencyAsync", "Task AddCurrencyAsync(string username, string platform, int amount, string reason)", "Add currency"),
                ("RemoveCurrencyAsync", "Task<bool> RemoveCurrencyAsync(string username, string platform, int amount, string reason)", "Remove currency"),
                ("GetLeaderboardAsync", "Task<IEnumerable<UserCurrency>> GetLeaderboardAsync(int limit)", "Get leaderboard"),
            ]
        )

    # IGiveawayService
    if f_igiveaway_service:
        add_interface(f_igiveaway_service, feat_giveaway, "IGiveawayService", "VyralBot.Core.Giveaways",
            "Giveaway lifecycle: create, start, enter, draw winners, end.",
            [
                ("CreateGiveawayAsync", "Task<Giveaway> CreateGiveawayAsync(string name, string keyword)", "Create new giveaway"),
                ("StartGiveawayAsync", "Task StartGiveawayAsync(Guid id)", "Start accepting entries"),
                ("EnterGiveawayAsync", "Task<bool> EnterGiveawayAsync(Guid giveawayId, string username, string platform)", "Enter giveaway"),
                ("DrawWinnersAsync", "Task<IEnumerable<string>> DrawWinnersAsync(Guid id)", "Draw random winners"),
                ("EndGiveawayAsync", "Task EndGiveawayAsync(Guid id)", "End giveaway"),
            ]
        )

    # IPollService
    if f_ipoll_service:
        add_interface(f_ipoll_service, feat_polls, "IPollService", "VyralBot.Core.Polls",
            "Poll system: create, vote, tally results.",
            [
                ("CreatePollAsync", "Task<Poll> CreatePollAsync(string question, IEnumerable<string> options, string creator)", "Create poll"),
                ("StartPollAsync", "Task StartPollAsync(Guid id)", "Start poll"),
                ("VoteAsync", "Task<bool> VoteAsync(Guid pollId, string username, string platform, int optionIndex)", "Cast vote"),
                ("EndPollAsync", "Task EndPollAsync(Guid id)", "End poll"),
                ("GetResultsAsync", "Task<Dictionary<int,int>> GetResultsAsync(Guid id)", "Get vote tally"),
            ]
        )

    # IWatchtimeService
    if f_iwatchtime_service:
        add_interface(f_iwatchtime_service, feat_watchtime, "IWatchtimeService", "VyralBot.Core.Watchtime",
            "Tracks viewer watchtime across platforms.",
            [
                ("TrackViewerAsync", "Task TrackViewerAsync(string username, string platform)", "Mark viewer as active"),
                ("UpdateWatchtimeAsync", "Task UpdateWatchtimeAsync()", "Increment watchtime for active viewers"),
                ("GetLeaderboardAsync", "Task<IEnumerable<UserWatchtime>> GetLeaderboardAsync(int limit)", "Get watchtime leaderboard"),
            ]
        )

    # ICommandService
    if f_icommand_service:
        add_interface(f_icommand_service, feat_commands, "ICommandService", "VyralBot.Core.Commands",
            "Command processing: parse, cooldown check, variable resolution, execute.",
            [
                ("ProcessCommandAsync", "Task<string?> ProcessCommandAsync(string message, string username, string platform)", "Parse and execute chat command"),
                ("GetCommandsAsync", "Task<IEnumerable<Command>> GetCommandsAsync()", "Get all commands"),
                ("SaveCommandAsync", "Task SaveCommandAsync(Command command)", "Save or update command"),
                ("DeleteCommandAsync", "Task DeleteCommandAsync(string id)", "Delete command"),
            ]
        )

    # IVariableService
    if f_ivar_service:
        add_interface(f_ivar_service, feat_variables, "IVariableService", "VyralBot.Core.Variables",
            "Variable system: resolve {{variables}} in command responses and templates.",
            [
                ("ResolveAsync", "Task<string> ResolveAsync(string template, IDictionary<string,string>? context)", "Resolve all variables in template"),
                ("GetVariablesAsync", "Task<IEnumerable<Variable>> GetVariablesAsync()", "Get all registered variables"),
                ("SetVariableAsync", "Task SetVariableAsync(string name, string? value)", "Set variable value"),
            ]
        )

    # MainViewModel
    if f_main_vm:
        insert_code_entity(f_main_vm, None, "class", "MainViewModel",
            namespace="VyralBot.UI.ViewModels",
            summary="Main application ViewModel. Hosts sidebar navigation, page routing, and app lifecycle.")
        count += 1

    # AppHost
    if f_app_host:
        insert_code_entity(f_app_host, None, "class", "AppHost",
            namespace="VyralBot.UI",
            summary="Microsoft.Extensions.Hosting-based app host. Configures all DI services, logging, and hosted services.",
            is_entry_point=True)
        count += 1

    print(f"  Inserted {count} code entities")


# ============================================================
# 3. DATA OPERATIONS
# ============================================================
def populate_data_operations():
    print("\n[3/4] Inserting data operations...")

    conn = get_conn()
    conn.execute("DELETE FROM data_operations")
    conn.commit()
    conn.close()

    f_data_service = get_file_id("src/VyralBot.Data/DapperDataService.cs")
    feat_data = get_feature_id("data-service")

    count = 0

    # Helper to insert an op
    def op(table_name, operation, method_name, sql, line=None, desc=None):
        nonlocal count
        tid = get_table_id(table_name)
        insert_data_operation(tid, feat_data, f_data_service, operation, method_name, sql, line, desc)
        count += 1

    # --- Platforms ---
    op("Platforms", "SELECT", "GetPlatformsAsync", "SELECT * FROM Platforms ORDER BY Name", 60, "Get all platforms")
    op("Platforms", "SELECT", "GetPlatformByNameAsync", "SELECT * FROM Platforms WHERE Name = @Name", 66, "Get platform by name")
    op("Platforms", "SELECT", "GetPlatformByIdAsync", "SELECT * FROM Platforms WHERE Id = @Id", 72, "Get platform by GUID")
    op("Platforms", "INSERT", "SavePlatformAsync", "INSERT INTO Platforms (...) VALUES (...)", 92, "Insert new platform")
    op("Platforms", "UPDATE", "SavePlatformAsync", "UPDATE Platforms SET ... WHERE Id = @Id", 83, "Update existing platform")
    op("Platforms", "DELETE", "DeletePlatformAsync", "DELETE FROM Platforms WHERE Id = @Id", 130, "Delete platform by ID")

    # --- ChatMessages ---
    op("ChatMessages", "SELECT", "SaveChatMessageAsync", "SELECT COUNT(1) FROM ChatMessages WHERE MessageId = @MessageId", 140, "Dedup check before insert")
    op("ChatMessages", "INSERT", "SaveChatMessageAsync", "INSERT INTO ChatMessages (...) VALUES (...)", 145, "Insert chat message")
    op("ChatMessages", "SELECT", "GetChatMessagesAsync", "SELECT * FROM ChatMessages WHERE Platform = @Platform AND Channel = @Channel ORDER BY Timestamp ASC LIMIT @Limit", 161, "Get chat history")
    op("ChatMessages", "DELETE", "DeleteChatMessagesAsync", "DELETE FROM ChatMessages WHERE Platform = @Platform AND Channel = @Channel", 168, "Delete channel messages")

    # --- ChatEventSettings ---
    op("ChatEventSettings", "SELECT", "GetChatEventSettingAsync", "SELECT * FROM ChatEventSettings WHERE Platform = @Platform AND EventType = @EventType", 196, "Get event setting")
    op("ChatEventSettings", "SELECT", "GetAllChatEventSettingsAsync", "SELECT * FROM ChatEventSettings ORDER BY Platform, EventType", 204, "Get all event settings")
    op("ChatEventSettings", "INSERT", "SaveChatEventSettingAsync", "INSERT INTO ChatEventSettings (...) VALUES (...)", 233, "Insert event setting")
    op("ChatEventSettings", "UPDATE", "SaveChatEventSettingAsync", "UPDATE ChatEventSettings SET ... WHERE Id = @Id", 222, "Update event setting")
    op("ChatEventSettings", "DELETE", "DeleteChatEventSettingAsync", "DELETE FROM ChatEventSettings WHERE Id = @Id", 248, "Delete event setting")

    # --- EventStatistics ---
    op("EventStatistics", "INSERT", "SaveEventStatisticAsync", "INSERT INTO EventStatistics (...) VALUES (...)", 257, "Insert event statistic")
    op("EventStatistics", "SELECT", "GetEventStatisticsAsync", "SELECT * FROM EventStatistics ... ORDER BY Timestamp DESC LIMIT @Limit", 266, "Dynamic query event stats")
    op("EventStatistics", "SELECT", "GetEventStatisticsByUsernameAsync", "SELECT * FROM EventStatistics WHERE Username = @Username ... ORDER BY Timestamp DESC LIMIT 100", 289, "Get stats by username")

    # --- CurrencySettings ---
    op("CurrencySettings", "SELECT", "GetCurrencySettingsAsync", "SELECT * FROM CurrencySettings LIMIT 1", 306, "Get currency settings singleton")
    op("CurrencySettings", "INSERT", "SaveCurrencySettingsAsync", "INSERT INTO CurrencySettings (...) VALUES (...)", 336, "Insert currency settings")
    op("CurrencySettings", "UPDATE", "SaveCurrencySettingsAsync", "UPDATE CurrencySettings SET ... WHERE Id = @Id", 316, "Update currency settings")

    # --- UserCurrency ---
    op("UserCurrency", "SELECT", "GetUserCurrencyAsync", "SELECT * FROM UserCurrency WHERE Username = @Username AND Platform = @Platform", 353, "Get user balance")
    op("UserCurrency", "SELECT", "GetUserCurrenciesAsync", "SELECT * FROM UserCurrency WHERE Username = @Username", 360, "Get all user balances")
    op("UserCurrency", "SELECT", "GetCurrencyLeaderboardAsync", "SELECT * FROM UserCurrency ORDER BY Balance DESC LIMIT @Limit", 367, "Get currency leaderboard")
    op("UserCurrency", "INSERT", "SaveUserCurrencyAsync", "INSERT INTO UserCurrency (...) VALUES (...)", 395, "Insert user currency")
    op("UserCurrency", "UPDATE", "SaveUserCurrencyAsync", "UPDATE UserCurrency SET ... WHERE Username = @Username AND Platform = @Platform", 385, "Update user currency")

    # --- CurrencyTransactions ---
    op("CurrencyTransactions", "INSERT", "SaveCurrencyTransactionAsync", "INSERT INTO CurrencyTransactions (...) VALUES (...)", 408, "Insert currency transaction")
    op("CurrencyTransactions", "SELECT", "GetCurrencyTransactionsAsync", "SELECT * FROM CurrencyTransactions ... ORDER BY Timestamp DESC LIMIT @Limit", 418, "Dynamic query transactions")

    # --- CurrencyRewards ---
    op("CurrencyRewards", "SELECT", "GetCurrencyRewardAsync", "SELECT * FROM CurrencyRewards WHERE Id = @Id", 442, "Get reward by ID")
    op("CurrencyRewards", "SELECT", "GetCurrencyRewardsAsync", "SELECT * FROM CurrencyRewards ... ORDER BY Cost ASC", 449, "Get all rewards")
    op("CurrencyRewards", "INSERT", "SaveCurrencyRewardAsync", "INSERT INTO CurrencyRewards (...) VALUES (...)", 476, "Insert reward")
    op("CurrencyRewards", "UPDATE", "SaveCurrencyRewardAsync", "UPDATE CurrencyRewards SET ... WHERE Id = @Id", 462, "Update reward")
    op("CurrencyRewards", "DELETE", "DeleteCurrencyRewardAsync", "DELETE FROM CurrencyRewards WHERE Id = @Id", 487, "Delete reward")

    # --- CurrencyRedemptions ---
    op("CurrencyRedemptions", "SELECT", "GetCurrencyRedemptionAsync", "SELECT * FROM CurrencyRedemptions WHERE Id = @Id", 493, "Get redemption by ID")
    op("CurrencyRedemptions", "SELECT", "GetCurrencyRedemptionsAsync", "SELECT * FROM CurrencyRedemptions ... ORDER BY RedeemedAt DESC", 500, "Dynamic query redemptions")
    op("CurrencyRedemptions", "INSERT", "SaveCurrencyRedemptionAsync", "INSERT INTO CurrencyRedemptions (...) VALUES (...)", 531, "Insert redemption")
    op("CurrencyRedemptions", "UPDATE", "SaveCurrencyRedemptionAsync", "UPDATE CurrencyRedemptions SET Status = @Status ... WHERE Id = @Id", 523, "Update redemption status")
    op("CurrencyRedemptions", "DELETE", "DeleteCurrencyRedemptionAsync", "DELETE FROM CurrencyRedemptions WHERE Id = @Id", 543, "Delete redemption")

    # --- Commands ---
    op("Commands", "SELECT", "GetCommandAsync", "SELECT * FROM Commands WHERE Id = @Id", 549, "Get command by ID")
    op("Commands", "SELECT", "GetCommandByNameAsync", "SELECT * FROM Commands WHERE Name = @Name COLLATE NOCASE", 555, "Get command by name (case-insensitive)")
    op("Commands", "SELECT", "GetCommandsAsync", "SELECT * FROM Commands WHERE 1=1 ... ORDER BY Name", 561, "Get commands with dynamic filters")
    op("Commands", "INSERT", "SaveCommandAsync", "INSERT INTO Commands (...) VALUES (...)", 581, "Insert new command")
    op("Commands", "UPDATE", "SaveCommandAsync", "UPDATE Commands SET ... WHERE Id = @Id", 592, "Update existing command")
    op("Commands", "DELETE", "DeleteCommandAsync", "DELETE FROM Commands WHERE Id = @Id AND IsBuiltIn = 0", 604, "Delete command (non-built-in only)")
    op("Commands", "UPDATE", "IncrementCommandUsageAsync", "UPDATE Commands SET UsageCount = UsageCount + 1, LastUsedAt = @Now WHERE Id = @Id", 610, "Increment usage counter")

    # --- CommandCooldowns ---
    op("CommandCooldowns", "SELECT", "GetCommandCooldownAsync", "SELECT * FROM CommandCooldowns WHERE CommandId = @CommandId AND Platform = @Platform AND ExpiresAt > @Now ...", 618, "Get active cooldown")
    op("CommandCooldowns", "INSERT", "SaveCommandCooldownAsync", "INSERT INTO CommandCooldowns (...) VALUES (...)", 633, "Insert cooldown entry")
    op("CommandCooldowns", "DELETE", "DeleteExpiredCooldownsAsync", "DELETE FROM CommandCooldowns WHERE ExpiresAt <= @Now", 643, "Clean up expired cooldowns")

    # --- AutoReplyRules ---
    op("AutoReplyRules", "SELECT", "GetAutoReplyRulesAsync", "SELECT * FROM AutoReplyRules ORDER BY Priority ASC, Id ASC", 651, "Get all auto-reply rules")
    op("AutoReplyRules", "SELECT", "GetAutoReplyRulesByPlatformAndEventAsync", "SELECT * FROM AutoReplyRules WHERE IsEnabled = 1 AND ... ORDER BY Priority ASC", 657, "Get matching rules by platform/event")
    op("AutoReplyRules", "SELECT", "GetAutoReplyRuleAsync", "SELECT * FROM AutoReplyRules WHERE Id = @Id", 666, "Get rule by ID")
    op("AutoReplyRules", "INSERT", "SaveAutoReplyRuleAsync", "INSERT INTO AutoReplyRules (...) VALUES (...)", 676, "Insert new rule")
    op("AutoReplyRules", "UPDATE", "SaveAutoReplyRuleAsync", "UPDATE AutoReplyRules SET ... WHERE Id = @Id", 694, "Update existing rule")
    op("AutoReplyRules", "DELETE", "DeleteAutoReplyRuleAsync", "DELETE FROM AutoReplyRules WHERE Id = @Id", 710, "Delete rule")
    op("AutoReplyRules", "UPDATE", "IncrementRuleTriggerCountAsync", "UPDATE AutoReplyRules SET TriggerCount = TriggerCount + 1, LastTriggeredAt = @Now WHERE Id = @Id", 716, "Increment trigger counter")

    # --- Giveaways ---
    op("Giveaways", "SELECT", "GetGiveawaysAsync", "SELECT * FROM Giveaways ORDER BY CreatedAt DESC", 726, "Get all giveaways")
    op("Giveaways", "SELECT", "GetGiveawayAsync", "SELECT * FROM Giveaways WHERE Id = @Id", 732, "Get giveaway by ID")
    op("Giveaways", "SELECT", "GetActiveGiveawayAsync", "SELECT * FROM Giveaways WHERE Status = @Status ORDER BY StartedAt DESC LIMIT 1", 738, "Get active giveaway")
    op("Giveaways", "INSERT", "SaveGiveawayAsync", "INSERT OR REPLACE INTO Giveaways (...) VALUES (...)", 746, "Upsert giveaway")
    op("Giveaways", "DELETE", "DeleteGiveawayAsync", "DELETE FROM Giveaways WHERE Id = @Id", 776, "Delete giveaway")

    # --- GiveawayEntries ---
    op("GiveawayEntries", "SELECT", "GetGiveawayEntriesAsync", "SELECT * FROM GiveawayEntries WHERE GiveawayId = @GiveawayId ORDER BY EnteredAt ASC", 783, "Get all entries for giveaway")
    op("GiveawayEntries", "SELECT", "GetGiveawayEntryAsync", "SELECT * FROM GiveawayEntries WHERE GiveawayId = @GiveawayId AND Username = @Username AND Platform = @Platform", 790, "Get specific entry")
    op("GiveawayEntries", "INSERT", "SaveGiveawayEntryAsync", "INSERT OR REPLACE INTO GiveawayEntries (...) VALUES (...)", 799, "Upsert giveaway entry")
    op("GiveawayEntries", "SELECT", "GetGiveawayEntryCountAsync", "SELECT COALESCE(SUM(EntryCount), 0) FROM GiveawayEntries WHERE GiveawayId = @GiveawayId", 816, "Get total entry count")
    op("GiveawayEntries", "SELECT", "GetGiveawayUniqueParticipantsAsync", "SELECT COUNT(DISTINCT Username || Platform) FROM GiveawayEntries WHERE GiveawayId = @GiveawayId", 823, "Get unique participants")
    op("GiveawayEntries", "DELETE", "DeleteGiveawayAsync", "DELETE FROM GiveawayEntries WHERE GiveawayId = @Id", 773, "Cascade delete entries for giveaway")

    # --- Polls ---
    op("Polls", "SELECT", "GetPollsAsync", "SELECT * FROM Polls ORDER BY CreatedAt DESC", 830, "Get all polls")
    op("Polls", "SELECT", "GetPollAsync", "SELECT * FROM Polls WHERE Id = @Id", 836, "Get poll by ID")
    op("Polls", "SELECT", "GetActivePollAsync", "SELECT * FROM Polls WHERE Status = @Status ORDER BY StartedAt DESC LIMIT 1", 842, "Get active poll")
    op("Polls", "INSERT", "SavePollAsync", "INSERT OR REPLACE INTO Polls (...) VALUES (...)", 850, "Upsert poll")
    op("Polls", "DELETE", "DeletePollAsync", "DELETE FROM Polls WHERE Id = @Id", 880, "Delete poll")

    # --- PollVotes ---
    op("PollVotes", "SELECT", "GetPollVotesAsync", "SELECT * FROM PollVotes WHERE PollId = @PollId ORDER BY VotedAt ASC", 887, "Get all votes for poll")
    op("PollVotes", "SELECT", "GetPollVoteAsync", "SELECT * FROM PollVotes WHERE PollId = @PollId AND Username = @Username AND Platform = @Platform", 893, "Get specific vote")
    op("PollVotes", "INSERT", "SavePollVoteAsync", "INSERT OR REPLACE INTO PollVotes (...) VALUES (...)", 902, "Upsert poll vote")
    op("PollVotes", "SELECT", "GetPollVoteCountAsync", "SELECT COUNT(*) FROM PollVotes WHERE PollId = @PollId", 920, "Get total vote count")
    op("PollVotes", "SELECT", "GetPollVoteResultsAsync", "SELECT OptionIndex, COUNT(*) as Count FROM PollVotes WHERE PollId = @PollId GROUP BY OptionIndex", 927, "Get aggregated results")
    op("PollVotes", "DELETE", "DeletePollAsync", "DELETE FROM PollVotes WHERE PollId = @Id", 877, "Cascade delete votes for poll")

    # --- UserWatchtime ---
    op("UserWatchtime", "SELECT", "GetUserWatchtimeAsync", "SELECT * FROM UserWatchtime WHERE Username = @Username AND Platform = @Platform", 935, "Get user watchtime")
    op("UserWatchtime", "SELECT", "GetAllUserWatchtimeAsync", "SELECT * FROM UserWatchtime ORDER BY TotalMinutes DESC", 942, "Get all watchtime")
    op("UserWatchtime", "SELECT", "GetTopWatchtimeUsersAsync", "SELECT * FROM UserWatchtime ORDER BY TotalMinutes DESC LIMIT @Limit", 948, "Get watchtime leaderboard")
    op("UserWatchtime", "SELECT", "GetActiveViewersAsync", "SELECT * FROM UserWatchtime WHERE IsActive = 1 ORDER BY LastSeenAt DESC", 955, "Get active viewers")
    op("UserWatchtime", "INSERT", "SaveUserWatchtimeAsync", "INSERT OR REPLACE INTO UserWatchtime (...) VALUES (...)", 963, "Upsert watchtime record")
    op("UserWatchtime", "SELECT", "GetTotalActiveViewersCountAsync", "SELECT COUNT(*) FROM UserWatchtime WHERE IsActive = 1", 979, "Count active viewers")
    op("UserWatchtime", "UPDATE", "ResetStreamSessionMinutesAsync", "UPDATE UserWatchtime SET StreamSessionMinutes = 0, UpdatedAt = @Now", 985, "Reset session minutes for all")
    op("UserWatchtime", "UPDATE", "DeactivateInactiveViewersAsync", "UPDATE UserWatchtime SET IsActive = 0, UpdatedAt = @Now WHERE LastSeenAt < @InactiveSince", 991, "Deactivate stale viewers")

    # --- Variables ---
    op("Variables", "SELECT", "GetVariableAsync", "SELECT * FROM Variables WHERE Id = @Id", 1002, "Get variable by ID")
    op("Variables", "SELECT", "GetVariableByNameAsync", "SELECT * FROM Variables WHERE Name = @Name COLLATE NOCASE", 1008, "Get variable by name")
    op("Variables", "SELECT", "GetVariablesAsync", "SELECT * FROM Variables ORDER BY Category, Name", 1014, "Get all variables")
    op("Variables", "SELECT", "GetVariablesByCategoryAsync", "SELECT * FROM Variables WHERE Category = @Category ORDER BY Name", 1020, "Get variables by category")
    op("Variables", "SELECT", "GetVariableCategoriesAsync", "SELECT DISTINCT Category FROM Variables ORDER BY Category", 1026, "Get distinct categories")
    op("Variables", "INSERT", "SaveVariableAsync", "INSERT INTO Variables (...) VALUES (...)", 1038, "Insert new variable")
    op("Variables", "UPDATE", "SaveVariableAsync", "UPDATE Variables SET ... WHERE Id = @Id", 1052, "Update existing variable")
    op("Variables", "DELETE", "DeleteVariableAsync", "DELETE FROM Variables WHERE Id = @Id AND IsSystem = 0 AND IsEditable = 1", 1067, "Delete custom variable only")
    op("Variables", "UPDATE", "UpdateVariableValueAsync", "UPDATE Variables SET Value = @Value, UpdatedAt = @Now, LastSyncedAt = @Now WHERE Name = @Name COLLATE NOCASE", 1074, "Update variable value by name")

    # --- Configs ---
    op("Configs", "SELECT", "GetConfigValueAsync", "SELECT Value FROM Configs WHERE Key = @Key", 1084, "Get config value by key")
    op("Configs", "INSERT", "SetConfigValueAsync", "INSERT INTO Configs (Key, Value, Category, IsEncrypted, UpdatedAt) VALUES (...)", 1098, "Insert new config")
    op("Configs", "UPDATE", "SetConfigValueAsync", "UPDATE Configs SET Value = @Value, Category = @Category, UpdatedAt = @UpdatedAt WHERE Key = @Key", 1092, "Update existing config")
    op("Configs", "SELECT", "GetConfigsByCategoryAsync", "SELECT * FROM Configs WHERE Category = @Category", 1105, "Get configs by category")

    # --- Triggers ---
    op("Triggers", "SELECT", "GetTriggersAsync", "SELECT * FROM Triggers [WHERE Enabled = 1]", 1115, "Get triggers with optional filter")
    op("Triggers", "SELECT", "GetTriggerByIdAsync", "SELECT * FROM Triggers WHERE Id = @Id", 1122, "Get trigger by ID")
    op("Triggers", "INSERT", "SaveTriggerAsync", "INSERT INTO Triggers (...) VALUES (...)", 1145, "Insert new trigger")
    op("Triggers", "UPDATE", "SaveTriggerAsync", "UPDATE Triggers SET ... WHERE Id = @Id", 1133, "Update existing trigger")
    op("Triggers", "DELETE", "DeleteTriggerAsync", "DELETE FROM Triggers WHERE Id = @Id", 1157, "Delete trigger")

    # --- Actions ---
    op("Actions", "SELECT", "GetActionsAsync", "SELECT * FROM Actions", 1163, "Get all actions")
    op("Actions", "SELECT", "GetActionByIdAsync", "SELECT * FROM Actions WHERE Id = @Id", 1169, "Get action by ID")
    op("Actions", "INSERT", "SaveActionAsync", "INSERT INTO Actions (...) VALUES (...)", 1188, "Insert new action")
    op("Actions", "UPDATE", "SaveActionAsync", "UPDATE Actions SET ... WHERE Id = @Id", 1180, "Update existing action")
    op("Actions", "DELETE", "DeleteActionAsync", "DELETE FROM Actions WHERE Id = @Id", 1198, "Delete action")

    # --- EventLogs ---
    op("EventLogs", "SELECT", "GetEventLogsAsync", "SELECT * FROM EventLogs ORDER BY Timestamp DESC LIMIT @Limit", 1206, "Get recent event logs")
    op("EventLogs", "INSERT", "LogEventAsync", "INSERT INTO EventLogs (...) VALUES (...)", 1213, "Insert event log")

    # --- WebServerSettings ---
    op("WebServerSettings", "SELECT", "GetWebServerSettingsAsync", "SELECT * FROM WebServerSettings WHERE Id = 1", 1223, "Get web server settings singleton")
    op("WebServerSettings", "INSERT", "SaveWebServerSettingsAsync", "INSERT INTO WebServerSettings (...) VALUES (...)", 1255, "Insert web server settings")
    op("WebServerSettings", "UPDATE", "SaveWebServerSettingsAsync", "UPDATE WebServerSettings SET ... WHERE Id = 1", 1243, "Update web server settings")

    # --- WebhookSubscriptions ---
    op("WebhookSubscriptions", "SELECT", "GetWebhookSubscriptionsAsync", "SELECT * FROM WebhookSubscriptions ... ORDER BY CreatedAt DESC", 1268, "Get webhook subscriptions")
    op("WebhookSubscriptions", "INSERT", "SaveWebhookSubscriptionAsync", "INSERT INTO WebhookSubscriptions (...) VALUES (...)", 1292, "Insert webhook subscription")
    op("WebhookSubscriptions", "UPDATE", "SaveWebhookSubscriptionAsync", "UPDATE WebhookSubscriptions SET ... WHERE Id = @Id", 1282, "Update webhook subscription")
    op("WebhookSubscriptions", "DELETE", "DeleteWebhookSubscriptionAsync", "DELETE FROM WebhookSubscriptions WHERE Id = @Id", 1303, "Delete webhook subscription")
    op("WebhookSubscriptions", "DELETE", "DeleteWebhookSubscriptionsByPlatformAsync", "DELETE FROM WebhookSubscriptions WHERE Platform = @Platform", 1309, "Delete all platform subscriptions")

    print(f"  Inserted {count} data operations")


# ============================================================
# 4. REBUILD FTS INDEX
# ============================================================
def rebuild_and_validate():
    print("\n[4/4] Rebuilding FTS index and validating...")
    rebuild_fts_index()

    conn = get_conn()
    counts = {}
    for table in ["features", "files", "code_entities", "db_tables", "db_columns",
                   "data_operations", "events", "event_flows", "di_registrations",
                   "architecture_rules", "navigation_map", "search_index", "project_metadata"]:
        counts[table] = conn.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
    conn.close()

    print("\n" + "=" * 50)
    print("FINAL DATABASE STATS")
    print("=" * 50)
    for table, c in counts.items():
        status = "✓" if c > 0 else "✗ EMPTY"
        print(f"  {table:25s} {c:5d}  {status}")
    print("=" * 50)


if __name__ == "__main__":
    print("=" * 60)
    print("VyralBot Context Database — Gap Filler")
    print("=" * 60)
    populate_db_columns()
    populate_code_entities()
    populate_data_operations()
    rebuild_and_validate()
    print("\nDone!")
