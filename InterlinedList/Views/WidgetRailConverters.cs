using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InterlinedList.Views;

/// <summary>
/// Shows an element only when a bound string has content — used for the widget
/// cards' quiet placeholder line and for optional subtitles.
/// </summary>
public sealed class WidgetStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The expand/collapse affordance on a widget card header. Text rather than an
/// icon font so it needs no asset and inherits the card's type tokens.
/// </summary>
public sealed class WidgetExpandGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "–" : "+";   // en dash when open, plus when closed

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colours a 24h market change: amber for a fall, green for a rise. Both come
/// from the Strata palette (<c>Resources/Palette.xaml</c>) — resolved by key at
/// runtime so no colour literal appears here.
/// </summary>
public sealed class WidgetChangeBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "AmberBrush" : "GreenBrush";
        return Application.Current?.TryFindResource(key)
               ?? Application.Current?.TryFindResource("TextMutedBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
