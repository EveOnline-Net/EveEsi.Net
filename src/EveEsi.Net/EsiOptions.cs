using System.Diagnostics.CodeAnalysis;
using EveEsi.Net.Abstractions.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EveEsi.Net;

/// <summary>
/// What <c>AddEveEsi(o =&gt; …)</c> configures: who is calling ESI, where, and the global middleware every call runs
/// through.
/// </summary>
/// <remarks>
/// <see cref="ApplicationName"/> and <see cref="Contact"/> are required. CCP uses them to reach whoever operates the
/// application, so they describe <em>your</em> application, not this library. The library adds its own name, version and
/// ESI compatibility date to the <c>User-Agent</c> after them.
/// </remarks>
public sealed class EsiOptions : IEsiMiddlewareTarget<EsiOptions>
{
    /// <summary>ESI's production address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://esi.evetech.net/");

    /// <summary>The tenant sent as <c>X-Tenant</c> unless set: the live server.</summary>
    public const string DefaultTenant = "tranquility";

    private readonly List<Func<IServiceProvider, IEsiMiddleware>> _middleware = [];
    private readonly List<Action<IServiceCollection>> _registrations = [];

    /// <summary>
    /// Your application's name, optionally with its version, as a User-Agent product token: <c>MyApp/1.2</c>.
    /// Letters, digits and <c>!#$%&amp;'*+-.^_`|~</c> only; no spaces.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// How CCP can reach the application's operator: an email address, a website, an EVE character or a Discord
    /// handle. Sent as a User-Agent comment, so it can't contain parentheses.
    /// </summary>
    public string? Contact { get; set; }

    /// <summary>The ESI address. Defaults to <see cref="DefaultBaseAddress"/>.</summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>The server to call, sent as <c>X-Tenant</c>. Defaults to <see cref="DefaultTenant"/>.</summary>
    public string Tenant { get; set; } = DefaultTenant;

    /// <summary>The global middleware, in registration order. Each entry resolves its middleware.</summary>
    internal IReadOnlyList<Func<IServiceProvider, IEsiMiddleware>> Middleware => _middleware;

    /// <inheritdoc />
    /// <remarks>Global middleware is outermost: it runs before service and request middleware, for every call.</remarks>
    public EsiOptions Use(IEsiMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middleware.Add(_ => middleware);
        return this;
    }

    /// <summary>
    /// Adds a global middleware resolved from the service provider, so it can take injected dependencies such as an
    /// <c>IEsiCacheStore</c>. It's registered as a singleton unless the application registered it first.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type.</typeparam>
    /// <returns>These options, for chaining.</returns>
    /// <remarks>
    /// Middleware is shared by concurrent calls, so it must hold no per-call state. Keep that in
    /// <see cref="EsiContext.Items"/>.
    /// </remarks>
    public EsiOptions Use<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IEsiMiddleware
    {
        _middleware.Add(provider => provider.GetRequiredService<TMiddleware>());
        _registrations.Add(services => services.TryAddSingleton<TMiddleware>());
        return this;
    }

    /// <summary>Registers the types added with <see cref="Use{TMiddleware}"/>. Generic, so it stays trim safe.</summary>
    internal void RegisterMiddlewareTypes(IServiceCollection services) => _registrations.ForEach(register => register(services));
}
