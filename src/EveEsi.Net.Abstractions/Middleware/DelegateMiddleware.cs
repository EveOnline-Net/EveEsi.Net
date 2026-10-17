namespace EveEsi.Net.Abstractions.Middleware;

/// <summary>Adapts an async callback to <see cref="IEsiMiddleware"/>.</summary>
internal sealed class DelegateMiddleware(Func<EsiContext, EsiNext, ValueTask> callback) : IEsiMiddleware
{
    public ValueTask InvokeAsync(EsiContext context, EsiNext next) => callback(context, next);
}
