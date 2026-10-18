using System.Net;

namespace NewEdenKit.ESI.Abstractions.Http;

/// <summary>
/// A raw ESI response: status, headers and body bytes. Middleware can inspect it after <c>next</c>, replace it, or
/// set one without calling <c>next</c> to short-circuit. Deserialization happens after the pipeline.
/// </summary>
public sealed class EsiResponse
{
    /// <summary>Creates a raw response.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="headers">The response and content headers.</param>
    /// <param name="body">The body bytes; empty when there is no body.</param>
    public EsiResponse(HttpStatusCode statusCode, EsiResponseHeaders headers, ReadOnlyMemory<byte> body)
    {
        ArgumentNullException.ThrowIfNull(headers);
        StatusCode = statusCode;
        Headers = headers;
        Body = body;
    }

    /// <summary>The HTTP status.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The response and content headers.</summary>
    public EsiResponseHeaders Headers { get; }

    /// <summary>The body bytes; empty when there is no body.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>Whether the status is in the 2xx range.</summary>
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}
