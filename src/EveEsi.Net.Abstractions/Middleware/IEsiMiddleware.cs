namespace EveEsi.Net.Abstractions.Middleware;

/// <summary>
/// One step in the ESI call pipeline. A middleware can change the request before calling <c>next</c>, inspect or
/// replace <see cref="EsiContext.Response"/> after it, or short-circuit by setting the response without calling it.
/// </summary>
/// <remarks>
/// Instances are shared across concurrent calls, so they must not hold mutable state. Keep per-call state in
/// <see cref="EsiContext.Items"/> and cross-call state behind an injected abstraction.
/// </remarks>
public interface IEsiMiddleware
{
    /// <summary>Runs this step.</summary>
    /// <param name="context">The call's context.</param>
    /// <param name="next">The rest of the pipeline. Call it at most once, unless the middleware retries.</param>
    ValueTask InvokeAsync(EsiContext context, EsiNext next);
}
