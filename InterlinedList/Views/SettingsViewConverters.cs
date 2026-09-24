using System.Globalization;
using System.Windows.Data;

namespace InterlinedList.Views;

/// <summary>
/// Two-way binds a group of <c>RadioButton</c>s to one string property: the
/// button whose <c>ConverterParameter</c> equals the bound value is checked,
/// and checking a button writes that parameter back. Lets the enum-shaped
/// preferences (theme, viewing preference) stay plain strings on the view model
/// — which is what the API wants — without a ComboBox to re-theme.
/// </summary>
public sealed class StringEqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        // Only the button being *checked* should write; the one being unchecked
        // must stay quiet or it would immediately clobber the new selection.
        value is true && parameter is string s ? s : Binding.DoNothing;
}
