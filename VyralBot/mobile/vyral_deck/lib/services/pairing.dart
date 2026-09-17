import 'dart:convert';
import 'dart:io';

class PairResult {
  PairResult(this.ok, {this.token, this.serverName, this.error});
  final bool ok;
  final String? token;
  final String? serverName;
  final String? error;
}

/// One-shot PIN pairing over HTTP (POST /vyraldeck/pair).
/// The returned token is stored on the phone and used for every later session.
class DeckPairing {
  static Future<PairResult> pair(
    String host,
    int port,
    String pin,
    String deviceName,
  ) async {
    try {
      final client = HttpClient()
        ..connectionTimeout = const Duration(seconds: 6);
      try {
        final request = await client.postUrl(
          Uri.parse('http://$host:$port/vyraldeck/pair'),
        );
        request.headers.contentType = ContentType.json;
        request.add(utf8.encode(jsonEncode({
          'pin': pin.trim(),
          'deviceName': deviceName,
        })));
        final response = await request.close();
        final body = await response.transform(utf8.decoder).join();
        final Map<String, dynamic> json;
        try {
          json = jsonDecode(body) as Map<String, dynamic>;
        } catch (_) {
          return PairResult(false, error: 'Unexpected reply from the PC.');
        }

        if (response.statusCode == 200 && json['ok'] == true) {
          return PairResult(
            true,
            token: (json['token'] ?? '').toString(),
            serverName: (json['serverName'] ?? 'Vyral PC').toString(),
          );
        }
        return PairResult(false, error: (json['error'] ?? 'Pairing failed.').toString());
      } finally {
        client.close(force: true);
      }
    } on SocketException catch (e) {
      return PairResult(false, error: 'Cannot reach $host:$port — ${e.message}');
    } catch (e) {
      return PairResult(false, error: 'Pairing failed — $e');
    }
  }
}
