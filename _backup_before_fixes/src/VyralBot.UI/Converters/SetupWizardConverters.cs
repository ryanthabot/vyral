using MaterialDesignThemes.Wpf;
using System;
using System.Globalization;
using System.Windows.Data;

namespace VyralBot.UI.Converters;

public class BoolToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string iconPair)
        {
            var icons = iconPair.Split('|');
            if (icons.Length == 2)
            {
                var iconName = boolValue ? icons[0] : icons[1];
                if (Enum.TryParse<PackIconKind>(iconName, out var kind))
                {
                    return kind;
                }
            }
        }
        return PackIconKind.HelpCircle;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string colorPair)
        {
            var colors = colorPair.Split('|');
            if (colors.Length == 2)
            {
                var colorName = boolValue ? colors[0] : colors[1];
                
                // Return the resource key name
                switch (colorName)
                {
                    case "Success":
                        return System.Windows.Application.Current.FindResource("SuccessBrush");
                    case "Warning":
                        return System.Windows.Application.Current.FindResource("WarningBrush");
                    case "Error":
                        return System.Windows.Application.Current.FindResource("ErrorBrush");
                    case "Primary":
                        return System.Windows.Application.Current.FindResource("PrimaryBrush");
                    default:
                        return System.Windows.Application.Current.FindResource("TextPrimaryBrush");
                }
            }
        }
        return System.Windows.Application.Current.FindResource("TextPrimaryBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
