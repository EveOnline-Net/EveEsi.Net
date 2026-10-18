using System.Net;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Routing;
using EveEsi.Net.Middleware.Caching;
using EveEsi.Net.Middleware.Tests.TestSupport;

namespace EveEsi.Net.Middleware.Tests.Caching;

public sealed class ETagMiddlewareTests
{
    private readonly ManualTimeProvider _time = new(Esi.Start);
    private readonly InMemoryEsiCacheStore _store;
    private readonly FakeNext _next = new();

    public ETagMiddlewareTests() => _store = new InMemoryEsiCacheStore(timeProvider: _time);

    [Fact]
    public async Task FirstCall_SendsNoIfNoneMatchAndStoresTheResponse()
    {
        _next.Respond(Esi.Ok("body", Esi.Start, TimeSpan.FromMinutes(5)));

        await Run();

        _next.Calls.ShouldHaveSingleItem().ContainsKey("If-None-Match").ShouldBeFalse();
        _store.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NotModified_ReturnsTheStoredBodyWithEsisFreshHeaders()
    {
        _next.Respond(Esi.Ok("assets", Esi.Start, TimeSpan.FromMinutes(5)));
        await Run();
        _time.Advance(TimeSpan.FromMinutes(10));
        var later = Esi.Start.AddMinutes(10);
        _next.Respond(Esi.Response(HttpStatusCode.NotModified, "",
            ("Date", Esi.HttpDate(later)), ("Expires", Esi.HttpDate(later.AddMinutes(5))), ("ETag", "\"v1\"")));

        var context = await Run();

        _next.Calls[1]["If-None-Match"].ShouldBe("\"v1\"");
        var response = context.Response.ShouldNotBeNull();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Body().ShouldBe("assets");
        response.Headers.Expires.ShouldBe(later.AddMinutes(5));
        response.Headers.ETag.ShouldBe("\"v1\"");
        context.Request.Headers.ContainsKey("If-None-Match").ShouldBeFalse("the header it added is taken off again");
        (await _store.GetAsync(EsiCacheKey.Create(context), TestContext.Current.CancellationToken))!.ExpiresAt.ShouldBe(later.AddMinutes(5));
    }

    [Fact]
    public async Task ChangedResource_ReplacesTheStoredEntry()
    {
        _next.Respond(Esi.Ok("old", Esi.Start, TimeSpan.Zero))
            .Respond(Esi.Ok("new", Esi.Start, TimeSpan.FromMinutes(5), etag: "\"v2\""));
        await Run();

        var context = await Run();

        context.Response!.Body().ShouldBe("new");
        (await _store.GetAsync(EsiCacheKey.Create(context), TestContext.Current.CancellationToken))!.Response.Headers.ETag.ShouldBe("\"v2\"");
    }

    [Fact]
    public async Task AlreadyExpiredResponse_IsStillKeptForRevalidation()
    {
        _next.Respond(Esi.Response(HttpStatusCode.OK, "body", ("ETag", "\"v1\"")))
            .Respond(Esi.Response(HttpStatusCode.NotModified, "", ("ETag", "\"v1\"")));
        await Run();

        var context = await Run();

        _next.Calls[1]["If-None-Match"].ShouldBe("\"v1\"");
        context.Response!.Body().ShouldBe("body");
    }

    [Fact]
    public async Task CallersOwnIfNoneMatch_IsLeftAloneAndTheNotModifiedPassesThrough()
    {
        _next.Respond(Esi.Ok("body", Esi.Start, TimeSpan.FromMinutes(5)))
            .Respond(Esi.Response(HttpStatusCode.NotModified, "", ("ETag", "\"v1\"")));
        await Run();
        var context = Esi.Context(Esi.Route());
        context.Request.Headers["If-None-Match"] = "\"mine\"";

        await new ETagMiddleware(_store, _time).InvokeAsync(context, _next.Invoke);

        _next.Calls[1]["If-None-Match"].ShouldBe("\"mine\"");
        context.Response!.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task ResponseWithoutETag_IsNotStoredOrRevalidated()
    {
        _next.Respond(Esi.Response(HttpStatusCode.OK, "body")).Respond(Esi.Response(HttpStatusCode.OK, "body"));

        await Run();
        await Run();

        _next.Calls[1].ContainsKey("If-None-Match").ShouldBeFalse();
        _store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task NonGet_PassesThrough()
    {
        var route = Esi.Route(method: HttpMethod.Post);
        _next.Respond(Esi.Ok("body", Esi.Start, TimeSpan.FromMinutes(5)));

        await Run(route);

        _store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Exception_StillRemovesTheHeaderItAdded()
    {
        _next.Respond(Esi.Ok("body", Esi.Start, TimeSpan.Zero)).Respond(_ => throw new HttpRequestException("down"));
        await Run();
        var context = Esi.Context(Esi.Route());

        await Should.ThrowAsync<HttpRequestException>(async () => await new ETagMiddleware(_store, _time).InvokeAsync(context, _next.Invoke));

        context.Request.Headers.ContainsKey("If-None-Match").ShouldBeFalse();
    }

    private async Task<EsiContext> Run(RouteDefinition? route = null)
    {
        var context = Esi.Context(route ?? Esi.Route());
        await new ETagMiddleware(_store, _time).InvokeAsync(context, _next.Invoke);
        return context;
    }
}
