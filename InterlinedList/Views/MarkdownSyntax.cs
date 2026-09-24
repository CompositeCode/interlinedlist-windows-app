namespace InterlinedList.Views;

/// <summary>
/// The line-level markdown predicates <see cref="MarkdownRenderer"/> classifies
/// with, split out from it deliberately: these are pure string functions with no
/// dependency on WPF, so they can be exercised on any platform, whereas
/// anything touching <c>FlowDocument</c> can only run on Windows. The
/// classification rules are the part most likely to get a construct wrong, so
/// they're the part worth being able to test.
/// </summary>
internal static class MarkdownSyntax
{
    /// <summary>
    /// An ATX heading: 1–6 <c>#</c> followed by a space. Returns false for
    /// <c>#hashtag</c> (no space) and for 7+ hashes, matching CommonMark.
    /// </summary>
    public static bool TryReadHeading(string line, out int level, out string text)
    {
        level = 0;
        text = "";

        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;

        if (hashes is 0 or > 6) return false;
        if (hashes >= line.Length || line[hashes] != ' ') return false;

        level = hashes;
        text = line[(hashes + 1)..].Trim();
        return true;
    }

    /// <summary>
    /// A list item marker. <paramref name="ordered"/> distinguishes
    /// <c>1.</c>/<c>1)</c> from <c>-</c>/<c>*</c>/<c>+</c> so a renderer can
    /// pick the right marker style and not merge the two kinds into one list.
    /// </summary>
    public static bool TryReadListMarker(string line, out bool ordered, out string content)
    {
        ordered = false;
        content = "";

        if (line.Length < 2) return false;

        if (line[0] is '-' or '*' or '+' && line[1] == ' ')
        {
            content = line[2..].Trim();
            return true;
        }

        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;

        if (digits > 0
            && digits + 1 < line.Length
            && line[digits] is '.' or ')'
            && line[digits + 1] == ' ')
        {
            ordered = true;
            content = line[(digits + 2)..].Trim();
            return true;
        }

        return false;
    }

    /// <summary>
    /// A thematic break: three or more of the same <c>-</c>, <c>*</c> or
    /// <c>_</c>, spaces allowed. Checked <i>before</i> the list rule, since
    /// <c>---</c> would otherwise never match and <c>* * *</c> would look like
    /// a bullet.
    /// </summary>
    public static bool IsHorizontalRule(string line)
    {
        if (line.Length < 3) return false;

        var marker = line[0];
        if (marker is not ('-' or '*' or '_')) return false;

        var count = 0;
        foreach (var c in line)
        {
            if (c == marker) count++;
            else if (c != ' ') return false;
        }

        return count >= 3;
    }

    /// <summary>
    /// The <c>|---|:---:|---:|</c> row under a GFM table header. Requires a
    /// pipe and a dash and nothing but pipes, dashes, colons and spaces — which
    /// is what tells a real table apart from a paragraph containing a dash.
    /// </summary>
    public static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.Contains('|') || !trimmed.Contains('-')) return false;

        foreach (var c in trimmed)
            if (c is not ('|' or '-' or ':' or ' ')) return false;

        return true;
    }

    /// <summary>
    /// A table row is a header candidate only when it contains a pipe and the
    /// next line is a separator. Taking both lines is what stops a paragraph
    /// with a stray <c>|</c> becoming a one-column table.
    /// </summary>
    public static bool IsTableHeader(string line, string? nextLine)
        => line.Contains('|') && nextLine is not null && IsTableSeparator(nextLine);

    /// <summary>Split <c>| a | b | c |</c> into cells, dropping the outer pipes.</summary>
    public static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];

        return trimmed.Split('|').Select(cell => cell.Trim()).ToList();
    }

    /// <summary>A fenced code block delimiter (<c>```</c> or longer).</summary>
    public static bool IsCodeFence(string line) => line.StartsWith("```", StringComparison.Ordinal);
}
