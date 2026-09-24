using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// The list-folder tree: browse, create, rename, move, delete.
/// </summary>
/// <remarks>
/// The API returns a flat array and expresses nesting only through
/// <c>parentId</c>, so the tree is built here. Critically, <b>the server does
/// not reject a cycle</b> — moving a folder into its own descendant returns
/// <c>200</c> — so every move is checked with
/// <see cref="InterlinedApiClient.WouldCreateCycle"/> first, and the tree build
/// is itself cycle-tolerant in case the data already contains one.
/// </remarks>
public partial class ListFolderPanelViewModel : ObservableObject
{
    private readonly SessionService _session;
    private List<ListFolder> _flat = [];

    public ObservableCollection<ListFolderNodeViewModel> Roots { get; } = new();

    /// <summary>Every folder, flat — for a "move to…" picker.</summary>
    public ObservableCollection<ListFolder> AllFolders { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateFolderCommand))]
    private string newFolderName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateFolderCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    /// <summary>Set when the data itself contains a loop, so the UI can say so.</summary>
    [ObservableProperty]
    private bool hasOrphanedFolders;

    public ListFolderPanelViewModel(SessionService session) => _session = session;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _flat = await _session.Api.GetListFoldersAsync();
            Rebuild();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Rebuild()
    {
        Roots.Clear();
        AllFolders.Clear();
        foreach (var f in _flat.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            AllFolders.Add(f);

        var byParent = _flat
            .GroupBy(f => f.ParentId ?? "")
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Track what we've placed. Because the server allows cycles, a naive
        // recursive build can loop forever — so nodes are only ever visited
        // once and anything left over is reported rather than rendered wrongly.
        var placed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in Children(byParent, "", placed))
            Roots.Add(root);

        HasOrphanedFolders = placed.Count < _flat.Count;
    }

    private List<ListFolderNodeViewModel> Children(
        Dictionary<string, List<ListFolder>> byParent, string parentKey, HashSet<string> placed)
    {
        if (!byParent.TryGetValue(parentKey, out var kids)) return [];

        var result = new List<ListFolderNodeViewModel>();
        foreach (var kid in kids.OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            // Cycle guard: never place the same folder twice.
            if (!placed.Add(kid.Id)) continue;
            result.Add(new ListFolderNodeViewModel(kid, Children(byParent, kid.Id, placed), this));
        }
        return result;
    }

    private bool CanCreate() => !IsBusy && !string.IsNullOrWhiteSpace(NewFolderName);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateFolderAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _session.Api.CreateListFolderAsync(NewFolderName.Trim());
            NewFolderName = "";
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            // Creating a folder is subscriber-gated; say that rather than
            // showing a bare status code.
            ErrorMessage = ex.StatusCode is 402 or 403
                ? "Creating list folders is a Subscriber feature."
                : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task RenameAsync(ListFolderNodeViewModel node, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            await _session.Api.UpdateListFolderAsync(node.Id, name: newName.Trim());
            await LoadAsync();
        }
        catch (InterlinedApiException ex) { ErrorMessage = ex.Message; }
    }

    internal async Task MoveAsync(ListFolderNodeViewModel node, string? newParentId)
    {
        // THE check. The server returns 200 for a cycle, so this is the only
        // thing standing between a move and an unrenderable tree.
        if (InterlinedApiClient.WouldCreateCycle(_flat, node.Id, newParentId))
        {
            ErrorMessage = $"Can't move “{node.Name}” inside itself or one of its own subfolders.";
            return;
        }

        try
        {
            if (newParentId is null or "")
                await _session.Api.UpdateListFolderAsync(node.Id, moveToRoot: true);
            else
                await _session.Api.UpdateListFolderAsync(node.Id, parentId: newParentId);
            await LoadAsync();
        }
        catch (InterlinedApiException ex) { ErrorMessage = ex.Message; }
    }

    internal async Task DeleteAsync(ListFolderNodeViewModel node)
    {
        try
        {
            await _session.Api.DeleteListFolderAsync(node.Id);
            StatusMessage = $"Deleted “{node.Name}” and any subfolders.";
            await LoadAsync();
        }
        catch (InterlinedApiException ex) { ErrorMessage = ex.Message; }
    }
}

/// <summary>One folder in the tree, with its children.</summary>
public partial class ListFolderNodeViewModel : ObservableObject
{
    private readonly ListFolderPanelViewModel _owner;

    public string Id { get; }
    public string Name { get; }
    public string? ParentId { get; }
    public ObservableCollection<ListFolderNodeViewModel> Children { get; }
    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private bool isRenaming;

    [ObservableProperty]
    private string renameText = "";

    /// <summary>Armed by a first press — delete cascades to subfolders.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeletePrompt))]
    private bool isDeleteArmed;

    public string DeletePrompt => IsDeleteArmed
        ? (HasChildren ? "Delete this and its subfolders?" : "Delete?")
        : "Delete";

    public ListFolderNodeViewModel(
        ListFolder folder, List<ListFolderNodeViewModel> children, ListFolderPanelViewModel owner)
    {
        Id = folder.Id;
        Name = folder.Name;
        ParentId = folder.ParentId;
        Children = new ObservableCollection<ListFolderNodeViewModel>(children);
        _owner = owner;
        renameText = folder.Name;
    }

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private async Task CommitRename()
    {
        IsRenaming = false;
        await _owner.RenameAsync(this, RenameText);
    }

    [RelayCommand]
    private async Task Delete()
    {
        // Two-step: the cascade to subfolders isn't obvious, and a native
        // confirm dialog would block the dispatcher.
        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            return;
        }
        IsDeleteArmed = false;
        await _owner.DeleteAsync(this);
    }

    [RelayCommand]
    private Task MoveToRoot() => _owner.MoveAsync(this, null);

    [RelayCommand]
    private Task MoveTo(ListFolder target) => _owner.MoveAsync(this, target?.Id);
}
