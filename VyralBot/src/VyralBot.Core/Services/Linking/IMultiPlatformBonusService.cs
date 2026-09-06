namespace VyralBot.Core.Services.Linking;

/// <summary>
/// Service resolving the multi-platform watch bonus multiplier and the
/// per-viewer opt-in state for it.
/// </summary>
public interface IMultiPlatformBonusService
{
    /// <summary>
    /// Resolves the earnings multiplier for username+platform
    /// (1.0 when the feature is disabled or the viewer does not qualify).
    /// </summary>
    Task<double> GetMultiplierAsync(string username, string platform);

    /// <summary>
    /// Stores the viewer's opt-in state for the multi-platform bonus.
    /// No-op for accounts that are not linked yet.
    /// </summary>
    Task SetOptInAsync(string username, string platform, bool optedIn);

    /// <summary>
    /// Gets the viewer's opt-in state for the multi-platform bonus.
    /// </summary>
    Task<bool> GetOptInAsync(string username, string platform);
}
