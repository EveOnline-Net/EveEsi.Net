using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NewEdenKit.ESI.Abstractions.Caching;
using NewEdenKit.ESI.Abstractions.Registration;

// The conventional namespace for registration extensions, like AddEveEsi() and EnableCaching().
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Chooses an application's own <see cref="IEsiCacheStore"/> by type.</summary>
public static class EsiCacheStoreExtensions
{
    /// <summary>
    /// Chooses <typeparamref name="TStore"/> as the cache store, created by the container so its constructor can take
    /// registered services (a connection, options, a logger).
    /// </summary>
    /// <typeparam name="TStore">The store. Registered as a singleton unless the application registered it already.</typeparam>
    /// <param name="cache">The cache builder from <c>EnableCaching(c =&gt; …)</c>.</param>
    /// <returns>The cache builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A store was already chosen.</exception>
    /// <remarks>
    /// An application registration of <typeparamref name="TStore"/> made before or after this call is kept, so a
    /// factory or another lifetime can be supplied that way. The store is shared by concurrent calls.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddEveEsi(o =&gt; { o.ApplicationName = "MyApp/1.2"; o.Contact = "admin@example.com"; })
    ///     .EnableCaching(c =&gt; c.UseStore&lt;MyCacheStore&gt;());
    /// </code>
    /// </example>
    public static IEsiCacheBuilder UseStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IEsiCacheBuilder cache)
        where TStore : class, IEsiCacheStore
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.UseStore(provider => provider.GetRequiredService<TStore>());
        cache.Services.TryAddSingleton<TStore>();
        return cache;
    }
}
