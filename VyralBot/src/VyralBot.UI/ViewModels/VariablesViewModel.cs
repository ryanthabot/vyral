using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class VariablesViewModel : ViewModelBase
{
    private readonly ILogger<VariablesViewModel> _logger;
    private readonly IDataService _dataService;
    private readonly IVariableReplacementService _variableReplacementService;
    
    private static readonly Regex VariableNameRegex = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    private ObservableCollection<Variable> _allVariables = new();

    [ObservableProperty]
    private ObservableCollection<Variable> _variables = new();

    [ObservableProperty]
    private ObservableCollection<string> _categories = new();

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private Variable? _selectedVariable;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isAddDialogOpen;

    [ObservableProperty]
    private bool _isEditDialogOpen;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Add/Edit dialog properties
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDisplayName = string.Empty;

    [ObservableProperty]
    private string _editCategory = "Custom";

    [ObservableProperty]
    private string _editValue = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private bool _editIsCurrentlyEditing;

    public VariablesViewModel(
        ILogger<VariablesViewModel> logger,
        IDataService dataService,
        IVariableReplacementService variableReplacementService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _variableReplacementService = variableReplacementService ?? throw new ArgumentNullException(nameof(variableReplacementService));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadVariablesAsync();
            await LoadCategoriesAsync();
            _logger.LogInformation("VariablesViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize VariablesViewModel");
            StatusMessage = "Failed to load variables";
        }
    }

    [RelayCommand]
    private async Task LoadVariablesAsync()
    {
        try
        {
            var allVars = await _dataService.GetVariablesAsync();
            _allVariables = new ObservableCollection<Variable>(allVars);
            
            ApplyFilters();
            
            _logger.LogInformation("Loaded {Count} variables", _allVariables.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load variables");
            StatusMessage = "Failed to load variables";
        }
    }

    [RelayCommand]
    private async Task LoadCategoriesAsync()
    {
        try
        {
            var categories = await _dataService.GetVariableCategoriesAsync();
            Categories.Clear();
            Categories.Add("All");
            foreach (var category in categories.OrderBy(c => c))
            {
                Categories.Add(category);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load categories");
        }
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allVariables.AsEnumerable();

        // Category filter
        if (SelectedCategory != "All")
        {
            filtered = filtered.Where(v => v.Category == SelectedCategory);
        }

        // Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLower();
            filtered = filtered.Where(v =>
                v.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (v.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Variables.Clear();
        foreach (var variable in filtered.OrderBy(v => v.Name))
        {
            Variables.Add(variable);
        }
    }

    [RelayCommand]
    private void FilterByCategory(string category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private void StartAddVariable()
    {
        EditIsCurrentlyEditing = false;
        SelectedVariable = null;
        EditName = "";
        EditDisplayName = "";
        EditCategory = "Custom";
        EditValue = "";
        EditDescription = "";
        IsAddDialogOpen = true;
        StatusMessage = "";
    }

    [RelayCommand]
    private void StartEditVariable(Variable? variable)
    {
        if (variable == null)
        {
            StatusMessage = "No variable selected";
            return;
        }

        if (!variable.IsEditable)
        {
            StatusMessage = "This variable cannot be edited";
            return;
        }

        EditIsCurrentlyEditing = true;
        SelectedVariable = variable;
        EditName = variable.Name;
        EditDisplayName = variable.DisplayName;
        EditCategory = variable.Category;
        EditValue = variable.Value ?? "";
        EditDescription = variable.Description ?? "";
        IsEditDialogOpen = true;
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task SaveVariableAsync()
    {
        try
        {
            // Validation
            if (!ValidateVariableName(EditName, out var nameError))
            {
                StatusMessage = nameError ?? "Invalid variable name";
                return;
            }

            if (string.IsNullOrWhiteSpace(EditDisplayName))
            {
                StatusMessage = "Display name is required";
                return;
            }

            // Check for duplicate names when adding new
            if (!EditIsCurrentlyEditing)
            {
                var existing = await _dataService.GetVariableByNameAsync(EditName);
                if (existing != null)
                {
                    StatusMessage = $"A variable with name '{EditName}' already exists";
                    return;
                }
            }

            var variable = new Variable
            {
                Id = SelectedVariable?.Id ?? Guid.NewGuid().ToString(),
                Name = EditName.ToLowerInvariant(),
                DisplayName = EditDisplayName,
                Category = EditCategory,
                Prefix = null,
                Value = EditValue,
                MaxLength = 2000,
                IsEditable = true,
                IsSystem = false,
                IsAutoUpdating = false,
                ContainsNestedVariables = EditValue?.Contains("{") ?? false,
                Platforms = null,
                Description = EditDescription,
                CreatedAt = SelectedVariable?.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastSyncedAt = null
            };

            await _dataService.SaveVariableAsync(variable);

            // Invalidate cache so changes are reflected immediately
            await _variableReplacementService.InvalidateCacheAsync();

            if (EditIsCurrentlyEditing)
            {
                StatusMessage = $"Variable '{EditName}' updated successfully";
                _logger.LogInformation("Updated variable: {VariableName}", EditName);
            }
            else
            {
                StatusMessage = $"Variable '{EditName}' added successfully";
                _logger.LogInformation("Added new variable: {VariableName}", EditName);
            }

            await LoadVariablesAsync();
            CancelEdit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save variable: {VariableName}", EditName);
            StatusMessage = $"Failed to save variable: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteVariableAsync(Variable? variable)
    {
        if (variable == null)
        {
            StatusMessage = "No variable selected";
            return;
        }

        if (!variable.IsEditable || variable.IsSystem)
        {
            StatusMessage = "This variable cannot be deleted";
            return;
        }

        try
        {
            await _dataService.DeleteVariableAsync(variable.Id);
            
            // Invalidate cache so changes are reflected immediately
            await _variableReplacementService.InvalidateCacheAsync();
            
            StatusMessage = $"Variable '{variable.Name}' deleted successfully";
            _logger.LogInformation("Deleted variable: {VariableName}", variable.Name);
            await LoadVariablesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete variable: {VariableName}", variable.Name);
            StatusMessage = $"Failed to delete variable: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsAddDialogOpen = false;
        IsEditDialogOpen = false;
        SelectedVariable = null;
        StatusMessage = "";
    }

    private bool ValidateVariableName(string name, out string? error)
    {
        error = null;
        
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Variable name is required";
            return false;
        }
        
        if (!VariableNameRegex.IsMatch(name))
        {
            error = "Name must be lowercase with hyphens only (e.g., my-variable)";
            return false;
        }
        
        return true;
    }
}
