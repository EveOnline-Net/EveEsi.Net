using System.Text.Json.Serialization.Metadata;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Results;

namespace NewEdenKit.ESI.Abstractions.Requests;

/// <summary>
/// Builds requests by operation ID. Generated services call this; advanced callers can too.
/// The factory looks the route up in the route table; the caller says what type the response decodes to.
/// </summary>
public interface IEsiClientFactory
{
    /// <summary>Builds a request for an endpoint that returns a body.</summary>
    /// <typeparam name="T">The response type. Write it explicitly; it must match <paramref name="responseType"/>.</typeparam>
    /// <param name="operationId">The spec's <c>operationId</c>, e.g. <c>GetCharactersCharacterIdAssets</c>.</param>
    /// <param name="responseType">Deserialization metadata for the response, from the source-generated JSON context.</param>
    /// <param name="configure">Sets route and query values, the body and pagination; <see langword="null"/> for none.</param>
    /// <param name="token">The SSO access token, or <see langword="null"/>. Required when the endpoint has a scope.</param>
    /// <param name="serviceMiddleware">
    /// A snapshot of the calling service's middleware, or <see langword="null"/>. It runs after global and before request middleware.
    /// </param>
    /// <returns>The request, ready for request middleware and execution.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="operationId"/> isn't in the route table, or <paramref name="token"/> is missing on an endpoint
    /// that has a scope.
    /// </exception>
    IEsiRequest<T> CreateEsiClient<T>(
        string operationId,
        JsonTypeInfo<T> responseType,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware);

    /// <summary>Builds a request for an endpoint that returns no body (e.g. <c>204 No Content</c>).</summary>
    /// <param name="operationId">The spec's <c>operationId</c>.</param>
    /// <param name="configure">Sets route and query values, the body and pagination; <see langword="null"/> for none.</param>
    /// <param name="token">The SSO access token, or <see langword="null"/>. Required when the endpoint has a scope.</param>
    /// <param name="serviceMiddleware">
    /// A snapshot of the calling service's middleware, or <see langword="null"/>. It runs after global and before request middleware.
    /// </param>
    /// <returns>The request, ready for request middleware and execution.</returns>
    /// <exception cref="ArgumentException">See <see cref="CreateEsiClient{T}"/>.</exception>
    IEsiRequest<EsiNoContent> CreateEsiClient(
        string operationId,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware);
}
