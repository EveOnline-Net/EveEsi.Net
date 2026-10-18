using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Registration;
using EveEsi.Net.Routes;
using EveEsi.Net.Services;
using EveEsi.Net.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace EveEsi.Net.Tests.Registration;

public sealed class AddEveEsiTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        _providers.ForEach(p => p.Dispose());
        _handler.Dispose();
    }

    [Fact]
    public async Task AddEveEsi_SendsTheRequiredHeadersToEsi()
    {
        var status = Build(o => Identify(o)).GetRequiredService<IStatus>();

        await status.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);

        var sent = _handler.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe(new Uri("https://esi.evetech.net/status"));
        sent.Headers["User-Agent"].ShouldBe(
            $"MyApp/1.2 (admin@example.com) EveEsi.Net/{EsiUserAgent.LibraryVersion} (compatibility-date {EsiSpec.CompatibilityDate})");
        sent.Headers["X-Compatibility-Date"].ShouldBe(EsiSpec.CompatibilityDate);
        sent.Headers["X-Tenant"].ShouldBe("tranquility");
    }

    [Fact]
    public void LibraryVersion_HasNoBuildMetadata()
    {
        EsiUserAgent.LibraryVersion.ShouldNotBeNullOrWhiteSpace();
        EsiUserAgent.LibraryVersion.ShouldNotContain("+");
    }

    [Fact]
    public async Task AddEveEsi_BaseAddressAndTenant_CanBeChanged()
    {
        var status = Build(o =>
        {
            Identify(o);
            o.BaseAddress = new Uri("https://esi.test/");
            o.Tenant = "singularity";
        }).GetRequiredService<IStatus>();

        await status.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);

        var sent = _handler.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe(new Uri("https://esi.test/status"));
        sent.Headers["X-Tenant"].ShouldBe("singularity");
    }

    [Theory]
    [InlineData(null, "admin@example.com", "ApplicationName is required")]
    [InlineData(" ", "admin@example.com", "ApplicationName is required")]
    [InlineData("MyApp/1.2", null, "Contact is required")]
    [InlineData("My App", "admin@example.com", "isn't a User-Agent product token")]
    [InlineData("MyApp/1.2 (x)", "admin@example.com", "isn't a User-Agent product token")]
    [InlineData("MyApp/1.2", "admin) EvilBot/1.0 (", "can't be sent in a User-Agent comment")]
    [InlineData("MyApp/1.2", "line\nbreak", "can't be sent in a User-Agent comment")]
    public void AddEveEsi_InvalidIdentity_FailsImmediately(string? applicationName, string? contact, string message) =>
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddEveEsi(o =>
        {
            o.ApplicationName = applicationName;
            o.Contact = contact;
        })).Message.ShouldContain(message);

    [Fact]
    public void AddEveEsi_RelativeBaseAddress_FailsImmediately() =>
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddEveEsi(o =>
        {
            Identify(o);
            o.BaseAddress = new Uri("/esi", UriKind.Relative);
        })).Message.ShouldContain("must be an absolute URI");

    [Fact]
    public void AddEveEsi_EmptyTenant_FailsImmediately() =>
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddEveEsi(o =>
        {
            Identify(o);
            o.Tenant = "";
        })).Message.ShouldContain("Tenant can't be empty");

    [Fact]
    public void AddEveEsi_CalledTwice_Fails()
    {
        var services = new ServiceCollection().AddEveEsi(o => Identify(o));

        Should.Throw<InvalidOperationException>(() => services.AddEveEsi(o => Identify(o))).Message.ShouldContain("already been called");
    }

    [Fact]
    public void AddEveEsi_RegistersTransientServicesAndFactory()
    {
        var provider = Build(o => Identify(o));

        provider.GetRequiredService<IMarket>().ShouldNotBeSameAs(provider.GetRequiredService<IMarket>());
        provider.GetRequiredService<IEsiClientFactory>().ShouldNotBeSameAs(provider.GetRequiredService<IEsiClientFactory>());
    }

    [Fact]
    public async Task GlobalMiddleware_InstanceCallbackAndResolved_RunInOrderBeforeServiceMiddleware()
    {
        var order = new List<string>();
        var provider = Build(
            o => Identify(o)
                .Use(new RecordingMiddleware(order, "instance"))
                .Use(async (context, next) =>
                {
                    order.Add("callback");
                    await next(context);
                })
                .Use<ResolvedMiddleware>(),
            services => services.AddSingleton(order));

        await provider.GetRequiredService<IStatus>()
            .Use(new RecordingMiddleware(order, "service"))
            .GetStatus()
            .ExecuteAsync(TestContext.Current.CancellationToken);

        order.ShouldBe(["instance", "callback", "resolved", "service"]);
    }

    [Fact]
    public void UseOfT_KeepsAMiddlewareTheAppRegisteredFirst()
    {
        var own = new ResolvedMiddleware([]);
        var services = new ServiceCollection().AddSingleton(own);

        services.AddEveEsi(o => Identify(o).Use<ResolvedMiddleware>());

        services.Where(d => d.ServiceType == typeof(ResolvedMiddleware)).ShouldHaveSingleItem().ImplementationInstance.ShouldBeSameAs(own);
    }

    [Fact]
    public void Options_NullArguments_Throw()
    {
        Should.Throw<ArgumentNullException>(() => new EsiOptions().Use((IEsiMiddleware)null!));
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddEveEsi(null!));
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddEveEsi(o => Identify(o)));
    }

    private static EsiOptions Identify(EsiOptions options)
    {
        options.ApplicationName = "MyApp/1.2";
        options.Contact = "admin@example.com";
        return options;
    }

    private ServiceProvider Build(Action<EsiOptions> configure, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        extra?.Invoke(services);
        services.AddEveEsi(configure);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => _handler));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private sealed class RecordingMiddleware(List<string> order, string name) : IEsiMiddleware
    {
        public ValueTask InvokeAsync(EsiContext context, EsiNext next)
        {
            order.Add(name);
            return next(context);
        }
    }

    // Resolved from DI: takes the shared list as an injected dependency.
    private sealed class ResolvedMiddleware(List<string> order) : IEsiMiddleware
    {
        public ValueTask InvokeAsync(EsiContext context, EsiNext next)
        {
            order.Add("resolved");
            return next(context);
        }
    }
}
