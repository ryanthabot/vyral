import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:vyral_deck/main.dart';

void main() {
  testWidgets('app builds to the connect screen', (WidgetTester tester) async {
    SharedPreferences.setMockInitialValues({});
    await tester.pumpWidget(const VyralDeckApp());
    await tester.pumpAndSettle();
    expect(find.text('VyralDeck'), findsOneWidget);
  });
}
