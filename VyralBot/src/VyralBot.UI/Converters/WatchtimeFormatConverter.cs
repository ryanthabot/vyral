using System;
using System.Globalization;
using System.Windows.Data;
using VyralBot.Core.Services;

namespace VyralBot.UI.Converters;

public class WatchtimeFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int minutes)
        {
            return WatchtimeService.FormatWatchtime(minutes);
        }
        return "0 minutes";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
