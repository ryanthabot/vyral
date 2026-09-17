import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

class DiscoveredServer {
  DiscoveredServer(this.serverName, this.host, this.tcpPort);
  final String serverName;
  final String host;
  final int tcpPort;

  String get label => '$serverName ($host:${tcpPort == 0 ? '?' : tcpPort})';
}

/// UDP discovery: VyralBot answers `VYRALDECK_DISCOVER` broadcasts on port
/// 5198 while its StreamDeck server is running. Best-effort — manual entry
/// always works when broadcast is blocked (guest Wi-Fi, AP isolation…).
class DeckDiscovery {
  static Future<List<DiscoveredServer>> scan({
    Duration timeout = const Duration(seconds: 3),
    int discoveryPort = 5198,
  }) async {
    RawDatagramSocket? socket;
    StreamSubscription<RawSocketEvent>? subscription;
    try {
      socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
      socket.broadcastEnabled = true;
      final payload = utf8.encode('VYRALDECK_DISCOVER');

      // Limited broadcast + (best-effort) the interface-directed broadcasts.
      final targets = <InternetAddress>[InternetAddress('255.255.255.255')];
      try {
        final interfaces = await NetworkInterface.list();
        for (final interface in interfaces) {
          for (final address in interface.addresses) {
            if (address.type == InternetAddressType.IPv4 && !address.isLoopback) {
              final octets = address.rawAddress;
              // Directed broadcast: x.y.z.255 for a /24 (most home networks).
              targets.add(InternetAddress.fromRawAddress(
                Uint8List.fromList([...octets.sublist(0, 3), 255]),
              ));
            }
          }
        }
      } catch (_) {
        // Interface listing is a bonus — limited broadcast already went out.
      }

      final results = <DiscoveredServer>[];
      final seen = <String>{};

      subscription = socket.listen((event) {
        if (event != RawSocketEvent.read) return;
        final datagram = socket!.receive();
        if (datagram == null) return;
        try {
          final json = jsonDecode(utf8.decode(datagram.data));
          if (json is! Map<String, dynamic> || json['app'] != 'VyralDeck') return;
          final host = datagram.address.address;
          final key = '$host:${json['tcpPort']}';
          if (seen.contains(key)) return;
          seen.add(key);
          results.add(DiscoveredServer(
            (json['serverName'] ?? 'Vyral PC').toString(),
            host,
            (json['tcpPort'] as num?)?.toInt() ?? 0,
          ));
        } catch (_) {
          // not ours / malformed — skip
        }
      }, onError: (_) {
        // Transient UDP errors — the scan simply returns what it has.
      });

      for (final target in targets) {
        try {
          socket.send(payload, target, discoveryPort);
        } catch (_) {}
      }

      await Future<void>.delayed(timeout);
      return results;
    } on SocketException {
      // UDP may be blocked (hotspots, managed Wi-Fi). Manual entry still works.
      return [];
    } finally {
      await subscription?.cancel();
      socket?.close();
    }
  }
}
