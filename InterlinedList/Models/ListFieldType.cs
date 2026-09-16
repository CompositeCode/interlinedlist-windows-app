namespace InterlinedList.Models;

/// <summary>
/// The twelve column types the list-schema DSL accepts (all verified live
/// 2026-09-16 by creating a list whose schema used every one of them).
/// Kept as string constants rather than an enum because the wire format is a
/// string and the server echoes unknown values back in its error messages.
/// </summary>
public static class ListFieldType
{
    public const string Text = "text";
    public const string TextArea = "textarea";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Email = "email";
    public const string Url = "url";
    public const string Tel = "tel";
    public const string Select = "select";
    public const string MultiSelect = "multiselect";
    public const string Priority = "priority";

    /// <summary>All twelve, in the order the server lists them in its 400 messages.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Text, Number, Date, DateTime, Boolean, Select, MultiSelect, TextArea, Email, Url, Tel, Priority
    ];

    /// <summary>
    /// The SIX types the non-destructive <c>properties</c> edit accepts.
    /// Verified live 2026-09-16: PUT /api/lists/{id}/schema with a properties
    /// array containing propertyType "textarea" answers
    /// <c>400 Unknown propertyType 'textarea'. Allowed: text, number, boolean, date, url, email</c>
    /// — and propertyType is mandatory on every item (omitting it or sending
    /// null yields the same error for 'undefined'/'null'). So a list holding any
    /// of the other six types can only be re-shaped through the destructive DSL
    /// rebuild. See <see cref="ListSchema.SupportsPropertiesEdit"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> PropertiesEditable =
    [
        Text, Number, Boolean, Date, Url, Email
    ];

    /// <summary>What <c>priority</c> falls back to when no options are supplied (verified live).</summary>
    public static readonly IReadOnlyList<string> PriorityOptions = ["low", "medium", "high", "urgent"];

    public static bool IsValid(string? type) => type is not null && All.Contains(type);

    /// <summary><c>select</c>/<c>multiselect</c> are rejected without an options array.</summary>
    public static bool RequiresOptions(string? type) => type is Select or MultiSelect;

    /// <summary>True when the type survives a non-destructive <c>properties</c> edit.</summary>
    public static bool SupportsPropertiesEdit(string? type) => type is not null && PropertiesEditable.Contains(type);

    /// <summary>Human label for the type picker.</summary>
    public static string DisplayName(string? type) => type switch
    {
        Text => "Text",
        TextArea => "Long text",
        Number => "Number",
        Boolean => "True/False",
        Date => "Date",
        DateTime => "Date & time",
        Email => "Email",
        Url => "URL",
        Tel => "Phone",
        Select => "Single select",
        MultiSelect => "Multi select",
        Priority => "Priority",
        _ => type ?? string.Empty
    };
}
