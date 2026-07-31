using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView()
    {
        InitializeComponent();
        var vm = new DocumentsViewModel(Services.AppServices.Session);
        DataContext = vm;
        _ = vm.LoadCommand.ExecuteAsync(null);
    }
}

/// <summary>
/// MultiBinding converter: returns Visible when the two bound values are
/// reference-equal (i.e. the row's folder is the folder currently being
/// edited / added-to), otherwise Collapsed. Used to show a folder's inline
/// rename / new-doc editors only on the row they belong to.
/// </summary>
public sealed class ReferenceEqualToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, System.Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return Visibility.Collapsed;
        return ReferenceEquals(values[0], values[1]) && values[0] is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, System.Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}
