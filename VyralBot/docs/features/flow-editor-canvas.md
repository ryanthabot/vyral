# Flow Editor Canvas (FlowCanvasControl)

The visual workflow editor's canvas layer: `src/VyralBot.UI/Controls/FlowCanvas/`
(FlowCanvasControl, FlowNodeVisual, FlowWireVisual, FlowCanvasNode,
FlowCanvasPalette) plus the canvas plumbing of
`ViewModels/FlowEditorViewModel.cs` and `Views/FlowEditorView.xaml`.

## Block palette coverage

`BlockCatalog.Blocks` (24 actions + 11 conditions) is the single source of
truth. The editor builds every palette entry from it
(`FlowEditorViewModel` ctor), the canvas renders icon/color/title from the
same definition (`FlowNodeVisual`, `FlowCanvasControl.CreateNode`), and every
`ParamField` gets a settings-panel row — `FlowParamRowViewModel` covers
text/multiline/number/boolean/select/role/channel/color; the `embed`, `rule`
and `comparisons` kinds render through the dedicated EmbedBuilder /
ConditionBuilder / ComparisonBuilder view models instead of plain rows.

## Performance notes (100+ nodes)

- **Node index:** `_nodesById` (id → `FlowCanvasNode`) is kept in sync with the
  `Nodes` collection (`OnNodesCollectionChanged`, `RebuildNodeIndex`), so
  `FindNode` is O(1). Hot paths that look targets up per wire —
  `UpdateWiresForNode`, `CreatesCycle`, `DisplaceIncomingConnections`,
  `AutoOrganizeLayout` — were O(n²) before the index. Ids are assigned in
  object initializers before `Nodes.Add` and never mutated after, so the index
  cannot drift.
- **Node drags do NOT re-render the canvas:** an X/Y `PropertyChanged` only
  moves the dragged visual and re-geometries wires that touch it
  (`UpdateWireGeometryFor`) plus the two pinned wires.
- **Frozen brushes:** the dot-grid `DrawingBrush` and each wire's base brush
  are frozen (`FlowWireVisual._baseBrush`) so WPF can share them across the
  render thread; per-node brushes stay unfrozen because hover/selection
  restyles them.
- **Architectural (not fixed):** node visuals live in a plain `Canvas` — there
  is no UI virtualization, so all nodes measure/arrange even when scrolled out
  of view. Fine to a few hundred nodes; a VirtualizingCanvas would be the next
  step. `AddNodeVisual` recomputes the trigger-card position per add
  (O(n²) over a bulk load, but each step is a cheap bounds union).

## Interaction model

- **Selection is multi-node** (`SelectedNodes`, primary = last interacted,
  exposed as `SelectedNode`; each node's `IsSelected` drives the dashed
  indigo highlight). Left-drag on empty canvas rubber-band selects every node
  whose bounds intersect the dashed `FlowCanvasSelection` rectangle
  (additive — union — while Ctrl/Shift is held; a plain left click still
  deselects). Ctrl/Shift+click toggles a single node in/out of the set;
  clicking a node inside a multi-selection without dragging keeps the set
  (collapse happens on release without movement, Windows-style). With more
  than one node selected, `NodeSelected` raises null — the settings panel has
  no single target.
- **Pan** is MIDDLE-drag (or scrollbars/wheel); the left button on empty
  canvas belongs to the rubber band.
- Dragging any selected node moves the whole selection (per-node clamping,
  wires follow live, one `GraphChanged` at drag end).
- Delete key removes ALL selected nodes in one batch (ONE `GraphChanged` so
  undo captures the whole delete). Protected attachment cards
  (`loop_after`/`loop_body`/`comparison_case`/`comparison_else`) are skipped
  by the same guard single-delete always used; component sub-nodes are
  deletable. Wires are locked: disconnect via the ⊗
  midpoint marker or by re-dragging the port.
- Ctrl+D duplicates every selected duplicatable block (+24/+24, fresh ids, no
  wires) and selects the copies; attachment cards and component sub-nodes are
  excluded from duplication
  (canvas `OnKeyDown`/`DuplicateSelected` and VM `DuplicateSelectedBlock`) —
  a component copy would be an unwired orphan.
- Ctrl+C/Ctrl+V copy/paste the selection through the same duplicate machinery
  (canvas-local clipboard — not the OS clipboard; fresh ids, config copied,
  no wires, +24/+24 offset that cascades on repeated pastes, one
  `GraphChanged` per paste, pasted nodes become the new selection).
- Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y (undo/redo) and Ctrl+S (save) are bound in
  `FlowEditorView.OnPreviewKeyDown`; undo snapshots are debounced 400 ms.
- Zoom clamps 25%–200%; wheel zooms at the cursor, Shift+wheel scrolls.
- Dirty state (`IsDirty`, title dot) is driven by `MarkDirty()` from every
  graph/config/trigger mutation, reset on save.

## Connection rules

`FlowCanvasControl.TryConnect` rejects self-connects, duplicates (silently),
cycles (DFS over `OutgoingWires`), and — since the 2026-11 block-editor
audit — wires landing on attachment cards or component sub-nodes: their only
legit inbound is the parent's programmatic drop-dot elbow
(`WireParentPort`, used by `AddAttachmentNode`/`AddComponentNode`), and a
user wire would otherwise silently displace that elbow through the
single-input rule and orphan a Delete-protected card. The pinned entry/error
card wires apply the same target check (`CompletePinnedConnect`). Input ports
hold at most one wire; a new connection displaces the previous source's
routing field.

## Theme

Every canvas surface/text role resolves through `FlowCanvasPalette`, which
reads the live `FlowCanvas*` color resources from `Themes/Colors/*.xaml`
(all 10 themes define the full key set) and falls back to compiled hex when a
key is missing. `ThemeManager.ThemeChanged` rebuilds the whole canvas
(`OnThemeChanged`) so an open editor re-themes without restart. Only the
documented semantic accents (condition green, error red, chip light tones)
are intentionally constant.
