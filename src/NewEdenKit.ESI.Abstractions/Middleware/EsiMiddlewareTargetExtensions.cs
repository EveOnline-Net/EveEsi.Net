namespace NewEdenKit.ESI.Abstractions.Middleware;

/// <summary>The async-callback form of <c>Use</c>, shared by every middleware level.</summary>
public static class EsiMiddlewareTargetExtensions
{
    /// <summary>Adds a middleware written as an async callback.</summary>
    /// <param name="target">Where to register it.</param>
    /// <param name="middleware">
    /// The callback. Call <c>next(ctx)</c> to continue the pipeline, or set <c>ctx.Response</c> and return to short-circuit.
    /// </param>
    /// <returns><paramref name="target"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Only a <see cref="ValueTask"/> overload exists. A parallel <see cref="Task"/> overload would make
    /// <c>async</c> lambdas ambiguous.
    /// </remarks>
    public static TSelf Use<TSelf>(this TSelf target, Func<EsiContext, EsiNext, ValueTask> middleware)
        where TSelf : IEsiMiddlewareTarget<TSelf>
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(middleware);
        return target.Use(new DelegateMiddleware(middleware));
    }
}
