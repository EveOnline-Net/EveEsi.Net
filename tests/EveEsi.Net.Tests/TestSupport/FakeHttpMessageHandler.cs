using System.Net;
using System.Text;

namespace EveEsi.Net.Tests.TestSupport;

/// <summary>Answers with queued responses (204 once they run out) and records what was sent.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<SentRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Respond(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = json is null ? new ByteArrayContent([]) : new StringContent(json, Encoding.UTF8, "application/json"),
            };
            foreach (var (name, value) in headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // User-Agent is split into product tokens on read; on the wire they're space-separated.
        var headers = request.Headers.ToDictionary(
            h => h.Key,
            h => string.Join(h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ",", h.Value),
            StringComparer.OrdinalIgnoreCase);

        Requests.Add(new SentRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, headers));
        return _responses.TryDequeue(out var next)
            ? next()
            : new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) };
    }
}

internal sealed record SentRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body, IReadOnlyDictionary<string, string> Headers)
{
    public string PathAndQuery => Uri.PathAndQuery;
}
