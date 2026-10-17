using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Results;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>
/// A built, validated ESI call, ready to run. Add request-level middleware with <c>Use</c>, then call
/// <see cref="ExecuteAsync"/>.
/// </summary>
/// <typeparam name="T">The response type.</typeparam>
public interface IEsiRequest<T> : IEsiMiddlewareTarget<IEsiRequest<T>>
{
    /// <summary>The endpoint this request calls.</summary>
    RouteDefinition<T> Route { get; }

    /// <summary>
    /// Sends the request through the pipeline (global → service → request middleware → HTTP) and decodes the response.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. HTTP errors and transport failures are failed results, not exceptions.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<EsiResult<T>> ExecuteAsync(CancellationToken cancellationToken = default);
}
