using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Tests.TestSupport;

internal static class TestRoutes
{
    public static RouteDefinition CharacterRoute(params string[] scopes) =>
        new("GetCharactersCharacterId", HttpMethod.Get, "/characters/{character_id}", requiredScopes: scopes);
}
