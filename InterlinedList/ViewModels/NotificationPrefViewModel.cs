using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Item view model for a single notification preference row. Two-way binding on
/// <see cref="Push"/> / <see cref="InApp"/> drives a fire-and-forget PATCH to the
/// API. Reentrancy during the initial ctor assignment is guarded by
/// <c>_loaded</c> so setting the initial channel state doesn't immediately POST.
/// </summary>
public partial class NotificationPrefViewModel : ObservableObject
{
    private readonly SessionService _session;
    private readonly bool _loaded;

    public string Key { get; }
    public string Label { get; }
    public string? Description { get; }

    [ObservableProperty]
    private bool push;

    [ObservableProperty]
    private bool inApp;

    public NotificationPrefViewModel(NotificationPreference pref, SessionService session)
    {
        _session = session;
        Key = pref.Key;
        Label = pref.Label;
        Description = pref.Description;
        push = pref.Channels.Push;
        inApp = pref.Channels.InApp;
        _loaded = true;
    }

    partial void OnPushChanged(bool value) => Persist();

    partial void OnInAppChanged(bool value) => Persist();

    private async void Persist()
    {
        if (!_loaded)
            return;

        try
        {
            await _session.Api.SetNotificationPreferenceAsync(Key, Push, InApp);
        }
        catch (InterlinedApiException)
        {
            // Non-critical — a failed toggle just isn't persisted; swallow so a
            // transient API error doesn't crash on a fire-and-forget path.
        }
    }
}
