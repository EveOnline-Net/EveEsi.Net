using NewEdenKit.ESI.Middleware.Caching;
using NewEdenKit.ESI.Middleware.Tests.TestSupport;

namespace NewEdenKit.ESI.Middleware.Tests.Caching;

public sealed class EsiCacheKeyTests
{
    [Fact]
    public void Create_SameCall_SameKey() =>
        EsiCacheKey.Create(Esi.Context(Esi.Route())).ShouldBe(EsiCacheKey.Create(Esi.Context(Esi.Route())));

    [Fact]
    public void Create_IncludesMethodPathAndQuery()
    {
        var key = EsiCacheKey.Create(Esi.Context(Esi.Route(), thingId: 1_000_000_000_000, page: 2));

        key.ShouldBe("esi:GET /things/1000000000000?page=2");
        key.ShouldNotBe(EsiCacheKey.Create(Esi.Context(Esi.Route(), thingId: 1_000_000_000_000, page: 3)));
    }

    [Fact]
    public void Create_TenantAndLanguageOverrides_ChangeTheKey()
    {
        var context = Esi.Context(Esi.Route());
        context.Request.Headers["X-Tenant"] = "singularity";
        context.Request.Headers["Accept-Language"] = "de";

        EsiCacheKey.Create(context).ShouldBe("esi:GET /things/1|tenant=singularity|lang=de");
    }

    [Fact]
    public void Create_SecuredRoute_KeysByTokenSubjectNotByToken()
    {
        var route = Esi.Route(scope: "esi-assets.read_assets.v1");
        var first = Esi.Token("CHARACTER:EVE:2112000000", id: "a");
        var refreshed = Esi.Token("CHARACTER:EVE:2112000000", id: "b");

        var key = EsiCacheKey.Create(Esi.Context(route, first));

        key.ShouldBe(EsiCacheKey.Create(Esi.Context(route, refreshed)), "a refreshed token for the same character");
        key.ShouldNotBe(EsiCacheKey.Create(Esi.Context(route, Esi.Token("CHARACTER:EVE:90000001"))));
        key.ShouldStartWith("esi:GET /things/1|sub=");
        key.ShouldNotContain(first);
        key.ShouldNotContain("2112000000");
    }

    [Fact]
    public void Create_TokenThatIsNotAJwt_IsHashedNotEmbedded()
    {
        var key = EsiCacheKey.Create(Esi.Context(Esi.Route(scope: "esi-assets.read_assets.v1"), "opaque-token"));

        key.ShouldNotContain("opaque-token");
        key.ShouldNotBe(EsiCacheKey.Create(Esi.Context(Esi.Route(scope: "esi-assets.read_assets.v1"), "other-token")));
    }

    [Fact]
    public void Create_PublicRoute_IgnoresTheToken() =>
        EsiCacheKey.Create(Esi.Context(Esi.Route(), Esi.Token("CHARACTER:EVE:1"))).ShouldBe("esi:GET /things/1");

    [Fact]
    public void Create_Null_Throws() => Should.Throw<ArgumentNullException>(() => EsiCacheKey.Create(null!));
}
