using System.Net;
using NewEdenKit.ESI.Abstractions.Caching;
using NewEdenKit.ESI.Middleware.Caching;
using NewEdenKit.ESI.Middleware.Tests.TestSupport;

namespace NewEdenKit.ESI.Middleware.Tests.Caching;

public sealed class FileEsiCacheStoreTests : IDisposable
{
    private const string Key = "esi:GET /characters/2112000000/assets?page=2|sub=3F2A";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NewEdenKit.ESI.Tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(Esi.Start);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetThenGet_RoundTripsStatusHeadersBodyAndTimes()
    {
        var store = Store();
        var response = Esi.Response(HttpStatusCode.OK, """[{"item_id":1000000000001}]""",
            ("ETag", "\"abc\""), ("Expires", Esi.HttpDate(Esi.Start.AddMinutes(5))), ("X-Pages", "3"));
        var entry = new EsiCacheEntry(response, Esi.Start.AddMinutes(5)) { RetainUntil = Esi.Start.AddHours(2) };

        await store.SetAsync(Key, entry, Ct);
        var read = (await store.GetAsync(Key, Ct)).ShouldNotBeNull();

        read.ExpiresAt.ShouldBe(entry.ExpiresAt);
        read.RetainUntil.ShouldBe(entry.RetainUntil);
        read.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Response.Body().ShouldBe("""[{"item_id":1000000000001}]""");
        read.Response.Headers.ETag.ShouldBe("\"abc\"");
        read.Response.Headers.Pages.ShouldBe(3);
        read.Response.Headers.Expires.ShouldBe(Esi.Start.AddMinutes(5));
    }

    [Fact]
    public async Task Entries_SurviveANewStoreInstance()
    {
        await Store().SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), Ct);

        (await Store().GetAsync(Key, Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task FileName_IsAHashOfTheKey()
    {
        var store = Store();

        await store.SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), Ct);

        var file = Path.GetFileName(Directory.GetFiles(_directory).ShouldHaveSingleItem());
        file.ShouldMatch("^[0-9A-F]{64}\\.esicache$");
        File.ReadAllText(Path.Combine(_directory, file)).ShouldContain(Key, Case.Sensitive, "the key is kept inside, to check on read");
    }

    [Fact]
    public async Task EntryPastRetention_IsAMissAndIsDeleted()
    {
        var store = Store(o => o.DefaultRetention = TimeSpan.FromHours(1));
        await store.SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), Ct);

        _time.Advance(TimeSpan.FromMinutes(64));
        (await store.GetAsync(Key, Ct)).ShouldNotBeNull("expired, but kept for revalidation");

        _time.Advance(TimeSpan.FromMinutes(1));
        (await store.GetAsync(Key, Ct)).ShouldBeNull();
        store.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cache file")]
    [InlineData("ESC1ÿÿÿ\u007fxx")]
    [InlineData("ESC1\u0005\u0000\u0000\u0000{oops")]
    public async Task CorruptFile_IsAMissAndIsDeleted(string content)
    {
        var store = Store();
        await store.SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), Ct);
        await File.WriteAllTextAsync(store.FileFor(Key), content, Ct);

        (await store.GetAsync(Key, Ct)).ShouldBeNull();
        File.Exists(store.FileFor(Key)).ShouldBeFalse();
    }

    [Fact]
    public async Task Remove_DeletesTheEntry()
    {
        var store = Store();
        await store.SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), Ct);

        await store.RemoveAsync(Key, Ct);

        (await store.GetAsync(Key, Ct)).ShouldBeNull();
        await store.RemoveAsync("never stored", Ct);
    }

    [Fact]
    public async Task Sweep_DeletesEntriesPastRetentionAndTrimsToTheLimit()
    {
        var store = Store(o => o.MaxEntries = 10);
        for (var i = 0; i < 15; i++)
        {
            // k0 is due soonest; k14 last.
            await store.SetAsync($"k{i}", Entry(TimeSpan.FromMinutes(1)) with { RetainUntil = Esi.Start.AddMinutes(i + 1) }, Ct);
        }

        _time.Advance(TimeSpan.FromMinutes(2)); // k0 and k1 are now past retention.
        store.Sweep();

        store.Count.ShouldBe(9);
        (await store.GetAsync("k2", Ct)).ShouldBeNull("trimmed: among the soonest due");
        (await store.GetAsync("k14", Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Writes_SweepOnTheirOwnOnceTheIntervalHasPassed()
    {
        var store = Store(o => o.SweepInterval = TimeSpan.FromMinutes(10));
        await store.SetAsync("old", Entry(TimeSpan.FromMinutes(1)) with { RetainUntil = Esi.Start.AddMinutes(1) }, Ct);

        _time.Advance(TimeSpan.FromMinutes(11));
        await store.SetAsync("new", Entry(TimeSpan.FromHours(1)), Ct);

        store.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentWritesAndReadsOfOneKey_NeverSeeAPartialEntry()
    {
        var store = Store();
        await store.SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), Ct);

        await Parallel.ForEachAsync(Enumerable.Range(0, 200), Ct, async (i, ct) =>
        {
            if (i % 2 == 0)
            {
                await store.SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), ct);
            }
            else if (await store.GetAsync(Key, ct) is { } entry)
            {
                entry.Response.Body().ShouldBe("body");
            }
        });

        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelledToken_Throws() =>
        await Should.ThrowAsync<OperationCanceledException>(async () => await Store().SetAsync(Key, Entry(TimeSpan.FromMinutes(5)), new CancellationToken(canceled: true)));

    [Fact]
    public void Constructor_CreatesTheFolder()
    {
        Store();

        Directory.Exists(_directory).ShouldBeTrue();
    }

    [Fact]
    public void EmptyDirectory_Throws() =>
        Should.Throw<ArgumentException>(() => new FileEsiCacheStore(new FileEsiCacheStoreOptions { Directory = " " }));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EsiCacheEntry Entry(TimeSpan expiresIn) => new(Esi.Response(HttpStatusCode.OK, "body"), Esi.Start + expiresIn);

    private FileEsiCacheStore Store(Action<FileEsiCacheStoreOptions>? configure = null)
    {
        var options = new FileEsiCacheStoreOptions { Directory = _directory };
        configure?.Invoke(options);
        return new FileEsiCacheStore(options, _time);
    }
}
