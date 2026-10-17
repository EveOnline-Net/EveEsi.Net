using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Abstractions.Results;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Client.Internal;

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
