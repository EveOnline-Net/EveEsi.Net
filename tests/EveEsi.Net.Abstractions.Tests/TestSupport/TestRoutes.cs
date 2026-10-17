using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Tests.TestSupport;

internal static class TestRoutes
{
    public static RouteDefinition<TestDto> CharacterRoute(params string[] scopes) => new(
        "GetCharactersCharacterId",
        HttpMethod.Get,
        "/characters/{character_id}",
        queryParameters: null,
        requiredQueryParameters: null,
        PaginationStyle.None,
        scopes,
        requiredRoles: null,
        cache: null,
        rateLimit: null,
        bodyTypeInfo: null,
        isBodyRequired: false,
        TestJsonContext.Default.TestDto);
}
