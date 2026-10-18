using EveEsi.Net.Abstractions.Caching;
using EveEsi.Net.Middleware.Caching;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The conventional namespace for IServiceCollection extensions, like AddEveEsi().
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the built-in ESI cache stores: in memory, or in a local folder.</summary>
public static class EsiCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryEsiCacheStore"/> as the <see cref="IEsiCacheStore"/> singleton, for
    /// <see cref="CacheMiddleware"/> and <see cref="ETagMiddleware"/>. A store the application registered first is kept.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the store's limits; optional.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    /// <remarks>The store uses a registered <see cref="TimeProvider"/> if there is one.</remarks>
    public static IServiceCollection AddEsiInMemoryCacheStore(this IServiceCollection services, Action<InMemoryEsiCacheStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new InMemoryEsiCacheStoreOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton<IEsiCacheStore>(provider => new InMemoryEsiCacheStore(options, provider.GetService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Registers <see cref="FileEsiCacheStore"/> as the <see cref="IEsiCacheStore"/> singleton, keeping entries in
    /// <paramref name="directory"/> across restarts. A store the application registered first is kept.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="directory">The folder for cache files; created if missing.</param>
    /// <param name="configure">Sets the store's limits; optional.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    /// <remarks>The store uses a registered <see cref="TimeProvider"/> if there is one. The folder is created when the store is first resolved.</remarks>
    public static IServiceCollection AddEsiFileCacheStore(this IServiceCollection services, string directory, Action<FileEsiCacheStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new FileEsiCacheStoreOptions { Directory = directory };
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton<IEsiCacheStore>(provider => new FileEsiCacheStore(options, provider.GetService<TimeProvider>()));
        return services;
    }
}
