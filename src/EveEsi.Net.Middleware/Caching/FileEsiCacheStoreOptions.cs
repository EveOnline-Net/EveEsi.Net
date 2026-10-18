namespace EveEsi.Net.Middleware.Caching;

/// <summary>Where and how much <see cref="FileEsiCacheStore"/> keeps.</summary>
public sealed class FileEsiCacheStoreOptions
{
    /// <summary>The folder entries are written to. Created if missing. Required.</summary>
    public string? Directory { get; set; }

    /// <summary>
    /// The most entries kept. A sweep past it drops expired entries first, then those due to be dropped soonest.
    /// Default 10,000.
    /// </summary>
    public int MaxEntries { get; set; } = 10_000;

    /// <summary>
    /// How long past its expiry an entry without its own <see cref="Abstractions.Caching.EsiCacheEntry.RetainUntil"/> is
    /// kept, so an ETag can still revalidate it. Default one day.
    /// </summary>
    public TimeSpan DefaultRetention { get; set; } = TimeSpan.FromDays(1);

    /// <summary>How often a write also sweeps the folder for entries past their retention. Default ten minutes.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <exception cref="ArgumentException"><see cref="Directory"/> is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Directory, nameof(Directory));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries, nameof(MaxEntries));
        ArgumentOutOfRangeException.ThrowIfLessThan(DefaultRetention, TimeSpan.Zero, nameof(DefaultRetention));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SweepInterval, TimeSpan.Zero, nameof(SweepInterval));
    }
}
