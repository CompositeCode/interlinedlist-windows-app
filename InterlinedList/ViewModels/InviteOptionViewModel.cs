using CommunityToolkit.Mvvm.ComponentModel;

namespace InterlinedList.ViewModels;

/// <summary>
/// One choice in the invite panel's role or expiry picker. The app has no
/// ComboBox anywhere (native combo chrome doesn't follow the Strata themes), so
/// both pickers render as a row of small selectable buttons driven by
/// <see cref="IsSelected"/> rather than by RadioButton grouping — grouping is
/// visual-tree scoped and would misbehave with two panels alive in the
/// MainWindow view cache at once.
/// </summary>
public partial class InviteOptionViewModel : ObservableObject
{
    public required string Label { get; init; }

    /// <summary>Secondary line explaining the choice (role pickers only).</summary>
    public string? Detail { get; init; }

    /// <summary>A <see cref="Models.ShareRoles"/> wire value, for role options.</summary>
    public string? Role { get; init; }

    /// <summary>How long the invite stays valid; null means no expiry.</summary>
    public TimeSpan? Duration { get; init; }

    [ObservableProperty]
    private bool isSelected;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}
