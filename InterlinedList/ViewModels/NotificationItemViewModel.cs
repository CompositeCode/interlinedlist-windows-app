using CommunityToolkit.Mvvm.ComponentModel;
using InterlinedList.Models;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// One notification row. Knows where it points, so clicking it goes somewhere
/// instead of being inert.
/// </summary>
public partial class NotificationItemViewModel : ObservableObject
{
    private readonly NotificationDestination _destination;
    private readonly string? _messageId;
    private readonly string? _listId;
    private readonly string? _orgId;

    public string Id { get; }
    public string Title { get; }
    public string Body { get; }
    public string TimeFormatted { get; }
    public string? ActionUrl { get; }
    public string? Type { get; }

    /// <summary>False when the destination is unrecognized — the row stays inert.</summary>
    public bool IsNavigable { get; }

    /// <summary>What clicking will do, for a tooltip.</summary>
    public string? NavigationHint => _destination switch
    {
        NotificationDestination.Message => "Open this message",
        NotificationDestination.List => "Open this list",
        NotificationDestination.Organization => "Open this organization",
        NotificationDestination.ConnectedAccounts => "Open Connected Accounts",
        _ => null,
    };

    [ObservableProperty]
    private bool isUnread;

    public NotificationItemViewModel(NotificationItem item)
    {
        Id = item.Id;
        Title = item.Title;
        Body = item.Body;
        TimeFormatted = item.TimeFormatted;
        ActionUrl = item.ActionUrl;
        Type = item.Type;

        // Routing comes from the structured `target`, not from parsing
        // `routePath` — that is a web URL and parsing it would couple this app
        // to the website's URL scheme.
        _destination = item.Destination;
        _messageId = item.Target?.MessageId;
        _listId = item.Target?.ListId;
        _orgId = item.Target?.OrgId;
        IsNavigable = item.IsNavigable;

        isUnread = item.IsUnread;
    }

    public void MarkRead() => IsUnread = false;

    /// <summary>
    /// Go to whatever this notification is about. A no-op for an unrecognized
    /// destination rather than a throw.
    /// </summary>
    public void Navigate()
    {
        switch (_destination)
        {
            case NotificationDestination.Message when _messageId is { Length: > 0 }:
                Navigator.OpenMessage(_messageId);
                break;
            case NotificationDestination.List when _listId is { Length: > 0 }:
                Navigator.OpenList(_listId);
                break;
            case NotificationDestination.Organization when _orgId is { Length: > 0 }:
                Navigator.OpenOrganization(_orgId);
                break;
            case NotificationDestination.ConnectedAccounts:
                Navigator.OpenConnectedAccounts();
                break;
        }
    }
}
