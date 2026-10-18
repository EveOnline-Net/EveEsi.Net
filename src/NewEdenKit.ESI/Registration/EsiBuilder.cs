using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Registration;

namespace NewEdenKit.ESI.Registration;

/// <summary>The builder <c>AddEveEsi()</c> returns. Holds the global middleware until the provider resolves it.</summary>
internal sealed class EsiBuilder(IServiceCollection services) : IEsiBuilder
{
    /// <summary>The typed client's name, so <c>ConfigureHttpClient</c> reaches the same client.</summary>
    public const string HttpClientName = "NewEdenKit.ESI";

    private readonly List<Func<IServiceProvider, IEsiMiddleware>> _middleware = [];

    public IServiceCollection Services { get; } = services;

    public IEsiBuilder Use(IEsiMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middleware.Add(_ => middleware);
        return this;
    }

    public IEsiBuilder Use<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IEsiMiddleware
    {
        Services.TryAddSingleton<TMiddleware>();
        _middleware.Add(provider => provider.GetRequiredService<TMiddleware>());
        return this;
    }

    /// <summary>The global middleware, in the order it was added, resolved from <paramref name="provider"/>.</summary>
    public IEsiMiddleware[] Resolve(IServiceProvider provider) => [.. _middleware.Select(resolve => resolve(provider))];
}
