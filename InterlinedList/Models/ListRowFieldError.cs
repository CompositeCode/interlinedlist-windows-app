namespace InterlinedList.Models;

/// <summary>
/// One entry of a row write's <c>422</c> body:
/// <c>{ "error": "Validation failed", "code": "validation_failed",
///     "details": [ { "field": "mail", "message": "Email must be a valid email address" } ] }</c>
///
/// <see cref="Field"/> is the column's <b>key</b> (so it maps straight onto a
/// form control) while <see cref="Message"/> is phrased with the column's
/// <b>label</b> — both live-verified 2026-09-16, including that a single write
/// can come back with several details at once (a five-problem row returned all
/// five).
/// </summary>
public sealed record ListRowFieldError(string Field, string Message);
