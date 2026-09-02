using VyralBot.Core.Models;

namespace VyralBot.Core.Services.Profiles;

/// <summary>
/// Service for managing user profiles with isolated settings and data.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Gets the current profile name synchronously (for DI initialization).
    /// Returns null if no profile is set (first run).
    /// </summary>
    string? GetCurrentProfileName();
    
    /// <summary>
    /// Gets the currently active profile.
    /// </summary>
    Task<Profile> GetCurrentProfileAsync();
    
    /// <summary>
    /// Gets all available profiles.
    /// </summary>
    Task<IEnumerable<Profile>> GetProfilesAsync();
    
    /// <summary>
    /// Creates a new profile.
    /// </summary>
    Task<Profile> CreateProfileAsync(string name, string? displayName = null, string? pinCode = null);
    
    /// <summary>
    /// Switches to a different profile (requires app restart).
    /// </summary>
    Task SwitchProfileAsync(string name, string? pinCode = null);
    
    /// <summary>
    /// Deletes a profile and all its data.
    /// </summary>
    Task DeleteProfileAsync(string name);
    
    /// <summary>
    /// Gets the data directory path for a specific profile.
    /// </summary>
    string GetProfileDataPath(string profileName);
    
    /// <summary>
    /// Gets the credentials directory path for a specific profile.
    /// </summary>
    string GetProfileCredentialsPath(string profileName);
    
    /// <summary>
    /// Validates a PIN code for a profile.
    /// </summary>
    Task<bool> ValidatePinAsync(string profileName, string pinCode);
    
    /// <summary>
    /// Updates the PIN for a profile.
    /// </summary>
    Task UpdateProfilePinAsync(string profileName, string? currentPin, string? newPin);
    
    /// <summary>
    /// Updates an existing profile's metadata (settings, display name, etc).
    /// </summary>
    Task UpdateProfileAsync(Profile profile);
    
    /// <summary>
    /// Exports a profile to a ZIP file.
    /// </summary>
    Task<string> ExportProfileAsync(string profileName, string destinationPath);
    
    /// <summary>
    /// Imports a profile from a ZIP file.
    /// </summary>
    Task<Profile> ImportProfileAsync(string zipPath);
}
