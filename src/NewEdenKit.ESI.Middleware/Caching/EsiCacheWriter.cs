using System.Net;
using NewEdenKit.ESI.Abstractions.Caching;
using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Middleware.Caching;

/// <summary>
/// How long a response stays fresh, and storing it. Shared by <see cref="CacheMiddleware"/> and
/// <see cref="ETagMiddleware"/>, so both write the same entry whichever order they run in.
/// </summary>
internal static class EsiCacheWriter
{
    // Set on the context once a response is stored, so a second caching middleware in the same call doesn't store it again.
    private const string StoredItem = "NewEdenKit.ESI.Middleware.Caching.Stored";

    /// <summary>
    /// When the response stops being fresh, or <see langword="null"/> if it mustn't be reused without asking ESI.
    /// <c>max-age</c> wins over <c>Expires</c>, which wins over the spec's <c>x-cache-age</c>. <c>Expires</c> is measured
    /// against ESI's own <c>Date</c>, so a skewed local clock doesn't stretch or shrink the lifetime.
    /// </summary>
    public static DateTimeOffset? FreshUntil(EsiResponse response, RouteDefinition route, DateTimeOffset now)
    {
        var headers = response.Headers;
        if (route.Cache?.Mode == RouteCacheMode.NotCached
            || headers.CacheControl?.Contains("no-store", StringComparison.OrdinalIgnoreCase) == true
            || headers.CacheControl?.Contains("no-cache", StringComparison.OrdinalIgnoreCase) == true)
        {
            return null;
        }

        DateTimeOffset? until = headers.MaxAge is { } maxAge ? now + maxAge
            : headers.Expires is { } expires ? (headers.Date is { } date ? now + (expires - date) : expires)
            : route.Cache?.MaxAge is { } hint ? now + hint
            : null;
        return until > now ? until : null;
    }

    /// <summary>Serves a stored response, marking it as stored so no middleware writes it back.</summary>
    public static void Serve(EsiContext context, EsiCacheEntry entry)
    {
        context.Response = entry.Response;
        context.Items[StoredItem] = entry.Response;
    }

    /// <summary>Stores a successful response, once per call.</summary>
    /// <param name="store">The store.</param>
    /// <param name="key">The call's <see cref="EsiCacheKey"/>.</param>
    /// <param name="context">The call, after <c>next</c>.</param>
    /// <param name="now">The current time.</param>
    /// <param name="forRevalidation">
    /// Store it even without a fresh lifetime, because its ETag can still revalidate it (ETag middleware).
    /// </param>
    public static async ValueTask StoreAsync(IEsiCacheStore store, string key, EsiContext context, DateTimeOffset now, bool forRevalidation)
    {
        if (context.Response is not { StatusCode: HttpStatusCode.OK } response
            || (context.Items.TryGetValue(StoredItem, out var stored) && ReferenceEquals(stored, response)))
        {
            return;
        }

        var hasETag = response.Headers.ETag is not null;
        var freshUntil = FreshUntil(response, context.Route, now);
        if (freshUntil is null && !(forRevalidation && hasETag))
        {
            return;
        }

        // An entry with an ETag is kept past its expiry (the store decides how long) so it can be revalidated cheaply.
        var entry = new EsiCacheEntry(response, freshUntil ?? now) { RetainUntil = hasETag ? null : freshUntil };
        await store.SetAsync(key, entry, context.CancellationToken).ConfigureAwait(false);
        context.Items[StoredItem] = response;
    }
}
