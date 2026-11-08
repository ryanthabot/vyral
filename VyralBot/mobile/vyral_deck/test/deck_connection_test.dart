import 'package:flutter_test/flutter_test.dart';

import 'package:vyral_deck/services/deck_connection.dart';

void main() {
  group('parseLiveState', () {
    final connection = DeckConnection('127.0.0.1', 5199);

    test('keeps platform bools', () {
      final state = connection.parseLiveState({
        'Twitch': true,
        'Kick': false,
        'YouTube': false,
      });
      expect(state['Twitch'], isTrue);
      expect(state['Kick'], isFalse);
      expect(state['YouTube'], isFalse);
    });

    test('keeps stream-context strings alongside the flags', () {
      final state = connection.parseLiveState({
        'Twitch': true,
        'Kick': false,
        'YouTube': false,
        'nowPlaying': 'Never Gonna Give You Up',
        'uptimeText': '2h 14m',
      });
      expect(state['Twitch'], isTrue);
      expect(state['nowPlaying'], 'Never Gonna Give You Up');
      expect(state['uptimeText'], '2h 14m');
      expect(state.containsKey('deathCount'), isFalse);
    });

    test('tolerates absent or non-map payloads', () {
      expect(connection.parseLiveState(null), isEmpty);
      expect(connection.parseLiveState('nope'), isEmpty);
    });
  });
}
