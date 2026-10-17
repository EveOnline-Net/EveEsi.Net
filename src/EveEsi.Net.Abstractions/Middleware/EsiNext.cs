namespace EveEsi.Net.Abstractions.Middleware;

/// <summary>Invokes the rest of the pipeline: the next middleware, or the HTTP send at the end.</summary>
/// <param name="context">The call's context.</param>
public delegate ValueTask EsiNext(EsiContext context);
