namespace InterlinedList.Models;

public sealed class CurrentUser
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public string? Bio { get; init; }
    public string? Theme { get; init; }
    public bool EmailVerified { get; init; }
    public int MaxMessageLength { get; init; }
    public bool DefaultPubliclyVisible { get; init; }
    public bool IsPrivateAccount { get; init; }
    public string? CustomerStatus { get; init; }
    public bool IsAdministrator { get; init; }

    public string DisplayNameOrUsername => DisplayName ?? Username;
}
