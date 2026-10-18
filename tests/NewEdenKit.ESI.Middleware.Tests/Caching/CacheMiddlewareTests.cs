using System.Net;
using NewEdenKit.ESI.Abstractions.Routing;
using NewEdenKit.ESI.Middleware.Caching;
using NewEdenKit.ESI.Middleware.Tests.TestSupport;

namespace NewEdenKit.ESI.Middleware.Tests.Caching;

public sealed class CacheMiddlewareTests
{
    private readonly ManualTimeProvider _time = new(Esi.Start);
    private readonly InMemoryEsiCacheStore _store;
    private readonly CacheMiddleware _middleware;
    private readonly FakeNext _next = new();

    public CacheMiddlewareTests()
    {
        _store = new InMemoryEsiCacheStore(timeProvider: _time);
        _middleware = new CacheMiddleware(_store, _time);
    }

    [Fact]
    public async Task FreshEntry_IsServedWithoutCallingEsi()
    {
        _next.Respond(Esi.Ok("first", Esi.Start, TimeSpan.FromMinutes(5)));
        await Run();

        _time.Advance(TimeSpan.FromMinutes(4));
        var context = await Run();

        _next.Calls.Count.ShouldBe(1);
        context.Response.ShouldNotBeNull().Body().ShouldBe("first");
    }

    [Fact]
    public async Task ExpiredEntry_CallsEsiAgain()
    {
        _next.Respond(Esi.Ok("first", Esi.Start, TimeSpan.FromMinutes(5)))
            .Respond(Esi.Ok("second", Esi.Start.AddMinutes(5), TimeSpan.FromMinutes(5)));
        await Run();

        _time.Advance(TimeSpan.FromMinutes(5));
        var context = await Run();

        _next.Calls.Count.ShouldBe(2);
        context.Response!.Body().ShouldBe("second");
    }

    [Fact]
    public async Task Lifetime_IsExpiresMinusDate_SoASkewedLocalClockDoesNotMatter()
    {
        // ESI's clock is an hour behind ours; the response is still fresh for its 5 minutes.
        _next.Respond(Esi.Ok("body", Esi.Start.AddHours(-1), TimeSpan.FromMinutes(5)));

        await Run();

        (await _store.GetAsync(EsiCacheKey.Create(Context()), TestContext.Current.CancellationToken))!.ExpiresAt.ShouldBe(Esi.Start.AddMinutes(5));
    }

    [Fact]
    public async Task Lifetime_MaxAgeWinsOverExpires()
    {
        _next.Respond(Esi.Response(HttpStatusCode.OK, "body",
            ("Date", Esi.HttpDate(Esi.Start)), ("Expires", Esi.HttpDate(Esi.Start.AddHours(1))), ("Cache-Control", "public, max-age=60")));

        await Run();

        (await _store.GetAsync(EsiCacheKey.Create(Context()), TestContext.Current.CancellationToken))!.ExpiresAt.ShouldBe(Esi.Start.AddSeconds(60));
    }

    [Fact]
    public async Task Lifetime_FallsBackToTheSpecsCacheAge()
    {
        var route = Esi.Route(cache: new RouteCacheHints(TimeSpan.FromSeconds(300), RouteCacheMode.TtlBased));
        _next.Respond(Esi.Response(HttpStatusCode.OK, "body"));

        await Run(route);

        (await _store.GetAsync(EsiCacheKey.Create(Esi.Context(route)), TestContext.Current.CancellationToken))!.ExpiresAt.ShouldBe(Esi.Start.AddSeconds(300));
    }

    [Fact]
    public async Task NoLifetime_IsNotStored()
    {
        _next.Respond(Esi.Response(HttpStatusCode.OK, "body"));

        await Run();

        _store.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData("private, no-cache")]
    public async Task NoStoreOrNoCache_IsNotStored(string cacheControl)
    {
        _next.Respond(Esi.Response(HttpStatusCode.OK, "body", ("Cache-Control", cacheControl), ("Expires", Esi.HttpDate(Esi.Start.AddHours(1)))));

        await Run();

        _store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NotCachedRoute_PassesThroughAndStoresNothing()
    {
        var route = Esi.Route(cache: new RouteCacheHints(null, RouteCacheMode.NotCached));
        _next.Respond(Esi.Ok("a", Esi.Start, TimeSpan.FromHours(1))).Respond(Esi.Ok("b", Esi.Start, TimeSpan.FromHours(1)));

        await Run(route);
        await Run(route);

        _next.Calls.Count.ShouldBe(2);
        _store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NonGet_PassesThroughAndStoresNothing()
    {
        var route = Esi.Route(method: HttpMethod.Post);
        _next.Respond(Esi.Ok("a", Esi.Start, TimeSpan.FromHours(1)));

        await Run(route);

        _store.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task NonOkResponse_IsNotStored(HttpStatusCode status)
    {
        _next.Respond(Esi.Response(status, "", ("Expires", Esi.HttpDate(Esi.Start.AddHours(1)))));

        await Run();

        _store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task EachPageAndCharacter_HasItsOwnEntry()
    {
        var route = Esi.Route(scope: "esi-assets.read_assets.v1");
        for (var i = 0; i < 3; i++)
        {
            _next.Respond(Esi.Ok($"r{i}", Esi.Start, TimeSpan.FromHours(1)));
        }

        await Run(route, Esi.Token("CHARACTER:EVE:1"), page: 1);
        await Run(route, Esi.Token("CHARACTER:EVE:1"), page: 2);
        await Run(route, Esi.Token("CHARACTER:EVE:2"), page: 1);

        _next.Calls.Count.ShouldBe(3);
        _store.Count.ShouldBe(3);
    }

    private static Abstractions.Middleware.EsiContext Context() => Esi.Context(Esi.Route());

    private async Task<Abstractions.Middleware.EsiContext> Run(RouteDefinition? route = null, string? token = null, int? page = null)
    {
        var context = Esi.Context(route ?? Esi.Route(), token, page: page);
        await _middleware.InvokeAsync(context, _next.Invoke);
        return context;
    }
}
