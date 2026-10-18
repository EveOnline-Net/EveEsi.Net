using System.Net;

namespace EveEsi.Net.Tests.TestSupport;

/// <summary>Answers every request with 204 and records what was sent.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<SentRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new SentRequest(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body));
        return new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) };
    }
}

internal sealed record SentRequest(HttpMethod Method, string PathAndQuery, string? Authorization, string? Body);
