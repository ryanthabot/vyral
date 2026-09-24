import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../services/discovery.dart';
import '../services/pairing.dart';
import 'deck_screen.dart';

class SavedSetup {
  String host = '';
  String port = '5199';
  String deviceName = '';
  String? token;
}

/// First screen: find/enter the PC, pair once with the PIN, then connect.
class ConnectScreen extends StatefulWidget {
  const ConnectScreen({super.key});

  @override
  State<ConnectScreen> createState() => _ConnectScreenState();
}

class _ConnectScreenState extends State<ConnectScreen> {
  final _host = TextEditingController();
  final _port = TextEditingController(text: '5199');
  final _pin = TextEditingController();
  final _deviceName = TextEditingController();

  final SavedSetup _saved = SavedSetup();
  bool _loaded = false;
  bool _busy = false;
  bool _scanning = false;
  String _status = '';
  List<DiscoveredServer> _found = [];

  @override
  void initState() {
    super.initState();
    _loadSaved();
  }

  @override
  void dispose() {
    _host.dispose();
    _port.dispose();
    _pin.dispose();
    _deviceName.dispose();
    super.dispose();
  }

  Future<void> _loadSaved() async {
    final prefs = await SharedPreferences.getInstance();
    if (!mounted) return;
    setState(() {
      _saved.host = prefs.getString('deck.host') ?? '';
      _saved.port = prefs.getString('deck.port') ?? '5199';
      _saved.deviceName = prefs.getString('deck.deviceName') ?? '';
      _saved.token = prefs.getString('deck.token');
      _host.text = _saved.host;
      _port.text = _saved.port;
      _deviceName.text = _saved.deviceName.isEmpty ? defaultDeviceName() : _saved.deviceName;
      _loaded = true;
    });
  }

  String get _portValue {
    final parsed = int.tryParse(_port.text.trim());
    return (parsed == null || parsed <= 0) ? '5199' : _port.text.trim();
  }

  bool get _hostValid => _host.text.trim().isNotEmpty;

  Future<void> _persist({String? token}) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('deck.host', _host.text.trim());
    await prefs.setString('deck.port', _portValue);
    await prefs.setString('deck.deviceName', _deviceName.text.trim());
    if (token != null) {
      await prefs.setString('deck.token', token);
      _saved.token = token;
    }
  }

  Future<void> _forgetPairing() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove('deck.token');
    if (!mounted) return;
    setState(() {
      _saved.token = null;
      _status = 'Pairing forgotten — next connect needs the PIN again.';
    });
  }

  Future<void> _scan() async {
    setState(() {
      _scanning = true;
      _status = 'Scanning the network…';
      _found = [];
    });
    final results = await DeckDiscovery.scan();
    if (!mounted) return;
    setState(() {
      _scanning = false;
      _found = results;
      _status = results.isEmpty
          ? 'No servers found. Make sure VyralBot is running and the StreamDeck server is STARTED on the PC — or type the address manually.'
          : 'Found ${results.length} server(s):';
    });
    if (results.isNotEmpty) {
      final first = results.first;
      setState(() {
        _host.text = first.host;
        if (first.tcpPort > 0) _port.text = first.tcpPort.toString();
      });
    }
  }

  /// Fresh pairing (PIN) → token → open the deck.
  Future<void> _pairAndConnect() async {
    if (!_hostValid) {
      setState(() => _status = 'Enter the PC address first.');
      return;
    }
    if (_pin.text.trim().length != 6) {
      setState(() => _status = 'Enter the 6-digit PIN shown on the PC.');
      return;
    }

    setState(() {
      _busy = true;
      _status = 'Pairing…';
    });

    final result = await DeckPairing.pair(
      _host.text.trim(),
      int.tryParse(_portValue) ?? 5199,
      _pin.text,
      _deviceName.text.trim().isEmpty ? defaultDeviceName() : _deviceName.text.trim(),
    );

    if (!mounted) return;
    if (!result.ok) {
      setState(() {
        _busy = false;
        _status = result.error ?? 'Pairing failed.';
      });
      return;
    }

    final newToken = result.token;
    if (newToken == null) {
      setState(() {
        _busy = false;
        _status = 'The PC accepted the pairing but sent no token — try again.';
      });
      return;
    }

    try {
      await _persist(token: newToken);
    } catch (_) {
      // Prefs failed — pairing still worked on the PC, just not saved locally.
    }
    if (!mounted) return;
    _openDeck(token: newToken);
  }

  /// Connect with the stored token (no PIN needed).
  Future<void> _connectSaved() async {
    if (!_hostValid) {
      setState(() => _status = 'Enter the PC address first.');
      return;
    }
    final token = _saved.token;
    if (token == null) {
      setState(() => _status = 'Not paired yet — pair with the PIN once.');
      return;
    }

    try {
      await _persist();
    } catch (_) {
      // non-fatal
    }
    if (!mounted) return;
    _openDeck(token: token);
  }

  /// The deck screen owns the connection from here on; when the token stops
  /// working it calls [onBadToken] and the captured root navigator routes back
  /// here for re-pairing (this State itself dies with pushReplacement).
  void _openDeck({required String token}) {
    final navigator = Navigator.of(context);
    navigator.pushReplacement(MaterialPageRoute(
      builder: (_) => DeckScreen(
        host: _host.text.trim(),
        port: int.tryParse(_portValue) ?? 5199,
        token: token,
        onBadToken: () => navigator.pushReplacement(
          MaterialPageRoute(builder: (_) => const ConnectScreen()),
        ),
      ),
    ));
  }

  @override
  Widget build(BuildContext context) {
    if (!_loaded) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    final paired = _saved.token != null;
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(20),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Icon(Icons.grid_view_rounded, color: Theme.of(context).colorScheme.primary, size: 34),
                      const SizedBox(width: 12),
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Text('VyralDeck', style: TextStyle(fontSize: 26, fontWeight: FontWeight.bold)),
                          Text(
                            'Your stream deck, on your Wi-Fi',
                            style: TextStyle(color: Theme.of(context).hintColor, fontSize: 13),
                          ),
                        ],
                      ),
                    ],
                  ),
                  const SizedBox(height: 24),
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(16),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Row(
                            children: [
                              Expanded(
                                flex: 3,
                                child: TextField(
                                  controller: _host,
                                  keyboardType: TextInputType.numberWithOptions(decimal: true),
                                  decoration: const InputDecoration(
                                    labelText: 'PC address',
                                    hintText: 'e.g. 192.168.1.20',
                                  ),
                                  onChanged: (_) => setState(() {}),
                                ),
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: TextField(
                                  controller: _port,
                                  keyboardType: TextInputType.number,
                                  decoration: const InputDecoration(labelText: 'Port'),
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 12),
                          OutlinedButton.icon(
                            onPressed: _scanning || _busy ? null : _scan,
                            icon: _scanning
                                ? const SizedBox(
                                    width: 16, height: 16,
                                    child: CircularProgressIndicator(strokeWidth: 2))
                                : const Icon(Icons.wifi_find),
                            label: Text(_scanning ? 'Scanning…' : 'Scan my network'),
                          ),
                          const Divider(height: 28),
                          if (!paired) ...[
                            TextField(
                              controller: _pin,
                              keyboardType: TextInputType.number,
                              maxLength: 6,
                              decoration: const InputDecoration(
                                labelText: 'Pairing PIN (shown on the PC)',
                                counterText: '',
                              ),
                            ),
                            const SizedBox(height: 12),
                            FilledButton.icon(
                              onPressed: _busy ? null : _pairAndConnect,
                              icon: const Icon(Icons.link),
                              label: const Text('PAIR & CONNECT'),
                            ),
                            const SizedBox(height: 12),
                          ] else ...[
                            FilledButton.icon(
                              onPressed: _busy ? null : _connectSaved,
                              icon: const Icon(Icons.play_arrow),
                              label: const Text('CONNECT'),
                            ),
                            TextButton(
                              onPressed: _busy ? null : _forgetPairing,
                              child: const Text('Forget pairing (pair again with PIN)'),
                            ),
                          ],
                          TextField(
                            controller: _deviceName,
                            decoration: const InputDecoration(
                              labelText: 'This device shows up as',
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (_status.isNotEmpty) ...[
                    const SizedBox(height: 16),
                    Text(
                      _status,
                      style: TextStyle(color: Theme.of(context).hintColor, fontSize: 13),
                    ),
                  ],
                  if (_found.isNotEmpty) ...[
                    const SizedBox(height: 8),
                    ..._found.map((server) => Card(
                          child: ListTile(
                            leading: const Icon(Icons.computer),
                            title: Text(server.label),
                            onTap: () {
                              setState(() {
                                _host.text = server.host;
                                if (server.tcpPort > 0) _port.text = server.tcpPort.toString();
                              });
                            },
                          ),
                        )),
                  ],
                  const SizedBox(height: 16),
                  Text(
                    'LAN only: VyralDeck never talks to the internet. Phone and PC must be on the same network.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: Theme.of(context).hintColor, fontSize: 12),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

String defaultDeviceName() => 'My Phone';
