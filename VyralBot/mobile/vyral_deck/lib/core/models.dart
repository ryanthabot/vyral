/// Deck data model — mirrors the desktop `DeckLayout` JSON exactly
/// (see VyralBot.UI/Services/VyralDeck/DeckModels.cs and PROTOCOL.md).
library;

import 'dart:math';

const int slotsPerPage = 12;

/// Deck protocol version this app understands (see PROTOCOL.md and
/// DeckServer.ProtocolVersion). The server reports its own version in the
/// pair response and the `welcome` frame; a NEWER server version is surfaced
/// as a non-blocking "update the app" hint, never a hard failure.
const int knownProtocolVersion = 1;

class DeckButtonData {
  DeckButtonData({
    required this.id,
    this.label = '',
    this.icon = '',
    this.colorHex = '#14B8A6',
    this.actionType = 'ObsScene',
    this.param1 = '',
    this.param2 = '',
  });

  String id;
  String label;
  String icon;
  String colorHex;
  String actionType;
  String param1;
  String param2;

  bool get isEmpty => label.trim().isEmpty;

  factory DeckButtonData.empty() => DeckButtonData(id: _newId());

  factory DeckButtonData.fromJson(Map<String, dynamic> json) => DeckButtonData(
        id: (json['id'] ?? _newId()).toString(),
        label: (json['label'] ?? '').toString(),
        icon: (json['icon'] ?? '').toString(),
        colorHex: (json['colorHex'] ?? '#14B8A6').toString(),
        actionType: (json['actionType'] ?? 'ObsScene').toString(),
        param1: (json['param1'] ?? '').toString(),
        param2: (json['param2'] ?? '').toString(),
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'label': label,
        'icon': icon,
        'colorHex': colorHex,
        'actionType': actionType,
        'param1': param1,
        'param2': param2,
      };

  DeckButtonData copy() => DeckButtonData(
        id: id,
        label: label,
        icon: icon,
        colorHex: colorHex,
        actionType: actionType,
        param1: param1,
        param2: param2,
      );
}

class DeckPageData {
  DeckPageData({required this.id, required this.name, required this.buttons});

  String id;
  String name;
  List<DeckButtonData> buttons;

  factory DeckPageData.empty({String name = 'Page 1'}) => DeckPageData(
        id: _newId(),
        name: name,
        buttons: List.generate(slotsPerPage, (_) => DeckButtonData.empty()),
      );

  factory DeckPageData.fromJson(Map<String, dynamic> json) {
    final rawButtons = (json['buttons'] as List<dynamic>? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(DeckButtonData.fromJson)
        .toList();
    // Keep the desktop invariant: exactly 12 slots per page.
    while (rawButtons.length < slotsPerPage) {
      rawButtons.add(DeckButtonData.empty());
    }
    if (rawButtons.length > slotsPerPage) {
      rawButtons.removeRange(slotsPerPage, rawButtons.length);
    }
    final name = (json['name'] ?? 'Page').toString();
    return DeckPageData(
      id: (json['id'] ?? _newId()).toString(),
      name: name.isEmpty ? 'Page' : name,
      buttons: rawButtons,
    );
  }

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'buttons': buttons.map((b) => b.toJson()).toList(),
      };
}

class DeckLayoutData {
  DeckLayoutData({required this.pages});

  List<DeckPageData> pages;

  factory DeckLayoutData.empty() =>
      DeckLayoutData(pages: [DeckPageData.empty()]);

  static DeckLayoutData? tryParse(Map<String, dynamic> json) {
    final rawPages = json['pages'];
    if (rawPages is! List<dynamic>) return null;
    final pages = rawPages
        .whereType<Map<String, dynamic>>()
        .map(DeckPageData.fromJson)
        .toList();
    if (pages.isEmpty) return null;
    return DeckLayoutData(pages: pages);
  }

  Map<String, dynamic> toJson() => {
        'pages': pages.map((p) => p.toJson()).toList(),
      };

  DeckButtonData? findButton(String buttonId) {
    for (final page in pages) {
      for (final button in page.buttons) {
        if (button.id == buttonId) return button;
      }
    }
    return null;
  }

  DeckLayoutData copy() => DeckLayoutData(
        pages: pages
            .map((p) => DeckPageData(
                  id: p.id,
                  name: p.name,
                  buttons: p.buttons.map((b) => b.copy()).toList(),
                ))
            .toList(),
      );
}

final Random _idRandom = Random.secure();

/// Random UUID v4 — collision-free even when several ids are minted in the
/// same microsecond (empty pages pre-create 12 button ids at once). Matches
/// the GUID ids the desktop generates; no extra package needed.
String _newId() {
  final bytes = List<int>.generate(16, (_) => _idRandom.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // RFC 4122 variant
  final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
      '${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}
