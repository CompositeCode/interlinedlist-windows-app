using System.Windows.Controls;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// Blog reading (browser handoff) and newsletter subscription.
/// </summary>
/// <remarks>
/// Self-contained so it can be hosted with a one-line add — <c>SettingsView</c>
/// is being reworked across several open PRs.
/// </remarks>
public partial class BlogPanel : UserControl
{
    public BlogPanel()
    {
        InitializeComponent();
        DataContext = new BlogPanelViewModel(AppServices.Session);
    }
}
