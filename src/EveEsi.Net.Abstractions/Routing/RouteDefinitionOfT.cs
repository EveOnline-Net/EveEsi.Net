using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Results;

namespace EveEsi.Net.Abstractions.Routing;

/// <summary>
/// An ESI endpoint whose response deserializes to <typeparamref name="T"/>. The factory only accepts a route whose
/// <typeparamref name="T"/> matches the requested result type, so a mismatch doesn't compile.
/// </summary>
/// <typeparam name="T">The response type, or <see cref="EsiNoContent"/> for endpoints that return no body.</typeparam>
public sealed class RouteDefinition<T> : RouteDefinition
{
    /// <summary>Creates a route definition. Generated code uses <c>RouteBuilder</c> rather than calling this directly.</summary>
    /// <param name="operationId">The spec's <c>operationId</c>.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pathTemplate">The path template. Its <c>{placeholders}</c> are the route parameters.</param>
    /// <param name="queryParameters">Query parameter names, excluding pagination and shared headers.</param>
    /// <param name="requiredQueryParameters">The query parameters that must be sent.</param>
    /// <param name="pagination">How the endpoint pages its results.</param>
    /// <param name="requiredScopes">SSO scopes the token must carry; empty for public endpoints.</param>
    /// <param name="requiredRoles">Corporation roles, any one of which is required.</param>
    /// <param name="cache">Cache facts from the spec.</param>
    /// <param name="rateLimit">The rate-limit bucket.</param>
    /// <param name="bodyTypeInfo">Serialization metadata for the request body, if the endpoint takes one.</param>
    /// <param name="isBodyRequired">Whether the body must be supplied.</param>
    /// <param name="responseTypeInfo">
    /// Deserialization metadata for the response, from the source-generated JSON context.
    /// <see langword="null"/> exactly when <typeparamref name="T"/> is <see cref="EsiNoContent"/>.
    /// </param>
    /// <exception cref="ArgumentException">The definition is inconsistent.</exception>
    public RouteDefinition(
        string operationId,
        HttpMethod method,
        string pathTemplate,
        IEnumerable<string>? queryParameters,
        IEnumerable<string>? requiredQueryParameters,
        PaginationStyle pagination,
        IEnumerable<string>? requiredScopes,
        IEnumerable<string>? requiredRoles,
        RouteCacheHints? cache,
        RateLimitPolicy? rateLimit,
        JsonTypeInfo? bodyTypeInfo,
        bool isBodyRequired,
        JsonTypeInfo<T>? responseTypeInfo)
        : base(operationId, method, pathTemplate, queryParameters, requiredQueryParameters, pagination,
            requiredScopes, requiredRoles, cache, rateLimit, bodyTypeInfo, isBodyRequired, typeof(T))
    {
        var isNoContent = typeof(T) == typeof(EsiNoContent);
        if (isNoContent != (responseTypeInfo is null))
        {
            throw new ArgumentException(
                isNoContent
                    ? $"{nameof(EsiNoContent)} routes have no response body, so they take no response type info."
                    : $"Route returning '{typeof(T).Name}' needs response type info.",
                nameof(responseTypeInfo));
        }

        ResponseTypeInfo = responseTypeInfo;
    }

    /// <summary>
    /// Deserialization metadata for the response, or <see langword="null"/> when the endpoint returns no body.
    /// </summary>
    public JsonTypeInfo<T>? ResponseTypeInfo { get; }
}
