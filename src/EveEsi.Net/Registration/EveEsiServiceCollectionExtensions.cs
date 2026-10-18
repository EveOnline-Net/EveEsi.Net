using EveEsi.Net;
using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Client.Internal;
using EveEsi.Net.Registration;
using EveEsi.Net.Routes;
using EveEsi.Net.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The conventional namespace for IServiceCollection extensions, so AddEveEsi() shows up without an extra using.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers EveEsi.Net.</summary>
public static class EveEsiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ESI services (<c>IAssets</c>, <c>ICharacter</c>, …), the request factory and the typed HTTP client,
    /// and configures the headers ESI requires on every call: <c>User-Agent</c>, <c>X-Compatibility-Date</c> and
    /// <c>X-Tenant</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Sets <see cref="EsiOptions.ApplicationName"/> and <see cref="EsiOptions.Contact"/> (both required), and optionally
    /// the base address, tenant and global middleware. It runs once, here.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A required option is missing or invalid, or <c>AddEveEsi</c> was already called on this collection.
    /// </exception>
    /// <remarks>
    /// The options are validated immediately, so a misconfiguration fails at startup rather than on the first call.
    /// Services and the factory are transient; a service the application registered first is kept.
    /// </remarks>
    public static IServiceCollection AddEveEsi(this IServiceCollection services, Action<EsiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(d => d.ServiceType == typeof(EsiRegistration)))
        {
            throw new InvalidOperationException("AddEveEsi() has already been called on this service collection.");
        }

        var options = new EsiOptions();
        configure(options);
        var userAgent = EsiUserAgent.Build(options);
        if (!options.BaseAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"AddEveEsi(): BaseAddress \"{options.BaseAddress}\" must be an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(options.Tenant))
        {
            throw new InvalidOperationException("AddEveEsi(): Tenant can't be empty.");
        }

        var baseAddress = options.BaseAddress;
        var tenant = options.Tenant;
        var middleware = options.Middleware.ToArray();

        services.AddSingleton<EsiRegistration>();
        services.AddHttpClient<EsiHttpClient>(client =>
        {
            client.BaseAddress = baseAddress;
            foreach (var product in userAgent)
            {
                client.DefaultRequestHeaders.UserAgent.Add(product);
            }

            client.DefaultRequestHeaders.Add("X-Compatibility-Date", EsiSpec.CompatibilityDate);
            client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        });

        options.RegisterMiddlewareTypes(services);
        services.TryAddSingleton(new EsiRouteTable(EsiRoutes.All));
        services.TryAddSingleton(provider => new EsiGlobalMiddleware([.. middleware.Select(resolve => resolve(provider))]));
        services.TryAddTransient<IEsiClientFactory, EsiClientFactory>();
        EsiServices.AddGeneratedServices(services);
        return services;
    }
}
