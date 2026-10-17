using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Results;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>
/// Builds requests for any endpoint. Generated services call this; advanced callers can too, with any route.
/// The route describes the endpoint; the caller says what type the response decodes to.
/// </summary>
public interface IEsiClientFactory
{
    /// <summary>Builds and validates a request for an endpoint that returns a body.</summary>
    /// <typeparam name="T">The response type. Write it explicitly; it must match <paramref name="responseType"/>.</typeparam>
    /// <param name="endpoint">The endpoint to call.</param>
    /// <param name="responseType">Deserialization metadata for the response, from the source-generated JSON context.</param>
    /// <param name="configure">Sets route and query values, the body and pagination; <see langword="null"/> for none.</param>
    /// <param name="token">The SSO access token, or <see langword="null"/>. Required when the endpoint requires scopes.</param>
    /// <param name="serviceMiddleware">
    /// A snapshot of the calling service's middleware, or <see langword="null"/>. It runs after global and before request middleware.
    /// </param>
    /// <returns>The request, ready for request middleware and execution.</returns>
    /// <exception cref="ArgumentException">
    /// A required route or query value or the body is missing, a value is set for a parameter the endpoint doesn't
    /// have, a body is set on an endpoint that takes none, or <paramref name="token"/> is missing on a secured endpoint.
    /// </exception>
    /// <exception cref="InvalidOperationException">The pagination type doesn't match the endpoint's <see cref="PaginationStyle"/>.</exception>
    IEsiRequest<T> CreateEsiClient<T>(
        RouteDefinition endpoint,
        JsonTypeInfo<T> responseType,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware);

    /// <summary>Builds and validates a request for an endpoint that returns no body (e.g. <c>204 No Content</c>).</summary>
    /// <param name="endpoint">The endpoint to call.</param>
    /// <param name="configure">Sets route and query values, the body and pagination; <see langword="null"/> for none.</param>
    /// <param name="token">The SSO access token, or <see langword="null"/>. Required when the endpoint requires scopes.</param>
    /// <param name="serviceMiddleware">
    /// A snapshot of the calling service's middleware, or <see langword="null"/>. It runs after global and before request middleware.
    /// </param>
    /// <returns>The request, ready for request middleware and execution.</returns>
    /// <exception cref="ArgumentException">See <see cref="CreateEsiClient{T}"/>.</exception>
    /// <exception cref="InvalidOperationException">See <see cref="CreateEsiClient{T}"/>.</exception>
    IEsiRequest<EsiNoContent> CreateEsiClient(
        RouteDefinition endpoint,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware);
}
