using System.Net.Http.Headers;
using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Middleware;

namespace NewEdenKit.ESI.Client.Internal;

/// <summary>
/// The typed HTTP client the library sends through, registered with <c>AddHttpClient&lt;EsiHttpClient&gt;</c>.
/// It turns the pipeline's <see cref="EsiContext"/> into an <see cref="HttpRequestMessage"/>, sends it, and stores the
/// raw response on the context.
/// </summary>
/// <remarks>
/// It configures nothing itself: the base address and the headers ESI requires on every call (<c>User-Agent</c>,
/// <c>X-Compatibility-Date</c>, <c>X-Tenant</c>) are set on the <see cref="HttpClient"/> by the service registration.
/// Typed clients are transient: whatever holds one (the factory, and the requests it builds) must not outlive it by
/// much, or the underlying handler never rotates.
/// </remarks>
internal sealed class EsiHttpClient(HttpClient httpClient)
{
    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json") { CharSet = "utf-8" };

    /// <summary>
    /// The end of every pipeline. Builds a fresh <see cref="HttpRequestMessage"/> each time, so middleware can call
    /// <c>next</c> again (retries).
    /// </summary>
    public async ValueTask SendAsync(EsiContext context)
    {
        using var request = new HttpRequestMessage(context.Request.Method, new Uri(context.Request.ToRelativeUri(), UriKind.Relative));
        foreach (var (name, value) in context.Request.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (context.Request.Body is { } body)
        {
            request.Content = new ReadOnlyMemoryContent(body);
            request.Content.Headers.ContentType = JsonContentType;
        }

        // Read at send time so middleware can replace the token (e.g. after a refresh).
        if (!string.IsNullOrEmpty(context.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, context.CancellationToken)
            .ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(context.CancellationToken).ConfigureAwait(false);

        context.Response = new EsiResponse(
            response.StatusCode,
            new EsiResponseHeaders(response.Headers.Concat(response.Content.Headers)),
            bytes);
    }
}
