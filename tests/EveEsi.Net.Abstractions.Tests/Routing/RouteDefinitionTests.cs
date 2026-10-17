using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Results;
using EveEsi.Net.Abstractions.Routing;
using EveEsi.Net.Abstractions.Tests.TestSupport;

namespace EveEsi.Net.Abstractions.Tests.Routing;

public sealed class RouteDefinitionTests
{
    [Fact]
    public void Constructor_ValidRoute_ExposesMetadata()
    {
        var cache = new RouteCacheHints(TimeSpan.FromHours(1), RouteCacheMode.TtlBased);
        var rateLimit = new RateLimitPolicy("market", 300, TimeSpan.FromMinutes(15));

        var route = new RouteDefinition<TestDto>(
            "GetMarketsRegionIdOrders", HttpMethod.Get, "/markets/{region_id}/orders",
            ["order_type", "type_id"], ["order_type"], PaginationStyle.Page, ["esi-markets.read.v1"], ["Trader"],
            cache, rateLimit, bodyTypeInfo: null, isBodyRequired: false, TestJsonContext.Default.TestDto);

        route.OperationId.ShouldBe("GetMarketsRegionIdOrders");
        route.Method.ShouldBe(HttpMethod.Get);
        route.PathTemplate.ShouldBe("/markets/{region_id}/orders");
        route.RouteParameters.ShouldBe(["region_id"]);
        route.QueryParameters.ShouldBe(["order_type", "type_id"]);
        route.RequiredQueryParameters.ShouldBe(["order_type"]);
        route.Pagination.ShouldBe(PaginationStyle.Page);
        route.RequiredScopes.ShouldBe(["esi-markets.read.v1"]);
        route.RequiredRoles.ShouldBe(["Trader"]);
        route.Cache.ShouldBe(cache);
        route.RateLimit.ShouldBe(rateLimit);
        route.ResponseType.ShouldBe(typeof(TestDto));
        route.ResponseTypeInfo.ShouldBeSameAs(TestJsonContext.Default.TestDto);
        route.ToString().ShouldBe("GET /markets/{region_id}/orders");
    }

    [Fact]
    public void RouteParameters_ComeFromTemplateInOrder()
    {
        var route = Create("/characters/{character_id}/contacts/{contact_id}");

        route.RouteParameters.ShouldBe(["character_id", "contact_id"]);
    }

    [Theory]
    [InlineData]
    [InlineData("esi-characters.read_contacts.v1")]
    public void RequiresAuthentication_FollowsScopes(params string[] scopes)
    {
        TestRoutes.CharacterRoute(scopes).RequiresAuthentication.ShouldBe(scopes.Length > 0);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("/characters/{character_id}/{character_id}")]
    [InlineData("/characters/{}")]
    public void Constructor_InvalidTemplate_Throws(string path)
    {
        Should.Throw<ArgumentException>(() => Create(path));
    }

    [Fact]
    public void Constructor_DuplicateQueryParameter_Throws()
    {
        Should.Throw<ArgumentException>(() => Create("/x", query: ["type_id", "type_id"]));
    }

    [Fact]
    public void Constructor_RequiredQueryParameterNotDeclared_Throws()
    {
        Should.Throw<ArgumentException>(() => Create("/x", query: ["type_id"], required: ["order_type"]));
    }

    [Fact]
    public void Constructor_RequiredBodyWithoutBodyType_Throws()
    {
        Should.Throw<ArgumentException>(() => new RouteDefinition<TestDto>(
            "Op", HttpMethod.Post, "/ui/autopilot", null, null, PaginationStyle.None, null, null, null, null,
            bodyTypeInfo: null, isBodyRequired: true, TestJsonContext.Default.TestDto));
    }

    [Fact]
    public void Constructor_NoContentWithoutResponseTypeInfo_Succeeds()
    {
        var route = new RouteDefinition<EsiNoContent>(
            "PostUiAutopilotWaypoint", HttpMethod.Post, "/ui/autopilot/waypoint", null, null, PaginationStyle.None,
            ["esi-ui.write_waypoint.v1"], null, null, null, TestJsonContext.Default.TestBody, true, responseTypeInfo: null);

        route.ResponseTypeInfo.ShouldBeNull();
        route.BodyTypeInfo.ShouldBeSameAs(TestJsonContext.Default.TestBody);
        route.IsBodyRequired.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_NoContentWithResponseTypeInfo_Throws()
    {
        // Any non-null JsonTypeInfo<EsiNoContent> is wrong; build one only to prove the check.
        var info = JsonTypeInfo.CreateJsonTypeInfo<EsiNoContent>(TestJsonContext.Default.Options);

        Should.Throw<ArgumentException>(() => new RouteDefinition<EsiNoContent>(
            "Op", HttpMethod.Delete, "/x", null, null, PaginationStyle.None, null, null, null, null, null, false, info));
    }

    [Fact]
    public void Constructor_BodyResponseWithoutResponseTypeInfo_Throws()
    {
        Should.Throw<ArgumentException>(() => new RouteDefinition<TestDto>(
            "Op", HttpMethod.Get, "/x", null, null, PaginationStyle.None, null, null, null, null, null, false, responseTypeInfo: null));
    }

    [Fact]
    public void Collections_CannotBeMutatedByCasting()
    {
        var route = Create("/characters/{character_id}", query: ["type_id"]);

        (route.RouteParameters as ICollection<string>)!.IsReadOnly.ShouldBeTrue();
        (route.QueryParameters as ICollection<string>)!.IsReadOnly.ShouldBeTrue();
        (route.RequiredScopes as ICollection<string>)!.IsReadOnly.ShouldBeTrue();
    }

    private static RouteDefinition<TestDto> Create(string path, string[]? query = null, string[]? required = null) => new(
        "Op", HttpMethod.Get, path, query, required, PaginationStyle.None, null, null, null, null, null, false,
        TestJsonContext.Default.TestDto);
}
