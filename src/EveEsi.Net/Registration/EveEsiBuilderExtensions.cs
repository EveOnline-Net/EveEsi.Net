using EveEsi.Net.Abstractions.Registration;
using EveEsi.Net.Registration;

// The conventional namespace for registration extensions, like AddEveEsi().
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>HTTP client settings for the ESI client.</summary>
public static class EveEsiBuilderExtensions
{
    /// <summary>
    /// Configures the typed <c>HttpClient</c> ESI calls go through: add message handlers (logging, a proxy), set the
    /// primary handler or a timeout. The ESI headers and base address are already set.
    /// </summary>
    /// <param name="builder">The builder from <c>AddEveEsi()</c>.</param>
    /// <param name="configure">Configures the client's <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>Retries, caching and rate limits belong in middleware, which sees the ESI route; handlers only see HTTP.</remarks>
    public static IEsiBuilder ConfigureHttpClient(this IEsiBuilder builder, Action<IHttpClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Services.AddHttpClient(EsiBuilder.HttpClientName));
        return builder;
    }
}
