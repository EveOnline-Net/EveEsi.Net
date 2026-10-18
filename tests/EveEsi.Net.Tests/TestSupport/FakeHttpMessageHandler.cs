using System.Net;

namespace EveEsi.Net.Tests.TestSupport;

/// <summary>Answers every request with 204 and records what was sent.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<SentRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // User-Agent is split into product tokens on read; on the wire they're space-separated.
        var headers = request.Headers.ToDictionary(
            h => h.Key,
            h => string.Join(h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ",", h.Value),
            StringComparer.OrdinalIgnoreCase);

        Requests.Add(new SentRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, headers));
        return new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) };
    }
}

internal sealed record SentRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body, IReadOnlyDictionary<string, string> Headers)
{
    public string PathAndQuery => Uri.PathAndQuery;
}
