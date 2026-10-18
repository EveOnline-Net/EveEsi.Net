using System.Text.Json.Serialization.Metadata;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Requests;
using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Client.Internal;

internal sealed class EsiRequest<T>(EsiCall call, JsonTypeInfo<T>? responseType) : IEsiRequest<T>
{
    // Replaced, never mutated, so an execution in flight keeps the list it started with.
    private IEsiMiddleware[] _middleware = [];

    public RouteDefinition Route => call.Route;

    public IEsiRequest<T> Use(IEsiMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middleware = [.. _middleware, middleware];
        return this;
    }

    public async Task<EsiResult<T>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var outcome = await call.RunAsync(_middleware, cancellationToken).ConfigureAwait(false);
        return EsiResponseDecoder.Decode(outcome, responseType);
    }
}
