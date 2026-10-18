using System.Diagnostics.CodeAnalysis;
using EveEsi.Net.Abstractions.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace EveEsi.Net.Abstractions.Registration;

/// <summary>
/// What <c>services.AddEveEsi(…)</c> returns: the place to add global middleware, a cache store and anything else that
/// belongs to the ESI client. Packages extend it with their own <c>Add…</c> methods.
/// </summary>
/// <remarks>
/// Global middleware is outermost: it runs before service and request middleware, for every call, in the order it was
/// added. Add everything before the service provider is built.
/// </remarks>
public interface IEsiBuilder : IEsiMiddlewareTarget<IEsiBuilder>
{
    /// <summary>The service collection the client is registered in, for extension methods.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds a global middleware resolved from the service provider, so it can take injected dependencies such as an
    /// <c>IEsiCacheStore</c>. It's registered as a singleton unless the application registered it first.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Middleware is shared by concurrent calls, so it must hold no per-call state. Keep that in
    /// <see cref="EsiContext.Items"/>.
    /// </remarks>
    IEsiBuilder Use<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IEsiMiddleware;
}
