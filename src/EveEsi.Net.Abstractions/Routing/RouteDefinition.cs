namespace EveEsi.Net.Abstractions.Routing;

/// <summary>
/// Static description of one ESI endpoint: where it lives, the scope it needs, and how it's cached and rate limited.
/// Middleware reads these facts from <c>context.Route</c>, never from the path.
/// </summary>
/// <remarks>
/// Every endpoint has one entry in the generated route table, keyed by <see cref="OperationId"/>. The route says
/// nothing about parameters, pagination, body or response type: the generated service signatures enforce those.
/// </remarks>
public sealed class RouteDefinition
{
    /// <summary>Creates a route definition.</summary>
    /// <param name="operationId">The spec's <c>operationId</c>, e.g. <c>GetCharactersCharacterIdAssets</c>.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pathTemplate">The path template relative to the ESI base address, e.g. <c>/characters/{character_id}/assets</c>.</param>
    /// <param name="scope">The SSO scope the access token must carry, or <see langword="null"/> for public endpoints.</param>
    /// <param name="cache">Cache facts from the spec, if any.</param>
    /// <param name="rateLimit">The rate-limit bucket, if the endpoint is rate limited.</param>
    /// <exception cref="ArgumentException">A required argument is blank, or the path template doesn't start with <c>/</c>.</exception>
    public RouteDefinition(
        string operationId,
        HttpMethod method,
        string pathTemplate,
        string? scope = null,
        RouteCacheHints? cache = null,
        RateLimitPolicy? rateLimit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);
        if (!pathTemplate.StartsWith('/'))
        {
            throw new ArgumentException($"Path template '{pathTemplate}' must start with '/'.", nameof(pathTemplate));
        }

        if (scope is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        }

        OperationId = operationId;
        Method = method;
        PathTemplate = pathTemplate;
        Scope = scope;
        Cache = cache;
        RateLimit = rateLimit;
    }

    /// <summary>The spec's <c>operationId</c>: the route table key. Also useful for logs and metrics.</summary>
    public string OperationId { get; }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The path template relative to the ESI base address, e.g. <c>/characters/{character_id}/assets</c>.</summary>
    public string PathTemplate { get; }

    /// <summary>The SSO scope the access token must carry, or <see langword="null"/> for public endpoints.</summary>
    public string? Scope { get; }

    /// <summary>Cache facts from the spec, if any.</summary>
    public RouteCacheHints? Cache { get; }

    /// <summary>The rate-limit bucket, if the endpoint is rate limited.</summary>
    public RateLimitPolicy? RateLimit { get; }

    /// <summary>Whether the endpoint needs an SSO access token.</summary>
    public bool RequiresAuthentication => Scope is not null;

    /// <inheritdoc />
    public override string ToString() => $"{Method} {PathTemplate}";
}
