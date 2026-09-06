using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Models.Rules;
using VyralBot.Core.Services.Rules;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Chat;

/// <summary>
/// Service for processing auto-reply messages with template variable replacement and conditional rules.
/// </summary>
public class AutoReplyService : IAutoReplyService
{
    private readonly ILogger<AutoReplyService> _logger;
    private readonly AutoReplyRateLimiter _rateLimiter;
    private readonly IDataService? _dataService;
    private readonly IRuleEvaluator? _ruleEvaluator;

    public AutoReplyService(
        ILogger<AutoReplyService> logger,
        IDataService? dataService = null,
        IRuleEvaluator? ruleEvaluator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimiter = new AutoReplyRateLimiter(TimeSpan.FromSeconds(30));
        _dataService = dataService;
        _ruleEvaluator = ruleEvaluator;
    }

    /// <summary>
    /// Processes an auto-reply message template by replacing variables.
    /// </summary>
    /// <param name="template">Message template with {variables}</param>
    /// <param name="variables">Dictionary of variable names and values</param>
    /// <returns>Processed message with variables replaced</returns>
    public string ProcessTemplate(string template, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var result = template;

        foreach (var (key, value) in variables)
        {
            var placeholder = $"{{{key}}}";
            result = result.Replace(placeholder, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Checks if an auto-reply should be sent based on rate limiting and conditional rules.
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="platform">Platform (Kick/Twitch)</param>
    /// <param name="eventType">Event type</param>
    /// <param name="context">Additional context for rule evaluation</param>
    /// <returns>Reply message if a rule matches, null otherwise. Rate limiting is
    /// the CALLER's responsibility (the send gate) — checking it here as well would
    /// double-consume the limiter slot and always reject the lookup.</returns>
    public async Task<string?> GetAutoReplyAsync(string username, string platform, string eventType, Dictionary<string, object>? context = null)
    {
        // If no data service or rule evaluator, can't check conditional rules
        if (_dataService == null || _ruleEvaluator == null)
        {
            return null;
        }

        try
        {
            // Load rules for this platform and event type
            var rules = await _dataService.GetAutoReplyRulesByPlatformAndEventAsync(platform, eventType);

            // Build context dictionary
            var evalContext = context ?? new Dictionary<string, object>();
            if (!evalContext.ContainsKey("username"))
                evalContext["username"] = username;
            if (!evalContext.ContainsKey("platform"))
                evalContext["platform"] = platform;
            if (!evalContext.ContainsKey("event_type"))
                evalContext["event_type"] = eventType;

            // Find first rule that matches conditions (rules are ordered by priority)
            foreach (var rule in rules)
            {
                try
                {
                    // Parse condition JSON
                    var condition = JsonSerializer.Deserialize<RuleCondition>(rule.ConditionJson);
                    
                    // Evaluate condition (null/empty condition means "always match",
                    // handled inside the evaluator)
                    if (_ruleEvaluator.EvaluateCondition(condition, evalContext))
                    {
                        // Condition met - increment trigger count
                        await _dataService.IncrementRuleTriggerCountAsync(rule.Id);

                        // Process template with variables
                        var variables = new Dictionary<string, string>();
                        foreach (var (key, value) in evalContext)
                        {
                            variables[key] = value?.ToString() ?? string.Empty;
                        }

                        return ProcessTemplate(rule.ReplyTemplate, variables);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to evaluate rule {RuleId} ({RuleName})", rule.Id, rule.Name);
                }
            }

            // No rules matched
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get auto-reply for {Platform}/{EventType}", platform, eventType);
            return null;
        }
    }

    /// <summary>
    /// Checks if an auto-reply should be sent based on rate limiting.
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="platform">Platform (Kick/Twitch)</param>
    /// <param name="eventType">Event type</param>
    /// <returns>True if reply should be sent, false if rate limited</returns>
    public bool ShouldSendReply(string username, string platform, string eventType)
    {
        var canReply = _rateLimiter.CanReply(username, platform, eventType);
        
        if (!canReply)
        {
            _logger.LogDebug("Auto-reply rate limited for {Username} on {Platform}/{EventType}", 
                username, platform, eventType);
        }

        return canReply;
    }

    /// <summary>
    /// Cleans up expired rate limiter entries.
    /// Should be called periodically (e.g., every 5 minutes).
    /// </summary>
    public void CleanupExpiredEntries()
    {
        _rateLimiter.CleanupExpired();
        _logger.LogDebug("Cleaned up expired auto-reply rate limiter entries");
    }

    /// <summary>
    /// Clears all rate limiting entries (for testing).
    /// </summary>
    public void ClearRateLimits()
    {
        _rateLimiter.Clear();
        _logger.LogInformation("Cleared all auto-reply rate limits");
    }
}
