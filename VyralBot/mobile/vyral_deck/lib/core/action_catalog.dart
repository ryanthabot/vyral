/// Local copy of the deck action catalog so the editor works before/independent
/// of the server's `welcome.catalog`. The server catalog (when received) wins.
/// Mirrors DeckActionCatalog in VyralBot.UI/Services/VyralDeck/DeckModels.cs.

class ActionInfo {
  const ActionInfo(
    this.type,
    this.name,
    this.category, [
    this.param1Label,
    this.param2Label,
    this.param1Placeholder = '',
    this.param2Placeholder = '',
  ]);

  final String type;
  final String name;
  final String category;
  final String? param1Label;
  final String? param2Label;
  final String param1Placeholder;
  final String param2Placeholder;

  factory ActionInfo.fromJson(Map<String, dynamic> json) => ActionInfo(
        (json['type'] ?? '').toString(),
        (json['name'] ?? '').toString(),
        (json['category'] ?? '').toString(),
        json['param1Label']?.toString(),
        json['param2Label']?.toString(),
        (json['param1Placeholder'] ?? '').toString(),
        (json['param2Placeholder'] ?? '').toString(),
      );
}

const List<ActionInfo> defaultActionCatalog = [
  ActionInfo('ObsScene', 'OBS: Switch Scene', 'OBS', 'Scene name', null, 'e.g. Gameplay'),
  ActionInfo('ObsStreamStart', 'OBS: Start Streaming', 'OBS'),
  ActionInfo('ObsStreamStop', 'OBS: Stop Streaming', 'OBS'),
  ActionInfo('ChatSend', 'Chat: Send Message', 'Chat', 'Message', 'Platform (twitch/kick/youtube)', 'e.g. GGs everyone!', 'twitch'),
  ActionInfo('TwitchClip', 'Twitch: Create Clip', 'Twitch'),
  ActionInfo('TwitchMarker', 'Twitch: Stream Marker', 'Twitch', 'Description (optional)'),
  ActionInfo('TwitchRaid', 'Twitch: Raid Channel', 'Twitch', 'Target channel', null, 'e.g. somechannel'),
  ActionInfo('TwitchStopRaid', 'Twitch: Cancel Raid', 'Twitch'),
  ActionInfo('TwitchClearChat', 'Twitch: Clear Chat', 'Twitch'),
  ActionInfo('TwitchEmoteOnlyToggle', 'Twitch: Toggle Emote-Only', 'Twitch'),
  ActionInfo('TwitchFollowerOnlyToggle', 'Twitch: Toggle Follower-Only', 'Twitch'),
  ActionInfo('TwitchShieldToggle', 'Twitch: Toggle Shield Mode', 'Twitch'),
  ActionInfo('KickClearChat', 'Kick: Clear Chat', 'Kick'),
  ActionInfo('KickEmoteOnly', 'Kick: Emote-Only', 'Kick', null, 'on/off', '', 'on'),
  ActionInfo('KickFollowerOnly', 'Kick: Follower-Only', 'Kick', null, 'on/off', '', 'on'),
  ActionInfo('KickSlowMode', 'Kick: Slow Mode', 'Kick', null, 'on/off', '', 'on'),
  ActionInfo('SoundPlay', 'Sound: Play', 'Audio', 'Sound file name', 'Volume 0.0-1.0 (optional)', 'e.g. applause.mp3', '0.8'),
  ActionInfo('AlertShow', 'Alert: Show', 'Audio', 'Title', 'Message (optional)', 'e.g. Thanks for the raid!', ''),
  ActionInfo('WorkflowRun', 'Automation: Run Workflow', 'Automation', 'Workflow name', null, 'e.g. Raid welcome'),
  ActionInfo('LightsFlash', 'Lights: Flash', 'Smart Home', 'Hex color (optional)', 'Times (optional)', '#FF00AA', '3'),
  ActionInfo('LightsPower', 'Lights: Power', 'Smart Home', null, 'on/off', '', 'on'),
  ActionInfo('UriLaunch', 'Advanced: Launch URI', 'Advanced', 'URI (also streamdeck://)', null, 'streamdeck://plugins/...'),
];

const List<String> colorChoices = [
  '#14B8A6', // teal — app accent
  '#2EA043', // green
  '#DA3633', // red
  '#F0883E', // orange
  '#9146FF', // purple
  '#388BFD', // blue
  '#DB2777', // pink
  '#EAC54F', // gold
  '#8B949E', // grey
];

ActionInfo infoFor(List<ActionInfo> catalog, String type) {
  for (final info in catalog) {
    if (info.type == type) return info;
  }
  return ActionInfo(type, type, 'Other');
}
