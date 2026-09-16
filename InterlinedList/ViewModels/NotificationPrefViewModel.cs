using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// One notification-event row: a label and a toggle per channel the event
/// actually supports.
/// </summary>
/// <remarks>
/// The channel list is built from the server payload rather than hard-coded.
/// A fixed Push/In-app pair silently broke three of the eight events: the server
/// rejects an unsupported channel with <c>400</c>, so toggling <c>follow</c>,
/// <c>reply</c> or <c>share</c> never persisted, and <c>reply</c> (email only)
/// rendered with no toggles at all.
/// </remarks>
public partial class NotificationPrefViewModel : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string? Description { get; }

    /// <summary>One toggle per supported channel, in canonical order.</summary>
    public ObservableCollection<NotificationChannelViewModel> Channels { get; } = new();

    /// <summary>Surfaced when a toggle fails, so a rejection isn't invisible.</summary>
    [ObservableProperty]
    private string? statusMessage;

    public NotificationPrefViewModel(NotificationPreference pref, SessionService session)
    {
        Key = pref.Key;
        Label = pref.Label;
        Description = pref.Description;

        foreach (var channel in pref.Channels.SupportedChannels)
        {
            Channels.Add(new NotificationChannelViewModel(
                pref.Key,
                channel,
                pref.Channels.IsEnabled(channel),
                session,
                onError: msg => StatusMessage = msg));
        }
    }
}

/// <summary>
/// A single channel toggle. Two-way binding on <see cref="Enabled"/> PATCHes
/// just this channel — the endpoint merges, so the other channels are untouched.
/// </summary>
public partial class NotificationChannelViewModel : ObservableObject
{
    private readonly string _eventKey;
    private readonly SessionService _session;
    private readonly Action<string?> _onError;

    /// <summary>
    /// Suppresses the PATCH while the property is being set programmatically —
    /// the initial load, and the revert after a failed write.
    /// </summary>
    private bool _suppress = true;

    public string Channel { get; }
    public string Label { get; }

    /// <summary>
    /// Explains a channel this desktop client cannot deliver itself (push is
    /// iOS/Android only — see <see cref="NotificationChannels.DeliveryNoteFor"/>).
    /// Null for channels that do apply here.
    /// </summary>
    public string? DeliveryNote { get; }

    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private bool isBusy;

    public NotificationChannelViewModel(
        string eventKey, string channel, bool enabled,
        SessionService session, Action<string?> onError)
    {
        _eventKey = eventKey;
        Channel = channel;
        Label = NotificationChannels.LabelFor(channel);
        DeliveryNote = NotificationChannels.DeliveryNoteFor(channel);
        _session = session;
        _onError = onError;
        this.enabled = enabled;
        _suppress = false;
    }

    partial void OnEnabledChanged(bool value) => Persist(value);

    private async void Persist(bool value)
    {
        if (_suppress || IsBusy) return;

        IsBusy = true;
        _onError(null);
        try
        {
            await _session.Api.SetNotificationChannelAsync(_eventKey, Channel, value);
        }
        catch (Exception ex)
        {
            // This used to be swallowed, which is exactly how three of the eight
            // events could appear to toggle while never persisting. Put the
            // control back to the server's actual state and say what happened.
            // The server's message here is already specific and useful
            // ("Channel 'inApp' is not supported for event 'follow'"), so show
            // it verbatim. Once #130 lands, route this through
            // ApiErrorPresenter.Describe for consistency with the other views.
            _onError(ex is InterlinedApiException api ? api.Message : "Couldn't save that change.");
            _suppress = true;
            try { Enabled = !value; }
            finally { _suppress = false; }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
