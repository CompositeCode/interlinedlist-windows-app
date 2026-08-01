using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class OrganizationsViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SessionService _session;

    public ObservableCollection<OrganizationSummary> MyOrganizations { get; } = new();
    public ObservableCollection<OrganizationSummary> AllOrganizations { get; } = new();

    // Members of the currently selected organization.
    public ObservableCollection<OrgMember> Members { get; } = new();

    // Results of the "add member" user search.
    public ObservableCollection<UserSearchResult> UserSearchResults { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string newOrgName = "";

    [ObservableProperty]
    private string newOrgDescription = "";

    [ObservableProperty]
    private bool newOrgIsPublic = true;

    [ObservableProperty]
    private OrganizationSummary? selectedOrganization;

    // True when the signed-in user's role in the selected org is owner or admin.
    [ObservableProperty]
    private bool canManageMembers;

    // True only for the owner (delete-org affordance).
    [ObservableProperty]
    private bool canDeleteOrganization;

    // ── Add-member search ───────────────────────────────────────────────────
    [ObservableProperty]
    private string memberSearchQuery = "";

    [ObservableProperty]
    private bool isSearchingUsers;

    // ── Edit-org form (mirrors SelectedOrganization when the panel opens) ────
    [ObservableProperty]
    private bool isEditingOrganization;

    [ObservableProperty]
    private string editOrgName = "";

    [ObservableProperty]
    private string editOrgDescription = "";

    [ObservableProperty]
    private bool editOrgIsPublic;

    public OrganizationsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var mine = await _session.Api.GetMyOrganizationsAsync();
            var all = await _session.Api.GetAllOrganizationsAsync(limit: PageSize);

            MyOrganizations.Clear();
            foreach (var org in mine.Organizations)
                MyOrganizations.Add(org);

            AllOrganizations.Clear();
            foreach (var org in all.Organizations)
                AllOrganizations.Add(org);

            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanCreateOrganization() => !string.IsNullOrWhiteSpace(NewOrgName);

    [RelayCommand(CanExecute = nameof(CanCreateOrganization))]
    private async Task CreateOrganizationAsync()
    {
        try
        {
            var description = string.IsNullOrWhiteSpace(NewOrgDescription) ? null : NewOrgDescription.Trim();
            await _session.Api.CreateOrganizationAsync(NewOrgName.Trim(), description, NewOrgIsPublic);
            NewOrgName = "";
            NewOrgDescription = "";
            NewOrgIsPublic = true;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SelectOrganizationAsync(OrganizationSummary org)
    {
        try
        {
            SelectedOrganization = await _session.Api.GetOrganizationAsync(org.Id);
            IsEditingOrganization = false;
            MemberSearchQuery = "";
            UserSearchResults.Clear();
            ErrorMessage = null;
            await LoadMembersAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Members ─────────────────────────────────────────────────────────────

    // Re-fetch the member list for the selected org and recompute the current
    // user's management rights. Called after selecting an org and after every
    // member mutation (read-after-write).
    private async Task LoadMembersAsync()
    {
        Members.Clear();
        CanManageMembers = false;
        CanDeleteOrganization = false;

        var org = SelectedOrganization;
        if (org is null)
            return;

        try
        {
            var members = await _session.Api.GetOrgMembersAsync(org.Id);
            foreach (var member in members)
                Members.Add(member);

            var myId = _session.CurrentUser?.Id;
            var mine = myId is null
                ? null
                : Members.FirstOrDefault(m => m.Id == myId);
            var myRole = mine?.Role;
            CanManageMembers = myRole is "owner" or "admin";
            CanDeleteOrganization = myRole is "owner";
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveOrgMemberAsync(OrgMember member)
    {
        var org = SelectedOrganization;
        if (org is null || member is null)
            return;

        try
        {
            await _session.Api.RemoveOrgMemberAsync(org.Id, member.Id);
            await LoadMembersAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task PromoteMemberAsync(OrgMember member)
    {
        // member → admin → owner
        var next = member?.Role switch
        {
            "member" => "admin",
            "admin" => "owner",
            _ => null,
        };
        await SetMemberRoleAsync(member, next);
    }

    [RelayCommand]
    private async Task DemoteMemberAsync(OrgMember member)
    {
        // owner → admin → member
        var next = member?.Role switch
        {
            "owner" => "admin",
            "admin" => "member",
            _ => null,
        };
        await SetMemberRoleAsync(member, next);
    }

    private async Task SetMemberRoleAsync(OrgMember? member, string? role)
    {
        var org = SelectedOrganization;
        if (org is null || member is null || role is null)
            return;

        try
        {
            await _session.Api.UpdateOrgMemberRoleAsync(org.Id, member.Id, role);
            await LoadMembersAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Add member (user search → add) ──────────────────────────────────────

    private bool CanSearchUsers() => !string.IsNullOrWhiteSpace(MemberSearchQuery);

    [RelayCommand(CanExecute = nameof(CanSearchUsers))]
    private async Task SearchUsersAsync()
    {
        UserSearchResults.Clear();
        IsSearchingUsers = true;
        try
        {
            var page = await _session.Api.SearchUsersAsync(MemberSearchQuery.Trim());
            foreach (var user in page.Users)
                UserSearchResults.Add(user);
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSearchingUsers = false;
        }
    }

    [RelayCommand]
    private async Task AddMemberAsync(UserSearchResult user)
    {
        var org = SelectedOrganization;
        if (org is null || user is null)
            return;

        try
        {
            await _session.Api.AddOrgMemberAsync(org.Id, user.Id, "member");
            MemberSearchQuery = "";
            UserSearchResults.Clear();
            await LoadMembersAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Edit / delete organization ──────────────────────────────────────────

    [RelayCommand]
    private void BeginEditOrganization()
    {
        var org = SelectedOrganization;
        if (org is null)
            return;

        EditOrgName = org.Name;
        EditOrgDescription = org.Description ?? "";
        EditOrgIsPublic = org.IsPublic;
        IsEditingOrganization = true;
    }

    [RelayCommand]
    private void CancelEditOrganization() => IsEditingOrganization = false;

    private bool CanSaveOrganization() => !string.IsNullOrWhiteSpace(EditOrgName);

    [RelayCommand(CanExecute = nameof(CanSaveOrganization))]
    private async Task SaveOrganizationAsync()
    {
        var org = SelectedOrganization;
        if (org is null)
            return;

        try
        {
            var description = string.IsNullOrWhiteSpace(EditOrgDescription) ? null : EditOrgDescription.Trim();
            await _session.Api.UpdateOrganizationAsync(org.Id, EditOrgName.Trim(), description, EditOrgIsPublic);
            IsEditingOrganization = false;
            // Read-after-write: re-fetch the org detail and its members.
            SelectedOrganization = await _session.Api.GetOrganizationAsync(org.Id);
            await LoadMembersAsync();
            // Keep the left-pane lists in sync with the edited name/visibility.
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteOrganizationAsync()
    {
        var org = SelectedOrganization;
        if (org is null)
            return;

        try
        {
            await _session.Api.DeleteOrganizationAsync(org.Id);
            SelectedOrganization = null;
            IsEditingOrganization = false;
            Members.Clear();
            UserSearchResults.Clear();
            CanManageMembers = false;
            CanDeleteOrganization = false;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnNewOrgNameChanged(string value) => CreateOrganizationCommand.NotifyCanExecuteChanged();
    partial void OnMemberSearchQueryChanged(string value) => SearchUsersCommand.NotifyCanExecuteChanged();
    partial void OnEditOrgNameChanged(string value) => SaveOrganizationCommand.NotifyCanExecuteChanged();
}
