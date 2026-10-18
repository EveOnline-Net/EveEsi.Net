using System.Net;
using EveEsi.Net.Abstractions.Caching;
using EveEsi.Net.Middleware.Caching;
using EveEsi.Net.Middleware.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace EveEsi.Net.Middleware.Tests.Caching;

public sealed class InMemoryEsiCacheStoreTests
{
    private readonly ManualTimeProvider _time = new(Esi.Start);

    [Fact]
    public async Task SetGetRemove_RoundTrip()
    {
        var store = new InMemoryEsiCacheStore(timeProvider: _time);
        var entry = Entry(expiresIn: TimeSpan.FromMinutes(5));

        await store.SetAsync("k", entry, Ct);
        (await store.GetAsync("k", Ct)).ShouldBeSameAs(entry);

        await store.RemoveAsync("k", Ct);
        (await store.GetAsync("k", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Entry_IsDroppedAtRetainUntil()
    {
        var store = new InMemoryEsiCacheStore(timeProvider: _time);
        await store.SetAsync("k", Entry(TimeSpan.FromMinutes(5)) with { RetainUntil = Esi.Start.AddMinutes(5) }, Ct);

        _time.Advance(TimeSpan.FromMinutes(5));

        (await store.GetAsync("k", Ct)).ShouldBeNull();
        store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task EntryWithoutRetainUntil_IsKeptForTheDefaultRetentionPastExpiry()
    {
        var store = new InMemoryEsiCacheStore(new InMemoryEsiCacheStoreOptions { DefaultRetention = TimeSpan.FromHours(1) }, _time);
        await store.SetAsync("k", Entry(TimeSpan.FromMinutes(5)), Ct);

        _time.Advance(TimeSpan.FromMinutes(64));
        (await store.GetAsync("k", Ct)).ShouldNotBeNull("expired, but still there to be revalidated");

        _time.Advance(TimeSpan.FromMinutes(1));
        (await store.GetAsync("k", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task SettingAnEntryAlreadyPastRetention_RemovesTheOldOne()
    {
        var store = new InMemoryEsiCacheStore(timeProvider: _time);
        await store.SetAsync("k", Entry(TimeSpan.FromMinutes(5)), Ct);

        await store.SetAsync("k", Entry(TimeSpan.Zero) with { RetainUntil = Esi.Start }, Ct);

        store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task OverMaxEntries_DropsExpiredThenSoonestDue()
    {
        var store = new InMemoryEsiCacheStore(new InMemoryEsiCacheStoreOptions { MaxEntries = 10 }, _time);
        for (var i = 0; i < 10; i++)
        {
            // "k0" is retained shortest, "k9" longest.
            await store.SetAsync($"k{i}", Entry(TimeSpan.FromMinutes(i + 1)) with { RetainUntil = Esi.Start.AddMinutes(i + 1) }, Ct);
        }

        await store.SetAsync("new", Entry(TimeSpan.FromHours(1)) with { RetainUntil = Esi.Start.AddHours(1) }, Ct);

        store.Count.ShouldBeLessThanOrEqualTo(10);
        (await store.GetAsync("k0", Ct)).ShouldBeNull();
        (await store.GetAsync("k9", Ct)).ShouldNotBeNull();
        (await store.GetAsync("new", Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ConcurrentWrites_StayWithinTheLimit()
    {
        var store = new InMemoryEsiCacheStore(new InMemoryEsiCacheStoreOptions { MaxEntries = 100 }, _time);

        await Parallel.ForEachAsync(Enumerable.Range(0, 2_000), Ct, async (i, ct) =>
            await store.SetAsync($"k{i}", Entry(TimeSpan.FromMinutes(i % 50 + 1)), ct));

        store.Count.ShouldBeLessThanOrEqualTo(100 + Environment.ProcessorCount);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var store = new InMemoryEsiCacheStore(timeProvider: _time);

        await Should.ThrowAsync<OperationCanceledException>(async () => await store.GetAsync("k", new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, -1)]
    public void InvalidOptions_Throw(int maxEntries, int retentionMinutes) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryEsiCacheStore(
            new InMemoryEsiCacheStoreOptions { MaxEntries = maxEntries, DefaultRetention = TimeSpan.FromMinutes(retentionMinutes) }));

    [Fact]
    public void AddEsiInMemoryCacheStore_RegistersASingletonUsingTheRegisteredClock()
    {
        var services = new ServiceCollection().AddEsiInMemoryCacheStore(o => o.MaxEntries = 5);

        var descriptor = services.ShouldHaveSingleItem();
        descriptor.ServiceType.ShouldBe(typeof(IEsiCacheStore));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(TimeProvider)).Returns(_time);
        descriptor.ImplementationFactory!(provider).ShouldBeOfType<InMemoryEsiCacheStore>();
    }

    [Fact]
    public void AddEsiInMemoryCacheStore_KeepsAStoreTheAppRegisteredFirst()
    {
        var own = Substitute.For<IEsiCacheStore>();
        var services = new ServiceCollection().AddSingleton(own);

        services.AddEsiInMemoryCacheStore();

        services.ShouldHaveSingleItem().ImplementationInstance.ShouldBeSameAs(own);
    }

    [Fact]
    public void AddEsiInMemoryCacheStore_InvalidOptions_FailImmediately() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddEsiInMemoryCacheStore(o => o.MaxEntries = 0));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EsiCacheEntry Entry(TimeSpan expiresIn) => new(Esi.Response(HttpStatusCode.OK, "body"), Esi.Start + expiresIn);
}
