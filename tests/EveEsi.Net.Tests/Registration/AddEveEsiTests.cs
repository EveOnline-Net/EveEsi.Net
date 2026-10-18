using System.Net;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Registration;
using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Middleware.Caching;
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
        var services = new ServiceCollection().AddEveEsi(o => Identify(o)).Services;

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
            o => Identify(o),
            esi => esi
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
    public async Task ETagMiddleware_FromDI_TurnsANotModifiedIntoTheStoredValue()
    {
        const string status = """{"players":31234,"server_version":"2345678","start_time":"2026-10-18T11:00:05Z","vip":false}""";
        _handler.Respond(HttpStatusCode.OK, status, ("ETag", "\"abc\""), ("Cache-Control", "public, max-age=30"))
            .Respond(HttpStatusCode.NotModified, null, ("ETag", "\"abc\""));
        var service = Build(o => Identify(o), esi => esi.EnableCaching(c => c.AddInMemoryCacheStore().DisableResponseCache()))
            .GetRequiredService<IStatus>();

        var first = await service.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);
        var second = await service.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);

        _handler.Requests[1].Headers["If-None-Match"].ShouldBe("\"abc\"");
        second.IsSuccess.ShouldBeTrue($"{second.StatusCode} {second.Error?.Kind} {second.Error?.Message}");
        first.IsSuccess.ShouldBeTrue();
        second.Value.ShouldBe(first.Value);
        second.Value!.Players.ShouldBe(31234);
    }

    [Fact]
    public async Task EnableCaching_ServesAFreshResponseWithoutCallingEsiAgain()
    {
        const string status = """{"players":31234,"server_version":"2345678","start_time":"2026-10-18T11:00:05Z","vip":false}""";
        _handler.Respond(HttpStatusCode.OK, status, ("ETag", "\"abc\""), ("Cache-Control", "public, max-age=30"));
        var service = Build(o => Identify(o), esi => esi.EnableCaching(c => c.AddInMemoryCacheStore()))
            .GetRequiredService<IStatus>();

        await service.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);
        var second = await service.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);

        _handler.Requests.ShouldHaveSingleItem();
        second.IsSuccess.ShouldBeTrue();
        second.Value!.Players.ShouldBe(31234);
    }

    [Fact]
    public void UseOfT_KeepsAMiddlewareTheAppRegisteredFirst()
    {
        var own = new ResolvedMiddleware([]);
        var services = new ServiceCollection().AddSingleton(own);

        services.AddEveEsi(o => Identify(o)).Use<ResolvedMiddleware>();

        services.Where(d => d.ServiceType == typeof(ResolvedMiddleware)).ShouldHaveSingleItem().ImplementationInstance.ShouldBeSameAs(own);
    }

    [Fact]
    public void AddEveEsi_ReturnsABuilderOverTheSameServices()
    {
        var services = new ServiceCollection();

        services.AddEveEsi(o => Identify(o)).Services.ShouldBeSameAs(services);
    }

    [Fact]
    public async Task ConfigureHttpClient_AddsHandlersToTheEsiClient()
    {
        var seen = new List<string>();
        var status = Build(o => Identify(o), esi => esi.ConfigureHttpClient(http => http.AddHttpMessageHandler(() => new RecordingHandler(seen))))
            .GetRequiredService<IStatus>();

        await status.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);

        seen.ShouldHaveSingleItem().ShouldStartWith("MyApp/1.2 (admin@example.com) EveEsi.Net/", Case.Sensitive, "the handler sees the ESI client's own headers");
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var builder = new ServiceCollection().AddEveEsi(o => Identify(o));

        Should.Throw<ArgumentNullException>(() => builder.Use((IEsiMiddleware)null!));
        Should.Throw<ArgumentNullException>(() => builder.ConfigureHttpClient(null!));
        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddEveEsi(null!));
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddEveEsi(o => Identify(o)));
    }

    private static EsiOptions Identify(EsiOptions options)
    {
        options.ApplicationName = "MyApp/1.2";
        options.Contact = "admin@example.com";
        return options;
    }

    private ServiceProvider Build(Action<EsiOptions> configure, Action<IEsiBuilder>? esi = null, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        extra?.Invoke(services);
        var builder = services.AddEveEsi(configure);
        esi?.Invoke(builder);
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

    private sealed class RecordingHandler(List<string> seen) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen.Add(request.Headers.UserAgent.ToString());
            return base.SendAsync(request, cancellationToken);
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
