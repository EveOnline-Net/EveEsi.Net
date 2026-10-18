using NewEdenKit.ESI.Abstractions.Middleware;

namespace NewEdenKit.ESI.Client.Internal;

internal static class EsiPipeline
{
    /// <summary>Chains middleware in order (first = outermost) around <paramref name="terminal"/>.</summary>
    public static EsiNext Build(IReadOnlyList<IEsiMiddleware> middleware, EsiNext terminal)
    {
        var next = terminal;
        for (var i = middleware.Count - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var inner = next;
            next = context => current.InvokeAsync(context, inner);
        }

        return next;
    }
}
