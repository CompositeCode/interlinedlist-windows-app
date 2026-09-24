using System.Globalization;
using System.Windows.Data;

namespace InterlinedList.Views;

/// <summary>
/// Negates a bool — for binding <c>IsEnabled</c> to a busy flag.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
