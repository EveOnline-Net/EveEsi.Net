namespace NewEdenKit.ESI.Abstractions.Routing;

/// <summary>How ESI caches an endpoint server side (<c>x-server-cache-mode</c> / <c>x-cache-mode</c>).</summary>
public enum RouteCacheMode
{
    /// <summary>The spec doesn't say.</summary>
    Unspecified,

    /// <summary>Cached for a fixed time (<c>ttl-based</c>).</summary>
    TtlBased,

    /// <summary>Cached until the underlying data changes (<c>event-based</c>).</summary>
    EventBased,

    /// <summary>Not cached (<c>not-cached</c>).</summary>
    NotCached,
}
