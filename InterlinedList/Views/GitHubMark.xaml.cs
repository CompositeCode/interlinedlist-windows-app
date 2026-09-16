using System.Windows.Controls;

namespace InterlinedList.Views;

/// <summary>
/// The GitHub mark as a reusable vector, inheriting <c>Foreground</c> from its
/// host so every use stays on the Strata palette. Set <c>Width</c>/<c>Height</c>
/// to size it.
/// </summary>
public partial class GitHubMark : UserControl
{
    public GitHubMark()
    {
        InitializeComponent();
    }
}
