using System.Net.Http;

namespace InterlinedList.Services;

/// <summary>
/// App-lifetime singletons. A full DI container would be overkill for three
/// objects with no per-request scoping needs.
/// </summary>
public static class AppServices
{
    public static InterlinedApiClient Api { get; } = new(new HttpClient { BaseAddress = new Uri(ApiConfig.BaseUrl) });
    public static SessionService Session { get; } = new(Api);
}
