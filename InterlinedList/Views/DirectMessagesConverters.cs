using System.Globalization;
using System.Windows;
using System.Windows.Data;
using InterlinedList.Models;

namespace InterlinedList.Views;

/// <summary>
/// True when the bound <see cref="DmFolder"/> equals the folder named by
/// ConverterParameter — lets the three Inbox/Sent/Deleted tabs share one Style
/// and still light up individually.
/// </summary>
public sealed class DmFolderEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DmFolder folder
        && parameter is string name
        && Enum.TryParse<DmFolder>(name, ignoreCase: true, out var other)
        && folder == other;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound boolean is false (the inverse of BooleanToVisibilityConverter).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
