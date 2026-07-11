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

    partial void OnNewDocTitleChanged(string value) => CreateDocumentCommand.NotifyCanExecuteChanged();

    partial void OnSelectedDocumentChanged(DocumentSummary? value) => SaveDocumentCommand.NotifyCanExecuteChanged();
}
