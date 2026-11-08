using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data.Entities;
using VyralBot.UI.ViewModels;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Wave-13 coverage for the wave-12 ChatHub parity work: the per-platform
/// chat-settings section view models round-trip their NEW sections and config
/// keys through in-memory fakes — load defaults → set → SaveSettingsCommand →
/// reload a fresh VM → values hold.
///
/// Covered sections:
///  - Twitch: channel-points (RewardRedeemed) row + EventSound_/EventVolume_
///    config mirroring, display prefs + auto-replies master toggle
///    (Chat.Twitch.*), platform flags via the toggle persist path
///  - YouTube: membership (row YouTube/Subscribe) + Super Chat (row
///    YouTube/Cheer) with ShowInChat forced true, display prefs (Chat.YouTube.*)
///  - Discord: member-join (row Discord/Follow) + server-boost (row
///    Discord/Subscribe), display prefs (Chat.Discord.*)
///  - Kick: display prefs (Kick.* — deliberately NOT Chat.Kick.*, they are
///    shared with KickSettingsViewModel's quick-settings toggles)
///
/// All four VMs construct directly with fakes (no UI scaffolding needed).
/// The SoundService/ChatEventSettingsService production classes are replaced
/// by the in-memory doubles below; the data layer is the existing
/// IntegrationWave8DataService (real config-table + platform-row behavior).
///
/// NOTE: Twitch and Kick SaveSettingsAsync end with a 2s Status reset delay,
/// so their save-driven tests each cost ~2s wall clock.
/// </summary>
public class Wave13ChatHubSectionSettingsTests
{
    private sealed class Wave13ChatEventSettingsService : IChatEventSettingsService
    {
        public List<ChatEventSetting> Rows { get; } = new();

        private ChatEventSetting? Find(string platform, string eventType)
            => Rows.FirstOrDefault(r =>
                string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.EventType, eventType, StringComparison.OrdinalIgnoreCase));

        public Task<ChatEventSetting?> GetSettingAsync(string platform, string eventType)
            => Task.FromResult(Find(platform, eventType));

        public Task<IEnumerable<ChatEventSetting>> GetAllSettingsAsync(string? platform = null)
            => Task.FromResult<IEnumerable<ChatEventSetting>>(
                platform == null
                    ? Rows.ToList()
                    : Rows.Where(r => string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task SaveSettingAsync(ChatEventSetting setting)
        {
            // Upsert keyed by (platform, event type), mirroring the service's
            // row identity; the caller's mutated instance becomes the row.
            var existing = Find(setting.Platform, setting.EventType);
            if (existing != null)
            {
                Rows.Remove(existing);
            }

            Rows.Add(setting);
            return Task.CompletedTask;
        }

        public Task DeleteSettingAsync(Guid id)
        {
            Rows.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }

        public Task<bool> IsEventEnabledAsync(string platform, string eventType)
            => Task.FromResult(Find(platform, eventType)?.ShowInChat ?? true);

        public Task<bool> IsAutoReplyEnabledAsync(string platform, string eventType)
            => Task.FromResult(Find(platform, eventType)?.AutoReplyEnabled ?? false);

        public Task<string?> GetAutoReplyMessageAsync(string platform, string eventType)
            => Task.FromResult(Find(platform, eventType)?.AutoReplyMessage);

        public Task<(bool Enabled, string? SoundFile, int Volume)> GetSoundSettingsAsync(string platform, string eventType)
        {
            var row = Find(platform, eventType);
            return Task.FromResult((row?.SoundEnabled ?? true, row?.SoundFile, row?.SoundVolume ?? 100));
        }

        public void InvalidateCache()
        {
        }
    }

    private static ChatEventSetting SeedRow(
        Wave13ChatEventSettingsService settings, string platform, string eventType,
        bool showInChat = true, bool autoReply = false, string? message = null,
        string? soundFile = null, bool soundEnabled = true, int soundVolume = 100)
    {
        var row = new ChatEventSetting
        {
            Platform = platform,
            EventType = eventType,
            ShowInChat = showInChat,
            AutoReplyEnabled = autoReply,
            AutoReplyMessage = message,
            SoundFile = soundFile,
            SoundEnabled = soundEnabled,
            SoundVolume = soundVolume
        };
        settings.Rows.Add(row);
        return row;
    }

    // ==================================================================
    // Twitch — channel points (RewardRedeemed) + display prefs + flags
    // ==================================================================

    [Fact]
    public async Task Twitch_ChannelPointsSection_RoundTripsThroughSave()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();
        SeedRow(settings, "Twitch", "RewardRedeemed",
            showInChat: false, autoReply: false, message: "old text");

        var vm = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm.InitializeAsync();

        // Loaded from the seeded row
        Assert.False(vm.ShowRewardsInChat);
        Assert.False(vm.RewardAutoReplyEnabled);
        Assert.Equal("old text", vm.RewardAutoReplyMessage);

        vm.ShowRewardsInChat = true;
        vm.RewardAutoReplyEnabled = true;
        vm.RewardAutoReplyMessage = "{username} redeemed {reward}!";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        var saved = await settings.GetSettingAsync("Twitch", "RewardRedeemed");
        Assert.NotNull(saved);
        Assert.True(saved.ShowInChat);
        Assert.True(saved.AutoReplyEnabled);
        Assert.Equal("{username} redeemed {reward}!", saved.AutoReplyMessage);

        // A fresh VM on the same fakes reloads the saved values
        var vm2 = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm2.InitializeAsync();
        Assert.True(vm2.ShowRewardsInChat);
        Assert.True(vm2.RewardAutoReplyEnabled);
        Assert.Equal("{username} redeemed {reward}!", vm2.RewardAutoReplyMessage);
    }

    [Fact]
    public async Task Twitch_ChannelPointsSound_MirrorsToSoundServiceConfigKeys()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();
        SeedRow(settings, "Twitch", "RewardRedeemed",
            soundFile: "reward.wav", soundEnabled: true, soundVolume: 75);

        var vm = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm.InitializeAsync();
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // Enabled: file name + invariant 0..1 volume (75 → 0.75)
        Assert.Equal("reward.wav", data.ConfigValues["EventSound_Twitch_redemption"]);
        Assert.Equal("0.75", data.ConfigValues["EventVolume_Twitch_redemption"]);
    }

    [Fact]
    public async Task Twitch_ChannelPointsSoundDisabled_PersistsEmptySoundKey()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();
        SeedRow(settings, "Twitch", "RewardRedeemed",
            soundFile: "reward.wav", soundEnabled: false, soundVolume: 100);

        var vm = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm.InitializeAsync();
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // Disabled: empty string (SoundService's "off" marker); volume still written
        Assert.Equal(string.Empty, data.ConfigValues["EventSound_Twitch_redemption"]);
        Assert.Equal("1.00", data.ConfigValues["EventVolume_Twitch_redemption"]);
    }

    [Fact]
    public async Task Twitch_DisplayPrefsAndMasterToggle_RoundTrip()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();

        var vm = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm.InitializeAsync();

        // Defaults with nothing stored: replies on, badges on, timestamps/compact off
        Assert.True(vm.AutoRepliesEnabled);
        Assert.True(vm.ShowBadges);
        Assert.False(vm.ShowTimestamps);
        Assert.False(vm.CompactMode);

        vm.AutoRepliesEnabled = false;
        vm.ShowBadges = false;
        vm.ShowTimestamps = true;
        vm.CompactMode = true;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("False", data.ConfigValues["Chat.Twitch.AutoRepliesEnabled"]);
        Assert.Equal("False", data.ConfigValues["Chat.Twitch.ShowBadges"]);
        Assert.Equal("True", data.ConfigValues["Chat.Twitch.ShowTimestamps"]);
        Assert.Equal("True", data.ConfigValues["Chat.Twitch.CompactMode"]);

        var vm2 = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm2.InitializeAsync();
        Assert.False(vm2.AutoRepliesEnabled);
        Assert.False(vm2.ShowBadges);
        Assert.True(vm2.ShowTimestamps);
        Assert.True(vm2.CompactMode);
    }

    [Fact]
    public async Task Twitch_PlatformFlags_PersistViaToggles_AndRoutingEventPublished()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();
        var platform = new Platform { Name = "Twitch", TwitchChatEnabled = true, TwitchChatAutoStart = false };
        data.PlatformRowsAtRest[platform.Id] = platform;

        var vm = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm.InitializeAsync();

        Assert.True(vm.IsChatEnabled);
        Assert.False(vm.AutoStartChat);

        vm.IsChatEnabled = false;  // toggle persist path (all fakes complete synchronously)
        vm.AutoStartChat = true;

        var stored = data.PlatformRowsAtRest.Values.Single(p => p.Name == "Twitch");
        Assert.False(stored.TwitchChatEnabled);
        Assert.True(stored.TwitchChatAutoStart);

        var routing = bus.Published.OfType<PlatformChatRoutingChangedEvent>().Single();
        Assert.Equal("Twitch", routing.Platform);
        Assert.False(routing.Enabled);

        var vm2 = new TwitchChatSettingsViewModel(
            NullLogger<TwitchChatSettingsViewModel>.Instance, settings, data, bus);
        await vm2.InitializeAsync();
        Assert.False(vm2.IsChatEnabled);
        Assert.True(vm2.AutoStartChat);
    }

    // ==================================================================
    // YouTube — membership + Super Chat sections, display prefs
    // ==================================================================

    [Fact]
    public async Task YouTube_MembershipAndSuperChatSections_RoundTrip()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();

        var vm = new YoutubeChatSettingsViewModel(
            NullLogger<YoutubeChatSettingsViewModel>.Instance, data, bus, settings);
        await vm.InitializeAsync();

        // Defaults with no stored rows
        Assert.False(vm.MembershipAutoReplyEnabled);
        Assert.False(vm.SuperChatAutoReplyEnabled);
        Assert.Equal("Thanks for the membership {username}! 🎉", vm.MembershipAutoReplyMessage);

        vm.MembershipAutoReplyEnabled = true;
        vm.MembershipAutoReplyMessage = "Welcome to the club, {username}!";
        vm.SuperChatAutoReplyEnabled = true;
        vm.SuperChatAutoReplyMessage = "{amount} — thank you {username}!";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // Membership maps onto the SHARED "Subscribe" row vocabulary
        var membership = await settings.GetSettingAsync("YouTube", "Subscribe");
        Assert.NotNull(membership);
        Assert.True(membership.AutoReplyEnabled);
        Assert.Equal("Welcome to the club, {username}!", membership.AutoReplyMessage);
        Assert.True(membership.ShowInChat); // forced true: the synthetic chat line always renders

        // Super Chat maps onto the shared "Cheer" row vocabulary
        var superChat = await settings.GetSettingAsync("YouTube", "Cheer");
        Assert.NotNull(superChat);
        Assert.True(superChat.AutoReplyEnabled);
        Assert.Equal("{amount} — thank you {username}!", superChat.AutoReplyMessage);
        Assert.True(superChat.ShowInChat);

        var vm2 = new YoutubeChatSettingsViewModel(
            NullLogger<YoutubeChatSettingsViewModel>.Instance, data, bus, settings);
        await vm2.InitializeAsync();
        Assert.True(vm2.MembershipAutoReplyEnabled);
        Assert.Equal("Welcome to the club, {username}!", vm2.MembershipAutoReplyMessage);
        Assert.True(vm2.SuperChatAutoReplyEnabled);
        Assert.Equal("{amount} — thank you {username}!", vm2.SuperChatAutoReplyMessage);
    }

    [Fact]
    public async Task YouTube_DisplayPrefs_RoundTrip()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();

        var vm = new YoutubeChatSettingsViewModel(
            NullLogger<YoutubeChatSettingsViewModel>.Instance, data, bus, settings);
        await vm.InitializeAsync();

        vm.AutoRepliesEnabled = false;
        vm.ShowBadges = false;
        vm.ShowTimestamps = true;
        vm.CompactMode = true;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("False", data.ConfigValues["Chat.YouTube.AutoRepliesEnabled"]);
        Assert.Equal("False", data.ConfigValues["Chat.YouTube.ShowBadges"]);
        Assert.Equal("True", data.ConfigValues["Chat.YouTube.ShowTimestamps"]);
        Assert.Equal("True", data.ConfigValues["Chat.YouTube.CompactMode"]);

        var vm2 = new YoutubeChatSettingsViewModel(
            NullLogger<YoutubeChatSettingsViewModel>.Instance, data, bus, settings);
        await vm2.InitializeAsync();
        Assert.False(vm2.AutoRepliesEnabled);
        Assert.False(vm2.ShowBadges);
        Assert.True(vm2.ShowTimestamps);
        Assert.True(vm2.CompactMode);
    }

    // ==================================================================
    // Discord — member join + server boost sections, display prefs
    // ==================================================================

    [Fact]
    public async Task Discord_JoinAndBoostSections_RoundTrip()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();
        SeedRow(settings, "Discord", "Follow", showInChat: true, autoReply: false, message: "old join");
        SeedRow(settings, "Discord", "Subscribe", showInChat: true, autoReply: false, message: "old boost");

        var vm = new DiscordChatSettingsViewModel(
            NullLogger<DiscordChatSettingsViewModel>.Instance, data, bus, settings);
        await vm.InitializeAsync();

        // Loaded from the seeded rows
        Assert.True(vm.ShowMemberJoinsInChat);
        Assert.Equal("old join", vm.MemberJoinAutoReplyMessage);
        Assert.Equal("old boost", vm.BoostAutoReplyMessage);

        vm.ShowMemberJoinsInChat = false;
        vm.MemberJoinAutoReplyEnabled = true;
        vm.MemberJoinAutoReplyMessage = "Welcome to {guild}, {username}!";
        vm.ShowBoostsInChat = false;
        vm.BoostAutoReplyEnabled = true;
        vm.BoostAutoReplyMessage = "Thanks for boosting {guild}!";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // Join → shared "Follow" row; boost → shared "Subscribe" row
        var join = await settings.GetSettingAsync("Discord", "Follow");
        Assert.NotNull(join);
        Assert.False(join.ShowInChat); // Discord has real show-in-chat toggles
        Assert.True(join.AutoReplyEnabled);
        Assert.Equal("Welcome to {guild}, {username}!", join.AutoReplyMessage);

        var boost = await settings.GetSettingAsync("Discord", "Subscribe");
        Assert.NotNull(boost);
        Assert.False(boost.ShowInChat);
        Assert.True(boost.AutoReplyEnabled);
        Assert.Equal("Thanks for boosting {guild}!", boost.AutoReplyMessage);

        var vm2 = new DiscordChatSettingsViewModel(
            NullLogger<DiscordChatSettingsViewModel>.Instance, data, bus, settings);
        await vm2.InitializeAsync();
        Assert.False(vm2.ShowMemberJoinsInChat);
        Assert.True(vm2.MemberJoinAutoReplyEnabled);
        Assert.Equal("Welcome to {guild}, {username}!", vm2.MemberJoinAutoReplyMessage);
        Assert.False(vm2.ShowBoostsInChat);
        Assert.True(vm2.BoostAutoReplyEnabled);
        Assert.Equal("Thanks for boosting {guild}!", vm2.BoostAutoReplyMessage);
    }

    [Fact]
    public async Task Discord_DisplayPrefs_RoundTrip()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();
        var bus = new RecordingEventBus();

        var vm = new DiscordChatSettingsViewModel(
            NullLogger<DiscordChatSettingsViewModel>.Instance, data, bus, settings);
        await vm.InitializeAsync();

        vm.AutoRepliesEnabled = false;
        vm.ShowBadges = false;
        vm.ShowTimestamps = true;
        vm.CompactMode = true;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("False", data.ConfigValues["Chat.Discord.AutoRepliesEnabled"]);
        Assert.Equal("False", data.ConfigValues["Chat.Discord.ShowBadges"]);
        Assert.Equal("True", data.ConfigValues["Chat.Discord.ShowTimestamps"]);
        Assert.Equal("True", data.ConfigValues["Chat.Discord.CompactMode"]);

        var vm2 = new DiscordChatSettingsViewModel(
            NullLogger<DiscordChatSettingsViewModel>.Instance, data, bus, settings);
        await vm2.InitializeAsync();
        Assert.False(vm2.AutoRepliesEnabled);
        Assert.False(vm2.ShowBadges);
        Assert.True(vm2.ShowTimestamps);
        Assert.True(vm2.CompactMode);
    }

    // ==================================================================
    // Kick — display prefs use the shared "Kick.*" keys (NOT Chat.Kick.*)
    // ==================================================================

    [Fact]
    public async Task Kick_DisplayPrefs_RoundTrip_UsesSharedKickKeys()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();

        var vm = new KickChatSettingsViewModel(
            NullLogger<KickChatSettingsViewModel>.Instance, settings, data);
        await vm.InitializeAsync();

        Assert.True(vm.AutoRepliesEnabled);
        Assert.True(vm.ShowBadges);
        Assert.False(vm.ShowTimestamps);
        Assert.False(vm.CompactMode);

        vm.AutoRepliesEnabled = false;
        vm.ShowBadges = false;
        vm.ShowTimestamps = true;
        vm.CompactMode = true;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // Kick deliberately shares "Kick.*" with KickSettingsViewModel's
        // quick-settings card so both UIs stay in sync — NOT "Chat.Kick.*".
        Assert.Equal("False", data.ConfigValues["Kick.AutoRepliesEnabled"]);
        Assert.Equal("False", data.ConfigValues["Kick.ShowBadges"]);
        Assert.Equal("True", data.ConfigValues["Kick.ShowTimestamps"]);
        Assert.Equal("True", data.ConfigValues["Kick.CompactMode"]);
        Assert.False(data.ConfigValues.ContainsKey("Chat.Kick.ShowBadges"));

        var vm2 = new KickChatSettingsViewModel(
            NullLogger<KickChatSettingsViewModel>.Instance, settings, data);
        await vm2.InitializeAsync();
        Assert.False(vm2.AutoRepliesEnabled);
        Assert.False(vm2.ShowBadges);
        Assert.True(vm2.ShowTimestamps);
        Assert.True(vm2.CompactMode);
    }

    [Fact]
    public async Task Kick_ChannelPointsSection_RoundTripsThroughSave()
    {
        var settings = new Wave13ChatEventSettingsService();
        var data = new IntegrationWave8DataService();

        var vm = new KickChatSettingsViewModel(
            NullLogger<KickChatSettingsViewModel>.Instance, settings, data);
        await vm.InitializeAsync();

        vm.ShowRewardsInChat = false;
        vm.RewardAutoReplyEnabled = true;
        vm.RewardAutoReplyMessage = "{username} grabbed {reward}!";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        var saved = await settings.GetSettingAsync("Kick", "RewardRedeemed");
        Assert.NotNull(saved);
        Assert.False(saved.ShowInChat);
        Assert.True(saved.AutoReplyEnabled);
        Assert.Equal("{username} grabbed {reward}!", saved.AutoReplyMessage);

        var vm2 = new KickChatSettingsViewModel(
            NullLogger<KickChatSettingsViewModel>.Instance, settings, data);
        await vm2.InitializeAsync();
        Assert.False(vm2.ShowRewardsInChat);
        Assert.True(vm2.RewardAutoReplyEnabled);
        Assert.Equal("{username} grabbed {reward}!", vm2.RewardAutoReplyMessage);
    }
}
