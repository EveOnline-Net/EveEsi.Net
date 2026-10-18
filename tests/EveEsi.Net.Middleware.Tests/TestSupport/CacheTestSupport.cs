using System.Globalization;
using System.Net;
using System.Text;
using EveEsi.Net.Abstractions.Caching;
using EveEsi.Net.Abstractions.Http;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Middleware.Tests.TestSupport;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Builds routes, contexts and responses shaped like ESI's.</summary>
internal static class Esi
{
    public static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static RouteDefinition Route(string? scope = null, RouteCacheHints? cache = null, HttpMethod? method = null) =>
        new("GetThingsThingId", method ?? HttpMethod.Get, "/things/{thing_id}", scope, cache);

    public static EsiContext Context(RouteDefinition route, string? token = null, long thingId = 1, int? page = null)
    {
        var request = new EsiRequestMessage(route.Method, route.PathTemplate);
        request.RouteValues.Add(new("thing_id", thingId.ToString(CultureInfo.InvariantCulture)));
        if (page is { } p)
        {
            request.Query.Add(new("page", p.ToString(CultureInfo.InvariantCulture)));
        }

        return new EsiContext(route, request, token, TestContext.Current.CancellationToken);
    }

    public static EsiResponse Response(HttpStatusCode status, string body, params (string Name, string Value)[] headers) =>
        new(status, new EsiResponseHeaders(headers.Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Name, [h.Value]))), Encoding.UTF8.GetBytes(body));

    /// <summary>An HTTP date (<c>Sun, 18 Oct 2026 12:00:00 GMT</c>).</summary>
    public static string HttpDate(DateTimeOffset value) => value.ToString("r", CultureInfo.InvariantCulture);

    /// <summary>A 200 as ESI sends it: Date, Expires <paramref name="lifetime"/> later, and an ETag.</summary>
    public static EsiResponse Ok(string body, DateTimeOffset date, TimeSpan lifetime, string? etag = "\"v1\"")
    {
        var headers = new List<(string, string)> { ("Date", HttpDate(date)), ("Expires", HttpDate(date + lifetime)) };
        if (etag is not null)
        {
            headers.Add(("ETag", etag));
        }

        return Response(HttpStatusCode.OK, body, [.. headers]);
    }

    /// <summary>An unsigned JWT with the given subject; enough for the cache key, which never validates it.</summary>
    public static string Token(string subject, string id = "jti-1")
    {
        static string Part(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("""{"alg":"RS256"}""")}.{Part($$"""{"scp":["esi-assets.read_assets.v1"],"jti":"{{id}}","sub":"{{subject}}"}""")}.signature";
    }

    public static string Body(this EsiResponse response) => Encoding.UTF8.GetString(response.Body.Span);
}

/// <summary>Stands in for the rest of the pipeline: answers with queued responses and records each call's headers.</summary>
internal sealed class FakeNext
{
    private readonly Queue<Func<EsiContext, EsiResponse>> _responses = new();

    public List<IReadOnlyDictionary<string, string>> Calls { get; } = [];

    public FakeNext Respond(EsiResponse response) => Respond(_ => response);

    public FakeNext Respond(Func<EsiContext, EsiResponse> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public ValueTask Invoke(EsiContext context)
    {
        Calls.Add(new Dictionary<string, string>(context.Request.Headers, StringComparer.OrdinalIgnoreCase));
        context.Response = _responses.Dequeue()(context);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Counts writes to an inner store.</summary>
internal sealed class CountingStore(IEsiCacheStore inner) : IEsiCacheStore
{
    public int Sets { get; private set; }

    public ValueTask<EsiCacheEntry?> GetAsync(string key, CancellationToken cancellationToken) => inner.GetAsync(key, cancellationToken);

    public ValueTask SetAsync(string key, EsiCacheEntry entry, CancellationToken cancellationToken)
    {
        Sets++;
        return inner.SetAsync(key, entry, cancellationToken);
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken) => inner.RemoveAsync(key, cancellationToken);
}
