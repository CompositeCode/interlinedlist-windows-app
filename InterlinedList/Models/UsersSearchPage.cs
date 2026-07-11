namespace InterlinedList.Models;

public sealed class UsersSearchPage
{
    public required List<UserSearchResult> Users { get; init; }
    public int Total { get; init; }
}
