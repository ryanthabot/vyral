import 'package:flutter/material.dart';

import '../core/action_catalog.dart';
import '../core/models.dart';

/// Bottom sheet that customizes one deck button (label, emoji icon, color,
/// action + parameters). Returns the updated button, or null when cancelled.
Future<DeckButtonData?> showButtonEditor(
  BuildContext context,
  List<ActionInfo> catalog,
  DeckButtonData original,
) {
  return showModalBottomSheet<DeckButtonData>(
    context: context,
    isScrollControlled: true,
    builder: (context) => _ButtonEditor(catalog: catalog, original: original),
  );
}

class _ButtonEditor extends StatefulWidget {
  const _ButtonEditor({required this.catalog, required this.original});

  final List<ActionInfo> catalog;
  final DeckButtonData original;

  @override
  State<_ButtonEditor> createState() => _ButtonEditorState();
}

class _ButtonEditorState extends State<_ButtonEditor> {
  late final TextEditingController _label;
  late final TextEditingController _icon;
  late final TextEditingController _param1;
  late final TextEditingController _param2;
  late ActionInfo _action;
  late String _color;

  @override
  void initState() {
    super.initState();
    final original = widget.original;
    _label = TextEditingController(text: original.label);
    _icon = TextEditingController(text: original.icon);
    _param1 = TextEditingController(text: original.param1);
    _param2 = TextEditingController(text: original.param2);
    _action = infoFor(widget.catalog, original.actionType);
    _color = original.isEmpty ? colorChoices.first : original.colorHex;
  }

  @override
  void dispose() {
    _label.dispose();
    _icon.dispose();
    _param1.dispose();
    _param2.dispose();
    super.dispose();
  }

  ActionInfo? _matchingCatalogEntry(String type) {
    for (final info in widget.catalog) {
      if (info.type == type) return info;
    }
    // Not in the catalog (older server) — no dropdown selection instead of an
    // invalid `value` assertion.
    return null;
  }

  void _save() {
    final label = _label.text.trim();
    if (label.isEmpty) {
      Navigator.of(context).pop();
      return;
    }
    Navigator.of(context).pop(DeckButtonData(
      id: widget.original.id,
      label: label,
      icon: _icon.text.trim(),
      colorHex: _color,
      actionType: _action.type,
      param1: _action.param1Label == null ? '' : _param1.text.trim(),
      param2: _action.param2Label == null ? '' : _param2.text.trim(),
    ));
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.only(
        bottom: MediaQuery.of(context).viewInsets.bottom,
      ),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                const Icon(Icons.edit, size: 20),
                const SizedBox(width: 8),
                Text('Edit "${widget.original.isEmpty ? 'empty slot' : widget.original.label}"',
                    style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
              ],
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _label,
              autofocus: widget.original.isEmpty,
              decoration: const InputDecoration(labelText: 'Label', hintText: 'e.g. Start Stream'),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _icon,
              decoration: const InputDecoration(labelText: 'Icon (emoji or short text)', hintText: 'e.g. ▶️'),
            ),
            const SizedBox(height: 16),
            const Text('COLOR', style: TextStyle(fontSize: 11, letterSpacing: 1)),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: colorChoices.map((hex) {
                final selected = hex == _color;
                return GestureDetector(
                  onTap: () => setState(() => _color = hex),
                  child: Container(
                    width: 36,
                    height: 36,
                    decoration: BoxDecoration(
                      color: _parseColor(hex),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(
                        color: selected ? Colors.white : Colors.white24,
                        width: selected ? 3 : 1,
                      ),
                    ),
                  ),
                );
              }).toList(),
            ),
            const SizedBox(height: 16),
            DropdownButtonFormField<ActionInfo>(
              initialValue: _matchingCatalogEntry(_action.type),              items: widget.catalog
                  .map((info) => DropdownMenuItem(
                        value: info,
                        child: Text(info.name, overflow: TextOverflow.ellipsis),
                      ))
                  .toList(),
              onChanged: (value) => setState(() => _action = value ?? _action),
              decoration: const InputDecoration(labelText: 'Action'),
            ),
            if (_action.param1Label != null) ...[
              const SizedBox(height: 12),
              TextField(
                controller: _param1,
                decoration: InputDecoration(
                  labelText: _action.param1Label,
                  hintText: _action.param1Placeholder,
                ),
              ),
            ],
            if (_action.param2Label != null) ...[
              const SizedBox(height: 12),
              TextField(
                controller: _param2,
                decoration: InputDecoration(
                  labelText: _action.param2Label,
                  hintText: _action.param2Placeholder,
                ),
              ),
            ],
            const SizedBox(height: 20),
            Row(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed: _label.text.trim().isEmpty ? null : _save,
                    icon: const Icon(Icons.check),
                    label: const Text('SAVE'),
                  ),
                ),
                const SizedBox(width: 12),
                TextButton(
                  onPressed: () => Navigator.of(context).pop(),
                  child: const Text('Cancel'),
                ),
              ],
            ),
          ],
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
