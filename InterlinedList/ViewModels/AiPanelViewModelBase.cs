using System.ComponentModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// What every AI panel in this app shares: the one availability gate, the
/// remaining-quota line, a single in-place <see cref="Notice"/> instead of any
/// dialog, and one funnel that all <c>/api/ai/*</c> traffic goes through.
///
/// <b>The funnel is the point.</b> <see cref="RunAiAsync"/> is the only place
/// AI calls are made from, which is what makes three otherwise easy-to-forget
/// rules structural rather than aspirational:
///
/// <list type="number">
/// <item>It <b>never retries</b>. A failed call that reached the model already
/// spent one of the 50 daily generations, so an automatic retry silently
/// doubles the bill. The user re-asks, or nobody does.</item>
/// <item>It refuses to start at all when <see cref="IsAiAvailable"/> is false,
/// so a stale bound button can't fire a call that would answer
/// <c>403 subscription_required</c> or <c>409 no_provider_configured</c>.</item>
/// <item>It folds the echoed quota back into
/// <see cref="AiAvailabilityService"/> on success <i>and</i> marks the
/// allowance gone on <c>quota_exceeded</c>, so the chip never disagrees with
/// the message next to it.</item>
/// </list>
///
/// Subclasses do the feature-specific pre-flight (word caps, required source
/// references) before calling in — every rejection caught there is a quota unit
/// that was never spent, which is the entire reason the caps are mirrored
/// client-side.
/// </summary>
public abstract partial class AiPanelViewModelBase : ObservableObject
{
    protected SessionService Session { get; }

    /// <summary>The shared gate. Views bind visibility to <c>Availability.IsAiAvailable</c>.</summary>
    public AiAvailabilityService Availability { get; }

    protected AiPanelViewModelBase(SessionService session, AiAvailabilityService? availability = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Availability = availability ?? AiAvailabilityService.Shared;

        // Re-raise the gate and the quota line as our own properties so a panel's
        // XAML can bind them without reaching through two DataContext levels.
        Availability.PropertyChanged += OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AiAvailabilityService.Status):
            case nameof(AiAvailabilityService.LoadError):
                OnPropertyChanged(nameof(IsAiAvailable));
                OnPropertyChanged(nameof(QuotaLabel));
                OnPropertyChanged(nameof(IsQuotaExhausted));
                OnPropertyChanged(nameof(CanRunAi));
                NotifyAiCommandsChanged();
                break;
        }
    }

    /// <summary>The gate — see <see cref="AiAvailabilityService.IsAiAvailable"/>.</summary>
    public bool IsAiAvailable => Availability.IsAiAvailable;

    /// <summary>"45 of 50 left today".</summary>
    public string QuotaLabel => Availability.QuotaLabel;

    public bool IsQuotaExhausted => Availability.IsQuotaExhausted;

    /// <summary>
    /// True when a call is worth attempting: available, nothing already in
    /// flight, and today's allowance isn't spent. Subclasses AND this with their
    /// own input checks in their commands' CanExecute.
    /// </summary>
    public bool CanRunAi => IsAiAvailable && !IsBusy && !IsQuotaExhausted;

    /// <summary>A /suggest or /generate is in flight. Drives the amber pending state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAi))]
    private bool isBusy;

    /// <summary>What the panel is doing right now, for the pending line ("Drafting your list…").</summary>
    [ObservableProperty]
    private string? busyText;

    /// <summary>
    /// The single in-place message slot. Non-blocking by construction: it's a
    /// bound string, not a dialog.
    /// </summary>
    [ObservableProperty]
    private AiNotice? notice;

    partial void OnIsBusyChanged(bool value) => NotifyAiCommandsChanged();

    /// <summary>
    /// Raise CanExecuteChanged for whichever commands depend on
    /// <see cref="CanRunAi"/>. Subclasses override because the generated
    /// <c>…Command</c> properties only exist on them.
    /// </summary>
    protected virtual void NotifyAiCommandsChanged() { }

    /// <summary>Called from a view's constructor; safe to call repeatedly.</summary>
    public Task EnsureStatusLoadedAsync() => Availability.EnsureLoadedAsync();

    protected void ClearNotice() => Notice = null;

    /// <summary>
    /// Fail a request locally, with the same code the server would have used,
    /// without spending a unit. This is what a pre-flight rejection looks like.
    /// </summary>
    protected void RejectLocally(string message) => Notice = AiNotice.Input(message);

    /// <summary>
    /// Run one AI call. Returns null on any failure, having already written the
    /// right in-place <see cref="Notice"/>. Does not retry, ever.
    /// </summary>
    /// <param name="busyText">Pending copy while it runs.</param>
    /// <param name="call">The single <c>/api/ai/*</c> call to make.</param>
    /// <param name="quotaOf">
    /// Pulls the echoed quota out of the result so the chip can count down.
    /// Note the echoed object has no <c>remaining</c> field — that's handled in
    /// <see cref="AiAvailabilityService.ApplyQuota"/>.
    /// </param>
    protected async Task<T?> RunAiAsync<T>(
        string busyText,
        Func<CancellationToken, Task<T>> call,
        Func<T, AiQuota?>? quotaOf = null,
        CancellationToken ct = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(call);

        // Rule 2: a stale binding must not be able to spend anything.
        if (!IsAiAvailable)
        {
            Notice = new AiNotice(AiNoticeKind.Error,
                Availability.UnavailableReason ?? "AI isn't available on this account.");
            return null;
        }

        if (IsBusy) return null;

        IsBusy = true;
        BusyText = busyText;
        Notice = null;

        try
        {
            var result = await call(ct);

            if (quotaOf is not null)
                Availability.ApplyQuota(quotaOf(result));

            return result;
        }
        catch (AiApiException ex)
        {
            Notice = AiNotice.From(ex);

            // Keep the chip honest: a 429 quota_exceeded is the server saying
            // the allowance is gone regardless of what we had cached.
            if (ex.Code == AiErrorCode.QuotaExceeded)
                Availability.MarkQuotaExhausted();

            // A 401 means the token died mid-session; the gate should stop
            // claiming AI is available until it's re-checked.
            if (ex.Code == AiErrorCode.Unauthorized)
                Availability.Reset();

            AppLog.Warn($"AI call failed ({ex.StatusCode} {ex.RawCode}): {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            // A cancelled call may or may not have reached the model — say
            // nothing rather than guess.
            return null;
        }
        catch (HttpRequestException ex)
        {
            Notice = new AiNotice(AiNoticeKind.Error, "Couldn't reach InterlinedList. Check your connection and try again.");
            AppLog.Warn($"AI call transport failure: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Notice = new AiNotice(AiNoticeKind.Error, "The AI request failed unexpectedly.");
            AppLog.Error("Unexpected failure during an AI call.", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>
    /// Run a plain (non-AI) follow-up call — the read-after-write GET that
    /// confirms what <c>/generate</c> actually created. Separate from
    /// <see cref="RunAiAsync"/> because it spends no quota and throws the
    /// ordinary exception type.
    /// </summary>
    protected async Task<T?> RunApiAsync<T>(string busyText, Func<CancellationToken, Task<T>> call, CancellationToken ct = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(call);

        IsBusy = true;
        BusyText = busyText;
        try
        {
            return await call(ct);
        }
        catch (InterlinedApiException ex)
        {
            Notice = new AiNotice(AiNoticeKind.Error, ex.Message);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Notice = new AiNotice(AiNoticeKind.Error, "The request failed unexpectedly.");
            AppLog.Error("Unexpected failure during an AI follow-up call.", ex);
            return null;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }
}
