using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NewEdenKit.ESI.Abstractions.Http;

/// <summary>
/// Immutable response headers with typed access to the ones ESI uses. Names are compared case-insensitively.
/// Typed accessors return <see langword="null"/> when a header is missing or malformed.
/// </summary>
public sealed class EsiResponseHeaders : IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>
{
    private readonly Dictionary<string, IReadOnlyList<string>> _headers;

    /// <summary>Creates headers from name/values pairs, such as <see cref="System.Net.Http.Headers.HttpHeaders"/>.</summary>
    /// <param name="headers">The headers. Repeated names are merged in order.</param>
    public EsiResponseHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in headers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(headers));
            if (!merged.TryGetValue(name, out var list))
            {
                merged[name] = list = [];
            }

            list.AddRange(values ?? []);
        }

        _headers = merged.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.AsReadOnly(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>No headers.</summary>
    public static EsiResponseHeaders Empty { get; } = new([]);

    /// <summary>The number of distinct header names.</summary>
    public int Count => _headers.Count;

    /// <summary><c>ETag</c>, including its quotes, as ESI sent it.</summary>
    public string? ETag => GetValue("ETag");

    /// <summary><c>Date</c>: when ESI produced the response, by ESI's clock.</summary>
    public DateTimeOffset? Date => GetHttpDate("Date");

    /// <summary><c>Expires</c>.</summary>
    public DateTimeOffset? Expires => GetHttpDate("Expires");

    /// <summary><c>Last-Modified</c>.</summary>
    public DateTimeOffset? LastModified => GetHttpDate("Last-Modified");

    /// <summary><c>Cache-Control</c>, raw.</summary>
    public string? CacheControl => GetValue("Cache-Control");

    /// <summary>The <c>max-age</c> directive of <c>Cache-Control</c>.</summary>
    public TimeSpan? MaxAge
    {
        get
        {
            foreach (var directive in (CacheControl ?? "").Split(',', StringSplitOptions.TrimEntries))
            {
                if (directive.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(directive.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return null;
        }
    }

    /// <summary><c>Content-Language</c> of a localized response.</summary>
    public string? ContentLanguage => GetValue("Content-Language");

    /// <summary><c>X-Pages</c>: the total page count of a page-based endpoint.</summary>
    public int? Pages => GetInt32("X-Pages");

    /// <summary><c>X-ESI-Error-Limit-Remain</c>: errors left in the current error-limit window.</summary>
    public int? ErrorLimitRemain => GetInt32("X-ESI-Error-Limit-Remain");

    /// <summary><c>X-ESI-Error-Limit-Reset</c>: time until the error-limit window resets.</summary>
    public TimeSpan? ErrorLimitReset => GetSeconds("X-ESI-Error-Limit-Reset");

    /// <summary><c>X-Ratelimit-Group</c>: the rate-limit bucket the request was counted against.</summary>
    public string? RateLimitGroup => GetValue("X-Ratelimit-Group");

    /// <summary><c>X-Ratelimit-Limit</c>, raw, e.g. <c>150/15m</c>.</summary>
    public string? RateLimitLimit => GetValue("X-Ratelimit-Limit");

    /// <summary><c>X-Ratelimit-Remaining</c>: tokens left in the bucket.</summary>
    public long? RateLimitRemaining => GetInt64("X-Ratelimit-Remaining");

    /// <summary><c>X-Ratelimit-Used</c>: tokens this request consumed.</summary>
    public long? RateLimitUsed => GetInt64("X-Ratelimit-Used");

    /// <summary><c>Retry-After</c> in its delta-seconds form. <see langword="null"/> for the HTTP-date form.</summary>
    public TimeSpan? RetryAfter => GetSeconds("Retry-After");

    /// <summary><c>X-ESI-Request-ID</c>, for reporting issues to CCP.</summary>
    public string? RequestId => GetValue("X-ESI-Request-ID");

    /// <summary>Gets all values of a header.</summary>
    public bool TryGetValues(string name, [NotNullWhen(true)] out IReadOnlyList<string>? values)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _headers.TryGetValue(name, out values);
    }

    /// <summary>Gets the first value of a header, or <see langword="null"/> if it's missing.</summary>
    public string? GetValue(string name) => TryGetValues(name, out var values) && values.Count > 0 ? values[0] : null;

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator() => _headers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int? GetInt32(string name) =>
        int.TryParse(GetValue(name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private long? GetInt64(string name) =>
        long.TryParse(GetValue(name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private TimeSpan? GetSeconds(string name) =>
        long.TryParse(GetValue(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? TimeSpan.FromSeconds(value) : null;

    private DateTimeOffset? GetHttpDate(string name) =>
        DateTimeOffset.TryParseExact(GetValue(name), "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
}
