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

        return platformName switch
        {
            "Twitch" => new SolidColorBrush(Color.FromRgb(145, 70, 255)), // Twitch Purple
            "Kick" => new SolidColorBrush(Color.FromRgb(83, 255, 26)),    // Kick Green
            "YouTube" => new SolidColorBrush(Color.FromRgb(255, 0, 0)),    // YouTube Red
            "TikTok" => new SolidColorBrush(Color.FromRgb(0, 0, 0)),       // TikTok Black
            "Discord" => new SolidColorBrush(Color.FromRgb(88, 101, 242)), // Discord Blurple
            "All Platforms" => new SolidColorBrush(Color.FromRgb(38, 166, 154)), // Teal (VyralBot primary)
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
