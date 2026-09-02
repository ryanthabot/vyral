using System.Globalization;
using System.Windows.Data;
using VyralBot.Data.Entities;

namespace VyralBot.UI.Converters;

/// <summary>
/// Converts Command object to edit title (Add or Edit).
/// </summary>
public class CommandEditTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Command)
        {
            return "✏️ Edit Command";
        }
        return "➕ Add New Command";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
