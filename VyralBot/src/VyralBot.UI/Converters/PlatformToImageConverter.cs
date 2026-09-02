using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts platform names to cached BitmapImage instances to prevent memory leaks
/// from repeated image loading when switching platforms
/// </summary>
public class PlatformToImageConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();
    private static readonly object _lock = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string platformName)
            return null;

        lock (_lock)
        {
            if (_imageCache.TryGetValue(platformName, out var cachedImage))
                return cachedImage;

            var imagePath = platformName switch
            {
                "Twitch" => "pack://application:,,,/Resources/Images/twitch-logo.png",
                "Kick" => "pack://application:,,,/Resources/Images/kick-logo.png",
                "YouTube" => "pack://application:,,,/Resources/Images/youtube-logo.png",
                "TikTok" => "pack://application:,,,/Resources/Images/tiktok-logo.png",
                "Discord" => "pack://application:,,,/Resources/Images/discord-logo.png",
                "All Platforms" => "pack://application:,,,/Resources/Images/vyral-logo.png",
                _ => null
            };

            if (imagePath == null)
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load into memory immediately
            bitmap.EndInit();
            bitmap.Freeze(); // Make immutable for thread-safety and performance

            _imageCache[platformName] = bitmap;
            return bitmap;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
