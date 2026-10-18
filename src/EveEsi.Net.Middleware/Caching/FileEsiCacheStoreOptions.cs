using System.Reflection;

namespace EveEsi.Net.Middleware.Caching;

/// <summary>Where and how much <see cref="FileEsiCacheStore"/> keeps.</summary>
public sealed class FileEsiCacheStoreOptions
{
    /// <summary>
    /// The folder used when <see cref="Directory"/> isn't set: <c>EveEsi.Net/&lt;app&gt;/cache</c> in the user's local
    /// application data (<c>%LOCALAPPDATA%</c> on Windows, <c>~/.local/share</c> on Linux), where <c>&lt;app&gt;</c> is the
    /// entry assembly's name. Per application, so two apps on one machine don't share entries; falls back to the
    /// temporary folder when there's no local application data.
    /// </summary>
    public static string DefaultDirectory { get; } = BuildDefaultDirectory();

    /// <summary>The folder entries are written to; created if missing. <see cref="DefaultDirectory"/> when not set.</summary>
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

    /// <summary>The folder to use: <see cref="Directory"/>, or <see cref="DefaultDirectory"/>.</summary>
    internal string ResolvedDirectory => Directory ?? DefaultDirectory;

    /// <exception cref="ArgumentException"><see cref="Directory"/> is set but empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit isn't positive.</exception>
    internal void Validate()
    {
        if (Directory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Directory, nameof(Directory));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntries, nameof(MaxEntries));
        ArgumentOutOfRangeException.ThrowIfLessThan(DefaultRetention, TimeSpan.Zero, nameof(DefaultRetention));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SweepInterval, TimeSpan.Zero, nameof(SweepInterval));
    }

    private static string BuildDefaultDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.GetTempPath();
        }

        var app = Assembly.GetEntryAssembly()?.GetName().Name ?? "default";
        return Path.Combine(root, "EveEsi.Net", app, "cache");
    }
}
