namespace VyralBot.Core.Services.Currency;

/// <summary>
/// Service for handling currency-related chat commands.
/// </summary>
public interface ICurrencyCommandService
{
    /// <summary>
    /// Process a chat command and return the response message (or null if not a currency command).
    /// </summary>
    Task<string?> ProcessCommandAsync(string command, string username, string platform, bool isModerator);
}
