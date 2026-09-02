using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using VyralBot.Workflows.Abstractions;

namespace VyralBot.UI.Controls.FlowCanvas;

/// <summary>
/// Canvas-side view model for one workflow node. Mirrors the shape of
/// <see cref="VyralBot.Workflows.Models.WorkflowNode"/> (Id, Type, Config,
/// routing fields, X/Y) so the flow editor can copy in both directions, while
/// raising <see cref="PropertyChanged"/> for every field the canvas cares
/// about — in particular X/Y so wires follow node drags live.
/// </summary>
public class FlowCanvasNode : INotifyPropertyChanged
{
    /// <summary>Prefix of the derived component drop-point port keys ("case:button0", "case:button1", ...).</summary>
    public const string ComponentButtonPortPrefix = "case:button";

    /// <summary>Port key of the (single) select-menu drop point under a message node.</summary>
    public const string ComponentMenuPortKey = "case:menu0";
    private string _id = Guid.NewGuid().ToString("N");
    private string _blockType = string.Empty;
    private string _label = string.Empty;
    private double _x;
    private double _y;
    private Dictionary<string, string> _config = new();
    private string? _nextTargetId;
    private string? _branchTrueTargetId;
    private string? _branchFalseTargetId;
    private string? _branchErrorTargetId;
    private Dictionary<string, string>? _branchTargets;
    private bool _isSelected;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Unique node id (GUID "N" string); referenced by other nodes' routing fields.</summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>BlockCatalog block type key (e.g. "send_message", "if", "switch").</summary>
    public string BlockType { get => _blockType; set => Set(ref _blockType, value); }

    /// <summary>User-editable label shown on the canvas node (defaults to the block title).</summary>
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>Canvas X position (world coordinates); wires follow live while dragging.</summary>
    public double X { get => _x; set => Set(ref _x, value); }

    /// <summary>Canvas Y position (world coordinates); wires follow live while dragging.</summary>
    public double Y { get => _y; set => Set(ref _y, value); }

    /// <summary>Block settings as string key/values (WorkflowNode.Config mirror).</summary>
    public Dictionary<string, string> Config
    {
        get => _config;
        set => Set(ref _config, value ?? new Dictionary<string, string>());
    }

    /// <summary>Target node id wired to the "next" output port (null = unwired).</summary>
    public string? NextTargetId { get => _nextTargetId; set => Set(ref _nextTargetId, value); }

    /// <summary>Target node id wired to the condition "true" output port (null = unwired).</summary>
    public string? BranchTrueTargetId { get => _branchTrueTargetId; set => Set(ref _branchTrueTargetId, value); }

    /// <summary>Target node id wired to the condition "false" output port (null = unwired).</summary>
    public string? BranchFalseTargetId { get => _branchFalseTargetId; set => Set(ref _branchFalseTargetId, value); }

    /// <summary>Target node id wired to the "error" output port (null = unwired).</summary>
    public string? BranchErrorTargetId { get => _branchErrorTargetId; set => Set(ref _branchErrorTargetId, value); }

    /// <summary>Per-case routing for "switch" nodes: case value → target node id.</summary>
    public Dictionary<string, string>? BranchTargets
    {
        get => _branchTargets;
        set => Set(ref _branchTargets, value);
    }

    /// <summary>Visual selection state (blue highlight on the canvas).</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>
    /// Enumerates the node's current outgoing wires as (port key, target id or
    /// null) pairs — the exact shape the editor copies into
    /// <see cref="VyralBot.Workflows.Models.WorkflowNode"/> routing fields.
    /// Port keys are "next", "true", "false", "error", "default" or
    /// "case:&lt;value&gt;". Note the switch "default" port routes through
    /// <see cref="BranchFalseTargetId"/> (runtime fallback semantics).
    /// </summary>
    public IEnumerable<KeyValuePair<string, string?>> OutgoingWires
    {
        get
        {
            HashSet<string> declared = new(StringComparer.Ordinal);
            foreach (PortDescriptor port in GetOutputPorts())
            {
                if (declared.Add(port.Key))
                    yield return new KeyValuePair<string, string?>(port.Key, GetTarget(port.Key));
            }

            // Keep an explicitly-set error routing visible even when the block
            // does not declare an error port (host-authored BranchError).
            if (!declared.Contains(BlockCatalog.PortError) && _branchErrorTargetId is not null)
                yield return new KeyValuePair<string, string?>(BlockCatalog.PortError, _branchErrorTargetId);
        }
    }

    /// <summary>
    /// Output ports of this node: the static <see cref="BlockCatalog.GetOutputs"/>
    /// list — which ALWAYS renders in full, regardless of Config (so e.g. the
    /// "loop" block keeps its static "After loop" ("next") and "Loop times"
    /// ("case:body") ports even without a Cases config) — plus derived ports:
    /// for "switch" nodes one additional port per case value parsed from
    /// Config["Cases"] (comma-separated) with key "case:&lt;value&gt;", and
    /// for message blocks carrying components one grey DROP-POINT port per
    /// configured button ("case:button0", "case:button1", ...) plus
    /// "case:menu0" when a Menu config exists (all routed through
    /// <see cref="BranchTargets"/> like any other case port). The derivation
    /// only ever adds ports; it never gates the static ones.
    /// </summary>
    public IReadOnlyList<PortDescriptor> GetOutputPorts()
    {
        List<PortDescriptor> ports = new(BlockCatalog.GetOutputs(BlockType));

        if (string.Equals(BlockType, "switch", StringComparison.OrdinalIgnoreCase)
            && _config.TryGetValue("Cases", out string? raw)
            && !string.IsNullOrWhiteSpace(raw))
        {
            foreach (string token in raw.Split(','))
            {
                string value = token.Trim();
                if (value.Length == 0)
                    continue;

                string key = BlockCatalog.PortCasePrefix + value;
                bool exists = false;
                foreach (PortDescriptor existing in ports)
                {
                    if (string.Equals(existing.Key, key, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    ports.Add(new PortDescriptor(key, value, FlowCanvasPalette.CasePortColor));
            }
        }

        // Comparison multi-rule mode: when Config["Comparisons"] holds a rule
        // array (an EMPTY "[]" array included — freshly placed comparisons are
        // seeded with it), the legacy True/False ports are REPLACED by one grey
        // port per rule ("case:{i}" — stored verbatim, see GetTarget/SetTarget)
        // plus the "else" port. Zero rules renders just the "else" port (the
        // auto-attached Else card hangs off it); a missing/blank/malformed/
        // non-array key keeps the legacy True/False ports.
        if (string.Equals(BlockType, "comparison", StringComparison.OrdinalIgnoreCase)
            && ComparisonIsMultiRule(_config))
        {
            ports.Clear();
            int rules = ComparisonRuleCount(_config);
            for (int i = 0; i < rules; i++)
                ports.Add(new PortDescriptor(BlockCatalog.PortCasePrefix + i, $"Rule {i + 1}", FlowCanvasPalette.PortNeutral));
            ports.Add(new PortDescriptor(ComparisonElsePortKey, "Else", FlowCanvasPalette.PortNeutral));
        }

        // Component drop points ("⊕ Button" / "⊕ Menu" chips): one grey port
        // per configured button plus the menu port, appended AFTER the static
        // ports so the main-flow port keeps its spot in the render order.
        if (!FlowNodeVisual.IsComponentType(BlockType)
            && !FlowNodeVisual.IsAttachmentCardType(BlockType))
        {
            int buttonCount = ComponentButtonCount(_config);
            for (int i = 0; i < buttonCount; i++)
                ports.Add(new PortDescriptor(ComponentButtonPortPrefix + i, $"Button {i + 1}", FlowCanvasPalette.PortNeutral));

            if (ComponentHasMenu(_config))
                ports.Add(new PortDescriptor(ComponentMenuPortKey, "Menu", FlowCanvasPalette.PortNeutral));
        }

        return ports;
    }

    /// <summary>
    /// True when <paramref name="portKey"/> is one of the derived component
    /// drop-point ports ("case:buttonN" / "case:menu0") — these render as grey
    /// drop dots under the card (not in the regular bottom port row).
    /// </summary>
    /// <summary>Output port key for a comparison node's Else branch (stored verbatim in BranchTargets).</summary>
    public const string ComparisonElsePortKey = "else";

    public static bool IsComponentSlotPort(string? portKey) =>
        portKey != null
        && (portKey.StartsWith(ComponentButtonPortPrefix, StringComparison.Ordinal)
            || string.Equals(portKey, ComponentMenuPortKey, StringComparison.Ordinal));

    /// <summary>
    /// True when Config["Comparisons"] switches a comparison node to multi-rule
    /// mode: the key is present, non-blank and parses as a JSON array — an
    /// EMPTY array ("[]") included. Mirrors the engine's
    /// ComparisonNode.ParseComparisonRules: absent/blank/malformed/non-array
    /// keeps the legacy single-rule (True/False) mode.
    /// </summary>
    public static bool ComparisonIsMultiRule(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("Comparisons", out string? raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        string trimmed = raw.Trim();
        if (trimmed == "null")
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Number of rules in a comparison node's "Comparisons" JSON config (0 when absent/empty/invalid).</summary>
    public static int ComparisonRuleCount(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("Comparisons", out string? raw) || string.IsNullOrWhiteSpace(raw))
            return 0;

        string trimmed = raw.Trim();
        if (trimmed == "[]" || trimmed == "null")
            return 0;

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Number of buttons in a "Buttons" JSON config (0 when absent/empty/invalid).</summary>
    public static int ComponentButtonCount(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("Buttons", out string? raw) || string.IsNullOrWhiteSpace(raw))
            return 0;

        string trimmed = raw.Trim();
        if (trimmed == "[]" || trimmed == "null")
            return 0;

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>True when the node's Config carries a non-empty "Menu" JSON object.</summary>
    public static bool ComponentHasMenu(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("Menu", out string? raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        string trimmed = raw.Trim();
        return trimmed != "{}" && trimmed != "null";
    }

    /// <summary>
    /// Reads the target node id currently wired to a port key ("next", "true",
    /// "false", "error", "default", "case:&lt;value&gt;"). Unknown keys return null.
    /// </summary>
    public string? GetTarget(string portKey)
    {
        if (string.Equals(portKey, BlockCatalog.PortNext, StringComparison.Ordinal))
            return NextTargetId;
        if (string.Equals(portKey, BlockCatalog.PortTrue, StringComparison.Ordinal))
            return BranchTrueTargetId;
        if (string.Equals(portKey, BlockCatalog.PortFalse, StringComparison.Ordinal))
            return BranchFalseTargetId;
        if (string.Equals(portKey, BlockCatalog.PortError, StringComparison.Ordinal))
            return BranchErrorTargetId;
        if (string.Equals(portKey, BlockCatalog.PortDefault, StringComparison.Ordinal))
            return BranchFalseTargetId;
        if (string.Equals(portKey, ComparisonElsePortKey, StringComparison.Ordinal))
            return _branchTargets != null && _branchTargets.TryGetValue(ComparisonElsePortKey, out string? elseTarget) ? elseTarget : null;

        // Comparison nodes store the FULL "case:{i}" key (the engine's
        // multi-rule routing reads Branches["case:{i}"] verbatim); every other
        // type keeps the switch-style stripped value.
        if (string.Equals(BlockType, "comparison", StringComparison.OrdinalIgnoreCase)
            && portKey.StartsWith(BlockCatalog.PortCasePrefix, StringComparison.Ordinal))
            return _branchTargets != null && _branchTargets.TryGetValue(portKey, out string? caseTarget) ? caseTarget : null;

        if (portKey.StartsWith(BlockCatalog.PortCasePrefix, StringComparison.Ordinal))
        {
            string caseValue = portKey[BlockCatalog.PortCasePrefix.Length..];
            return _branchTargets != null && _branchTargets.TryGetValue(caseValue, out string? target) ? target : null;
        }

        return null;
    }

    /// <summary>
    /// Wires (or clears, with null) the output port identified by key.
    /// "default" maps to <see cref="BranchFalseTargetId"/> (the switch default
    /// falls back to the false branch at runtime); "case:&lt;value&gt;" maps
    /// into <see cref="BranchTargets"/>. Unknown keys are ignored.
    /// </summary>
    public void SetTarget(string portKey, string? targetId)
    {
        if (string.Equals(portKey, BlockCatalog.PortNext, StringComparison.Ordinal))
            NextTargetId = targetId;
        else if (string.Equals(portKey, BlockCatalog.PortTrue, StringComparison.Ordinal))
            BranchTrueTargetId = targetId;
        else if (string.Equals(portKey, BlockCatalog.PortFalse, StringComparison.Ordinal))
            BranchFalseTargetId = targetId;
        else if (string.Equals(portKey, BlockCatalog.PortError, StringComparison.Ordinal))
            BranchErrorTargetId = targetId;
        else if (string.Equals(portKey, BlockCatalog.PortDefault, StringComparison.Ordinal))
            BranchFalseTargetId = targetId;
        else if (string.Equals(portKey, ComparisonElsePortKey, StringComparison.Ordinal))
        {
            Dictionary<string, string> elseTargets = _branchTargets ??= new Dictionary<string, string>(StringComparer.Ordinal);
            if (targetId == null)
                elseTargets.Remove(ComparisonElsePortKey);
            else
                elseTargets[ComparisonElsePortKey] = targetId;
            _branchTargets = elseTargets.Count == 0 ? null : elseTargets;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BranchTargets)));
        }
        else if (string.Equals(BlockType, "comparison", StringComparison.OrdinalIgnoreCase)
            && portKey.StartsWith(BlockCatalog.PortCasePrefix, StringComparison.Ordinal))
        {
            Dictionary<string, string> compTargets = _branchTargets ??= new Dictionary<string, string>(StringComparer.Ordinal);
            compTargets[portKey] = targetId;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BranchTargets)));
        }
        else if (portKey.StartsWith(BlockCatalog.PortCasePrefix, StringComparison.Ordinal))
        {
            string caseValue = portKey[BlockCatalog.PortCasePrefix.Length..];
            if (targetId == null)
            {
                if (_branchTargets != null && _branchTargets.Remove(caseValue))
                {
                    // The (mutated) dictionary instance is often unchanged, which
                    // Set's equality check would skip — raise explicitly instead.
                    _branchTargets = _branchTargets.Count == 0 ? null : _branchTargets;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BranchTargets)));
                }
            }
            else
            {
                Dictionary<string, string> targets = _branchTargets ??= new Dictionary<string, string>(StringComparer.Ordinal);
                targets[caseValue] = targetId;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BranchTargets)));
            }
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
