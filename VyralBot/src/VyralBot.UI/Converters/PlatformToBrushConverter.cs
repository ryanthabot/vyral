using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts platform names to their brand color brushes
/// </summary>
public class PlatformToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string platformName)
            return null;

        // Facebook's brand background is white, which needs a colored button
        // Foreground: bind the send button's Foreground to this converter with
        // ConverterParameter="foreground" to get the readable text color
        // (dark-blue for Facebook, white for every other platform).
        if (string.Equals(parameter as string, "foreground", StringComparison.OrdinalIgnoreCase))
        {
            return platformName switch
            {
                "Facebook" => new SolidColorBrush(Color.FromRgb(0x18, 0x77, 0xF2)), // Facebook Blue (text on white)
                _ => Brushes.White
            };
        }

        return platformName switch
        {
            "Twitch" => new SolidColorBrush(Color.FromRgb(145, 70, 255)), // Twitch Purple
            "Kick" => new SolidColorBrush(Color.FromRgb(83, 255, 26)),    // Kick Green
            "YouTube" => new SolidColorBrush(Color.FromRgb(255, 0, 0)),    // YouTube Red
            "TikTok" => new SolidColorBrush(Color.FromRgb(0, 0, 0)),       // TikTok Black
            "Discord" => new SolidColorBrush(Color.FromRgb(88, 101, 242)), // Discord Blurple
            "Facebook" => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), // Facebook White (needs dark-blue text — bind Foreground with ConverterParameter="foreground")
            "Vyral" => new SolidColorBrush(Color.FromRgb(0x14, 0xB8, 0xA6)), // Vyral Teal
            "All Platforms" => new SolidColorBrush(Color.FromRgb(38, 166, 154)), // Teal (VyralBot primary)
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
