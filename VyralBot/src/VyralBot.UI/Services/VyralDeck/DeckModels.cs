using System.Text.Json;
using System.Text.Json.Serialization;

namespace VyralBot.UI.Services.VyralDeck;

/// <summary>
/// Everything a VyralDeck button can do. One flat enum so the desktop page, the
/// LAN protocol and the mobile companion app all speak the same action names.
/// </summary>
public enum DeckActionType
{
    ObsScene,
    ObsStreamStart,
    ObsStreamStop,
    ChatSend,
    TwitchClip,
    TwitchMarker,
    TwitchRaid,
    TwitchStopRaid,
    TwitchClearChat,
    TwitchEmoteOnlyToggle,
    TwitchFollowerOnlyToggle,
    TwitchShieldToggle,
    KickClearChat,
    KickEmoteOnly,
    KickFollowerOnly,
    KickSlowMode,
    SoundPlay,
    AlertShow,
    WorkflowRun,
    LightsFlash,
    LightsPower,
    UriLaunch,
}

/// <summary>One deck button slot. An empty slot is just a button with no Label.</summary>
public sealed class DeckButton
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = "";

    /// <summary>Short emoji/text shown above the label ("" = none).</summary>
    public string Icon { get; set; } = "";

    public string ColorHex { get; set; } = "#14B8A6";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DeckActionType ActionType { get; set; } = DeckActionType.ObsScene;

    /// <summary>Primary parameter — meaning depends on <see cref="DeckActionCatalog"/>.</summary>
    public string Param1 { get; set; } = "";

    /// <summary>Secondary parameter (platform, volume, on/off …).</summary>
    public string Param2 { get; set; } = "";

    public bool IsEmpty => string.IsNullOrWhiteSpace(Label);

    public DeckButton Clone() => new()
    {
        Id = Id,
        Label = Label,
        Icon = Icon,
        ColorHex = ColorHex,
        ActionType = ActionType,
        Param1 = Param1,
        Param2 = Param2,
    };
}

/// <summary>A named page of exactly <see cref="SlotsPerPage"/> button slots.</summary>
public sealed class DeckPage
{
    public const int SlotsPerPage = 12;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Page 1";

    public List<DeckButton> Buttons { get; set; } = new();

    public static DeckPage CreateDefault(string name) => new()
    {
        Name = name,
        Buttons = Enumerable.Range(0, SlotsPerPage).Select(_ => new DeckButton()).ToList(),
    };
}

/// <summary>The whole deck: pages + persistence round-trip.</summary>
public sealed class DeckLayout
{
    public List<DeckPage> Pages { get; set; } = new();

    public static DeckLayout CreateDefault() => new()
    {
        Pages =
        {
            DefaultPage(),
        },
    };

    private static DeckPage DefaultPage()
    {
        var page = DeckPage.CreateDefault("Main");
        page.Buttons[0] = new DeckButton { Label = "Clip", Icon = "🎬", ColorHex = "#9146FF", ActionType = DeckActionType.TwitchClip };
        page.Buttons[1] = new DeckButton { Label = "Marker", Icon = "📌", ColorHex = "#9146FF", ActionType = DeckActionType.TwitchMarker };
        page.Buttons[2] = new DeckButton { Label = "Start Stream", Icon = "▶️", ColorHex = "#2EA043", ActionType = DeckActionType.ObsStreamStart };
        page.Buttons[3] = new DeckButton { Label = "Stop Stream", Icon = "⏹️", ColorHex = "#DA3633", ActionType = DeckActionType.ObsStreamStop };
        return page;
    }

    public DeckButton? FindButton(string buttonId) =>
        Pages.SelectMany(p => p.Buttons).FirstOrDefault(b => b.Id == buttonId);
}

/// <summary>Human-facing metadata for one action type: display name + which params it uses.</summary>
public sealed record DeckActionInfo(
    DeckActionType Type,
    string DisplayName,
    string Category,
    string? Param1Label,
    string? Param2Label,
    string Param1Placeholder = "",
    string Param2Placeholder = "");

/// <summary>
/// Static catalog shared by the desktop editor and the mobile companion app
/// (the server serializes it into the welcome message so the phone can render
/// the same editor without a hardcoded copy).
/// </summary>
public static class DeckActionCatalog
{
    public static readonly IReadOnlyList<DeckActionInfo> Actions =
    [
        new(DeckActionType.ObsScene, "OBS: Switch Scene", "OBS", "Scene name", null, "e.g. Gameplay"),
        new(DeckActionType.ObsStreamStart, "OBS: Start Streaming", "OBS", null, null),
        new(DeckActionType.ObsStreamStop, "OBS: Stop Streaming", "OBS", null, null),
        new(DeckActionType.ChatSend, "Chat: Send Message", "Chat", "Message", "Platform (twitch/kick/youtube)", "e.g. GGs everyone!", "twitch"),
        new(DeckActionType.TwitchClip, "Twitch: Create Clip", "Twitch", null, null),
        new(DeckActionType.TwitchMarker, "Twitch: Stream Marker", "Twitch", "Description (optional)", null, "e.g. Boss fight"),
        new(DeckActionType.TwitchRaid, "Twitch: Raid Channel", "Twitch", "Target channel", null, "e.g. somechannel"),
        new(DeckActionType.TwitchStopRaid, "Twitch: Cancel Raid", "Twitch", null, null),
        new(DeckActionType.TwitchClearChat, "Twitch: Clear Chat", "Twitch", null, null),
        new(DeckActionType.TwitchEmoteOnlyToggle, "Twitch: Toggle Emote-Only", "Twitch", null, null),
        new(DeckActionType.TwitchFollowerOnlyToggle, "Twitch: Toggle Follower-Only", "Twitch", null, null),
        new(DeckActionType.TwitchShieldToggle, "Twitch: Toggle Shield Mode", "Twitch", null, null),
        new(DeckActionType.KickClearChat, "Kick: Clear Chat", "Kick", null, null),
        new(DeckActionType.KickEmoteOnly, "Kick: Emote-Only", "Kick", null, "on/off", "", "on"),
        new(DeckActionType.KickFollowerOnly, "Kick: Follower-Only", "Kick", null, "on/off", "", "on"),
        new(DeckActionType.KickSlowMode, "Kick: Slow Mode", "Kick", null, "on/off", "", "on"),
        new(DeckActionType.SoundPlay, "Sound: Play", "Audio", "Sound file name", "Volume 0.0-1.0 (optional)", "e.g. applause.mp3", "0.8"),
        new(DeckActionType.AlertShow, "Alert: Show", "Audio", "Title", "Message (optional)", "e.g. Thanks for the raid!", ""),
        new(DeckActionType.WorkflowRun, "Automation: Run Workflow", "Automation", "Workflow name", null, "e.g. Raid welcome"),
        new(DeckActionType.LightsFlash, "Lights: Flash", "Smart Home", "Hex color (optional)", "Times (optional)", "#FF00AA", "3"),
        new(DeckActionType.LightsPower, "Lights: Power", "Smart Home", null, "on/off", "", "on"),
        new(DeckActionType.UriLaunch, "Advanced: Launch URI", "Advanced", "URI (also streamdeck://)", null, "streamdeck://plugins/..."),
    ];

    public static DeckActionInfo Info(DeckActionType type) =>
        Actions.FirstOrDefault(a => a.Type == type)
        ?? new DeckActionInfo(type, type.ToString(), "Other", null, null);

    /// <summary>Short one-line description of what a button does (tooltip / mobile subtitle).</summary>
    public static string Describe(DeckButton button)
    {
        var info = Info(button.ActionType);
        return (info.Param1Label, info.Param2Label) switch
        {
            (null, null) => info.DisplayName,
            ("Description (optional)", _) => $"{info.DisplayName} — {Or(button.Param1, "untitled")}",
            (_, "on/off") => $"{info.DisplayName} ({(string.IsNullOrWhiteSpace(button.Param2) ? "on" : button.Param2)})",
            ("Scene name", _) => $"{info.DisplayName} — {Or(button.Param1, "…")}",
            ("Message", _) => $"{info.DisplayName} [{Or(button.Param2, "twitch")}] {Or(button.Param1, "")}".TrimEnd(),
            _ => button.Param1.Length > 0 ? $"{info.DisplayName} — {button.Param1}" : info.DisplayName,
        };
    }

    private static string Or(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
