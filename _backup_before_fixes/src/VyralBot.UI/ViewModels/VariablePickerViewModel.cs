using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using VyralBot.Core.Services.Variables;

namespace VyralBot.UI.ViewModels;

public partial class VariablePickerViewModel : ObservableObject
{
    private readonly ILogger<VariablePickerViewModel> _logger;
    private readonly IVariableReplacementService _variableReplacementService;

    [ObservableProperty]
    private ObservableCollection<VariableInfo> _variables = new();

    [ObservableProperty]
    private ObservableCollection<VariableInfo> _filteredVariables = new();

    [ObservableProperty]
    private ObservableCollection<string> _categories = new();

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private VariableInfo? _selectedVariable;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string? _platform;

    [ObservableProperty]
    private bool _isCompactMode;

    [ObservableProperty]
    private TextBox? _currentTargetTextBox;

    public event EventHandler? VariableSelected;

    public VariablePickerViewModel(
        ILogger<VariablePickerViewModel> logger,
        IVariableReplacementService variableReplacementService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _variableReplacementService = variableReplacementService ?? throw new ArgumentNullException(nameof(variableReplacementService));

        Categories = new ObservableCollection<string> { "All" };
    }

    public async Task LoadVariablesAsync(string? platform = null)
    {
        try
        {
            Platform = platform;
            var variableDict = await _variableReplacementService.GetAvailableVariablesAsync(platform);

            Variables.Clear();
            var categorySet = new HashSet<string> { "All" };
            foreach (var kvp in variableDict)
            {
                var category = DetermineCategory(kvp.Key);
                categorySet.Add(category);
                Variables.Add(new VariableInfo
                {
                    Name = kvp.Key,
                    Value = kvp.Value,
                    Category = category,
                    DisplayName = FormatDisplayName(kvp.Key)
                });
            }
            Categories = new ObservableCollection<string>(categorySet.OrderBy(c => c));
            ApplyFilters();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load variables for picker");
        }
    }

    [RelayCommand]
    private void FilterByCategory(string category)
    {
        SelectedCategory = category;
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = Variables.AsEnumerable();

        // Category filter
        if (SelectedCategory != "All")
        {
            filtered = filtered.Where(v => v.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        // Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(v =>
                v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (v.Value?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        FilteredVariables = new ObservableCollection<VariableInfo>(filtered.OrderBy(v => v.Category).ThenBy(v => v.Name));
    }

    [RelayCommand]
    private void SelectVariable(VariableInfo? variable)
    {
        if (variable == null || CurrentTargetTextBox == null)
            return;

        var variableText = $"{{{variable.Name}}}";
        var caretIndex = CurrentTargetTextBox.CaretIndex;
        var currentText = CurrentTargetTextBox.Text ?? string.Empty;
        var newText = currentText.Insert(caretIndex, variableText);
        CurrentTargetTextBox.Text = newText;
        CurrentTargetTextBox.CaretIndex = caretIndex + variableText.Length;
        CurrentTargetTextBox.Focus();

        VariableSelected?.Invoke(this, EventArgs.Empty);
        IsOpen = false;
        SelectedVariable = null;
        SearchText = string.Empty;
        SelectedCategory = "All";
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        SelectedVariable = null;
        SearchText = string.Empty;
        SelectedCategory = "All";
    }

    private string DetermineCategory(string variableName)
    {
        if (variableName.StartsWith("twitch-"))
            return "Twitch";
        if (variableName.StartsWith("kick-"))
            return "Kick";
        if (variableName.StartsWith("youtube-"))
            return "Social";
        if (variableName.StartsWith("trovo-"))
            return "Social";
        if (variableName.StartsWith("tiktok-"))
            return "Social";

        var globalVars = new[] { "username", "message", "months", "number", "platform", 
            "viewers", "bits", "reward", "tier", "gifter", "raider", "hoster", 
            "sender", "amount", "balance", "role", "channel" };

        if (globalVars.Contains(variableName))
            return "Global";

        var socialVars = new[] { "twitter", "discord", "youtube", "full-socials" };
        if (socialVars.Contains(variableName))
            return "Social";

        return "Custom";
    }

    private string FormatDisplayName(string variableName)
    {
        // Convert kebab-case to Title Case
        return string.Join(" ", variableName.Split('-')
            .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
    }
}

public class VariableInfo
{
    public required string Name { get; set; }
    public string? Value { get; set; }
    public required string Category { get; set; }
    public required string DisplayName { get; set; }
}
