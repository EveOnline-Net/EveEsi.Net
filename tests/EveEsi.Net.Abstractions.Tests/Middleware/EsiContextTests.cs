using EveEsi.Net.Abstractions.Http;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Tests.TestSupport;

namespace EveEsi.Net.Abstractions.Tests.Middleware;

public sealed class EsiContextTests
{
    [Fact]
    public void Constructor_OptionalArgumentsOmitted_UsesEmptyDefaults()
    {
        var context = FakeMiddlewareTarget.NewContext();

        context.AccessToken.ShouldBeNull();
        context.Response.ShouldBeNull();
        context.Items.ShouldBeEmpty();
        context.CancellationToken.ShouldBe(CancellationToken.None);
    }

    [Fact]
    public void Constructor_NullRoute_Throws()
    {
        var request = new EsiRequestMessage(HttpMethod.Get, "/status");

        Should.Throw<ArgumentNullException>(() => new EsiContext(null!, request));
    }
}
