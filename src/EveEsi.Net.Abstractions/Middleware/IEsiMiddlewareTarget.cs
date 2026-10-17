namespace EveEsi.Net.Abstractions.Middleware;

/// <summary>
/// Something middleware can be registered on: global options, a service, or a single request.
/// Every level implements only this member; the async-callback overload is
/// <see cref="EsiMiddlewareTargetExtensions.Use{TSelf}(TSelf, Func{EsiContext, EsiNext, ValueTask})"/>.
/// </summary>
/// <typeparam name="TSelf">The implementing type, so chained calls keep it.</typeparam>
public interface IEsiMiddlewareTarget<out TSelf>
    where TSelf : IEsiMiddlewareTarget<TSelf>
{
    /// <summary>Adds a middleware. Within a level, middleware runs in registration order.</summary>
    /// <param name="middleware">The middleware.</param>
    /// <returns>This target, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="middleware"/> is <see langword="null"/>.</exception>
    TSelf Use(IEsiMiddleware middleware);
}
