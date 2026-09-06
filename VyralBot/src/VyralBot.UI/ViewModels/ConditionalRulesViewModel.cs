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
using VyralBot.Core.Services.Rules;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.ViewModels;

public partial class ConditionalRulesViewModel : ViewModelBase
{
    private readonly IDataService _dataService;
    private readonly ILogger<ConditionalRulesViewModel> _logger;
    private readonly IRuleEvaluator? _ruleEvaluator;

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

    // Must match the event-type strings the live pipeline stores/looks up
    // (see ChatViewModel event handlers: GetSettingAsync("Kick"/"Twitch", ...)).
    public ObservableCollection<string> EventTypes { get; } = new()
    {
        "ChatMessage", "Follow", "Subscribe", "Resub", "GiftSub", "Raid",
        "Cheer", "Host", "RewardRedeemed", "KicksGifted", "StreamStatus"
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

    public ConditionalRulesViewModel(IDataService dataService, ILogger<ConditionalRulesViewModel> logger,
        IRuleEvaluator? ruleEvaluator = null)
    {
        _dataService = dataService;
        _logger = logger;
        _ruleEvaluator = ruleEvaluator;
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

            // The UI treats Value as the divisor for modulo ("count % 100 == 0"),
            // so persist it into ModuloDivisor as well for the evaluator.
            if (compOp == ComparisonOperator.Modulo && int.TryParse(ConditionValue, out var moduloDivisor))
            {
                newCondition.ModuloDivisor = moduloDivisor;
            }

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
    private async Task TestRuleAsync()
    {
        if (SelectedRule == null) return;

        try
        {
            // Sample context mirroring what the live pipeline feeds the evaluator
            // (numeric fields the condition builder examples use, plus booleans
            // for Status conditions).
            var testPlatform = string.IsNullOrEmpty(SelectedRule.Platform) || SelectedRule.Platform == "All"
                ? "Twitch"
                : SelectedRule.Platform;
            var testContext = new Dictionary<string, object>
            {
                ["username"] = "TestUser",
                ["platform"] = testPlatform,
                ["event_type"] = SelectedRule.EventType,
                ["count"] = 100,
                ["months"] = 1,
                ["viewers"] = 10,
                ["amount"] = 5,
                ["bits"] = 100,
                ["is_subscriber"] = false,
                ["is_moderator"] = false
            };

            // Parse the stored condition JSON
            RuleCondition? condition = null;
            string? jsonError = null;
            if (!string.IsNullOrWhiteSpace(SelectedRule.ConditionJson))
            {
                try
                {
                    condition = JsonSerializer.Deserialize<RuleCondition>(SelectedRule.ConditionJson);
                }
                catch (JsonException ex)
                {
                    jsonError = ex.Message;
                }
            }

            if (jsonError != null)
            {
                MessageBox.Show(
                    $"Rule '{SelectedRule.Name}' has invalid condition JSON and can never be evaluated.\n\n{jsonError}",
                    "Test Rule", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_ruleEvaluator == null)
            {
                MessageBox.Show("Rule evaluator is not available.", "Test Rule",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Evaluate the rule's condition against the sample context
            var matched = _ruleEvaluator.EvaluateCondition(condition, testContext);

            string reason;
            if (condition == null || string.IsNullOrWhiteSpace(condition.Field))
                reason = "The rule has no condition, so it matches every event.";
            else if (condition.Type == Core.Models.Rules.ConditionType.Time)
                reason = matched
                    ? "The time condition matched the current time."
                    : "The time condition did not match the current time.";
            else if (!testContext.ContainsKey(condition.Field))
                reason = $"Field '{condition.Field}' was not present in the sample context, so it cannot match.";
            else if (matched)
                reason = "The condition matched the sample values.";
            else
                reason = $"The condition did not match the sample value for '{condition.Field}'.";

            var info = $"Rule: {SelectedRule.Name}\n" +
                      $"Platform: {SelectedRule.Platform} (tested as {testPlatform})\n" +
                      $"Event: {SelectedRule.EventType}\n" +
                      $"Enabled: {SelectedRule.IsEnabled}\n" +
                      $"Priority: {SelectedRule.Priority}\n" +
                      $"Triggered: {SelectedRule.TriggerCount} times\n" +
                      $"Last Triggered: {SelectedRule.LastTriggeredAt?.ToLocalTime().ToString() ?? "Never"}\n\n" +
                      $"Condition: {SelectedRule.ConditionJson}\n\n" +
                      $"Result: {(matched ? "MATCHED" : "NOT MATCHED")}\n" +
                      $"Reason: {reason}";

            MessageBox.Show(info, "Test Rule", MessageBoxButton.OK,
                matched ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test rule");
            MessageBox.Show($"Failed to test rule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
