using System.Windows;
using System.Windows.Controls;
using InterlinedList.Services;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// The shared "Invite people" card. Hosts drive it declaratively — no
/// code-behind wiring needed in the hosting view:
///
/// <code>
/// &lt;local:InvitePanel Kind="List"     TargetId="{Binding SelectedList.Id}"/&gt;
/// &lt;local:InvitePanel Kind="Document" TargetId="{Binding SelectedDocument.Id}"/&gt;
/// </code>
///
/// A null/empty <see cref="TargetId"/> (nothing selected, or a list someone
/// else shared with you) collapses the whole card. Like every other view here
/// it news up its own ViewModel from <see cref="AppServices"/> rather than
/// having one injected.
/// </summary>
public partial class InvitePanel : UserControl
{
    public static readonly DependencyProperty TargetIdProperty =
        DependencyProperty.Register(
            nameof(TargetId), typeof(string), typeof(InvitePanel),
            new PropertyMetadata(null, OnTargetChanged));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind), typeof(InviteTargetKind), typeof(InvitePanel),
            new PropertyMetadata(InviteTargetKind.List, OnTargetChanged));

    private readonly InvitePanelViewModel _vm;

    public InvitePanel()
    {
        InitializeComponent();
        _vm = new InvitePanelViewModel(AppServices.Session);
        DataContext = _vm;
    }

    /// <summary>The id of the list/document whose invites this panel manages.</summary>
    public string? TargetId
    {
        get => (string?)GetValue(TargetIdProperty);
        set => SetValue(TargetIdProperty, value);
    }

    /// <summary>Which family of invite endpoints to drive.</summary>
    public InviteTargetKind Kind
    {
        get => (InviteTargetKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((InvitePanel)d).Retarget();

    private void Retarget()
    {
        var target = InviteTargets.For(Kind, TargetId, AppServices.Api);
        // Fire-and-forget: SetTargetAsync owns its own errors and stamps a
        // generation so a slower earlier load can't paint over a newer one.
        _ = _vm.SetTargetAsync(target);
    }
}
