using EveEsi.Net.Abstractions.Caching;
using EveEsi.Net.Abstractions.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace EveEsi.Net.Middleware.Caching;

/// <summary>The builder <c>EnableCaching</c> hands its configure delegate.</summary>
internal sealed class EsiCacheBuilder(IServiceCollection services) : IEsiCacheBuilder
{
    public IServiceCollection Services { get; } = services;

    public Func<IServiceProvider, IEsiCacheStore>? Store { get; private set; }

    public bool ETags { get; private set; } = true;

    public bool ResponseCache { get; private set; } = true;

    public IEsiCacheBuilder UseStore(Func<IServiceProvider, IEsiCacheStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (Store is not null)
        {
            throw new InvalidOperationException("EnableCaching(): a cache store was already chosen. Choose one store.");
        }

        Store = factory;
        return this;
    }

    public IEsiCacheBuilder DisableETags()
    {
        ETags = false;
        return this;
    }

    public IEsiCacheBuilder DisableResponseCache()
    {
        ResponseCache = false;
        return this;
    }
}
