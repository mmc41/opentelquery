namespace OpenTel.Query.Core.Abstractions;

public sealed class TelemetryBackendException : Exception
{
    public string BackendName { get; }
    public int StatusCode { get; }
    public string ReasonPhrase { get; }
    public string ResponseBody { get; }
    public string? Hint { get; }

    public TelemetryBackendException(string backendName, int statusCode, string reasonPhrase, string body, string? hint)
        : base($"{backendName} returned HTTP {statusCode} {reasonPhrase}.")
    {
        BackendName = backendName;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ResponseBody = body;
        Hint = hint;
    }
}
