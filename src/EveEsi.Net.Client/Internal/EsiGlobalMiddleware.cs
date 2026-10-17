using EveEsi.Net.Abstractions.Middleware;

namespace EveEsi.Net.Client.Internal;

/// <summary>The application-wide middleware, outermost in every pipeline, in registration order.</summary>
internal sealed class EsiGlobalMiddleware(IReadOnlyList<IEsiMiddleware> middleware)
{
    public static EsiGlobalMiddleware None { get; } = new([]);

    public IReadOnlyList<IEsiMiddleware> Middleware { get; } = middleware ?? throw new ArgumentNullException(nameof(middleware));
}
