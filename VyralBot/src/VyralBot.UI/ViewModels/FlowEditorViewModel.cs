using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Moderation;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.UI.Controls.FlowCanvas;
using VyralBot.UI.Services;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;

namespace VyralBot.UI.ViewModels;

// =====================================================================================
// Small supporting view models (trigger / options / palette / settings-panel rows)
// =====================================================================================

/// <summary>One entry of the trigger-type dropdown in the flow editor toolbar.</summary>
public class FlowTriggerTypeOption
{
    /// <summary>Raw <see cref="WorkflowTriggerTypes"/> value stored on the workflow.</summary>
    public string Key { get; }

    /// <summary>Human label shown in the dropdown.</summary>
    public string Label { get; }

    public FlowTriggerTypeOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public override string ToString() => Label;
}

/// <summary>One entry of the command-option type dropdown (cooldown, permissions, scoping).</summary>
public class FlowOptionTypeOption
{
    /// <summary>Raw <see cref="WorkflowOptionTypes"/> value.</summary>
    public string Key { get; }

    /// <summary>Human label shown in the dropdown.</summary>
    public string Label { get; }

    /// <summary>Watermark hint for the matching value box.</summary>
    public string ValueHint { get; }

    public FlowOptionTypeOption(string key, string label, string valueHint)
    {
        Key = key;
        Label = label;
        ValueHint = valueHint;
    }

    public override string ToString() => Label;
}

/// <summary>One editable row of the "Options…" popup: a constraint type + its value.</summary>
public partial class FlowOptionRowViewModel : ObservableObject
{
    [ObservableProperty]
    private FlowOptionTypeOption _type;

    [ObservableProperty]
    private string _value = string.Empty;

    public FlowOptionRowViewModel(FlowOptionTypeOption type, string value)
    {
        _type = type;
        _value = value;
    }

    /// <summary>True when the type needs no value (DM only / guild only / NSFW flags).</summary>
    public bool IsFlagOnly =>
        Type.Key == WorkflowOptionTypes.DmOnly ||
        Type.Key == WorkflowOptionTypes.GuildOnly ||
        Type.Key == WorkflowOptionTypes.Nsfw;

    /// <summary>
    /// "Label: value" text for the purple options chips bar. The label is
    /// resolved from the palette's <see cref="BlockCatalog.OptionDefinitions"/>
    /// (purple Options tab) with the option-type dropdown label as fallback.
    /// </summary>
    public string ChipText
    {
        get
        {
            string label = BlockCatalog.OptionDefinitions
                               .FirstOrDefault(d => string.Equals(d.Type, Type.Key, StringComparison.OrdinalIgnoreCase))
                               ?.Label
                           ?? Type.Label;
            string value = IsFlagOnly
                ? "on"
                : string.IsNullOrWhiteSpace(Value) ? "set value…" : Value.Trim();
            return $"{label}: {value}";
        }
    }

    partial void OnTypeChanged(FlowOptionTypeOption value)
    {
        OnPropertyChanged(nameof(IsFlagOnly));
        OnPropertyChanged(nameof(ChipText));
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(ChipText));
}

/// <summary>One block entry in the editor palette (drag source / double-click source).</summary>
public class FlowPaletteItemViewModel
{
    /// <summary>The catalog definition (icon, title, description, accent color).</summary>
    public BlockDefinition Block { get; }

    /// <summary>Accent color as a brush for the palette entry's left edge.</summary>
    public Brush AccentBrush { get; }

    public FlowPaletteItemViewModel(BlockDefinition block)
    {
        Block = block;

        try
        {
            AccentBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(block.Color) ? "#3B82F6" : block.Color));
        }
        catch (FormatException)
        {
            AccentBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        }
    }
}

/// <summary>
/// One row of the BotGhost-style grouped palette list: either a bold group
/// header (emitted whenever <see cref="BlockDefinition.Section"/> changes
/// value) or a draggable block entry. Lets a single ItemsControl render both.
/// </summary>
public class FlowPaletteRowViewModel
{
    /// <summary>True for a bold group-header row (no block behind it).</summary>
    public bool IsHeader { get; }

    /// <summary>Group header text (e.g. "Message Actions").</summary>
    public string HeaderTitle { get; }

    /// <summary>The palette entry (null on header rows).</summary>
    public FlowPaletteItemViewModel? Item { get; }

    private FlowPaletteRowViewModel(bool isHeader, string headerTitle, FlowPaletteItemViewModel? item)
    {
        IsHeader = isHeader;
        HeaderTitle = headerTitle;
        Item = item;
    }

    public static FlowPaletteRowViewModel Header(string title) => new(true, title, null);

    public static FlowPaletteRowViewModel For(FlowPaletteItemViewModel item) => new(false, string.Empty, item);

    /// <summary>Catalog definition of the row's block (header rows return null!).</summary>
    public BlockDefinition? Block => Item?.Block;

    /// <summary>Accent brush of the row's block (header rows return null!).</summary>
    public Brush? AccentBrush => Item?.AccentBrush;
}

/// <summary>One parsed "Value|Label" choice of a Select parameter field.</summary>
public class FlowSelectChoice
{
    /// <summary>Value stored in the node config.</summary>
    public string Value { get; }

    /// <summary>Label shown in the dropdown (falls back to the value).</summary>
    public string Label { get; }

    public FlowSelectChoice(string value, string label)
    {
        Value = value;
        Label = string.IsNullOrWhiteSpace(label) ? value : label;
    }

    public override string ToString() => Label;
}

/// <summary>
/// One settings-panel parameter row for the selected canvas node. Every field
/// writes straight through into <see cref="FlowCanvasNode.Config"/> (string
/// values so half-typed input never crashes), mirroring the
/// <see cref="WorkflowStepViewModel"/> config-backed-fields pattern.
/// </summary>
public partial class FlowParamRowViewModel : ObservableObject
{
    private readonly FlowCanvasNode _node;
    private readonly Action<string?> _onConfigChanged;

    /// <summary>False while the ctor fills fields, so load-time assignments never write back.</summary>
    private bool _ready;

    /// <summary>WorkflowNode.Config key this row writes.</summary>
    public string Key { get; }

    /// <summary>Field label shown in the panel.</summary>
    public string Label { get; }

    /// <summary>One of <see cref="ParamKind"/>.</summary>
    public string Kind { get; }

    /// <summary>Hint text under/inside the field.</summary>
    public string? Hint { get; }

    /// <summary>Choices for Select fields.</summary>
    public ObservableCollection<FlowSelectChoice> Choices { get; } = new();

    /// <summary>Options for role/channel fields (lazily fetched; often empty).</summary>
    public ObservableCollection<string> RemoteOptions { get; } = new();

    [ObservableProperty]
    private string _textValue = string.Empty;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private FlowSelectChoice? _selectedChoice;

    [ObservableProperty]
    private Brush _colorBrush = Brushes.Gray;

    // Per-kind visibility flags (drive the per-kind field rendering, same
    // pattern as the AutomationView step rows).

    public bool IsText => Kind == ParamKind.Text;
    public bool IsMultiline => Kind == ParamKind.Multiline;
    public bool IsNumber => Kind == ParamKind.Number;
    public bool IsBoolean => Kind == ParamKind.Boolean;
    public bool IsSelect => Kind == ParamKind.Select;
    public bool IsRole => Kind == ParamKind.Role;
    public bool IsChannel => Kind == ParamKind.Channel;
    public bool IsColor => Kind == ParamKind.Color;
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);
    public bool HasChoices => Choices.Count > 0;

    public FlowParamRowViewModel(
        ParamField field,
        FlowCanvasNode node,
        Action<string?> onConfigChanged,
        IReadOnlyList<string>? remoteOptions = null)
    {
        Key = field.Key;
        Label = field.Label;
        Kind = field.Kind;
        Hint = field.Hint;
        _node = node;
        _onConfigChanged = onConfigChanged;

        if (field.Choices != null)
        {
            foreach (string choice in field.Choices)
            {
                string[] parts = choice.Split('|', 2);
                Choices.Add(new FlowSelectChoice(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : string.Empty));
            }
        }

        node.Config.TryGetValue(Key, out string? current);
        current ??= string.Empty;

        switch (Kind)
        {
            case ParamKind.Boolean:
                _boolValue = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
                break;

            case ParamKind.Select:
                _selectedChoice = Choices.FirstOrDefault(c => string.Equals(c.Value, current, StringComparison.Ordinal))
                                  ?? Choices.FirstOrDefault();
                break;

            default:
                _textValue = current;
                break;
        }

        if (Kind == ParamKind.Color)
        {
            RefreshColorBrush();
        }

        if (remoteOptions != null)
        {
            foreach (string option in remoteOptions)
            {
                RemoteOptions.Add(option);
            }
        }

        _ready = true;
    }

    partial void OnTextValueChanged(string value)
    {
        if (!_ready) return;
        WriteConfig(value);
        if (IsColor)
        {
            RefreshColorBrush();
        }
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (!_ready) return;
        WriteConfig(value ? "true" : "false");
    }

    partial void OnSelectedChoiceChanged(FlowSelectChoice? value)
    {
        if (!_ready) return;
        if (value != null)
        {
            WriteConfig(value.Value);
        }
    }

    /// <summary>Preset swatch click (Discord palette): writes the hex directly.</summary>
    [RelayCommand]
    private void ApplySwatch(string hex)
    {
        _ready = true; // swatches are user actions only
        TextValue = hex; // setter writes config + refreshes the swatch
    }

    private void WriteConfig(string value)
    {
        _node.Config[Key] = value ?? string.Empty;
        _onConfigChanged(Key);
    }

    private void RefreshColorBrush()
    {
        try
        {
            Color color = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(TextValue) ? "#555555" : TextValue.Trim());
            ColorBrush = new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            ColorBrush = Brushes.Gray;
        }
    }
}

/// <summary>One row of the embed builder's fields list (name / value / inline).</summary>
public partial class EmbedFieldRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _inline;

    /// <summary>Raised whenever any field changes so the embed re-serializes.</summary>
    public event EventHandler? Changed;

    public EmbedFieldRowViewModel() { }

    public EmbedFieldRowViewModel(string name, string value, bool inline)
    {
        _name = name;
        _value = value;
        _inline = inline;
    }

    partial void OnNameChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnValueChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnInlineChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// The embed builder bound to a node's Config["Embed"] JSON. Every edit
/// re-serializes into the node config and refreshes the live preview bindings
/// (title/description/footer/colors/fields are bound directly).
/// </summary>
public partial class EmbedBuilderViewModel : ObservableObject
{
    private readonly FlowCanvasNode _node;
    private readonly Action<string?> _onConfigChanged;
    private bool _loading;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _colorHex = "#5865F2";

    [ObservableProperty]
    private string _footer = string.Empty;

    [ObservableProperty]
    private string _imageUrl = string.Empty;

    [ObservableProperty]
    private string _thumbnailUrl = string.Empty;

    [ObservableProperty]
    private string _authorName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<EmbedFieldRowViewModel> _fields = new();

    [ObservableProperty]
    private Brush _previewBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));

    public EmbedBuilderViewModel(FlowCanvasNode node, Action<string?> onConfigChanged)
    {
        _node = node;
        _onConfigChanged = onConfigChanged;
        LoadFromConfig();

        // Keep re-serializing when rows are added/removed/edited.
        Fields.CollectionChanged += (_, _) => Serialize();
    }

    partial void OnTitleChanged(string value) => Serialize();
    partial void OnDescriptionChanged(string value) => Serialize();
    partial void OnColorHexChanged(string value)
    {
        RefreshPreviewBrush();
        Serialize();
    }
    partial void OnFooterChanged(string value) => Serialize();
    partial void OnImageUrlChanged(string value) => Serialize();
    partial void OnThumbnailUrlChanged(string value) => Serialize();
    partial void OnAuthorNameChanged(string value) => Serialize();

    [RelayCommand]
    private void AddField()
    {
        AddFieldRow(new EmbedFieldRowViewModel());
    }

    /// <summary>Adds a field row and hooks its edit event so edits re-serialize.</summary>
    private void AddFieldRow(EmbedFieldRowViewModel row)
    {
        row.Changed += (_, _) => Serialize();
        Fields.Add(row);
    }

    [RelayCommand]
    private void RemoveField(EmbedFieldRowViewModel? row)
    {
        if (row != null)
        {
            Fields.Remove(row);
        }
    }

    /// <summary>Preset swatch click (Discord palette).</summary>
    [RelayCommand]
    private void ApplySwatch(string hex)
    {
        ColorHex = hex;
    }

    private void RefreshPreviewBrush()
    {
        try
        {
            Color color = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(ColorHex) ? "#5865F2" : ColorHex.Trim());
            PreviewBrush = new SolidColorBrush(color);
        }
        catch (FormatException)
        {
            PreviewBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
        }
    }

    /// <summary>Parses Config["Embed"] (tolerates missing/malformed JSON) into the form fields.</summary>
    private void LoadFromConfig()
    {
        _loading = true;
        try
        {
            if (_node.Config.TryGetValue("Embed", out string? json) && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        Title = GetString(root, "title");
                        Description = GetString(root, "description");
                        ColorHex = string.IsNullOrWhiteSpace(GetString(root, "color")) ? "#5865F2" : GetString(root, "color");
                        Footer = GetString(root, "footer");
                        ImageUrl = GetString(root, "imageUrl");
                        ThumbnailUrl = GetString(root, "thumbnailUrl");
                        AuthorName = GetString(root, "authorName");

                        if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var field in fields.EnumerateArray())
                            {
                                AddFieldRow(new EmbedFieldRowViewModel(
                                    GetString(field, "name"),
                                    GetString(field, "value"),
                                    field.TryGetProperty("inline", out var inline) && inline.ValueKind == JsonValueKind.True));
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Malformed stored JSON — start with a clean form.
                }
            }

            RefreshPreviewBrush();
        }
        finally
        {
            _loading = false;
        }
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>Writes the current form back into Config["Embed"].</summary>
    private void Serialize()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["title"] = Title,
                ["description"] = Description,
                ["color"] = string.IsNullOrWhiteSpace(ColorHex) ? "#5865F2" : ColorHex.Trim(),
                ["footer"] = Footer,
                ["imageUrl"] = ImageUrl,
                ["thumbnailUrl"] = ThumbnailUrl,
                ["authorName"] = AuthorName,
                ["fields"] = Fields.Select(f => new Dictionary<string, object?>
                {
                    ["name"] = f.Name,
                    ["value"] = f.Value,
                    ["inline"] = f.Inline
                }).ToList()
            };

            _node.Config["Embed"] = JsonSerializer.Serialize(payload, JsonOpts.Indented);
            _onConfigChanged(null);
        }
        catch (Exception)
        {
            // Serialization of plain dictionaries cannot realistically fail —
            // guard anyway so a bad color string never breaks typing.
        }
    }
}

/// <summary>One rule-type option of the condition rule builder.</summary>
public class RuleTypeOption
{
    /// <summary>Leaf JSON "type" value.</summary>
    public string Key { get; }

    /// <summary>Human label shown in the dropdown.</summary>
    public string Label { get; }

    public RuleTypeOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public override string ToString() => Label;
}

/// <summary>
/// One row of the condition rule builder: a rule type plus the value fields
/// that type needs. Serialized as one leaf of the Config["ConditionRule"] JSON.
/// </summary>
public partial class ConditionRuleRowViewModel : ObservableObject
{
    /// <summary>Raised whenever anything in the row changes.</summary>
    public event EventHandler? Changed;

    public static IReadOnlyList<RuleTypeOption> RuleTypes { get; } = new List<RuleTypeOption>
    {
        new("user_has_role", "User has role"),
        new("message_contains", "Message contains"),
        new("variable_equals", "Variable equals"),
        new("variable_exists", "Variable exists"),
        new("time_between", "Time between"),
        new("user_is_bot", "User is bot"),
        new("channel_is", "Channel is")
    };

    /// <summary>Instance view of <see cref="RuleTypes"/> for the row's XAML binding.</summary>
    public IReadOnlyList<RuleTypeOption> TypeChoices => RuleTypes;

    [ObservableProperty]
    private RuleTypeOption _type = RuleTypes[0];

    [ObservableProperty]
    private string _roleId = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _caseSensitive;

    [ObservableProperty]
    private string _variableName = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _startHour = "0";

    [ObservableProperty]
    private string _endHour = "23";

    [ObservableProperty]
    private string _channelName = string.Empty;

    public ConditionRuleRowViewModel()
    {
        // Hour fields are numeric-only — keep them sane on every change.
    }

    public bool IsRole => Type.Key == "user_has_role";
    public bool IsMessageContains => Type.Key == "message_contains";
    public bool IsVariableEquals => Type.Key == "variable_equals";
    public bool IsVariableExists => Type.Key == "variable_exists";
    public bool IsTimeBetween => Type.Key == "time_between";
    public bool IsUserIsBot => Type.Key == "user_is_bot";
    public bool IsChannelIs => Type.Key == "channel_is";

    partial void OnTypeChanged(RuleTypeOption value)
    {
        OnPropertyChanged(nameof(IsRole));
        OnPropertyChanged(nameof(IsMessageContains));
        OnPropertyChanged(nameof(IsVariableEquals));
        OnPropertyChanged(nameof(IsVariableExists));
        OnPropertyChanged(nameof(IsTimeBetween));
        OnPropertyChanged(nameof(IsUserIsBot));
        OnPropertyChanged(nameof(IsChannelIs));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnRoleIdChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnTextChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnCaseSensitiveChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnVariableNameChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnValueChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnStartHourChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnEndHourChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnChannelNameChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Serializes this row into its leaf JSON object (null when the row is entirely empty).</summary>
    public Dictionary<string, object?>? ToLeaf()
    {
        switch (Type.Key)
        {
            case "user_has_role":
                if (string.IsNullOrWhiteSpace(RoleId)) return null;
                return new Dictionary<string, object?> { ["type"] = "user_has_role", ["roleId"] = RoleId.Trim() };

            case "message_contains":
                if (string.IsNullOrWhiteSpace(Text)) return null;
                return new Dictionary<string, object?> { ["type"] = "message_contains", ["text"] = Text, ["caseSensitive"] = CaseSensitive };

            case "variable_equals":
                if (string.IsNullOrWhiteSpace(VariableName)) return null;
                return new Dictionary<string, object?> { ["type"] = "variable_equals", ["variableName"] = VariableName.Trim(), ["value"] = Value };

            case "variable_exists":
                if (string.IsNullOrWhiteSpace(VariableName)) return null;
                return new Dictionary<string, object?> { ["type"] = "variable_exists", ["variableName"] = VariableName.Trim() };

            case "time_between":
                if (!int.TryParse(StartHour, out int start) || !int.TryParse(EndHour, out int end)) return null;
                return new Dictionary<string, object?> { ["type"] = "time_between", ["startHour"] = Math.Clamp(start, 0, 23), ["endHour"] = Math.Clamp(end, 0, 23) };

            case "user_is_bot":
                return new Dictionary<string, object?> { ["type"] = "user_is_bot" };

            case "channel_is":
                if (string.IsNullOrWhiteSpace(ChannelName)) return null;
                return new Dictionary<string, object?> { ["type"] = "channel_is", ["channelName"] = ChannelName.Trim() };

            default:
                return null;
        }
    }

    /// <summary>Refills the row from a stored leaf object (tolerates missing keys).</summary>
    public void LoadLeaf(JsonElement leaf)
    {
        string Type() => leaf.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? string.Empty : string.Empty;
        string Str(string name)
        {
            if (leaf.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
                if (v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return v.ToString();
            }
            return string.Empty;
        }

        switch (Type())
        {
            case "user_has_role":
                _type = RuleTypes.First(t => t.Key == "user_has_role");
                RoleId = Str("roleId");
                break;
            case "message_contains":
                _type = RuleTypes.First(t => t.Key == "message_contains");
                Text = Str("text");
                CaseSensitive = leaf.TryGetProperty("caseSensitive", out var cs) && cs.ValueKind == JsonValueKind.True;
                break;
            case "variable_equals":
                _type = RuleTypes.First(t => t.Key == "variable_equals");
                VariableName = Str("variableName");
                Value = Str("value");
                break;
            case "variable_exists":
                _type = RuleTypes.First(t => t.Key == "variable_exists");
                VariableName = Str("variableName");
                break;
            case "time_between":
                _type = RuleTypes.First(t => t.Key == "time_between");
                StartHour = Str("startHour");
                EndHour = Str("endHour");
                break;
            case "user_is_bot":
                _type = RuleTypes.First(t => t.Key == "user_is_bot");
                break;
            case "channel_is":
                _type = RuleTypes.First(t => t.Key == "channel_is");
                ChannelName = Str("channelName");
                break;
        }

        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(IsRole));
        OnPropertyChanged(nameof(IsMessageContains));
        OnPropertyChanged(nameof(IsVariableEquals));
        OnPropertyChanged(nameof(IsVariableExists));
        OnPropertyChanged(nameof(IsTimeBetween));
        OnPropertyChanged(nameof(IsUserIsBot));
        OnPropertyChanged(nameof(IsChannelIs));
    }
}

/// <summary>
/// The condition builder bound to a node's Config["Expression"]/["ConditionRule"].
/// Two modes: a plain guard expression, or a structured rule list serialized as
/// {"all":[...], "any":[...]} JSON. Switching modes copies data across where
/// possible (rule → simple copies the rule JSON into the expression field —
/// the runtime treats JSON in "Expression" as a rule too).
/// </summary>
public partial class ConditionBuilderViewModel : ObservableObject
{
    private const string ModeSimple = "Simple expression";
    private const string ModeBuilder = "Rule builder";

    private readonly FlowCanvasNode _node;
    private readonly Action<string?> _onConfigChanged;
    private bool _loading;

    public ObservableCollection<string> Modes { get; } = new() { ModeSimple, ModeBuilder };

    [ObservableProperty]
    private string _mode = ModeSimple;

    [ObservableProperty]
    private string _expression = string.Empty;

    [ObservableProperty]
    private string _combinator = "ALL";

    public ObservableCollection<string> Combinators { get; } = new() { "ALL", "ANY" };

    [ObservableProperty]
    private ObservableCollection<ConditionRuleRowViewModel> _rows = new();

    public bool IsSimpleMode => Mode == ModeSimple;
    public bool IsBuilderMode => Mode == ModeBuilder;

    public ConditionBuilderViewModel(FlowCanvasNode node, Action<string?> onConfigChanged)
    {
        _node = node;
        _onConfigChanged = onConfigChanged;

        Rows.CollectionChanged += (_, _) => Serialize();

        _loading = true;
        try
        {
            // Default mode: rule builder when the node already carries a rule JSON.
            bool hasRule = _node.Config.TryGetValue("ConditionRule", out string? existing) && !string.IsNullOrWhiteSpace(existing);
            _mode = hasRule ? ModeBuilder : ModeSimple;

            Expression = ReadStoredExpression();
            Combinator = "ALL";
            LoadRowsFromRuleJson(existing);

            if (IsBuilderMode && Rows.Count == 0)
            {
                AddRuleRow(new ConditionRuleRowViewModel());
            }
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(IsBuilderMode));

        if (_loading)
        {
            return;
        }

        if (IsBuilderMode)
        {
            // Simple → rule builder: the structured rows take over. Seed one
            // blank row when nothing structural exists yet; the plain text
            // stays in Config["Expression"] and is still honored if all rows
            // are removed.
            if (Rows.Count == 0)
            {
                AddRuleRow(new ConditionRuleRowViewModel());
            }
            Serialize();
        }
        else
        {
            // Rule builder → simple: copy the rule JSON across into the
            // expression field (the runtime accepts rule JSON there too) so
            // the switch never loses data.
            string ruleJson = BuildRuleJson();
            if (!string.IsNullOrEmpty(ruleJson))
            {
                Expression = ruleJson;
            }
            _node.Config["Expression"] = Expression;
            _node.Config.Remove("ConditionRule");
            _onConfigChanged(null);
        }
    }

    partial void OnExpressionChanged(string value)
    {
        if (_loading || !IsSimpleMode)
        {
            return;
        }

        _node.Config["Expression"] = value;
        _node.Config.Remove("ConditionRule");
        _onConfigChanged("Expression");
    }

    partial void OnCombinatorChanged(string value) => Serialize();

    [RelayCommand]
    private void AddRow()
    {
        AddRuleRow(new ConditionRuleRowViewModel());
    }

    /// <summary>Adds a rule row and hooks its edit event so edits re-serialize.</summary>
    private void AddRuleRow(ConditionRuleRowViewModel row)
    {
        row.Changed += (_, _) => Serialize();
        Rows.Add(row);
    }

    [RelayCommand]
    private void RemoveRow(ConditionRuleRowViewModel? row)
    {
        if (row != null)
        {
            Rows.Remove(row);
        }
    }

    private string ReadStoredExpression()
    {
        if (_node.Config.TryGetValue("Expression", out string? expression) && !string.IsNullOrWhiteSpace(expression))
        {
            return expression;
        }
        if (_node.Config.TryGetValue("expression", out string? lower) && !string.IsNullOrWhiteSpace(lower))
        {
            return lower;
        }
        return string.Empty;
    }

    private void LoadRowsFromRuleJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (root.TryGetProperty("any", out var any) && any.ValueKind == JsonValueKind.Array && any.GetArrayLength() > 0)
            {
                Combinator = "ANY";
                foreach (var leaf in any.EnumerateArray())
                {
                    AddRuleRow(NewRowFrom(leaf));
                }
            }
            else if (root.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.Array)
            {
                Combinator = "ALL";
                foreach (var leaf in all.EnumerateArray())
                {
                    AddRuleRow(NewRowFrom(leaf));
                }
            }
        }
        catch (JsonException)
        {
            // Stored value is not rule JSON (plain expression) — leave rows empty.
        }
    }

    private static ConditionRuleRowViewModel NewRowFrom(JsonElement leaf)
    {
        var row = new ConditionRuleRowViewModel();
        if (leaf.ValueKind == JsonValueKind.Object)
        {
            row.LoadLeaf(leaf);
        }
        return row;
    }

    private void Serialize()
    {
        if (_loading || !IsBuilderMode)
        {
            return;
        }

        string json = BuildRuleJson();
        if (json == null)
        {
            // No usable rows: fall back to the plain expression so the node
            // never ends up silently unconditional.
            _node.Config.Remove("ConditionRule");
        }
        else
        {
            _node.Config["ConditionRule"] = json;
            // Copy across where possible: the runtime also accepts rule JSON
            // in "Expression", so mirror it for older loaders.
            _node.Config["Expression"] = json;
        }
        _onConfigChanged("ConditionRule");
    }

    /// <summary>Builds the {"all":[...], "any":[...]} document from the rows (null when nothing usable).</summary>
    private string? BuildRuleJson()
    {
        var leaves = Rows.Select(r => r.ToLeaf()).Where(l => l != null).Cast<Dictionary<string, object?>>().ToList();
        if (leaves.Count == 0)
        {
            return null;
        }

        var payload = new Dictionary<string, object?>();
        payload[string.Equals(Combinator, "ANY", StringComparison.OrdinalIgnoreCase) ? "any" : "all"] = leaves;
        return JsonSerializer.Serialize(payload, JsonOpts.Indented);
    }
}

// =====================================================================================
// Typed command parameters (BotGhost's purple Options = slash-command arguments)
// =====================================================================================

/// <summary>
/// One entry of the Options tab's "COMMAND PARAMETERS" group: a typed
/// slash-command parameter definition the user can attach to the flow
/// (text, number, user, channel, role, choice, attachment, …).
/// </summary>
public sealed class FlowParameterDefinition
{
    /// <summary>Raw <see cref="WorkflowParameterTypes"/> value stored on the workflow.</summary>
    public string TypeKey { get; }

    /// <summary>Human label shown in the palette.</summary>
    public string Label { get; }

    /// <summary>Description shown in the palette.</summary>
    public string Description { get; }

    /// <summary>Glyph shown in the violet palette chip.</summary>
    public string Glyph { get; }

    /// <summary>Fixed parameter name (e.g. "search" for Music Smart Search) instead of an auto name.</summary>
    public string? FixedName { get; }

    /// <summary>Fixed description used when the parameter is created (falls back to "The {type} value").</summary>
    public string? FixedDescription { get; }

    public FlowParameterDefinition(
        string typeKey,
        string label,
        string description,
        string glyph = "◈",
        string? fixedName = null,
        string? fixedDescription = null)
    {
        TypeKey = typeKey;
        Label = label;
        Description = description;
        Glyph = glyph;
        FixedName = fixedName;
        FixedDescription = fixedDescription;
    }
}

/// <summary>
/// One row of the typed-parameter editor popup and one violet chip in the
/// chips bar. Edits mark the flow dirty through <see cref="Changed"/> and are
/// carried into the saved <see cref="Workflow.Parameters"/> list by
/// <see cref="FlowEditorViewModel.AssembleWorkflow"/> (names normalized there).
/// </summary>
public partial class WorkflowParameterRowViewModel : ObservableObject
{
    /// <summary>Raised whenever any field changes (dirty-marking in the editor VM).</summary>
    public event EventHandler? Changed;

    /// <summary>Raw parameter type (one of <see cref="WorkflowParameterTypes"/>) — read-only after creation.</summary>
    public string Type { get; }

    /// <summary>Display label of the type ("Text", "Number", …).</summary>
    public string TypeLabel => Type.Length == 0 ? Type : char.ToUpperInvariant(Type[0]) + Type[1..];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _required = true;

    public WorkflowParameterRowViewModel(string type, string name, string description, bool isRequired)
    {
        Type = type;
        _name = name;
        _description = description;
        _required = isRequired;
    }

    /// <summary>"Name · Type" text of the violet chip.</summary>
    public string ChipText => $"{Name} · {TypeLabel}";

    /// <summary>Normalized name actually sent to Discord (lowercase identifier).</summary>
    public string NormalizedName => FlowEditorViewModel.NormalizeParameterName(Name);

    /// <summary>True when the normalized name differs from what the user typed (hint is shown).</summary>
    public bool HasNormalizedHint =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.Equals(NormalizedName, Name.Trim(), StringComparison.Ordinal);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(ChipText));
        OnPropertyChanged(nameof(NormalizedName));
        OnPropertyChanged(nameof(HasNormalizedHint));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDescriptionChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnRequiredChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
}

// =====================================================================================
// Message-node components (Discord buttons + select menus, BotGhost chips)
// =====================================================================================

/// <summary>
/// One button row of the settings panel's "BUTTONS &amp; MENUS" section.
/// Serialized into the node's Config["Buttons"] JSON:
/// [{"label":"Yes","style":"Success","emoji":"✅"}] (styles
/// Primary|Secondary|Success|Danger|Link, max 5 per message).
/// </summary>
public partial class ComponentButtonRowViewModel : ObservableObject
{
    /// <summary>Raised whenever any field changes so the config re-serializes.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    private string _label = "Button";

    [ObservableProperty]
    private string _style = "Secondary";

    [ObservableProperty]
    private string _emoji = string.Empty;

    partial void OnLabelChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnStyleChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnEmojiChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>One option row of a select menu (Config["Menu"].options[n]).</summary>
public partial class ComponentMenuOptionRowViewModel : ObservableObject
{
    /// <summary>Raised whenever any field changes so the config re-serializes.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    partial void OnLabelChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnValueChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// The select menu sub-editor of a message node (Config["Menu"] JSON:
/// {"placeholder":"...","options":[{"label":"A","value":"a"}]}, max 25 options).
/// Null in the settings panel when the node has no menu yet.
/// </summary>
public partial class ComponentMenuViewModel : ObservableObject
{
    /// <summary>Raised whenever anything changes so the config re-serializes.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    private string _placeholder = string.Empty;

    public ObservableCollection<ComponentMenuOptionRowViewModel> Options { get; } = new();

    partial void OnPlaceholderChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Hooks a row so its edits re-serialize the menu.</summary>
    public void Track(ComponentMenuOptionRowViewModel row)
    {
        row.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Options.Add(row);
    }
}

// =====================================================================================
// Comparison rules (BotGhost-style multi-rule comparison condition)
// =====================================================================================

/// <summary>
/// One operator of the comparison rules editor. <see cref="Key"/> is the
/// canonical engine key stored in Config["Comparisons"][n].operator; the
/// keys/labels mirror <see cref="BlockCatalog.ComparisonOperators"/>.
/// </summary>
public sealed class ComparisonOperatorOption
{
    /// <summary>Canonical operator key ("eq", "contains", ...).</summary>
    public string Key { get; }

    /// <summary>Human label shown in the dropdown ("Equal to", ...).</summary>
    public string Label { get; }

    public ComparisonOperatorOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public override string ToString() => Label;
}

/// <summary>
/// One rule row of the settings panel's "COMPARISONS" section. Serialized into
/// the node's Config["Comparisons"] JSON:
/// [{"left":"10","operator":"eq","right":"5"}] (max 10 rules, evaluated in
/// order — the first match routes Branches["case:{i}"], none routes "else").
/// </summary>
public partial class ComparisonRuleRowViewModel : ObservableObject
{
    /// <summary>Raised whenever any field changes so the config re-serializes.</summary>
    public event EventHandler? Changed;

    /// <summary>Human label per canonical comparison operator key.</summary>
    private static readonly Dictionary<string, string> OperatorLabels = new(StringComparer.Ordinal)
    {
        ["lt"] = "Less than",
        ["lte"] = "Less than or equal to",
        ["gt"] = "Greater than",
        ["gte"] = "Greater than or equal to",
        ["eq"] = "Equal to",
        ["neq"] = "Not equal to",
        ["contains"] = "Contains",
        ["not_contains"] = "Does not contain",
        ["starts_with"] = "Starts with",
        ["ends_with"] = "Ends with",
        ["not_starts_with"] = "Does not start with",
        ["not_ends_with"] = "Does not end with",
        ["collection_contains"] = "Collection contains",
        ["collection_not_contains"] = "Collection does not contain"
    };

    /// <summary>
    /// The comparison operators in canonical order — keys come from
    /// <see cref="BlockCatalog.ComparisonOperators"/>, labels are mapped here.
    /// </summary>
    public static IReadOnlyList<ComparisonOperatorOption> AllOperators { get; } =
        BlockCatalog.ComparisonOperators
            .Select(key => new ComparisonOperatorOption(key, OperatorLabels.TryGetValue(key, out string? label) ? label : key))
            .ToList();

    /// <summary>Instance passthrough so XAML rows can bind the operator list.</summary>
    public IReadOnlyList<ComparisonOperatorOption> OperatorChoices => AllOperators;

    [ObservableProperty]
    private string _left = "{args}";

    [ObservableProperty]
    private ComparisonOperatorOption? _selectedOperator;

    [ObservableProperty]
    private string _right = string.Empty;

    public ComparisonRuleRowViewModel()
    {
        _selectedOperator = AllOperators.First(c => string.Equals(c.Key, "eq", StringComparison.Ordinal));
    }

    public ComparisonRuleRowViewModel(string left, string op, string right)
    {
        _left = left;
        _selectedOperator = AllOperators.FirstOrDefault(c => string.Equals(c.Key, op, StringComparison.Ordinal))
                            ?? AllOperators.First(c => string.Equals(c.Key, "eq", StringComparison.Ordinal));
        _right = right;
    }

    partial void OnLeftChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnRightChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedOperatorChanged(ComparisonOperatorOption? value) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// The comparison rules sub-editor of a "comparison" condition node
/// (Config["Comparisons"] JSON array). Raises <see cref="Changed"/> on every
/// row edit so the host view-model can persist the config, refresh the node's
/// branch ports and re-sync the attached case/Else cards.
/// </summary>
public partial class ComparisonBuilderViewModel : ObservableObject
{
    /// <summary>Raised whenever anything changes so the config re-serializes.</summary>
    public event EventHandler? Changed;

    /// <summary>Discord/BotGhost parity cap on rules per comparison node.</summary>
    public const int MaxRules = 10;

    public ObservableCollection<ComparisonRuleRowViewModel> Rows { get; } = new();

    public bool CanAddRule => Rows.Count < MaxRules;

    public ComparisonBuilderViewModel(FlowCanvasNode node)
    {
        foreach (ComparisonRuleRowViewModel row in FlowEditorViewModel.ParseComparisons(node)
                     .Select(rule => new ComparisonRuleRowViewModel(rule.Left, rule.Operator, rule.Right)))
        {
            Track(row);
        }

        // Legacy prefill: a comparison saved before the multi-rule era carries
        // Left/Operator/Right instead of Config["Comparisons"]. Prefill ONE row
        // from those values — nothing is written back until the user edits a
        // rule, so the runtime's legacy path keeps old flows working.
        if (Rows.Count == 0 && !node.Config.ContainsKey("Comparisons") &&
            (node.Config.ContainsKey("Left") || node.Config.ContainsKey("Operator") || node.Config.ContainsKey("Right")))
        {
            node.Config.TryGetValue("Left", out string? left);
            node.Config.TryGetValue("Operator", out string? op);
            node.Config.TryGetValue("Right", out string? right);
            Track(new ComparisonRuleRowViewModel(
                string.IsNullOrEmpty(left) ? "{args}" : left,
                string.IsNullOrEmpty(op) ? "eq" : op,
                right ?? string.Empty));
        }
    }

    /// <summary>Hooks a row so its edits re-serialize the rules.</summary>
    public void Track(ComparisonRuleRowViewModel row)
    {
        row.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Rows.Add(row);
        OnPropertyChanged(nameof(CanAddRule));
    }

    /// <summary>Settings panel "+ Add Comparison" (capped at 10 rules).</summary>
    [RelayCommand]
    private void AddRule()
    {
        if (!CanAddRule)
        {
            return;
        }

        Track(new ComparisonRuleRowViewModel()); // defaults: "{args}" / eq / empty
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes one rule row (the matching case card is re-indexed by the host).</summary>
    [RelayCommand]
    private void RemoveRule(ComparisonRuleRowViewModel? row)
    {
        if (row == null || !Rows.Remove(row))
        {
            return;
        }

        OnPropertyChanged(nameof(CanAddRule));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

// =====================================================================================
// Snapshot / draft DTOs (undo, autosave, restore)
// =====================================================================================

/// <summary>JSON serializer options for editor snapshots (drafts + undo stack).</summary>
internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Trigger section of an editor snapshot.</summary>
public class FlowTriggerDto
{
    public string Type { get; set; } = WorkflowTriggerTypes.Manual;
    public string? CommandName { get; set; }
    public string? CommandPrefix { get; set; }
    public int? IntervalMinutes { get; set; }
    public string? EventType { get; set; }
    public string? Platform { get; set; }
    public string? Description { get; set; }

    /// <summary>Minimum mod tier (mod_command triggers); null for other trigger types.</summary>
    public string? MinimumTier { get; set; }
}

/// <summary>One command-option row of an editor snapshot.</summary>
public class FlowOptionDto
{
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>One typed command parameter of an editor snapshot / .flow.json document.</summary>
public class FlowParameterDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Required { get; set; } = true;
}

/// <summary>One node (settings + routing) of an editor snapshot.</summary>
public class FlowNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Config { get; set; } = new();
    public string? Next { get; set; }
    public string? BranchTrue { get; set; }
    public string? BranchFalse { get; set; }
    public string? BranchError { get; set; }
    public Dictionary<string, string>? Branches { get; set; }
}

/// <summary>Full editor state serialized into the undo stack and the draft files.</summary>
public class FlowEditorSnapshotDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public FlowTriggerDto? Trigger { get; set; }
    public List<FlowOptionDto>? Options { get; set; }
    public List<FlowParameterDto>? Parameters { get; set; }
    public List<FlowNodeDto>? Nodes { get; set; }

    /// <summary>Pinned entry node (null = legacy auto-detection).</summary>
    public string? EntryNodeId { get; set; }

    /// <summary>Pinned error-handler node (null = error port routing only).</summary>
    public string? ErrorHandlerEntryId { get; set; }
}

// =====================================================================================
// FlowEditorViewModel
// =====================================================================================

/// <summary>
/// View model of the visual flow editor: hosts the
/// <see cref="FlowCanvasControl"/> graph, the block palette, the per-block
/// settings panel and the trigger/option chrome, and maps everything to/from a
/// persisted <see cref="Workflow"/> via <see cref="WorkflowJson"/> — the same
/// save path as the structured builder (<see cref="AutomationViewModel"/>).
/// <para>
/// Hosts (overlay panels in AutomationView / DiscordBotDetailView) resolve this
/// VM from DI, call <see cref="InitializeAsync(Workflow?, string?)"/> and hide
/// their overlay when <see cref="EditorClosed"/> fires.
/// </para>
/// </summary>
public partial class FlowEditorViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private const int MaxUndoSnapshots = 50;
    private static readonly string[] PalettePlatforms = { "Any", "Twitch", "Kick", "Discord", "YouTube" };

    private readonly ILogger<FlowEditorViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly WorkflowTriggerService _triggerService;
    private readonly WorkflowDiscordBridge _discordBridge;
    private readonly MarketplaceService _marketplace;

    /// <summary>The canvas the view attached (null until the view loads).</summary>
    private FlowCanvasControl? _canvas;

    /// <summary>The workflow being edited (null for a brand-new flow).</summary>
    private Workflow? _existingWorkflow;

    private string _workflowId = Guid.NewGuid().ToString("N");
    private string? _existingCategory;
    private DateTime _existingCreatedAt = DateTime.UtcNow;

    /// <summary>
    /// Command row id this flow was seeded from (Command → flow adoption), or
    /// null for a flow that never came from a command. Carried onto the saved
    /// <see cref="Workflow"/> as SourceCommandId so the save-time adoption step
    /// can retire the legacy command row.
    /// </summary>
    private string? _sourceCommandId;

    /// <summary>Pinned entry node id (workflow.EntryNodeId on save; null = legacy auto-detection).</summary>
    private string? _entryNodeId;

    /// <summary>Pinned error-handler node id (workflow.ErrorHandlerEntryId on save).</summary>
    private string? _errorHandlerEntryId;

    /// <summary>
    /// Canvas component sub-node mapping for the open flow:
    /// "{parentNodeId}:{kind}:{index}" (kind "button"|"menu") → sub-node id.
    /// Filled by <see cref="AddComponentNode"/> handling and flow loading.
    /// </summary>
    private readonly Dictionary<string, string> _componentSubNodes = new(StringComparer.Ordinal);

    private readonly DispatcherTimer _snapshotDebounce;
    private readonly DispatcherTimer _autosaveTimer;

    /// <summary>Undo history: chronological snapshots, the last entry is the current state.</summary>
    private readonly List<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _suppressSnapshot;
    private int _cascadeCount;

    private static readonly string[] DefaultVariables =
    {
        "{username}", "{channel}", "{platform}", "{message}", "{args}", "{command}",
        "{guild}", "{response}", "{http_response}", "{http_status}", "{random}"
    };

    // ======================================================================
    // Header / state
    // ======================================================================

    [ObservableProperty]
    private string _flowName = string.Empty;

    [ObservableProperty]
    private bool _flowEnabled = true;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _statusText = "Drag blocks from the left, wire the ports, then SAVE.";

    [ObservableProperty]
    private bool _isBusy;

    public event EventHandler? EditorClosed;

    // ======================================================================
    // Canvas / palette
    // ======================================================================

    [ObservableProperty]
    private ObservableCollection<FlowCanvasNode> _nodes = new();

    [ObservableProperty]
    private FlowCanvasNode? _selectedNode;

    [ObservableProperty]
    private string _paletteSearch = string.Empty;

    /// <summary>Actions tab active (blue palette).</summary>
    [ObservableProperty]
    private bool _isActionsTab = true;

    /// <summary>Options tab active (purple command options — click to add).</summary>
    [ObservableProperty]
    private bool _isOptionsTab;

    /// <summary>
    /// Conditions tab active. Derived: neither Options nor Actions selected.
    /// Setting it to true clears the other two (three-way exclusive tabs).
    /// </summary>
    public bool IsConditionsTab
    {
        get => !IsActionsTab && !IsOptionsTab;
        set
        {
            if (!value)
            {
                return; // one of the other two tabs was checked — nothing to do
            }

            bool actionsChanged = _isActionsTab;
            bool optionsChanged = _isOptionsTab;
            _isActionsTab = false;
            _isOptionsTab = false;
            if (actionsChanged)
            {
                OnPropertyChanged(nameof(IsActionsTab));
            }
            if (optionsChanged)
            {
                OnPropertyChanged(nameof(IsOptionsTab));
            }
            OnPropertyChanged(nameof(IsConditionsTab));
            OnPropertyChanged(nameof(HasNoMatchingBlocks));
        }
    }

    public ObservableCollection<FlowPaletteItemViewModel> ActionBlocks { get; } = new();
    public ObservableCollection<FlowPaletteItemViewModel> ConditionBlocks { get; } = new();

    /// <summary>Grouped rows (bold Section headers + block entries) per palette tab.</summary>
    public ObservableCollection<FlowPaletteRowViewModel> FilteredActionBlocks { get; } = new();
    public ObservableCollection<FlowPaletteRowViewModel> FilteredConditionBlocks { get; } = new();

    /// <summary>Purple Options-tab entries (single click adds them to the command).</summary>
    public ObservableCollection<FlowOptionDefinition> FilteredOptionDefinitions { get; } = new();

    /// <summary>True when the search matched nothing on the active tab.</summary>
    public bool HasNoMatchingBlocks =>
        (IsActionsTab ? FilteredActionBlocks.Count
            : IsOptionsTab ? FilteredOptionDefinitions.Count + FilteredParameterDefinitions.Count
            : FilteredConditionBlocks.Count) == 0;

    /// <summary>"No blocks found" flags for the three palette tabs.</summary>
    public bool ActionsEmpty => FilteredActionBlocks.Count == 0;
    public bool ConditionsEmpty => FilteredConditionBlocks.Count == 0;
    public bool OptionsEmpty => FilteredOptionDefinitions.Count == 0;

    partial void OnIsActionsTabChanged(bool value)
    {
        if (value)
        {
            if (_isOptionsTab)
            {
                _isOptionsTab = false;
                OnPropertyChanged(nameof(IsOptionsTab));
            }
        }
        else if (!_isOptionsTab)
        {
            // Clicking the active tab must never leave the palette without an
            // active tab (conditions is derived) — snap this one back on.
            _isActionsTab = true;
            OnPropertyChanged(nameof(IsActionsTab));
        }
        OnPropertyChanged(nameof(IsConditionsTab));
        OnPropertyChanged(nameof(HasNoMatchingBlocks));
    }

    partial void OnIsOptionsTabChanged(bool value)
    {
        if (value)
        {
            if (_isActionsTab)
            {
                _isActionsTab = false;
                OnPropertyChanged(nameof(IsActionsTab));
            }
        }
        else if (!_isActionsTab)
        {
            // See OnIsActionsTabChanged — keep an active tab at all times.
            _isOptionsTab = true;
            OnPropertyChanged(nameof(IsOptionsTab));
        }
        OnPropertyChanged(nameof(IsConditionsTab));
        OnPropertyChanged(nameof(HasNoMatchingBlocks));
        OnPropertyChanged(nameof(OptionsEmpty));
    }

    /// <summary>
    /// True when the ENTIRE left side (icon rail + side panel) is collapsed to
    /// zero width. The floating ☰ button over the canvas reopens it — this
    /// replaces the old slim-bar palette collapse.
    /// </summary>
    [ObservableProperty]
    private bool _isLeftCollapsed;

    /// <summary>Role names for the rule builder's "User has role" pickers (may stay empty).</summary>
    public ObservableCollection<string> RoleOptions { get; } = new();

    // ======================================================================
    // Settings panel
    // ======================================================================

    [ObservableProperty]
    private BlockDefinition? _selectedBlock;

    /// <summary>Accent color of the selected block as a brush (header underline).</summary>
    public Brush SelectedBlockBrush =>
        SelectedBlock == null ? Brushes.Transparent : ParseBrush(SelectedBlock.Color);

    [ObservableProperty]
    private EmbedBuilderViewModel? _embedBuilder;

    [ObservableProperty]
    private ConditionBuilderViewModel? _conditionBuilder;

    /// <summary>Comparison rules sub-editor of the selected comparison node (null otherwise).</summary>
    [ObservableProperty]
    private ComparisonBuilderViewModel? _comparisonBuilder;

    /// <summary>Button rows of the selected message node (Buttons &amp; Menus section).</summary>
    [ObservableProperty]
    private ObservableCollection<ComponentButtonRowViewModel> _componentButtons = new();

    /// <summary>Select-menu sub-editor of the selected message node (null when it has none).</summary>
    [ObservableProperty]
    private ComponentMenuViewModel? _componentMenu;

    /// <summary>True while component editors refill from config, so load-time writes are ignored.</summary>
    private bool _loadingComponents;

    public bool HasEmbedParam => SelectedNode != null && EmbedBuilder != null;
    public bool HasRuleParam => SelectedNode != null && ConditionBuilder != null;
    public bool HasComparisonsParam => SelectedNode != null && ComparisonBuilder != null;
    public bool HasSelectedNode => SelectedNode != null;
    public bool HasNoOptions => OptionRows.Count == 0;
    public bool HasParamRows => ParamRows.Count > 0;

    /// <summary>True when the selected node can carry Discord buttons / select menus.</summary>
    public bool HasComponentSection => SelectedNode != null && IsMessageBlock(SelectedNode.BlockType);
    public bool HasComponentButtons => ComponentButtons.Count > 0;
    public bool HasComponentMenu => ComponentMenu != null;

    /// <summary>Discord button styles (Config["Buttons"].style values).</summary>
    public IReadOnlyList<string> ComponentButtonStyles { get; } =
        new[] { "Primary", "Secondary", "Success", "Danger", "Link" };

    partial void OnComponentButtonsChanged(ObservableCollection<ComponentButtonRowViewModel> value) =>
        OnPropertyChanged(nameof(HasComponentButtons));

    partial void OnComponentMenuChanged(ComponentMenuViewModel? value) => OnPropertyChanged(nameof(HasComponentMenu));

    // ======================================================================
    // Right settings-panel modes (None / Block / Command / Parameter)
    // ======================================================================

    /// <summary>Which content the right-hand settings host is showing.</summary>
    public enum FlowSettingsMode
    {
        /// <summary>Panel hidden — the help card shows instead.</summary>
        None,

        /// <summary>The selected canvas node's settings.</summary>
        Block,

        /// <summary>Flow-wide command settings: name, enabled, trigger, options, parameters.</summary>
        Command,

        /// <summary>One typed parameter's settings (canvas parameter-card click).</summary>
        Parameter
    }

    /// <summary>Current right-panel mode — drives the four visibility slots of the settings host.</summary>
    [ObservableProperty]
    private FlowSettingsMode _settingsMode;

    /// <summary>Block mode: a canvas node is being edited on the right.</summary>
    public bool IsBlockSelected => SettingsMode == FlowSettingsMode.Block;

    /// <summary>Command mode: the flow-wide command editor is on the right.</summary>
    public bool IsCommandMode => SettingsMode == FlowSettingsMode.Command;

    /// <summary>Parameter mode: one typed parameter's editor is on the right.</summary>
    public bool IsParameterMode => SettingsMode == FlowSettingsMode.Parameter;

    /// <summary>Idle mode: nothing selected — the help card shows.</summary>
    public bool IsSettingsIdle => SettingsMode == FlowSettingsMode.None;

    partial void OnSettingsModeChanged(FlowSettingsMode value)
    {
        OnPropertyChanged(nameof(IsBlockSelected));
        OnPropertyChanged(nameof(IsCommandMode));
        OnPropertyChanged(nameof(IsParameterMode));
        OnPropertyChanged(nameof(IsSettingsIdle));
    }

    /// <summary>The typed parameter shown in Parameter mode (canvas parameter-card click).</summary>
    [ObservableProperty]
    private WorkflowParameterRowViewModel? _selectedParameter;

    /// <summary>
    /// Opens the right panel on the flow-wide Command Settings (trigger-card
    /// click, OPTIONS-tab entries, the toolbar TRIGGER button). A selected
    /// node is deselected first so the panel never serves two masters — the
    /// mode is set before the deselect so the selection change cannot close it.
    /// </summary>
    public void EnterCommandMode()
    {
        SettingsMode = FlowSettingsMode.Command;
        if (SelectedNode != null)
        {
            DeselectNode(); // keeps the canvas chrome in sync
        }
    }

    /// <summary>
    /// Opens the right panel on one typed parameter's settings (canvas
    /// parameter-card click). Stale indexes (the parameter was removed) are
    /// ignored so a dead card can never open an empty editor.
    /// </summary>
    public void EnterParameterMode(int index)
    {
        if (index < 0 || index >= ParameterRows.Count)
        {
            RefreshParameterCards(); // the card list drifted — repaint it
            return;
        }

        SettingsMode = FlowSettingsMode.Parameter;
        if (SelectedNode != null)
        {
            DeselectNode(); // keeps the canvas chrome in sync
        }

        SelectedParameter = ParameterRows[index];
    }

    /// <summary>
    /// Red ✕ of the panel header — works in every mode (mode → None; Block
    /// mode additionally deselects the canvas node).
    /// </summary>
    [RelayCommand]
    private void CloseSettingsPanel()
    {
        if (SelectedNode != null)
        {
            DeselectNode(); // keeps the canvas chrome in sync for Block mode
        }

        SettingsMode = FlowSettingsMode.None;
        SelectedParameter = null;
    }

    /// <summary>Drops the Parameter-mode editor (the flow was reloaded wholesale).</summary>
    private void ResetParameterEditor()
    {
        SelectedParameter = null;
        if (SettingsMode == FlowSettingsMode.Parameter)
        {
            SettingsMode = FlowSettingsMode.None;
        }
    }

    // ======================================================================
    // Typed command parameters (canvas cards + Command Settings editor)
    // ======================================================================

    /// <summary>The Options-tab "COMMAND PARAMETERS" definitions (BotGhost's real Options meaning).</summary>
    public static IReadOnlyList<FlowParameterDefinition> ParameterDefinitions { get; } = new List<FlowParameterDefinition>
    {
        new(WorkflowParameterTypes.Text, "Text", "A text option", "T"),
        new(WorkflowParameterTypes.Number, "Number", "A number option", "#"),
        new(WorkflowParameterTypes.User, "User", "Select a member from the server", "@"),
        new(WorkflowParameterTypes.Channel, "Channel", "Select a channel from the server", "#"),
        new(WorkflowParameterTypes.Role, "Role", "Select a role from the server", "@"),
        new(WorkflowParameterTypes.Choice, "Choice", "A True or False option", "☑"),
        new(WorkflowParameterTypes.Attachment, "Attachment", "An attachment option", "📎"),
        new(WorkflowParameterTypes.Text, "Music Smart Search", "Search and select music tracks", "🎵",
            fixedName: "search", fixedDescription: "Music search query")
    };

    /// <summary>Options-tab entries for the COMMAND PARAMETERS group (search-filtered).</summary>
    public ObservableCollection<FlowParameterDefinition> FilteredParameterDefinitions { get; } = new();

    /// <summary>The flow's typed parameters — the Command Settings rows and the canvas cards.</summary>
    [ObservableProperty]
    private ObservableCollection<WorkflowParameterRowViewModel> _parameterRows = new();

    public bool HasNoParameters => ParameterRows.Count == 0;
    public bool ParametersEmpty => FilteredParameterDefinitions.Count == 0;

    partial void OnParameterRowsChanged(ObservableCollection<WorkflowParameterRowViewModel> value)
    {
        OnPropertyChanged(nameof(HasNoParameters));
        RefreshFlowVariables(); // flow Parameters feed the Variables panel
        RefreshParameterCards();
    }

    /// <summary>
    /// Projects the flow's typed parameters onto the canvas parameter cards
    /// (name / type / missing-name warning). Called after every parameter
    /// mutation, on load, and on undo / restore / import — a no-op until the
    /// view attached its canvas.
    /// </summary>
    public void RefreshParameterCards()
    {
        _canvas?.SetParameterCards(ParameterRows
            .Select(p => new FlowParameterCardInfo(p.Name, p.Type, string.IsNullOrWhiteSpace(p.Name)))
            .ToList());
    }

    [ObservableProperty]
    private List<FlowParamRowViewModel> _paramRows = new();

    partial void OnParamRowsChanged(List<FlowParamRowViewModel> value) => OnPropertyChanged(nameof(HasParamRows));

    partial void OnEmbedBuilderChanged(EmbedBuilderViewModel? value) => OnPropertyChanged(nameof(HasEmbedParam));
    partial void OnConditionBuilderChanged(ConditionBuilderViewModel? value) => OnPropertyChanged(nameof(HasRuleParam));

    /// <summary>Cheat-sheet entries shown on the help card.</summary>
    public ObservableCollection<string> VariableCheatSheet { get; } = new(DefaultVariables);

    partial void OnSelectedNodeChanged(FlowCanvasNode? value)
    {
        SelectedBlock = value == null ? null : BlockCatalog.Find(value.BlockType);
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(SelectedBlockBrush));
        OnPropertyChanged(nameof(HasComponentSection));
        RebuildSettingsPanel();

        // Right-panel mode: a node selection always switches to Block mode
        // (panel content swaps live). Clearing the selection only closes the
        // panel when Block mode was showing — Command / Parameter modes stay
        // open until their own ✕, a new selection, or a wholesale reload.
        if (value != null)
        {
            SelectedParameter = null;
            SettingsMode = FlowSettingsMode.Block;
        }
        else if (SettingsMode == FlowSettingsMode.Block)
        {
            SettingsMode = FlowSettingsMode.None;
        }
    }

    /// <summary>Parses a "#RRGGBB" string, falling back to gray on bad input.</summary>
    private static Brush ParseBrush(string? hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#9CA3AF" : hex));
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }

    // ======================================================================
    // Trigger editor
    // ======================================================================

    public ObservableCollection<FlowTriggerTypeOption> TriggerTypes { get; } = new()
    {
        new(WorkflowTriggerTypes.Command, "Prefix command"),
        new(WorkflowTriggerTypes.SlashCommand, "Slash command (Discord)"),
        new(WorkflowTriggerTypes.ModCommand, "Moderator command"),
        new(WorkflowTriggerTypes.ButtonClick, "Button click"),
        new(WorkflowTriggerTypes.SelectMenu, "Select menu"),
        new(WorkflowTriggerTypes.Event, "Event"),
        new(WorkflowTriggerTypes.Timer, "Timer"),
        new(WorkflowTriggerTypes.Manual, "Manual run")
    };

    [ObservableProperty]
    private FlowTriggerTypeOption _selectedTriggerType;

    [ObservableProperty]
    private string _triggerCommandName = string.Empty;

    [ObservableProperty]
    private string _triggerCommandPrefix = "!";

    [ObservableProperty]
    private string _triggerDescription = string.Empty;

    [ObservableProperty]
    private string _triggerEventType = string.Empty;

    [ObservableProperty]
    private string _triggerPlatform = "Any";

    [ObservableProperty]
    private string _triggerIntervalMinutes = "15";

    /// <summary>Minimum mod tier for mod_command triggers (canonical ModTiers spelling).</summary>
    [ObservableProperty]
    private string _triggerMinimumTier = "Mod";

    public ObservableCollection<string> PlatformOptions { get; } = new(PalettePlatforms);

    /// <summary>Tier ladder for mod_command triggers, lowest rank first (ModTiers.TierNames).</summary>
    public ObservableCollection<string> TierOptions { get; } = new(ModTiers.TierNames);

    /// <summary>
    /// Event types WorkflowTriggerService actually dispatches (Twitch + Kick
    /// subscriptions use exactly these keys) — offered by the editable event-type
    /// ComboBox. Free text still binds so legacy/aliased values keep loading.
    /// </summary>
    public ObservableCollection<string> TriggerEventTypeOptions { get; } = new()
    {
        "follow", "subscribe", "resub", "giftsub", "raid", "cheer", "host"
    };

    public bool IsTriggerCommand => SelectedTriggerType?.Key == WorkflowTriggerTypes.Command;
    public bool IsTriggerSlash => SelectedTriggerType?.Key == WorkflowTriggerTypes.SlashCommand;
    public bool IsTriggerModCommand => SelectedTriggerType?.Key == WorkflowTriggerTypes.ModCommand;
    public bool IsTriggerNeedsCommandName => IsTriggerCommand || IsTriggerSlash || IsTriggerModCommand;
    public bool IsTriggerButtonOrSelect =>
        SelectedTriggerType?.Key is WorkflowTriggerTypes.ButtonClick or WorkflowTriggerTypes.SelectMenu;
    public bool IsTriggerEvent => SelectedTriggerType?.Key == WorkflowTriggerTypes.Event;
    public bool IsTriggerTimer => SelectedTriggerType?.Key == WorkflowTriggerTypes.Timer;
    public bool IsTriggerManual => SelectedTriggerType?.Key == WorkflowTriggerTypes.Manual;

    /// <summary>Hint for button/select triggers: the component ids the runtime subscribes.</summary>
    public string TriggerComponentHint =>
        $"Discord components addressed as vyralwf:{_workflowId} — wired automatically after SAVE.";

    /// <summary>Trigger summary chip for the toolbar.</summary>
    public string TriggerSummaryText
    {
        get
        {
            string label = SelectedTriggerType?.Label ?? "Manual run";
            string prefix = string.IsNullOrWhiteSpace(TriggerCommandPrefix) ? "!" : TriggerCommandPrefix.Trim();
            return IsTriggerNeedsCommandName && !string.IsNullOrWhiteSpace(TriggerCommandName)
                ? $"{label} · {prefix}{TriggerCommandName.Trim()}"
                : label;
        }
    }

    // ======================================================================
    // Toolbar popups (open-state is VM-owned; the view dismisses them)
    // ======================================================================
    // Only the OPTIONS / create-variable / save-template popups remain — the
    // TRIGGER and PARAMETERS popups were retired in favor of the right-hand
    // settings panel (Command mode shows the trigger editor plus the options
    // and parameters lists inline).

    /// <summary>Two-way bound to the view's OPTIONS popup IsOpen.</summary>
    [ObservableProperty]
    private bool _isOptionsPopupOpen;

    /// <summary>Toolbar TRIGGER button: opens Command Settings in the right panel.</summary>
    [RelayCommand]
    private void OpenCommandSettings() => EnterCommandMode();

    /// <summary>
    /// Closes every stay-open popup at once (✕ / DONE buttons, Escape, and the
    /// view's outside-press capture).
    /// </summary>
    [RelayCommand]
    public void CloseAllPopups()
    {
        IsOptionsPopupOpen = false;
        IsCreateVariablePopupOpen = false;
        IsTemplatePopupOpen = false;
    }

    // Back-compat alias for the popup ✕ / DONE command binding name.
    public IRelayCommand ClosePopupsCommand => CloseAllPopupsCommand;

    // ======================================================================
    // Canvas trigger card ("When: !roll — click to edit")
    // ======================================================================

    /// <summary>Card title pushed onto the canvas ("When: /roll (slash)", "When: every 5 min", …).</summary>
    public string TriggerCardTitle
    {
        get
        {
            string prefix = string.IsNullOrWhiteSpace(TriggerCommandPrefix) ? "!" : TriggerCommandPrefix.Trim();
            string commandName = string.IsNullOrWhiteSpace(TriggerCommandName) ? "command" : TriggerCommandName.Trim();

            if (IsTriggerSlash)
            {
                return $"When: /{commandName} (slash)";
            }
            if (IsTriggerCommand)
            {
                return $"When: {prefix}{commandName}";
            }
            if (IsTriggerModCommand)
            {
                return $"When: !mod {commandName} ({TriggerMinimumTier})";
            }
            if (IsTriggerButtonOrSelect)
            {
                return IsTriggerButton
                    ? "When: button click"
                    : "When: select menu";
            }
            if (IsTriggerEvent)
            {
                return $"When: {(string.IsNullOrWhiteSpace(TriggerEventType) ? "event" : TriggerEventType.Trim())} on {TriggerPlatform}";
            }
            if (IsTriggerTimer)
            {
                int minutes = int.TryParse(TriggerIntervalMinutes, out int parsed) && parsed > 0 ? parsed : 15;
                return $"When: every {minutes} min";
            }
            return "When: manual run";
        }
    }

    /// <summary>Card subtitle pushed onto the canvas.</summary>
    public string TriggerCardSubtitle => "Click to edit the trigger";

    /// <summary>
    /// Pushes the trigger summary onto the canvas card (no-op until the view
    /// attached its canvas). Called on load and on every trigger-property change.
    /// </summary>
    public void UpdateTriggerCard() => _canvas?.SetTriggerCard(TriggerCardTitle, TriggerCardSubtitle);

    public bool IsTriggerButton => SelectedTriggerType?.Key == WorkflowTriggerTypes.ButtonClick;
    public bool IsTriggerSelectMenu => SelectedTriggerType?.Key == WorkflowTriggerTypes.SelectMenu;

    partial void OnSelectedTriggerTypeChanged(FlowTriggerTypeOption value)
    {
        OnPropertyChanged(nameof(IsTriggerCommand));
        OnPropertyChanged(nameof(IsTriggerSlash));
        OnPropertyChanged(nameof(IsTriggerModCommand));
        OnPropertyChanged(nameof(IsTriggerNeedsCommandName));
        OnPropertyChanged(nameof(IsTriggerButtonOrSelect));
        OnPropertyChanged(nameof(IsTriggerEvent));
        OnPropertyChanged(nameof(IsTriggerTimer));
        OnPropertyChanged(nameof(IsTriggerManual));
        OnPropertyChanged(nameof(TriggerSummaryText));
        UpdateTriggerCard();
        MarkDirty();
    }

    partial void OnFlowNameChanged(string value) => MarkDirty();
    partial void OnFlowEnabledChanged(bool value) => MarkDirty();
    partial void OnTriggerCommandNameChanged(string value)
    {
        OnPropertyChanged(nameof(TriggerSummaryText));
        UpdateTriggerCard();
        MarkDirty();
    }
    partial void OnTriggerCommandPrefixChanged(string value)
    {
        OnPropertyChanged(nameof(TriggerSummaryText));
        UpdateTriggerCard();
        MarkDirty();
    }
    partial void OnTriggerDescriptionChanged(string value) => MarkDirty();
    partial void OnTriggerEventTypeChanged(string value)
    {
        UpdateTriggerCard();
        MarkDirty();
    }
    partial void OnTriggerPlatformChanged(string value)
    {
        UpdateTriggerCard();
        MarkDirty();
    }
    partial void OnTriggerIntervalMinutesChanged(string value)
    {
        UpdateTriggerCard();
        MarkDirty();
    }
    partial void OnTriggerMinimumTierChanged(string value)
    {
        UpdateTriggerCard();
        MarkDirty();
    }

    // ======================================================================
    // Command options
    // ======================================================================

    public static IReadOnlyList<FlowOptionTypeOption> OptionTypes { get; } = new List<FlowOptionTypeOption>
    {
        new(WorkflowOptionTypes.Cooldown, "Cooldown seconds", "30"),
        new(WorkflowOptionTypes.Permission, "Require moderator", "Moderator"),
        new(WorkflowOptionTypes.ChannelRestriction, "Channel restriction", "general, support"),
        new(WorkflowOptionTypes.DmOnly, "DM only", "true"),
        new(WorkflowOptionTypes.GuildOnly, "Guild only", "true"),
        new(WorkflowOptionTypes.Nsfw, "NSFW flag", "true")
    };

    /// <summary>Instance view of <see cref="OptionTypes"/> for XAML bindings. The generic
    /// "Require moderator" row is redundant for mod_command triggers (the trigger's
    /// MinimumTier already gates the tier), so it is not offered there; existing
    /// permission rows still load and save untouched.</summary>
    public IReadOnlyList<FlowOptionTypeOption> OptionTypeChoices =>
        IsTriggerModCommand
            ? OptionTypes.Where(t => t.Key != WorkflowOptionTypes.Permission).ToList()
            : OptionTypes;

    [ObservableProperty]
    private ObservableCollection<FlowOptionRowViewModel> _optionRows = new();

    // ======================================================================
    // Construction / teardown
    // ======================================================================

    /// <summary>
    /// Pinned DI signature (registered by the host's service configuration —
    /// do not change without updating the registration).
    /// </summary>
    public FlowEditorViewModel(
        IDataService dataService,
        WorkflowTriggerService triggerService,
        WorkflowDiscordBridge discordBridge,
        MarketplaceService marketplaceService,
        ILogger<FlowEditorViewModel> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _triggerService = triggerService ?? throw new ArgumentNullException(nameof(triggerService));
        _discordBridge = discordBridge ?? throw new ArgumentNullException(nameof(discordBridge));
        _marketplace = marketplaceService ?? throw new ArgumentNullException(nameof(marketplaceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _selectedTriggerType = TriggerTypes.First(t => t.Key == WorkflowTriggerTypes.Manual);

        foreach (BlockDefinition block in BlockCatalog.Blocks)
        {
            var item = new FlowPaletteItemViewModel(block);
            if (string.Equals(block.Group, BlockGroup.Condition, StringComparison.OrdinalIgnoreCase))
            {
                ConditionBlocks.Add(item);
            }
            else
            {
                ActionBlocks.Add(item);
            }
        }

        OptionRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoOptions));
            MarkDirty();
        };

        // Undo snapshots are debounced so a burst of graph edits (drag, wire,
        // config typing) lands as one history entry. The same tick refreshes
        // the Variables side panel (flow-assigned {tokens} track set_variable
        // nodes) so a burst of edits updates the list once, not per keystroke.
        _snapshotDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _snapshotDebounce.Tick += (_, _) =>
        {
            _snapshotDebounce.Stop();
            PushUndoSnapshot();
            RefreshFlowVariables();
        };

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autosaveTimer.Tick += async (_, _) => await SaveDraftAsync();

        ApplyPaletteFilter();
    }

    /// <summary>
    /// Wires the view's canvas into the editor (collection, selection and
    /// graph-change plumbing). Called from FlowEditorView on Loaded.
    /// </summary>
    public void AttachCanvas(FlowCanvasControl canvas)
    {
        if (canvas == null || ReferenceEquals(_canvas, canvas))
        {
            return;
        }

        _canvas = canvas;
        canvas.Nodes = Nodes;
        canvas.GraphChanged += OnCanvasGraphChanged;
        canvas.NodeSelected += OnCanvasNodeSelected;
        canvas.EntryNodeChanged += OnCanvasEntryNodeChanged;
        canvas.ErrorNodeChanged += OnCanvasErrorNodeChanged;
        canvas.ComponentAddRequested += OnComponentAddRequested;
        canvas.ComponentRemoveRequested += OnComponentRemoveRequested;
        UpdateTriggerCard(); // the card may have been missed before the view attached
        RefreshParameterCards(); // parameter cards may have been missed before the view attached
        ApplyEntryErrorTargets(); // entry/error pins may have loaded before the attach
        EnsureComponentSubNodes(); // component chips may reference sub-nodes not yet spawned
        EnsureAttachmentCards(); // loop/comparison/condition attachment cards (legacy flows may lack them)

        // Frame the graph once the canvas has measured (load may have finished
        // before the view attached).
        if (Nodes.Count > 0)
        {
            FitCanvasAfterLayout();
        }
    }

    /// <summary>Unhooks the canvas (view unload) so stale references never leak.</summary>
    public void DetachCanvas()
    {
        if (_canvas == null)
        {
            return;
        }

        _canvas.GraphChanged -= OnCanvasGraphChanged;
        _canvas.NodeSelected -= OnCanvasNodeSelected;
        _canvas.EntryNodeChanged -= OnCanvasEntryNodeChanged;
        _canvas.ErrorNodeChanged -= OnCanvasErrorNodeChanged;
        _canvas.ComponentAddRequested -= OnComponentAddRequested;
        _canvas.ComponentRemoveRequested -= OnComponentRemoveRequested;
        _canvas = null;
    }

    // ======================================================================
    // Initialize / load
    // ======================================================================

    /// <summary>
    /// Loads a workflow into the editor. A null workflow starts a fresh flow
    /// (fresh id, manual trigger, default platform); an unsaved draft for the
    /// flow id is silently restored over the persisted state.
    /// </summary>
    public async Task InitializeAsync(Workflow? existingWorkflow, string? defaultPlatform)
    {
        _existingWorkflow = existingWorkflow;
        _workflowId = existingWorkflow?.Id ?? Guid.NewGuid().ToString("N");
        _existingCategory = existingWorkflow?.Category;
        _existingCreatedAt = existingWorkflow?.CreatedAt ?? DateTime.UtcNow;
        _sourceCommandId = null;
        _loadedFromImport = false;
        _cascadeCount = 0;
        _componentSubNodes.Clear();

        // Entry/error pins start from the loaded workflow; draft/undo snapshots
        // overwrite them below when present.
        _entryNodeId = existingWorkflow?.EntryNodeId;
        _errorHandlerEntryId = existingWorkflow?.ErrorHandlerEntryId;

        ClearHistory();
        SelectedNode = null;
        ResetParameterEditor(); // a wholesale reload never keeps a stale parameter editor

        FlowTriggerDto? draftTrigger = null;
        List<FlowOptionDto>? draftOptions = null;
        List<FlowParameterDto>? draftParameters = null;
        List<FlowNodeDto>? draftNodes = null;
        string? draftName = null;
        string? draftDescription = null;
        bool draftEnabled = true;
        string? draftEntryNodeId = null;
        string? draftErrorNodeId = null;
        bool restoredDraft = false;

        // Draft restore: existing flows restore {Id}.json; brand-new flows
        // additionally check the stable "unsaved.json" key.
        var candidates = new List<string> { DraftPathFor(_workflowId) };
        if (existingWorkflow == null)
        {
            candidates.Add(DraftPathFor("unsaved"));
        }

        foreach (string path in candidates)
        {
            var draft = TryReadSnapshot(path);
            if (draft != null)
            {
                draftName = draft.Name;
                draftDescription = draft.Description;
                draftEnabled = draft.Enabled;
                draftTrigger = draft.Trigger;
                draftOptions = draft.Options;
                draftParameters = draft.Parameters;
                draftNodes = draft.Nodes;
                draftEntryNodeId = draft.EntryNodeId;
                draftErrorNodeId = draft.ErrorHandlerEntryId;
                restoredDraft = true;
                break;
            }
        }

        if (restoredDraft)
        {
            FlowName = draftName ?? existingWorkflow?.Name ?? string.Empty;
            FlowEnabled = draftEnabled;
            TriggerDescription = draftDescription ?? existingWorkflow?.Description ?? string.Empty;
            LoadTriggerIntoForm(draftTrigger ?? TriggerFromWorkflow(existingWorkflow?.Trigger, defaultPlatform));
            LoadOptionsIntoForm(draftOptions?.Select(o => new WorkflowOption { Type = o.Type, Value = o.Value }));
            LoadParametersIntoForm(draftParameters);
            _entryNodeId = draftEntryNodeId;
            _errorHandlerEntryId = draftErrorNodeId;
            LoadNodesIntoCanvas(draftNodes ?? new List<FlowNodeDto>());
            IsDirty = true; // restored work is by definition unsaved
            StatusText = "Restored your unsaved draft — SAVE to keep it.";
            _logger.LogInformation("Restored flow editor draft for workflow {WorkflowId}", _workflowId);
        }
        else if (existingWorkflow != null)
        {
            FlowName = existingWorkflow.Name;
            FlowEnabled = existingWorkflow.Enabled;
            TriggerDescription = existingWorkflow.Description ?? string.Empty;
            LoadTriggerIntoForm(TriggerFromWorkflow(existingWorkflow.Trigger, defaultPlatform));
            LoadOptionsIntoForm(existingWorkflow.Options);
            LoadParametersIntoForm(existingWorkflow.Parameters?.Select(p => new FlowParameterDto
            {
                Name = p.Name,
                Type = p.Type,
                Description = p.Description,
                Required = p.Required
            }));
            LoadNodesFromWorkflow(existingWorkflow);
            IsDirty = false;
            StatusText = $"Editing \"{existingWorkflow.Name}\" — SAVE keeps the same workflow id.";
        }
        else
        {
            FlowName = string.Empty;
            FlowEnabled = true;
            TriggerDescription = string.Empty;
            LoadTriggerIntoForm(TriggerFromWorkflow(null, defaultPlatform));
            LoadOptionsIntoForm(null);
            LoadParametersIntoForm(null);
            LoadNodesIntoCanvas(new List<FlowNodeDto>());
            IsDirty = false;
            StatusText = "New flow — drag a block from the left palette to get started.";
        }

        OnPropertyChanged(nameof(TriggerComponentHint));
        OnPropertyChanged(nameof(TriggerSummaryText));
        UpdateTriggerCard();
        PushUndoSnapshot(); // baseline history entry

        // Side panels: rebuild from the freshly-loaded graph / parameter list,
        // and re-query the workflow + execution lists for the new flow id.
        RefreshFlowVariables();
        RefreshParameterCards();
        RefreshBlockTemplates();
        _ = LoadSidePanelWorkflowsAsync();
        _ = LoadErrorLogsAsync();

        await Task.CompletedTask;
        _autosaveTimer.Start();
        FitCanvasAfterLayout();
    }

    /// <summary>
    /// Loads the editor from a Command row instead of a persisted workflow:
    /// the row is converted into an equivalent flow via
    /// <see cref="CommandFlowTemplates.FromCommand"/> (trigger "command",
    /// prefix "!", nodes for the built-in behavior / random replies / action
    /// steps) and pushed through the exact same load path
    /// <see cref="InitializeAsync"/> uses for an existing workflow, so canvas,
    /// trigger form and options form all populate.
    /// <para>
    /// The flow keeps its freshly generated id — SAVE therefore INSERTS it as
    /// a new workflow that carries the command's id as
    /// <see cref="Workflow.SourceCommandId"/>, which drives the save-time
    /// adoption (the legacy command row is retired so it never double-fires).
    /// </para>
    /// </summary>
    public async Task InitializeFromCommandAsync(VyralBot.Data.Entities.Command command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        Workflow flow = CommandFlowTemplates.FromCommand(command);
        await InitializeAsync(flow, null);

        // InitializeAsync cleared the adoption marker — set it after the load
        // on both the working copy and the model held for the next save.
        _sourceCommandId = command.Id;
        if (_existingWorkflow != null)
        {
            _existingWorkflow.SourceCommandId = command.Id;
        }

        StatusText = $"Editing {command.Name} in the Visual Builder — SAVE moves it into your flows.";
        _logger.LogInformation("Flow editor seeded from command {CommandName} ({CommandId})",
            command.Name, command.Id);
    }

    /// <summary>Maps a persisted trigger into the toolbar form fields.</summary>
    private void LoadTriggerIntoForm(FlowTriggerDto trigger)
    {
        _selectedTriggerType = TriggerTypes.FirstOrDefault(t => string.Equals(t.Key, trigger.Type, StringComparison.OrdinalIgnoreCase))
                               ?? TriggerTypes.First(t => t.Key == WorkflowTriggerTypes.Manual);
        OnPropertyChanged(nameof(SelectedTriggerType));
        NotifyTriggerFlagsChanged();

        TriggerCommandName = trigger.CommandName ?? string.Empty;
        TriggerCommandPrefix = string.IsNullOrWhiteSpace(trigger.CommandPrefix) ? "!" : trigger.CommandPrefix;
        TriggerEventType = trigger.EventType ?? string.Empty;
        TriggerPlatform = string.IsNullOrWhiteSpace(trigger.Platform) ? "Any" : trigger.Platform.Trim();
        if (!PlatformOptions.Contains(TriggerPlatform))
        {
            PlatformOptions.Add(TriggerPlatform);
        }
        TriggerIntervalMinutes = (trigger.IntervalMinutes ?? 15).ToString();
        TriggerMinimumTier = ModTiers.NormalizeTier(trigger.MinimumTier) ?? "Mod";
    }

    private void NotifyTriggerFlagsChanged()
    {
        OnPropertyChanged(nameof(IsTriggerCommand));
        OnPropertyChanged(nameof(IsTriggerSlash));
        OnPropertyChanged(nameof(IsTriggerModCommand));
        OnPropertyChanged(nameof(IsTriggerNeedsCommandName));
        OnPropertyChanged(nameof(IsTriggerButtonOrSelect));
        OnPropertyChanged(nameof(IsTriggerEvent));
        OnPropertyChanged(nameof(IsTriggerTimer));
        OnPropertyChanged(nameof(IsTriggerManual));
    }

    private static FlowTriggerDto TriggerFromWorkflow(WorkflowTrigger? trigger, string? defaultPlatform) => new()
    {
        Type = trigger?.Type ?? WorkflowTriggerTypes.Manual,
        CommandName = trigger?.CommandName,
        CommandPrefix = trigger?.CommandPrefix,
        IntervalMinutes = trigger?.IntervalMinutes,
        EventType = trigger?.EventType,
        Platform = string.IsNullOrWhiteSpace(trigger?.Platform) ? (string.IsNullOrWhiteSpace(defaultPlatform) ? "Any" : defaultPlatform) : trigger!.Platform,
        Description = null,
        MinimumTier = trigger?.MinimumTier
    };

    private void LoadOptionsIntoForm(IEnumerable<WorkflowOption>? options)
    {
        OptionRows.Clear();
        foreach (WorkflowOption option in options ?? Enumerable.Empty<WorkflowOption>())
        {
            var type = OptionTypes.FirstOrDefault(t => string.Equals(t.Key, option.Type, StringComparison.OrdinalIgnoreCase));
            if (type == null || string.IsNullOrWhiteSpace(option.Value))
            {
                continue; // tolerate unknown/empty rows
            }
            OptionRows.Add(new FlowOptionRowViewModel(type, option.Value));
        }
    }

    /// <summary>Maps persisted workflow nodes onto the canvas (fresh collection).</summary>
    private void LoadNodesFromWorkflow(Workflow workflow)
    {
        var dtos = (workflow.Nodes ?? new List<WorkflowNode>()).Select(n => new FlowNodeDto
        {
            Id = n.Id,
            BlockType = n.Type,
            Label = NodeLabel(n.Type),
            X = n.X,
            Y = n.Y,
            Config = new Dictionary<string, string>(n.Config ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            Next = n.Next,
            BranchTrue = n.BranchTrue,
            BranchFalse = n.BranchFalse,
            BranchError = n.BranchError,
            Branches = n.Branches == null ? null : new Dictionary<string, string>(n.Branches, StringComparer.Ordinal)
        }).ToList();

        LoadNodesIntoCanvas(dtos);
    }

    /// <summary>WorkflowNode has no label column — the canvas shows the catalog title.</summary>
    private static string NodeLabel(string blockType) => BlockCatalog.Find(blockType)?.Title ?? blockType;

    private void LoadNodesIntoCanvas(List<FlowNodeDto> dtos)
    {
        var collection = new ObservableCollection<FlowCanvasNode>();
        foreach (FlowNodeDto dto in dtos)
        {
            collection.Add(new FlowCanvasNode
            {
                Id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id,
                BlockType = dto.BlockType,
                Label = string.IsNullOrWhiteSpace(dto.Label) ? NodeLabel(dto.BlockType) : dto.Label,
                X = dto.X,
                Y = dto.Y,
                Config = new Dictionary<string, string>(dto.Config, StringComparer.Ordinal),
                NextTargetId = dto.Next,
                BranchTrueTargetId = dto.BranchTrue,
                BranchFalseTargetId = dto.BranchFalse,
                BranchErrorTargetId = dto.BranchError,
                BranchTargets = dto.Branches == null || dto.Branches.Count == 0
                    ? null
                    : new Dictionary<string, string>(dto.Branches, StringComparer.Ordinal)
            });
        }

        _suppressSnapshot = true;
        try
        {
            Nodes = collection;
            OnPropertyChanged(nameof(Nodes));
            if (_canvas != null)
            {
                _canvas.Nodes = collection;
                ApplyEntryErrorTargets();
                EnsureComponentSubNodes(); // spawn missing button/menu sub-nodes for loaded configs
                EnsureAttachmentCards(); // spawn missing loop/comparison/condition attachment cards (undo, drafts, imports)
            }
        }
        finally
        {
            _suppressSnapshot = false;
        }
    }

    /// <summary>Pushes the pinned entry / error-handler targets onto the canvas (no-op before attach).</summary>
    private void ApplyEntryErrorTargets()
    {
        if (_canvas == null)
        {
            return;
        }

        _canvas.SetEntryTarget(_entryNodeId);
        _canvas.SetErrorTarget(_errorHandlerEntryId);
    }

    /// <summary>
    /// Refills the typed-parameter rows (popup + violet chips) from a workflow /
    /// snapshot parameter list. Tolerates null and unknown types (kept verbatim
    /// so nothing authored elsewhere is silently dropped).
    /// </summary>
    private void LoadParametersIntoForm(IEnumerable<FlowParameterDto>? parameters)
    {
        var rows = new ObservableCollection<WorkflowParameterRowViewModel>();
        foreach (FlowParameterDto parameter in parameters ?? Enumerable.Empty<FlowParameterDto>())
        {
            if (string.IsNullOrWhiteSpace(parameter.Name) && string.IsNullOrWhiteSpace(parameter.Type))
            {
                continue; // tolerate junk rows
            }

            rows.Add(new WorkflowParameterRowViewModel(
                parameter.Type,
                parameter.Name,
                parameter.Description ?? string.Empty,
                parameter.Required));
        }

        ParameterRows = rows;
        HookParameterRows();
    }

    /// <summary>(Re)subscribes the parameter rows' edit events (dirty + card refresh).</summary>
    private void HookParameterRows()
    {
        foreach (WorkflowParameterRowViewModel row in ParameterRows)
        {
            row.Changed -= OnParameterRowChanged;
            row.Changed += OnParameterRowChanged;
        }
    }

    /// <summary>
    /// Any typed-parameter row edit (Parameter mode, Command Settings list):
    /// marks the flow dirty, refreshes the Variables {tokens} list and repaints
    /// the canvas parameter card that mirrors the row.
    /// </summary>
    private void OnParameterRowChanged(object? sender, EventArgs e)
    {
        MarkDirty();
        RefreshFlowVariables(); // renamed/added parameters change the {tokens} list
        RefreshParameterCards();
    }

    /// <summary>Frames the graph once the canvas has measured (next dispatcher pass).</summary>
    private void FitCanvasAfterLayout()
    {
        if (_canvas == null)
        {
            return;
        }

        _ = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
        {
            try
            {
                _canvas?.FitToContent();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FitToContent after load failed");
            }
        }, DispatcherPriority.Loaded);
    }

    // ======================================================================
    // Palette
    // ======================================================================

    partial void OnPaletteSearchChanged(string value) => ApplyPaletteFilter();

    private void ApplyPaletteFilter()
    {
        FilterWithGroupHeaders(ActionBlocks, FilteredActionBlocks);
        FilterWithGroupHeaders(ConditionBlocks, FilteredConditionBlocks);

        FilteredOptionDefinitions.Clear();
        FilteredParameterDefinitions.Clear();
        string optionsNeedle = PaletteSearch?.Trim() ?? string.Empty;
        foreach (FlowParameterDefinition parameter in ParameterDefinitions)
        {
            if (optionsNeedle.Length == 0 ||
                parameter.Label.Contains(optionsNeedle, StringComparison.OrdinalIgnoreCase) ||
                parameter.Description.Contains(optionsNeedle, StringComparison.OrdinalIgnoreCase))
            {
                FilteredParameterDefinitions.Add(parameter);
            }
        }
        foreach (FlowOptionDefinition option in BlockCatalog.OptionDefinitions)
        {
            if (optionsNeedle.Length == 0 ||
                option.Label.Contains(optionsNeedle, StringComparison.OrdinalIgnoreCase) ||
                option.Description.Contains(optionsNeedle, StringComparison.OrdinalIgnoreCase))
            {
                FilteredOptionDefinitions.Add(option);
            }
        }

        OnPropertyChanged(nameof(HasNoMatchingBlocks));
        OnPropertyChanged(nameof(ActionsEmpty));
        OnPropertyChanged(nameof(ConditionsEmpty));
        OnPropertyChanged(nameof(OptionsEmpty));
        OnPropertyChanged(nameof(ParametersEmpty));

        void FilterWithGroupHeaders(
            IReadOnlyList<FlowPaletteItemViewModel> source,
            ObservableCollection<FlowPaletteRowViewModel> target)
        {
            target.Clear();
            string needle = PaletteSearch?.Trim() ?? string.Empty;
            string? lastSection = null;
            foreach (FlowPaletteItemViewModel item in source)
            {
                if (needle.Length > 0 &&
                    !item.Block.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                    !item.Block.Description.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // BotGhost-style bold group header whenever Section changes
                // (empty sections are skipped — flat list fallback).
                string section = item.Block.Section;
                if (!string.IsNullOrWhiteSpace(section) &&
                    !string.Equals(section, lastSection, StringComparison.Ordinal))
                {
                    lastSection = section;
                    target.Add(FlowPaletteRowViewModel.Header(section));
                }

                target.Add(FlowPaletteRowViewModel.For(item));
            }
        }
    }

    /// <summary>
    /// Double-click on a palette item: drops a new node at the center of the
    /// current view plus a small cascade offset.
    /// </summary>
    [RelayCommand]
    public void PlaceBlock(FlowPaletteItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        (double centerX, double centerY) = GetViewportCenterWorld();
        double offset = (_cascadeCount++ % 6) * 28d;
        var node = _canvas?.CreateNode(item.Block.Type, centerX - 90d + offset, centerY - 24d + offset);
        if (node == null)
        {
            // No canvas attached (should not happen once the view is loaded) —
            // create through the collection directly so the click still works.
            node = new FlowCanvasNode { BlockType = item.Block.Type, Label = item.Block.Title, X = 400 + offset, Y = 300 + offset };
            PrefillConfig(node, item.Block);
            SeedPlacedComparison(node);
            Nodes.Add(node);
            SelectedNode = node;
        }

        MarkDirty();
    }

    /// <summary>
    /// Seeds a freshly PLACED comparison node with Config["Comparisons"] = "[]"
    /// (multi-rule mode, zero rules) so it matches the canvas placement path:
    /// the Else port shows, the Else card auto-attaches wired and the engine
    /// outputs "else" until rules are added. Legacy loaded nodes are never
    /// seeded here — placement paths only.
    /// </summary>
    private static void SeedPlacedComparison(FlowCanvasNode node)
    {
        if (IsComparisonNode(node))
        {
            node.Config[ComparisonConfigKey] = "[]";
        }
    }

    private static void PrefillConfig(FlowCanvasNode node, BlockDefinition block)
    {
        foreach (ParamField param in block.Params)
        {
            if (!string.IsNullOrEmpty(param.DefaultValue))
            {
                node.Config[param.Key] = param.DefaultValue!;
            }
        }
    }

    /// <summary>World-space center of the canvas viewport (canvas center as fallback).</summary>
    private (double X, double Y) GetViewportCenterWorld()
    {
        const double worldCenterX = 3000d;
        const double worldCenterY = 2000d;

        if (_canvas?.Template == null)
        {
            return (worldCenterX, worldCenterY);
        }

        try
        {
            if (_canvas.Template.FindName("PART_ScrollViewer", _canvas) is System.Windows.Controls.ScrollViewer scrollViewer
                && scrollViewer.ViewportWidth > 0)
            {
                double zoom = _canvas.Zoom <= 0 ? 1d : _canvas.Zoom;
                return (
                    (scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth / 2d) / zoom,
                    (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight / 2d) / zoom);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve the canvas viewport center");
        }

        return (worldCenterX, worldCenterY);
    }

    // ======================================================================
    // Canvas event plumbing
    // ======================================================================

    private void OnCanvasGraphChanged(object? sender, EventArgs e)
    {
        MarkDirty();
        EnsureAttachmentCards(); // freshly dropped loops/comparisons/conditions get their cards; deleted parents take theirs along
        if (!_suppressSnapshot)
        {
            _snapshotDebounce.Stop();
            _snapshotDebounce.Start();
        }
    }

    private void OnCanvasNodeSelected(object? sender, FlowCanvasNode? node)
    {
        SelectedNode = node;
    }

    private void OnCanvasEntryNodeChanged(object? sender, string? nodeId)
    {
        _entryNodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
        MarkDirty();
    }

    private void OnCanvasErrorNodeChanged(object? sender, string? nodeId)
    {
        _errorHandlerEntryId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
        MarkDirty();
    }

    // ======================================================================
    // Message-node components: buttons & select menus
    // ======================================================================

    /// <summary>True for the block types that can carry Discord components.</summary>
    private static bool IsMessageBlock(string blockType) =>
        string.Equals(blockType, "send_message", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(blockType, "send_embed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mapping key for a component sub-node ("{parentNodeId}:{kind}:{index}").</summary>
    private static string ComponentMapKey(string parentNodeId, string kind, int index) =>
        $"{parentNodeId}:{kind}:{index}";

    /// <summary>Config["ComponentKey"] the canvas stamps on component sub-nodes ("button0", "menu0").</summary>
    private static string SubNodeComponentKey(string kind, int index) => $"{kind}{index}";

    /// <summary>Canvas BlockType of a component sub-node.</summary>
    private static string SubNodeBlockType(string kind) =>
        string.Equals(kind, "menu", StringComparison.OrdinalIgnoreCase) ? "menu_start" : "button_start";

    /// <summary>Parses Config["Buttons"] into a mutable payload list (tolerates missing/malformed JSON).</summary>
    private static List<Dictionary<string, object?>> ParseButtonsConfig(FlowCanvasNode node)
    {
        var buttons = new List<Dictionary<string, object?>>();
        if (!node.Config.TryGetValue("Buttons", out string? json) || string.IsNullOrWhiteSpace(json))
        {
            return buttons;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    buttons.Add(new Dictionary<string, object?>
                    {
                        ["label"] = TryGetString(element, "label"),
                        ["style"] = TryGetString(element, "style"),
                        ["emoji"] = TryGetString(element, "emoji")
                    });
                }
            }
        }
        catch (JsonException)
        {
            // Malformed stored JSON — treat as no buttons.
        }

        return buttons;
    }

    private static string? TryGetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Writes the button payload back into Config["Buttons"] (removed when empty).</summary>
    private static void WriteButtonsConfig(FlowCanvasNode node, List<Dictionary<string, object?>> buttons)
    {
        if (buttons.Count == 0)
        {
            node.Config.Remove("Buttons");
            return;
        }

        node.Config["Buttons"] = JsonSerializer.Serialize(
            buttons.Take(5).ToList(), JsonOpts.Indented);
    }

    /// <summary>
    /// Canvas ComponentAddRequested: the user clicked a "+ Button" / "+ Menu"
    /// chip on a message node. Appends the default config, refreshes the node's
    /// chips, then spawns (and for buttons selects) the sub-node.
    /// </summary>
    private void OnComponentAddRequested(object? sender, (string NodeId, string Kind) e)
    {
        FlowCanvasNode? node = Nodes.FirstOrDefault(n => string.Equals(n.Id, e.NodeId, StringComparison.Ordinal));
        if (node == null)
        {
            return;
        }

        if (string.Equals(e.Kind, "button", StringComparison.OrdinalIgnoreCase))
        {
            var buttons = ParseButtonsConfig(node);
            if (buttons.Count >= 5)
            {
                StatusText = "Discord allows at most 5 buttons per message.";
                return;
            }

            buttons.Add(new Dictionary<string, object?>
            {
                ["label"] = $"Button {buttons.Count + 1}",
                ["style"] = "Secondary"
            });
            WriteButtonsConfig(node, buttons);
            _canvas?.RefreshNode(node.Id);

            int index = Math.Min(buttons.Count, 5) - 1;
            FlowCanvasNode? sub = _canvas?.AddComponentNode(node.Id, "button", index);
            _componentSubNodes[ComponentMapKey(node.Id, "button", index)] = sub?.Id ?? string.Empty;
            if (sub != null)
            {
                sub.IsSelected = true; // select the fresh sub-node on the canvas
            }

            MarkDirty();
            StatusText = $"Button {index + 1} added — wire its sub-flow, then SAVE so clicks run it.";
        }
        else if (string.Equals(e.Kind, "menu", StringComparison.OrdinalIgnoreCase))
        {
            bool hadMenu = node.Config.TryGetValue("Menu", out string? menuJson) && !string.IsNullOrWhiteSpace(menuJson);
            if (!hadMenu)
            {
                node.Config["Menu"] = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["placeholder"] = "Choose an option...",
                    ["options"] = new List<object?>()
                }, JsonOpts.Indented);
                _canvas?.RefreshNode(node.Id);
            }

            FlowCanvasNode? sub = _canvas?.AddComponentNode(node.Id, "menu", 0);
            _componentSubNodes[ComponentMapKey(node.Id, "menu", 0)] = sub?.Id ?? string.Empty;
            MarkDirty();
            StatusText = hadMenu
                ? "Select menu sub-flow opened."
                : "Select menu added — set its placeholder/options here, wire its sub-flow, then SAVE.";
        }
    }

    /// <summary>
    /// Canvas ComponentRemoveRequested: a chip's ✕ was clicked. Drops the
    /// button (or the whole menu) from the node config, refreshes the node,
    /// removes the matching sub-node and re-indexes the remaining button subs.
    /// </summary>
    private void OnComponentRemoveRequested(object? sender, (string NodeId, string Kind, int Index) e)
    {
        FlowCanvasNode? node = Nodes.FirstOrDefault(n => string.Equals(n.Id, e.NodeId, StringComparison.Ordinal));
        if (node != null)
        {
            if (string.Equals(e.Kind, "button", StringComparison.OrdinalIgnoreCase))
            {
                var buttons = ParseButtonsConfig(node);
                if (e.Index >= 0 && e.Index < buttons.Count)
                {
                    buttons.RemoveAt(e.Index);
                    WriteButtonsConfig(node, buttons);
                }

                _canvas?.RefreshNode(node.Id);
                ReindexButtonSubNodes(node, e.Index);
            }
            else if (string.Equals(e.Kind, "menu", StringComparison.OrdinalIgnoreCase))
            {
                node.Config.Remove("Menu");
                _canvas?.RefreshNode(node.Id);
            }
        }

        RemoveSubNode(e.NodeId, e.Kind, e.Index);
        MarkDirty();
    }

    /// <summary>
    /// After removing button <paramref name="removedIndex"/>, shifts the
    /// tracked sub-nodes (and their ComponentKey configs) down so indexes stay
    /// aligned with the serialized button list.
    /// </summary>
    private void ReindexButtonSubNodes(FlowCanvasNode parent, int removedIndex)
    {
        for (int i = removedIndex; i < 5; i++)
        {
            string currentKey = ComponentMapKey(parent.Id, "button", i + 1);
            string targetKey = ComponentMapKey(parent.Id, "button", i);
            if (_componentSubNodes.TryGetValue(currentKey, out string? subId))
            {
                _componentSubNodes.Remove(currentKey);
                _componentSubNodes[targetKey] = subId;

                FlowCanvasNode? sub = Nodes.FirstOrDefault(n => string.Equals(n.Id, subId, StringComparison.Ordinal));
                if (sub != null)
                {
                    sub.Config["ComponentKey"] = SubNodeComponentKey("button", i);
                }
            }
            else
            {
                _componentSubNodes.Remove(targetKey);
            }
        }
    }

    /// <summary>Removes a tracked sub-node from the canvas and the mapping.</summary>
    private void RemoveSubNode(string parentNodeId, string kind, int index)
    {
        string mapKey = ComponentMapKey(parentNodeId, kind, index);
        if (!_componentSubNodes.Remove(mapKey, out string? subId))
        {
            return;
        }

        if (!string.IsNullOrEmpty(subId) && Nodes.Any(n => string.Equals(n.Id, subId, StringComparison.Ordinal)))
        {
            _canvas?.RemoveComponentNode(subId);
        }
    }

    /// <summary>
    /// Makes sure every message-node component config has a matching sub-node
    /// on the canvas, and no orphan sub-nodes linger. Runs after flow loads,
    /// undo restores and canvas attach; matches pre-existing sub-nodes
    /// (loaded flows / snapshots) by their Config["ComponentKey"].
    /// </summary>
    private void EnsureComponentSubNodes()
    {
        if (_canvas == null)
        {
            return;
        }

        // Drop stale tracking entries (their node is gone).
        foreach (string key in _componentSubNodes.Where(kv => string.IsNullOrEmpty(kv.Value) ||
                                                              !Nodes.Any(n => string.Equals(n.Id, kv.Value, StringComparison.Ordinal)))
                                                  .Select(kv => kv.Key)
                                                  .ToList())
        {
            _componentSubNodes.Remove(key);
        }

        var claimed = new HashSet<string>(_componentSubNodes.Values, StringComparer.Ordinal);

        foreach (FlowCanvasNode node in Nodes.ToList())
        {
            if (!IsMessageBlock(node.BlockType))
            {
                continue;
            }

            var buttons = ParseButtonsConfig(node);
            for (int i = 0; i < Math.Min(buttons.Count, 5); i++)
            {
                EnsureSubNode(node, "button", i, claimed);
            }

            bool hasMenu = node.Config.TryGetValue("Menu", out string? menuJson) && !string.IsNullOrWhiteSpace(menuJson);
            if (hasMenu)
            {
                EnsureSubNode(node, "menu", 0, claimed);
            }
            else
            {
                RemoveSubNode(node.Id, "menu", 0);
            }

            for (int i = buttons.Count; i < 5; i++)
            {
                RemoveSubNode(node.Id, "button", i);
            }
        }
    }

    /// <summary>Reuses (or creates) the sub-node for one component of a message node.</summary>
    private void EnsureSubNode(FlowCanvasNode parent, string kind, int index, HashSet<string> claimed)
    {
        string mapKey = ComponentMapKey(parent.Id, kind, index);

        // A tracked id is alive by definition (stale entries were pruned first).
        if (_componentSubNodes.TryGetValue(mapKey, out string? tracked) && !string.IsNullOrEmpty(tracked))
        {
            claimed.Add(tracked);
            return;
        }

        FlowCanvasNode? existing = FindComponentSubNode(parent, kind, index, claimed);
        if (existing != null)
        {
            claimed.Add(existing.Id);
            _componentSubNodes[mapKey] = existing.Id;
            return;
        }

        FlowCanvasNode? created = _canvas?.AddComponentNode(parent.Id, kind, index);
        if (created != null)
        {
            claimed.Add(created.Id);
            _componentSubNodes[mapKey] = created.Id;
        }
    }

    /// <summary>
    /// Finds an unclaimed sub-node matching a parent's component slot: exact
    /// Config["ComponentKey"] match first ("button2"), then any unclaimed
    /// sub-node of the right block type ordered by its numeric key then Y.
    /// </summary>
    private FlowCanvasNode? FindComponentSubNode(FlowCanvasNode parent, string kind, int index, HashSet<string> claimed)
    {
        string blockType = SubNodeBlockType(kind);
        string expectedKey = SubNodeComponentKey(kind, index);

        var candidates = Nodes.Where(n =>
            !claimed.Contains(n.Id) &&
            string.Equals(n.BlockType, blockType, StringComparison.Ordinal)).ToList();

        FlowCanvasNode? exact = candidates.FirstOrDefault(n =>
            n.Config.TryGetValue("ComponentKey", out string? ck) &&
            string.Equals(ck, expectedKey, StringComparison.Ordinal));
        if (exact != null)
        {
            return exact;
        }

        return candidates
            .OrderBy(n => NumericSuffix(n, kind))
            .ThenBy(n => n.Y)
            .FirstOrDefault();

        static int NumericSuffix(FlowCanvasNode n, string kind)
        {
            if (n.Config.TryGetValue("ComponentKey", out string? ck) &&
                ck.StartsWith(kind, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(ck[kind.Length..], out int value))
            {
                return value;
            }

            return int.MaxValue;
        }
    }

    // ======================================================================
    // Loop, comparison & condition attachment cards (BotGhost-style)
    // ======================================================================

    /// <summary>Canvas BlockType of the loop's "After loop" attachment card.</summary>
    private const string LoopAfterCardType = "loop_after";

    /// <summary>Canvas BlockType of the loop's "Loop # times" attachment card.</summary>
    private const string LoopBodyCardType = "loop_body";

    /// <summary>Canvas BlockType of one comparison rule's case attachment card.</summary>
    private const string ComparisonCaseCardType = "comparison_case";

    /// <summary>Canvas BlockType of the comparison "Else" attachment card.</summary>
    private const string ComparisonElseCardType = "comparison_else";

    /// <summary>WorkflowNode.Config key carrying the comparison rules JSON array.</summary>
    private const string ComparisonConfigKey = "Comparisons";

    /// <summary>Vertical gap between a parent node's bottom and its cards.</summary>
    private const double AttachmentCardGapY = 90d;

    /// <summary>Left card column offset (mirrors component button cards).</summary>
    private const double AttachmentCardLeftX = -160d;

    /// <summary>Right card column offset (mirrors component menu cards).</summary>
    private const double AttachmentCardRightX = 60d;

    /// <summary>Vertical pitch of the stacked comparison case cards.</summary>
    private const double AttachmentCardStackY = 96d;

    /// <summary>True while <see cref="EnsureAttachmentCards"/> mutates the graph, so the
    /// nested GraphChanged events cannot re-enter it.</summary>
    private bool _ensuringAttachmentCards;

    /// <summary>One parsed comparison rule (Config["Comparisons"] JSON row).</summary>
    internal sealed record ComparisonRule(string Left, string Operator, string Right);

    /// <summary>True for the four canvas attachment-card block types.</summary>
    private static bool IsAttachmentCardType(string blockType) =>
        string.Equals(blockType, LoopAfterCardType, StringComparison.Ordinal) ||
        string.Equals(blockType, LoopBodyCardType, StringComparison.Ordinal) ||
        string.Equals(blockType, ComparisonCaseCardType, StringComparison.Ordinal) ||
        string.Equals(blockType, ComparisonElseCardType, StringComparison.Ordinal);

    private static bool IsLoopNode(FlowCanvasNode node) =>
        string.Equals(node.BlockType, "loop", StringComparison.Ordinal);

    private static bool IsComparisonNode(FlowCanvasNode node) =>
        string.Equals(node.BlockType, "comparison", StringComparison.Ordinal);

    /// <summary>
    /// True for condition-family nodes whose False branch auto-attaches an Else
    /// card — the catalog's <see cref="BlockCatalog.UsesElseAttachment"/> set
    /// (if, if_user_has_role, if_message_contains, if_variable_equals,
    /// if_time_between, if_user_is_bot, chance, if_user_permission, if_channel,
    /// if_user). Excludes "comparison" (manages its own case/Else cards via
    /// <see cref="EnsureComparisonCards"/>) and "wait_for_response" (its False
    /// branch is a normal green branch, no Else card).
    /// </summary>
    private static bool IsConditionNode(FlowCanvasNode node) =>
        BlockCatalog.UsesElseAttachment(node.BlockType);

    /// <summary>Mapping key for an attachment card ("{parentNodeId}:{slot}").</summary>
    private static string AttachmentMapKey(string parentNodeId, string slot) => $"{parentNodeId}:{slot}";

    /// <summary>True when a <see cref="_componentSubNodes"/> entry is an attachment card.</summary>
    private static bool IsAttachmentMapKey(string key)
    {
        if (key.EndsWith(":after", StringComparison.Ordinal) ||
            key.EndsWith(":body", StringComparison.Ordinal) ||
            key.EndsWith(":else", StringComparison.Ordinal))
        {
            return true;
        }

        int caseIndex = key.LastIndexOf(":case", StringComparison.Ordinal);
        return caseIndex >= 0 && int.TryParse(key[(caseIndex + 5)..], out _);
    }

    /// <summary>
    /// Makes sure every loop node has its "After loop" + "Loop # times" cards,
    /// every new-style comparison node has its Else + per-rule case cards,
    /// every condition-family node has its Else card on the False branch, and
    /// that no orphan card outlives its parent. Runs after flow loads,
    /// undo restores, imports, canvas attach and every canvas graph change
    /// (so freshly dropped loops/comparisons/conditions get their cards
    /// immediately and deleting a loop/comparison/condition takes its cards
    /// with it).
    /// </summary>
    private void EnsureAttachmentCards()
    {
        if (_canvas == null || _ensuringAttachmentCards)
        {
            return;
        }

        bool anyParent = Nodes.Any(n => IsLoopNode(n) || IsComparisonNode(n) || IsConditionNode(n));
        bool anyTracked = _componentSubNodes.Keys.Any(IsAttachmentMapKey);
        bool anyCard = Nodes.Any(n => IsAttachmentCardType(n.BlockType));
        if (!anyParent && !anyTracked && !anyCard)
        {
            return;
        }

        _ensuringAttachmentCards = true;
        string? selectedId = SelectedNode?.Id;
        try
        {
            PruneOrphanAttachmentCards();

            var claimed = CollectTrackedAttachmentIds();
            foreach (FlowCanvasNode node in Nodes.ToList())
            {
                if (IsLoopNode(node))
                {
                    EnsureLoopCards(node, claimed);
                }
                else if (IsComparisonNode(node))
                {
                    EnsureComparisonCards(node, claimed);
                }
                else if (IsConditionNode(node))
                {
                    EnsureConditionElseCard(node, claimed);
                }
            }
        }
        finally
        {
            _ensuringAttachmentCards = false;
        }

        RestoreSelectionAfterCardWork(selectedId);
    }

    /// <summary>Ids of every alive attachment card currently tracked in the mapping.</summary>
    private HashSet<string> CollectTrackedAttachmentIds() =>
        new(_componentSubNodes
            .Where(kv => IsAttachmentMapKey(kv.Key) && !string.IsNullOrEmpty(kv.Value))
            .Select(kv => kv.Value)
            .Where(id => Nodes.Any(n => string.Equals(n.Id, id, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>
    /// Delete cascade for attachment cards: a card whose loop/comparison/condition
    /// parent is gone is removed (covers canvas Delete-key deletion, panel
    /// deletion, undo restores and imports that drop the parent but kept the
    /// card). Condition Else cards qualify through their "{conditionId}:else"
    /// mapping key (step 1) and their parent's False wire (step 2).
    /// </summary>
    private void PruneOrphanAttachmentCards()
    {
        // 1. Tracked cards whose parent node no longer exists.
        foreach (string key in _componentSubNodes.Keys.Where(IsAttachmentMapKey).ToList())
        {
            string parentId = key[..key.LastIndexOf(':')];
            if (Nodes.Any(n => string.Equals(n.Id, parentId, StringComparison.Ordinal)))
            {
                continue;
            }

            if (_componentSubNodes.Remove(key, out string? cardId) && !string.IsNullOrEmpty(cardId))
            {
                FlowCanvasNode? card = FindNodeById(cardId);
                if (card != null)
                {
                    _canvas?.RemoveComponentNode(card.Id);
                }
            }
        }

        // 2. Untracked cards nothing points at anymore (loop.NextTargetId /
        //    BranchTargets / legacy branch targets of surviving parents).
        foreach (FlowCanvasNode card in Nodes.Where(n => IsAttachmentCardType(n.BlockType)).ToList())
        {
            bool referenced = Nodes.Any(parent =>
                !IsAttachmentCardType(parent.BlockType) &&
                (string.Equals(parent.NextTargetId, card.Id, StringComparison.Ordinal) ||
                 string.Equals(parent.BranchTrueTargetId, card.Id, StringComparison.Ordinal) ||
                 string.Equals(parent.BranchFalseTargetId, card.Id, StringComparison.Ordinal) ||
                 string.Equals(parent.BranchErrorTargetId, card.Id, StringComparison.Ordinal) ||
                 (parent.BranchTargets != null && parent.BranchTargets.ContainsValue(card.Id))));
            if (!referenced)
            {
                _canvas?.RemoveComponentNode(card.Id);
            }
        }
    }

    // ----------------------------------------------------------------------
    // Loop cards
    // ----------------------------------------------------------------------

    /// <summary>
    /// Ensures a loop node has exactly one "After loop" card (the loop's
    /// <c>next</c> port routes through it) and one "Loop # times" card (the
    /// <c>case:body</c> port routes through it). When a card is created, any
    /// pre-existing wiring on that port is re-pointed onto the card so the
    /// user's chain is preserved across the migration.
    /// </summary>
    private void EnsureLoopCards(FlowCanvasNode loop, HashSet<string> claimed)
    {
        // --- "After loop" card (loop.NextTargetId must always target it) ---
        // The pre-existing next-target is captured BEFORE card creation — the
        // canvas's AddAttachmentNode wires the port to the fresh card, and the
        // captured chain is re-pointed onto the card so the user's wiring is
        // preserved across the migration.
        string? previousNext = loop.NextTargetId;
        FlowCanvasNode? after = ResolveAttachmentCard(
            AttachmentMapKey(loop.Id, "after"), LoopAfterCardType, "after", previousNext, claimed);
        if (after == null)
        {
            after = CreateAttachmentCard(loop, BlockCatalog.PortNext, LoopAfterCardType, "After loop", "after");
            if (after != null)
            {
                if (!string.IsNullOrEmpty(previousNext) &&
                    !string.Equals(previousNext, after.Id, StringComparison.Ordinal))
                {
                    after.NextTargetId = previousNext; // preserve the user's chain
                }

                loop.NextTargetId = after.Id;
            }
        }
        else if (!string.Equals(loop.NextTargetId, after.Id, StringComparison.Ordinal))
        {
            loop.NextTargetId = after.Id; // the attachment is part of the loop's shape — re-pin
        }

        // --- "Loop # times" card (Branches["body"] must always target it) ---
        string bodyPortKey = BlockCatalog.PortCasePrefix + "body";
        string? wiredBody = loop.GetTarget(bodyPortKey);
        FlowCanvasNode? body = ResolveAttachmentCard(
            AttachmentMapKey(loop.Id, "body"), LoopBodyCardType, "body", wiredBody, claimed);
        if (body == null)
        {
            body = CreateAttachmentCard(loop, bodyPortKey, LoopBodyCardType, "Loop # times", "body");
            if (body != null)
            {
                if (!string.IsNullOrEmpty(wiredBody) &&
                    !string.Equals(wiredBody, body.Id, StringComparison.Ordinal))
                {
                    body.NextTargetId = wiredBody; // preserve the user's body chain
                }

                loop.SetTarget(bodyPortKey, body.Id);
            }
        }
        else if (!string.Equals(wiredBody, body.Id, StringComparison.Ordinal))
        {
            loop.SetTarget(bodyPortKey, body.Id);
        }
    }

    // ----------------------------------------------------------------------
    // Comparison cards
    // ----------------------------------------------------------------------

    /// <summary>
    /// Ensures a comparison node's attachment cards match its config: new-style
    /// nodes (Config["Comparisons"] present — freshly placed nodes are seeded
    /// with "[]") get one case card per rule plus the Else card; nodes without
    /// the key (legacy Left/Operator/Right flows and any other unseeded node)
    /// keep the legacy True/False canvas with NO cards at all — creating an
    /// Else card there would leave it floating, because the legacy branch
    /// ports/outputs never route through "else". A rules edit in the settings
    /// panel writes the key and migrates the node (cards + wiring) at that point.
    /// </summary>
    private void EnsureComparisonCards(FlowCanvasNode comparison, HashSet<string> claimed)
    {
        if (comparison.Config.ContainsKey(ComparisonConfigKey))
        {
            List<ComparisonRule> rules = ParseComparisons(comparison);
            SyncComparisonCards(comparison, rules, claimed, migrateLegacyWiring: false);
            return;
        }

        // No "Comparisons" key — legacy mode: strip any stray case/Else cards
        // instead of leaving a floating Else card unwired.
        RemoveComparisonCards(comparison);
    }

    /// <summary>True when the node still carries the pre-multi-rule Left/Operator/Right config.</summary>
    private static bool HasLegacyComparisonFields(FlowCanvasNode node) =>
        node.Config.ContainsKey("Left") || node.Config.ContainsKey("Operator") || node.Config.ContainsKey("Right");

    /// <summary>
    /// Reconciles a comparison node's case/Else cards with its (already
    /// written) Config["Comparisons"] rules: retitles existing cards, adopts
    /// shifted cards on removal (preserving their outgoing wiring), creates
    /// missing ones and rewrites BranchTargets["case:{i}"]/["else"] in one go.
    /// </summary>
    private void SyncComparisonCards(FlowCanvasNode comparison, List<ComparisonRule> previousRules, HashSet<string> claimed, bool migrateLegacyWiring)
    {
        List<ComparisonRule> rules = ParseComparisons(comparison);
        int removedIndex = FindRemovedRuleIndex(previousRules, rules);

        // --- case cards (index-keyed; a removal shifts later cards down) ---
        var desiredTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedCards = new HashSet<string>(StringComparer.Ordinal);
        var caseCards = new List<FlowCanvasNode>();
        for (int i = 0; i < rules.Count; i++)
        {
            int source = removedIndex >= 0 && i >= removedIndex ? i + 1 : i;
            string mapKey = AttachmentMapKey(comparison.Id, $"case{i}");
            string portKey = $"case:{i}";
            string? wired = comparison.BranchTargets != null && comparison.BranchTargets.TryGetValue(portKey, out string? target)
                ? target
                : null;

            FlowCanvasNode? card;
            if (source == i)
            {
                card = ResolveAttachmentCard(mapKey, ComparisonCaseCardType, $"case{i}", wired, claimed);
            }
            else
            {
                // Removal shift: adopt the card tracked under the old (shifted)
                // index so its outgoing wiring survives the reindex. The stale
                // resolve path is deliberately skipped — the mapping under
                // case{i} still holds the REMOVED rule's card.
                card = null;
                string sourceKey = AttachmentMapKey(comparison.Id, $"case{source}");
                if (_componentSubNodes.TryGetValue(sourceKey, out string? shiftedId) &&
                    !string.IsNullOrEmpty(shiftedId) &&
                    !usedCards.Contains(shiftedId) &&
                    FindNodeById(shiftedId) is { } shifted &&
                    claimed.Add(shifted.Id))
                {
                    _componentSubNodes.Remove(sourceKey);
                    _componentSubNodes[mapKey] = shifted.Id;
                    shifted.X = ComparisonCardX(comparison);
                    shifted.Y = ComparisonCardY(comparison, caseCards.Count);
                    card = shifted;
                }
            }

            if (card == null)
            {
                card = CreateAttachmentCard(comparison, portKey, ComparisonCaseCardType,
                    BlockCatalog.ComparisonRuleSummary(rules[i].Left, rules[i].Operator, rules[i].Right), $"case{i}");
                if (card != null)
                {
                    // The comparison's case ports are not catalog-declared yet,
                    // so the canvas cannot position them under port dots — lay
                    // the fresh card out in the centered column instead.
                    card.X = ComparisonCardX(comparison);
                    card.Y = ComparisonCardY(comparison, caseCards.Count);
                    _componentSubNodes[mapKey] = card.Id;
                }
            }

            if (card == null)
            {
                continue;
            }

            usedCards.Add(card.Id);
            caseCards.Add(card);
            string label = BlockCatalog.ComparisonRuleSummary(rules[i].Left, rules[i].Operator, rules[i].Right);
            if (!string.Equals(card.Label, label, StringComparison.Ordinal))
            {
                card.Label = label; // canvas re-renders + raises GraphChanged
            }

            desiredTargets[portKey] = card.Id;
        }

        // --- surplus case cards (removed rules whose card was not shifted) ---
        string idPrefix = comparison.Id + ":case";
        foreach (string key in _componentSubNodes.Keys
                     .Where(k => k.StartsWith(idPrefix, StringComparison.Ordinal) && IsAttachmentMapKey(k))
                     .ToList())
        {
            if (_componentSubNodes.TryGetValue(key, out string? cardId) &&
                !string.IsNullOrEmpty(cardId) &&
                !usedCards.Contains(cardId) &&
                FindNodeById(cardId) is { } surplus)
            {
                _componentSubNodes.Remove(key);
                claimed.Remove(cardId);
                _canvas?.RemoveComponentNode(surplus.Id);
            }
        }

        // --- Else card (always the last row of the column) ---
        FlowCanvasNode? elseCard = EnsureElseCard(comparison, claimed, slot: rules.Count);
        if (elseCard != null)
        {
            desiredTargets["else"] = elseCard.Id;
            elseCard.X = ComparisonCardX(comparison);
            elseCard.Y = ComparisonCardY(comparison, rules.Count);
        }

        // --- rewrite BranchTargets in one shot (raises PropertyChanged once) ---
        if (!BranchTargetsMatch(comparison.BranchTargets, desiredTargets))
        {
            comparison.BranchTargets = desiredTargets.Count == 0
                ? null
                : new Dictionary<string, string>(desiredTargets, StringComparer.Ordinal);
            _canvas?.RefreshNode(comparison.Id); // branch port slots follow the rule list
        }

        // --- one-time legacy wiring adoption (first panel save of an old flow) ---
        if (migrateLegacyWiring)
        {
            if (caseCards.Count > 0 &&
                caseCards[0].NextTargetId == null &&
                comparison.BranchTrueTargetId is { } legacyTrue)
            {
                caseCards[0].NextTargetId = legacyTrue;
            }

            if (elseCard is { NextTargetId: null } &&
                comparison.BranchFalseTargetId is { } legacyFalse)
            {
                elseCard.NextTargetId = legacyFalse;
            }

            if (comparison.BranchTrueTargetId != null || comparison.BranchFalseTargetId != null)
            {
                comparison.BranchTrueTargetId = null;
                comparison.BranchFalseTargetId = null;
            }
        }
    }

    /// <summary>Resolves (or creates) the comparison's red "Else" card.</summary>
    private FlowCanvasNode? EnsureElseCard(FlowCanvasNode comparison, HashSet<string> claimed, int slot)
    {
        string mapKey = AttachmentMapKey(comparison.Id, "else");
        string? wired = comparison.BranchTargets != null && comparison.BranchTargets.TryGetValue("else", out string? target)
            ? target
            : null;
        FlowCanvasNode? card = ResolveAttachmentCard(mapKey, ComparisonElseCardType, "else", wired, claimed);
        if (card == null)
        {
            card = CreateAttachmentCard(comparison, "else", ComparisonElseCardType, "Else", "else");
            if (card != null)
            {
                // "else" is not a FlowCanvasNode.SetTarget-known port key (the
                // engine reads Branches["else"] directly), so the canvas could
                // not pre-wire it — the caller's BranchTargets rewrite does.
                card.X = ComparisonCardX(comparison);
                card.Y = ComparisonCardY(comparison, slot);
                _componentSubNodes[mapKey] = card.Id;
            }
        }

        return card;
    }

    /// <summary>Centered X for a comparison attachment card (BotGhost column under the node).</summary>
    private static double ComparisonCardX(FlowCanvasNode comparison) =>
        comparison.X + (FlowNodeVisual.WidthFor(comparison) - FlowNodeVisual.AttachmentCardWidth) / 2d;

    /// <summary>Y for a comparison attachment card: <paramref name="slot"/> rows below the node.</summary>
    private static double ComparisonCardY(FlowCanvasNode comparison, int slot) =>
        comparison.Y + FlowNodeVisual.DefaultHeightFor(comparison) + AttachmentCardGapY + slot * AttachmentCardStackY;

    /// <summary>
    /// Removes every tracked case/Else card of a comparison node and drops its
    /// case:/else BranchTargets (used when a node turns out to be legacy —
    /// the runtime's BranchTrue/False path stays authoritative until the user
    /// edits the rules in the panel).
    /// </summary>
    private void RemoveComparisonCards(FlowCanvasNode comparison)
    {
        string idPrefix = comparison.Id + ":";
        foreach (string key in _componentSubNodes.Keys
                     .Where(k => k.StartsWith(idPrefix, StringComparison.Ordinal) && IsAttachmentMapKey(k))
                     .ToList())
        {
            if (_componentSubNodes.Remove(key, out string? cardId) && !string.IsNullOrEmpty(cardId) &&
                FindNodeById(cardId) is { } card)
            {
                _canvas?.RemoveComponentNode(card.Id);
            }
        }

        if (comparison.BranchTargets is { } targets &&
            (targets.ContainsKey("else") || targets.Keys.Any(k => k.StartsWith("case:", StringComparison.Ordinal))))
        {
            Dictionary<string, string> cleaned = targets
                .Where(kv => !string.Equals(kv.Key, "else", StringComparison.Ordinal) &&
                             !kv.Key.StartsWith("case:", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            comparison.BranchTargets = cleaned.Count == 0 ? null : cleaned;
        }
    }

    /// <summary>
    /// First index whose rule disappeared between the previous and current rule
    /// lists (-1 when nothing was removed — appends and in-place edits keep
    /// every card at its index). Used to shift case cards down on removal.
    /// </summary>
    private static int FindRemovedRuleIndex(List<ComparisonRule> previous, List<ComparisonRule> current)
    {
        if (current.Count >= previous.Count)
        {
            return -1;
        }

        for (int i = 0; i < current.Count; i++)
        {
            if (!string.Equals(previous[i].Left, current[i].Left, StringComparison.Ordinal) ||
                !string.Equals(previous[i].Operator, current[i].Operator, StringComparison.Ordinal) ||
                !string.Equals(previous[i].Right, current[i].Right, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return current.Count; // trailing rules were removed
    }

    private static bool BranchTargetsMatch(Dictionary<string, string>? current, Dictionary<string, string> desired)
    {
        if (current == null)
        {
            return desired.Count == 0;
        }

        return current.Count == desired.Count && current.All(kv =>
            desired.TryGetValue(kv.Key, out string? value) &&
            string.Equals(kv.Value, value, StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------------
    // Condition Else cards
    // ----------------------------------------------------------------------

    /// <summary>
    /// Ensures a condition-family node (every <see cref="BlockCatalog.UsesElseAttachment"/>
    /// block — "if", "if_user_has_role", "if_message_contains", "if_variable_equals",
    /// "if_time_between", "if_user_is_bot", "chance", "if_user_permission",
    /// "if_channel", "if_user") has exactly one red "Else" attachment card
    /// heading its False branch:
    /// <list type="bullet">
    /// <item><c>BranchFalseTargetId</c> EMPTY → a fresh card is created (via the
    /// same <see cref="CreateAttachmentCard"/> path the comparison Else card
    /// uses, incl. Config["ComponentKey"]="else" tracking under the
    /// "{conditionId}:else" mapping key) and the False port is wired to it —
    /// the card's own blue "next" exit then continues the else chain, while the
    /// True branch keeps its direct green wire.</item>
    /// <item><c>BranchFalseTargetId</c> already routing through a
    /// comparison_else card → kept (re-adopted into the mapping after a
    /// reload).</item>
    /// <item><c>BranchFalseTargetId</c> pointing at a REAL node (an existing
    /// flow's own else wiring) → left completely alone: no card is created and
    /// no wire is touched (migration safety for previously saved flows).</item>
    /// </list>
    /// Cleanup needs no extra logic: the card is tracked under
    /// "{conditionId}:else", so <see cref="PruneOrphanAttachmentCards"/> step 1
    /// removes it when the condition dies, and step 2 keeps it alive exactly as
    /// long as the condition's False wire references it.
    /// </summary>
    private void EnsureConditionElseCard(FlowCanvasNode condition, HashSet<string> claimed)
    {
        string? wiredFalse = condition.BranchFalseTargetId;
        FlowCanvasNode? wiredNode = FindNodeById(wiredFalse);
        if (wiredNode != null &&
            !string.Equals(wiredNode.BlockType, ComparisonElseCardType, StringComparison.Ordinal))
        {
            return; // a real node owns the False branch — never disturb existing wiring
        }

        // Empty, dangling or already-card-wired: adopt the tracked/unclaimed
        // Else card first (reload re-adoption via mapping, wire or ComponentKey),
        // then create one when none exists.
        string mapKey = AttachmentMapKey(condition.Id, "else");
        FlowCanvasNode? card = ResolveAttachmentCard(mapKey, ComparisonElseCardType, "else", wiredNode?.Id, claimed);
        if (card == null)
        {
            card = CreateAttachmentCard(condition, BlockCatalog.PortFalse, ComparisonElseCardType, "Else", "else");
            if (card == null)
            {
                return;
            }

            // "false" is a catalog-declared port, so the AddAttachmentNode path
            // already pre-wired BranchFalseTargetId and centered the card under
            // the port dot; the generic CreateNode fallback needs the wiring
            // done here. Either way, track it for the prune + panel deletes.
            if (!string.Equals(condition.BranchFalseTargetId, card.Id, StringComparison.Ordinal))
            {
                condition.BranchFalseTargetId = card.Id;
            }

            _componentSubNodes[mapKey] = card.Id;
            // Creation does not flow through ResolveAttachmentCard's claim
            // bookkeeping — claim it here so a LATER condition in this same
            // pass (empty False branch, no tracking) cannot adopt this card
            // via the ComponentKey fallback and wire both to one card.
            claimed.Add(card.Id);
        }
        else if (!string.Equals(condition.BranchFalseTargetId, card.Id, StringComparison.Ordinal))
        {
            condition.BranchFalseTargetId = card.Id; // re-pin a drifted wire onto the card
        }
    }

    // ----------------------------------------------------------------------
    // Attachment card plumbing
    // ----------------------------------------------------------------------

    /// <summary>
    /// Finds (or tracks) a parent's attachment card: the tracked mapping id
    /// first, then whatever the parent's port already routes to, then any
    /// unclaimed card of the right type carrying the expected
    /// Config["ComponentKey"] (covers freshly loaded graphs whose mapping
    /// starts empty). Returns null when no card exists yet.
    /// </summary>
    private FlowCanvasNode? ResolveAttachmentCard(string mapKey, string cardBlockType, string componentKey, string? wiredTargetId, HashSet<string> claimed)
    {
        if (_componentSubNodes.TryGetValue(mapKey, out string? tracked) && !string.IsNullOrEmpty(tracked))
        {
            FlowCanvasNode? trackedNode = FindNodeById(tracked);
            if (trackedNode != null && string.Equals(trackedNode.BlockType, cardBlockType, StringComparison.Ordinal))
            {
                claimed.Add(trackedNode.Id);
                return trackedNode;
            }

            _componentSubNodes.Remove(mapKey); // stale entry — the card is gone or retyped
        }

        if (!string.IsNullOrEmpty(wiredTargetId) &&
            FindNodeById(wiredTargetId) is { } wired &&
            string.Equals(wired.BlockType, cardBlockType, StringComparison.Ordinal) &&
            claimed.Add(wired.Id))
        {
            _componentSubNodes[mapKey] = wired.Id;
            return wired;
        }

        FlowCanvasNode? found = Nodes
            .Where(n => !claimed.Contains(n.Id) &&
                        string.Equals(n.BlockType, cardBlockType, StringComparison.Ordinal) &&
                        n.Config.TryGetValue("ComponentKey", out string? ck) &&
                        string.Equals(ck, componentKey, StringComparison.Ordinal))
            .OrderBy(n => n.Y)
            .ThenBy(n => n.X)
            .FirstOrDefault(n => claimed.Add(n.Id));
        if (found != null)
        {
            _componentSubNodes[mapKey] = found.Id;
        }

        return found;
    }

    /// <summary>
    /// Creates one attachment card for a parent's port. Preferred path is the
    /// canvas's <see cref="FlowCanvasControl.AddAttachmentNode"/> (centers the
    /// card under the parent's port dot and pre-wires the port); the generic
    /// <see cref="FlowCanvasControl.CreateNode"/> path is the fallback. Either
    /// way the card gets Config["ComponentKey"] so a reloaded graph can be
    /// re-matched. The card is a normal FlowCanvasNode — it round-trips through
    /// snapshots, exports and saves like any other node.
    /// </summary>
    private FlowCanvasNode? CreateAttachmentCard(FlowCanvasNode parent, string portKey, string blockType, string label, string componentKey)
    {
        FlowCanvasNode? card = _canvas?.AddAttachmentNode(parent.Id, portKey, blockType, label);
        if (card == null)
        {
            // Fallback (no canvas API or unknown port): generic creation with a
            // BotGhost-style offset — "after"/case cards left, "body"/else right.
            bool leftColumn = componentKey is "after" || componentKey.StartsWith("case", StringComparison.Ordinal);
            card = _canvas?.CreateNode(
                blockType,
                parent.X + (leftColumn ? AttachmentCardLeftX : AttachmentCardRightX),
                parent.Y + FlowNodeVisual.DefaultHeightFor(parent) + AttachmentCardGapY);
            if (card == null)
            {
                return null;
            }

            card.Label = label;
        }

        card.Config["ComponentKey"] = componentKey;
        return card;
    }

    private FlowCanvasNode? FindNodeById(string? nodeId) =>
        string.IsNullOrEmpty(nodeId)
            ? null
            : Nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));

    /// <summary>Parses Config["Comparisons"] into rules (tolerates missing/malformed JSON).</summary>
    internal static List<ComparisonRule> ParseComparisons(FlowCanvasNode node)
    {
        var rules = new List<ComparisonRule>();
        if (!node.Config.TryGetValue(ComparisonConfigKey, out string? json) || string.IsNullOrWhiteSpace(json))
        {
            return rules;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    rules.Add(new ComparisonRule(
                        TryGetString(element, "left") ?? "{args}",
                        TryGetString(element, "operator") ?? "eq",
                        TryGetString(element, "right") ?? string.Empty));
                }
            }
        }
        catch (JsonException)
        {
            // Malformed stored JSON — treat as no rules.
        }

        return rules;
    }

    /// <summary>Serializes rules into Config["Comparisons"] (camelCase left/operator/right).</summary>
    private static void WriteComparisons(FlowCanvasNode node, IReadOnlyList<ComparisonRule> rules)
    {
        // Always written — even an empty list marks the node as new-style so
        // the Else card survives (a missing key would read as a legacy node).
        node.Config[ComparisonConfigKey] = JsonSerializer.Serialize(
            rules.Select(rule => (object)new Dictionary<string, object?>
            {
                ["left"] = rule.Left,
                ["operator"] = rule.Operator,
                ["right"] = rule.Right
            }).ToList(), JsonOpts.Indented);
    }

    /// <summary>
    /// A comparison rule changed in the settings panel: persist the rules
    /// JSON, refresh the node's branch ports, re-sync the case/Else cards and
    /// put selection (and the panel) back onto the comparison node.
    /// </summary>
    private void OnComparisonRulesChanged(object? sender, EventArgs e)
    {
        if (SelectedNode is not { } node || ComparisonBuilder == null || !IsComparisonNode(node))
        {
            return;
        }

        List<ComparisonRule> previous = ParseComparisons(node);
        WriteComparisons(node, ComparisonBuilder.Rows
            .Select(row => new ComparisonRule(row.Left, row.SelectedOperator?.Key ?? "eq", row.Right))
            .ToList());
        MarkDirty();

        bool migrateLegacyWiring = previous.Count == 0 && HasLegacyComparisonFields(node);
        string editedNodeId = node.Id;
        var claimed = CollectTrackedAttachmentIds();

        _ensuringAttachmentCards = true; // the sync raises GraphChanged (labels) — keep the ensure out
        try
        {
            _canvas?.RefreshNode(node.Id);
            SyncComparisonCards(node, previous, claimed, migrateLegacyWiring);
        }
        finally
        {
            _ensuringAttachmentCards = false;
        }

        RestoreSelection(editedNodeId);
    }

    /// <summary>
    /// Puts selection back where it was before attachment-card work — creating
    /// cards selects them (canvas CreateNode behavior) which would otherwise
    /// yank the settings panel off the block being edited.
    /// </summary>
    private void RestoreSelectionAfterCardWork(string? previousSelectedId)
    {
        if (string.IsNullOrEmpty(previousSelectedId))
        {
            if (SelectedNode is { } stolen)
            {
                stolen.IsSelected = false;
                SelectedNode = null;
            }

            return;
        }

        RestoreSelection(previousSelectedId);
    }


    /// <summary>Deselects the current node (settings-panel ✕ button).</summary>
    [RelayCommand]
    public void DeselectNode()
    {
        if (_canvas != null)
        {
            // Route through the canvas so its visual chrome stays in sync.
            if (SelectedNode != null)
            {
                SelectedNode.IsSelected = false;
            }
            _canvas.Focus();
        }
        SelectedNode = null;
    }

    /// <summary>Duplicates the selected block (+24/+24, fresh id, no wires).</summary>
    [RelayCommand]
    public void DuplicateSelectedBlock()
    {
        FlowCanvasNode? source = SelectedNode;
        if (source == null)
        {
            return;
        }

        var copy = new FlowCanvasNode
        {
            BlockType = source.BlockType,
            Label = source.Label,
            X = source.X + 24d,
            Y = source.Y + 24d,
            Config = new Dictionary<string, string>(source.Config, StringComparer.Ordinal)
        };
        if (BlockCatalog.Find(source.BlockType) is { } block)
        {
            PrefillConfig(copy, block);
        }

        Nodes.Add(copy);
        // Mirror RestoreSelection: land the canvas highlight on the COPY, not the
        // source — otherwise the settings panel edits the copy while the source
        // stays highlighted. Clearing the others first also covers the no-canvas
        // fallback path (with a canvas attached, SyncSelectionFromNode keeps the
        // visual chrome in sync as each flag flips).
        foreach (var other in Nodes)
        {
            if (!ReferenceEquals(other, copy))
            {
                other.IsSelected = false;
            }
        }
        copy.IsSelected = true;
        SelectedNode = copy; // opens the settings panel on the fresh copy
        MarkDirty();
        EnsureAttachmentCards(); // a duplicated loop/comparison/condition needs its own cards (no canvas event fires here)
    }

    /// <summary>Deletes the selected block (canvas clears every wire referencing it).</summary>
    [RelayCommand]
    public void DeleteSelectedBlock()
    {
        FlowCanvasNode? node = SelectedNode;
        if (node == null)
        {
            return;
        }

        _canvas?.DeleteNode(node.Id);
        if (Nodes.Contains(node))
        {
            Nodes.Remove(node); // fallback when no canvas is attached
        }
        SelectedNode = null;
        MarkDirty();
    }

    /// <summary>Flags the editor content as changed (title dot, autosave, close prompt).</summary>
    private void MarkDirty()
    {
        IsDirty = true;
    }

    /// <summary>Config write hook for the settings-panel rows (dirty + switch case ports).</summary>
    private void OnConfigChanged(string? key)
    {
        MarkDirty();
        if (string.Equals(key, "Cases", StringComparison.Ordinal) && SelectedNode != null)
        {
            // Editing a switch node's Cases changes its derived output ports.
            _canvas?.RefreshNode(SelectedNode.Id);
        }
    }

    /// <summary>Rebuilds the settings panel for the selected node.</summary>
    private void RebuildSettingsPanel()
    {
        FlowCanvasNode? node = SelectedNode;
        BlockDefinition? block = SelectedBlock;

        if (node == null || block == null)
        {
            ParamRows = new List<FlowParamRowViewModel>();
            EmbedBuilder = null;
            ConditionBuilder = null;
            ComparisonBuilder = null;
            OnPropertyChanged(nameof(HasComparisonsParam));
            LoadComponentEditors(null);
            return;
        }

        var rows = new List<FlowParamRowViewModel>();
        EmbedBuilderViewModel? embed = null;
        ConditionBuilderViewModel? rule = null;
        ComparisonBuilderViewModel? comparisons = null;

        foreach (ParamField field in block.Params)
        {
            if (string.Equals(field.Kind, ParamKind.Embed, StringComparison.Ordinal))
            {
                embed = new EmbedBuilderViewModel(node, key => OnConfigChanged(key));
                continue;
            }
            if (string.Equals(field.Kind, ParamKind.Rule, StringComparison.Ordinal))
            {
                rule = new ConditionBuilderViewModel(node, key => OnConfigChanged(key));
                continue;
            }
            if (string.Equals(field.Kind, ParamKind.Comparisons, StringComparison.Ordinal))
            {
                // Custom editor for the multi-rule comparison condition — the
                // single Comparisons param never renders as a plain row.
                comparisons = new ComparisonBuilderViewModel(node);
                comparisons.Changed += OnComparisonRulesChanged;
                continue;
            }

            rows.Add(new FlowParamRowViewModel(field, node, OnConfigChanged));
        }

        ParamRows = rows;
        EmbedBuilder = embed;
        ConditionBuilder = rule;
        ComparisonBuilder = comparisons;
        OnPropertyChanged(nameof(HasEmbedParam));
        OnPropertyChanged(nameof(HasRuleParam));
        OnPropertyChanged(nameof(HasComparisonsParam));
        LoadComponentEditors(IsMessageBlock(node.BlockType) ? node : null);

        // Fill role/channel pickers lazily (empty list without a connected bot
        // → the editable ComboBox text remains the fallback).
        foreach (FlowParamRowViewModel row in rows)
        {
            if (row.IsRole || row.IsChannel)
            {
                _ = FillRemoteOptionsAsync(row);
            }
        }

        // The rule builder's "User has role" pickers share one role list.
        if (rule != null && RoleOptions.Count == 0)
        {
            _ = FillRoleOptionsAsync();
        }
    }

    // ======================================================================
    // Settings panel: Buttons & Menus editors (message nodes)
    // ======================================================================

    /// <summary>
    /// Refills the settings panel's Buttons &amp; Menus editors from the node's
    /// Config["Buttons"]/Config["Menu"] JSON (null clears the section).
    /// </summary>
    private void LoadComponentEditors(FlowCanvasNode? node)
    {
        _loadingComponents = true;
        try
        {
            ComponentButtons = new ObservableCollection<ComponentButtonRowViewModel>();
            ComponentMenu = null;

            if (node == null)
            {
                return;
            }

            foreach (var payload in ParseButtonsConfig(node))
            {
                var row = new ComponentButtonRowViewModel
                {
                    Label = payload.TryGetValue("label", out var label) && label is string l ? l : "Button",
                    Style = payload.TryGetValue("style", out var style) && style is string s && !string.IsNullOrWhiteSpace(s) ? s : "Secondary",
                    Emoji = payload.TryGetValue("emoji", out var emoji) && emoji is string e ? e : string.Empty
                };
                row.Changed += (_, _) => SerializeComponentConfig();
                ComponentButtons.Add(row);
            }

            if (node.Config.TryGetValue("Menu", out string? menuJson) && !string.IsNullOrWhiteSpace(menuJson))
            {
                try
                {
                    using var document = JsonDocument.Parse(menuJson);
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var menu = new ComponentMenuViewModel
                        {
                            Placeholder = document.RootElement.TryGetProperty("placeholder", out var ph) && ph.ValueKind == JsonValueKind.String
                                ? ph.GetString() ?? string.Empty
                                : string.Empty
                        };
                        if (document.RootElement.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var option in options.EnumerateArray())
                            {
                                if (option.ValueKind != JsonValueKind.Object)
                                {
                                    continue;
                                }

                                menu.Track(new ComponentMenuOptionRowViewModel
                                {
                                    Label = TryGetString(option, "label") ?? string.Empty,
                                    Value = TryGetString(option, "value") ?? string.Empty
                                });
                            }
                        }

                        menu.Options.CollectionChanged += (_, _) => SerializeComponentConfig();
                        ComponentMenu = menu;
                        HookComponentMenu();
                    }
                }
                catch (JsonException)
                {
                    // Malformed stored JSON — show no menu editor.
                }
            }
        }
        finally
        {
            _loadingComponents = false;
        }
    }

    /// <summary>
    /// Serializes the Buttons &amp; Menus editors back into the selected node's
    /// Config (per the component JSON schema), refreshes the canvas node so its
    /// chips rebuild, and keeps the tracked sub-nodes aligned.
    /// </summary>
    private void SerializeComponentConfig()
    {
        if (_loadingComponents || SelectedNode is not { } node || !HasComponentSection)
        {
            return;
        }

        var buttons = ComponentButtons.Select(b => new Dictionary<string, object?>
        {
            ["label"] = b.Label?.Trim() ?? string.Empty,
            ["style"] = string.IsNullOrWhiteSpace(b.Style) ? "Secondary" : b.Style.Trim(),
            ["emoji"] = string.IsNullOrWhiteSpace(b.Emoji) ? null : b.Emoji.Trim()
        }).ToList();
        WriteButtonsConfig(node, buttons);

        if (ComponentMenu != null &&
            (!string.IsNullOrWhiteSpace(ComponentMenu.Placeholder) || ComponentMenu.Options.Count > 0))
        {
            node.Config["Menu"] = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["placeholder"] = ComponentMenu.Placeholder?.Trim() ?? string.Empty,
                ["options"] = ComponentMenu.Options.Take(25).Select(o => new Dictionary<string, object?>
                {
                    ["label"] = o.Label?.Trim() ?? string.Empty,
                    ["value"] = o.Value?.Trim() ?? string.Empty
                }).ToList()
            }, JsonOpts.Indented);
        }
        else
        {
            node.Config.Remove("Menu");
        }

        OnConfigChanged("Buttons");
        string? editedNodeId = node.Id;
        _canvas?.RefreshNode(node.Id);
        EnsureComponentSubNodes(); // keep canvas sub-nodes aligned with the edited config
        RestoreSelection(editedNodeId);
    }

    /// <summary>
    /// Puts selection back onto a node after a canvas operation (component
    /// sub-node creation) may have selected the freshly created node instead —
    /// keeps the user editing the same block in the settings panel.
    /// </summary>
    private void RestoreSelection(string nodeId)
    {
        if (SelectedNode is { } current && string.Equals(current.Id, nodeId, StringComparison.Ordinal))
        {
            return; // selection never moved
        }

        FlowCanvasNode? edited = Nodes.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal));
        if (edited == null)
        {
            return;
        }

        edited.IsSelected = true; // canvas syncs and re-raises NodeSelected(edited)
        SelectedNode = edited;
    }

    /// <summary>Settings panel "+ ADD BUTTON" (Discord caps messages at 5 buttons).</summary>
    [RelayCommand]
    private void AddComponentButton()
    {
        if (!HasComponentSection)
        {
            return;
        }

        if (ComponentButtons.Count >= 5)
        {
            StatusText = "Discord allows at most 5 buttons per message.";
            return;
        }

        var row = new ComponentButtonRowViewModel
        {
            Label = $"Button {ComponentButtons.Count + 1}",
            Style = "Secondary"
        };
        row.Changed += (_, _) => SerializeComponentConfig();
        ComponentButtons.Add(row);
        SerializeComponentConfig();
        MarkDirty();
    }

    /// <summary>Settings panel button ✕ — drops the row (and its tracked sub-node on next reconcile).</summary>
    [RelayCommand]
    private void RemoveComponentButton(ComponentButtonRowViewModel? row)
    {
        if (row == null || !HasComponentSection)
        {
            return;
        }

        int index = ComponentButtons.IndexOf(row);
        ComponentButtons.Remove(row);
        SerializeComponentConfig();
        if (index >= 0)
        {
            ReindexButtonSubNodes(SelectedNode!, index);
        }

        MarkDirty();
    }

    /// <summary>Settings panel select-menu "+ ADD OPTION" (Discord caps menus at 25 options).</summary>
    [RelayCommand]
    private void AddComponentMenuOption()
    {
        if (!HasComponentSection)
        {
            return;
        }

        // A menu exists implicitly as soon as the user asks for an option row.
        if (ComponentMenu == null)
        {
            ComponentMenu = new ComponentMenuViewModel();
            HookComponentMenu();
        }

        if (ComponentMenu.Options.Count >= 25)
        {
            StatusText = "Discord select menus allow at most 25 options.";
            return;
        }

        if (ComponentMenu.Options.Count == 0 && string.IsNullOrWhiteSpace(ComponentMenu.Placeholder))
        {
            ComponentMenu.Placeholder = "Choose an option...";
        }

        var row = new ComponentMenuOptionRowViewModel
        {
            Label = $"Option {ComponentMenu.Options.Count + 1}",
            Value = $"option{ComponentMenu.Options.Count + 1}"
        };
        ComponentMenu.Track(row);
        HookComponentMenu();
        SerializeComponentConfig();
        MarkDirty();
    }

    /// <summary>Settings panel select-menu option ✕.</summary>
    [RelayCommand]
    private void RemoveComponentMenuOption(ComponentMenuOptionRowViewModel? row)
    {
        if (row == null || ComponentMenu == null || !HasComponentSection)
        {
            return;
        }

        ComponentMenu.Options.Remove(row);
        SerializeComponentConfig();
        MarkDirty();
    }

    /// <summary>Ensures the current menu editor re-serializes on edits.</summary>
    private void HookComponentMenu()
    {
        if (ComponentMenu == null)
        {
            return;
        }

        ComponentMenu.Changed -= OnComponentMenuChanged;
        ComponentMenu.Changed += OnComponentMenuChanged;
    }

    private void OnComponentMenuChanged(object? sender, EventArgs e) => SerializeComponentConfig();

    private async Task FillRoleOptionsAsync()
    {
        try
        {
            IReadOnlyList<string> names = await _discordBridge.GetRoleNamesAsync(null).ConfigureAwait(true);
            foreach (string name in names)
            {
                if (!RoleOptions.Contains(name))
                {
                    RoleOptions.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not populate the rule-builder role picker");
        }
    }

    private async Task FillRemoteOptionsAsync(FlowParamRowViewModel row)
    {
        try
        {
            IReadOnlyList<string> names = row.IsRole
                ? await _discordBridge.GetRoleNamesAsync(null).ConfigureAwait(true)
                : await _discordBridge.GetChannelNamesAsync(null).ConfigureAwait(true);

            row.RemoteOptions.Clear();
            foreach (string name in names)
            {
                row.RemoteOptions.Add(name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not populate the {Kind} picker", row.IsRole ? "role" : "channel");
        }
    }

    // ======================================================================
    // Zoom / toolbar
    // ======================================================================

    [RelayCommand] private void ZoomIn() => _canvas?.ZoomIn();
    [RelayCommand] private void ZoomOut() => _canvas?.ZoomOut();
    [RelayCommand] private void ResetZoom() => _canvas?.ResetZoom();
    [RelayCommand] private void FitToScreen() => _canvas?.FitToContent();

    [RelayCommand]
    private void AddOption()
    {
        OptionRows.Add(new FlowOptionRowViewModel(OptionTypes[0], string.Empty));
    }

    [RelayCommand]
    private void RemoveOption(FlowOptionRowViewModel? row)
    {
        if (row != null)
        {
            OptionRows.Remove(row);
        }
    }

    /// <summary>
    /// Single click (or Enter) on a purple Options-tab entry: appends a
    /// WorkflowOption of that type seeded with the definition's value hint into
    /// the flow's <see cref="OptionRows"/> — the very collection the Options
    /// popup and Command Settings edit — then opens the right panel in Command
    /// mode so the user sees (and fills) the list they just added to.
    /// Flag-only options (DM only, guild only, NSFW) are singletons: a second
    /// click just reopens Command Settings on the existing row.
    /// </summary>
    [RelayCommand]
    public void AddOptionFromDefinition(FlowOptionDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        FlowOptionTypeOption? type = OptionTypes.FirstOrDefault(t =>
            string.Equals(t.Key, definition.Type, StringComparison.OrdinalIgnoreCase));
        if (type == null)
        {
            _logger.LogWarning("Options-tab entry {Label} has unknown type {Type} — ignored", definition.Label, definition.Type);
            return;
        }

        bool flagOnly = type.Key is WorkflowOptionTypes.DmOnly or WorkflowOptionTypes.GuildOnly or WorkflowOptionTypes.Nsfw;
        if (!flagOnly || !OptionRows.Any(o => o.Type.Key == type.Key))
        {
            OptionRows.Add(new FlowOptionRowViewModel(
                type,
                string.IsNullOrWhiteSpace(definition.ValueHint) ? string.Empty : definition.ValueHint));
        }

        EnterCommandMode(); // the user sees (and fills) the list they just added to
        StatusText = $"{definition.Label} option added — set its value in Command Settings.";
    }

    // ======================================================================
    // Typed command parameters (COMMAND PARAMETERS group of the Options tab)
    // ======================================================================

    /// <summary>
    /// Normalizes a user-typed parameter name into the lowercase identifier
    /// Discord expects ("My Value" → "my_value").
    /// </summary>
    public static string NormalizeParameterName(string? raw)
    {
        var builder = new StringBuilder();
        foreach (char character in (raw ?? string.Empty).Trim())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is ' ' or '-' or '.')
            {
                builder.Append('_');
            }
        }

        if (builder.Length == 0)
        {
            builder.Append("param");
        }

        if (char.IsAsciiDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    /// <summary>Auto parameter name: "{type}1", "{type}2", … first unused.</summary>
    private string NextParameterName(string typeKey)
    {
        string baseName = NormalizeParameterName(typeKey);
        for (int n = 1; n < 1000; n++)
        {
            string candidate = $"{baseName}{n}";
            if (ParameterRows.All(r => !string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return $"{baseName}{Guid.NewGuid():N}".Substring(0, baseName.Length + 5);
    }

    /// <summary>True when the name is a previously auto-generated default for this definition ("text1", "search", …).</summary>
    private static bool IsDefaultParameterName(string? name, FlowParameterDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (definition.FixedName != null &&
            string.Equals(name, definition.FixedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string baseName = NormalizeParameterName(definition.TypeKey);
        return name!.StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
               name.Length > baseName.Length &&
               name[baseName.Length..].All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Click on a COMMAND PARAMETERS definition: appends a typed
    /// <see cref="WorkflowParameter"/> (unless a default-named one of that type
    /// already exists) and opens Command Settings so the user sees the list
    /// they just added to.
    /// </summary>
    [RelayCommand]
    public void AddParameterFromDefinition(FlowParameterDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        if (!ParameterRows.Any(r => IsDefaultParameterName(r.Name, definition) &&
                                    string.Equals(r.Type, definition.TypeKey, StringComparison.OrdinalIgnoreCase)))
        {
            string name = definition.FixedName ?? NextParameterName(definition.TypeKey);
            string description = definition.FixedDescription ?? $"The {definition.Label.ToLowerInvariant()} value";
            var row = new WorkflowParameterRowViewModel(definition.TypeKey, name, description, isRequired: true);
            row.Changed += OnParameterRowChanged;
            ParameterRows.Add(row);
            MarkDirty();
            RefreshParameterCards();
        }

        EnterCommandMode();
        StatusText = $"{definition.Label} parameter ready — review it in Command Settings.";
    }

    /// <summary>
    /// Parameter ✕ (Command Settings list or Parameter mode's Remove button).
    /// Removes the row, repaints the canvas cards and closes Parameter mode
    /// when the edited parameter itself is the one being removed.
    /// </summary>
    [RelayCommand]
    public void RemoveParameter(WorkflowParameterRowViewModel? row)
    {
        if (row == null)
        {
            return;
        }

        ParameterRows.Remove(row);
        if (SettingsMode == FlowSettingsMode.Parameter && ReferenceEquals(row, SelectedParameter))
        {
            SelectedParameter = null;
            SettingsMode = FlowSettingsMode.None; // the edited parameter is gone — close the panel
        }

        RefreshParameterCards();
        MarkDirty();
    }

    // ======================================================================
    // Undo / redo
    // ======================================================================

    public bool CanUndo => _undoStack.Count > 1;
    public bool CanRedo => _redoStack.Count > 0;

    private void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void PushUndoSnapshot()
    {
        if (_suppressSnapshot)
        {
            return;
        }

        string json = SerializeSnapshot();
        if (_undoStack.Count > 0 && string.Equals(_undoStack[^1], json, StringComparison.Ordinal))
        {
            return; // no observable change since the last snapshot
        }

        _undoStack.Add(json);
        if (_undoStack.Count > MaxUndoSnapshots)
        {
            _undoStack.RemoveRange(0, _undoStack.Count - MaxUndoSnapshots);
        }

        _redoStack.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count <= 1)
        {
            return;
        }

        string current = SerializeSnapshot();
        if (!string.Equals(_undoStack[^1], current, StringComparison.Ordinal))
        {
            _redoStack.Push(current);
        }

        _undoStack.RemoveAt(_undoStack.Count - 1);
        RestoreSnapshot(_undoStack[^1]);
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        string next = _redoStack.Pop();
        _undoStack.Add(next);
        RestoreSnapshot(next);
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Collects the full editor state into a snapshot DTO (undo, drafts, export, marketplace sharing).</summary>
    public FlowEditorSnapshotDto BuildSnapshotDto()
    {
        return new FlowEditorSnapshotDto
        {
            Id = _workflowId,
            Name = FlowName,
            Description = TriggerDescription,
            Enabled = FlowEnabled,
            Trigger = BuildTriggerDto(),
            Options = OptionRows.Select(o => new FlowOptionDto { Type = o.Type.Key, Value = o.Value }).ToList(),
            Parameters = ParameterRows.Select(p => new FlowParameterDto
            {
                Name = p.Name,
                Type = p.Type,
                Description = p.Description,
                Required = p.Required
            }).ToList(),
            EntryNodeId = _entryNodeId,
            ErrorHandlerEntryId = _errorHandlerEntryId,
            Nodes = Nodes.Select(ToNodeDto).ToList()
        };
    }

    private string SerializeSnapshot()
    {
        return JsonSerializer.Serialize(BuildSnapshotDto(), JsonOpts.Indented);
    }

    private static FlowNodeDto ToNodeDto(FlowCanvasNode node) => new()
    {
        Id = node.Id,
        BlockType = node.BlockType,
        Label = node.Label,
        X = node.X,
        Y = node.Y,
        Config = new Dictionary<string, string>(node.Config, StringComparer.Ordinal),
        Next = node.NextTargetId,
        BranchTrue = node.BranchTrueTargetId,
        BranchFalse = node.BranchFalseTargetId,
        BranchError = node.BranchErrorTargetId,
        Branches = node.BranchTargets == null || node.BranchTargets.Count == 0
            ? null
            : new Dictionary<string, string>(node.BranchTargets, StringComparer.Ordinal)
    };

    private void RestoreSnapshot(string json)
    {
        FlowEditorSnapshotDto? dto = null;
        try
        {
            dto = JsonSerializer.Deserialize<FlowEditorSnapshotDto>(json, JsonOpts.Indented);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt undo snapshot skipped");
        }

        if (dto == null)
        {
            return;
        }

        _suppressSnapshot = true;
        try
        {
            FlowName = dto.Name ?? string.Empty;
            FlowEnabled = dto.Enabled;
            TriggerDescription = dto.Description ?? string.Empty;
            LoadTriggerIntoForm(dto.Trigger ?? new FlowTriggerDto { Type = WorkflowTriggerTypes.Manual });
            LoadOptionsIntoForm(dto.Options?.Select(o => new WorkflowOption { Type = o.Type, Value = o.Value }));
            LoadParametersIntoForm(dto.Parameters);
            ResetParameterEditor(); // the undo may have removed the parameter being edited
            RefreshParameterCards();
            _entryNodeId = dto.EntryNodeId;
            _errorHandlerEntryId = dto.ErrorHandlerEntryId;
            LoadNodesIntoCanvas(dto.Nodes ?? new List<FlowNodeDto>());
            SelectedNode = null;
            IsDirty = true;
            OnPropertyChanged(nameof(TriggerSummaryText));
            UpdateTriggerCard();
        }
        finally
        {
            _suppressSnapshot = false;
        }
    }

    // ======================================================================
    // Drafts (autosave every 30s + on close)
    // ======================================================================

    private static string DraftDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vyral", "FlowDrafts");

    private static string DraftPathFor(string id) => Path.Combine(DraftDirectory, $"{id}.json");

    private async Task SaveDraftAsync()
    {
        try
        {
            Directory.CreateDirectory(DraftDirectory);
            string json = SerializeSnapshot();
            string draftKey = _existingWorkflow != null || _savedOnce || _loadedFromImport
                ? _workflowId
                : "unsaved";
            string path = DraftPathFor(draftKey);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flow editor autosave failed for workflow {WorkflowId}", _workflowId);
        }
    }

    /// <summary>True once the flow has been persisted at least once (or was loaded from the DB).</summary>
    private bool IsPersisted => _existingWorkflow != null || _savedOnce;

    private bool _savedOnce;

    /// <summary>
    /// True while editing an imported shared code (file or marketplace). The
    /// import is a deliberate load with a durable source — its autosave must
    /// NOT take the "unsaved new flow" draft slot, or every later NEW COMMAND
    /// resurrects the imported flow instead of starting fresh.
    /// </summary>
    private bool _loadedFromImport;

    private FlowEditorSnapshotDto? TryReadSnapshot(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<FlowEditorSnapshotDto>(File.ReadAllText(path), JsonOpts.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read flow editor draft {Path}", path);
            return null;
        }
    }

    private void DeleteDrafts()
    {
        foreach (string path in new[] { DraftPathFor(_workflowId), DraftPathFor("unsaved") })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete flow editor draft {Path}", path);
            }
        }
    }

    // ======================================================================
    // Save / test run / export / import / close
    // ======================================================================

    /// <summary>First validation problem, or null when the flow can be saved.</summary>
    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(FlowName))
        {
            return "Give the flow a name first (top toolbar).";
        }
        if (Nodes.Count == 0)
        {
            return "Place at least one block on the canvas before saving.";
        }
        if (IsTriggerNeedsCommandName && string.IsNullOrWhiteSpace(TriggerCommandName))
        {
            return IsTriggerSlash
                ? "Slash command triggers need a command name (e.g. hello for /hello)."
                : "Command triggers need a command name (e.g. hello for !hello).";
        }
        if (IsTriggerNeedsCommandName && !string.IsNullOrWhiteSpace(TriggerCommandName)
            && TriggerCommandName.Trim().Contains(' '))
        {
            // The runtime matches the FIRST token after the prefix, so a name
            // with spaces can never fire; "!mod" verbs are single tokens too.
            return "Command names cannot contain spaces — use a single word (hello, not hello world).";
        }
        if (IsTriggerTimer && (!int.TryParse(TriggerIntervalMinutes, out int minutes) || minutes < 1))
        {
            return "Timer triggers need an interval of at least 1 minute.";
        }
        return null;
    }

    /// <summary>
    /// Async half of save validation: a command/slash/mod-command trigger name
    /// must not already be claimed. Two flows with the same chat-command name
    /// would BOTH fire on every invocation, a duplicate slash name silently
    /// loses one flow at registration, and a duplicate "!mod" verb is a silent
    /// last-one-wins in the engine's verb index. A "!mod" verb that collides
    /// with a built-in engine verb (kick, ban, …) can never run at all — the
    /// engine consumes built-in verbs before offering the verb to workflows.
    /// Returns the user-facing problem, or null when the name is free.
    /// </summary>
    private async Task<string?> ValidateCommandNameAvailabilityAsync()
    {
        if (!IsTriggerNeedsCommandName || string.IsNullOrWhiteSpace(TriggerCommandName))
        {
            return null;
        }

        string name = TriggerCommandName.Trim();

        if (IsTriggerModCommand && ModTiers.Admin.Contains(name.ToLowerInvariant(), StringComparer.Ordinal))
        {
            return $"'!mod {name.ToLowerInvariant()}' is a built-in mod action — the engine always answers it first. Pick a different verb.";
        }

        try
        {
            string normalized = NormalizeCommandName(name);
            foreach (WorkflowEntity row in await _dataService.GetWorkflowsAsync())
            {
                if (string.Equals(row.Id, _workflowId, StringComparison.Ordinal))
                {
                    continue; // re-saving THIS flow keeps its own name
                }

                Workflow loaded;
                try
                {
                    loaded = WorkflowJson.FromRow(row);
                }
                catch (Exception)
                {
                    continue; // unreadable row — never blocks the save
                }

                var trigger = loaded.Trigger;
                if (trigger == null || string.IsNullOrWhiteSpace(trigger.CommandName))
                {
                    continue;
                }

                bool claimsNames = string.Equals(trigger.Type, WorkflowTriggerTypes.Command, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trigger.Type, WorkflowTriggerTypes.SlashCommand, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trigger.Type, WorkflowTriggerTypes.ModCommand, StringComparison.OrdinalIgnoreCase);
                if (!claimsNames)
                {
                    continue;
                }

                if (string.Equals(NormalizeCommandName(trigger.CommandName), normalized, StringComparison.Ordinal))
                {
                    string kind = IsTriggerModCommand ? "!mod verb" : IsTriggerSlash ? "slash command" : "command";
                    return $"A {kind} named '{name}' already exists (flow \"{loaded.Name}\"). Command names must be unique — pick another.";
                }
            }
        }
        catch (Exception ex)
        {
            // The availability check is advisory — a storage failure must not
            // block saving entirely.
            _logger.LogWarning(ex, "Command-name availability check failed for workflow {WorkflowId}", _workflowId);
        }

        return null;
    }

    /// <summary>Builds the trigger DTO from the toolbar form.</summary>
    private FlowTriggerDto BuildTriggerDto()
    {
        var trigger = new FlowTriggerDto
        {
            Type = SelectedTriggerType?.Key ?? WorkflowTriggerTypes.Manual,
            CommandName = string.IsNullOrWhiteSpace(TriggerCommandName) ? null : TriggerCommandName.Trim(),
            CommandPrefix = IsTriggerNeedsCommandName && !string.IsNullOrWhiteSpace(TriggerCommandPrefix)
                ? TriggerCommandPrefix.Trim()
                : null,
            EventType = IsTriggerEvent && !string.IsNullOrWhiteSpace(TriggerEventType) ? TriggerEventType.Trim() : null,
            Platform = IsTriggerEvent ? (string.IsNullOrWhiteSpace(TriggerPlatform) ? "Any" : TriggerPlatform.Trim()) : null
        };

        if (IsTriggerTimer && int.TryParse(TriggerIntervalMinutes, out int minutes) && minutes > 0)
        {
            trigger.IntervalMinutes = minutes;
        }

        if (IsTriggerModCommand)
        {
            // Unknown/empty tier spellings fall back to "Mod" on save.
            trigger.MinimumTier = ModTiers.NormalizeTier(TriggerMinimumTier) ?? "Mod";
        }

        return trigger;
    }

    /// <summary>
    /// Maps the canvas graph onto a <see cref="Workflow"/>: routing targets are
    /// swept for orphans and the entry node (one nobody routes to) is moved to
    /// the front of the list, mirroring the runtime's entry discovery.
    /// </summary>
    private Workflow AssembleWorkflow()
    {
        var ids = new HashSet<string>(Nodes.Select(n => n.Id), StringComparer.Ordinal);
        string? Clean(string? id) => id != null && ids.Contains(id) ? id : null;

        var workflowNodes = Nodes.Select(node => new WorkflowNode
        {
            Id = node.Id,
            Type = node.BlockType,
            Config = new Dictionary<string, string>(node.Config, StringComparer.Ordinal),
            Next = Clean(node.NextTargetId),
            BranchTrue = Clean(node.BranchTrueTargetId),
            BranchFalse = Clean(node.BranchFalseTargetId),
            BranchError = Clean(node.BranchErrorTargetId),
            Branches = node.BranchTargets == null || node.BranchTargets.Count == 0
                ? null
                : node.BranchTargets
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && ids.Contains(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            X = node.X,
            Y = node.Y
        }).ToList();

        // Entry node = the first node no other node routes to.
        var routedTo = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkflowNode node in workflowNodes)
        {
            if (node.Next != null) routedTo.Add(node.Next);
            if (node.BranchTrue != null) routedTo.Add(node.BranchTrue);
            if (node.BranchFalse != null) routedTo.Add(node.BranchFalse);
            if (node.BranchError != null) routedTo.Add(node.BranchError);
            if (node.Branches != null)
            {
                foreach (string target in node.Branches.Values)
                {
                    routedTo.Add(target);
                }
            }
        }

        int entryIndex = workflowNodes.FindIndex(n => !routedTo.Contains(n.Id));
        if (entryIndex > 0)
        {
            WorkflowNode entry = workflowNodes[entryIndex];
            workflowNodes.RemoveAt(entryIndex);
            workflowNodes.Insert(0, entry);
        }

        var now = DateTime.UtcNow;
        var trigger = BuildTriggerDto();
        var workflow = new Workflow
        {
            Id = _workflowId,
            Name = FlowName.Trim(),
            Description = string.IsNullOrWhiteSpace(TriggerDescription) ? null : TriggerDescription.Trim(),
            Enabled = FlowEnabled,
            Category = _existingCategory,
            SourceCommandId = _existingWorkflow?.SourceCommandId ?? _sourceCommandId,
            EntryNodeId = Clean(_entryNodeId),
            ErrorHandlerEntryId = Clean(_errorHandlerEntryId),
            Trigger = new WorkflowTrigger
            {
                Type = trigger.Type,
                CommandName = trigger.CommandName,
                CommandPrefix = trigger.CommandPrefix,
                IntervalMinutes = trigger.IntervalMinutes,
                EventType = trigger.EventType,
                Platform = trigger.Platform,
                MinimumTier = trigger.MinimumTier ?? "Mod"
            },
            Nodes = workflowNodes,
            Parameters = ParameterRows
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new WorkflowParameter
                {
                    Name = NormalizeParameterName(p.Name),
                    Type = p.Type,
                    Description = (p.Description ?? string.Empty).Trim(),
                    Required = p.Required
                })
                .ToList(),
            Options = OptionRows
                .Where(o => !string.IsNullOrWhiteSpace(o.Value) || o.IsFlagOnly)
                .Select(o => new WorkflowOption { Type = o.Type.Key, Value = o.IsFlagOnly ? "true" : o.Value.Trim() })
                .ToList(),
            CreatedAt = _existingCreatedAt,
            UpdatedAt = now
        };

        return workflow;
    }

    /// <summary>Persists the flow (same path as the structured builder) and reloads the trigger service.</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        string? problem = Validate();
        if (problem != null)
        {
            StatusText = problem;
            System.Windows.MessageBox.Show(problem, "Cannot save flow", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        problem = await ValidateCommandNameAvailabilityAsync();
        if (problem != null)
        {
            StatusText = problem;
            System.Windows.MessageBox.Show(problem, "Cannot save flow", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            var workflow = AssembleWorkflow();
            await _dataService.SaveWorkflowAsync(WorkflowJson.ToRow(workflow));

            // A command-triggered flow IS a custom command: make sure it has a
            // row on the Commands page (first save) / stays linked (re-saves).
            await EnsureCommandRowAsync(workflow);

            // Component child workflows (button / select sub-flows) sync after
            // the parent row exists so the parent id in their component keys is
            // stable. Best-effort — a child-sync failure never fails the save.
            await SyncComponentChildrenAsync(workflow);

            await _triggerService.ReloadAsync();

            _savedOnce = true;
            IsDirty = false;
            DeleteDrafts();
            ClearHistory();
            PushUndoSnapshot();

            // Side panels: the saved flow may now appear in Commands / Events /
            // Timed Events, and its graph may have gained set_variable blocks.
            RefreshFlowVariables();
            _ = LoadSidePanelWorkflowsAsync();
            _ = LoadErrorLogsAsync();

            StatusText = $"\"{workflow.Name}\" saved and live — triggers reloaded.";
            _logger.LogInformation("Flow editor saved workflow '{Name}' ({WorkflowId}, {NodeCount} nodes)",
                workflow.Name, workflow.Id, workflow.Nodes.Count);

            // Command → flow adoption: retire the legacy command row (and
            // shadow same-named Discord built-ins) so the flow is the single
            // owner of the command. Best-effort — never fails the save.
            // (The command row itself was ensured above, before the reload.)
            await AdoptCommandOwnershipAsync(workflow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flow editor save failed for workflow {WorkflowId}", _workflowId);
            StatusText = $"Save failed: {ex.Message}";
            System.Windows.MessageBox.Show($"Save failed: {ex.Message}", "Vyral", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ======================================================================
    // Component child workflows (button / select-menu sub-flows)
    // ======================================================================

    /// <summary>Prefix of every component custom id / child ComponentKey of this parent flow.</summary>
    private string ComponentKeyPrefix => $"vyralwf:{_workflowId}:";

    /// <summary>The component custom id of one button ("vyralwf:{parentWorkflowId}:{nodeId}:btn{i}").</summary>
    private string ButtonComponentKey(string parentNodeId, int index) =>
        $"{ComponentKeyPrefix}{parentNodeId}:btn{index}";

    /// <summary>The component custom id of a message's select menu ("...:{nodeId}:menu").</summary>
    private string MenuComponentKey(string parentNodeId) =>
        $"{ComponentKeyPrefix}{parentNodeId}:menu";

    /// <summary>
    /// Deterministic child workflow id from a component custom id: MD5 of the
    /// id string formatted as a GUID "N", so re-saving always updates the same
    /// row instead of duplicating children.
    /// </summary>
    private static string DeterministicWorkflowId(string componentKey)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(componentKey));
        return new Guid(hash).ToString("N");
    }

    /// <summary>
    /// Persists one child workflow per message-node component that has a wired
    /// sub-chain, and deletes the rows whose key no longer exists in this save
    /// (chain removed). Runs after the parent row was saved; every failure is
    /// swallowed into the status text — never fails the save.
    /// </summary>
    private async Task SyncComponentChildrenAsync(Workflow parent)
    {
        try
        {
            // id → CreatedAt of the existing rows, so deterministic re-saves
            // keep the original creation stamp.
            var existingRows = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            var existingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkflowEntity row in await _dataService.GetWorkflowsAsync())
            {
                existingRows[row.Id] = row.CreatedAt;
                Workflow? loaded = null;
                try
                {
                    loaded = WorkflowJson.FromRow(row);
                }
                catch (Exception)
                {
                    // Unreadable row — it simply never matches the orphan sweep.
                }

                if (!string.IsNullOrWhiteSpace(loaded?.ComponentKey))
                {
                    existingKeys.Add(loaded.ComponentKey);
                }
            }

            var presentKeys = new HashSet<string>(StringComparer.Ordinal);
            int savedChildren = 0;

            foreach (FlowCanvasNode node in Nodes)
            {
                if (!IsMessageBlock(node.BlockType))
                {
                    continue;
                }

                // Buttons: one child per button whose sub-node has a chain.
                var buttons = ParseButtonsConfig(node);
                for (int i = 0; i < Math.Min(buttons.Count, 5); i++)
                {
                    string label = buttons[i].TryGetValue("label", out var l) && l is string ls && !string.IsNullOrWhiteSpace(ls)
                        ? ls.Trim()
                        : $"Button {i + 1}";
                    string key = ButtonComponentKey(node.Id, i);
                    List<WorkflowNode> chain = CollectComponentChain(node, "button", i);
                    if (chain.Count == 0)
                    {
                        continue; // empty chain → orphan sweep deletes any stale row
                    }

                    presentKeys.Add(key);
                    await SaveComponentChildAsync(
                        parent, key, chain, WorkflowTriggerTypes.ButtonClick,
                        $"{parent.Name} ▸ {label}", existingRows);
                    savedChildren++;
                }

                // Select menu: one child for the whole menu.
                if (node.Config.TryGetValue("Menu", out string? menuJson) && !string.IsNullOrWhiteSpace(menuJson))
                {
                    string key = MenuComponentKey(node.Id);
                    List<WorkflowNode> chain = CollectComponentChain(node, "menu", 0);
                    if (chain.Count > 0)
                    {
                        presentKeys.Add(key);
                        await SaveComponentChildAsync(
                            parent, key, chain, WorkflowTriggerTypes.SelectMenu,
                            $"{parent.Name} ▸ Menu", existingRows);
                        savedChildren++;
                    }
                }
            }

            // Orphan sweep: children of this parent whose key vanished.
            int deleted = 0;
            foreach (string key in existingKeys.Where(k => k.StartsWith(ComponentKeyPrefix, StringComparison.Ordinal)))
            {
                if (presentKeys.Contains(key))
                {
                    continue;
                }

                string childId = DeterministicWorkflowId(key);
                if (existingRows.ContainsKey(childId))
                {
                    await _dataService.DeleteWorkflowAsync(childId);
                    deleted++;
                }
            }

            if (savedChildren > 0 || deleted > 0)
            {
                StatusText += $" ({savedChildren} component sub-flow{(savedChildren == 1 ? "" : "s")} saved" +
                              (deleted > 0 ? $", {deleted} removed)." : ")");
                _logger.LogInformation(
                    "Flow {WorkflowId} synced {Saved} component child workflows ({Deleted} removed)",
                    _workflowId, savedChildren, deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Component child-workflow sync failed for workflow {WorkflowId} — the main flow is saved",
                _workflowId);
            StatusText += " (Note: component sub-flows could not be fully synced — SAVE again later.)";
        }
    }

    /// <summary>Builds/updates one component child workflow row from its custom key and copied chain.</summary>
    private async Task SaveComponentChildAsync(
        Workflow parent,
        string componentKey,
        List<WorkflowNode> chain,
        string triggerType,
        string name,
        Dictionary<string, DateTime> existingRows)
    {
        string childId = DeterministicWorkflowId(componentKey);
        var child = new Workflow
        {
            Id = childId,
            Name = name,
            Description = $"Component sub-flow of \"{parent.Name}\" ({componentKey}).",
            Enabled = parent.Enabled,
            Category = "Component",
            ComponentKey = componentKey,
            Trigger = new WorkflowTrigger
            {
                Type = triggerType,
                Platform = "Discord"
            },
            Nodes = chain,
            Options = new List<WorkflowOption>(),
            CreatedAt = existingRows.TryGetValue(childId, out DateTime createdAt) ? createdAt : DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataService.SaveWorkflowAsync(WorkflowJson.ToRow(child));
    }

    /// <summary>
    /// Walks a component's sub-chain: the button/menu sub-node first (entry),
    /// then following Next through canvas nodes. An empty result (sub-node has
    /// no Next, or no sub-node at all) means "no child needed". Routing fields
    /// are cleaned against the chain itself so no cross-links leak into the
    /// child workflow.
    /// </summary>
    private List<WorkflowNode> CollectComponentChain(FlowCanvasNode parent, string kind, int index)
    {
        var byId = new Dictionary<string, FlowCanvasNode>(StringComparer.Ordinal);
        foreach (FlowCanvasNode n in Nodes)
        {
            byId[n.Id] = n;
        }

        // Prefer the tracked sub-node; fall back to a config-key scan.
        FlowCanvasNode? cursor = null;
        if (_componentSubNodes.TryGetValue(ComponentMapKey(parent.Id, kind, index), out string? subId) &&
            !string.IsNullOrEmpty(subId) &&
            byId.TryGetValue(subId, out FlowCanvasNode? tracked))
        {
            cursor = tracked;
        }
        else
        {
            string blockType = SubNodeBlockType(kind);
            string expectedKey = SubNodeComponentKey(kind, index);
            cursor = Nodes.FirstOrDefault(n =>
                string.Equals(n.BlockType, blockType, StringComparison.Ordinal) &&
                n.Config.TryGetValue("ComponentKey", out string? ck) &&
                string.Equals(ck, expectedKey, StringComparison.Ordinal));
        }

        if (cursor == null || string.IsNullOrEmpty(cursor.NextTargetId))
        {
            return new List<WorkflowNode>(); // empty sub-chain → no child workflow
        }

        var chainIds = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<FlowCanvasNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        cursor = byId.TryGetValue(cursor.NextTargetId!, out FlowCanvasNode? first) ? first : null;
        while (cursor != null && seen.Add(cursor.Id))
        {
            ordered.Add(cursor);
            chainIds.Add(cursor.Id);
            cursor = !string.IsNullOrEmpty(cursor.NextTargetId) && byId.TryGetValue(cursor.NextTargetId, out FlowCanvasNode? next)
                ? next
                : null;
        }

        string? Clean(string? id) => id != null && chainIds.Contains(id) ? id : null;

        return ordered.Select(node => new WorkflowNode
        {
            Id = node.Id,
            Type = node.BlockType,
            Config = new Dictionary<string, string>(node.Config, StringComparer.Ordinal),
            Next = Clean(node.NextTargetId),
            BranchTrue = Clean(node.BranchTrueTargetId),
            BranchFalse = Clean(node.BranchFalseTargetId),
            BranchError = Clean(node.BranchErrorTargetId),
            Branches = node.BranchTargets == null || node.BranchTargets.Count == 0
                ? null
                : node.BranchTargets
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && chainIds.Contains(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            X = node.X,
            Y = node.Y
        }).ToList();
    }

    // ======================================================================
    // Command adoption (a saved flow takes over its source Command row)
    // ======================================================================

    /// <summary>
    /// A command-triggered flow without a linked row gets one created (disabled,
    /// flow-owned) so it shows in the Commands page CUSTOM list and EDIT routes
    /// back into this flow. A same-named row is adopted instead of duplicated.
    /// Best-effort — never fails the save.
    /// </summary>
    private async Task EnsureCommandRowAsync(Workflow workflow)
    {
        try
        {
            if (!FlowCommandRowBuilder.NeedsCommandRow(workflow))
            {
                return;
            }

            string name = workflow.Trigger!.CommandName!.Trim();
            Command? existing = await _dataService.GetCommandByNameAsync(name);
            if (existing == null)
            {
                existing = FlowCommandRowBuilder.BuildRow(workflow);
                await _dataService.SaveCommandAsync(existing);
                _logger.LogInformation("Flow {WorkflowId} created command row {CommandName} ({CommandId})",
                    workflow.Id, existing.Name, existing.Id);
            }

            workflow.SourceCommandId = existing.Id;
            _sourceCommandId = existing.Id;
            if (_existingWorkflow != null)
            {
                _existingWorkflow.SourceCommandId = existing.Id;
            }

            await _dataService.SaveWorkflowAsync(WorkflowJson.ToRow(workflow)); // carry SourceCommandId in the graph
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not link flow {WorkflowId} to a command row — it may not show under CUSTOM",
                workflow.Id);
        }
    }

    /// <summary>
    /// After a successful save of a command-triggered flow that carries a
    /// SourceCommandId (seeded from a Command row via
    /// <see cref="InitializeFromCommandAsync"/> or set by
    /// <see cref="EnsureCommandRowAsync"/>):
    /// <para>
    /// 1. The legacy Command row is disabled so <c>CommandService</c> never
    ///    double-fires — the flow now owns the command. Built-in rows keep
    ///    their IsBuiltIn flag; they are only disabled, so deleting the flow
    ///    from the Commands list re-enables the original.
    /// </para>
    /// <para>
    /// 2. When the flow's command name matches a Discord built-in (a built-in
    ///    Command row or a moderation command claimed by the in-Discord
    ///    handler) and the trigger platform is Discord or any, the name is
    ///    merged into every bot's "DiscordBot.{botId}.DisabledBuiltIns" config
    ///    array — the exact list ChatViewModel reads before running the
    ///    in-Discord mod-command handler — so it stands down too.
    /// </para>
    /// Both steps are best-effort: failures are logged and noted in the status
    /// text but never fail the save.
    /// </summary>
    private async Task AdoptCommandOwnershipAsync(Workflow workflow)
    {
        try
        {
            bool isCommandTrigger =
                string.Equals(workflow.Trigger?.Type, WorkflowTriggerTypes.Command, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(workflow.Trigger?.Type, WorkflowTriggerTypes.SlashCommand, StringComparison.OrdinalIgnoreCase);
            string? sourceCommandId = workflow.SourceCommandId;

            if (!isCommandTrigger || string.IsNullOrWhiteSpace(sourceCommandId))
            {
                return;
            }

            bool adopted = false;

            // 1) Retire the legacy Command row (it may be gone already — e.g.
            //    Discord moderation commands have no row at all).
            Command? legacy = await _dataService.GetCommandAsync(sourceCommandId);
            if (legacy is { IsEnabled: true })
            {
                legacy.IsEnabled = false;
                legacy.UpdatedAt = DateTime.UtcNow;
                await _dataService.SaveCommandAsync(legacy);
                adopted = true;
                _logger.LogInformation(
                    "Flow {WorkflowId} adopted command {CommandName} — legacy row {CommandId} disabled",
                    workflow.Id, legacy.Name, sourceCommandId);
            }

            // 2) Shadow same-named Discord built-ins for every configured bot.
            string? commandName = workflow.Trigger?.CommandName;
            if (!string.IsNullOrWhiteSpace(commandName)
                && await MergeDisabledBuiltInsAsync(commandName, workflow.Trigger?.Platform))
            {
                adopted = true;
            }

            if (adopted)
            {
                StatusText += " The legacy command now runs as this flow.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Command adoption after flow save failed for workflow {WorkflowId} — the legacy command may still respond",
                workflow.Id);
            StatusText += " (Note: the legacy command row could not be retired — it may still respond.)";
        }
    }

    /// <summary>
    /// Merges <paramref name="commandName"/> into every Discord bot's
    /// "DiscordBot.{botId}.DisabledBuiltIns" config array, read-modify-write in
    /// the exact JSON shape the rest of the app writes (normalized lowercase
    /// name list, ordinal order). Only runs when the flow's trigger platform
    /// is Discord or any, and only when the name actually collides with a
    /// Discord built-in. Returns true when at least one bot list changed.
    /// </summary>
    private async Task<bool> MergeDisabledBuiltInsAsync(string commandName, string? triggerPlatform)
    {
        if (!string.IsNullOrWhiteSpace(triggerPlatform) &&
            !string.Equals(triggerPlatform, "Any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(triggerPlatform, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            return false; // a Twitch/Kick-only flow never collides with Discord
        }

        string normalized = NormalizeCommandName(commandName);
        if (normalized.Length == 0)
        {
            return false;
        }

        // Does the name collide with a Discord built-in? Either a built-in
        // Command row (!commands, !uptime, …) or a moderation command the
        // in-Discord handler claims (kick, ban, unmute, …).
        bool shadowsBuiltIn = false;
        try
        {
            foreach (Command row in await _dataService.GetCommandsAsync(builtInOnly: true))
            {
                if (string.Equals(NormalizeCommandName(row.Name), normalized, StringComparison.Ordinal))
                {
                    shadowsBuiltIn = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check built-in command rows for name {CommandName}", commandName);
        }

        if (!shadowsBuiltIn &&
            DiscordBotIntegrationsViewModel.BuiltInModerationCommandNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            shadowsBuiltIn = true;
        }

        if (!shadowsBuiltIn)
        {
            return false;
        }

        bool changed = false;
        foreach (DiscordBot bot in await _dataService.GetDiscordBotsAsync())
        {
            try
            {
                string key = $"DiscordBot.{bot.Id}.DisabledBuiltIns";
                var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                string? stored = await _dataService.GetConfigValueAsync(key);
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    List<string>? names = JsonSerializer.Deserialize<List<string>>(stored);
                    if (names != null)
                    {
                        foreach (string name in names)
                        {
                            string entry = NormalizeCommandName(name);
                            if (entry.Length > 0)
                            {
                                disabled.Add(entry);
                            }
                        }
                    }
                }

                if (disabled.Add(normalized))
                {
                    await _dataService.SetConfigValueAsync(
                        key,
                        JsonSerializer.Serialize(disabled.OrderBy(n => n, StringComparer.Ordinal).ToList()));
                    changed = true;
                    _logger.LogInformation(
                        "Flow command '{CommandName}' shadowed for Discord bot {BotId} ({Key})",
                        commandName, bot.Id, key);
                }
            }
            catch (Exception ex)
            {
                // One bad bot row must never block the others.
                _logger.LogWarning(ex, "Could not update disabled built-ins for Discord bot {BotId}", bot.Id);
            }
        }

        return changed;
    }

    /// <summary>Strips an optional '!'/'.' prefix and lowercases ("!Lurk" → "lurk").</summary>
    private static string NormalizeCommandName(string? raw)
    {
        string name = (raw ?? string.Empty).Trim();
        while (name.Length > 0 && (name[0] == '!' || name[0] == '.'))
        {
            name = name[1..].Trim();
        }
        return name.ToLowerInvariant();
    }

    /// <summary>Runs the assembled (unsaved) workflow via the manual trigger — save-less fire.</summary>
    [RelayCommand]
    public void TestRun()
    {
        string? problem = Validate();
        if (problem != null)
        {
            StatusText = problem;
            return;
        }

        try
        {
            _triggerService.RunManual(AssembleWorkflow());
            StatusText = "Test run dispatched — check Automation ▸ History.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flow editor test run failed for workflow {WorkflowId}", _workflowId);
            StatusText = $"Test run failed: {ex.Message}";
        }
    }

    /// <summary>
    /// EXPORT: opens the export chooser — online share code (marketplace),
    /// self-contained offline code, or the classic .flow.json file.
    /// </summary>
    [RelayCommand]
    private async Task ExportJson()
    {
        if (Nodes.Count == 0)
        {
            StatusText = "Add at least one block before exporting.";
            return;
        }

        var dialog = new Views.ExportCodeDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        switch (dialog.Choice)
        {
            case Views.ExportFlowChoice.OnlineCode:
                await ShareToMarketplaceAsync();
                break;
            case Views.ExportFlowChoice.OfflineCode:
                CopyOfflineCodeToClipboard();
                break;
            case Views.ExportFlowChoice.File:
                ExportToFile();
                break;
        }
    }

    /// <summary>Classic file export (also reachable from the export chooser).</summary>
    private void ExportToFile()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export flow",
                Filter = "Vyral flow (*.flow.json)|*.flow.json|JSON (*.json)|*.json",
                FileName = (string.IsNullOrWhiteSpace(FlowName) ? "flow" : FlowName.Trim()).Replace(' ', '-').ToLowerInvariant() + ".flow.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            File.WriteAllText(dialog.FileName, FlowSnapshotService.BuildDocument(BuildSnapshotDto()));
            StatusText = $"Exported to {Path.GetFileName(dialog.FileName)}.";
            _logger.LogInformation("Exported workflow {WorkflowId} to {Path}", _workflowId, dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flow export failed for workflow {WorkflowId}", _workflowId);
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// IMPORT: paste box that accepts online share codes, offline codes, or
    /// URLs containing a code — plus the classic .flow.json file picker.
    /// </summary>
    [RelayCommand]
    private async Task ImportJson()
    {
        var dialog = new Views.ImportCodeDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.FilePath != null)
        {
            try
            {
                TryImportSnapshot(
                    FlowSnapshotService.ParseDocument(File.ReadAllText(dialog.FilePath)),
                    Path.GetFileName(dialog.FilePath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow import failed");
                StatusText = $"Import failed: {ex.Message}";
            }
            return;
        }

        if (dialog.Code == null)
        {
            return;
        }

        await ImportFromCodeAsync(dialog.Code!);
    }

    /// <summary>Resolves a pasted share/offline code and loads it as the working copy.</summary>
    public async Task ImportFromCodeAsync(string input)
    {
        StatusText = "Resolving code…";
        var result = await _marketplace.ResolveImportInputAsync(input);
        if (!result.Success)
        {
            StatusText = result.Error ?? "Import failed.";
            return;
        }

        TryImportSnapshot(result.Snapshot, result.Label);
    }

    /// <summary>
    /// Loads a marketplace-shared command (parsed snapshot) as a fresh working
    /// copy. Same path as file import — review, then SAVE to persist.
    /// </summary>
    public void ImportSharedSnapshot(FlowEditorSnapshotDto snapshot, string sourceLabel)
    {
        TryImportSnapshot(snapshot, sourceLabel);
    }

    // ==================================================================================
    // Marketplace: share to the cloud + self-contained offline paste-code
    // (both reachable from the EXPORT chooser)
    // ==================================================================================

    /// <summary>Shares the current flow to the marketplace via a BotGhost webhook (code copied on success).</summary>
    private async Task ShareToMarketplaceAsync()
    {
        if (Nodes.Count == 0)
        {
            StatusText = "Add at least one block before sharing.";
            return;
        }

        try
        {
            var settings = await _marketplace.LoadSettingsAsync();
            var dialog = new Views.ShareCommandDialog(
                FlowName,
                TriggerDescription,
                settings.LastAuthor ?? string.Empty)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true || dialog.Result == null)
            {
                return;
            }

            var share = dialog.Result;
            StatusText = "Submitting to the marketplace…";

            string sharecode = MarketplaceService.GenerateShareCode();
            string json = MarketplaceService.BuildShareDocumentJson(
                BuildSnapshotDto(), sharecode, share.Name, share.Description, share.Category, share.Author);
            var document = MarketplaceService.ParseShareDocument(json);
            if (document == null)
            {
                StatusText = "Share failed: could not build the command document.";
                return;
            }

            var (ok, error) = await _marketplace.SubmitShareAsync(document);
            if (!ok)
            {
                StatusText = $"Share failed: {error}";
                return;
            }

            await settings.SaveAsync(_dataService); // remember the author name
            System.Windows.Clipboard.SetText(sharecode);
            StatusText = $"Shared as {sharecode} (copied) — waiting for it to appear in the marketplace…";

            _ = ConfirmPublishedAsync(sharecode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace share failed for workflow {WorkflowId}", _workflowId);
            StatusText = $"Share failed: {ex.Message}";
        }
    }

    private async Task ConfirmPublishedAsync(string sharecode)
    {
        try
        {
            bool published = await _marketplace.WaitUntilPublishedAsync(sharecode);
            StatusText = published
                ? $"Shared as {sharecode} — it is now live in the marketplace."
                : $"Shared as {sharecode} — not visible yet; check the marketplace in a minute.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace publish confirmation failed for {Code}", sharecode);
        }
    }

    /// <summary>Copies a self-contained offline share code (no internet needed to import).</summary>
    private void CopyOfflineCodeToClipboard()
    {
        if (Nodes.Count == 0)
        {
            StatusText = "Add at least one block before sharing.";
            return;
        }

        try
        {
            string code = MarketplaceService.EncodeOfflineCode(FlowSnapshotService.BuildDocument(BuildSnapshotDto()));
            System.Windows.Clipboard.SetText(code);
            StatusText = "Offline share code copied — anyone can paste it into IMPORT to load this flow.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Offline code copy failed for workflow {WorkflowId}", _workflowId);
            StatusText = $"Copy failed: {ex.Message}";
        }
    }

    /// <summary>Core import: rebuilds form + canvas from a parsed snapshot as a new working copy.</summary>
    private bool TryImportSnapshot(FlowEditorSnapshotDto? dto, string sourceLabel)
    {
        if (dto == null)
        {
            StatusText = $"Import failed: {sourceLabel} is not a Vyral flow document.";
            return false;
        }

        try
        {
            _suppressSnapshot = true;
            try
            {
                // Imports keep their own ids — but never collide with the flow
                // currently open (the import becomes the working copy).
                _existingWorkflow = null;
                _savedOnce = false;
                _loadedFromImport = true;
                _workflowId = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id;
                _existingCategory = null;
                _existingCreatedAt = DateTime.UtcNow;
                _sourceCommandId = null; // an import is a fresh working copy
                _componentSubNodes.Clear();

                FlowName = dto.Name ?? string.Empty;
                FlowEnabled = dto.Enabled;
                TriggerDescription = dto.Description ?? string.Empty;
                LoadTriggerIntoForm(dto.Trigger ?? new FlowTriggerDto { Type = WorkflowTriggerTypes.Manual });
                LoadOptionsIntoForm(dto.Options?.Select(o => new WorkflowOption { Type = o.Type, Value = o.Value }));
                LoadParametersIntoForm(dto.Parameters); // tolerant of missing/malformed sections
                ResetParameterEditor();
                _entryNodeId = string.IsNullOrWhiteSpace(dto.EntryNodeId) ? null : dto.EntryNodeId;
                _errorHandlerEntryId = string.IsNullOrWhiteSpace(dto.ErrorHandlerEntryId) ? null : dto.ErrorHandlerEntryId;

                var nodes = dto.Nodes ?? new List<FlowNodeDto>();
                LoadNodesIntoCanvas(nodes);

                SelectedNode = null;
            }
            finally
            {
                _suppressSnapshot = false;
            }

            OnPropertyChanged(nameof(TriggerComponentHint));
            IsDirty = true;
            ClearHistory();
            PushUndoSnapshot();
            FitCanvasAfterLayout();
            UpdateTriggerCard();
            RefreshParameterCards();
            StatusText = $"Imported {sourceLabel} — review and SAVE to persist it.";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flow import failed for {Source}", sourceLabel);
            StatusText = $"Import failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Closes the editor (autosaves the draft first, then notifies the host overlay).</summary>
    [RelayCommand]
    public async Task Close()
    {
        _autosaveTimer.Stop();
        _snapshotDebounce.Stop();
        if (IsDirty)
        {
            await SaveDraftAsync();
        }

        _canvas?.Focus();
        EditorClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Flushes pending autosave state when the host tears the view down.</summary>
    public async Task ShutdownAsync()
    {
        _autosaveTimer.Stop();
        _snapshotDebounce.Stop();
        DetachCanvas();
        if (IsDirty)
        {
            await SaveDraftAsync();
        }
    }

    // ======================================================================
    // Side rail: six panels (Blocks / Variables / Error Logs / Commands /
    // Timed Events / Block Templates) + hamburger collapse
    // ======================================================================

    /// <summary>
    /// Index of the panel shown next to the icon rail (0 Blocks … 5 Block
    /// Templates). Selecting a panel refreshes its data lazily, so lists are
    /// always fresh when the user opens them.
    /// </summary>
    [ObservableProperty]
    private int _selectedPanelIndex;

    /// <summary>Bold panel title for the shared panel header.</summary>
    public string SelectedPanelTitle => SelectedPanelIndex switch
    {
        1 => "Variables",
        2 => "Error Logs",
        3 => "Commands",
        4 => "Timed Events",
        5 => "Block Templates",
        _ => "Blocks"
    };

    partial void OnSelectedPanelIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedPanelTitle));
        RefreshPanelData(value);
    }

    /// <summary>Rail icon click: switch the panel (parameter "0"…"5") and make sure the left side is expanded.</summary>
    [RelayCommand]
    private void SelectRailPanel(string? parameter)
    {
        if (int.TryParse(parameter, out int index))
        {
            SelectedPanelIndex = index;
        }

        IsLeftCollapsed = false;
    }

    /// <summary>Panel-header ☰: collapses the whole left side (rail + panel).</summary>
    [RelayCommand]
    private void ToggleLeftPanel() => IsLeftCollapsed = !IsLeftCollapsed;

    /// <summary>Lazy per-panel refresh on rail selection.</summary>
    private void RefreshPanelData(int index)
    {
        switch (index)
        {
            case 1:
                RefreshFlowVariables();
                break;
            case 2:
                _ = LoadErrorLogsAsync();
                break;
            case 3:
            case 4:
                _ = LoadSidePanelWorkflowsAsync();
                break;
            case 5:
                RefreshBlockTemplates();
                break;
        }
    }

    // ----------------------------------------------------------------------
    // Panel 2 — Variables (auto-collected from THIS flow)
    // ----------------------------------------------------------------------

    /// <summary>One row of the Variables panel: display name, {token} and its origin badge.</summary>
    public sealed class FlowVariableRowViewModel
    {
        public string DisplayName { get; }
        public string Token { get; }
        public string Badge { get; }

        public FlowVariableRowViewModel(string name, string badge)
        {
            string clean = name.Trim().Trim('{', '}').Trim();
            DisplayName = clean;
            Token = $"{{{clean}}}";
            Badge = badge;
        }
    }

    /// <summary>Search filter for the Variables panel.</summary>
    [ObservableProperty]
    private string _variableSearch = string.Empty;

    public ObservableCollection<FlowVariableRowViewModel> FlowVariables { get; } = new();

    public bool VariablesEmpty => FlowVariables.Count == 0;

    /// <summary>Stay-open "create variable" popup (Name box + DONE).</summary>
    [ObservableProperty]
    private bool _isCreateVariablePopupOpen;

    [ObservableProperty]
    private string _variableDraftName = string.Empty;

    partial void OnVariableSearchChanged(string value) => RefreshFlowVariables();

    /// <summary>
    /// Rebuilds the Variables list from everything relevant to THIS flow:
    /// typed Parameters ("Parameter"), set_variable nodes ("Flow"), block
    /// outputs ("Built-in") and the run context ("Context").
    /// </summary>
    private void RefreshFlowVariables()
    {
        string needle = VariableSearch?.Trim() ?? string.Empty;
        FlowVariables.Clear();

        void Add(string? rawName, string badge)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return;
            }

            var row = new FlowVariableRowViewModel(rawName, badge);
            if (needle.Length > 0 &&
                !row.Token.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                !badge.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FlowVariables.Add(row);
        }

        foreach (WorkflowParameterRowViewModel parameter in ParameterRows)
        {
            Add(string.IsNullOrWhiteSpace(parameter.NormalizedName) ? parameter.Name : parameter.NormalizedName, "Parameter");
        }

        foreach (FlowCanvasNode node in Nodes)
        {
            if (string.Equals(node.BlockType, "set_variable", StringComparison.OrdinalIgnoreCase) &&
                node.Config.TryGetValue("name", out string? variableName))
            {
                Add(variableName, "Flow");
            }
        }

        foreach (string name in new[] { "response", "http_response", "http_status", "random", "builtin_response" })
        {
            Add(name, "Built-in");
        }

        foreach (string name in new[]
                 {
                     "username", "channel", "platform", "message", "args", "command",
                     "guild", "userRoles", "buttonId", "customId", "values"
                 })
        {
            Add(name, "Context");
        }

        OnPropertyChanged(nameof(VariablesEmpty));
    }

    /// <summary>Opens the stay-open create-variable popup.</summary>
    [RelayCommand]
    private void BeginCreateVariable()
    {
        CloseAllPopups();
        VariableDraftName = string.Empty;
        IsCreateVariablePopupOpen = true;
    }

    /// <summary>
    /// Popup DONE: creating a variable = placing a Set Variable block preset
    /// with the given name (selected, near the view center).
    /// </summary>
    [RelayCommand]
    private void CreateVariable()
    {
        IsCreateVariablePopupOpen = false;
        string name = VariableDraftName.Trim().Trim('{', '}').Trim();
        if (name.Length == 0)
        {
            StatusText = "Give the variable a name first.";
            return;
        }

        FlowCanvasNode? node = CreateNodeNearViewCenter("set_variable");
        if (node == null)
        {
            StatusText = "The canvas is not ready yet — try again.";
            return;
        }

        node.Config["name"] = name;
        node.Label = $"Set {name}";
        _canvas?.RefreshNode(node.Id);
        SelectedNode = node;
        MarkDirty();
        RefreshFlowVariables();
        StatusText = $"Variable {{{name}}} created — set its value in the Set Variable block.";
    }

    /// <summary>
    /// Drops a new node of <paramref name="blockType"/> at the cascading view
    /// center (same placement math as <see cref="PlaceBlock"/>). Returns the
    /// fresh node, or null when no canvas is attached.
    /// </summary>
    private FlowCanvasNode? CreateNodeNearViewCenter(string blockType)
    {
        (double centerX, double centerY) = GetViewportCenterWorld();
        double offset = (_cascadeCount++ % 6) * 28d;
        return _canvas?.CreateNode(blockType, centerX - 90d + offset, centerY - 24d + offset);
    }

    // ----------------------------------------------------------------------
    // Panel 3 — Error Logs (workflow-execution failures)
    // ----------------------------------------------------------------------

    /// <summary>One row of the Error Logs panel.</summary>
    public sealed class FlowErrorRowViewModel
    {
        public string Title { get; }
        public string RelativeTime { get; }
        public string Message { get; }
        public string TriggerSummary { get; }

        public FlowErrorRowViewModel(string title, string relativeTime, string message, string triggerSummary)
        {
            Title = title;
            RelativeTime = relativeTime;
            Message = message;
            TriggerSummary = triggerSummary;
        }
    }

    public ObservableCollection<FlowErrorRowViewModel> ErrorLogs { get; } = new();

    public bool ErrorLogsEmpty => ErrorLogs.Count == 0;

    [ObservableProperty]
    private bool _isLoadingErrorLogs;

    private bool _loadingErrorLogsGuard;

    /// <summary>Indigo Refresh button.</summary>
    [RelayCommand]
    public async Task RefreshErrorLogsAsync() => await LoadErrorLogsAsync();

    /// <summary>
    /// Loads the most recent non-completed executions (errors only, newest
    /// first) for THIS workflow, falling back to all executions when this id
    /// has none yet. Never blocks the UI — failures land in the status text.
    /// </summary>
    private async Task LoadErrorLogsAsync()
    {
        if (_loadingErrorLogsGuard)
        {
            return;
        }

        _loadingErrorLogsGuard = true;
        IsLoadingErrorLogs = true;
        try
        {
            List<WorkflowExecution> errors;
            try
            {
                errors = await QueryErrorExecutionsAsync(_workflowId, 60);
                if (errors.Count == 0)
                {
                    errors = await QueryErrorExecutionsAsync(null, 200);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Flow editor could not load error logs for workflow {WorkflowId}", _workflowId);
                StatusText = $"Could not load error logs: {ex.Message}";
                return;
            }

            ErrorLogs.Clear();
            foreach (WorkflowExecution execution in errors)
            {
                ErrorLogs.Add(new FlowErrorRowViewModel(
                    string.Equals(execution.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                        ? "Run cancelled"
                        : "Node error",
                    RelativeTime(execution.StartedAt),
                    string.IsNullOrWhiteSpace(execution.Error) ? "(no error message recorded)" : execution.Error.Trim(),
                    string.IsNullOrWhiteSpace(execution.TriggerSummary) ? "manual run" : execution.TriggerSummary.Trim()));
            }

            OnPropertyChanged(nameof(ErrorLogsEmpty));
        }
        finally
        {
            _loadingErrorLogsGuard = false;
            IsLoadingErrorLogs = false;
        }
    }

    private async Task<List<WorkflowExecution>> QueryErrorExecutionsAsync(string? workflowId, int limit)
    {
        var rows = await _dataService.GetWorkflowExecutionsAsync(workflowId, limit);
        return rows.Where(e => !string.Equals(e.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(e => e.StartedAt)
                   .Take(30)
                   .ToList();
    }

    /// <summary>"just now / Nm ago / Nh ago / Nd ago" relative stamp (UTC input).</summary>
    internal static string RelativeTime(DateTime utc)
    {
        TimeSpan delta = DateTime.UtcNow - utc.ToUniversalTime();
        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return $"{(int)delta.TotalDays}d ago";
    }

    // ----------------------------------------------------------------------
    // Panels 4/5 — Commands & Events / Timed Events (workflow lists)
    // ----------------------------------------------------------------------

    private readonly List<FlowWorkflowRowViewModel> _panelCommandsMaster = new();
    private readonly List<FlowWorkflowRowViewModel> _panelEventsMaster = new();
    private readonly List<FlowWorkflowRowViewModel> _panelTimersMaster = new();

    public ObservableCollection<FlowWorkflowRowViewModel> FilteredPanelCommands { get; } = new();
    public ObservableCollection<FlowWorkflowRowViewModel> FilteredPanelEvents { get; } = new();
    public ObservableCollection<FlowWorkflowRowViewModel> FilteredPanelTimers { get; } = new();

    public bool PanelCommandsEmpty => FilteredPanelCommands.Count == 0;
    public bool PanelEventsEmpty => FilteredPanelEvents.Count == 0;
    public bool PanelTimersEmpty => FilteredPanelTimers.Count == 0;

    /// <summary>"Active Timers (N)" count (all timer flows, unfiltered).</summary>
    public int ActiveTimersCount => _panelTimersMaster.Count;

    /// <summary>Search filter shared by the Commands / Events / Timed Events lists.</summary>
    [ObservableProperty]
    private string _commandSearch = string.Empty;

    partial void OnCommandSearchChanged(string value) => ApplyPanelWorkflowFilter();

    /// <summary>Segmented tabs of the Commands panel: true = Commands, false = Events.</summary>
    [ObservableProperty]
    private bool _isCommandListTab = true;

    /// <summary>
    /// Derived Events tab — settable (like <see cref="IsConditionsTab"/>) so the
    /// panel's segmented ToggleButtons bind two-way; setting it to true clears
    /// the Commands tab.
    /// </summary>
    public bool IsEventListTab
    {
        get => !_isCommandListTab;
        set
        {
            if (!value || !_isCommandListTab)
            {
                return; // unchecking means the other tab was picked; already on Events
            }

            _isCommandListTab = false;
            OnPropertyChanged(nameof(IsCommandListTab));
            OnPropertyChanged(nameof(IsEventListTab));
        }
    }

    partial void OnIsCommandListTabChanged(bool value) => OnPropertyChanged(nameof(IsEventListTab));

    private bool _loadingPanelWorkflows;

    /// <summary>
    /// Re-queries every workflow row and splits it into the Commands / Events /
    /// Timed Events lists. Excludes the flow currently being edited and
    /// component child workflows (button / select-menu sub-flows).
    /// </summary>
    public async Task LoadSidePanelWorkflowsAsync()
    {
        if (_loadingPanelWorkflows)
        {
            return;
        }

        _loadingPanelWorkflows = true;
        try
        {
            var rows = await _dataService.GetWorkflowsAsync();
            var flows = new List<(WorkflowEntity Row, Workflow Flow)>();
            foreach (WorkflowEntity row in rows)
            {
                try
                {
                    flows.Add((row, WorkflowJson.FromRow(row)));
                }
                catch (Exception)
                {
                    // Unreadable row — it simply never shows in the lists.
                }
            }

            _panelCommandsMaster.Clear();
            _panelEventsMaster.Clear();
            _panelTimersMaster.Clear();

            foreach ((WorkflowEntity row, Workflow flow) in flows.OrderBy(f => f.Row.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!SidePanelCandidate(row, flow))
                {
                    continue;
                }

                var listRow = new FlowWorkflowRowViewModel(row, flow);
                switch (flow.Trigger.Type)
                {
                    case WorkflowTriggerTypes.Command:
                    case WorkflowTriggerTypes.SlashCommand:
                        _panelCommandsMaster.Add(listRow);
                        break;
                    case WorkflowTriggerTypes.Event:
                        _panelEventsMaster.Add(listRow);
                        break;
                    case WorkflowTriggerTypes.Timer:
                        _panelTimersMaster.Add(listRow);
                        break;
                }
            }

            ApplyPanelWorkflowFilter();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flow editor side panels could not load the workflow lists");
            StatusText = $"Could not load the flow lists: {ex.Message}";
        }
        finally
        {
            _loadingPanelWorkflows = false;
        }
    }

    /// <summary>A row belongs to the side lists when it is not THIS flow and not a component child.</summary>
    private bool SidePanelCandidate(WorkflowEntity row, Workflow flow) =>
        !string.Equals(row.Id, _workflowId, StringComparison.Ordinal) &&
        string.IsNullOrWhiteSpace(flow.ComponentKey);

    private void ApplyPanelWorkflowFilter()
    {
        string needle = CommandSearch?.Trim() ?? string.Empty;
        Refill(FilteredPanelCommands, _panelCommandsMaster, needle);
        Refill(FilteredPanelEvents, _panelEventsMaster, needle);
        Refill(FilteredPanelTimers, _panelTimersMaster, needle);

        OnPropertyChanged(nameof(PanelCommandsEmpty));
        OnPropertyChanged(nameof(PanelEventsEmpty));
        OnPropertyChanged(nameof(PanelTimersEmpty));
        OnPropertyChanged(nameof(ActiveTimersCount));

        static void Refill(
            ObservableCollection<FlowWorkflowRowViewModel> target,
            List<FlowWorkflowRowViewModel> source,
            string needle)
        {
            target.Clear();
            foreach (FlowWorkflowRowViewModel row in source)
            {
                if (needle.Length == 0 ||
                    row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    row.Description.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    target.Add(row);
                }
            }
        }
    }

    /// <summary>OK/Cancel guard: switching flows discards unsaved work.</summary>
    private bool ConfirmDiscardUnsavedChanges()
    {
        if (!IsDirty)
        {
            return true;
        }

        return System.Windows.MessageBox.Show(
            "You have unsaved changes in the current flow. Discard them and open another flow?",
            "Unsaved changes",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.OK;
    }

    /// <summary>"+ New Command": a fresh flow with a prefix-command trigger preset.</summary>
    [RelayCommand]
    public async Task StartNewCommandAsync()
    {
        if (!ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        DeleteDrafts(); // a brand-new command must not resurrect the "unsaved" draft
        await InitializeAsync(null, null);
        SelectedTriggerType = TriggerTypes.First(t => t.Key == WorkflowTriggerTypes.Command);
        StatusText = "New command — name it, add blocks, then SAVE.";
    }

    /// <summary>"Add New Timed Event": a fresh flow with a timer trigger preset (5 min default).</summary>
    [RelayCommand]
    public async Task StartNewTimedEventAsync()
    {
        if (!ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        DeleteDrafts();
        await InitializeAsync(null, null);
        SelectedTriggerType = TriggerTypes.First(t => t.Key == WorkflowTriggerTypes.Timer);
        TriggerIntervalMinutes = "5";
        StatusText = "New timed event — name it, set the interval, then SAVE.";
    }

    /// <summary>"+ New Moderator Command": a fresh flow with a mod_command trigger preset (tier "Mod").</summary>
    [RelayCommand]
    public async Task StartNewModCommandAsync()
    {
        if (!ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        DeleteDrafts();
        await InitializeAsync(null, null);
        SelectedTriggerType = TriggerTypes.First(t => t.Key == WorkflowTriggerTypes.ModCommand);
        TriggerMinimumTier = "Mod";
        StatusText = "New moderator command — set the verb and tier, add blocks, then SAVE.";
    }

    /// <summary>Row ✏️: loads that workflow into the editor (with unsaved-changes guard).</summary>
    [RelayCommand]
    public async Task EditPanelWorkflowAsync(FlowWorkflowRowViewModel? row)
    {
        if (row == null || !ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        await InitializeAsync(row.Flow, null);
        IsLeftCollapsed = false;
        StatusText = $"Editing \"{row.Name}\" — SAVE keeps the same workflow id.";
    }

    /// <summary>Row toggle: flips Enabled on the row, persists it and reloads the trigger service.</summary>
    [RelayCommand]
    public async Task TogglePanelWorkflowAsync(FlowWorkflowRowViewModel? row)
    {
        if (row == null)
        {
            return;
        }

        try
        {
            row.Row.Enabled = !row.Row.Enabled;
            row.Row.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveWorkflowAsync(row.Row);
            row.IsEnabled = row.Row.Enabled;

            try
            {
                await _triggerService.ReloadAsync();
            }
            catch (Exception reloadEx)
            {
                _logger.LogWarning(reloadEx, "Trigger reload after side-panel enable/disable failed");
            }

            StatusText = $"{row.Name} {(row.IsEnabled ? "enabled" : "disabled")}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flow editor could not toggle workflow {WorkflowId}", row.WorkflowId);
            StatusText = $"Could not update {row.Name}: {ex.Message}";
        }
    }

    /// <summary>Row 🗑️: confirm, delete the workflow row and reload the trigger service.</summary>
    [RelayCommand]
    public async Task DeletePanelWorkflowAsync(FlowWorkflowRowViewModel? row)
    {
        if (row == null)
        {
            return;
        }

        var choice = System.Windows.MessageBox.Show(
            $"Delete \"{row.Name}\"? This cannot be undone.",
            "Delete flow",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (choice != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _dataService.DeleteWorkflowAsync(row.WorkflowId);

            try
            {
                await _triggerService.ReloadAsync();
            }
            catch (Exception reloadEx)
            {
                _logger.LogWarning(reloadEx, "Trigger reload after side-panel delete failed");
            }

            StatusText = $"\"{row.Name}\" deleted.";
            await LoadSidePanelWorkflowsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flow editor could not delete workflow {WorkflowId}", row.WorkflowId);
            StatusText = $"Could not delete \"{row.Name}\": {ex.Message}";
        }
    }

    // ----------------------------------------------------------------------
    // Panel 6 — Block Templates (JSON files under %LocalAppData%\Vyral)
    // ----------------------------------------------------------------------

    /// <summary>One row of the Block Templates panel.</summary>
    public sealed class FlowTemplateRowViewModel
    {
        public string FileName { get; }
        public string Name { get; }
        public string Description { get; }
        public string BlockType { get; }
        public string Label { get; }
        public DateTime SavedAt { get; }
        public IReadOnlyDictionary<string, string> Config { get; }

        public FlowTemplateRowViewModel(
            string fileName, string name, string description, string blockType,
            string label, DateTime savedAt, IReadOnlyDictionary<string, string> config)
        {
            FileName = fileName;
            Name = name;
            Description = description;
            BlockType = blockType;
            Label = label;
            SavedAt = savedAt;
            Config = config;
        }

        /// <summary>Catalog title of the template's block (raw type as fallback).</summary>
        public string BlockTitle => BlockCatalog.Find(BlockType)?.Title ?? BlockType;

        /// <summary>Full template JSON — stashed on the drag payload ("vyral-block-template").</summary>
        public string ToJsonPayload() => JsonSerializer.Serialize(
            new BlockTemplateFile
            {
                Name = Name,
                Description = Description,
                BlockType = BlockType,
                Label = Label,
                SavedAt = SavedAt,
                Config = new Dictionary<string, string>(Config, StringComparer.Ordinal)
            },
            JsonOpts.Indented);
    }

    /// <summary>Persisted template file shape ({ name, description, blockType, label, config, savedAt }).</summary>
    internal sealed class BlockTemplateFile
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BlockType { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public Dictionary<string, string> Config { get; set; } = new();
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }

    private static string TemplateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vyral", "BlockTemplates");

    /// <summary>Search filter for the Block Templates panel.</summary>
    [ObservableProperty]
    private string _templateSearch = string.Empty;

    partial void OnTemplateSearchChanged(string value) => ApplyTemplateFilter();

    private readonly List<FlowTemplateRowViewModel> _blockTemplatesMaster = new();

    public ObservableCollection<FlowTemplateRowViewModel> BlockTemplates { get; } = new();

    public bool BlockTemplatesEmpty => BlockTemplates.Count == 0;

    /// <summary>Stay-open "save template" popup (Name + Description + DONE).</summary>
    [ObservableProperty]
    private bool _isTemplatePopupOpen;

    [ObservableProperty]
    private string _templateDraftName = string.Empty;

    [ObservableProperty]
    private string _templateDraftDescription = string.Empty;

    private void RefreshBlockTemplates()
    {
        _blockTemplatesMaster.Clear();
        try
        {
            Directory.CreateDirectory(TemplateDirectory);
            foreach (string path in Directory.EnumerateFiles(TemplateDirectory, "*.template.json"))
            {
                try
                {
                    var file = JsonSerializer.Deserialize<BlockTemplateFile>(File.ReadAllText(path), JsonOpts.Indented);
                    if (file == null || string.IsNullOrWhiteSpace(file.BlockType))
                    {
                        continue;
                    }

                    _blockTemplatesMaster.Add(new FlowTemplateRowViewModel(
                        Path.GetFileName(path),
                        string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileNameWithoutExtension(path) : file.Name.Trim(),
                        file.Description ?? string.Empty,
                        file.BlockType,
                        file.Label ?? string.Empty,
                        file.SavedAt == default ? File.GetLastWriteTimeUtc(path) : file.SavedAt,
                        new Dictionary<string, string>(file.Config ?? new Dictionary<string, string>(), StringComparer.Ordinal)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read block template {Path}", path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate block templates in {Directory}", TemplateDirectory);
        }

        ApplyTemplateFilter();
    }

    private void ApplyTemplateFilter()
    {
        string needle = TemplateSearch?.Trim() ?? string.Empty;
        BlockTemplates.Clear();
        foreach (FlowTemplateRowViewModel template in _blockTemplatesMaster.OrderByDescending(t => t.SavedAt))
        {
            if (needle.Length == 0 ||
                template.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                template.Description.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                template.BlockTitle.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                BlockTemplates.Add(template);
            }
        }

        OnPropertyChanged(nameof(BlockTemplatesEmpty));
    }

    /// <summary>"Save Selected Block": opens the stay-open name/description popup.</summary>
    [RelayCommand]
    private void BeginSaveBlockTemplate()
    {
        if (SelectedNode == null)
        {
            StatusText = "Select a block on the canvas first — templates save the selected block.";
            return;
        }

        CloseAllPopups();
        TemplateDraftName = string.IsNullOrWhiteSpace(SelectedNode.Label) ? SelectedBlock?.Title ?? string.Empty : SelectedNode.Label;
        TemplateDraftDescription = SelectedBlock?.Description ?? string.Empty;
        IsTemplatePopupOpen = true;
    }

    /// <summary>Popup DONE: writes the selected node (blockType, label, config) to a template file.</summary>
    [RelayCommand]
    public void SaveBlockTemplate()
    {
        IsTemplatePopupOpen = false;
        FlowCanvasNode? node = SelectedNode;
        if (node == null)
        {
            StatusText = "The selected block disappeared — select one and try again.";
            return;
        }

        string name = TemplateDraftName.Trim();
        if (name.Length == 0)
        {
            name = string.IsNullOrWhiteSpace(node.Label) ? NodeLabel(node.BlockType) : node.Label.Trim();
        }

        try
        {
            Directory.CreateDirectory(TemplateDirectory);
            string fileBase = SanitizeFileName(name);
            string path = Path.Combine(TemplateDirectory, $"{fileBase}.template.json");
            if (File.Exists(path))
            {
                // Keep every save — never overwrite; stamp the duplicate.
                path = Path.Combine(TemplateDirectory, $"{fileBase}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.template.json");
            }

            var file = new BlockTemplateFile
            {
                Name = name,
                Description = TemplateDraftDescription.Trim(),
                BlockType = node.BlockType,
                Label = node.Label,
                Config = new Dictionary<string, string>(node.Config, StringComparer.Ordinal),
                SavedAt = DateTime.UtcNow
            };
            File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOpts.Indented));

            StatusText = $"Template \"{name}\" saved — drag or click it in the Block Templates panel.";
            RefreshBlockTemplates();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flow editor could not save block template {Name}", name);
            StatusText = $"Could not save the template: {ex.Message}";
        }
    }

    /// <summary>Row ✕: deletes the template file.</summary>
    [RelayCommand]
    public void DeleteBlockTemplate(FlowTemplateRowViewModel? template)
    {
        if (template == null)
        {
            return;
        }

        try
        {
            string path = Path.Combine(TemplateDirectory, template.FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            StatusText = $"Template \"{template.Name}\" deleted.";
            RefreshBlockTemplates();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flow editor could not delete block template {File}", template.FileName);
            StatusText = $"Could not delete the template: {ex.Message}";
        }
    }

    /// <summary>
    /// Click (or double-click) a template: places its block at the cascading
    /// view center and applies the template's Config + Label — guaranteed
    /// correct, unlike the best-effort drag path.
    /// </summary>
    [RelayCommand]
    public void PlaceBlockTemplate(FlowTemplateRowViewModel? template)
    {
        if (template == null)
        {
            return;
        }

        FlowCanvasNode? node = CreateNodeNearViewCenter(template.BlockType);
        if (node == null)
        {
            double offset = (_cascadeCount % 6) * 28d;
            node = new FlowCanvasNode
            {
                BlockType = template.BlockType,
                Label = NodeLabel(template.BlockType),
                X = 400 + offset,
                Y = 300 + offset
            };
            SeedPlacedComparison(node);
            Nodes.Add(node);
        }

        ApplyTemplateToNode(node, template);
        SelectedNode = node;
        MarkDirty();
        StatusText = $"Placed \"{template.Name}\" — adjust its settings on the right.";
    }

    /// <summary>
    /// Best-effort drag support: after a drag-drop completes (the canvas
    /// already created a default-config node from the block type), find that
    /// fresh node and copy the template Config + Label onto it.
    /// </summary>
    public void ApplyTemplateAfterDrop(FlowTemplateRowViewModel template, HashSet<string> knownNodeIds)
    {
        FlowCanvasNode? fresh = Nodes.FirstOrDefault(node =>
            string.Equals(node.BlockType, template.BlockType, StringComparison.Ordinal) &&
            !knownNodeIds.Contains(node.Id));
        if (fresh == null)
        {
            return; // the drop never created a node — nothing to decorate
        }

        ApplyTemplateToNode(fresh, template);
        SelectedNode = fresh;
        MarkDirty();
    }

    /// <summary>Copies a template's Config + Label onto a canvas node (re-renders ports/label).</summary>
    private void ApplyTemplateToNode(FlowCanvasNode node, FlowTemplateRowViewModel template)
    {
        node.Config = new Dictionary<string, string>(template.Config, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(template.Label))
        {
            node.Label = template.Label;
        }
        else if (!string.IsNullOrWhiteSpace(template.Name))
        {
            node.Label = template.Name;
        }

        // A comparison template without stored rules would read as legacy —
        // keep placed comparisons in the seeded multi-rule mode instead.
        SeedPlacedComparison(node);

        _canvas?.RefreshNode(node.Id);
        if (IsComparisonNode(node) || IsLoopNode(node) || IsConditionNode(node))
        {
            // Config writes do not raise the canvas GraphChanged — sync the
            // attachment cards right away (e.g. a template carrying rules,
            // or a freshly placed condition needing its Else card).
            EnsureAttachmentCards();
        }
    }

    /// <summary>File-name-safe version of a template name.</summary>
    private static string SanitizeFileName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char character in name.Trim())
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }

        string result = builder.ToString().Trim();
        return result.Length == 0 ? "template" : result;
    }
}

/// <summary>
/// One workflow row of the side-rail lists (Commands / Events / Timed Events).
/// Carries the parsed <see cref="Workflow"/> (for EDIT re-load) alongside the
/// persisted <see cref="WorkflowEntity"/> (for the enable toggle).
/// </summary>
public partial class FlowWorkflowRowViewModel : ObservableObject
{
    public WorkflowEntity Row { get; }

    public Workflow Flow { get; }

    public string WorkflowId => Row.Id;

    public string Name => Row.Name;

    public string Description => Row.Description ?? Flow.Description ?? string.Empty;

    /// <summary>True for command / slash-command rows (title gets the trigger prefix).</summary>
    public bool IsCommandRow =>
        string.Equals(Flow.Trigger.Type, WorkflowTriggerTypes.Command, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Flow.Trigger.Type, WorkflowTriggerTypes.SlashCommand, StringComparison.OrdinalIgnoreCase);

    /// <summary>"/name" for commands, plain name otherwise.</summary>
    public string DisplayTitle => IsCommandRow
        ? $"{(string.IsNullOrWhiteSpace(Flow.Trigger.CommandPrefix) ? "!" : Flow.Trigger.CommandPrefix.Trim())}{Name}"
        : Name;

    /// <summary>Small label of the trigger kind shown under the name.</summary>
    public string TriggerLabel => Flow.Trigger.Type switch
    {
        WorkflowTriggerTypes.SlashCommand => "slash command",
        WorkflowTriggerTypes.Command => "prefix command",
        WorkflowTriggerTypes.ModCommand => $"!mod {(string.IsNullOrWhiteSpace(Flow.Trigger.CommandName) ? "?" : Flow.Trigger.CommandName.Trim())} ({ModTiers.NormalizeTier(Flow.Trigger.MinimumTier) ?? "Mod"})",
        WorkflowTriggerTypes.Event => $"event · {(string.IsNullOrWhiteSpace(Flow.Trigger.EventType) ? "any" : Flow.Trigger.EventType)}",
        WorkflowTriggerTypes.Timer => "timer",
        _ => Flow.Trigger.Type
    };

    /// <summary>Timer interval in minutes (default 15 when unset).</summary>
    public int IntervalMinutes => Flow.Trigger.IntervalMinutes ?? 15;

    /// <summary>"every N min" sub-label for timer rows.</summary>
    public string IntervalText => $"every {IntervalMinutes} min";

    /// <summary>Row toggle state (mirrors WorkflowEntity.Enabled).</summary>
    [ObservableProperty]
    private bool _isEnabled;

    public FlowWorkflowRowViewModel(WorkflowEntity row, Workflow flow)
    {
        Row = row;
        Flow = flow;
        _isEnabled = row.Enabled;
    }
}
