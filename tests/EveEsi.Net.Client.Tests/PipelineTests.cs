using System.Net;
using EveEsi.Net.Abstractions.Http;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Client.Tests.TestSupport;

namespace EveEsi.Net.Client.Tests;

public sealed class PipelineTests
{
    private const string Ok = "{\"name\":\"x\",\"corporation_id\":1}";

    [Fact]
    public async Task ExecuteAsync_Levels_RunGlobalThenServiceThenRequest()
    {
        var order = new List<string>();
        var client = new TestClient([new Recording("global-1", order), new Recording("global-2", order)]);
        client.Handler.Respond(HttpStatusCode.OK, Ok);

        await client.Factory
            .CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, [new Recording("service", order)])
            .Use(new Recording("request-instance", order))
            .Use(async (ctx, next) =>
            {
                order.Add("request-callback");
                await next(ctx);
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        order.ShouldBe(["global-1", "global-2", "service", "request-instance", "request-callback"]);
    }

    [Fact]
    public async Task CreateEsiClient_ServiceMiddleware_IsSnapshotAtCreation()
    {
        var order = new List<string>();
        var client = new TestClient();
        client.Handler.Respond(HttpStatusCode.OK, Ok);
        var serviceMiddleware = new List<IEsiMiddleware> { new Recording("before", order) };

        var request = client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, serviceMiddleware);
        serviceMiddleware.Add(new Recording("after", order));
        await request.ExecuteAsync(TestContext.Current.CancellationToken);

        order.ShouldBe(["before"]);
    }

    [Fact]
    public void Use_ReturnsSameRequestForChaining()
    {
        var request = new TestClient().Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null);

        request.Use((ctx, next) => next(ctx)).ShouldBeSameAs(request);
    }

    [Fact]
    public async Task ExecuteAsync_ShortCircuit_SkipsHttp()
    {
        var client = new TestClient();
        var cached = new EsiResponse(HttpStatusCode.OK, EsiResponseHeaders.Empty, "{\"name\":\"cached\",\"corporation_id\":1}"u8.ToArray());

        var result = await client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .Use((ctx, _) =>
            {
                ctx.Response = cached;
                return ValueTask.CompletedTask;
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.Value!.Name.ShouldBe("cached");
        client.Handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShortCircuitWithoutResponse_Throws()
    {
        var request = new TestClient().Factory
            .CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .Use((_, _) => ValueTask.CompletedTask);

        await Should.ThrowAsync<InvalidOperationException>(() => request.ExecuteAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_MiddlewareReplacesResponseAfterNext_ResultUsesReplacement()
    {
        var client = new TestClient();
        client.Handler.Respond(HttpStatusCode.NotModified);
        var stored = new EsiResponse(HttpStatusCode.OK, EsiResponseHeaders.Empty, "{\"name\":\"stored\",\"corporation_id\":1}"u8.ToArray());

        var result = await client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .Use(async (ctx, next) =>
            {
                await next(ctx);
                if (ctx.Response!.StatusCode == HttpStatusCode.NotModified)
                {
                    ctx.Response = stored;
                }
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Name.ShouldBe("stored");
    }

    [Fact]
    public async Task ExecuteAsync_MiddlewareHeaders_AreSentAndOverrideClientDefaults()
    {
        var client = new TestClient();
        client.Handler.Respond(HttpStatusCode.OK, Ok);

        await client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .Use((ctx, next) =>
            {
                ctx.Request.Headers["If-None-Match"] = "\"v1\"";
                ctx.Request.Headers["X-Tenant"] = "singularity";
                return next(ctx);
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        client.SingleRequest.Headers["If-None-Match"].ShouldBe("\"v1\"");
        client.SingleRequest.Headers["X-Tenant"].ShouldBe("singularity");
    }

    [Fact]
    public async Task ExecuteAsync_MiddlewareReplacesAccessToken_NewTokenIsSent()
    {
        var client = new TestClient();
        client.Handler.Respond(HttpStatusCode.OK, "[]");

        await client.Factory.CreateEsiClient(TestClient.GetAssets, TestJsonContext.Default.IReadOnlyListInt64,
                p => p.Route.Set("character_id", 1L), "expired", null)
            .Use((ctx, next) =>
            {
                ctx.AccessToken = "refreshed";
                return next(ctx);
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        client.SingleRequest.Headers["Authorization"].ShouldBe("Bearer refreshed");
    }

    [Fact]
    public async Task ExecuteAsync_MiddlewareCallsNextTwice_SendsTwice()
    {
        var client = new TestClient();
        client.Handler.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.OK, Ok);

        var result = await client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .Use(async (ctx, next) =>
            {
                await next(ctx);
                if (ctx.Response!.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    await next(ctx);
                }
            })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        client.Handler.Requests.Count.ShouldBe(2);
    }

    private sealed class Recording(string name, List<string> order) : IEsiMiddleware
    {
        public async ValueTask InvokeAsync(EsiContext context, EsiNext next)
        {
            order.Add(name);
            await next(context);
        }
    }
}
