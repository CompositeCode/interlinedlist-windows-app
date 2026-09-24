using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace InterlinedList.Views;

/// <summary>
/// A small markdown → <see cref="FlowDocument"/> renderer, so #15's "rendered
/// markdown preview" is actually rendered rather than a wall of hashes and
/// asterisks.
///
/// <b>Why hand-rolled.</b> The app has two NuGet references and no markdown
/// library, and the artifact it has to display is a narrow, known subset:
/// <c>powered_document</c> returns model-written prose with ATX headings, lists
/// and light inline emphasis (verified live — see
/// <see cref="ViewModels.PoweredDocumentPanelViewModel"/>). Pulling in a
/// CommonMark implementation to render six constructs would add a dependency to
/// a csproj several other open PRs are also touching. This covers what the
/// server actually emits and degrades to plain paragraphs for anything else,
/// which is the right failure mode for a preview.
///
/// Everything it draws uses the existing Strata theme brushes looked up
/// dynamically, so it follows light/dark like the rest of the app and
/// introduces no colour of its own.
///
/// Supported: ATX headings (<c>#</c>…<c>######</c>), fenced code blocks,
/// unordered (<c>-</c>/<c>*</c>/<c>+</c>) and ordered lists, GFM pipe tables,
/// blockquotes, horizontal rules, paragraphs, and inline <c>**bold**</c>,
/// <c>*italic*</c>, <c>`code`</c> and <c>[text](url)</c>. Anything else renders
/// as literal text.
///
/// <b>Pipe tables are here because the server emits them.</b> The live
/// <c>powered_document</c> probe came back with a GFM table
/// (<c>| Date | Platform | Topic / Asset | Owner |</c>) in the middle of the
/// draft — without table support those three lines would have collapsed into
/// one run-together paragraph of pipes and dashes, which is exactly the kind of
/// mangling a preview exists to prevent. Constructs the real payload used:
/// h1, h2, bullet lists, paragraphs, <c>*italic*</c>, and that table.
/// </summary>
internal static class MarkdownRenderer
{
    private const string MonoFonts = "JetBrains Mono, Consolas";

    public static FlowDocument Render(string? markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = 12.5,
            LineHeight = 19,
            Foreground = Brush("TextBodyBrush", Colors.Black)
        };

        if (string.IsNullOrWhiteSpace(markdown))
            return document;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var index = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            document.Blocks.Add(Body(string.Join(" ", paragraph)));
            paragraph.Clear();
        }

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();

            // ── Fenced code block ───────────────────────────────────────────
            if (MarkdownSyntax.IsCodeFence(trimmed))
            {
                FlushParagraph();
                index++;
                var code = new StringBuilder();
                while (index < lines.Length && !MarkdownSyntax.IsCodeFence(lines[index].TrimStart()))
                {
                    code.AppendLine(lines[index]);
                    index++;
                }
                index++; // closing fence (or end of input)
                document.Blocks.Add(CodeBlock(code.ToString().TrimEnd()));
                continue;
            }

            // ── Blank line ──────────────────────────────────────────────────
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                index++;
                continue;
            }

            // ── Horizontal rule ─────────────────────────────────────────────
            if (MarkdownSyntax.IsHorizontalRule(trimmed))
            {
                FlushParagraph();
                document.Blocks.Add(HorizontalRule());
                index++;
                continue;
            }

            // ── Heading ─────────────────────────────────────────────────────
            if (MarkdownSyntax.TryReadHeading(trimmed, out var headingLevel, out var headingText))
            {
                FlushParagraph();
                document.Blocks.Add(Heading(headingText, headingLevel));
                index++;
                continue;
            }

            // ── GFM pipe table ──────────────────────────────────────────────
            // Needs a header row AND a |---|---| separator underneath, so a
            // paragraph that merely contains a pipe isn't mistaken for one.
            if (MarkdownSyntax.IsTableHeader(trimmed, index + 1 < lines.Length ? lines[index + 1] : null))
            {
                FlushParagraph();

                var header = MarkdownSyntax.SplitTableRow(trimmed);
                index += 2; // header + separator

                var body = new List<List<string>>();
                while (index < lines.Length)
                {
                    var rowLine = lines[index].TrimStart();
                    if (rowLine.Length == 0 || !rowLine.Contains('|')) break;
                    body.Add(MarkdownSyntax.SplitTableRow(rowLine));
                    index++;
                }

                document.Blocks.Add(TableBlock(header, body));
                continue;
            }

            // ── Blockquote ──────────────────────────────────────────────────
            if (trimmed[0] == '>')
            {
                FlushParagraph();
                var quote = new List<string>();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
                {
                    quote.Add(lines[index].TrimStart().TrimStart('>').Trim());
                    index++;
                }
                document.Blocks.Add(BlockQuote(string.Join(" ", quote)));
                continue;
            }

            // ── List ────────────────────────────────────────────────────────
            if (MarkdownSyntax.TryReadListMarker(trimmed, out var ordered, out _))
            {
                FlushParagraph();
                var list = new List
                {
                    MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(16, 4, 0, 8),
                    Padding = new Thickness(0)
                };

                while (index < lines.Length)
                {
                    var itemLine = lines[index].TrimStart();
                    if (itemLine.Length == 0) break;
                    if (!MarkdownSyntax.TryReadListMarker(itemLine, out var itemOrdered, out var content)) break;
                    if (itemOrdered != ordered) break;

                    list.ListItems.Add(new ListItem(Body(content, bottomMargin: 2)));
                    index++;
                }

                document.Blocks.Add(list);
                continue;
            }

            // ── Ordinary paragraph line ─────────────────────────────────────
            paragraph.Add(trimmed);
            index++;
        }

        FlushParagraph();
        return document;
    }

    // ── Block builders ──────────────────────────────────────────────────────────

    private static Paragraph Heading(string text, int level)
    {
        // Sizes stay on the app's existing type scale; no new tokens.
        var size = level switch { 1 => 19.0, 2 => 16.0, 3 => 14.0, 4 => 13.0, _ => 12.5 };

        var paragraph = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush", Colors.Black),
            Margin = new Thickness(0, level == 1 ? 0 : 12, 0, 6)
        };

        AppendInline(paragraph.Inlines, text);
        return paragraph;
    }

    private static Paragraph Body(string text, double bottomMargin = 8)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, bottomMargin) };
        AppendInline(paragraph.Inlines, text);
        return paragraph;
    }

    private static Section BlockQuote(string text)
    {
        var body = Body(text, bottomMargin: 0);
        body.Foreground = Brush("TextMutedBrush", Colors.Gray);

        // 3px amber edge: the brand's live/AI accent, same as the notice bar.
        return new Section(body)
        {
            BorderBrush = Brush("AmberBrush", Color.FromRgb(0xF0, 0xA8, 0x30)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 4, 0, 4),
            Margin = new Thickness(0, 4, 0, 8)
        };
    }

    private static Section CodeBlock(string code)
    {
        var paragraph = new Paragraph(new Run(code))
        {
            FontFamily = new FontFamily(MonoFonts),
            FontSize = 11,
            Margin = new Thickness(0)
        };

        return new Section(paragraph)
        {
            Background = Brush("Surface3Brush", Color.FromRgb(0xF1, 0xEA, 0xDD)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 8)
        };
    }

    /// <summary>
    /// A GFM pipe table. Columns are sized off the widest row rather than the
    /// header alone, so a header with fewer cells than a body row doesn't drop
    /// data — malformed tables are common in generated markdown.
    /// </summary>
    private static Table TableBlock(List<string> header, List<List<string>> body)
    {
        var columns = Math.Max(header.Count, body.Count == 0 ? 0 : body.Max(row => row.Count));

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 4, 0, 10),
            BorderBrush = Brush("BorderBrush", Colors.Gray),
            BorderThickness = new Thickness(1, 1, 0, 0)
        };

        for (var i = 0; i < columns; i++)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        group.Rows.Add(BuildTableRow(header, columns, isHeader: true));
        foreach (var row in body)
            group.Rows.Add(BuildTableRow(row, columns, isHeader: false));

        return table;
    }

    private static TableRow BuildTableRow(List<string> cells, int columns, bool isHeader)
    {
        var row = new TableRow();
        if (isHeader) row.Background = Brush("Surface3Brush", Color.FromRgb(0xF1, 0xEA, 0xDD));

        for (var i = 0; i < columns; i++)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0), FontSize = 11 };
            if (isHeader) paragraph.FontWeight = FontWeights.SemiBold;
            AppendInline(paragraph.Inlines, i < cells.Count ? cells[i] : "");

            row.Cells.Add(new TableCell(paragraph)
            {
                Padding = new Thickness(7, 4, 7, 4),
                BorderBrush = Brush("BorderBrush", Colors.Gray),
                BorderThickness = new Thickness(0, 0, 1, 1)
            });
        }

        return row;
    }

    private static Paragraph HorizontalRule() => new()
    {
        BorderBrush = Brush("BorderStrongBrush", Colors.Gray),
        BorderThickness = new Thickness(0, 0, 0, 1),
        Margin = new Thickness(0, 8, 0, 12),
        FontSize = 1
    };

    // ── Inline parsing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Single left-to-right pass over <c>**bold**</c>, <c>*italic*</c>,
    /// <c>`code`</c> and <c>[text](url)</c>. An unclosed marker is emitted as
    /// literal text rather than swallowing the rest of the line — a preview
    /// should never lose the user's content to a parse failure.
    /// </summary>
    private static void AppendInline(InlineCollection inlines, string text)
    {
        var literal = new StringBuilder();
        var i = 0;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            inlines.Add(new Run(literal.ToString()));
            literal.Clear();
        }

        while (i < text.Length)
        {
            // `code`
            if (text[i] == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushLiteral();
                    inlines.Add(new Run(text[(i + 1)..close])
                    {
                        FontFamily = new FontFamily(MonoFonts),
                        FontSize = 11.5
                    });
                    i = close + 1;
                    continue;
                }
            }

            // **bold**
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    FlushLiteral();
                    var bold = new Bold();
                    AppendInline(bold.Inlines, text[(i + 2)..close]);
                    inlines.Add(bold);
                    i = close + 2;
                    continue;
                }
            }

            // *italic* / _italic_
            if (text[i] is '*' or '_')
            {
                var marker = text[i];
                var close = text.IndexOf(marker, i + 1);
                if (close > i + 1)
                {
                    FlushLiteral();
                    var italic = new Italic();
                    AppendInline(italic.Inlines, text[(i + 1)..close]);
                    inlines.Add(italic);
                    i = close + 1;
                    continue;
                }
            }

            // [text](url) — styled, not navigable. A preview shouldn't be able
            // to launch a browser from model-authored text on a stray click.
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i
                    && closeBracket + 1 < text.Length
                    && text[closeBracket + 1] == '('
                    && text.IndexOf(')', closeBracket + 2) is var closeParen and > 0)
                {
                    FlushLiteral();
                    var label = text[(i + 1)..closeBracket];
                    var url = text[(closeBracket + 2)..closeParen];

                    inlines.Add(new Run(label.Length > 0 ? label : url)
                    {
                        Foreground = Brush("LinkBrush", Colors.SteelBlue),
                        ToolTip = url
                    });
                    i = closeParen + 1;
                    continue;
                }
            }

            literal.Append(text[i]);
            i++;
        }

        FlushLiteral();
    }

    /// <summary>
    /// Resolve a theme brush by resource key, with a literal fallback so the
    /// renderer still works in a designer or a unit-test context where no
    /// Application exists.
    /// </summary>
    private static Brush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
