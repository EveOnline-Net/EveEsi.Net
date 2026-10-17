using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Tests.Routing;

public sealed class RouteDefinitionTests
{
    [Fact]
    public void Constructor_AllArguments_ExposesMetadata()
    {
        var cache = new RouteCacheHints(TimeSpan.FromHours(1), RouteCacheMode.TtlBased);
        var rateLimit = new RateLimitPolicy("char-asset", 1800, TimeSpan.FromMinutes(15));

        var route = new RouteDefinition(
            "GetCharactersCharacterIdAssets", HttpMethod.Get, "/characters/{character_id}/assets",
            "esi-assets.read_assets.v1", cache, rateLimit);

        route.OperationId.ShouldBe("GetCharactersCharacterIdAssets");
        route.Method.ShouldBe(HttpMethod.Get);
        route.PathTemplate.ShouldBe("/characters/{character_id}/assets");
        route.Scope.ShouldBe("esi-assets.read_assets.v1");
        route.Cache.ShouldBe(cache);
        route.RateLimit.ShouldBe(rateLimit);
        route.RequiresAuthentication.ShouldBeTrue();
        route.ToString().ShouldBe("GET /characters/{character_id}/assets");
    }

    [Fact]
    public void Constructor_PublicRoute_HasNoScopeCacheOrRateLimit()
    {
        var route = new RouteDefinition("GetStatus", HttpMethod.Get, "/status");

        route.Scope.ShouldBeNull();
        route.Cache.ShouldBeNull();
        route.RateLimit.ShouldBeNull();
        route.RequiresAuthentication.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "/status")]
    [InlineData("GetStatus", "")]
    [InlineData("GetStatus", "status")]
    public void Constructor_InvalidOperationIdOrPath_Throws(string operationId, string path)
    {
        Should.Throw<ArgumentException>(() => new RouteDefinition(operationId, HttpMethod.Get, path));
    }

    [Fact]
    public void Constructor_BlankScope_Throws()
    {
        Should.Throw<ArgumentException>(() => new RouteDefinition("GetStatus", HttpMethod.Get, "/status", scope: " "));
    }
}
