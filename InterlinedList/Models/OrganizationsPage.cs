namespace InterlinedList.Models;

public sealed class OrganizationsPage
{
    public required List<OrganizationSummary> Organizations { get; init; }

    // "my organizations" (GET api/user/organizations) returns no pagination key at all.
    public Pagination? Pagination { get; init; }
}
