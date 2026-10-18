namespace EveEsi.Net.Middleware.Caching;

/// <summary>Limits for <see cref="InMemoryEsiCacheStore"/>.</summary>
public sealed class InMemoryEsiCacheStoreOptions
{
    /// <summary>
    /// The most entries kept. Past it, expired entries go first, then those due to be dropped soonest. Default 10,000.
    /// </summary>
    public int MaxEntries { get; set; } = 10_000;

    /// <summary>
    /// How long past its expiry an entry without its own <see cref="Abstractions.Caching.EsiCacheEntry.RetainUntil"/> is
    /// kept, so an ETag can still revalidate it. Default one day.
    /// </summary>
    public TimeSpan DefaultRetention { get; set; } = TimeSpan.FromDays(1);

    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries, nameof(MaxEntries));
        ArgumentOutOfRangeException.ThrowIfLessThan(DefaultRetention, TimeSpan.Zero, nameof(DefaultRetention));
    }
}
