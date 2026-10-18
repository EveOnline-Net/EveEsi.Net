using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EveEsi.Net.Abstractions.Caching;
using EveEsi.Net.Abstractions.Http;

namespace EveEsi.Net.Middleware.Caching;

/// <summary>
/// An <see cref="IEsiCacheStore"/> in a local folder: entries survive restarts and can be shared by processes on the
/// same machine. One file per entry, named by a hash of its key.
/// </summary>
/// <remarks>
/// <para>
/// Writes go to a temporary file that is then renamed over the old entry, so a reader never sees half an entry. Each
/// file's last-write time is set to when it may be dropped, so sweeping and trimming need only the folder listing.
/// </para>
/// <para>
/// It's a cache: a file that can't be read (corrupt, locked, deleted meanwhile) is a miss and is removed, and a write
/// that fails because another process holds the file is skipped. Cancellation still throws.
/// </para>
/// </remarks>
public sealed class FileEsiCacheStore : IEsiCacheStore
{
    private const string Extension = ".esicache";
    private static readonly byte[] Magic = "ESC1"u8.ToArray();

    private readonly FileEsiCacheStoreOptions _options;
    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly object _sweepLock = new();
    private DateTimeOffset _nextSweep;

    /// <summary>Creates a store, and its folder if it doesn't exist.</summary>
    /// <param name="options">The folder and limits.</param>
    /// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No folder is set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    public FileEsiCacheStore(FileEsiCacheStoreOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _directory = Path.GetFullPath(options.Directory!);
        Directory.CreateDirectory(_directory);
        _nextSweep = _time.GetUtcNow() + options.SweepInterval;
    }

    /// <summary>The number of entry files in the folder, including ones not yet swept.</summary>
    public int Count => Directory.EnumerateFiles(_directory, "*" + Extension).Count();

    /// <inheritdoc />
    public async ValueTask<EsiCacheEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(key);

        byte[] bytes;
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // Being replaced or removed by someone else: a miss.
        }

        var stored = Read(bytes);
        if (stored is null || stored.Value.Key != key || stored.Value.DropAt <= _time.GetUtcNow())
        {
            TryDelete(path);
            return null;
        }

        return stored.Value.Entry;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, EsiCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _time.GetUtcNow();
        var path = PathFor(key);
        var dropAt = entry.RetainUntil ?? entry.ExpiresAt + _options.DefaultRetention;
        if (dropAt <= now)
        {
            TryDelete(path);
            return;
        }

        var temp = Path.Combine(_directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, Write(key, entry, dropAt), cancellationToken).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(temp, dropAt.UtcDateTime);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp); // Another process holds the entry; it's only a cache.
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        if (now >= _nextSweep)
        {
            Sweep(now);
        }
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(PathFor(key));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Deletes entries past their retention and leftover temporary files, then the entries due soonest while there are
    /// more than <see cref="FileEsiCacheStoreOptions.MaxEntries"/> (down to 90%, so trimming isn't constant). Runs
    /// on its own every <see cref="FileEsiCacheStoreOptions.SweepInterval"/> when entries are written.
    /// </summary>
    public void Sweep()
    {
        Sweep(_time.GetUtcNow());
    }

    private void Sweep(DateTimeOffset now)
    {
        if (!Monitor.TryEnter(_sweepLock))
        {
            return; // Another write is already sweeping.
        }

        try
        {
            _nextSweep = now + _options.SweepInterval;
            var folder = new DirectoryInfo(_directory);
            // Left behind by a crash mid-write. Temporary files carry real write times, not the store's clock.
            foreach (var temp in folder.EnumerateFiles("*.tmp").Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)))
            {
                TryDelete(temp.FullName);
            }

            var live = new List<FileInfo>();
            foreach (var file in folder.EnumerateFiles("*" + Extension))
            {
                if (file.LastWriteTimeUtc <= now.UtcDateTime)
                {
                    TryDelete(file.FullName);
                }
                else
                {
                    live.Add(file);
                }
            }

            if (live.Count > _options.MaxEntries)
            {
                var target = Math.Max(1, _options.MaxEntries - (_options.MaxEntries / 10));
                foreach (var file in live.OrderBy(f => f.LastWriteTimeUtc).Take(live.Count - target))
                {
                    TryDelete(file.FullName);
                }
            }
        }
        finally
        {
            Monitor.Exit(_sweepLock);
        }
    }

    private string PathFor(string key) =>
        Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + Extension);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // In use or already gone; a later sweep gets it.
        }
    }

    // Layout: "ESC1", int32 (little-endian) length of the JSON metadata, the metadata, then the raw body.
    private static byte[] Write(string key, EsiCacheEntry entry, DateTimeOffset dropAt)
    {
        using var meta = new MemoryStream();
        using (var json = new Utf8JsonWriter(meta))
        {
            json.WriteStartObject();
            json.WriteString("key", key);
            json.WriteNumber("status", (int)entry.Response.StatusCode);
            json.WriteString("expiresAt", entry.ExpiresAt);
            json.WriteString("dropAt", dropAt);
            if (entry.RetainUntil is { } retainUntil)
            {
                json.WriteString("retainUntil", retainUntil);
            }

            json.WriteStartObject("headers");
            foreach (var (name, values) in entry.Response.Headers)
            {
                json.WriteStartArray(name);
                foreach (var value in values)
                {
                    json.WriteStringValue(value);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        var body = entry.Response.Body.Span;
        var bytes = new byte[Magic.Length + 4 + meta.Length + body.Length];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Magic.Length), (int)meta.Length);
        meta.GetBuffer().AsSpan(0, (int)meta.Length).CopyTo(bytes.AsSpan(Magic.Length + 4));
        body.CopyTo(bytes.AsSpan(Magic.Length + 4 + (int)meta.Length));
        return bytes;
    }

    private static (string Key, DateTimeOffset DropAt, EsiCacheEntry Entry)? Read(byte[] bytes)
    {
        if (bytes.Length < Magic.Length + 4 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return null;
        }

        var metaLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(Magic.Length));
        var bodyStart = Magic.Length + 4 + metaLength;
        if (metaLength <= 0 || bodyStart > bytes.Length)
        {
            return null;
        }

        try
        {
            using var meta = JsonDocument.Parse(bytes.AsMemory(Magic.Length + 4, metaLength));
            var root = meta.RootElement;
            var headers = root.GetProperty("headers").EnumerateObject()
                .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Name, h.Value.EnumerateArray().Select(v => v.GetString()!).ToArray()))
                .ToList();
            var response = new EsiResponse((HttpStatusCode)root.GetProperty("status").GetInt32(), new EsiResponseHeaders(headers), bytes.AsMemory(bodyStart));
            var entry = new EsiCacheEntry(response, root.GetProperty("expiresAt").GetDateTimeOffset())
            {
                RetainUntil = root.TryGetProperty("retainUntil", out var retain) ? retain.GetDateTimeOffset() : null,
            };
            return (root.GetProperty("key").GetString()!, root.GetProperty("dropAt").GetDateTimeOffset(), entry);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>For tests: the file an entry is written to.</summary>
    internal string FileFor(string key) => PathFor(key);
}
