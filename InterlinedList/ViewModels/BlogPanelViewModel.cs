using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Services;

namespace InterlinedList.ViewModels;

/// <summary>
/// Blog: read it in the browser, and subscribe to the newsletter.
/// </summary>
/// <remarks>
/// Reading is a handoff because there is no public blog-read endpoint (#121) —
/// <c>/api/blog</c>, <c>/api/blog/posts</c> and <c>/api/blog/list</c> all 404,
/// and authoring lives under admin-only cookie-authed routes. Unsubscribe is
/// also a handoff, because the endpoint needs a token that only the email
/// footer carries.
/// </remarks>
public partial class BlogPanelViewModel : ObservableObject
{
    private readonly SessionService _session;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubscribeCommand))]
    private string email = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubscribeCommand))]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public BlogPanelViewModel(SessionService session)
    {
        _session = session;
        // Pre-fill with the account address — the overwhelmingly likely choice.
        email = session.CurrentUser?.Email ?? "";
    }

    private bool CanSubscribe() => !IsBusy && LooksLikeEmail(Email);

    // Cheap client-side shape check only. The server is authoritative and
    // returns 400 "A valid email is required"; this just avoids an obviously
    // pointless round trip.
    private static bool LooksLikeEmail(string value)
    {
        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@');
        return at > 0
            && at < trimmed.Length - 1
            && trimmed.IndexOf('.', at) > at + 1
            && !trimmed.Contains(' ');
    }

    [RelayCommand(CanExecute = nameof(CanSubscribe))]
    private async Task SubscribeAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _session.Api.SubscribeToBlogAsync(Email.Trim());
            // Double opt-in: say so, or the user will assume they're done.
            StatusMessage = $"Check {Email.Trim()} for a confirmation link — "
                          + "the subscription isn't active until you click it.";
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

    /// <summary>Open the blog in the OS browser — there is no read API to render.</summary>
    [RelayCommand]
    private void OpenBlog() => OpenInBrowser(InterlinedApiClient.BlogUrl);

    private static void OpenInBrowser(string url)
    {
        // UseShellExecute is required for .NET Core+ to hand a URL to the OS
        // default browser — same pattern as the OAuth handoff.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }
}
