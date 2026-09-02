using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using VyralBot.Data;

namespace VyralBot.Core.Services.Variables;

/// <summary>
/// Implementation of variable replacement service with caching and nested variable support.
/// </summary>
public class VariableReplacementService : IVariableReplacementService
{
    private readonly ILogger<VariableReplacementService> _logger;
    private readonly IDataService _dataService;
    
    private static readonly Regex VariableNameRegex = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex VariablePlaceholderRegex = new(@"\{([a-z0-9]+(-[a-z0-9]+)*)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private const int MaxNestedDepth = 5;
    private const int CacheExpiryMinutes = 5;
    
    private readonly ConcurrentDictionary<string, CachedVariables> _cache = new();
    
    private class CachedVariables
    {
        public Dictionary<string, string> Variables { get; set; } = new();
        public DateTime ExpiresAt { get; set; }
    }

    public VariableReplacementService(
        ILogger<VariableReplacementService> logger,
        IDataService dataService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public async Task<string> ReplaceVariablesAsync(
        string template, 
        Dictionary<string, string>? contextVariables = null, 
        string? platform = null)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        try
        {
            // Get all variables (cached)
            var dbVariables = await GetCachedVariablesAsync(platform);
            
            // Merge context variables with database variables (context takes priority)
            var allVariables = new Dictionary<string, string>(dbVariables, StringComparer.OrdinalIgnoreCase);
            if (contextVariables != null)
            {
                foreach (var kvp in contextVariables)
                {
                    allVariables[kvp.Key] = kvp.Value;
                }
            }

            // Replace variables with nested support
            var result = await ReplaceVariablesRecursiveAsync(template, allVariables, 0);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing variables in template: {Template}", template);
            return template; // Return original on error
        }
    }

    private async Task<string> ReplaceVariablesRecursiveAsync(
        string template, 
        Dictionary<string, string> variables, 
        int depth)
    {
        if (depth >= MaxNestedDepth)
        {
            _logger.LogWarning("Max nested variable depth ({Depth}) reached, stopping recursion", MaxNestedDepth);
            return template;
        }

        var result = template;
        var matches = VariablePlaceholderRegex.Matches(template);
        var replacedAny = false;
        var circularRefCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var variableName = match.Groups[1].Value.ToLowerInvariant();
            
            // Circular reference detection
            if (circularRefCheck.Contains(variableName))
            {
                _logger.LogWarning("Circular reference detected for variable: {VariableName}", variableName);
                continue;
            }
            circularRefCheck.Add(variableName);

            if (variables.TryGetValue(variableName, out var value) && !string.IsNullOrEmpty(value))
            {
                result = result.Replace(match.Value, value);
                replacedAny = true;
            }
        }

        // If we replaced anything and there are still variables, recurse
        if (replacedAny && VariablePlaceholderRegex.IsMatch(result))
        {
            result = await ReplaceVariablesRecursiveAsync(result, variables, depth + 1);
        }

        return result;
    }

    public async Task<Dictionary<string, string>> GetAvailableVariablesAsync(string? platform = null)
    {
        return await GetCachedVariablesAsync(platform);
    }

    private async Task<Dictionary<string, string>> GetCachedVariablesAsync(string? platform = null)
    {
        var cacheKey = platform ?? "all";
        
        // Check cache
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Variables;
        }

        // Load from database
        var variables = await LoadVariablesFromDatabaseAsync(platform);
        
        // Cache the result
        _cache[cacheKey] = new CachedVariables
        {
            Variables = variables,
            ExpiresAt = DateTime.UtcNow.AddMinutes(CacheExpiryMinutes)
        };
        
        return variables;
    }

    private async Task<Dictionary<string, string>> LoadVariablesFromDatabaseAsync(string? platform = null)
    {
        try
        {
            var allVariables = await _dataService.GetVariablesAsync();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var variable in allVariables)
            {
                // Skip if platform filter doesn't match
                if (!string.IsNullOrEmpty(platform) && 
                    !string.IsNullOrEmpty(variable.Platforms) && 
                    !variable.Platforms.Contains(platform, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Only include variables that have values (editable or auto-updating)
                if (!string.IsNullOrEmpty(variable.Value))
                {
                    result[variable.Name] = variable.Value;
                }
            }

            _logger.LogDebug("Loaded {Count} variables from database (platform: {Platform})", 
                result.Count, platform ?? "all");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading variables from database");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<string?> GetVariableValueAsync(string variableName)
    {
        try
        {
            var variable = await _dataService.GetVariableByNameAsync(variableName);
            return variable?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting variable value for: {VariableName}", variableName);
            return null;
        }
    }

    public bool ValidateVariableName(string name, out string? errorMessage)
    {
        errorMessage = null;
        
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Variable name is required";
            return false;
        }
        
        if (!VariableNameRegex.IsMatch(name))
        {
            errorMessage = "Variable name must be lowercase with hyphens only (e.g., my-variable)";
            return false;
        }
        
        return true;
    }

    public Task InvalidateCacheAsync()
    {
        _cache.Clear();
        _logger.LogInformation("Variable cache invalidated");
        return Task.CompletedTask;
    }
}
