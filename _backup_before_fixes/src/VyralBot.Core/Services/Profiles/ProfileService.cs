using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VyralBot.Core.Models;
using VyralBot.Core.Services.EventBus;

namespace VyralBot.Core.Services.Profiles;

/// <summary>
/// Manages user profiles with isolated settings, credentials, and data.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly ILogger<ProfileService> _logger;
    private readonly IEventBus? _eventBus;
    private readonly string _profilesRootPath;
    private readonly string _currentProfilePath;
    private Profile? _currentProfile;

    public ProfileService(ILogger<ProfileService> logger, IEventBus? eventBus = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus;
        
        // Portable: Store in appdata folder at VyralBot root (not in build output)
        // This allows users to easily see/manage their data and makes the app portable
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        
        // Navigate up from: src/VyralBot.UI/bin/Debug/net9.0-windows/ to VyralBot root
        // Go up 6 levels: net9.0-windows -> Debug -> bin -> VyralBot.UI -> src -> VyralBot (root)
        var projectRoot = Directory.GetParent(baseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
        
        // Fallback to base directory if we can't find project root (e.g., published release)
        var rootDirectory = projectRoot ?? baseDirectory;
        var portableDataPath = Path.Combine(rootDirectory, "appdata");
        
        _profilesRootPath = Path.Combine(portableDataPath, "profiles");
        _currentProfilePath = Path.Combine(portableDataPath, "current_profile.txt");
        
        // Ensure profiles directory exists
        Directory.CreateDirectory(_profilesRootPath);
        
        _logger.LogInformation("ProfileService using portable storage: {Path}", portableDataPath);
    }

    /// <summary>
    /// Gets the current profile name synchronously for DI initialization.
    /// Returns null if no profile is set (first run).
    /// </summary>
    public string? GetCurrentProfileName()
    {
        if (_currentProfile != null)
            return _currentProfile.Name;
            
        // Read current profile name from file
        if (File.Exists(_currentProfilePath))
        {
            var profileName = File.ReadAllText(_currentProfilePath).Trim();
            _logger.LogInformation("Current profile from file: {ProfileName}", profileName);
            return profileName;
        }
        
        // First launch - no profile exists
        _logger.LogInformation("No current profile found - first run detected");
        return null;
    }
    
    public async Task<Profile> GetCurrentProfileAsync()
    {
        if (_currentProfile != null)
            return _currentProfile;

        var profileName = GetCurrentProfileName();
        
        // If no profile name exists (first run), return null
        if (string.IsNullOrEmpty(profileName))
        {
            _logger.LogWarning("No current profile - first run detected");
            return null!;
        }

        // Load profile metadata
        var profilePath = GetProfilePath(profileName);
        var profileJsonPath = Path.Combine(profilePath, "profile.json");

        if (File.Exists(profileJsonPath))
        {
            var json = await File.ReadAllTextAsync(profileJsonPath);
            _currentProfile = JsonSerializer.Deserialize<Profile>(json) ?? throw new InvalidOperationException("Failed to deserialize profile");
        }
        else
        {
            // Create new profile
            _currentProfile = new Profile
            {
                Name = profileName,
                DisplayName = profileName,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };

            // Ensure profile directory exists
            Directory.CreateDirectory(profilePath);
            
            // Save profile metadata
            await SaveProfileAsync(_currentProfile);
            
            _logger.LogInformation("Created new profile: {ProfileName}", profileName);
        }

        // Update last used time
        _currentProfile.LastUsedAt = DateTime.UtcNow;
        await SaveProfileAsync(_currentProfile);

        return _currentProfile;
    }

    public async Task<IEnumerable<Profile>> GetProfilesAsync()
    {
        var profiles = new List<Profile>();
        
        if (!Directory.Exists(_profilesRootPath))
            return profiles;

        var profileDirs = Directory.GetDirectories(_profilesRootPath);
        
        foreach (var dir in profileDirs)
        {
            var profileJsonPath = Path.Combine(dir, "profile.json");
            
            if (File.Exists(profileJsonPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(profileJsonPath);
                    var profile = JsonSerializer.Deserialize<Profile>(json);
                    if (profile != null)
                        profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load profile from {Path}", dir);
                }
            }
        }

        return profiles.OrderByDescending(p => p.LastUsedAt);
    }

    public async Task<Profile> CreateProfileAsync(string name, string? displayName = null, string? pinCode = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile name cannot be empty", nameof(name));

        // Validate name (alphanumeric, underscore, hyphen only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_-]+$"))
            throw new ArgumentException("Profile name can only contain letters, numbers, underscores, and hyphens", nameof(name));

        var profilePath = GetProfilePath(name);
        
        if (Directory.Exists(profilePath))
            throw new InvalidOperationException($"Profile '{name}' already exists");

        var profile = new Profile
        {
            Name = name,
            DisplayName = displayName ?? name,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            PinCode = string.IsNullOrEmpty(pinCode) ? null : HashPin(pinCode)
        };

        Directory.CreateDirectory(profilePath);
        await SaveProfileAsync(profile);

        _logger.LogInformation("Created profile: {ProfileName}", name);
        
        // If this is the first profile (no current profile set), automatically set it as current
        var profiles = await GetProfilesAsync();
        if (profiles.Count() == 1 || !File.Exists(_currentProfilePath))
        {
            await File.WriteAllTextAsync(_currentProfilePath, name);
            _currentProfile = null; // Clear cache
            _logger.LogInformation("Set {ProfileName} as current profile (first profile or no current profile)", name);
        }
        
        return profile;
    }

    public async Task SwitchProfileAsync(string name, string? pinCode = null)
    {
        var oldProfileName = GetCurrentProfileName() ?? "none";
        
        var profilePath = GetProfilePath(name);
        
        if (!Directory.Exists(profilePath))
            throw new InvalidOperationException($"Profile '{name}' does not exist");

        // Validate PIN if required
        if (!await ValidatePinAsync(name, pinCode ?? ""))
            throw new UnauthorizedAccessException("Invalid PIN code");

        // Write new current profile
        await File.WriteAllTextAsync(_currentProfilePath, name);
        
        _logger.LogInformation("Switched from profile '{OldProfile}' to '{NewProfile}'", oldProfileName, name);
        
        // Clear cached current profile to force reload
        _currentProfile = null;
        
        // Publish profile switched event
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new ProfileSwitchedEvent
            {
                Source = "System",
                OldProfileName = oldProfileName,
                NewProfileName = name
            });
        }
    }

    public async Task DeleteProfileAsync(string name)
    {
        var profilePath = GetProfilePath(name);
        
        if (Directory.Exists(profilePath))
        {
            // Force garbage collection to release any file handles
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // Small delay to ensure file handles are released
            await Task.Delay(100);
            
            try
            {
                Directory.Delete(profilePath, recursive: true);
                _logger.LogInformation("Deleted profile: {ProfileName}", name);
                
                // Check if this was the current profile
                var currentProfileName = GetCurrentProfileName();
                if (currentProfileName == name)
                {
                    // Clear current profile reference
                    if (File.Exists(_currentProfilePath))
                    {
                        File.Delete(_currentProfilePath);
                        _logger.LogInformation("Cleared current_profile.txt after deleting current profile");
                    }
                    _currentProfile = null;
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to delete profile {ProfileName}. Files may be in use.", name);
                throw new InvalidOperationException($"Cannot delete profile '{name}'. Please ensure no other processes are using the profile files.", ex);
            }
        }
    }

    public string GetProfileDataPath(string profileName)
    {
        return Path.Combine(GetProfilePath(profileName), "data");
    }

    public string GetProfileCredentialsPath(string profileName)
    {
        return GetProfilePath(profileName);
    }

    public async Task<bool> ValidatePinAsync(string profileName, string pinCode)
    {
        var profilePath = GetProfilePath(profileName);
        var profileJsonPath = Path.Combine(profilePath, "profile.json");
        
        _logger.LogInformation("Validating PIN for profile: {ProfileName}", profileName);
        
        if (!File.Exists(profileJsonPath))
        {
            _logger.LogInformation("No profile.json found - no PIN required");
            return true; // No profile metadata = no PIN
        }

        var json = await File.ReadAllTextAsync(profileJsonPath);
        var profile = JsonSerializer.Deserialize<Profile>(json);
        
        if (profile?.PinCode == null)
        {
            _logger.LogInformation("Profile has no PIN set - validation passed");
            return true; // No PIN set
        }

        var hashedInput = HashPin(pinCode);
        var isValid = profile.PinCode == hashedInput;
        
        _logger.LogInformation("PIN validation result: {IsValid} (Stored hash length: {StoredLength}, Input hash length: {InputLength})", 
            isValid, profile.PinCode?.Length ?? 0, hashedInput?.Length ?? 0);

        return isValid;
    }

    public async Task UpdateProfilePinAsync(string profileName, string? currentPin, string? newPin)
    {
        var profilePath = GetProfilePath(profileName);
        var profileJsonPath = Path.Combine(profilePath, "profile.json");
        
        if (!File.Exists(profileJsonPath))
            throw new InvalidOperationException($"Profile '{profileName}' does not exist");

        var json = await File.ReadAllTextAsync(profileJsonPath);
        var profile = JsonSerializer.Deserialize<Profile>(json) ?? throw new InvalidOperationException("Failed to deserialize profile");
        
        // Validate current PIN if one exists
        if (!string.IsNullOrEmpty(profile.PinCode))
        {
            var isValid = await ValidatePinAsync(profileName, currentPin ?? "");
            if (!isValid)
                throw new UnauthorizedAccessException("Current PIN is incorrect");
        }
        
        // Update PIN
        profile.PinCode = string.IsNullOrEmpty(newPin) ? null : HashPin(newPin);
        
        // Save updated profile
        await SaveProfileAsync(profile);
        
        _logger.LogInformation("Updated PIN for profile: {ProfileName}", profileName);
    }

    public async Task UpdateProfileAsync(Profile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        await SaveProfileAsync(profile);
        
        _logger.LogInformation("Updated profile: {ProfileName}", profile.Name);
    }

    public Task<string> ExportProfileAsync(string profileName, string destinationPath)
    {
        var profilePath = GetProfilePath(profileName);

        if (!Directory.Exists(profilePath))
            throw new InvalidOperationException($"Profile '{profileName}' does not exist");

        var zipPath = Path.Combine(destinationPath, $"{profileName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        ZipFile.CreateFromDirectory(profilePath, zipPath);

        _logger.LogInformation("Exported profile {ProfileName} to {ZipPath}", profileName, zipPath);

        return Task.FromResult(zipPath);
    }

    public async Task<Profile> ImportProfileAsync(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Profile ZIP file not found", zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        
        // Find profile.json in the ZIP
        var profileEntry = archive.Entries.FirstOrDefault(e => e.Name == "profile.json");
        if (profileEntry == null)
            throw new InvalidOperationException("Invalid profile ZIP - missing profile.json");

        // Read profile metadata
        using var stream = profileEntry.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var profile = JsonSerializer.Deserialize<Profile>(json) ?? throw new InvalidOperationException("Failed to deserialize profile");

        // Check if profile already exists
        var profilePath = GetProfilePath(profile.Name);
        if (Directory.Exists(profilePath))
            throw new InvalidOperationException($"Profile '{profile.Name}' already exists");

        // Extract ZIP to profile directory
        ZipFile.ExtractToDirectory(zipPath, profilePath);
        
        _logger.LogInformation("Imported profile: {ProfileName}", profile.Name);
        
        return profile;
    }

    private string GetProfilePath(string profileName)
    {
        return Path.Combine(_profilesRootPath, profileName);
    }

    private async Task SaveProfileAsync(Profile profile)
    {
        var profilePath = GetProfilePath(profile.Name);
        var profileJsonPath = Path.Combine(profilePath, "profile.json");
        
        Directory.CreateDirectory(profilePath);
        
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(profileJsonPath, json);
    }

    private string HashPin(string pin)
    {
        if (string.IsNullOrEmpty(pin))
            return "";

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(pin);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}
