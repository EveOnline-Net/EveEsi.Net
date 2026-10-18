using NewEdenKit.ESI.Abstractions.Caching;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Middleware.Caching;

/// <summary>
/// Serves GET responses from the <see cref="IEsiCacheStore"/> while they're fresh, without calling ESI, and stores
/// successful responses for as long as ESI says they stay fresh.
/// </summary>
/// <remarks>
/// <para>
/// Freshness comes from <c>Cache-Control: max-age</c>, then <c>Expires</c> (measured against ESI's <c>Date</c>), then the
/// spec's <c>x-cache-age</c>. Routes marked not-cached, and responses with <c>no-store</c> or <c>no-cache</c>, are never
/// stored. Keys come from <see cref="EsiCacheKey"/>.
/// </para>
/// <para>
/// Pair it with <see cref="ETagMiddleware"/> so a stale entry is revalidated with <c>If-None-Match</c> instead of
/// downloaded again. They share entries and work in either order.
/// </para>
/// </remarks>
/// <param name="store">Where responses are kept.</param>
/// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
public sealed class CacheMiddleware(IEsiCacheStore store, TimeProvider? timeProvider = null) : IEsiMiddleware
{
    private readonly IEsiCacheStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask InvokeAsync(EsiContext context, EsiNext next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.Request.Method != HttpMethod.Get || context.Route.Cache?.Mode == RouteCacheMode.NotCached)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var key = EsiCacheKey.Create(context);
        var entry = await _store.GetAsync(key, context.CancellationToken).ConfigureAwait(false);
        if (entry is not null && entry.ExpiresAt > _time.GetUtcNow())
        {
            EsiCacheWriter.Serve(context, entry);
            return;
        }

        await next(context).ConfigureAwait(false);
        await EsiCacheWriter.StoreAsync(_store, key, context, _time.GetUtcNow(), forRevalidation: false).ConfigureAwait(false);
    }
}
