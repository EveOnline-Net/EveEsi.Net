namespace EveEsi.Net.Abstractions.Routing;

/// <summary>Cache facts from the spec, for caching middleware. Response headers take precedence when present.</summary>
/// <param name="MaxAge">How long a response may be reused (<c>x-cache-age</c>), if the spec says.</param>
/// <param name="Mode">How ESI caches the endpoint server side.</param>
/// <param name="TombstoneTtl">How long a deleted resource keeps returning its tombstone (<c>x-tombstone-ttl</c>).</param>
public sealed record RouteCacheHints(TimeSpan? MaxAge, RouteCacheMode Mode, TimeSpan? TombstoneTtl = null);
