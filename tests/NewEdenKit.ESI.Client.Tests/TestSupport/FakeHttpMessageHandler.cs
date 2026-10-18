using System.Net;
using System.Text;

namespace NewEdenKit.ESI.Client.Tests.TestSupport;

/// <summary>Returns canned responses and records what was sent. Never touches the network.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Respond(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = json is null ? new ByteArrayContent([]) : new StringContent(json, Encoding.UTF8, "application/json"),
            };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
        return this;
    }

    public FakeHttpMessageHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            // User-Agent is split into product tokens on read; on the wire they're space-separated.
            request.Headers.ToDictionary(
                h => h.Key,
                h => string.Join(h.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ? " " : ",", h.Value),
                StringComparer.OrdinalIgnoreCase),
            body,
            request.Content?.Headers.ContentType?.ToString()));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No canned response left.");
        }

        return _responses.Dequeue()(request);
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? ContentType);
