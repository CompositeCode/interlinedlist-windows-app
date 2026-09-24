namespace InterlinedList.Models;

/// <summary>
/// A latitude/longitude pair. Used to pass the *profile* location explicitly
/// into the location-dependent widget calls.
///
/// <para>The app deliberately never asks the OS for a position: the web
/// product's browser-permission flow has no desktop equivalent, and the
/// documented source of truth is the coordinates stored on the user's profile
/// (<c>latitude</c>/<c>longitude</c> on GET /api/user).</para>
/// </summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);
