using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Tests.TestSupport;

internal static class TestRoutes
{
    public static RouteDefinition CharacterRoute(string? scope = null) =>
        new("GetCharactersCharacterId", HttpMethod.Get, "/characters/{character_id}", scope);
}
