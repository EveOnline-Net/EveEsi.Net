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

        var route = new RouteDefinition(
            "GetMarketsRegionIdOrders", HttpMethod.Get, "/markets/{region_id}/orders",
            ["order_type", "type_id"], ["order_type"], PaginationStyle.Page, ["esi-markets.read.v1"], ["Trader"],
            cache, rateLimit);

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
        route.Body.ShouldBe(BodyRequirement.None);
        route.ToString().ShouldBe("GET /markets/{region_id}/orders");
    }

    [Fact]
    public void Constructor_OnlyRequiredArguments_UsesEmptyDefaults()
    {
        var route = new RouteDefinition("GetStatus", HttpMethod.Get, "/status");

        route.RouteParameters.ShouldBeEmpty();
        route.QueryParameters.ShouldBeEmpty();
        route.RequiredScopes.ShouldBeEmpty();
        route.Pagination.ShouldBe(PaginationStyle.None);
        route.Cache.ShouldBeNull();
        route.Body.ShouldBe(BodyRequirement.None);
    }

    [Fact]
    public void Constructor_RequiredBody_IsExposed()
    {
        var route = new RouteDefinition(
            "PostCharactersCharacterIdAssetsNames", HttpMethod.Post, "/characters/{character_id}/assets/names",
            requiredScopes: ["esi-assets.read_assets.v1"], body: BodyRequirement.Required);

        route.Body.ShouldBe(BodyRequirement.Required);
    }

    [Fact]
    public void RouteParameters_ComeFromTemplateInOrder()
    {
        var route = new RouteDefinition("Op", HttpMethod.Get, "/characters/{character_id}/contacts/{contact_id}");

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
        Should.Throw<ArgumentException>(() => new RouteDefinition("Op", HttpMethod.Get, path));
    }

    [Fact]
    public void Constructor_DuplicateQueryParameter_Throws()
    {
        Should.Throw<ArgumentException>(() => new RouteDefinition("Op", HttpMethod.Get, "/x", ["type_id", "type_id"]));
    }

    [Fact]
    public void Constructor_RequiredQueryParameterNotDeclared_Throws()
    {
        Should.Throw<ArgumentException>(() => new RouteDefinition("Op", HttpMethod.Get, "/x", ["type_id"], ["order_type"]));
    }

    [Fact]
    public void Collections_CannotBeMutatedByCasting()
    {
        var route = new RouteDefinition("Op", HttpMethod.Get, "/characters/{character_id}", ["type_id"], requiredScopes: ["s"]);

        (route.RouteParameters as ICollection<string>)!.IsReadOnly.ShouldBeTrue();
        (route.QueryParameters as ICollection<string>)!.IsReadOnly.ShouldBeTrue();
        (route.RequiredScopes as ICollection<string>)!.IsReadOnly.ShouldBeTrue();
    }
}
