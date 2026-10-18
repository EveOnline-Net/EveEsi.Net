using NewEdenKit.ESI.Abstractions.Http;

namespace NewEdenKit.ESI.Abstractions.Caching;

/// <summary>A cached raw response.</summary>
/// <param name="Response">The raw response: status, headers and body.</param>
/// <param name="ExpiresAt">When the response stops being fresh. After that it may still be revalidated with its ETag.</param>
public sealed record EsiCacheEntry(EsiResponse Response, DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// When the store may drop the entry. Later than <see cref="ExpiresAt"/> when the entry is kept for ETag
    /// revalidation. <see langword="null"/> means the store decides.
    /// </summary>
    public DateTimeOffset? RetainUntil { get; init; }
}
