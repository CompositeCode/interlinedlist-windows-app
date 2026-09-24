using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>The control shape a column's editor uses; drives template selection in XAML.</summary>
public static class ListRowFieldKind
{
    public const string Text = "Text";
    public const string LongText = "LongText";
    public const string Number = "Number";
    public const string Boolean = "Boolean";
    public const string Date = "Date";
    public const string DateTime = "DateTime";
    public const string Select = "Select";
    public const string MultiSelect = "MultiSelect";

    public static string For(string? type) => type switch
    {
        ListFieldType.TextArea => LongText,
        ListFieldType.Number => Number,
        ListFieldType.Boolean => Boolean,
        ListFieldType.Date => Date,
        ListFieldType.DateTime => DateTime,
        ListFieldType.Select or ListFieldType.Priority => Select,
        ListFieldType.MultiSelect => MultiSelect,
        // text / email / url / tel all edit as one line; their validation differs.
        _ => Text
    };
}

/// <summary>One checkbox in a multiselect.</summary>
public partial class ListRowOptionViewModel : ObservableObject
{
    public required string Value { get; init; }

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>
/// One column's control in the typed row form. Holds the half-typed editor state
/// a wire type can't (a number mid-keystroke, a date with no time yet) and turns
/// it back into the exact JSON the row endpoints accept.
///
/// Two behaviours here are load-bearing, both live-verified 2026-09-16:
/// <list type="bullet">
///   <item><b>Untouched values are echoed verbatim.</b> A row write REPLACES
///   <c>rowData</c> wholesale, so every key has to be re-sent; re-sending the
///   stored <see cref="JsonElement"/> rather than a re-rendered one keeps values
///   the editor can't fully model (a naive datetime, a number stored as a string,
///   a nested object) exactly as they were.</item>
///   <item><b>Types are enforced on write.</b> <c>"true"</c> in a boolean column
///   is <c>422 Done must be true or false</c>, a multiselect must be a real JSON
///   array, and a date must be <c>YYYY-MM-DD</c> — so this emits real booleans,
///   real arrays and ISO strings, never their text renderings. (A number is the
///   exception the server is loose about — it accepts <c>"7"</c> and then skips
///   its own min/max check — which is exactly why this sends a number.)</item>
/// </list>
/// </summary>
public partial class ListRowFieldViewModel : ObservableObject
{
    public const string BoolTrue = "True";
    public const string BoolFalse = "False";
    public const string BoolNeither = "Neither";

    private static readonly Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private bool _loading;
    private JsonElement? _original;

    public ListField Field { get; }

    public string Key => Field.Key;
    public string Label => Field.Label;
    public string Kind { get; }
    public bool IsRequired => Field.IsRequired;
    public string? HelpText => Field.HelpText;
    public string Placeholder => Field.Placeholder ?? string.Empty;
    public bool IsHiddenColumn => !Field.IsVisible;
    public string TypeLabel => Field.TypeDisplayName;

    /// <summary>Options for a single-select, with a blank first entry when the column is optional.</summary>
    public IReadOnlyList<string> SelectOptions { get; }

    /// <summary>Checkboxes for a multiselect.</summary>
    public ObservableCollection<ListRowOptionViewModel> MultiOptions { get; } = new();

    /// <summary>
    /// True/False, plus <b>Neither</b> (a stored <c>null</c>) when the column is
    /// optional — the tri-state #21 calls for.
    /// </summary>
    public IReadOnlyList<string> BoolOptions { get; }

    /// <summary>The <c>validation.step</c> hint; the server never enforces it.</summary>
    public string? StepHint => Field.Validation?.Step is { } step
        ? $"steps of {step.ToString(CultureInfo.InvariantCulture)}"
        : null;

    [ObservableProperty]
    private string textValue = "";

    [ObservableProperty]
    private string boolSelection = BoolNeither;

    [ObservableProperty]
    private string? selectedOption;

    [ObservableProperty]
    private DateTime? dateValue;

    /// <summary>HH:mm (or HH:mm:ss) half of a datetime column.</summary>
    [ObservableProperty]
    private string timeValue = "";

    /// <summary>Message from a 422 detail, or from the local pre-flight check.</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>False while a conditional-visibility rule says to hide this column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool matchesCondition = true;

    /// <summary>Set by the editor's "show hidden columns" toggle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool showHiddenColumns;

    public bool IsShown => MatchesCondition && (!IsHiddenColumn || ShowHiddenColumns);

    /// <summary>True once the user has changed this control (see the verbatim-echo note).</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// True when the row being edited actually had a value under this key — so a
    /// blank optional column on a NEW row can be left out of the write entirely
    /// instead of padding rowData with nulls, while clearing a value that WAS
    /// there still sends an explicit null to erase it.
    /// </summary>
    public bool HasStoredValue => _original is not null;

    public ListRowFieldViewModel(ListField field)
    {
        Field = field;
        Kind = ListRowFieldKind.For(field.Type);

        BoolOptions = field.IsRequired
            ? [BoolTrue, BoolFalse]
            : [BoolTrue, BoolFalse, BoolNeither];

        var options = field.EffectiveOptions;
        SelectOptions = field.IsRequired ? options : ["", .. options];

        foreach (var option in options)
        {
            var item = new ListRowOptionViewModel { Value = option };
            item.PropertyChanged += OnOptionChanged;
            MultiOptions.Add(item);
        }
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        IsDirty = true;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised whenever the held value changes, so the editor can re-run visibility.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Fill the controls for a NEW row: from <c>defaultValue</c>, which is why
    /// the DSL's decoded default matters (a number column pre-fills with 3, not "3").
    /// </summary>
    public void LoadDefault()
    {
        _original = null;
        Load(DefaultAsText(), Field.DefaultValue);
        IsDirty = false;
    }

    /// <summary>Fill the controls from a stored row value (or blank when the row has no such key).</summary>
    public void LoadStored(JsonElement? stored)
    {
        _original = stored;
        Load(stored is { } element ? ListFieldConditionEvaluator.AsText(element) : string.Empty, stored);
        IsDirty = false;
    }

    private void Load(string text, object? raw)
    {
        _loading = true;
        try
        {
            ErrorMessage = null;
            TextValue = text;

            switch (Kind)
            {
                case ListRowFieldKind.Boolean:
                    BoolSelection = text switch
                    {
                        "true" => BoolTrue,
                        "false" => BoolFalse,
                        _ => BoolOptions.Contains(BoolNeither) ? BoolNeither : BoolFalse
                    };
                    break;

                case ListRowFieldKind.Select:
                    SelectedOption = SelectOptions.Contains(text, StringComparer.Ordinal)
                        ? text
                        : text.Length > 0 ? text : SelectOptions.FirstOrDefault();
                    break;

                case ListRowFieldKind.MultiSelect:
                    var selected = SelectedFrom(raw, text);
                    foreach (var option in MultiOptions)
                        option.IsSelected = selected.Contains(option.Value, StringComparer.Ordinal);
                    break;

                case ListRowFieldKind.Date:
                    DateValue = System.DateTime.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date) ? date.Date : null;
                    break;

                case ListRowFieldKind.DateTime:
                    if (System.DateTime.TryParse(text, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var stamp))
                    {
                        DateValue = stamp.Date;
                        TimeValue = stamp.ToString("HH:mm", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        DateValue = null;
                        TimeValue = "";
                    }
                    break;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private static List<string> SelectedFrom(object? raw, string text)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.Array } array)
            return array.EnumerateArray().Select(e => ListFieldConditionEvaluator.AsText(e)).ToList();

        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private string DefaultAsText() => ListFieldDefaults.FromDecoded(Field.DefaultValue);

    /// <summary>The value as the condition evaluator should see it right now.</summary>
    public object? CurrentValue()
    {
        if (!TryBuildValue(out var value, out _)) return null;
        return value;
    }

    /// <summary>
    /// Turn the controls back into the JSON the row endpoints want. Returns false
    /// with a message for anything catchable client-side (a required blank, an
    /// unparseable number, a malformed email/url, a time that isn't a time,
    /// a validation rule the column declares) so the round trip is skipped.
    /// </summary>
    public bool TryBuildValue(out object? value, out string? error)
    {
        error = null;

        // Untouched: echo exactly what the server gave us.
        if (!IsDirty && _original is { } original)
        {
            value = original;
            return true;
        }

        var text = TextValue?.Trim() ?? string.Empty;

        switch (Kind)
        {
            case ListRowFieldKind.Boolean:
                value = BoolSelection switch
                {
                    BoolTrue => true,
                    BoolFalse => false,
                    _ => null
                };
                break;

            case ListRowFieldKind.Number:
                if (text.Length == 0) { value = null; break; }
                if (!ListFieldDefaults.TryParseNumber(text, out var number))
                {
                    error = $"{Label} must be a valid number.";
                    value = null;
                    return false;
                }
                value = number;
                break;

            case ListRowFieldKind.Date:
                value = DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;

            case ListRowFieldKind.DateTime:
                if (DateValue is not { } day) { value = null; break; }
                if (!TryParseTime(TimeValue, out var time))
                {
                    error = $"{Label} needs a time as HH:MM.";
                    value = null;
                    return false;
                }
                value = day.Date.Add(time).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                break;

            case ListRowFieldKind.Select:
                value = string.IsNullOrWhiteSpace(SelectedOption) ? null : SelectedOption;
                break;

            case ListRowFieldKind.MultiSelect:
                // Must be a real array — a comma string answers 422 "must be an array".
                value = MultiOptions.Where(o => o.IsSelected).Select(o => o.Value).ToList();
                break;

            default:
                value = text.Length == 0 ? null : TextValue;
                break;
        }

        if (!ValidateValue(value, ref error))
            return false;

        return true;
    }

    private bool ValidateValue(object? value, ref string? error)
    {
        var isEmpty = ListFieldConditionEvaluator.IsEmpty(value);

        if (IsRequired && isEmpty)
        {
            error = $"{Label} is required.";
            return false;
        }

        if (isEmpty) return true;

        var text = ListFieldConditionEvaluator.AsText(value);

        switch (Field.Type)
        {
            case ListFieldType.Email when !EmailPattern.IsMatch(text):
                error = $"{Label} must be a valid email address.";
                return false;

            case ListFieldType.Url when !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps):
                error = $"{Label} must be a valid URL (http or https).";
                return false;
        }

        // The column's own rules. The server enforces these too (422), but
        // catching them here keeps the message next to the control.
        if (Field.Validation is { } rules)
        {
            if (value is double number)
            {
                if (rules.Min is { } min && ListFieldDefaults.TryParseNumber(
                        ListFieldConditionEvaluator.AsText(min), out var minimum) && number < minimum)
                {
                    error = $"{Label} must be at least {ListFieldConditionEvaluator.AsText(min)}.";
                    return false;
                }
                if (rules.Max is { } max && ListFieldDefaults.TryParseNumber(
                        ListFieldConditionEvaluator.AsText(max), out var maximum) && number > maximum)
                {
                    error = $"{Label} must be at most {ListFieldConditionEvaluator.AsText(max)}.";
                    return false;
                }
            }

            if (value is string s)
            {
                if (rules.MinLength is { } minLength && s.Length < minLength)
                {
                    error = $"{Label} must be at least {minLength} characters.";
                    return false;
                }
                if (rules.MaxLength is { } maxLength && s.Length > maxLength)
                {
                    error = $"{Label} must be at most {maxLength} characters.";
                    return false;
                }
                if (rules.Pattern is { Length: > 0 } pattern && !MatchesPattern(s, pattern))
                {
                    error = $"{Label} format is invalid.";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern);
        }
        catch (ArgumentException)
        {
            // A pattern this client can't compile isn't the user's problem —
            // let the server have the last word on it.
            return true;
        }
    }

    private static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;   // midnight

        return TimeSpan.TryParseExact(trimmed, ["hh\\:mm", "h\\:mm", "hh\\:mm\\:ss"],
                   CultureInfo.InvariantCulture, out time)
               || TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out time);
    }

    partial void OnTextValueChanged(string value) => MarkDirty();
    partial void OnBoolSelectionChanged(string value) => MarkDirty();
    partial void OnSelectedOptionChanged(string? value) => MarkDirty();
    partial void OnDateValueChanged(DateTime? value) => MarkDirty();
    partial void OnTimeValueChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        ErrorMessage = null;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
