using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace InterlinedList.Views;

/// <summary>
/// A read-only rendered view of a markdown string. Bind <see cref="Markdown"/>
/// and it re-renders on every change:
///
/// <code>
/// &lt;local:MarkdownViewer Markdown="{Binding DraftMarkdown}" MaxHeight="360"/&gt;
/// </code>
///
/// Code-only (no XAML partner file) because it's a
/// <see cref="FlowDocumentScrollViewer"/> with one dependency property and a
/// re-render callback — a markup file would only add indirection.
///
/// <see cref="FlowDocumentScrollViewer.IsToolBarVisible"/> stays off: the stock
/// toolbar brings page/zoom controls styled nothing like Strata.
/// </summary>
public sealed class MarkdownViewer : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownViewer),
            new PropertyMetadata(null, OnMarkdownChanged));

    public MarkdownViewer()
    {
        IsToolBarVisible = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Padding = new Thickness(0);
        Document = MarkdownRenderer.Render(null);
    }

    /// <summary>The markdown source to render.</summary>
    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownViewer)d).Document = MarkdownRenderer.Render(e.NewValue as string);
}
