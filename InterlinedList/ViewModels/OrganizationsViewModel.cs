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
            ErrorMessage = null;
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnNewOrgNameChanged(string value) => CreateOrganizationCommand.NotifyCanExecuteChanged();
}
