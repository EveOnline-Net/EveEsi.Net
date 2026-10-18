using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Abstractions.Middleware;

/// <summary>The state of one ESI call as it passes through the middleware pipeline.</summary>
public sealed class EsiContext
{
    /// <summary>Creates a context. The client does this for every call; tests can too.</summary>
    /// <param name="route">The endpoint being called.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="accessToken">The SSO access token, or <see langword="null"/> for public calls.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public EsiContext(
        RouteDefinition route,
        EsiRequestMessage request,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(request);
        Route = route;
        Request = request;
        AccessToken = accessToken;
        CancellationToken = cancellationToken;
    }

    /// <summary>The endpoint being called. Read scopes, roles, cache hints and rate-limit group from here.</summary>
    public RouteDefinition Route { get; }

    /// <summary>The outgoing request. Middleware may change its route and query values, headers and body before calling <c>next</c>.</summary>
    public EsiRequestMessage Request { get; }

    /// <summary>
    /// The SSO access token, or <see langword="null"/> for public calls. The client sends it as
    /// <c>Authorization: Bearer</c> at send time, so middleware may replace it (e.g. after a refresh).
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>The raw response, once there is one. Set it without calling <c>next</c> to short-circuit.</summary>
    public EsiResponse? Response { get; set; }

    /// <summary>Per-call state shared between middleware.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Cancels the call. Middleware must honour it.</summary>
    public CancellationToken CancellationToken { get; }
}
