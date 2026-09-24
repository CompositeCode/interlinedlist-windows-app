using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InterlinedList.Views;

// The home for converters more than one view needs.
//
// Read this before adding one elsewhere. During the parity merge pass the same
// converter was independently added in two files FIVE times
// (InverseBoolToVisibilityConverter twice, NotNullToVisibilityConverter twice,
// plus others). Every time, git reported the PRs MERGEABLE — different files, no
// textual overlap — and only the compiler objected, with CS0101/CS0111 on a
// green-in-isolation branch. These are namespace-visible with no `using`, so a
// per-view copy is never necessary.

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

/// <summary>
/// Collapsed when the bound bool is true — the inverse of the framework's
/// <c>BooleanToVisibilityConverter</c>.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound value is non-null.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
