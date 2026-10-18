using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Abstractions.Tests.TestSupport;

internal static class TestRoutes
{
    public static RouteDefinition CharacterRoute(string? scope = null) =>
        new("GetCharactersCharacterId", HttpMethod.Get, "/characters/{character_id}", scope);
}
