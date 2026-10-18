using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Middleware;
using NewEdenKit.ESI.Abstractions.Tests.TestSupport;

namespace NewEdenKit.ESI.Abstractions.Tests.Middleware;

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
