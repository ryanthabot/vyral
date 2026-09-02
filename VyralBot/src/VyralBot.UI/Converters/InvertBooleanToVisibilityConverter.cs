using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts a boolean value to Visibility, inverting the value.
/// True -> Collapsed, False -> Visible
/// </summary>
public class InvertBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}
