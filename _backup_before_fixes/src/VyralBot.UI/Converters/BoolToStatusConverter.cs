using System.Globalization;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts boolean to "Enabled" or "Disabled" status text.
/// </summary>
public class BoolToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            return isEnabled ? "✅ Enabled" : "❌ Disabled";
        }
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
