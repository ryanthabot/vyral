using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

/// <summary>
/// Multi-value converter: returns Collapsed when ANY bound boolean is true,
/// Visible when all are false. Used by the Home "Get started" strip, which
/// hides once the user dismissed it or any platform connection flag is set.
/// </summary>
public class AnyTrueToCollapsedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is bool boolValue && boolValue)
            {
                return Visibility.Collapsed;
            }
        }
        return Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
