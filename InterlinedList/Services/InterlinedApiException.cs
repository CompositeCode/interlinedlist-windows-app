namespace InterlinedList.Services;

public sealed class InterlinedApiException : Exception
{
    public int StatusCode { get; }

    public InterlinedApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
