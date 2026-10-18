using System.Net;
using NewEdenKit.ESI.Abstractions.Caching;
using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Middleware;

namespace NewEdenKit.ESI.Middleware.Caching;

/// <summary>
/// Revalidates GET responses with their ETag. It sends <c>If-None-Match</c> from the stored entry; when ESI answers
/// <c>304 Not Modified</c>, it returns the stored body with ESI's fresh headers, so the caller gets the value as if it
/// had been sent again.
/// </summary>
/// <remarks>
/// <para>
/// Responses with an ETag are stored in the <see cref="IEsiCacheStore"/> and kept past their expiry so they can be
/// revalidated. A request that already carries <c>If-None-Match</c> is left alone, and its 304 is passed through.
/// </para>
/// <para>
/// It always asks ESI. Add <see cref="CacheMiddleware"/> to skip the call entirely while an entry is fresh; the two share
/// entries and work in either order.
/// </para>
/// </remarks>
/// <param name="store">Where responses are kept.</param>
/// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
public sealed class ETagMiddleware(IEsiCacheStore store, TimeProvider? timeProvider = null) : IEsiMiddleware
{
    private const string IfNoneMatch = "If-None-Match";

    private readonly IEsiCacheStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask InvokeAsync(EsiContext context, EsiNext next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.Request.Method != HttpMethod.Get || context.Request.Headers.ContainsKey(IfNoneMatch))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var key = EsiCacheKey.Create(context);
        var entry = await _store.GetAsync(key, context.CancellationToken).ConfigureAwait(false);
        var etag = entry?.Response.Headers.ETag;
        if (etag is not null)
        {
            context.Request.Headers[IfNoneMatch] = etag;
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            // Ours, not the caller's: take it off again so a retry decides afresh.
            if (etag is not null)
            {
                context.Request.Headers.Remove(IfNoneMatch);
            }
        }

        if (entry is not null && etag is not null && context.Response is { StatusCode: HttpStatusCode.NotModified } notModified)
        {
            context.Response = new EsiResponse(entry.Response.StatusCode, Refresh(entry.Response.Headers, notModified.Headers), entry.Response.Body);
        }

        await EsiCacheWriter.StoreAsync(_store, key, context, _time.GetUtcNow(), forRevalidation: true).ConfigureAwait(false);
    }

    /// <summary>The stored headers, with every header the 304 sent (Expires, Date, ETag, …) replacing the stored one.</summary>
    private static EsiResponseHeaders Refresh(EsiResponseHeaders stored, EsiResponseHeaders fresh)
    {
        var merged = stored
            .Where(h => !fresh.TryGetValues(h.Key, out _))
            .Concat(fresh)
            .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value));
        return new EsiResponseHeaders(merged);
    }
}
