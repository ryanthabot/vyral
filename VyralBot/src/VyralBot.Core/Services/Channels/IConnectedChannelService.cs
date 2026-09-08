namespace VyralBot.Core.Services.Channels;

/// <summary>Registry of Discord channels a bot monitors, with purpose tags.</summary>
public interface IConnectedChannelService
{
    Task<List<VyralBot.Data.Entities.ConnectedChannel>> GetChannelsAsync(string botId);
    Task<VyralBot.Data.Entities.ConnectedChannel> UpsertAsync(VyralBot.Data.Entities.ConnectedChannel channel);
    Task DeleteAsync(string botId, string channelId);
    Task<bool> IsMonitoredAsync(string botId, string channelId);
    /// <summary>
    /// True when the channel may run tier-gated bot commands: it must exist, be monitored,
    /// and be tagged Moderation or Admin. LEGACY RULE: also true whenever the bot has ZERO
    /// configured channels, so existing behaviour (commands in any channel) is unchanged
    /// until the user configures the Connected Channels tab.
    /// </summary>
    Task<bool> AcceptsCommandsAsync(string botId, string channelId);
    Task<IReadOnlyList<VyralBot.Data.Entities.ConnectedChannel>> GetByPurposeAsync(string botId, string purpose);
}
