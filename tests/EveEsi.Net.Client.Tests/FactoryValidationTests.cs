using EveEsi.Net.Abstractions.Requests;
using EveEsi.Net.Abstractions.Routing;
using EveEsi.Net.Client.Internal;
using EveEsi.Net.Client.Tests.TestSupport;

namespace EveEsi.Net.Client.Tests;

public sealed class FactoryValidationTests
{
    private readonly TestClient _client = new();

    [Fact]
    public void CreateEsiClient_UnknownOperationId_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            _client.Factory.CreateEsiClient("GetNothing", TestJsonContext.Default.CharacterInfo, null, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateEsiClient_ScopedRouteWithoutToken_Throws(string? token)
    {
        Should.Throw<ArgumentException>(() => _client.Factory.CreateEsiClient(
            TestClient.GetAssets, TestJsonContext.Default.IReadOnlyListInt64,
            p => p.Route.Set("character_id", 1L), token, null));
    }

    [Fact]
    public void CreateEsiClient_MissingRouteValue_Throws()
    {
        var ex = Should.Throw<ArgumentException>(() =>
            _client.Factory.CreateEsiClient(TestClient.GetCharacter, TestJsonContext.Default.CharacterInfo, null, null, null));

        ex.Message.ShouldContain("character_id");
    }

    [Fact]
    public void CreateEsiClient_UnknownRouteName_Throws()
    {
        Should.Throw<ArgumentException>(() => _client.Factory.CreateEsiClient(
            TestClient.GetCharacter, TestJsonContext.Default.CharacterInfo,
            p => p.Route.Set("character_id", 1L).Set("type_id", 2L), null, null));
    }

    [Fact]
    public void CreateEsiClient_ListAsRouteValue_Throws()
    {
        Should.Throw<ArgumentException>(() => _client.Factory.CreateEsiClient(
            TestClient.GetCharacter, TestJsonContext.Default.CharacterInfo,
            p => p.Route.Set<IReadOnlyList<long>>("character_id", [1, 2]), null, null));
    }

    [Fact]
    public void CreateEsiClient_PageBelowOne_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _client.Factory.CreateEsiClient(
            TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, p => p.ApplyPagination(0), null, null));
    }

    [Fact]
    public void CreateEsiClient_ParametersChangedAfterBuild_Throws()
    {
        IEsiRequestParameters? captured = null;
        _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, p => captured = p, null, null);

        Should.Throw<InvalidOperationException>(() => captured!.Query.Set("type_id", 34L));
        Should.Throw<InvalidOperationException>(() => captured!.Query.Set("search", (string?)null));
        Should.Throw<InvalidOperationException>(() => captured!.ApplyPagination(2));
        Should.Throw<InvalidOperationException>(() => captured!.Body(new RenameBody("x"), TestJsonContext.Default.RenameBody));
    }

    [Fact]
    public void RouteTable_KeyNotMatchingOperationId_Throws()
    {
        var routes = new Dictionary<string, RouteDefinition> { ["A"] = new("B", HttpMethod.Get, "/b") };

        Should.Throw<ArgumentException>(() => new EsiRouteTable(routes));
    }
}
