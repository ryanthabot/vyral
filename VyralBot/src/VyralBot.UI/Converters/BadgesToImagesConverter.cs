using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts a Dictionary of badge names/versions to a list of badge image URLs.
/// Twitch badges format: {"broadcaster": "1", "moderator": "1", "subscriber": "12"}
/// </summary>
public class BadgesToImagesConverter : IValueConverter
{
    public BadgesToImagesConverter()
    {
        System.Diagnostics.Debug.WriteLine("[BadgesToImagesConverter] CONSTRUCTOR CALLED");
    }
    
    // Twitch badge CDN base URL (global badges)
    private const string TwitchBadgeBaseUrl = "https://static-cdn.jtvnw.net/badges/v1/";
    
    // Kick badge CDN base URL
    private const string KickBadgeBaseUrl = "https://files.kick.com/images/badges/";
    
    // Comprehensive Twitch badge mappings (badge name -> image ID)
    // Reference: https://dev.twitch.tv/docs/irc/tags#privmsg-tags
    private static readonly Dictionary<string, string> TwitchBadgeImageIds = new()
    {
        // Core Badges
        { "broadcaster", "5527c58c-fb7d-422d-b71b-f309dcb85cc1" },
        { "moderator", "3267646d-33f0-4b17-b3df-f923a41db1d0" },
        { "vip", "b817aba4-fad8-49e2-b88a-7cc744dfa6ec" },
        { "subscriber", "5d9f2208-5dd8-11e7-8513-2ff4adfae661" },
        { "founder", "511b78a9-ab37-472f-9569-457753bbe7d3" },
        
        // Staff & Special Roles
        { "staff", "d97c37bd-a6f5-4c38-8f57-4e4bef88af34" },
        { "admin", "9ef7e029-4cdf-4d4d-a0d5-e2b3fb2583fe" },
        { "global_mod", "9384c43e-4ce7-4e94-b2a1-b93656896eba" },
        { "partner", "d12a2e27-16f6-41d0-ab77-b780518f00a3" },
        { "first-time-chatter", "a35ca5ed-ac1c-477c-8dc8-6d8fb0ab7e1c" },
        
        // Subscription Related
        { "premium", "bbbe0db0-a598-423e-86d0-f9fb98ca1933" },
        { "turbo", "bd444ec6-8f34-4bf9-91f4-af1e3428d80f" },
        { "sub-gift-leader", "21656088-7da2-4467-acd2-55220e1f45ad" },
        
        // Bits & Cheering
        { "bits", "743a0f3b-53b8-41bd-bf03-4b6e4c7dcb6e" },
        { "bits-leader", "8d8f83ae-2e62-49e7-b1ec-531098e0d502" },
        { "bits-charity", "a539dc5f-f7f6-4d4d-8e2e-d0e6d3f4e4e6" },
        
        // Hype Train
        { "hype-train", "fae4086c-3190-44d4-83c8-8ef0cbe1a515" },
        
        // Predictions & Channel Points
        { "predictions", "e33d8b46-f63b-4e67-996d-4a7dcec0ad33" },
        { "predictions-blue", "e33d8b46-f63b-4e67-996d-4a7dcec0ad33" },
        { "predictions-pink", "d1d1a29f-ad6d-46f9-9e27-9dfe32024b80" },
        
        // Moments & Clips
        { "moments", "bf370830-6d6b-4ca8-a1c4-c7e5ab76f6e3" },
        { "clip-champ", "f38976e0-ffc9-11e7-86d4-7f98b26a9d79" },
        
        // Special Events & Programs
        { "artist-badge", "4300a897-03dc-4e83-8c0e-c332fee7057f" },
        { "game-developer", "568c46ba-cd6f-4163-94a0-779a2f1e0c10" },
        { "extension", "ea8b0f8f-7e6a-4e99-8d1d-ce8b9d7f0c0c" },
        
        // Accessibility
        { "no_audio", "aef2cd08-f29b-45a1-8c12-d44d7fd5e6f0" },
        { "no_video", "199a0dba-58f3-494e-a7fc-1fa0a1001fb8" },
        
        // Overwatch League
        { "overwatch-league-insider", "a2ca920a-5d21-43d7-8532-f6a499b1d5b3" },
        
        // Power-Ups & Gifting
        { "power-up-host", "00aa4a3d-ae48-40d6-8e3e-b7f3b5f3e48e" },
        { "power-up-raid", "00aa4a3d-ae48-40d6-8e3e-b7f3b5f3e48e" },
        
        // Twitch Anniversary
        { "twitchcon2017", "0964638c-e97d-4be2-9f46-9e6d9d2f0c5c" },
        { "twitchcon2018", "e68164e4-087f-4f85-9a67-2d19c8b56d5e" },
        { "twitchconEU2019", "c6b5c54d-2a3a-4fbd-a5d7-b8a9b8b6b7b7" },
        { "twitchconNA2019", "c6b5c54d-2a3a-4fbd-a5d7-b8a9b8b6b7b7" },
        { "twitchcon2020", "e68164e4-087f-4f85-9a67-2d19c8b56d5e" },
        { "glitchcon2020", "4b971e69-2f8c-4bf5-b1fe-80e0bb2fd69d" },
        
        // Twitch Recap
        { "twitch-recap-2023", "44d85e87-151c-4d3e-ad2e-eka862b1c2e6" },
        { "twitch-recap-2024", "44d85e87-151c-4d3e-ad2e-eka862b1c2e6" },
        
        // Other Special Badges
        { "anonymous-gifter", "ca3db7f8-4a1c-40a1-88d1-8e1a67a2e5c8" },
        { "frozen-chatter", "d4a40f8e-5f3c-4b8c-8e7e-8f8e8e8e8e8e" },
        { "partner-anniversary", "d12a2e27-16f6-41d0-ab77-b780518f00a3" },
        { "prime-gaming-1", "a1f4f5f5-b8e7-4e6e-8e6e-8e6e8e6e8e6e" },
        { "glhf-pledge", "3158e758-3cb4-43c5-94b3-7639810451c5" },
        { "battlerite-1", "4fcb2d5f-7d4e-4b3e-8f8f-8f8f8f8f8f8f" }
    };
    
    // Kick badge mappings (badge type -> badge file name)
    // Note: Kick uses actual image files, not UUIDs like Twitch
    private static readonly Dictionary<string, string> KickBadgeImageFiles = new()
    {
        { "Broadcaster", "broadcaster" },
        { "broadcaster", "broadcaster" },
        { "Moderator", "moderator" },
        { "moderator", "moderator" },
        { "Subscriber", "subscriber" },
        { "subscriber", "subscriber" },
        { "Verified", "verified" },
        { "verified", "verified" },
        { "SubGifter", "sub-gifter" },
        { "sub-gifter", "sub-gifter" },
        { "OG", "og" },
        { "og", "og" }
    };

    // Thread-safe cache for loaded badge images to avoid repeated network requests and allocations
    private static readonly ConcurrentDictionary<string, BitmapImage?> _imageCache = new();
    
    // Pre-loaded local resources
    private static BitmapImage? _vyralBadge;
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Dictionary<string, string> badges || badges.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[BadgesToImagesConverter] No badges or invalid value");
            return Array.Empty<BitmapImage>();
        }

        System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] Converting {badges.Count} badges: {string.Join(", ", badges.Select(b => $"{b.Key}={b.Value}"))}");
        var badgeImages = new List<BitmapImage>();

        // Order badges by priority: broadcaster, moderator, vip, subscriber, then others
        var orderedBadges = badges
            .OrderBy(b => GetBadgePriority(b.Key))
            .ToList();

        foreach (var badge in orderedBadges)
        {
            // Special-case local badges (vyral)
            if (string.Equals(badge.Key, "vyral", StringComparison.OrdinalIgnoreCase))
            {
                var vyralImage = GetVyralBadge();
                if (vyralImage != null) badgeImages.Add(vyralImage);
                continue;
            }

            // Try Kick badges first (case-sensitive Type strings like "Broadcaster", "Moderator")
            if (KickBadgeImageFiles.TryGetValue(badge.Key, out var kickFileName))
            {
                // Kick badge URLs: https://files.kick.com/images/badges/broadcaster.png
                var url = $"{KickBadgeBaseUrl}{kickFileName}.png";
                System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] Loading Kick badge: {badge.Key} -> {url}");
                var cachedImage = GetOrLoadImage(url);
                if (cachedImage != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] ✅ Kick badge loaded: {badge.Key}");
                    badgeImages.Add(cachedImage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] ❌ Kick badge failed: {badge.Key}");
                }
                continue;
            }

            // Try Twitch badges (lowercase strings like "broadcaster", "moderator")
            if (TwitchBadgeImageIds.TryGetValue(badge.Key, out var imageId))
            {
                // Use size 1 (18x18 pixels) - options are 1, 2, or 3
                var url = $"{TwitchBadgeBaseUrl}{imageId}/1";
                System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] Loading Twitch badge: {badge.Key} -> {url}");
                var cachedImage = GetOrLoadImage(url);
                if (cachedImage != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] ✅ Twitch badge loaded: {badge.Key}");
                    badgeImages.Add(cachedImage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] ❌ Twitch badge failed: {badge.Key}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] ⚠️ Unknown badge type: {badge.Key}");
            }
        }

        System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] Returning {badgeImages.Count} badge images");
        return badgeImages;
    }
    
    /// <summary>
    /// Gets or loads a badge image from cache
    /// </summary>
    private static BitmapImage? GetOrLoadImage(string url)
    {
        return _imageCache.GetOrAdd(url, uri =>
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(uri, UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 18; // Decode at exact size needed
                image.DecodePixelHeight = 18;
                image.EndInit();
                image.Freeze(); // Make cross-thread accessible and reduces memory
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BadgesToImagesConverter] ERROR loading image from {uri}: {ex.Message}");
                return null;
            }
        });
    }
    
    /// <summary>
    /// Gets the cached vyral badge from local resources
    /// </summary>
    private static BitmapImage? GetVyralBadge()
    {
        if (_vyralBadge == null)
        {
            try
            {
                _vyralBadge = new BitmapImage();
                _vyralBadge.BeginInit();
                _vyralBadge.UriSource = new Uri("pack://application:,,,/Resources/Images/vyral-logo.png");
                _vyralBadge.CacheOption = BitmapCacheOption.OnLoad;
                _vyralBadge.DecodePixelWidth = 18;
                _vyralBadge.DecodePixelHeight = 18;
                _vyralBadge.EndInit();
                _vyralBadge.Freeze();
            }
            catch
            {
                _vyralBadge = null;
            }
        }
        return _vyralBadge;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static int GetBadgePriority(string badgeName)
    {
        return badgeName switch
        {
            // Twitch (lowercase)
            "broadcaster" => 0,
            "moderator" => 1,
            "vyral" => 2,
            "vip" => 2,
            "first-time-chatter" => 3,
            "subscriber" => 4,
            "premium" => 5,
            "turbo" => 6,
            "partner" => 7,
            "staff" => 8,
            "admin" => 9,
            "global_mod" => 10,
            "bits-leader" => 11,
            
            // Kick (PascalCase)
            "Broadcaster" => 0,
            "Moderator" => 1,
            "Verified" => 2,
            "Subscriber" => 4,
            "SubGifter" => 5,
            "OG" => 6,
            
            _ => 99
        };
    }
}
