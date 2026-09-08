using CommunityToolkit.Mvvm.ComponentModel;

namespace VyralBot.UI.ViewModels;

/// <summary>Row model for the Connected Channels panel: a guild channel plus its stored registry state.</summary>
public partial class ConnectedChannelItem : ObservableObject
{
    public string BotId { get; init; } = "";
    public string ChannelId { get; init; } = "";
    public string ChannelName { get; init; } = "";
    public string GuildId { get; init; } = "";
    public string GuildName { get; init; } = "";
    /// <summary>False when the stored channel no longer exists in the guild snapshot (stale row).</summary>
    public bool ExistsInGuild { get; init; } = true;

    [ObservableProperty]
    private bool _monitoringEnabled;

    [ObservableProperty]
    private bool _isModeration;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isSupport;

    [ObservableProperty]
    private bool _isLogging;

    public string PurposeSummary
    {
        get
        {
            var purposes = new[] { IsModeration ? "Moderation" : null, IsAdmin ? "Admin" : null, IsSupport ? "Support" : null, IsLogging ? "Logging" : null }
                .OfType<string>().ToList();
            return purposes.Count == 0 ? "General" : string.Join(", ", purposes);
        }
    }

    public string PurposeCsv =>
        string.Join(",", new[] { IsModeration ? "Moderation" : null, IsAdmin ? "Admin" : null,
                                 IsSupport ? "Support" : null, IsLogging ? "Logging" : null }.OfType<string>());

    /// <summary>PurposeSummary/PurposeCsv depend on the flag properties; raise their change after a flag toggles.</summary>
    public void RefreshPurposeSummary() => OnPropertyChanged(nameof(PurposeSummary));
}
