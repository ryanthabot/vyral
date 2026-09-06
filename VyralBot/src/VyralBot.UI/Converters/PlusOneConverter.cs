using System.Globalization;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Displays a 0-based chat-vote option index as the 1-based number viewers
/// actually type ("!poll vote &lt;number&gt;"): OptionIndex + 1. ConvertBack
/// is deliberately not a real conversion — the votes grid is read-only.
/// </summary>
public class PlusOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int index ? index + 1 : value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}
