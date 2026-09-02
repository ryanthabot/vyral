using System.Windows.Controls;

namespace VyralBot.UI.Views;

/// <summary>
/// Discord Bots page: the multi-bot home. Lists configured bots with their
/// live status, quick-adds new bots, and opens the per-bot dashboard.
/// Initialization is driven by MainViewModel navigation (InitializeAsync).
/// </summary>
public partial class DiscordBotsView : UserControl
{
    public DiscordBotsView()
    {
        InitializeComponent();
    }
}
