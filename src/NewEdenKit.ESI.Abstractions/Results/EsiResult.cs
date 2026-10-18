using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using NewEdenKit.ESI.Abstractions.Http;

namespace NewEdenKit.ESI.Abstractions.Results;

/// <summary>
/// The outcome of an ESI call. HTTP errors and transport failures are failed results, never exceptions.
/// </summary>
/// <typeparam name="T">The response type.</typeparam>
/// <example>
/// <code>
/// var result = await characters.GetCharactersDetail(characterId).ExecuteAsync(ct);
/// if (result.IsSuccess)
/// {
///     Use(result.Value);                     // non-null when IsSuccess is true
/// }
/// else if (result.Error!.Kind == EsiErrorKind.Http)
/// {
///     // ESI answered with an error: result.StatusCode (e.g. 404), result.Error.Message
/// }
/// else
/// {
///     // Transport (no response) or Deserialization (unreadable body): result.Error.Exception
/// }
/// </code>
/// </example>
public sealed class EsiResult<T>
{
    private EsiResult(HttpStatusCode? statusCode, T? value, EsiError? error, EsiResponseHeaders headers, bool isSuccess)
    {
        StatusCode = statusCode;
        Value = value;
        Error = error;
        Headers = headers;
        IsSuccess = isSuccess;
    }

    /// <summary>Whether the call succeeded and <see cref="Value"/> holds the response.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess { get; }

    /// <summary>ESI answered <c>304 Not Modified</c> and no middleware resolved it to a cached body.</summary>
    public bool IsNotModified => StatusCode == HttpStatusCode.NotModified && !IsSuccess;

    /// <summary>The HTTP status, or <see langword="null"/> when no response was received.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The response value. Set only when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public T? Value { get; }

    /// <summary>Why the call failed. <see langword="null"/> on success and on an unresolved <c>304</c>.</summary>
    public EsiError? Error { get; }

    /// <summary>The response headers: ETag, expiry, pages, error and rate limits, and raw access to the rest.</summary>
    public EsiResponseHeaders Headers { get; }

    /// <summary>Creates a successful result.</summary>
#pragma warning disable CA1000 // Factory members on the generic type read naturally: EsiResult<T>.Success(...).
    public static EsiResult<T> Success(HttpStatusCode statusCode, T value, EsiResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(headers);
        return new EsiResult<T>(statusCode, value, null, headers, isSuccess: true);
    }

    /// <summary>Creates a <c>304 Not Modified</c> result that no middleware resolved.</summary>
    public static EsiResult<T> NotModified(EsiResponseHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return new EsiResult<T>(HttpStatusCode.NotModified, default, null, headers, isSuccess: false);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">Why the call failed.</param>
    /// <param name="statusCode">The HTTP status, or <see langword="null"/> when no response was received.</param>
    /// <param name="headers">The response headers, if there was a response.</param>
    public static EsiResult<T> Failure(EsiError error, HttpStatusCode? statusCode = null, EsiResponseHeaders? headers = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new EsiResult<T>(statusCode, default, error, headers ?? EsiResponseHeaders.Empty, isSuccess: false);
    }
#pragma warning restore CA1000

    /// <inheritdoc />
    public override string ToString()
    {
        var status = StatusCode is { } code ? ((int)code).ToString(CultureInfo.InvariantCulture) : "no response";
        return IsSuccess ? $"Success ({status})"
            : IsNotModified ? "Not modified (304)"
            : $"Failure ({status}): {Error?.Message}";
    }
}
