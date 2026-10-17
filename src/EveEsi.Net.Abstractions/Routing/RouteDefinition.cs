using System.Collections.ObjectModel;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace EveEsi.Net.Abstractions.Routing;

/// <summary>
/// Static, immutable description of one ESI endpoint. Middleware reads endpoint facts (scopes, roles, cache hints,
/// rate-limit group, pagination) from here, never from the path.
/// </summary>
/// <remarks>Endpoints are generated once into the <c>Endpoints</c> catalogue. See <see cref="RouteDefinition{T}"/>.</remarks>
public abstract partial class RouteDefinition
{
    // Only RouteDefinition<T> may derive, so every route carries its response type.
    private protected RouteDefinition(
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
        Type responseType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);

        if (!pathTemplate.StartsWith('/'))
        {
            throw new ArgumentException($"Path template '{pathTemplate}' must start with '/'.", nameof(pathTemplate));
        }

        var routeParameters = PlaceholderPattern().Matches(pathTemplate).Select(m => m.Groups[1].Value).ToArray();
        RequireUnique(routeParameters, nameof(pathTemplate));

        var query = (queryParameters ?? []).ToArray();
        RequireUnique(query, nameof(queryParameters));

        var requiredQuery = (requiredQueryParameters ?? []).ToArray();
        RequireUnique(requiredQuery, nameof(requiredQueryParameters));
        var undeclared = requiredQuery.Except(query, StringComparer.Ordinal).ToArray();
        if (undeclared.Length > 0)
        {
            throw new ArgumentException(
                $"Required query parameters [{string.Join(", ", undeclared)}] aren't in the query parameter list.",
                nameof(requiredQueryParameters));
        }

        if (bodyTypeInfo is null && isBodyRequired)
        {
            throw new ArgumentException("A required body needs a body type.", nameof(isBodyRequired));
        }

        OperationId = operationId;
        Method = method;
        PathTemplate = pathTemplate;
        RouteParameters = new ReadOnlyCollection<string>(routeParameters);
        QueryParameters = new ReadOnlyCollection<string>(query);
        RequiredQueryParameters = new ReadOnlyCollection<string>(requiredQuery);
        Pagination = pagination;
        RequiredScopes = new ReadOnlyCollection<string>((requiredScopes ?? []).ToArray());
        RequiredRoles = new ReadOnlyCollection<string>((requiredRoles ?? []).ToArray());
        Cache = cache;
        RateLimit = rateLimit;
        BodyTypeInfo = bodyTypeInfo;
        IsBodyRequired = isBodyRequired;
        ResponseType = responseType;
    }

    /// <summary>The spec's <c>operationId</c>, e.g. <c>GetCharactersCharacterIdAssets</c>. Useful for logs and metrics.</summary>
    public string OperationId { get; }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The path template relative to the ESI base address, e.g. <c>/characters/{character_id}/assets</c>.</summary>
    public string PathTemplate { get; }

    /// <summary>Path parameter names, in template order. All are required.</summary>
    public IReadOnlyList<string> RouteParameters { get; }

    /// <summary>Query parameter names the endpoint accepts, excluding pagination and shared headers.</summary>
    public IReadOnlyList<string> QueryParameters { get; }

    /// <summary>The subset of <see cref="QueryParameters"/> that must be sent.</summary>
    public IReadOnlyList<string> RequiredQueryParameters { get; }

    /// <summary>How the endpoint pages its results.</summary>
    public PaginationStyle Pagination { get; }

    /// <summary>SSO scopes the access token must carry. Empty for public endpoints.</summary>
    public IReadOnlyList<string> RequiredScopes { get; }

    /// <summary>In-game corporation roles, any one of which the character must hold (<c>x-required-roles</c>).</summary>
    public IReadOnlyList<string> RequiredRoles { get; }

    /// <summary>Cache facts from the spec, if any.</summary>
    public RouteCacheHints? Cache { get; }

    /// <summary>The rate-limit bucket, if the endpoint is rate limited.</summary>
    public RateLimitPolicy? RateLimit { get; }

    /// <summary>Serialization metadata for the request body, or <see langword="null"/> if the endpoint takes none.</summary>
    public JsonTypeInfo? BodyTypeInfo { get; }

    /// <summary>Whether a request body must be supplied.</summary>
    public bool IsBodyRequired { get; }

    /// <summary>The CLR type the response body deserializes to (<c>EsiNoContent</c> when there is none).</summary>
    public Type ResponseType { get; }

    /// <summary>Whether the endpoint needs an SSO access token.</summary>
    public bool RequiresAuthentication => RequiredScopes.Count > 0;

    /// <inheritdoc />
    public override string ToString() => $"{Method} {PathTemplate}";

    private static void RequireUnique(string[] names, string paramName)
    {
        var duplicates = names.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
        {
            throw new ArgumentException($"Parameters [{string.Join(", ", duplicates)}] are listed more than once.", paramName);
        }

        if (names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Parameter names can't be blank.", paramName);
        }
    }

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex PlaceholderPattern();
}
