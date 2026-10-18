using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Tests.TestSupport;

namespace NewEdenKit.ESI.Abstractions.Tests.Middleware;

public sealed class EsiMiddlewareTargetExtensionsTests
{
    [Fact]
    public async Task Use_AsyncCallback_IsRegisteredAndRuns()
    {
        var target = new FakeMiddlewareTarget();
        var ran = false;

        target.Use(async (ctx, next) =>
        {
            ran = true;
            await next(ctx);
        });

        await target.RunAsync(FakeMiddlewareTarget.NewContext(), _ => ValueTask.CompletedTask);

        target.Middleware.Count.ShouldBe(1);
        ran.ShouldBeTrue();
    }

    [Fact]
    public void Use_AsyncCallback_ReturnsTargetForChaining()
    {
        var target = new FakeMiddlewareTarget();

        var returned = target.Use((ctx, next) => next(ctx));

        returned.ShouldBeSameAs(target);
    }

    [Fact]
    public async Task Use_InstancesAndCallbacks_RunInRegistrationOrder()
    {
        var order = new List<string>();
        var target = new FakeMiddlewareTarget()
            .Use(new RecordingMiddleware("instance-1", order))
            .Use(async (ctx, next) =>
            {
                order.Add("callback-2");
                await next(ctx);
            })
            .Use(new RecordingMiddleware("instance-3", order));

        await target.RunAsync(FakeMiddlewareTarget.NewContext(), _ =>
        {
            order.Add("terminal");
            return ValueTask.CompletedTask;
        });

        order.ShouldBe(["instance-1", "callback-2", "instance-3", "terminal"]);
    }

    [Fact]
    public async Task Use_CallbackThatShortCircuits_SkipsRestOfPipeline()
    {
        var response = new EsiResponse(System.Net.HttpStatusCode.OK, EsiResponseHeaders.Empty, "{}"u8.ToArray());
        var terminalCalled = false;
        var target = new FakeMiddlewareTarget().Use((ctx, _) =>
        {
            ctx.Response = response;
            return ValueTask.CompletedTask;
        });
        var context = FakeMiddlewareTarget.NewContext();

        await target.RunAsync(context, _ =>
        {
            terminalCalled = true;
            return ValueTask.CompletedTask;
        });

        terminalCalled.ShouldBeFalse();
        context.Response.ShouldBeSameAs(response);
    }

    [Fact]
    public void Use_NullCallback_Throws()
    {
        var target = new FakeMiddlewareTarget();

        Should.Throw<ArgumentNullException>(() => target.Use((Func<EsiContext, EsiNext, ValueTask>)null!));
    }

    [Fact]
    public void Use_NullTarget_Throws()
    {
        FakeMiddlewareTarget target = null!;

        Should.Throw<ArgumentNullException>(() => target.Use((ctx, next) => next(ctx)));
    }

    private sealed class RecordingMiddleware(string name, List<string> order) : IEsiMiddleware
    {
        public async ValueTask InvokeAsync(EsiContext context, EsiNext next)
        {
            order.Add(name);
            await next(context);
        }
    }
}
