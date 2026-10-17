using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Abstractions.Results;

namespace EveEsi.Net.Client.Internal;

/// <summary>
/// Builds requests: resolves the operation ID, checks the token against the route's scope, runs the configurator and
/// checks route values against the path template. Programmer errors throw here, before anything is sent.
/// </summary>
/// <remarks>Register as transient: it holds a typed <see cref="EsiHttpClient"/>.</remarks>
internal sealed partial class EsiClientFactory : IEsiClientFactory
{
    private readonly EsiHttpClient _httpClient;
    private readonly EsiRouteTable _routes;
    private readonly EsiGlobalMiddleware _globalMiddleware;

    public EsiClientFactory(EsiHttpClient httpClient, EsiRouteTable routes, EsiGlobalMiddleware globalMiddleware)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _globalMiddleware = globalMiddleware ?? throw new ArgumentNullException(nameof(globalMiddleware));
    }

    public IEsiRequest<T> CreateEsiClient<T>(
        string operationId,
        JsonTypeInfo<T> responseType,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware)
    {
        ArgumentNullException.ThrowIfNull(responseType);
        return new EsiRequest<T>(Build(operationId, configure, token, serviceMiddleware), responseType);
    }

    public IEsiRequest<EsiNoContent> CreateEsiClient(
        string operationId,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware) =>
        new EsiRequest<EsiNoContent>(Build(operationId, configure, token, serviceMiddleware), responseType: null);

    private EsiCall Build(
        string operationId,
        Action<IEsiRequestParameters>? configure,
        string? token,
        IReadOnlyList<IEsiMiddleware>? serviceMiddleware)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (!_routes.TryGet(operationId, out var route))
        {
            throw new ArgumentException($"Unknown operation ID '{operationId}'.", nameof(operationId));
        }

        if (route.RequiresAuthentication && string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException($"{route} requires an access token with scope '{route.Scope}'.", nameof(token));
        }

        var parameters = new EsiRequestParameters();
        configure?.Invoke(parameters);
        parameters.Freeze();
        ValidateRouteValues(route.PathTemplate, parameters.RoutePairs);

        return new EsiCall(
            route,
            parameters,
            token,
            _globalMiddleware.Middleware,
            serviceMiddleware is null ? [] : [.. serviceMiddleware],
            _httpClient);
    }

    private static void ValidateRouteValues(string pathTemplate, IReadOnlyList<KeyValuePair<string, string>> routeValues)
    {
        var placeholders = PlaceholderPattern().Matches(pathTemplate).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var names = routeValues.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

        var missing = placeholders.Except(names).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException($"Missing route values for '{pathTemplate}': {string.Join(", ", missing)}.");
        }

        var unknown = names.Except(placeholders).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException($"'{pathTemplate}' has no route parameters named: {string.Join(", ", unknown)}.");
        }
    }

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex PlaceholderPattern();
}
