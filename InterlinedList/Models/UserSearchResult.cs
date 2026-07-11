namespace InterlinedList.Models;

public sealed class UserSearchResult
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public string? Avatar { get; init; }
    public bool IsPrivate { get; init; }
}
