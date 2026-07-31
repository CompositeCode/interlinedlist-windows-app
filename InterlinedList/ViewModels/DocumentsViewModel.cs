using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

public partial class DocumentsViewModel : ObservableObject
{
    private readonly SessionService _session;

    public ObservableCollection<DocumentSummary> RootDocuments { get; } = new();
    public ObservableCollection<DocumentFolder> Folders { get; } = new();
    public ObservableCollection<DocumentTemplate> Templates { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string newDocTitle = "";

    [ObservableProperty]
    private string newDocContent = "";

    [ObservableProperty]
    private DocumentSummary? selectedDocument;

    [ObservableProperty]
    private string editTitle = "";

    [ObservableProperty]
    private string editContent = "";

    // ── Folder management ─────────────────────────────────────────
    [ObservableProperty]
    private string newFolderName = "";

    // The folder currently being renamed inline (null when no inline editor is open).
    [ObservableProperty]
    private DocumentFolder? editingFolder;

    [ObservableProperty]
    private string editingFolderName = "";

    // The folder currently receiving a new document inline (null when closed).
    [ObservableProperty]
    private DocumentFolder? addingDocFolder;

    [ObservableProperty]
    private string newFolderDocTitle = "";

    public DocumentsViewModel(SessionService session)
    {
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var documentsPage = await _session.Api.GetRootDocumentsAsync();
            var foldersPage = await _session.Api.GetDocumentFoldersAsync();
            var templatesResponse = await _session.Api.GetDocumentTemplatesAsync();

            RootDocuments.Clear();
            foreach (var doc in documentsPage.Documents)
                RootDocuments.Add(doc);

            Folders.Clear();
            foreach (var folder in foldersPage.Folders)
                Folders.Add(folder);

            Templates.Clear();
            foreach (var template in templatesResponse.Templates)
                Templates.Add(template);

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

    private bool CanCreateDocument() => !string.IsNullOrWhiteSpace(NewDocTitle);

    [RelayCommand(CanExecute = nameof(CanCreateDocument))]
    private async Task CreateDocumentAsync()
    {
        try
        {
            await _session.Api.CreateDocumentAsync(NewDocTitle.Trim(), NewDocContent);
            NewDocTitle = "";
            NewDocContent = "";
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void SelectDocument(DocumentSummary doc)
    {
        SelectedDocument = doc;
        EditTitle = doc.Title;
        EditContent = doc.Content;
    }

    private bool CanSaveDocument() => SelectedDocument is not null;

    [RelayCommand(CanExecute = nameof(CanSaveDocument))]
    private async Task SaveDocumentAsync()
    {
        if (SelectedDocument is not { } doc) return;

        try
        {
            await _session.Api.UpdateDocumentAsync(doc.Id, EditTitle, EditContent);
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteDocumentAsync(DocumentSummary doc)
    {
        try
        {
            await _session.Api.DeleteDocumentAsync(doc.Id);
            if (SelectedDocument == doc)
            {
                SelectedDocument = null;
                EditTitle = "";
                EditContent = "";
            }
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task UseTemplateAsync(DocumentTemplate template)
    {
        try
        {
            await _session.Api.CreateFromTemplateAsync(template.Id);
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Folder management commands ────────────────────────────────

    private bool CanCreateFolder() => !string.IsNullOrWhiteSpace(NewFolderName);

    [RelayCommand(CanExecute = nameof(CanCreateFolder))]
    private async Task CreateFolderAsync()
    {
        try
        {
            await _session.Api.CreateDocumentFolderAsync(NewFolderName.Trim());
            NewFolderName = "";
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void StartRenameFolder(DocumentFolder folder)
    {
        EditingFolder = folder;
        EditingFolderName = folder.Name;
    }

    [RelayCommand]
    private void CancelRenameFolder()
    {
        EditingFolder = null;
        EditingFolderName = "";
    }

    private bool CanSaveRenameFolder() => EditingFolder is not null && !string.IsNullOrWhiteSpace(EditingFolderName);

    [RelayCommand(CanExecute = nameof(CanSaveRenameFolder))]
    private async Task SaveRenameFolderAsync()
    {
        if (EditingFolder is not { } folder) return;

        try
        {
            await _session.Api.RenameDocumentFolderAsync(folder.Id, EditingFolderName.Trim());
            EditingFolder = null;
            EditingFolderName = "";
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(DocumentFolder folder)
    {
        try
        {
            await _session.Api.DeleteDocumentFolderAsync(folder.Id);
            if (EditingFolder == folder)
            {
                EditingFolder = null;
                EditingFolderName = "";
            }
            if (AddingDocFolder == folder)
            {
                AddingDocFolder = null;
                NewFolderDocTitle = "";
            }
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void StartAddDocToFolder(DocumentFolder folder)
    {
        AddingDocFolder = folder;
        NewFolderDocTitle = "";
    }

    [RelayCommand]
    private void CancelAddDocToFolder()
    {
        AddingDocFolder = null;
        NewFolderDocTitle = "";
    }

    private bool CanSaveDocToFolder() => AddingDocFolder is not null && !string.IsNullOrWhiteSpace(NewFolderDocTitle);

    [RelayCommand(CanExecute = nameof(CanSaveDocToFolder))]
    private async Task SaveDocToFolderAsync()
    {
        if (AddingDocFolder is not { } folder) return;

        try
        {
            await _session.Api.CreateDocumentInFolderAsync(folder.Id, NewFolderDocTitle.Trim(), "");
            AddingDocFolder = null;
            NewFolderDocTitle = "";
            ErrorMessage = null;
            await LoadAsync();
        }
        catch (InterlinedApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnNewDocTitleChanged(string value) => CreateDocumentCommand.NotifyCanExecuteChanged();

    partial void OnSelectedDocumentChanged(DocumentSummary? value) => SaveDocumentCommand.NotifyCanExecuteChanged();

    partial void OnNewFolderNameChanged(string value) => CreateFolderCommand.NotifyCanExecuteChanged();

    partial void OnEditingFolderChanged(DocumentFolder? value) => SaveRenameFolderCommand.NotifyCanExecuteChanged();

    partial void OnEditingFolderNameChanged(string value) => SaveRenameFolderCommand.NotifyCanExecuteChanged();

    partial void OnAddingDocFolderChanged(DocumentFolder? value) => SaveDocToFolderCommand.NotifyCanExecuteChanged();

    partial void OnNewFolderDocTitleChanged(string value) => SaveDocToFolderCommand.NotifyCanExecuteChanged();
}
