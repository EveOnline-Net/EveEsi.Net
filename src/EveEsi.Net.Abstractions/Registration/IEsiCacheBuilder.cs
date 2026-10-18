using EveEsi.Net.Abstractions.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace EveEsi.Net.Abstractions.Registration;

/// <summary>
/// What <c>EnableCaching(c =&gt; …)</c> configures: the cache store, and which caching middleware runs. Store packages
/// extend it with their own <c>Add…CacheStore</c> methods, which call <see cref="UseStore"/>.
/// </summary>
public interface IEsiCacheBuilder
{
    /// <summary>The service collection the client is registered in, for extension methods.</summary>
    IServiceCollection Services { get; }

    /// <summary>Chooses the cache store. One store per application.</summary>
    /// <param name="factory">Creates the store; it's registered as a singleton.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A store was already chosen.</exception>
    IEsiCacheBuilder UseStore(Func<IServiceProvider, IEsiCacheStore> factory);

    /// <summary>Leaves out ETag revalidation: stale entries are downloaded again instead of revalidated.</summary>
    /// <returns>This builder, for chaining.</returns>
    IEsiCacheBuilder DisableETags();

    /// <summary>Leaves out serving fresh entries without calling ESI: every call asks ESI, revalidating with its ETag.</summary>
    /// <returns>This builder, for chaining.</returns>
    IEsiCacheBuilder DisableResponseCache();
}
