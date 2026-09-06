namespace VyralBot.Integrations.Discord;

/// <summary>
/// Well-known per-bot role names for the multi-bot Discord manager
/// (<see cref="IDiscordBotManager"/>). A role is single-assignment app-wide:
/// exactly one DiscordBots row holds it at a time —
/// <see cref="IDiscordBotManager.AssignRoleAsync"/> clears the role on every
/// other row when it assigns it. Role resolution is CASE-SENSITIVE on these
/// constants (they are stored verbatim in the row flags, not as strings —
/// the constants are the public identifiers used by callers and the UI).
/// </summary>
public static class DiscordBotRoles
{
    /// <summary>
    /// The bot that handles account-link DM intake (Viewer Hub account linking).
    /// Unset falls back to the Primary (first connected) bot.
    /// </summary>
    public const string Linking = "Linking";

    /// <summary>
    /// The bot that sends giveaway announcements. Unset falls back to the
    /// Primary (first connected) bot.
    /// </summary>
    public const string Giveaway = "GiveawayAnnouncements";

    /// <summary>All known roles (UI pickers and validation).</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Linking, Giveaway };
}
