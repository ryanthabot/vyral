namespace VyralBot.Core.Services.Variables;

/// <summary>
/// Service for replacing variable placeholders in templates with their actual values.
/// Supports context variables, database variables, nested variables, and platform filtering.
/// </summary>
public interface IVariableReplacementService
{
    /// <summary>
    /// Replaces all variables in a template string with their values.
    /// Variables are referenced using {variablename} syntax.
    /// </summary>
    /// <param name="template">The template string containing variable placeholders</param>
    /// <param name="contextVariables">Context-specific variables that take priority over database variables</param>
    /// <param name="platform">Optional platform filter (e.g., "Twitch", "Kick")</param>
    /// <returns>The template with all variables replaced</returns>
    Task<string> ReplaceVariablesAsync(
        string template, 
        Dictionary<string, string>? contextVariables = null, 
        string? platform = null);

    /// <summary>
    /// Gets all available variables as a dictionary, optionally filtered by platform.
    /// </summary>
    /// <param name="platform">Optional platform filter</param>
    /// <returns>Dictionary mapping variable names to their current values</returns>
    Task<Dictionary<string, string>> GetAvailableVariablesAsync(string? platform = null);

    /// <summary>
    /// Gets the current value of a specific variable from the database.
    /// </summary>
    /// <param name="variableName">The name of the variable (without braces)</param>
    /// <returns>The variable value, or null if not found</returns>
    Task<string?> GetVariableValueAsync(string variableName);

    /// <summary>
    /// Validates that a variable name follows the naming conventions.
    /// Variable names must be lowercase with hyphens only (kebab-case).
    /// </summary>
    /// <param name="name">The variable name to validate</param>
    /// <param name="errorMessage">The error message if validation fails</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateVariableName(string name, out string? errorMessage);

    /// <summary>
    /// Invalidates the variable cache, forcing a reload on the next access.
    /// Call this when variables are added, updated, or deleted.
    /// </summary>
    Task InvalidateCacheAsync();
}
