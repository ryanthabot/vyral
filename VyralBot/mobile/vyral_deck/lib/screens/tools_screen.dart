import 'package:flutter/material.dart';

import '../core/action_catalog.dart';
import '../services/deck_connection.dart';

/// One-tap stream tools + chat send. Everything here rides the same
/// authenticated WebSocket as the deck — the server executes each request
/// through the regular action executor.
class ToolsScreen extends StatefulWidget {
  const ToolsScreen({super.key, required this.connection, required this.catalog});

  final DeckConnection connection;
  final List<ActionInfo> catalog;

  @override
  State<ToolsScreen> createState() => _ToolsScreenState();
}

class _ToolsScreenState extends State<ToolsScreen> {
  final _chatController = TextEditingController();
  String _chatPlatform = 'twitch';

  @override
  void dispose() {
    _chatController.dispose();
    super.dispose();
  }

  void _fire(String actionType, {String param1 = '', String param2 = ''}) {
    widget.connection.action(actionType, param1: param1, param2: param2);
  }

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
      children: [
        _section(context, 'STREAM TOOLS', Icons.live_tv),
        _toolTile(Icons.movie_creation, 'Create clip',
            'Saves a 30-second clip of the last moments (Twitch)', () => _fire('TwitchClip')),
        _toolTile(Icons.bookmark_add, 'Add stream marker',
            'Bookmark this moment on the Twitch VOD', () => _fire('TwitchMarker')),
        _toolTile(Icons.logout, 'Cancel raid', 'Stop an outgoing raid', () => _fire('TwitchStopRaid')),
        _toolTile(Icons.auto_awesome, 'Flash lights', 'Flash the smart lights (party!)',
            () => _fire('LightsFlash', param1: '', param2: '3')),

        _section(context, 'MODERATION', Icons.shield),
        _toolTile(Icons.delete_sweep, 'Clear chat — Twitch', 'Delete every chat message',
            () => _fire('TwitchClearChat')),
        _toolTile(Icons.delete_sweep, 'Clear chat — Kick', 'Delete every chat message',
            () => _fire('KickClearChat')),
        _toolTile(Icons.emoticon, 'Toggle emote-only — Twitch', 'Flips the current state',
            () => _fire('TwitchEmoteOnlyToggle')),
        _toolTile(Icons.shield_moon, 'Toggle shield mode — Twitch', 'Flips the current state',
            () => _fire('TwitchShieldToggle')),
        _toolTile(Icons.follow_the_signs, 'Toggle follower-only — Twitch', 'Flips the current state',
            () => _fire('TwitchFollowerOnlyToggle')),
        _toolTile(Icons.emoticon, 'Emote-only — Kick', 'Turn on (param off to disable)',
            () => _fire('KickEmoteOnly', param2: 'on')),
        _toolTile(Icons.speed, 'Slow mode — Kick', 'Turn on', () => _fire('KickSlowMode', param2: 'on')),

        _section(context, 'SEND TO CHAT', Icons.chat_bubble),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              children: [
                SegmentedButton<String>(
                  segments: const [
                    ButtonSegment(value: 'twitch', label: Text('Twitch')),
                    ButtonSegment(value: 'kick', label: Text('Kick')),
                    ButtonSegment(value: 'youtube', label: Text('YouTube')),
                  ],
                  selected: {_chatPlatform},
                  onSelectionChanged: (selection) =>
                      setState(() => _chatPlatform = selection.first),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: _chatController,
                  maxLines: 2,
                  decoration: InputDecoration(
                    labelText: 'Message to send',
                    suffixIcon: IconButton(
                      icon: const Icon(Icons.send),
                      onPressed: _sendChat,
                    ),
                  ),
                  onSubmitted: (_) => _sendChat(),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  void _sendChat() {
    final message = _chatController.text.trim();
    if (message.isEmpty) return;
    _fire('ChatSend', param1: message, param2: _chatPlatform);
    _chatController.clear();
    FocusScope.of(context).unfocus();
  }

  Widget _section(BuildContext context, String title, IconData icon) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(0, 18, 0, 6),
      child: Row(
        children: [
          Icon(icon, size: 15, color: Theme.of(context).colorScheme.primary),
          const SizedBox(width: 6),
          Text(title,
              style: const TextStyle(fontSize: 12, fontWeight: FontWeight.bold, letterSpacing: 1)),
        ],
      ),
    );
  }

  Widget _toolTile(IconData icon, String title, String subtitle, VoidCallback onTap) {
    return Card(
      margin: const EdgeInsets.symmetric(vertical: 4),
      child: ListTile(
        leading: Icon(icon, color: Theme.of(context).colorScheme.primary),
        title: Text(title, style: const TextStyle(fontSize: 15)),
        subtitle: Text(subtitle, style: const TextStyle(fontSize: 12)),
        trailing: const Icon(Icons.play_arrow),
        onTap: onTap,
      ),
    );
  }
}
