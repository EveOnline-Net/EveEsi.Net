using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Abstractions.Routing;
using EveEsi.Net.Client.Internal;
using EveEsi.Net.Models.Market;
using EveEsi.Net.Models.Search;
using EveEsi.Net.Routes;
using EveEsi.Net.Services;
using EveEsi.Net.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace EveEsi.Net.Tests.Services;

/// <summary>
/// The generated services, driven through the real factory, route table and HTTP client: every method must build a
/// valid request for its own operation, and the parameter shapes (enums, lists, bodies, pagination) must reach the wire.
/// </summary>
public sealed partial class GeneratedServiceTests : IDisposable
{
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly ServiceProvider _provider;

    public GeneratedServiceTests()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("https://esi.test/") };
        var factory = new EsiClientFactory(new EsiHttpClient(http), new EsiRouteTable(EsiRoutes.All), EsiGlobalMiddleware.None);
        var services = new ServiceCollection().AddSingleton<IEsiClientFactory>(factory);
        EsiServices.AddGeneratedServices(services);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _handler.Dispose();
    }

    public static TheoryData<string, string> Methods()
    {
        var data = new TheoryData<string, string>();
        foreach (var service in ServiceInterfaces())
        {
            foreach (var method in service.GetMethods().Where(m => m.DeclaringType == service))
            {
                data.Add(service.Name, method.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task EveryMethod_SendsItsOwnOperationWithAllRouteValues(string serviceName, string methodName)
    {
        var service = ServiceInterfaces().Single(s => s.Name == serviceName);
        var method = service.GetMethod(methodName)!;

        var request = method.Invoke(_provider.GetRequiredService(service), [.. method.GetParameters().Select(Argument)])!;
        var route = (RouteDefinition)request.GetType().GetProperty(nameof(IEsiRequest<object>.Route))!.GetValue(request)!;
        await ((Task)request.GetType().GetMethod(nameof(IEsiRequest<object>.ExecuteAsync))!
            .Invoke(request, [TestContext.Current.CancellationToken])!);

        // Named like its Operation constant, and that constant is the operation it calls.
        var constant = typeof(Operation).GetNestedType(serviceName[1..])!.GetField(methodName)!;
        route.OperationId.ShouldBe((string)constant.GetValue(null)!);

        var sent = _handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(route.Method);
        var path = sent.PathAndQuery.Split('?')[0];
        var pattern = "^" + Placeholder().Replace(Regex.Escape(route.PathTemplate).Replace("\\{", "{", StringComparison.Ordinal), "[^/]+") + "$";
        path.ShouldMatch(pattern);
        sent.Authorization.ShouldBe(route.RequiresAuthentication ? "Bearer token" : null);
    }

    [Fact]
    public async Task QueryValues_EnumsGoThroughTheirConverterAndNullsAreLeftOut()
    {
        await _provider.GetRequiredService<IMarket>()
            .GetMarketsRegionIdOrders(10_000_002, GetMarketsRegionIdOrdersOrderType.Sell, typeId: 1_000_000_000_000, page: 2)
            .ExecuteAsync(TestContext.Current.CancellationToken);
        await _provider.GetRequiredService<IMarket>()
            .GetMarketsRegionIdOrders(10_000_002, GetMarketsRegionIdOrdersOrderType.All)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        _handler.Requests.Select(r => r.PathAndQuery).ShouldBe(
        [
            "/markets/10000002/orders?order_type=sell&type_id=1000000000000&page=2",
            "/markets/10000002/orders?order_type=all",
        ]);
    }

    [Fact]
    public async Task EnumList_SendsOnePairPerItem()
    {
        await _provider.GetRequiredService<ISearch>()
            .GetCharactersCharacterIdSearch(
                2_112_000_000, [GetCharactersCharacterIdSearchCategoriesItem.Agent, GetCharactersCharacterIdSearchCategoriesItem.SolarSystem],
                "Jita", "token", strict: true)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        _handler.Requests.ShouldHaveSingleItem().PathAndQuery
            .ShouldBe("/characters/2112000000/search?categories=agent&categories=solar_system&search=Jita&strict=true");
    }

    [Fact]
    public async Task Body_IsSerializedWithTheTokenAndQueryValues()
    {
        await _provider.GetRequiredService<IContacts>()
            .PutCharactersCharacterIdContacts(90_000_001, 5.5, [1_000_000_000_001, 2], "token", labelIds: [7])
            .ExecuteAsync(TestContext.Current.CancellationToken);

        var sent = _handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Put);
        sent.PathAndQuery.ShouldBe("/characters/90000001/contacts?standing=5.5&label_ids=7");
        sent.Body.ShouldBe("[1000000000001,2]");
        sent.Authorization.ShouldBe("Bearer token");
    }

    [Fact]
    public async Task Cursor_SendsBeforeAfterAndLimit()
    {
        await _provider.GetRequiredService<IFreelanceJobs>()
            .GetFreelanceJobsListing(cursor: new CursorPage(null, "abc", 25))
            .ExecuteAsync(TestContext.Current.CancellationToken);

        _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe("/freelance-jobs?after=abc&limit=25");
    }

    [Fact]
    public void RequiredReferenceParameter_Null_Throws() =>
        Should.Throw<ArgumentNullException>(() => _provider.GetRequiredService<IKillmails>().GetKillmailsKillmailIdKillmailHash(1, null!))
            .ParamName.ShouldBe("killmailHash");

    [Fact]
    public void SecuredMethod_EmptyToken_Throws() =>
        Should.Throw<ArgumentException>(() => _provider.GetRequiredService<IMarket>().GetCharactersCharacterIdOrders(1, " "))
            .ParamName.ShouldBe("token");

    [Fact]
    public async Task Use_InstanceAndCallback_ReturnTheServiceAndRunInOrder()
    {
        var order = new List<string>();

        IStatus status = _provider.GetRequiredService<IStatus>()
            .Use(new RecordingMiddleware(order))
            .Use(async (context, next) =>
            {
                order.Add("callback");
                await next(context);
            });
        await status.GetStatus().ExecuteAsync(TestContext.Current.CancellationToken);

        order.ShouldBe(["instance", "callback"]);
    }

    [Fact]
    public void Registration_EveryServiceResolvesAsTransient()
    {
        var services = ServiceInterfaces();

        services.Count.ShouldBeGreaterThan(30, "the generated services weren't found");
        foreach (var service in services)
        {
            var first = _provider.GetRequiredService(service);
            first.ShouldNotBeSameAs(_provider.GetRequiredService(service), service.Name);
        }
    }

    [Fact]
    public void Registration_KeepsAServiceTheAppRegisteredFirst()
    {
        var own = Substitute.For<IStatus>();
        var services = new ServiceCollection().AddSingleton(own);

        EsiServices.AddGeneratedServices(services);

        services.Where(d => d.ServiceType == typeof(IStatus)).ShouldHaveSingleItem().ImplementationInstance.ShouldBeSameAs(own);
    }

    /// <summary>Every generated service interface: <c>I&lt;Tag&gt;</c> deriving <c>IEsiService&lt;I&lt;Tag&gt;&gt;</c>.</summary>
    private static List<Type> ServiceInterfaces() =>
        [.. typeof(EsiServices).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface && t.GetInterfaces().Contains(typeof(IEsiService<>).MakeGenericType(t)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>A valid value for a parameter: null when optional, otherwise one per wire type.</summary>
    private static object? Argument(ParameterInfo parameter) =>
        parameter.IsOptional ? null : parameter.Name == "token" ? "token" : Value(parameter.ParameterType);

    private static object Value(Type type)
    {
        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(1)!; // The first member after Unknown.
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var item = type.GetGenericArguments()[0];
            var array = Array.CreateInstance(item, 1);
            array.SetValue(Value(item), 0);
            return array;
        }

        return type switch
        {
            _ when type == typeof(long) => 1_000_000_000_000L,
            _ when type == typeof(int) => 1,
            _ when type == typeof(double) => 1.5,
            _ when type == typeof(bool) => true,
            _ when type == typeof(string) => "value",
            _ when type == typeof(Guid) => Guid.Parse("8c3f5f3e-5a5e-4c1b-9f0e-6f1d2a3b4c5d"),
            _ when type.IsClass => Body(type),
            _ => throw new NotSupportedException($"No test value for {type}."),
        };
    }

    /// <summary>A request body with every required member set; optional ones stay null.</summary>
    private static object Body(Type type)
    {
        var body = RuntimeHelpers.GetUninitializedObject(type);
        foreach (var property in type.GetProperties().Where(p => p.IsDefined(typeof(RequiredMemberAttribute), inherit: false)))
        {
            property.SetValue(body, Value(property.PropertyType));
        }

        return body;
    }

    private sealed class RecordingMiddleware(List<string> order) : IEsiMiddleware
    {
        public ValueTask InvokeAsync(EsiContext context, EsiNext next)
        {
            order.Add("instance");
            return next(context);
        }
    }

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();
}
