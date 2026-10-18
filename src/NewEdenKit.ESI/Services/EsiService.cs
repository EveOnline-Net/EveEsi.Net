using System.Text.Json.Serialization.Metadata;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Requests;
using NewEdenKit.ESI.Abstractions.Results;

namespace NewEdenKit.ESI.Services;

/// <summary>
/// Base class for section services (<c>Assets</c>, <c>Characters</c>, …). Holds the instance's service-level
/// middleware and hands a snapshot of it to the factory with every request it builds.
/// </summary>
/// <typeparam name="TSelf">The service interface the derived class implements, e.g. <c>IAssets</c>.</typeparam>
/// <remarks>
/// Services are registered as transient, so a newly resolved service starts with no service-level middleware.
/// Middleware added with <see cref="Use"/> applies to requests built afterwards, not to ones that already exist.
/// </remarks>
public abstract class EsiService<TSelf> : IEsiService<TSelf>
    where TSelf : class, IEsiService<TSelf>
{
    private readonly IEsiClientFactory _factory;

    // Replaced, never mutated, so the array handed to a request is a stable snapshot.
    private IEsiMiddleware[] _middleware = [];

    /// <summary>Creates the service.</summary>
    /// <param name="factory">The factory that builds and validates requests.</param>
    /// <exception cref="InvalidOperationException">The derived class doesn't implement <typeparamref name="TSelf"/>.</exception>
    protected EsiService(IEsiClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (this is not TSelf)
        {
            throw new InvalidOperationException($"{GetType().Name} must implement {typeof(TSelf).Name}.");
        }

        _factory = factory;
    }

    /// <inheritdoc />
    public TSelf Use(IEsiMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middleware = [.. _middleware, middleware];
        return (TSelf)(object)this;
    }

    /// <summary>Builds a request for an endpoint that returns a body, with this service's middleware.</summary>
    /// <typeparam name="T">The response type. Write it explicitly; it must match <paramref name="responseType"/>.</typeparam>
    /// <param name="operationId">The spec's <c>operationId</c>, the route table key.</param>
    /// <param name="responseType">Deserialization metadata for the response, from <c>EsiJsonContext</c>.</param>
    /// <param name="configure">Sets route and query values, the body and pagination; <see langword="null"/> for none.</param>
    /// <param name="token">The SSO access token, or <see langword="null"/> for public endpoints.</param>
    protected IEsiRequest<T> CreateEsiClient<T>(
        string operationId,
        JsonTypeInfo<T> responseType,
        Action<IEsiRequestParameters>? configure,
        string? token) =>
        _factory.CreateEsiClient(operationId, responseType, configure, token, _middleware);

    /// <summary>Builds a request for an endpoint that returns no body, with this service's middleware.</summary>
    /// <param name="operationId">The spec's <c>operationId</c>, the route table key.</param>
    /// <param name="configure">Sets route and query values, the body and pagination; <see langword="null"/> for none.</param>
    /// <param name="token">The SSO access token, or <see langword="null"/> for public endpoints.</param>
    protected IEsiRequest<EsiNoContent> CreateEsiClient(
        string operationId,
        Action<IEsiRequestParameters>? configure,
        string? token) =>
        _factory.CreateEsiClient(operationId, configure, token, _middleware);
}
