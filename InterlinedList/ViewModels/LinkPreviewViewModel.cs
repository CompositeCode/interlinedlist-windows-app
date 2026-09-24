using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InterlinedList.Models;

namespace InterlinedList.ViewModels;

/// <summary>
/// One unfurled link, as a card. Built from the <c>linkMetadata</c> that arrives
/// inline on a feed message, or from the ad-hoc
/// <c>GET /api/link-metadata?url=</c> used for the compose-time preview.
/// </summary>
/// <remarks>
/// Only construct this for a renderable link (see
/// <see cref="LinkMetadataExtensions.IsRenderable"/>) — a failed unfurl carries
/// no metadata at all and would draw an empty card.
/// </remarks>
public partial class LinkPreviewViewModel : ObservableObject
{
    private BitmapImage? _thumbnail;
    private bool _thumbnailRequested;

    public string? Url { get; }
    public string? Title { get; }
    public string? Description { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// The host, as the card's footer line — <c>youtu.be</c>, <c>jmap.io</c>. The
    /// server's own <c>platform</c> ("youtube", "other", "instagram") is too
    /// coarse to show: 22 of 42 sampled links were just "other".
    /// </summary>
    public string? Host { get; }

    private readonly string? _thumbnailUrl;

    /// <summary>
    /// True until a thumbnail is known to be unusable. Flips to false if the
    /// download or decode fails, which collapses the image and leaves a
    /// text-only card rather than a hole where the picture should be.
    /// </summary>
    [ObservableProperty]
    private bool hasThumbnail;

    public LinkPreviewViewModel(LinkPreview link)
    {
        Url = link.Url;
        Title = link.Metadata?.Title;
        Description = link.Metadata?.Description;
        _thumbnailUrl = link.Metadata?.Thumbnail;
        hasThumbnail = !string.IsNullOrWhiteSpace(_thumbnailUrl);

        Host = Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
            ? uri.Host
            : link.Platform;

        // Nothing usable to label the card with? Fall back to the raw URL so the
        // card is never blank. (No sampled success case lacked a title, but the
        // shape allows it.)
        if (string.IsNullOrWhiteSpace(Title))
            Title = link.Url;
    }

    /// <summary>
    /// The decoded thumbnail, created on first binding evaluation — i.e. when the
    /// card is actually realized by the virtualizing list, not when the feed page
    /// is parsed. <c>DelayCreation</c> defers the decode further, until render.
    /// Remote URIs download off the UI thread; a failure flips
    /// <see cref="HasThumbnail"/> instead of leaving a gap.
    /// </summary>
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailRequested) return _thumbnail;
            _thumbnailRequested = true;

            if (string.IsNullOrWhiteSpace(_thumbnailUrl) ||
                !Uri.TryCreate(_thumbnailUrl, UriKind.Absolute, out var uri))
            {
                HasThumbnail = false;
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                // OnDemand (the default) rather than OnLoad: OnLoad forces the
                // decode at EndInit and would defeat DelayCreation.
                bitmap.CreateOptions = BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = 320; // the card never shows it larger
                bitmap.UriSource = uri;
                bitmap.EndInit();

                bitmap.DownloadFailed += (_, _) => HasThumbnail = false;
                bitmap.DecodeFailed += (_, _) => HasThumbnail = false;

                _thumbnail = bitmap;
            }
            catch (Exception)
            {
                // A malformed image or an unsupported scheme throws from EndInit;
                // degrade to the text-only card rather than taking down the feed.
                HasThumbnail = false;
            }

            return _thumbnail;
        }
    }

    [RelayCommand]
    private void Open()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // No handler registered for the scheme, or the shell refused. Not
            // worth an error banner on a decorative card.
        }
    }
}
