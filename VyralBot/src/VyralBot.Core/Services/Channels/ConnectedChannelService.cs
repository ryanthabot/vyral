using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Channels;

public sealed class ConnectedChannelService : IConnectedChannelService
{
    private readonly IDataService _data;

    public ConnectedChannelService(IDataService data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public Task<List<ConnectedChannel>> GetChannelsAsync(string botId) => _data.GetConnectedChannelsAsync(botId);

    public async Task<ConnectedChannel> UpsertAsync(ConnectedChannel channel) =>
        await _data.UpsertConnectedChannelAsync(channel).ConfigureAwait(false);

    public Task DeleteAsync(string botId, string channelId) => _data.DeleteConnectedChannelAsync(botId, channelId);

    public async Task<bool> IsMonitoredAsync(string botId, string channelId) =>
        (await _data.GetConnectedChannelsAsync(botId).ConfigureAwait(false)).Any(c => c.ChannelId == channelId && c.MonitoringEnabled);

    public async Task<bool> AcceptsCommandsAsync(string botId, string channelId)
    {
        var rows = await _data.GetConnectedChannelsAsync(botId).ConfigureAwait(false);
        if (rows.Count == 0) return true; // legacy: nothing configured → commands allowed everywhere
        return rows.Any(c => c.ChannelId == channelId && c.MonitoringEnabled &&
            (ChannelPurposes.HasPurpose(c.Purposes, ChannelPurposes.Moderation) ||
             ChannelPurposes.HasPurpose(c.Purposes, ChannelPurposes.Admin)));
    }

    public async Task<IReadOnlyList<ConnectedChannel>> GetByPurposeAsync(string botId, string purpose) =>
        (await _data.GetConnectedChannelsAsync(botId).ConfigureAwait(false))
            .Where(c => c.MonitoringEnabled && ChannelPurposes.HasPurpose(c.Purposes, purpose))
            .ToList();
}
