using EveEsi.Net.Abstractions.Http;
using EveEsi.Net.Abstractions.Middleware;

namespace EveEsi.Net.Abstractions.Tests.TestSupport;

/// <summary>A minimal middleware level: records registrations and runs them in order.</summary>
internal sealed class FakeMiddlewareTarget : IEsiMiddlewareTarget<FakeMiddlewareTarget>
{
    public List<IEsiMiddleware> Middleware { get; } = [];

    public FakeMiddlewareTarget Use(IEsiMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        Middleware.Add(middleware);
        return this;
    }

    public ValueTask RunAsync(EsiContext context, EsiNext terminal)
    {
        var next = terminal;
        for (var i = Middleware.Count - 1; i >= 0; i--)
        {
            var middleware = Middleware[i];
            var inner = next;
            next = ctx => middleware.InvokeAsync(ctx, inner);
        }

        return next(context);
    }

    public static EsiContext NewContext() =>
        new(TestRoutes.CharacterRoute(), new EsiRequestMessage(HttpMethod.Get, "/characters/{character_id}"));
}
