using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using VyralBot.Core.Services.Moderation;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// State for the ModeratorCommandBuilderDialog: category/action picking,
/// the per-action parameter fields, the live-composed !mod preview, the
/// tier badge and the favorites list (shared with the page's
/// ModeratorViewModel). The dialog only composes and validates — the run /
/// save-favorite outcomes are applied by ModeratorViewModel after the
/// dialog closes, reusing its existing send and persistence paths.
/// </summary>
public partial class ModeratorCommandBuilderViewModel : ObservableObject
{
    private const string ModerationCategory = "Moderation";
    private const string CurrencyCategory = "Currency";
    private const string EngagementCategory = "Engagement";
    private const string StreamFxCategory = "Stream FX";

    private const string CommandPrefix = "!mod";

    /// <summary>Verb → builder category. Covers every ActionTemplates verb.</summary>
    private static readonly Dictionary<string, string> VerbCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["timeout"] = ModerationCategory,
        ["kick"] = ModerationCategory,
        ["ban"] = ModerationCategory,
        ["purge"] = ModerationCategory,
        ["addcurrency"] = CurrencyCategory,
        ["removecurrency"] = CurrencyCategory,
        ["setbalance"] = CurrencyCategory,
        ["startgiveaway"] = EngagementCategory,
        ["cancelgiveaway"] = EngagementCategory,
        ["drawwinners"] = EngagementCategory,
        ["startpoll"] = EngagementCategory,
        ["endpoll"] = EngagementCategory,
        ["flashlights"] = StreamFxCategory,
        ["lightson"] = StreamFxCategory,
        ["lightsoff"] = StreamFxCategory,
        ["streamdeck"] = StreamFxCategory
    };

    /// <summary>Verbs whose amount field must parse as a positive whole number.</summary>
    private static readonly HashSet<string> IntegerAmountVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "timeout", "purge", "drawwinners" };

    /// <summary>Verbs whose amount field must parse as a number (currency values).</summary>
    private static readonly HashSet<string> NumberAmountVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "addcurrency", "removecurrency", "setbalance" };

    private readonly ModeratorViewModel _owner;
    private readonly IReadOnlyList<ModCommandTemplate> _allTemplates;

    public ModeratorCommandBuilderViewModel(ModeratorViewModel owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _allTemplates = owner.ActionTemplates;

        // Shared with the page VM so dialog deletions persist through the
        // existing "Mod.FavoriteCommands" store.
        FavoriteCommands = owner.FavoriteCommands;

        SelectedCategory = ModerationCategory;
    }

    // ======================================================================
    // Category + action picking
    // ======================================================================

    public IReadOnlyList<string> Categories { get; } =
        new[] { ModerationCategory, CurrencyCategory, EngagementCategory, StreamFxCategory };

    [ObservableProperty]
    private string? _selectedCategory;

    /// <summary>The selected category's templates (filtered from ActionTemplates).</summary>
    public IReadOnlyList<ModCommandTemplate> Templates { get; private set; } = [];

    partial void OnSelectedCategoryChanged(string? value)
    {
        Templates = _allTemplates
            .Where(t => string.Equals(CategoryOf(t.Verb), value, StringComparison.Ordinal))
            .ToList();
        OnPropertyChanged(nameof(Templates));

        // Keep the selection when it survives the category switch.
        SelectedTemplate = Templates.FirstOrDefault(t => ReferenceEquals(t, SelectedTemplate))
            ?? Templates.FirstOrDefault();
    }

    [ObservableProperty]
    private ModCommandTemplate? _selectedTemplate;

    // ======================================================================
    // Parameter fields (visibility driven by the selected template)
    // ======================================================================

    [ObservableProperty]
    private string _target = string.Empty;

    [ObservableProperty]
    private string _amount = string.Empty;

    [ObservableProperty]
    private string _reason = string.Empty;

    /// <summary>Whether the builder's target field applies to the selected template.</summary>
    public bool IsTargetVisible => SelectedTemplate?.NeedsTarget == true;

    public bool IsAmountVisible => SelectedTemplate?.NeedsAmount == true;

    public bool IsReasonVisible => SelectedTemplate?.NeedsReason == true;

    public string AmountLabel => SelectedTemplate?.AmountLabel ?? "Amount";

    partial void OnSelectedTemplateChanged(ModCommandTemplate? value)
    {
        OnPropertyChanged(nameof(IsTargetVisible));
        OnPropertyChanged(nameof(IsAmountVisible));
        OnPropertyChanged(nameof(IsReasonVisible));
        OnPropertyChanged(nameof(AmountLabel));
        TierBadge = BuildTierBadge();
        UpdateComposedPreview();
    }

    partial void OnTargetChanged(string value) => UpdateComposedPreview();

    partial void OnAmountChanged(string value) => UpdateComposedPreview();

    partial void OnReasonChanged(string value) => UpdateComposedPreview();

    // ======================================================================
    // Preview, tier badge, validation
    // ======================================================================

    /// <summary>Live-composed !mod command line (read-only preview in the dialog).</summary>
    [ObservableProperty]
    private string _composedPreview = CommandPrefix;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>"Requires {ModTiers.MinimumTier(verb)} or higher", or "Custom" for unknown verbs.</summary>
    [ObservableProperty]
    private string _tierBadge = "Custom";

    private string BuildTierBadge()
    {
        if (SelectedTemplate is null)
        {
            return "Custom";
        }

        var minimum = ModTiers.MinimumTier(SelectedTemplate.Verb);
        return minimum is null ? "Custom" : $"Requires {minimum} or higher";
    }

    private void UpdateComposedPreview()
    {
        var parts = new List<string> { CommandPrefix };

        if (SelectedTemplate is not null)
        {
            parts.Add(SelectedTemplate.Verb);
        }

        if (SelectedTemplate?.NeedsTarget == true && !string.IsNullOrWhiteSpace(Target))
        {
            parts.Add(Target.Trim());
        }

        if (SelectedTemplate?.NeedsAmount == true && !string.IsNullOrWhiteSpace(Amount))
        {
            parts.Add(Amount.Trim());
        }

        if (SelectedTemplate?.NeedsReason == true && !string.IsNullOrWhiteSpace(Reason))
        {
            parts.Add(Reason.Trim());
        }

        ComposedPreview = string.Join(' ', parts);
    }

    /// <summary>
    /// Run/Save gate: the selected template's required fields must be filled
    /// and numeric amounts must parse. Reason stays optional — the composed
    /// line only appends it when non-empty, matching the page's behavior.
    /// </summary>
    public bool Validate()
    {
        var template = SelectedTemplate;
        if (template is null)
        {
            ValidationError = "Pick an action first.";
            return false;
        }

        if (template.NeedsTarget && string.IsNullOrWhiteSpace(Target))
        {
            ValidationError = $"{template.Name} needs a target (username or Discord user ID).";
            return false;
        }

        if (template.NeedsAmount && string.IsNullOrWhiteSpace(Amount))
        {
            ValidationError = $"{template.Name} needs a value for \"{template.AmountLabel}\".";
            return false;
        }

        if (template.NeedsAmount && !IsAmountValid(template.Verb, Amount.Trim(), out var amountError))
        {
            ValidationError = amountError;
            return false;
        }

        ValidationError = null;
        return true;
    }

    private static bool IsAmountValid(string verb, string amount, out string? error)
    {
        error = null;

        if (IntegerAmountVerbs.Contains(verb))
        {
            if (!int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                error = $"'{amount}' is not a whole number.";
                return false;
            }

            if (value <= 0)
            {
                error = "The number must be greater than zero.";
                return false;
            }

            if (verb.Equals("purge", StringComparison.OrdinalIgnoreCase) && value > 100)
            {
                error = "Purge takes 1-100 messages.";
                return false;
            }

            return true;
        }

        if (NumberAmountVerbs.Contains(verb))
        {
            if (!double.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                error = $"'{amount}' is not a number.";
                return false;
            }

            return true;
        }

        if (verb.Equals("flashlights", StringComparison.OrdinalIgnoreCase))
        {
            // Amount shape: "#RRGGBB <times>" — the color is optional, the
            // times (when present) must be within the 1-20 range.
            foreach (var token in amount.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith('#'))
                {
                    if (!Regex.IsMatch(token, "^#[0-9A-Fa-f]{6}$"))
                    {
                        error = $"'{token}' is not a #RRGGBB hex color.";
                        return false;
                    }
                }
                else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var times)
                         && times is < 1 or > 20)
                {
                    error = "Light flash times must be 1-20.";
                    return false;
                }
            }
        }

        // streamdeck action names (and any free-text leftovers) stay unvalidated.
        return true;
    }

    // ======================================================================
    // Favorites
    // ======================================================================

    /// <summary>Shared instance from the page VM — deletions persist through its store.</summary>
    public ObservableCollection<string> FavoriteCommands { get; }

    /// <summary>Loads a stored favorite (e.g. "!mod kick user123 spam") back into the builder.</summary>
    [RelayCommand]
    private void LoadFavorite(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var tokens = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !tokens[0].Equals(CommandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            ValidationError = "That favorite is not a !mod command.";
            return;
        }

        var template = _allTemplates.FirstOrDefault(t => t.Verb.Equals(tokens[1], StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            ValidationError = $"No builder action matches '{tokens[1]}'.";
            return;
        }

        SelectedCategory = CategoryOf(template.Verb);
        SelectedTemplate = Templates.FirstOrDefault(t => t.Verb.Equals(template.Verb, StringComparison.OrdinalIgnoreCase))
            ?? template;

        // Field order matches the composed order: target, amount, reason.
        var args = tokens.Skip(2).ToArray();
        var index = 0;
        Target = template.NeedsTarget && index < args.Length ? args[index++] : string.Empty;
        Amount = template.NeedsAmount && index < args.Length ? args[index++] : string.Empty;
        Reason = template.NeedsReason && index < args.Length ? string.Join(' ', args.Skip(index)) : string.Empty;

        ValidationError = null;
    }

    /// <summary>Removes a favorite through the page VM (same store and persistence).</summary>
    [RelayCommand]
    private void DeleteFavorite(string? command) => _owner.DeleteFavoriteCommand.Execute(command);

    private static string CategoryOf(string verb) =>
        VerbCategories.TryGetValue(verb, out var category) ? category : ModerationCategory;
}
