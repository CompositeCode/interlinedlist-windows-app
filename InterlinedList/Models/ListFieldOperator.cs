namespace InterlinedList.Models;

/// <summary>
/// The ten conditional-visibility operators a <see cref="ListFieldCondition"/>
/// may use. NOTE (verified live 2026-09-16): the server does <b>not</b> validate
/// these — a schema carrying operator "matches", a condition pointing at an
/// unknown key, or one pointing at a column declared *later* in <c>fields</c>
/// all return 200 and are simply never satisfied. Validation is therefore a
/// client responsibility (see <see cref="ListSchema.Validate"/>).
/// </summary>
public static class ListFieldOperator
{
    // "Equals"/"NotEquals" would hide the inherited static object.Equals, hence
    // the Equal/NotEqual spelling — the wire values are unchanged.
    public const string Equal = "equals";
    public const string NotEqual = "notEquals";
    public const string Contains = "contains";
    public const string NotContains = "notContains";
    public const string GreaterThan = "greaterThan";
    public const string LessThan = "lessThan";
    public const string GreaterThanOrEqual = "greaterThanOrEqual";
    public const string LessThanOrEqual = "lessThanOrEqual";
    public const string IsEmpty = "isEmpty";
    public const string IsNotEmpty = "isNotEmpty";

    public static readonly IReadOnlyList<string> All =
    [
        Equal, NotEqual, Contains, NotContains,
        GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual,
        IsEmpty, IsNotEmpty
    ];

    public static bool IsValid(string? op) => op is not null && All.Contains(op);

    /// <summary><c>isEmpty</c>/<c>isNotEmpty</c> ignore the condition's value.</summary>
    public static bool IgnoresValue(string? op) => op is IsEmpty or IsNotEmpty;

    public static string DisplayName(string? op) => op switch
    {
        Equal => "equals",
        NotEqual => "does not equal",
        Contains => "contains",
        NotContains => "does not contain",
        GreaterThan => "is greater than",
        LessThan => "is less than",
        GreaterThanOrEqual => "is at least",
        LessThanOrEqual => "is at most",
        IsEmpty => "is empty",
        IsNotEmpty => "is not empty",
        _ => op ?? string.Empty
    };
}
