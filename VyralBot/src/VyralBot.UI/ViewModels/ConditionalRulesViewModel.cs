using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models.Rules;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class ConditionalRulesViewModel : ViewModelBase
{
    private readonly IDataService _dataService;
    private readonly ILogger<ConditionalRulesViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<AutoReplyRule> _rules = new();

    [ObservableProperty]
    private AutoReplyRule? _selectedRule;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editPlatform = "All";

    [ObservableProperty]
    private string _editEventType = string.Empty;

    [ObservableProperty]
    private bool _editIsEnabled = true;

    [ObservableProperty]
    private int _editPriority;

    [ObservableProperty]
    private string _editConditionJson = "{}";

    [ObservableProperty]
    private string _editReplyTemplate = string.Empty;

    [ObservableProperty]
    private string _conditionField = string.Empty;

    [ObservableProperty]
    private string _conditionOperator = "Equals";

    [ObservableProperty]
    private string _conditionValue = string.Empty;

    [ObservableProperty]
    private string _conditionType = "Value";

    public ObservableCollection<string> Platforms { get; } = new() { "All", "Twitch", "Kick", "Discord" };

    public ObservableCollection<string> EventTypes { get; } = new()
    {
        "Follow", "Subscription", "Raid", "Host", "Bits", "Donation",
        "SubscriptionGift", "ChatMessage", "ChannelPointRedemption"
    };

    public ObservableCollection<string> ConditionTypes { get; } = new()
    {
        "Value", "Status", "Time", "Complex"
    };

    public ObservableCollection<string> Operators { get; } = new()
    {
        "Equals", "NotEquals", "GreaterThan", "LessThan",
        "GreaterOrEqual", "LessOrEqual", "Contains", "StartsWith",
        "EndsWith", "Modulo"
    };

    public ConditionalRulesViewModel(IDataService dataService, ILogger<ConditionalRulesViewModel> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    public async Task LoadRulesAsync()
    {
        try
        {
            var rules = await _dataService.GetAutoReplyRulesAsync();
            Rules.Clear();
            foreach (var rule in rules)
            {
                Rules.Add(rule);
            }
            _logger.LogInformation("Loaded {Count} auto-reply rules", Rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load auto-reply rules");
            MessageBox.Show($"Failed to load rules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void NewRule()
    {
        SelectedRule = null;
        IsEditMode = true;
        EditName = "New Rule";
        EditPlatform = "All";
        EditEventType = "Follow";
        EditIsEnabled = true;
        EditPriority = 0;
        EditConditionJson = "{}";
        EditReplyTemplate = "Thank you, {username}!";
    }

    [RelayCommand]
    private void EditRule()
    {
        if (SelectedRule == null) return;

        IsEditMode = true;
        EditName = SelectedRule.Name;
        EditPlatform = SelectedRule.Platform;
        EditEventType = SelectedRule.EventType;
        EditIsEnabled = SelectedRule.IsEnabled;
        EditPriority = SelectedRule.Priority;
        EditConditionJson = SelectedRule.ConditionJson;
        EditReplyTemplate = SelectedRule.ReplyTemplate;
    }

    [RelayCommand]
    private async Task SaveRuleAsync()
    {
        try
        {
            var rule = SelectedRule ?? new AutoReplyRule();
            rule.Name = EditName;
            rule.Platform = EditPlatform;
            rule.EventType = EditEventType;
            rule.IsEnabled = EditIsEnabled;
            rule.Priority = EditPriority;
            rule.ConditionJson = EditConditionJson;
            rule.ReplyTemplate = EditReplyTemplate;

            var ruleId = await _dataService.SaveAutoReplyRuleAsync(rule);
            
            if (SelectedRule == null)
            {
                rule.Id = ruleId;
                Rules.Add(rule);
            }

            IsEditMode = false;
            _logger.LogInformation("Saved auto-reply rule: {Name}", rule.Name);
            
            await LoadRulesAsync(); // Refresh list
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save auto-reply rule");
            MessageBox.Show($"Failed to save rule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditMode = false;
        SelectedRule = null;
    }

    [RelayCommand]
    private async Task DeleteRuleAsync()
    {
        if (SelectedRule == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the rule '{SelectedRule.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _dataService.DeleteAutoReplyRuleAsync(SelectedRule.Id);
                Rules.Remove(SelectedRule);
                SelectedRule = null;
                _logger.LogInformation("Deleted auto-reply rule");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete auto-reply rule");
                MessageBox.Show($"Failed to delete rule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void AddCondition()
    {
        try
        {
            // Parse the condition type
            var condType = Enum.Parse<Core.Models.Rules.ConditionType>(ConditionType);
            var compOp = Enum.Parse<ComparisonOperator>(ConditionOperator);

            // Create new condition
            var newCondition = new RuleCondition
            {
                Type = condType,
                Field = ConditionField,
                Operator = compOp,
                Value = ConditionValue
            };

            // Parse existing condition JSON or create new root condition
            RuleCondition? rootCondition = null;
            if (!string.IsNullOrWhiteSpace(EditConditionJson) && EditConditionJson != "{}")
            {
                try
                {
                    rootCondition = JsonSerializer.Deserialize<RuleCondition>(EditConditionJson);
                }
                catch
                {
                    // Invalid JSON, start fresh
                }
            }

            // If we already have a root condition, wrap both in a Complex condition with AND logic
            if (rootCondition != null)
            {
                var complexCondition = new RuleCondition
                {
                    Type = Core.Models.Rules.ConditionType.Complex,
                    LogicalOperator = LogicalOperator.And,
                    SubConditions = new() { rootCondition, newCondition }
                };
                EditConditionJson = JsonSerializer.Serialize(complexCondition, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                // First condition
                EditConditionJson = JsonSerializer.Serialize(newCondition, new JsonSerializerOptions { WriteIndented = true });
            }

            // Clear input fields
            ConditionField = string.Empty;
            ConditionValue = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add condition");
            MessageBox.Show($"Failed to add condition: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ClearConditions()
    {
        EditConditionJson = "{}";
    }

    [RelayCommand]
    private Task TestRuleAsync()
    {
        if (SelectedRule == null) return Task.CompletedTask;

        try
        {
            // Show test results in a message box
            var info = $"Rule: {SelectedRule.Name}\n" +
                      $"Platform: {SelectedRule.Platform}\n" +
                      $"Event: {SelectedRule.EventType}\n" +
                      $"Enabled: {SelectedRule.IsEnabled}\n" +
                      $"Priority: {SelectedRule.Priority}\n" +
                      $"Triggered: {SelectedRule.TriggerCount} times\n" +
                      $"Last Triggered: {SelectedRule.LastTriggeredAt?.ToLocalTime().ToString() ?? "Never"}\n\n" +
                      $"Condition: {SelectedRule.ConditionJson}\n\n" +
                      $"Reply: {SelectedRule.ReplyTemplate}";

            MessageBox.Show(info, "Rule Details", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test rule");
            MessageBox.Show($"Failed to test rule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }
}
