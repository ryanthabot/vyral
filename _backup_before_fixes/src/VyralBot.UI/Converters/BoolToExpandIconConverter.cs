using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts a boolean to ChevronDown (false) or ChevronUp (true) icon.
/// </summary>
public class BoolToExpandIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? PackIconKind.ChevronDown : PackIconKind.ChevronRight;
        }
        return PackIconKind.ChevronRight;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
