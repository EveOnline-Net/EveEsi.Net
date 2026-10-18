using System.Collections.Concurrent;
using EveEsi.Net.Abstractions.Caching;

namespace EveEsi.Net.Middleware.Caching;

/// <summary>
/// An <see cref="IEsiCacheStore"/> in process memory, bounded by <see cref="InMemoryEsiCacheStoreOptions"/>. Safe for
/// concurrent use. Entries are lost on restart and aren't shared between instances; use an
/// <c>EveEsi.Net.Caching.*</c> store for that.
/// </summary>
public sealed class InMemoryEsiCacheStore : IEsiCacheStore
{
    private readonly ConcurrentDictionary<string, Slot> _entries = new(StringComparer.Ordinal);
    private readonly InMemoryEsiCacheStoreOptions _options;
    private readonly TimeProvider _time;
    private readonly object _trimLock = new();

    /// <summary>Creates a store.</summary>
    /// <param name="options">Limits; the defaults when <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    public InMemoryEsiCacheStore(InMemoryEsiCacheStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new InMemoryEsiCacheStoreOptions();
        _options.Validate();
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The number of entries held, including ones not yet dropped.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public ValueTask<EsiCacheEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryGetValue(key, out var slot))
        {
            return ValueTask.FromResult<EsiCacheEntry?>(null);
        }

        if (slot.DropAt <= _time.GetUtcNow())
        {
            // Only this slot: a newer entry set meanwhile stays.
            _entries.TryRemove(new KeyValuePair<string, Slot>(key, slot));
            return ValueTask.FromResult<EsiCacheEntry?>(null);
        }

        return ValueTask.FromResult<EsiCacheEntry?>(slot.Entry);
    }

    /// <inheritdoc />
    public ValueTask SetAsync(string key, EsiCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _time.GetUtcNow();
        var dropAt = entry.RetainUntil ?? entry.ExpiresAt + _options.DefaultRetention;
        if (dropAt <= now)
        {
            _entries.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        _entries[key] = new Slot(entry, dropAt);
        if (_entries.Count > _options.MaxEntries)
        {
            Trim(now);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    /// <summary>Drops expired entries, then the ones due soonest, down to 90% of the limit so trimming isn't constant.</summary>
    private void Trim(DateTimeOffset now)
    {
        lock (_trimLock)
        {
            if (_entries.Count <= _options.MaxEntries)
            {
                return;
            }

            var target = Math.Max(1, _options.MaxEntries - (_options.MaxEntries / 10));
            // ToArray is an atomic snapshot; enumerating the live dictionary while others write isn't safe to sort.
            foreach (var (key, slot) in _entries.ToArray().OrderBy(e => e.Value.DropAt))
            {
                if (_entries.Count <= target && slot.DropAt > now)
                {
                    break;
                }

                _entries.TryRemove(new KeyValuePair<string, Slot>(key, slot));
            }
        }
    }

    private sealed record Slot(EsiCacheEntry Entry, DateTimeOffset DropAt);
}
