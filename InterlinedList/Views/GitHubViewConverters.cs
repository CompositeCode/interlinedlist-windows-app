using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InterlinedList.Views;

/// <summary>Visible when the bound bool is <c>false</c> — for "not connected" prompts.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible <b>only</b> on an explicit <c>true</c> — a <c>null</c> stays hidden.
///
/// <para>
/// This exists because of <c>githubRepoPrivate</c>. Per /help/lists, "lists
/// created before this tag existed show no tag until their first sync", so an
/// unrecorded visibility must never be presented as a decision either way. WPF's
/// built-in <c>BooleanToVisibilityConverter</c> is typed to non-nullable
/// <c>bool</c> and turns a <c>bool?</c> of <c>null</c> into <c>Collapsed</c> only
/// by accident of unboxing; being explicit about it is the difference between a
/// deliberate third state and a lucky default.
/// </para>
/// </summary>
public sealed class TrueOnlyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible when the bound value is not null — the counterpart to
/// <see cref="InverseNullToVisibilityConverter"/>.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
