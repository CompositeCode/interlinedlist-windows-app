using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using InterlinedList.ViewModels;

namespace InterlinedList.Views;

/// <summary>
/// Visible when the bound object is non-null. The AI panels use one nullable
/// <see cref="AiNotice"/> / artifact slot each, so "is something there" is the
/// visibility question over and over.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// InverseBoolToVisibilityConverter is NOT declared here. It already exists in
// ListsViewConverters.cs, in this same InterlinedList.Views namespace, with a
// byte-identical implementation — declaring it twice is CS0101/CS0111.
//
// That is exactly what happened: #161 (lists schema UI) and #173 (AI gating)
// were developed in parallel and each added one. Git reported both MERGEABLE
// because they touched different files; only the compiler objected. The AI
// views reference the existing one, which is namespace-visible without a using.
//
// If you need a converter here, check ListsViewConverters.cs and
// CommonConverters.cs first.

/// <summary>
/// An <see cref="AiNoticeKind"/> to its Strata colour. Amber <c>#F0A830</c> — the
/// brand's live/pending accent — covers the three "come back and try again"
/// states, because they are states, not errors: the request was well-formed and
/// the app is working correctly. Only a genuine failure gets the error red, and
/// input problems get plain body text since they're just instructions.
///
/// Returns a resource key rather than a literal so both themes' brushes apply;
/// the two fixed hexes are the pair already used for error/amber text
/// throughout the app (see the ErrorBanner styles in Lists/Documents).
/// </summary>
public sealed class AiNoticeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xE8, 0x11, 0x23));
    private static readonly SolidColorBrush AmberBrush = new(Color.FromRgb(0xF0, 0xA8, 0x30));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AiNoticeKind.QuotaExceeded or AiNoticeKind.RateLimited or AiNoticeKind.ModelDeclined => AmberBrush,
        AiNoticeKind.Error => ErrorBrush,
        // Input and Info read as guidance; let them inherit the body colour via
        // the app's muted text brush, resolved by the caller's DynamicResource.
        _ => Application.Current?.TryFindResource("TextBodyBrush") as Brush ?? (Brush)AmberBrush
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// An <see cref="AiNoticeKind"/> to the short label that prefixes the message,
/// so <c>quota_exceeded</c> and <c>rate_limited</c> are told apart at a glance
/// rather than reading as the same red failure (#10).
/// </summary>
public sealed class AiNoticeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AiNoticeKind.QuotaExceeded => "DAILY LIMIT",
        AiNoticeKind.RateLimited => "SLOW DOWN",
        AiNoticeKind.ModelDeclined => "TRY DIFFERENT WORDING",
        AiNoticeKind.Input => "CHECK THIS",
        AiNoticeKind.Error => "FAILED",
        _ => "AI"
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
