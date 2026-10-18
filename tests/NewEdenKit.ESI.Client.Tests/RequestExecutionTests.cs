using System.Net;
using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Abstractions.Routing;
using NewEdenKit.ESI.Client.Tests.TestSupport;

namespace NewEdenKit.ESI.Client.Tests;

public sealed class RequestExecutionTests
{
    private const long BigId = 1_000_000_000_000;

    private readonly TestClient _client = new();

    [Fact]
    public async Task ExecuteAsync_BuildsUrlFromRouteQueryAndPagination()
    {
        _client.Handler.Respond(HttpStatusCode.OK, "[]");

        await _client.Factory.CreateEsiClient(TestClient.GetAssets, TestJsonContext.Default.IReadOnlyListInt64, p =>
        {
            p.Route.Set("character_id", BigId);
            p.Query.Set<IReadOnlyList<long>>("type_ids", [34, 35]);
            p.Query.Set("strict", true);
            p.Query.Set("search", (string?)null);
            p.ApplyPagination(2);
        }, "token", null).ExecuteAsync(TestContext.Current.CancellationToken);

        _client.SingleRequest.Uri.ShouldBe(new Uri(
            "https://esi.evetech.net/characters/1000000000000/assets?type_ids=34&type_ids=35&strict=true&page=2"));
    }

    [Fact]
    public async Task ExecuteAsync_CursorPagination_SendsOnlySetMembers()
    {
        _client.Handler.Respond(HttpStatusCode.OK, "{}");

        await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo,
            p => p.ApplyPagination(new CursorPage(After: "abc", Limit: 50)), null, null).ExecuteAsync(TestContext.Current.CancellationToken);

        _client.SingleRequest.Uri.Query.ShouldBe("?after=abc&limit=50");
    }

    [Fact]
    public async Task ExecuteAsync_SendsClientOwnedHeaders()
    {
        _client.Handler.Respond(HttpStatusCode.OK, "[]");

        await _client.Factory.CreateEsiClient(TestClient.GetAssets, TestJsonContext.Default.IReadOnlyListInt64,
            p => p.Route.Set("character_id", 1L), "access-token", null).ExecuteAsync(TestContext.Current.CancellationToken);

        var headers = _client.SingleRequest.Headers;
        headers["X-Compatibility-Date"].ShouldBe("2026-08-18");
        headers["X-Tenant"].ShouldBe("tranquility");
        headers["User-Agent"].ShouldBe("NewEdenKit.ESI.Tests (test@example.com)");
        headers["Authorization"].ShouldBe("Bearer access-token");
    }

    [Fact]
    public async Task ExecuteAsync_PublicRouteWithoutToken_SendsNoAuthorization()
    {
        _client.Handler.Respond(HttpStatusCode.OK, "{\"name\":\"x\",\"corporation_id\":1}");

        await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        _client.SingleRequest.Headers.ContainsKey("Authorization").ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Body_IsSentAsJson()
    {
        _client.Handler.Respond(HttpStatusCode.NoContent);

        await _client.Factory.CreateEsiClient(TestClient.PostRename, p =>
        {
            p.Route.Set("fleet_id", 1L).Set("wing_id", 2L);
            p.Body(new RenameBody("Alpha"), TestJsonContext.Default.RenameBody);
        }, "token", null).ExecuteAsync(TestContext.Current.CancellationToken);

        var request = _client.SingleRequest;
        request.Method.ShouldBe(HttpMethod.Put);
        request.Body.ShouldBe("{\"name\":\"Alpha\"}");
        request.ContentType.ShouldBe("application/json; charset=utf-8");
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsValueAndHeaders()
    {
        _client.Handler.Respond(HttpStatusCode.OK, $"{{\"name\":\"Pilot\",\"corporation_id\":{BigId}}}", ("X-Pages", "3"), ("ETag", "\"v1\""));

        var result = await _client.Factory.CreateEsiClient(TestClient.GetCharacter, TestJsonContext.Default.CharacterInfo,
            p => p.Route.Set("character_id", 1L), null, null).ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Value.ShouldBe(new CharacterInfo("Pilot", BigId));
        result.Headers.Pages.ShouldBe(3);
        result.Headers.ETag.ShouldBe("\"v1\"");
        result.Headers.ContentLanguage.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoContent_IsSuccess()
    {
        _client.Handler.Respond(HttpStatusCode.NoContent);

        var result = await _client.Factory.CreateEsiClient(TestClient.PostRename,
            p => p.Route.Set("fleet_id", 1L).Set("wing_id", 2L), "token", null).ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ExecuteAsync_NotModified_IsNotModifiedResult()
    {
        _client.Handler.Respond(HttpStatusCode.NotModified, headers: ("ETag", "\"v1\""));

        var result = await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsNotModified.ShouldBeTrue();
        result.Error.ShouldBeNull();
        result.Headers.ETag.ShouldBe("\"v1\"");
    }

    [Fact]
    public async Task ExecuteAsync_EsiErrorBody_IsParsedIntoError()
    {
        _client.Handler.Respond(HttpStatusCode.UnprocessableEntity,
            "{\"error\":\"Validation failed\",\"status\":422,\"details\":[{\"location\":\"path.character_id\",\"message\":\"too small\",\"value\":-1}]}");

        var result = await _client.Factory.CreateEsiClient(TestClient.GetCharacter, TestJsonContext.Default.CharacterInfo,
            p => p.Route.Set("character_id", -1L), null, null).ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        result.Error!.Kind.ShouldBe(EsiErrorKind.Http);
        result.Error.Message.ShouldBe("Validation failed");
        var detail = result.Error.Details.ShouldHaveSingleItem();
        detail.Location.ShouldBe("path.character_id");
        detail.Message.ShouldBe("too small");
        detail.Value!.Value.GetInt32().ShouldBe(-1);
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonErrorBody_KeepsRawBody()
    {
        _client.Handler.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var result = await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.Error!.Kind.ShouldBe(EsiErrorKind.Http);
        result.Error.Message.ShouldBe("ESI returned 502.");
        result.Error.RawBody.ShouldBe("<html>bad gateway</html>");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task ExecuteAsync_UnreadableSuccessBody_IsDeserializationFailure(string? body)
    {
        _client.Handler.Respond(HttpStatusCode.OK, body);

        var result = await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Error!.Kind.ShouldBe(EsiErrorKind.Deserialization);
    }

    [Fact]
    public async Task ExecuteAsync_NetworkFailure_IsTransportFailure()
    {
        var exception = new HttpRequestException("connection refused");
        _client.Handler.Throw(exception);

        var result = await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.StatusCode.ShouldBeNull();
        result.Error!.Kind.ShouldBe(EsiErrorKind.Transport);
        result.Error.Exception.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task ExecuteAsync_HttpClientTimeout_IsTransportFailure()
    {
        _client.Handler.Throw(new TaskCanceledException("timeout", new TimeoutException()));

        var result = await _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.Error!.Kind.ShouldBe(EsiErrorKind.Transport);
        result.Error.Message.ShouldContain("timed out");
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancels_Throws()
    {
        _client.Handler.Respond(HttpStatusCode.OK, "{}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null)
                .ExecuteAsync(cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_CalledTwice_SendsTwice()
    {
        _client.Handler.Respond(HttpStatusCode.OK, "{\"name\":\"a\",\"corporation_id\":1}").Respond(HttpStatusCode.OK, "{\"name\":\"b\",\"corporation_id\":1}");
        var request = _client.Factory.CreateEsiClient(TestClient.GetStatus, TestJsonContext.Default.CharacterInfo, null, null, null);

        var first = await request.ExecuteAsync(TestContext.Current.CancellationToken);
        var second = await request.ExecuteAsync(TestContext.Current.CancellationToken);

        first.Value!.Name.ShouldBe("a");
        second.Value!.Name.ShouldBe("b");
        _client.Handler.Requests.Count.ShouldBe(2);
    }
}
