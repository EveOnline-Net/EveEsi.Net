using System.Text.Json.Serialization;
using EveEsi.Net.Abstractions.Middleware;
using EveEsi.Net.Abstractions.Routing;
using EveEsi.Net.Client.Internal;

namespace EveEsi.Net.Client.Tests.TestSupport;

public sealed record CharacterInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("corporation_id")] long CorporationId);

public sealed record RenameBody([property: JsonPropertyName("name")] string Name);

[JsonSerializable(typeof(CharacterInfo))]
[JsonSerializable(typeof(RenameBody))]
[JsonSerializable(typeof(IReadOnlyList<long>))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>Builds a real factory over a <see cref="FakeHttpMessageHandler"/> and a small route table.</summary>
internal sealed class TestClient
{
    public const string GetCharacter = "GetCharactersCharacterId";
    public const string GetAssets = "GetCharactersCharacterIdAssets";
    public const string PostRename = "PostFleetsFleetIdWingsWingIdNaming";
    public const string GetStatus = "GetStatus";

    public static readonly IReadOnlyDictionary<string, RouteDefinition> Routes = new Dictionary<string, RouteDefinition>
    {
        [GetCharacter] = new(GetCharacter, HttpMethod.Get, "/characters/{character_id}"),
        [GetAssets] = new(GetAssets, HttpMethod.Get, "/characters/{character_id}/assets", scope: "esi-assets.read_assets.v1"),
        [PostRename] = new(PostRename, HttpMethod.Put, "/fleets/{fleet_id}/wings/{wing_id}", scope: "esi-fleets.write_fleet.v1"),
        [GetStatus] = new(GetStatus, HttpMethod.Get, "/status"),
    };

    public TestClient(IReadOnlyList<IEsiMiddleware>? globalMiddleware = null)
    {
        // Stands in for the service registration, which configures the typed client's HttpClient.
        var httpClient = new HttpClient(Handler, disposeHandler: false) { BaseAddress = new Uri("https://esi.evetech.net/") };
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Compatibility-Date", "2026-08-18");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Tenant", "tranquility");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "EveEsi.Net.Tests (test@example.com)");

        Factory = new EsiClientFactory(new EsiHttpClient(httpClient), new EsiRouteTable(Routes), new EsiGlobalMiddleware(globalMiddleware ?? []));
    }

    public FakeHttpMessageHandler Handler { get; } = new();

    public EsiClientFactory Factory { get; }

    public RecordedRequest SingleRequest => Handler.Requests.ShouldHaveSingleItem();
}
