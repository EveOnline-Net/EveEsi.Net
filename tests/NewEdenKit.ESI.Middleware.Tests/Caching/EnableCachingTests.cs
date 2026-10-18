using Microsoft.Extensions.DependencyInjection;
using NewEdenKit.ESI.Abstractions.Caching;
using NewEdenKit.ESI.Abstractions.Registration;
using NewEdenKit.ESI.Middleware.Caching;
using NewEdenKit.ESI.Middleware.Tests.TestSupport;

namespace NewEdenKit.ESI.Middleware.Tests.Caching;

public sealed class EnableCachingTests : IDisposable
{
    private readonly ServiceCollection _services = new();
    private readonly IEsiBuilder _builder = Substitute.For<IEsiBuilder>();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NewEdenKit.ESI.Tests", Guid.NewGuid().ToString("N"));

    public EnableCachingTests()
    {
        _builder.Services.Returns(_services);
        _builder.Use<CacheMiddleware>().Returns(_builder);
        _builder.Use<ETagMiddleware>().Returns(_builder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void EnableCaching_AddsTheResponseCacheThenETags()
    {
        _builder.EnableCaching(c => c.AddInMemoryCacheStore()).ShouldBeSameAs(_builder);

        Received.InOrder(() =>
        {
            _builder.Use<CacheMiddleware>();
            _builder.Use<ETagMiddleware>();
        });
    }

    [Fact]
    public void DisableETags_AddsOnlyTheResponseCache()
    {
        _builder.EnableCaching(c => c.AddInMemoryCacheStore().DisableETags());

        _builder.Received(1).Use<CacheMiddleware>();
        _builder.DidNotReceive().Use<ETagMiddleware>();
    }

    [Fact]
    public void DisableResponseCache_AddsOnlyETags()
    {
        _builder.EnableCaching(c => c.AddInMemoryCacheStore().DisableResponseCache());

        _builder.DidNotReceive().Use<CacheMiddleware>();
        _builder.Received(1).Use<ETagMiddleware>();
    }

    [Fact]
    public void DisablingBoth_Fails() =>
        Should.Throw<InvalidOperationException>(() => _builder.EnableCaching(c => c.DisableETags().DisableResponseCache()))
            .Message.ShouldContain("nothing would be cached");

    [Fact]
    public void InMemoryStore_IsTheSingletonStoreAndUsesTheRegisteredClock()
    {
        var time = new ManualTimeProvider(Esi.Start);

        _builder.EnableCaching(c => c.AddInMemoryCacheStore(o => o.MaxEntries = 5));

        Resolve(time).ShouldBeOfType<InMemoryEsiCacheStore>();
    }

    [Fact]
    public void FileStore_IsTheSingletonStoreInTheGivenFolder()
    {
        _builder.EnableCaching(c => c.AddFileCacheStore(_directory));

        Resolve().ShouldBeOfType<FileEsiCacheStore>();
        Directory.Exists(_directory).ShouldBeTrue();
    }

    [Fact]
    public void NoStoreChosen_DefaultsToAFileStore()
    {
        _builder.EnableCaching();

        var descriptor = _services.Single(d => d.ServiceType == typeof(IEsiCacheStore));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        descriptor.ImplementationFactory!.Method.Name.ShouldBe("DefaultStore", "resolving it would create the real default folder");
    }

    [Fact]
    public void DefaultDirectory_IsPerApplicationInLocalApplicationData()
    {
        var app = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "default";

        Path.IsPathRooted(FileEsiCacheStoreOptions.DefaultDirectory).ShouldBeTrue();
        FileEsiCacheStoreOptions.DefaultDirectory.ShouldEndWith(Path.Combine("NewEdenKit.ESI", app, "cache"));
    }

    [Fact]
    public void CustomStore_ThroughUseStore_IsRegistered()
    {
        var store = Substitute.For<IEsiCacheStore>();

        _builder.EnableCaching(c => c.UseStore(_ => store));

        Resolve().ShouldBeSameAs(store);
    }

    [Fact]
    public void TwoStores_Fail() =>
        Should.Throw<InvalidOperationException>(() => _builder.EnableCaching(c => c.AddInMemoryCacheStore().AddFileCacheStore(_directory)))
            .Message.ShouldContain("already chosen");

    [Fact]
    public void CalledTwice_Fails()
    {
        _builder.EnableCaching(c => c.AddInMemoryCacheStore());

        Should.Throw<InvalidOperationException>(() => _builder.EnableCaching()).Message.ShouldContain("already been called");
    }

    [Fact]
    public void AStoreTheAppRegisteredFirst_IsKept()
    {
        var own = Substitute.For<IEsiCacheStore>();
        _services.AddSingleton(own);

        _builder.EnableCaching(c => c.AddInMemoryCacheStore());

        Resolve().ShouldBeSameAs(own);
    }

    [Fact]
    public void InvalidStoreOptions_FailImmediately()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _builder.EnableCaching(c => c.AddInMemoryCacheStore(o => o.MaxEntries = 0)));
        Should.Throw<ArgumentException>(() => Substitute.For<IEsiBuilder>().EnableCaching(c => c.AddFileCacheStore(" ")));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => ((IEsiBuilder)null!).EnableCaching());
        Should.Throw<ArgumentNullException>(() => ((IEsiCacheBuilder)null!).AddInMemoryCacheStore());
        Should.Throw<ArgumentNullException>(() => ((IEsiCacheBuilder)null!).AddFileCacheStore());
        Should.Throw<ArgumentNullException>(() => _builder.EnableCaching(c => c.UseStore(null!)));
    }

    /// <summary>Resolves the registered store, with a clock when given.</summary>
    private IEsiCacheStore Resolve(TimeProvider? time = null)
    {
        var descriptor = _services.First(d => d.ServiceType == typeof(IEsiCacheStore));
        if (descriptor.ImplementationInstance is IEsiCacheStore instance)
        {
            return instance;
        }

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(TimeProvider)).Returns(time);
        return (IEsiCacheStore)descriptor.ImplementationFactory!(provider);
    }
}
