using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// The single source of truth for "may this account be shown AI controls at
/// all, and how much of today's quota is left". Fetches
/// <c>GET /api/ai/status</c> once per session, caches it, and exposes
/// <see cref="IsAiAvailable"/> — the one gate every AI affordance in the app
/// binds its visibility to.
///
/// <b>Why this exists as its own singleton rather than a field on
/// <see cref="AppServices"/>:</b> every AI panel needs the same answer and must
/// not each spend a round-trip on it, and adding a member to
/// <see cref="AppServices"/> would conflict with several other open branches
/// that also touch it. <see cref="Shared"/> is created lazily off
/// <see cref="AppServices.Session"/>, so nothing else in the app changes.
///
/// <b>The gate deliberately excludes quota.</b> <see cref="AiStatus.CanUseAi"/>
/// also requires unspent quota, which is the wrong rule for <i>visibility</i>:
/// a subscriber who has used all 50 of today's generations should still see the
/// AI controls with a "you're out until tomorrow" line in place, not watch them
/// vanish. So visibility = <c>Subscriber &amp;&amp; providers non-empty</c>, and
/// quota exhaustion is reported through <see cref="IsQuotaExhausted"/> /
/// <see cref="QuotaLabel"/> instead.
///
/// Verified live 2026-09-16 on the test account:
/// <c>{"subscriber":true,"providers":["anthropic"],"defaultModels":{…},
/// "quota":{"usedToday":5,"dailyLimit":50,"remaining":45}}</c>
///
/// Threading: this is an <see cref="ObservableObject"/> bound directly by
/// views, so mutate it from the UI thread. Every public method here is awaited
/// from a ViewModel command (WPF resumes continuations on the dispatcher), and
/// <see cref="ApplyQuota"/> / <see cref="Reset"/> are synchronous callbacks
/// made from the same place.
/// </summary>
public sealed partial class AiAvailabilityService : ObservableObject
{
    private static readonly Lazy<AiAvailabilityService> LazyShared =
        new(() => new AiAvailabilityService(AppServices.Session));

    /// <summary>The process-wide instance. Panels use this rather than newing one up.</summary>
    public static AiAvailabilityService Shared => LazyShared.Value;

    private readonly SessionService _session;

    /// <summary>
    /// Single-flight guard: several AI panels can be constructed in the same
    /// frame (switching to Lists then Documents), and they must share one
    /// in-flight GET rather than each issuing their own.
    /// </summary>
    private Task<AiStatus?>? _inFlight;

    /// <summary>
    /// Bumped by <see cref="Reset"/> and <see cref="RefreshAsync"/>. A response
    /// that comes back stamped with an older generation is discarded: signing
    /// out (or into another account) while a status GET is in flight must not
    /// let the previous account's subscriber flag land in the cache and open
    /// the gate for someone who isn't entitled to it.
    /// </summary>
    private int _generation;

    public AiAvailabilityService(SessionService session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        // A different account has a different subscriber state and a different
        // quota, so the cache is per-session, not per-process.
        _session.PropertyChanged += OnSessionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    [NotifyPropertyChangedFor(nameof(IsAiAvailable))]
    [NotifyPropertyChangedFor(nameof(Quota))]
    [NotifyPropertyChangedFor(nameof(RemainingToday))]
    [NotifyPropertyChangedFor(nameof(DailyLimit))]
    [NotifyPropertyChangedFor(nameof(QuotaLabel))]
    [NotifyPropertyChangedFor(nameof(IsQuotaExhausted))]
    [NotifyPropertyChangedFor(nameof(DefaultModel))]
    [NotifyPropertyChangedFor(nameof(UnavailableReason))]
    private AiStatus? status;

    /// <summary>True while the one-per-session GET is running.</summary>
    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Set when the status fetch itself failed (offline, 401, 5xx). AI controls
    /// stay hidden in that case — the app can't prove the account is entitled,
    /// and guessing "yes" would show a subscriber-only control to a free
    /// account, which #10 forbids outright.
    /// </summary>
    [ObservableProperty]
    private string? loadError;

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionService.CurrentUser))
            Reset();
    }

    // ── The gate ────────────────────────────────────────────────────────────────

    public bool IsLoaded => Status is not null;

    /// <summary>
    /// <b>The</b> gate. Bind every AI button, tab, panel and menu entry's
    /// visibility to this and nothing else.
    ///
    /// <c>providers: []</c> means the server has no <c>ANTHROPIC_API_KEY</c>, so
    /// a call would come back <c>409 no_provider_configured</c> — there is
    /// nothing a user could do about it, so the control is hidden rather than
    /// shown-and-failing. <c>subscriber: false</c> is a free account, and per
    /// #10 those see no AI controls at all (not a disabled one, not an upsell).
    /// Unknown (not loaded yet, or the fetch failed) is also false: hidden is
    /// the safe default in both directions.
    /// </summary>
    public bool IsAiAvailable => Status is { Subscriber: true } status && status.HasProviderConfigured;

    /// <summary>The model /suggest will run, for the "powered by" byline. Anthropic-only app-wide.</summary>
    public string? DefaultModel => Status?.DefaultModel;

    /// <summary>Why AI isn't offered, when it isn't. Diagnostic only — never shown to a free account.</summary>
    public string? UnavailableReason => Status is null
        ? LoadError ?? "AI status hasn't loaded yet."
        : Status.UnavailableReason;

    // ── Quota surfacing ─────────────────────────────────────────────────────────

    public AiQuota? Quota => Status?.Quota;

    public int RemainingToday => Status?.Quota.RemainingOrComputed ?? 0;

    public int DailyLimit => Status?.Quota.DailyLimit ?? AiFeatureLimits.DailyGenerationLimit;

    /// <summary>"45 of 50 left today" — the line #10 asks to appear wherever an AI action is offered.</summary>
    public string QuotaLabel => Status is null
        ? "Checking AI quota…"
        : $"{RemainingToday} of {DailyLimit} left today";

    public bool IsQuotaExhausted => Status?.Quota.IsExhausted ?? false;

    // ── Loading ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch the status if it hasn't been fetched for this session yet.
    /// Idempotent and safe to call from every panel's constructor: concurrent
    /// callers await the same request, and a completed one is a no-op.
    /// </summary>
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (Status is not null) return Task.CompletedTask;
        return _inFlight ??= LoadAsync(_generation, ct);
    }

    /// <summary>
    /// Re-fetch on demand — after a subscription change, or to reconcile the
    /// quota against the server (a /suggest echoes usedToday but omits
    /// <c>remaining</c>, so a long session's computed figure can drift if the
    /// same account is used elsewhere).
    /// </summary>
    public Task RefreshAsync(CancellationToken ct = default)
        => _inFlight = LoadAsync(++_generation, ct);

    private async Task<AiStatus?> LoadAsync(int generation, CancellationToken ct)
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var status = await _session.Api.GetAiStatusAsync(ct);

            // Superseded by a Reset (sign-out / account switch) or a newer
            // Refresh while we were waiting — drop it on the floor.
            if (generation != _generation) return null;

            Status = status;
            return status;
        }
        catch (AiApiException ex)
        {
            // 401 here means the token is gone; anything else means the server
            // couldn't answer. Either way AI stays hidden.
            if (generation == _generation) LoadError = ex.UserMessage;
            AppLog.Warn($"GET /api/ai/status failed ({ex.StatusCode} {ex.RawCode}): {ex.Message}");
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            if (generation == _generation) LoadError = "Couldn't check whether AI is available.";
            AppLog.Error("GET /api/ai/status failed.", ex);
            return null;
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                // Let the next EnsureLoadedAsync retry a failed fetch instead
                // of caching the failure for the rest of the session.
                if (Status is null) _inFlight = null;
            }
        }
    }

    // ── Quota bookkeeping ───────────────────────────────────────────────────────

    /// <summary>
    /// Fold the quota a /suggest or /generate echoed back into the cached
    /// status, so the chip counts down without another GET.
    ///
    /// <b>Shape note:</b> the echoed object has <c>usedToday</c> and
    /// <c>dailyLimit</c> but <b>no <c>remaining</c></b> (verified live, twice),
    /// so this stores <see cref="AiQuota.RemainingOrComputed"/> into
    /// <see cref="AiQuota.Remaining"/> — keeping the cached status shaped like a
    /// /status response and letting the UI keep binding one property.
    /// </summary>
    public void ApplyQuota(AiQuota? quota)
    {
        if (quota is null || Status is not { } status) return;

        var limit = quota.DailyLimit > 0 ? quota.DailyLimit : status.Quota.DailyLimit;

        Status = new AiStatus
        {
            Subscriber = status.Subscriber,
            Providers = status.Providers,
            DefaultModels = status.DefaultModels,
            Quota = new AiQuota
            {
                UsedToday = quota.UsedToday,
                DailyLimit = limit,
                Remaining = Math.Max(0, limit - quota.UsedToday)
            }
        };
    }

    /// <summary>
    /// A <c>429 quota_exceeded</c> arrived, so today's allowance is gone
    /// whatever the cached figure said — reflect that immediately instead of
    /// leaving a stale "3 left today" next to a "you're out" message.
    /// </summary>
    public void MarkQuotaExhausted()
    {
        if (Status is not { } status) return;

        var limit = status.Quota.DailyLimit > 0 ? status.Quota.DailyLimit : AiFeatureLimits.DailyGenerationLimit;

        Status = new AiStatus
        {
            Subscriber = status.Subscriber,
            Providers = status.Providers,
            DefaultModels = status.DefaultModels,
            Quota = new AiQuota { UsedToday = limit, DailyLimit = limit, Remaining = 0 }
        };
    }

    /// <summary>
    /// Drop the cache (sign-out, or a switch to another account). Bumping the
    /// generation makes any in-flight GET discard its own result, so a response
    /// for the previous account can't repopulate the cache afterwards.
    /// </summary>
    public void Reset()
    {
        _generation++;
        _inFlight = null;
        IsLoading = false;
        LoadError = null;
        Status = null;
    }
}
