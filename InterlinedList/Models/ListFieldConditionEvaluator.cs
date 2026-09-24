using System.Globalization;
using System.Text.Json;

namespace InterlinedList.Models;

/// <summary>
/// Client-side evaluation of a column's conditional visibility. This is not a
/// convenience — it is the ONLY evaluation there is: the server stores a
/// <c>visibilityCondition</c> verbatim and never applies it (an unknown
/// operator, a missing watched key and a forward reference all save happily and
/// then never match — live-verified 2026-09-16), so a form that doesn't do this
/// simply shows every column always.
///
/// Values arrive as whatever the row editor is holding — a string, a
/// <see cref="bool"/>, a number, a list of strings for a multiselect, or the
/// raw <see cref="JsonElement"/> straight off a stored row — hence the
/// text/number coercion rather than a typed comparison.
/// </summary>
public static class ListFieldConditionEvaluator
{
    /// <summary>
    /// True when <paramref name="field"/> should be shown, given a lookup for
    /// the current value of any other column by key. A field with no condition
    /// is always shown; a condition naming a column the lookup doesn't know
    /// resolves against null (which is what the server would effectively do —
    /// never match — for anything but isEmpty).
    /// </summary>
    public static bool IsShown(ListField field, Func<string, object?> valueOf)
    {
        if (field.Visibility?.Condition is not { } condition) return true;
        return Matches(condition, valueOf(condition.Field));
    }

    public static bool Matches(ListFieldCondition condition, object? value)
    {
        var text = AsText(value);
        var targetText = AsText(condition.Value);

        return condition.Operator switch
        {
            ListFieldOperator.Equal => Equal(value, condition.Value),
            ListFieldOperator.NotEqual => !Equal(value, condition.Value),
            ListFieldOperator.Contains => Contains(value, targetText),
            ListFieldOperator.NotContains => !Contains(value, targetText),
            ListFieldOperator.GreaterThan => Compare(text, targetText) > 0,
            ListFieldOperator.LessThan => Compare(text, targetText) < 0,
            ListFieldOperator.GreaterThanOrEqual => Compare(text, targetText) >= 0,
            ListFieldOperator.LessThanOrEqual => Compare(text, targetText) <= 0,
            ListFieldOperator.IsEmpty => IsEmpty(value),
            ListFieldOperator.IsNotEmpty => !IsEmpty(value),
            // An operator neither side understands can only ever be false —
            // matching the server's "saves fine, never matches" behaviour.
            _ => false
        };
    }

    public static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        JsonElement e => e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                         || (e.ValueKind == JsonValueKind.String && (e.GetString() ?? "").Length == 0)
                         || (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 0),
        System.Collections.IEnumerable list and not string => !list.Cast<object?>().Any(),
        _ => false
    };

    private static bool Equal(object? value, object? target)
    {
        if (TryNumber(value, out var left) && TryNumber(target, out var right))
            return Math.Abs(left - right) < double.Epsilon;
        return string.Equals(AsText(value), AsText(target), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(object? value, string target)
    {
        if (target.Length == 0) return false;

        // A multiselect "contains" means membership, not substring.
        if (value is JsonElement { ValueKind: JsonValueKind.Array } array)
            return array.EnumerateArray().Any(e => string.Equals(AsText(e), target, StringComparison.OrdinalIgnoreCase));

        if (value is System.Collections.IEnumerable list and not string)
            return list.Cast<object?>().Any(item => string.Equals(AsText(item), target, StringComparison.OrdinalIgnoreCase));

        return AsText(value).Contains(target, StringComparison.OrdinalIgnoreCase);
    }

    private static int Compare(string left, string right)
    {
        if (TryNumber(left, out var l) && TryNumber(right, out var r))
            return l.CompareTo(r);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double d:
                number = d;
                return true;
            case int i:
                number = i;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetDouble(out number);
        }

        return double.TryParse(AsText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Flatten any held value to the text form comparisons use.</summary>
    public static string AsText(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => e.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(AsTextElement)),
            _ => e.GetRawText()
        },
        System.Collections.IEnumerable list and not string =>
            string.Join(", ", list.Cast<object?>().Select(AsText)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString() ?? string.Empty
    };

    private static string AsTextElement(JsonElement element) => AsText(element);
}
