using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using InterlinedList.Services;

namespace InterlinedList.Views;

/// <summary>
/// Renders text into a <see cref="TextBlock"/> with <c>@mentions</c> as
/// clickable links that open that profile.
/// </summary>
/// <remarks>
/// <para>
/// Usage: <c>&lt;TextBlock local:MentionText.Text="{Binding Content}"/&gt;</c>
/// instead of the plain <c>Text</c> property.
/// </para>
/// <para>
/// A mention is resolved by <c>GET /api/users/lookup?handle=</c>, which accepts
/// a <b>bare username only</b> — so the leading <c>@</c> is stripped before the
/// handle is passed on. Navigation goes through <see cref="Navigator"/>, which
/// the shell already wires to the People tab.
/// </para>
/// <para>
/// The pattern deliberately matches the username characters this API actually
/// uses (letters, digits, <c>_</c>, <c>-</c>, <c>.</c>) and requires a
/// non-word character before the <c>@</c>, so an email address in a post does
/// not turn its domain into a bogus mention.
/// </para>
/// </remarks>
public static class MentionText
{
    // (?<![\w@./]) — not preceded by a word char, @, dot or slash. Each
    // exclusion earns its place against a real false positive:
    //   foo@bar.com            -> no mention (word char before @)
    //   @@weird                -> no mention
    //   https://x.com/@notme   -> no mention (slash before @)
    // Trailing punctuation is excluded by requiring the final character to be
    // alphanumeric or underscore, so "@adron." and "@adron," link just the name.
    private static readonly Regex MentionPattern = new(
        @"(?<![\w@./])@([A-Za-z0-9][A-Za-z0-9_.\-]*[A-Za-z0-9_]|[A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(MentionText),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject element, string? value)
        => element.SetValue(TextProperty, value);

    public static string? GetText(DependencyObject element)
        => (string?)element.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;

        block.Inlines.Clear();
        var text = e.NewValue as string;
        if (string.IsNullOrEmpty(text)) return;

        var cursor = 0;
        foreach (Match match in MentionPattern.Matches(text))
        {
            if (match.Index > cursor)
                block.Inlines.Add(new Run(text[cursor..match.Index]));

            var username = match.Groups[1].Value;
            var link = new Hyperlink(new Run(match.Value))
            {
                // The bare username — the lookup endpoint rejects a leading @.
                NavigateUri = null,
                ToolTip = $"Open @{username}",
            };
            link.Click += (_, _) => Navigator.OpenProfile(username);
            block.Inlines.Add(link);

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            block.Inlines.Add(new Run(text[cursor..]));
    }
}
