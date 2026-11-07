import 'dart:async';
import 'dart:convert';
import 'dart:io';

import '../core/action_catalog.dart';
import '../core/models.dart';

/// Live status of one platform as reported by the server.
typedef LiveState = Map<String, bool>;

/// Events surfaced from the socket to the UI.
sealed class DeckEvent {}

class WelcomeEvent extends DeckEvent {
  WelcomeEvent(this.serverName, this.layout, this.state, this.catalog,
      {this.protocol = 0});
  final String serverName;
  final DeckLayoutData layout;
  final LiveState state;
  final List<ActionInfo> catalog;

  /// Protocol version the server reports (0 when absent/unreadable).
  final int protocol;
}

class ConfigEvent extends DeckEvent {
  ConfigEvent(this.layout);
  final DeckLayoutData layout;
}

class StateEvent extends DeckEvent {
  StateEvent(this.state);
  final LiveState state;
}

class PressResultEvent extends DeckEvent {
  PressResultEvent(this.buttonId, this.ok, this.message);
  final String? buttonId;
  final bool ok;
  final String message;
}

class ErrorEvent extends DeckEvent {
  ErrorEvent(this.message);
  final String message;
}

class DisconnectedEvent extends DeckEvent {
  DisconnectedEvent(this.reason);
  final String reason;
}

/// Result of an initial connect attempt.
class ConnectResult {
  ConnectResult(this.ok, this.error, {this.rejected = false});
  final bool ok;
  final String? error;

  /// True when the PC answered but refused the token (unpaired on desktop).
  final bool rejected;
}

/// WebSocket session with the VyralDeck server on the PC.
/// Plain dart:io WebSocket — LAN-only app, no extra dependencies.
class DeckConnection {
  DeckConnection(this.host, this.port);

  final String host;
  final int port;

  WebSocket? _socket;
  StreamSubscription? _subscription;
  final _events = StreamController<DeckEvent>.broadcast();
  String? token;
  String serverName = '';

  bool get isConnected => _socket != null && _socket!.readyState == WebSocket.open;

  Stream<DeckEvent> get events => _events.stream;

  Future<ConnectResult> connect() async {
    try {
      await _closeSocket();
      final query = token == null ? '' : '?token=${Uri.encodeQueryComponent(token!)}';
      final socket = await WebSocket.connect(
        'ws://$host:$port/vyraldeck/ws$query',
        protocols: ['vyraldeck'],
      );
      _socket = socket;
      _subscription = socket.listen(
        _onMessage,
        onError: (Object error) => _events.add(DisconnectedEvent('$error')),
        onDone: () {
          _socket = null;
          _events.add(DisconnectedEvent('Connection closed'));
        },
        cancelOnError: true,
      );
      return ConnectResult(true, null);
    } on SocketException catch (e) {
      return ConnectResult(false, 'Cannot reach $host:$port — ${e.message}');
    } on WebSocketException {
      // The server answered with a non-upgrade response (HTTP 401 = bad token).
      return ConnectResult(
        false,
        'The PC rejected the pairing — pair again with the PIN.',
        rejected: true,
      );
    } catch (e) {
      return ConnectResult(false, 'Connection failed — $e');
    }
  }

  void _onMessage(dynamic data) {
    Map<String, dynamic> message;
    try {
      message = jsonDecode(data as String) as Map<String, dynamic>;
    } catch (_) {
      return; // ignore malformed frames
    }

    // A single unexpected shape must never kill the socket handler.
    try {
      _dispatch(message);
    } catch (_) {
      _events.add(ErrorEvent('Could not read a message from the PC.'));
    }
  }

  void _dispatch(Map<String, dynamic> message) {
    switch (message['type']) {
      case 'welcome':
        serverName = (message['serverName'] ?? '').toString();
        final layoutJson = message['layout'];
        final layout = layoutJson is Map<String, dynamic>
            ? DeckLayoutData.tryParse(layoutJson) ?? DeckLayoutData.empty()
            : DeckLayoutData.empty();
        final catalogJson = (message['catalog'] as List<dynamic>? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(ActionInfo.fromJson)
            .toList();
        _events.add(WelcomeEvent(
          serverName,
          layout,
          parseLiveState(message['state']),
          catalogJson.isEmpty ? defaultActionCatalog : catalogJson,
          protocol: parseProtocolVersion(message['protocol']),
        ));
        break;
      case 'config':
        final layoutJson = message['layout'];
        final layout = layoutJson is Map<String, dynamic>
            ? DeckLayoutData.tryParse(layoutJson) ?? DeckLayoutData.empty()
            : DeckLayoutData.empty();
        _events.add(ConfigEvent(layout));
        break;
      case 'state':
        _events.add(StateEvent(parseLiveState(message['state'])));
        break;
      case 'press_result':
        _events.add(PressResultEvent(
          message['buttonId']?.toString(),
          message['ok'] == true,
          (message['message'] ?? '').toString(),
        ));
        break;
      case 'config_saved':
        // The broadcast 'config' message does the UI update.
        break;
      case 'error':
        _events.add(ErrorEvent((message['message'] ?? '').toString()));
        break;
      default:
        break;
    }
  }

  LiveState parseLiveState(dynamic raw) {
    final map = <String, bool>{};
    if (raw is Map<String, dynamic>) {
      raw.forEach((key, value) => map[key] = value == true);
    }
    return map;
  }

  /// `protocol` rides on the pair response and every `welcome` frame.
  /// Returns 0 when the field is missing or not a number so callers only ever
  /// compare against [knownProtocolVersion].
  static int parseProtocolVersion(dynamic raw) =>
      raw is num ? raw.toInt() : int.tryParse('$raw') ?? 0;

  void press(String buttonId) => _send({
        'type': 'press',
        'buttonId': buttonId,
      });

  void action(String actionType, {String param1 = '', String param2 = ''}) =>
      _send({
        'type': 'action',
        'actionType': actionType,
        'param1': param1,
        'param2': param2,
      });

  void sendConfig(DeckLayoutData layout) => _send({
        'type': 'config_set',
        'layout': layout.toJson(),
      });

  void ping() => _send({'type': 'ping'});

  void _send(Map<String, dynamic> message) {
    final socket = _socket;
    if (socket == null || socket.readyState != WebSocket.open) return;
    socket.add(jsonEncode(message));
  }

  Future<void> _closeSocket() async {
    await _subscription?.cancel();
    _subscription = null;
    final socket = _socket;
    _socket = null;
    if (socket != null) {
      try {
        await socket.close();
      } catch (_) {}
    }
  }

  Future<void> dispose() => _closeSocket();
}
