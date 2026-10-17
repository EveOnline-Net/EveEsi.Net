using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Results;

namespace EveEsi.Net.Client.Internal;

/// <summary>Turns a pipeline outcome into an <see cref="EsiResult{T}"/>. Never throws for HTTP or JSON problems.</summary>
internal static class EsiResponseDecoder
{
    /// <param name="outcome">The pipeline outcome.</param>
    /// <param name="responseType">The response type info, or <see langword="null"/> for <see cref="EsiNoContent"/>.</param>
    public static EsiResult<T> Decode<T>(EsiCallOutcome outcome, JsonTypeInfo<T>? responseType)
    {
        if (outcome.TransportError is { } transportError)
        {
            return EsiResult<T>.Failure(transportError);
        }

        var response = outcome.Response!;
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return EsiResult<T>.NotModified(response.Headers);
        }

        if (!response.IsSuccessStatusCode)
        {
            return EsiResult<T>.Failure(EsiErrorParser.Parse(response), response.StatusCode, response.Headers);
        }

        if (responseType is null)
        {
            // Only reachable with T = EsiNoContent (the non-generic factory overload).
            return EsiResult<T>.Success(response.StatusCode, default!, response.Headers);
        }

        if (response.Body.IsEmpty)
        {
            return DeserializationFailure<T>(response, "ESI returned an empty body.", null);
        }

        try
        {
            var value = JsonSerializer.Deserialize(response.Body.Span, responseType);
            return value is null
                ? DeserializationFailure<T>(response, "ESI returned null.", null)
                : EsiResult<T>.Success(response.StatusCode, value, response.Headers);
        }
        catch (JsonException ex)
        {
            return DeserializationFailure<T>(response, $"The response couldn't be read as {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    private static EsiResult<T> DeserializationFailure<T>(Abstractions.Http.EsiResponse response, string message, Exception? exception) =>
        EsiResult<T>.Failure(
            new EsiError(EsiErrorKind.Deserialization, message) { Exception = exception },
            response.StatusCode,
            response.Headers);
}
