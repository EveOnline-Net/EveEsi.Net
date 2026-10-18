using System.Net;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Middleware.Caching;
using EveEsi.Net.Middleware.Tests.TestSupport;

namespace EveEsi.Net.Middleware.Tests.Caching;

/// <summary>The two caching middleware together, in both orders: they share one entry and store it once per call.</summary>
public sealed class CacheAndETagTests
{
    private readonly ManualTimeProvider _time = new(Esi.Start);
    private readonly CountingStore _store;
    private readonly FakeNext _next = new();

    public CacheAndETagTests() => _store = new CountingStore(new InMemoryEsiCacheStore(timeProvider: _time));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FreshThenStale_ServesFromCacheThenRevalidates(bool cacheOutermost)
    {
        var later = Esi.Start.AddMinutes(10);
        _next.Respond(Esi.Ok("assets", Esi.Start, TimeSpan.FromMinutes(5)))
            .Respond(Esi.Response(HttpStatusCode.NotModified, "",
                ("Date", Esi.HttpDate(later)), ("Expires", Esi.HttpDate(later.AddMinutes(5))), ("ETag", "\"v1\"")));

        await Run(cacheOutermost);                         // miss: fetched and stored
        _time.Advance(TimeSpan.FromMinutes(1));
        await Run(cacheOutermost);                         // fresh: served by the cache, ESI not called
        _time.Advance(TimeSpan.FromMinutes(9));
        var revalidated = await Run(cacheOutermost);       // stale: revalidated with the ETag
        _time.Advance(TimeSpan.FromMinutes(1));
        var fresh = await Run(cacheOutermost);             // fresh again from the refreshed entry

        _next.Calls.Count.ShouldBe(2);
        _next.Calls[1]["If-None-Match"].ShouldBe("\"v1\"");
        revalidated.Response!.Body().ShouldBe("assets");
        fresh.Response!.Body().ShouldBe("assets");
        _store.Sets.ShouldBe(2, "one write per call that reached ESI, not one per middleware");
    }

    private async Task<EsiContext> Run(bool cacheOutermost)
    {
        IEsiMiddleware cache = new CacheMiddleware(_store, _time);
        IEsiMiddleware etag = new ETagMiddleware(_store, _time);
        var (outer, inner) = cacheOutermost ? (cache, etag) : (etag, cache);

        var context = Esi.Context(Esi.Route());
        await outer.InvokeAsync(context, c => inner.InvokeAsync(c, _next.Invoke));
        return context;
    }
}
