import 'dart:async';

import 'package:flutter/material.dart';

import '../core/action_catalog.dart';
import '../core/models.dart';
import '../services/deck_connection.dart';
import '../widgets/button_editor_sheet.dart';
import 'tools_screen.dart';

/// The live deck: pages + button grid. Tap fires; long-press or edit-mode tap
/// customizes. Edits are saved on the PC and rebroadcast to all devices.
/// Owns its [DeckConnection]: connects on entry, disposes on exit.
class DeckScreen extends StatefulWidget {
  const DeckScreen({
    super.key,
    required this.host,
    required this.port,
    required this.token,
    required this.onBadToken,
  });

  final String host;
  final int port;

  /// Paired-device token (never leaves the LAN).
  final String token;

  /// Called when the PC rejects the token (unpaired on the desktop) so the
  /// app can fall back to the pairing screen.
  final VoidCallback onBadToken;

  @override
  State<DeckScreen> createState() => _DeckScreenState();
}

class _DeckScreenState extends State<DeckScreen> {
  late final DeckConnection _connection;
  DeckLayoutData _layout = DeckLayoutData.empty();
  List<ActionInfo> _catalog = defaultActionCatalog;
  LiveState _state = {};
  String _serverName = '';
  int _pageIndex = 0;
  int _tab = 0;
  bool _editMode = false;
  StreamSubscription<DeckEvent>? _subscription;
  bool _connecting = true;
  bool _openingSession = false;
  String? _connectError;

  DeckPageData get _page {
    if (_layout.pages.isEmpty) _layout.pages.add(DeckPageData.empty());
    if (_pageIndex >= _layout.pages.length) _pageIndex = 0;
    return _layout.pages[_pageIndex];
  }

  @override
  void initState() {
    super.initState();
    _connection = DeckConnection(widget.host, widget.port);
    _connection.token = widget.token;
    _subscription = _connection.events.listen(_onEvent);
    _openSession();
  }

  Future<void> _openSession() async {
    if (_openingSession) return;
    _openingSession = true;
    try {
      setState(() {
        _connecting = true;
        _connectError = null;
      });
      final result = await _connection.connect();
      if (!mounted) return;
      if (!result.ok) {
        if (result.rejected) {
          // Token is no longer valid on the PC — fall back to pairing.
          widget.onBadToken();
          return;
        }
        setState(() {
          _connecting = false;
          _connectError = result.error;
        });
        return;
      }

      // The welcome frame confirms the session and carries the layout. Nothing
      // within a few seconds means the server went away mid-handshake.
      final welcome = await _connection.events
          .firstWhere(
            (event) => event is WelcomeEvent || event is DisconnectedEvent,
          )
          .timeout(const Duration(seconds: 8), onTimeout: () => DisconnectedEvent('Timed out'));
      if (!mounted) return;

      if (welcome is WelcomeEvent) {
        setState(() => _connecting = false);
        return;
      }

      setState(() {
        _connecting = false;
        _connectError = 'No answer from the PC — check VyralBot and press Retry.';
      });
    } finally {
      _openingSession = false;
    }
  }

  @override
  void dispose() {
    _subscription?.cancel();
    _connection.dispose();
    super.dispose();
  }

  void _applyWelcome(WelcomeEvent event) {
    setState(() {
      _serverName = event.serverName;
      _layout = event.layout;
      _catalog = event.catalog;
      _state = event.state;
      if (_pageIndex >= _layout.pages.length) _pageIndex = 0;
    });
  }

  void _onEvent(DeckEvent event) {
    switch (event) {
      case WelcomeEvent():
        _applyWelcome(event);
        break;
      case ConfigEvent():
        setState(() {
          _layout = event.layout;
          if (_pageIndex >= _layout.pages.length) _pageIndex = 0;
        });
        break;
      case StateEvent():
        setState(() => _state = event.state);
        break;
      case PressResultEvent():
        _snack(event.message, error: !event.ok);
        break;
      case ErrorEvent():
        _snack(event.message, error: true);
        break;
      case DisconnectedEvent():
        // Attempt one delayed reconnect; failures surface as the retry banner.
        Future.delayed(const Duration(seconds: 2), () {
          if (mounted && !_openingSession) _openSession();
        });
        break;
    }
  }

  void _snack(String message, {bool error = false}) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..clearSnackBars()
      ..showSnackBar(SnackBar(
        content: Text(message),
        duration: const Duration(seconds: 3),
        backgroundColor: error ? const Color(0xFF7A2E2E) : null,
      ));
  }

  Future<void> _tapButton(DeckButtonData button) async {
    if (_editMode) {
      await _editButton(button);
      return;
    }
    if (button.isEmpty) {
      _snack('Empty slot — turn on edit mode to configure it.');
      return;
    }
    _connection.press(button.id);
  }

  Future<void> _editButton(DeckButtonData original) async {
    final updated = await showButtonEditor(context, _catalog, original);
    if (updated == null) return;

    // Mutate a copy, send the whole layout — the PC is the store of truth.
    final copy = _layout.copy();
    for (final page in copy.pages) {
      for (var i = 0; i < page.buttons.length; i++) {
        if (page.buttons[i].id == original.id) {
          page.buttons[i] = updated;
        }
      }
    }
    setState(() {
      // Optimistic update; the server broadcast confirms it for everyone.
      for (final page in _layout.pages) {
        for (var i = 0; i < page.buttons.length; i++) {
          if (page.buttons[i].id == original.id) {
            page.buttons[i] = updated;
          }
        }
      }
    });
    _connection.sendConfig(copy);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(_serverName.isEmpty ? 'VyralDeck' : _serverName,
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
            Row(children: [
              _liveDot('Twitch', const Color(0xFF9146FF)),
              _liveDot('Kick', const Color(0xFF2EA043)),
              _liveDot('YouTube', const Color(0xFFDA3633)),
            ]),
          ],
        ),
        actions: [
          if (_connectError != null)
            TextButton.icon(
              onPressed: () => _openSession(),
              icon: const Icon(Icons.refresh, size: 18),
              label: const Text('Retry'),
            ),
          FilterChip(
            selected: _editMode,
            onSelected: (selected) => setState(() => _editMode = selected),
            label: const Text('Edit'),
            avatar: Icon(_editMode ? Icons.done : Icons.edit, size: 18),
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: _connecting
          ? const Center(child: CircularProgressIndicator())
          : IndexedStack(
              index: _tab,
              children: [
                _buildDeck(),
                ToolsScreen(connection: _connection, catalog: _catalog),
              ],
            ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: (index) => setState(() => _tab = index),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.grid_view_rounded), label: 'Deck'),
          NavigationDestination(icon: Icon(Icons.build_rounded), label: 'Tools'),
        ],
      ),
    );
  }

  Widget _liveDot(String platform, Color color) {
    final live = _state[platform] == true;
    return Padding(
      padding: const EdgeInsets.only(right: 10, top: 2),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: live ? color : Colors.white24,
            ),
          ),
          const SizedBox(width: 4),
          Text(platform,
              style: TextStyle(fontSize: 11, color: live ? Colors.white : Colors.white38)),
        ],
      ),
    );
  }

  Widget _buildDeck() {
    return Column(
      children: [
        // Page selector
        SizedBox(
          height: 52,
          child: ListView.builder(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            itemCount: _layout.pages.length,
            itemBuilder: (context, index) {
              final selected = index == _pageIndex;
              return Padding(
                padding: const EdgeInsets.only(right: 8),
                child: ChoiceChip(
                  label: Text(_layout.pages[index].name),
                  selected: selected,
                  onSelected: (_) => setState(() => _pageIndex = index),
                ),
              );
            },
          ),
        ),
        Expanded(
          child: GridView.builder(
            padding: const EdgeInsets.fromLTRB(12, 4, 12, 16),
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 3,
              mainAxisSpacing: 10,
              crossAxisSpacing: 10,
              childAspectRatio: 0.95,
            ),
            itemCount: _page.buttons.length,
            itemBuilder: (context, index) {
              final button = _page.buttons[index];
              return _DeckButtonTile(
                button: button,
                editMode: _editMode,
                onTap: () => _tapButton(button),
                onLongPress: () => _editButton(button),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _DeckButtonTile extends StatelessWidget {
  const _DeckButtonTile({
    required this.button,
    required this.editMode,
    required this.onTap,
    required this.onLongPress,
  });

  final DeckButtonData button;
  final bool editMode;
  final VoidCallback onTap;
  final VoidCallback onLongPress;

  @override
  Widget build(BuildContext context) {
    final empty = button.isEmpty;
    final base = empty ? const Color(0xFF21262D) : _parseColor(button.colorHex);
    return GestureDetector(
      onTap: onTap,
      onLongPress: onLongPress,
      child: AnimatedOpacity(
        duration: const Duration(milliseconds: 150),
        opacity: empty ? 0.45 : 1.0,
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: base,
            borderRadius: BorderRadius.circular(14),
            border: editMode
                ? Border.all(color: Colors.white54, width: 1.5)
                : Border.all(color: Colors.white10),
          ),
          child: Stack(
            children: [
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(8),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      if (button.icon.isNotEmpty)
                        Text(button.icon, style: const TextStyle(fontSize: 24)),
                      const SizedBox(height: 2),
                      Text(
                        button.label,
                        textAlign: TextAlign.center,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
                      ),
                    ],
                  ),
                ),
              ),
              if (editMode)
                const Positioned(
                  top: 6,
                  right: 6,
                  child: Icon(Icons.edit, size: 14, color: Colors.white70),
                ),
            ],
          ),
        ),
      ),
    );
  }

  static Color _parseColor(String hex) {
    var value = hex.replaceAll('#', '').trim();
    if (value.length == 6) value = 'FF$value';
    final parsed = int.tryParse(value, radix: 16);
    return parsed == null ? const Color(0xFF21262D) : Color(parsed);
  }
}
