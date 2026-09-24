using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Shared behaviour for every right-rail widget: collapsible, individually
/// refreshable, time-cached, and — most importantly — unable to take the shell
/// down with it.
///
/// <para><b>Failure isolation is the point of this class.</b> All five widget
/// endpoints proxy third-party services (Hacker News, CoinGecko, a transit
/// feed, GBFS), so they fail in ways this app cannot prevent: a live 502 was
/// observed from the markets endpoint, and the transit endpoint returns HTTP
/// 200 with <c>"error":"unavailable"</c> in the body. <see cref="LoadAsync"/>
/// therefore catches <i>every</i> exception — not just
/// <see cref="InterlinedApiException"/>, which would let a socket timeout or a
/// JSON surprise escape — and turns it into a quiet
/// <see cref="Placeholder"/> line. A widget never raises a dialog, never
/// rethrows, and never leaves a spinner running.</para>
///
/// <para>A failed refresh keeps whatever content was last loaded rather than
/// blanking the card, so a transient upstream blip is invisible.</para>
/// </summary>
public abstract partial class WidgetViewModel : ObservableObject
{
    protected WidgetViewModel(SessionService session) => Session = session;

    protected SessionService Session { get; }

    private DateTimeOffset? _loadedAt;

    /// <summary>Card title shown in the rail header.</summary>
    public abstract string Title { get; }

    /// <summary>
    /// How long a successful load stays fresh. Keeps the rail from re-hitting
    /// these proxied endpoints on every navigation switch or expand/collapse.
    /// </summary>
    protected abstract TimeSpan CacheFor { get; }

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// The quiet degraded state: an explanatory line shown instead of content.
    /// Non-null for "nothing to show", "you have not set a location", and
    /// "upstream is unavailable" alike — a widget deliberately does not
    /// distinguish a third party's outage from emptiness in the UI.
    /// </summary>
    [ObservableProperty]
    private string? placeholder;

    /// <summary>True once real content has been loaded at least once.</summary>
    [ObservableProperty]
    private bool hasContent;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private Task RefreshAsync(CancellationToken ct) => LoadAsync(force: true, ct);

    /// <summary>
    /// Loads the widget, honouring <see cref="CacheFor"/> unless
    /// <paramref name="force"/> is set. Never throws.
    /// </summary>
    public async Task LoadAsync(bool force = false, CancellationToken ct = default)
    {
        if (IsLoading) return;

        if (!force && _loadedAt is { } last && DateTimeOffset.UtcNow - last < CacheFor)
            return;

        IsLoading = true;
        try
        {
            await LoadCoreAsync(ct);
            _loadedAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Window closing or a superseded refresh — leave the card as it was.
        }
        catch (Exception ex)
        {
            // Deliberately broad: see the class remarks. A widget's upstream
            // failing is a cosmetic non-event, so it is logged for
            // diagnosability and then swallowed.
            AppLog.Warn($"Widget '{Title}' failed to load: {ex.GetType().Name}: {ex.Message}");

            // Keep the last good content if there is any; only an empty card
            // needs to explain itself.
            if (!HasContent)
                Placeholder = "Unavailable right now.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Fetch and project. Implementations set <see cref="HasContent"/> and
    /// <see cref="Placeholder"/>; they should let exceptions propagate to
    /// <see cref="LoadAsync"/> rather than catching them.
    /// </summary>
    protected abstract Task LoadCoreAsync(CancellationToken ct);

    /// <summary>Records "loaded fine, but there is nothing to display".</summary>
    protected void ShowPlaceholder(string message)
    {
        HasContent = false;
        Placeholder = message;
    }

    /// <summary>Records "loaded fine and there is content".</summary>
    protected void ShowContent()
    {
        HasContent = true;
        Placeholder = null;
    }
}
