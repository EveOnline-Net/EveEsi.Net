using EveEsi.Net.Abstractions.Caching;
using EveEsi.Net.Abstractions.Registration;
using EveEsi.Net.Middleware.Caching;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The conventional namespace for registration extensions, like AddEveEsi().
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Turns on response caching for the ESI client, and chooses where responses are kept.</summary>
public static class EsiCacheBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="CacheMiddleware"/> and <see cref="ETagMiddleware"/> to the global middleware, at this point in
    /// the chain, with the store chosen in <paramref name="configure"/>. Without a store, a
    /// <see cref="FileEsiCacheStore"/> in <see cref="FileEsiCacheStoreOptions.DefaultDirectory"/> is used.
    /// </summary>
    /// <param name="builder">The builder from <c>AddEveEsi()</c>.</param>
    /// <param name="configure">Chooses the store and turns either middleware off; optional.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Caching is already enabled, two stores were chosen, or both middleware were turned off.
    /// </exception>
    /// <remarks>
    /// An <see cref="IEsiCacheStore"/> the application registered itself is kept over the one chosen here.
    /// <code>
    /// services.AddEveEsi(o =&gt; …).EnableCaching(c =&gt; c.AddFileCacheStore("cache/esi"));
    /// </code>
    /// </remarks>
    public static IEsiBuilder EnableCaching(this IEsiBuilder builder, Action<IEsiCacheBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        if (services.Any(d => d.ServiceType == typeof(EsiCacheBuilder)))
        {
            throw new InvalidOperationException("EnableCaching() has already been called.");
        }

        var cache = new EsiCacheBuilder(services);
        configure?.Invoke(cache);
        if (!cache.ResponseCache && !cache.ETags)
        {
            throw new InvalidOperationException("EnableCaching(): both the response cache and ETags are turned off, so nothing would be cached.");
        }

        services.AddSingleton(cache);
        services.TryAddSingleton(cache.Store ?? DefaultStore);
        if (cache.ResponseCache)
        {
            builder.Use<CacheMiddleware>();
        }

        if (cache.ETags)
        {
            builder.Use<ETagMiddleware>();
        }

        return builder;
    }

    /// <summary>Keeps responses in process memory, bounded by the options. Lost on restart.</summary>
    /// <param name="cache">The cache builder.</param>
    /// <param name="configure">Sets the store's limits; optional.</param>
    /// <returns>The cache builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    /// <exception cref="InvalidOperationException">A store was already chosen.</exception>
    /// <remarks>The store uses a registered <see cref="TimeProvider"/> if there is one.</remarks>
    public static IEsiCacheBuilder AddInMemoryCacheStore(this IEsiCacheBuilder cache, Action<InMemoryEsiCacheStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var options = new InMemoryEsiCacheStoreOptions();
        configure?.Invoke(options);
        options.Validate();
        return cache.UseStore(provider => new InMemoryEsiCacheStore(options, provider.GetService<TimeProvider>()));
    }

    /// <summary>Keeps responses in a local folder, across restarts and shared by processes on the machine.</summary>
    /// <param name="cache">The cache builder.</param>
    /// <param name="directory">
    /// The folder; created if missing. <see langword="null"/> for <see cref="FileEsiCacheStoreOptions.DefaultDirectory"/>.
    /// </param>
    /// <param name="configure">Sets the store's limits; optional.</param>
    /// <returns>The cache builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    /// <exception cref="InvalidOperationException">A store was already chosen.</exception>
    /// <remarks>
    /// The store uses a registered <see cref="TimeProvider"/> if there is one. The folder is created when the store is
    /// first resolved.
    /// </remarks>
    public static IEsiCacheBuilder AddFileCacheStore(this IEsiCacheBuilder cache, string? directory = null, Action<FileEsiCacheStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var options = new FileEsiCacheStoreOptions { Directory = directory };
        configure?.Invoke(options);
        options.Validate();
        return cache.UseStore(provider => new FileEsiCacheStore(options, provider.GetService<TimeProvider>()));
    }

    private static FileEsiCacheStore DefaultStore(IServiceProvider provider) =>
        new FileEsiCacheStore(new FileEsiCacheStoreOptions(), provider.GetService<TimeProvider>());
}
