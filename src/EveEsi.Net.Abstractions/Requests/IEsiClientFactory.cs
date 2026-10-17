using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>
/// Builds requests for any endpoint. Generated services call this; advanced callers can too, with any route.
/// </summary>
public interface IEsiClientFactory
{
    /// <summary>Builds and validates a request.</summary>
    /// <typeparam name="T">The response type. Must match <paramref name="endpoint"/>'s, so write it explicitly.</typeparam>
    /// <param name="endpoint">The endpoint to call.</param>
    /// <param name="configure">Sets route and query values, the body and pagination.</param>
    /// <param name="token">The SSO access token. Required when the endpoint requires scopes.</param>
    /// <param name="serviceMiddleware">
    /// A snapshot of the calling service's middleware. It runs after global and before request middleware.
    /// </param>
    /// <returns>The request, ready for request middleware and execution.</returns>
    /// <exception cref="ArgumentException">
    /// A required route or query value or the body is missing, a value is set for a parameter the endpoint doesn't
    /// have, a body is set on an endpoint that takes none, or <paramref name="token"/> is missing on a secured endpoint.
    /// </exception>
    /// <exception cref="InvalidOperationException">The pagination type doesn't match the endpoint's <see cref="PaginationStyle"/>.</exception>
    IEsiRequest<T> CreateEsiClient<T>(
        RouteDefinition<T> endpoint,
        Action<IEsiRequestParameters>? configure = null,
        string? token = null,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware = null);
}
