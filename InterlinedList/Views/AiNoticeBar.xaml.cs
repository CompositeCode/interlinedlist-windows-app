using System.Windows;
using System.Windows.Controls;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// Renders one <see cref="AiNotice"/> in place. Hosts pass the notice in and
/// need nothing else:
///
/// <code>
/// &lt;local:AiNoticeBar Notice="{Binding Notice}"/&gt;
/// </code>
///
/// A null notice collapses the control, so a host never needs its own
/// visibility rule. Nothing here blocks the dispatcher — that's the whole
/// reason AI messages are a control rather than a dialog (#10).
/// </summary>
public partial class AiNoticeBar : UserControl
{
    public static readonly DependencyProperty NoticeProperty =
        DependencyProperty.Register(nameof(Notice), typeof(AiNotice), typeof(AiNoticeBar), new PropertyMetadata(null));

    public AiNoticeBar() => InitializeComponent();

    /// <summary>The message to show, or null to show nothing.</summary>
    public AiNotice? Notice
    {
        get => (AiNotice?)GetValue(NoticeProperty);
        set => SetValue(NoticeProperty, value);
    }
}
