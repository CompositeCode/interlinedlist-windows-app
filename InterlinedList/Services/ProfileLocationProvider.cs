using InterlinedList.Models;

namespace InterlinedList.Services;

/// <summary>
/// One shared, cached read of the signed-in user's profile coordinates, so the
/// several location-dependent widgets don't each issue their own GET /api/user.
///
/// <para>The app never asks the OS where it is. The web product gates
/// geolocation behind a browser permission prompt that has no desktop
/// equivalent, and the documented source is the location stored on the
/// profile — so "no coordinates on the profile" is a normal state the UI
/// explains, not a failure to work around.</para>
/// </summary>
public sealed class ProfileLocationProvider
{
    private readonly SessionService _session;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private GeoPoint? _cached;
    private bool _loaded;

    public ProfileLocationProvider(SessionService session) => _session = session;

    /// <summary>
    /// The profile coordinates, or null when the user has not set a location.
    /// Cached for the lifetime of the provider; pass
    /// <paramref name="force"/> to re-read after the user edits Settings.
    /// </summary>
    public async Task<GeoPoint?> GetAsync(bool force = false, CancellationToken ct = default)
    {
        if (_loaded && !force) return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_loaded && !force) return _cached;

            _cached = await _session.Api.GetProfileLocationAsync(ct);
            _loaded = true;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
