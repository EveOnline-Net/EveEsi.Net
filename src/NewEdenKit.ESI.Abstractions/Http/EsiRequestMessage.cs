using System.Text;
using System.Text.RegularExpressions;

namespace NewEdenKit.ESI.Abstractions.Http;

/// <summary>
/// The outgoing request as middleware sees it: route and query values as plain string pairs, headers and body.
/// The client builds a fresh <see cref="HttpRequestMessage"/> from it for every send, so middleware that calls
/// <c>next</c> more than once (retries) can resend it safely.
/// </summary>
public sealed partial class EsiRequestMessage
{
    /// <summary>Creates a request message.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pathTemplate">The path template, e.g. <c>/characters/{character_id}/assets</c>.</param>
    public EsiRequestMessage(HttpMethod method, string pathTemplate)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);
        Method = method;
        PathTemplate = pathTemplate;
    }

    /// <summary>The HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>The path template the route values are substituted into.</summary>
    public string PathTemplate { get; }

    /// <summary>Path values, already formatted as strings, e.g. <c>character_id = 2112625428</c>.</summary>
    public IList<KeyValuePair<string, string>> RouteValues { get; } = [];

    /// <summary>
    /// Query values, already formatted as strings, in send order. A name may repeat (one pair per array item).
    /// Includes pagination values (<c>page</c>, <c>before</c>, <c>after</c>, <c>limit</c>).
    /// </summary>
    public IList<KeyValuePair<string, string>> Query { get; } = [];

    /// <summary>
    /// Request headers, compared case-insensitively. Middleware adds conditional and language headers here
    /// (<c>If-None-Match</c>, <c>Accept-Language</c>, …). Headers the client owns are applied at send time and override these.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The serialized JSON body, or <see langword="null"/> when there is none.</summary>
    public ReadOnlyMemory<byte>? Body { get; set; }

    /// <summary>
    /// Builds the escaped path and query, e.g. <c>/characters/1/assets?page=2</c>. The client resolves it against the
    /// ESI base address; caching middleware can use it as part of a cache key.
    /// </summary>
    /// <exception cref="InvalidOperationException">A placeholder in the template has no route value.</exception>
    public string ToRelativeUri()
    {
        var path = PlaceholderPattern().Replace(PathTemplate, match =>
        {
            var name = match.Groups[1].Value;
            foreach (var (key, value) in RouteValues)
            {
                if (string.Equals(key, name, StringComparison.Ordinal))
                {
                    return Uri.EscapeDataString(value);
                }
            }

            throw new InvalidOperationException($"No route value for '{{{name}}}' in '{PathTemplate}'.");
        });

        if (Query.Count == 0)
        {
            return path;
        }

        var builder = new StringBuilder(path).Append('?');
        for (var i = 0; i < Query.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(Query[i].Key)).Append('=').Append(Uri.EscapeDataString(Query[i].Value));
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex PlaceholderPattern();
}
