using EveEsi.Net.Abstractions.Http;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Results;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Client.Internal;

/// <summary>The outcome of running the pipeline: a raw response, or a transport failure.</summary>
internal readonly record struct EsiCallOutcome(EsiResponse? Response, EsiError? TransportError);

/// <summary>
/// Everything a validated request needs to run, independent of its response type. Immutable, so one request can be
/// executed more than once, and concurrently.
/// </summary>
internal sealed class EsiCall(
    RouteDefinition route,
    EsiRequestParameters parameters,
    string? token,
    IReadOnlyList<IEsiMiddleware> globalMiddleware,
    IReadOnlyList<IEsiMiddleware> serviceMiddleware,
    EsiHttpClient httpClient)
{
    public RouteDefinition Route => route;

    public async Task<EsiCallOutcome> RunAsync(IReadOnlyList<IEsiMiddleware> requestMiddleware, CancellationToken cancellationToken)
    {
        // Each execution gets its own message: middleware may edit it, the frozen parameters stay as built.
        var message = new EsiRequestMessage(route.Method, route.PathTemplate) { Body = parameters.BodyBytes };
        foreach (var pair in parameters.RoutePairs)
        {
            message.RouteValues.Add(pair);
        }

        foreach (var pair in parameters.QueryPairs)
        {
            message.Query.Add(pair);
        }

        var context = new EsiContext(route, message, token, cancellationToken);
        var pipeline = EsiPipeline.Build([.. globalMiddleware, .. serviceMiddleware, .. requestMiddleware], httpClient.SendAsync);

        try
        {
            await pipeline(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return new EsiCallOutcome(null, new EsiError(EsiErrorKind.Transport, TransportMessage(ex)) { Exception = ex });
        }

        return context.Response is { } response
            ? new EsiCallOutcome(response, null)
            : throw new InvalidOperationException(
                $"The middleware pipeline for {route} finished without a response. A middleware that doesn't call next must set context.Response.");
    }

    // The caller's own cancellation propagates; anything else from the network (including HttpClient's timeout,
    // which surfaces as a TaskCanceledException) becomes a failed result.
    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or IOException
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static string TransportMessage(Exception ex) =>
        ex is OperationCanceledException ? "The request to ESI timed out." : $"The request to ESI failed: {ex.Message}";
}
