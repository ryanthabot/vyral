using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts a value to Visibility based on equality with a parameter.
/// Returns Visible if value equals parameter, Collapsed otherwise.
/// </summary>
public class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null && parameter == null)
            return Visibility.Visible;

        if (value == null || parameter == null)
            return Visibility.Collapsed;

        return value.ToString() == parameter.ToString() 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
