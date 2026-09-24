using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>One source a derived Powered Document mode can point at.</summary>
public sealed record AiDocumentSource(string Id, string Title)
{
    public override string ToString() => Title;
}

/// <summary>One entry in the mode picker.</summary>
public sealed record AiDocumentModeOption(AiDocumentMode Mode, string Label, string Hint);

/// <summary>
/// Powered Document: draft a full markdown document, in one of four modes.
///
/// <b>You pass a reference, not source text.</b> <c>from_list</c> sends a
/// <c>listId</c>, <c>from_article</c> a <c>documentId</c>, <c>research_url</c> a
/// URL — and the server loads (or fetches) and truncates the source itself.
/// Those three are IDOR-guarded: a reference that's missing or isn't yours comes
/// back <c>422 invalid_input</c>. Verified live, all four for free (input
/// validation costs no quota):
/// <list type="bullet">
/// <item><c>from_list</c> with a non-owned id → <c>{"error":"List not found.","code":"invalid_input"}</c></item>
/// <item><c>from_list</c> with no id → <c>{"error":"A list must be selected.","code":"invalid_input"}</c></item>
/// <item><c>from_article</c> with a non-owned id → <c>{"error":"Document not found.","code":"invalid_input"}</c></item>
/// <item><c>research_url</c> with <c>ftp://</c> → <c>{"error":"Only http(s) URLs are supported.","code":"invalid_input"}</c></item>
/// </list>
/// Those all land on <see cref="SourceError"/> — next to the offending picker —
/// rather than in the generic notice slot, which is #15's last acceptance
/// criterion.
///
/// <b>One live finding shapes the mode picker.</b> An <i>unrecognized</i>
/// <c>context.mode</c> is not cheap-rejected: <c>{"mode":"not_a_mode"}</c> fell
/// through to the model and came back <c>422 invalid_ai_output</c> — <b>billed
/// a quota unit</b>. So the mode is never a free string here; it's the
/// four-value <see cref="AiDocumentMode"/> enum, always, and the picker can only
/// ever produce one of them.
/// </summary>
public sealed partial class PoweredDocumentPanelViewModel : AiPanelViewModelBase
{
    private bool _sourcesLoaded;

    public PoweredDocumentPanelViewModel(SessionService session, AiAvailabilityService? availability = null)
        : base(session, availability)
    {
        Modes = new[]
        {
            new AiDocumentModeOption(AiDocumentMode.Article, "From a topic", "Drafts from your description alone."),
            new AiDocumentModeOption(AiDocumentMode.FromList, "From one of my lists", "Reads the list's schema and up to 50 rows."),
            new AiDocumentModeOption(AiDocumentMode.FromArticle, "From one of my documents", "Reads that document's title and body."),
            new AiDocumentModeOption(AiDocumentMode.ResearchUrl, "From a URL", "Fetches the page and cites it.")
        };

        selectedMode = Modes[0];
    }

    /// <summary>Set by the hosting control from its <c>OpenDocumentCommand</c> dependency property.</summary>
    public ICommand? OpenDocumentCommand { get; set; }

    /// <summary>Set by the hosting control from its <c>RefreshCommand</c> dependency property.</summary>
    public ICommand? RefreshCommand { get; set; }

    // ── Mode ────────────────────────────────────────────────────────────────────

    public IReadOnlyList<AiDocumentModeOption> Modes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(IsArticleSourceMode))]
    [NotifyPropertyChangedFor(nameof(IsUrlMode))]
    [NotifyPropertyChangedFor(nameof(NeedsSource))]
    [NotifyPropertyChangedFor(nameof(ModeHint))]
    private AiDocumentModeOption selectedMode;

    public bool IsListMode => SelectedMode.Mode == AiDocumentMode.FromList;
    public bool IsArticleSourceMode => SelectedMode.Mode == AiDocumentMode.FromArticle;
    public bool IsUrlMode => SelectedMode.Mode == AiDocumentMode.ResearchUrl;

    /// <summary>True for the three derived modes, which all need a reference.</summary>
    public bool NeedsSource => SelectedMode.Mode != AiDocumentMode.Article;

    public string ModeHint => SelectedMode.Hint;

    partial void OnSelectedModeChanged(AiDocumentModeOption value)
    {
        SourceError = null;
        SuggestCommand.NotifyCanExecuteChanged();

        // Load the pickers' contents only once a mode actually needs them —
        // two GETs nobody asked for is the wrong default on panel construction.
        if (NeedsSource) _ = LoadSourcesAsync();
    }

    // ── Sources ─────────────────────────────────────────────────────────────────

    public ObservableCollection<AiDocumentSource> MyLists { get; } = new();

    public ObservableCollection<AiDocumentSource> MyDocuments { get; } = new();

    [ObservableProperty]
    private AiDocumentSource? selectedList;

    [ObservableProperty]
    private AiDocumentSource? selectedDocument;

    [ObservableProperty]
    private string sourceUrl = "";

    /// <summary>
    /// The message shown <b>against the source picker</b>, not in the generic
    /// notice slot. Both the client's own pre-flight and a server-side
    /// reference rejection land here.
    /// </summary>
    [ObservableProperty]
    private string? sourceError;

    [ObservableProperty]
    private bool isLoadingSources;

    partial void OnSelectedListChanged(AiDocumentSource? value)
    {
        SourceError = null;
        SuggestCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDocumentChanged(AiDocumentSource? value)
    {
        SourceError = null;
        SuggestCommand.NotifyCanExecuteChanged();
    }

    partial void OnSourceUrlChanged(string value)
    {
        SourceError = null;
        SuggestCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSourcesAsync()
    {
        if (_sourcesLoaded) return;

        IsLoadingSources = true;
        try
        {
            var lists = await Session.Api.GetListsAsync(limit: 100, offset: 0);
            MyLists.Clear();
            foreach (var list in lists.Lists)
                MyLists.Add(new AiDocumentSource(list.Id, list.Title));

            // Root documents plus everything filed in a folder — a document a
            // user tidied away is still a legitimate source, and the folder
            // entries carry their own ids.
            var documents = await Session.Api.GetRootDocumentsAsync();
            var folders = await Session.Api.GetDocumentFoldersAsync();

            MyDocuments.Clear();
            foreach (var doc in documents.Documents)
                MyDocuments.Add(new AiDocumentSource(doc.Id, doc.Title));

            foreach (var folder in folders.Folders)
                foreach (var entry in folder.Documents)
                    MyDocuments.Add(new AiDocumentSource(entry.Id, $"{folder.Name} / {entry.Title}"));

            _sourcesLoaded = true;
        }
        catch (InterlinedApiException ex)
        {
            SourceError = ex.Message;
        }
        catch (Exception ex)
        {
            SourceError = "Couldn't load your lists and documents.";
            AppLog.Warn($"Powered Document source load failed: {ex.Message}");
        }
        finally
        {
            IsLoadingSources = false;
        }
    }

    [RelayCommand]
    private async Task ReloadSourcesAsync()
    {
        _sourcesLoaded = false;
        await LoadSourcesAsync();
    }

    // ── Input ───────────────────────────────────────────────────────────────────

    /// <summary>Collapsed by default — an option in the Documents flow, not the default path.</summary>
    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordCount))]
    [NotifyPropertyChangedFor(nameof(WordCountLabel))]
    [NotifyPropertyChangedFor(nameof(IsOverWordCap))]
    private string topic = "";

    public int WordCount => AiFeatureLimits.CountWords(Topic);

    public int MaxWords => AiFeatureLimits.For(AiFeature.PoweredDocument).MaxInputWords;

    public string WordCountLabel => $"{WordCount} / {MaxWords} words";

    public bool IsOverWordCap => WordCount > MaxWords;

    partial void OnTopicChanged(string value) => SuggestCommand.NotifyCanExecuteChanged();

    // ── Preview ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool hasPreview;

    [ObservableProperty]
    private string draftTitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CharacterCountLabel))]
    [NotifyPropertyChangedFor(nameof(IsOverCharacterCap))]
    private string draftMarkdown = "";

    /// <summary>Server-side ceiling on a generated document.</summary>
    public int MaxCharacters => AiFeatureLimits.MaxDocumentCharacters;

    public string CharacterCountLabel => $"{DraftMarkdown.Length:N0} / {MaxCharacters:N0} characters";

    public bool IsOverCharacterCap => DraftMarkdown.Length > MaxCharacters;

    /// <summary>The model's section plan, shown as read-only context for the draft.</summary>
    public ObservableCollection<string> DraftOutline { get; } = new();

    public bool HasOutline => DraftOutline.Count > 0;

    /// <summary>Whether the created document should be publicly readable. The artifact carries this.</summary>
    [ObservableProperty]
    private bool draftIsPublic;

    /// <summary>
    /// Toggles the preview between the rendered document and the editable
    /// markdown source. The source is the single point of truth — the rendered
    /// view is a projection of it, so edits can't be lost to the toggle.
    /// </summary>
    [ObservableProperty]
    private bool showMarkdownSource;

    [ObservableProperty]
    private string? usageLabel;

    // ── Commands ────────────────────────────────────────────────────────────────

    protected override void NotifyAiCommandsChanged()
    {
        SuggestCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleOpen()
    {
        IsOpen = !IsOpen;
        if (IsOpen) _ = EnsureStatusLoadedAsync();
    }

    private bool CanSuggest()
    {
        if (!CanRunAi || string.IsNullOrWhiteSpace(Topic)) return false;

        return SelectedMode.Mode switch
        {
            AiDocumentMode.FromList => SelectedList is not null,
            AiDocumentMode.FromArticle => SelectedDocument is not null,
            AiDocumentMode.ResearchUrl => !string.IsNullOrWhiteSpace(SourceUrl),
            _ => true
        };
    }

    [RelayCommand(CanExecute = nameof(CanSuggest))]
    private async Task SuggestAsync(CancellationToken ct)
    {
        SourceError = null;

        if (!TryBuildContext(out var context, out var sourceError))
        {
            // Against the field, not in the generic slot.
            SourceError = sourceError;
            return;
        }

        if (!AiFeatureLimits.TryValidateInput(AiFeature.PoweredDocument, Topic, out var inputError))
        {
            RejectLocally(inputError!);
            return;
        }

        var mode = SelectedMode.Mode;

        var result = await RunAiAsync(
            "Drafting your document…",
            token => Session.Api.AiSuggestAsync(AiFeature.PoweredDocument, Topic.Trim(), context, ct: token),
            r => r.Quota,
            ct);

        if (result is null)
        {
            AttributeFailureToSource(mode);
            return;
        }

        if (result.Artifact.AsDocument() is not { } document)
        {
            Notice = new AiNotice(AiNoticeKind.ModelDeclined,
                $"Expected a document but the AI returned a \"{result.Artifact.Kind}\". " +
                "That attempt still counted against today's allowance — try rewording the topic.",
                Spent: true);
            return;
        }

        DraftTitle = document.Title;
        DraftMarkdown = document.Markdown;
        DraftIsPublic = document.IsPublic;

        DraftOutline.Clear();
        foreach (var section in document.Outline)
            DraftOutline.Add(section);
        OnPropertyChanged(nameof(HasOutline));

        ShowMarkdownSource = false;
        HasPreview = true;
        UsageLabel = result.Usage?.Display;
        Notice = AiNotice.Info("Draft ready. Edit it below, then save it as a document.");
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Move a reference rejection out of the generic notice and onto the source
    /// field, per #15.
    ///
    /// <b>Why mode + code is enough, without reading the server's prose.</b>
    /// Every client-knowable input problem — the 500-word cap, an empty topic,
    /// a missing reference, a non-http scheme — is already rejected locally
    /// before the call goes out. So an <c>invalid_input</c> that comes back from
    /// the server in a derived mode can only be the one thing the client
    /// cannot check: the reference doesn't exist, or it isn't this user's. That
    /// inference is what lets this branch on the code alone (the repo's rule)
    /// while still showing the server's own wording, which is the specific and
    /// useful part.
    /// </summary>
    private void AttributeFailureToSource(AiDocumentMode mode)
    {
        if (mode == AiDocumentMode.Article) return;
        if (Notice is not { Kind: AiNoticeKind.Input } notice) return;

        SourceError = mode switch
        {
            AiDocumentMode.FromList => $"{notice.Text} Pick a different list.",
            AiDocumentMode.FromArticle => $"{notice.Text} Pick a different document.",
            AiDocumentMode.ResearchUrl => $"{notice.Text} Check the URL.",
            _ => notice.Text
        };

        ClearNotice();
    }

    /// <summary>
    /// Build <c>context</c> for the selected mode, running the checks the
    /// server would answer 422 for. These cost no quota either way (verified),
    /// but catching them locally also keeps the message on the right field.
    /// </summary>
    private bool TryBuildContext(out AiContext? context, out string? error)
    {
        context = null;
        error = null;

        switch (SelectedMode.Mode)
        {
            case AiDocumentMode.Article:
                context = AiContext.DocumentFromTopic();
                return true;

            case AiDocumentMode.FromList:
                if (SelectedList is null)
                {
                    error = "Pick one of your lists to draft from.";
                    return false;
                }
                context = AiContext.DocumentFromList(SelectedList.Id);
                break;

            case AiDocumentMode.FromArticle:
                if (SelectedDocument is null)
                {
                    error = "Pick one of your documents to draft from.";
                    return false;
                }
                context = AiContext.DocumentFromArticle(SelectedDocument.Id);
                break;

            case AiDocumentMode.ResearchUrl:
                var url = SourceUrl.Trim();
                if (url.Length == 0)
                {
                    error = "Enter the URL to research.";
                    return false;
                }

                // Client-side http/https check. The server enforces it too
                // ("Only http(s) URLs are supported.", verified), but catching
                // it here puts the message on the field and skips a round trip.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    error = "Only http:// and https:// URLs are supported.";
                    return false;
                }

                context = AiContext.DocumentFromResearchUrl(uri.ToString());
                break;

            default:
                error = "Pick how the document should be drafted.";
                return false;
        }

        // Second belt: the service's own pre-flight for the same rules.
        if (context is not null && !context.TryValidate(AiFeature.PoweredDocument, out var contextError))
        {
            error = contextError;
            return false;
        }

        return true;
    }

    [RelayCommand]
    private void Discard()
    {
        HasPreview = false;
        DraftTitle = "";
        DraftMarkdown = "";
        DraftIsPublic = false;
        DraftOutline.Clear();
        OnPropertyChanged(nameof(HasOutline));
        ShowMarkdownSource = false;
        UsageLabel = null;
        ClearNotice();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleSourceView() => ShowMarkdownSource = !ShowMarkdownSource;

    private bool CanConfirm() => CanRunAi && HasPreview;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(DraftTitle))
        {
            RejectLocally("Give the document a title before saving it.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DraftMarkdown))
        {
            RejectLocally("The document is empty — there's nothing to save.");
            return;
        }

        // Mirrored server ceiling. /generate costs a unit, so an over-long
        // document is rejected here rather than there.
        if (DraftMarkdown.Length > MaxCharacters)
        {
            RejectLocally($"A document is capped at {MaxCharacters:N0} characters (this one is {DraftMarkdown.Length:N0}). Trim it before saving.");
            return;
        }

        var artifact = AiArtifact.FromPayload(new AiDocumentArtifact
        {
            Title = DraftTitle.Trim(),
            Markdown = DraftMarkdown,
            Outline = DraftOutline.ToList(),
            IsPublic = DraftIsPublic
        });

        var result = await RunAiAsync(
            "Saving your document…",
            token => Session.Api.AiGenerateAsync(AiFeature.PoweredDocument, artifact, ct: token),
            r => r.Quota,
            ct);

        if (result is null) return;

        // /generate's envelope is contract-transcribed, not observed, so the id
        // is a hint — prove the document exists before handing it to the host.
        if (result.Created.DocumentId is { Length: > 0 } documentId)
        {
            var created = await RunApiAsync("Loading the new document…", token => Session.Api.GetDocumentAsync(documentId, token), ct);

            RefreshCommand?.Execute(null);

            if (created is not null)
            {
                Notice = AiNotice.Info($"Saved \"{created.Title}\".");
                Discard();
                IsOpen = false;
                OpenDocumentCommand?.Execute(created);
                return;
            }

            Notice = AiNotice.Info("The document was saved, but loading it back failed. It's in the document list.");
            Discard();
            return;
        }

        RefreshCommand?.Execute(null);
        Notice = new AiNotice(AiNoticeKind.Error,
            "The server accepted the document but didn't return its id. Check the document list before trying again — a second attempt would create a duplicate and spend another AI credit.");
    }
}
