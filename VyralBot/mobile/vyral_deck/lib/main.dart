import 'package:flutter/material.dart';

import 'screens/connect_screen.dart';

void main() {
  runApp(const VyralDeckApp());
}

class VyralDeckApp extends StatelessWidget {
  const VyralDeckApp({super.key});

  @override
  Widget build(BuildContext context) {
    // Same dark-teal identity as the desktop VyralBot app.
    const seed = Color(0xFF14B8A6);
    return MaterialApp(
      title: 'VyralDeck',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        useMaterial3: true,
        brightness: Brightness.dark,
        colorScheme: ColorScheme.fromSeed(
          seedColor: seed,
          brightness: Brightness.dark,
          surface: const Color(0xFF161B22),
        ),
        scaffoldBackgroundColor: const Color(0xFF0B0F14),
        inputDecorationTheme: const InputDecorationTheme(
          border: OutlineInputBorder(),
          isDense: true,
        ),
      ),
      home: const ConnectScreen(),
    );
  }
}
