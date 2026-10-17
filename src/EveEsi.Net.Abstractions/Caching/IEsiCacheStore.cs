namespace EveEsi.Net.Abstractions.Caching;

/// <summary>
/// Storage for raw ESI responses, used by caching and ETag middleware. The library holds no cache itself; this
/// abstraction is where that state lives. Implementations must be safe for concurrent use.
/// </summary>
/// <remarks>
/// Keys are built by middleware and never contain a raw access token. Stores should treat them as opaque.
/// </remarks>
public interface IEsiCacheStore
{
    /// <summary>Gets an entry, or <see langword="null"/> if there is none.</summary>
    ValueTask<EsiCacheEntry?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Adds or replaces an entry.</summary>
    ValueTask SetAsync(string key, EsiCacheEntry entry, CancellationToken cancellationToken);

    /// <summary>Removes an entry, if present.</summary>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);
}
